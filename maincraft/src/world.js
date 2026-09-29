'use strict';
// World storage, worker pool, lighting engine, block updates (main thread).

const COL_GRID = 64, COL_MASK = 63;
const colKey = (cx, cz) => (cx + 32768) * 65536 + (cz + 32768);
const secIdx = (x, ly, z) => (ly << 8) | (z << 4) | x;

class Section {
  constructor(skyFill) {
    this.ids = new Uint16Array(4096);
    this.meta = new Uint8Array(4096);
    this.light = new Uint8Array(4096);
    if (skyFill) this.light.fill(0xF0);
  }
}

class Column {
  constructor(cx, cz) {
    this.cx = cx; this.cz = cz; this.key = colKey(cx, cz);
    this.secs = new Array(SECTIONS).fill(null);
    this.state = 0;          // 0 requested, 1 have blocks + sky init, 2 fully lit
    this.dirty = 0;          // bitmask of sections that need a remesh
    this.meshVer = new Uint32Array(SECTIONS);
    this.meshBusy = 0;       // bitmask of sections with a mesh job in flight
    this.meshed = 0;         // bitmask of sections that have (possibly empty) meshes
    this.meshes = new Array(SECTIONS).fill(null);
    this.edits = null;       // Map localIndex -> id | meta<<16 (player modifications)
    this.biomes = null;
    this.emitters = null;
    this.hm = new Int16Array(256); // lowest y (exclusive) that still has full sky light
  }
}

class WorkerPool {
  constructor(n, src, onMsg) {
    const url = URL.createObjectURL(new Blob([src], { type: 'text/javascript' }));
    this.workers = [];
    this.busy = [];
    for (let i = 0; i < n; i++) {
      const w = new Worker(url);
      w.onmessage = (e) => { this.busy[i]--; onMsg(e.data, i); };
      w.onerror = (e) => console.error('worker error', e.message || e);
      this.workers.push(w); this.busy.push(0);
    }
  }
  post(msg, tr) {
    let best = 0;
    for (let i = 1; i < this.workers.length; i++) if (this.busy[i] < this.busy[best]) best = i;
    this.busy[best]++;
    this.workers[best].postMessage(msg, tr || []);
    return best;
  }
  broadcast(msg) { for (let i = 0; i < this.workers.length; i++) { this.busy[i]++; this.workers[i].postMessage(msg); } }
  load() { let s = 0; for (const b of this.busy) s += b; return s; }
  terminate() { for (const w of this.workers) w.terminate(); }
}

class World {
  constructor(seed, workerSrc, layers, opts) {
    this.seed = seed;
    this.cols = new Map();
    this.grid = new Array(COL_GRID * COL_GRID).fill(null);
    this.savedEdits = new Map();   // colKey -> Map (edits of unloaded / not yet loaded columns)
    this.dirtyCols = new Set();    // columns with unsaved edits
    this.pendingLit = new Set();   // columns waiting for the horizontal light pass
    this.opts = opts;
    this.genInFlight = 0; this.meshInFlight = 0;
    this.meshPool = [];            // recycled padded arrays
    this.onMeshResult = null;
    this.onColumnUnload = null;
    this.ready = false;
    const nw = Math.max(1, Math.min(opts.workers || 4, 8));
    this.pool = new WorkerPool(nw, workerSrc, (d) => this.onWorker(d));
    this.readyCount = 0;
    this.pool.broadcast({ t: 'init', seed, layers });
    // light queues (x,y,z,v)
    this.lq = new Int32Array(1 << 20); this.lqh = 0; this.lqt = 0;
    this.rq = new Int32Array(1 << 20); this.rqh = 0; this.rqt = 0;
    // scheduled block updates: Map key -> due tick
    this.sched = new Map();
    this.tick = 0;
    this.stats = { gen: 0, mesh: 0, lit: 0 };
  }

  // ------------------------------------------------------------------ access
  col(cx, cz) {
    const c = this.grid[((cx & COL_MASK) << 6) | (cz & COL_MASK)];
    return c && c.cx === cx && c.cz === cz ? c : null;
  }
  getBlock(x, y, z) {
    if (y < WORLD_MIN_Y) return B.BEDROCK;
    if (y >= WORLD_MAX_Y) return 0;
    const c = this.col(x >> 4, z >> 4);
    if (!c || !c.state) return 0;
    const s = c.secs[(y - WORLD_MIN_Y) >> 4];
    return s ? s.ids[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] : 0;
  }
  getMeta(x, y, z) {
    if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return 0;
    const c = this.col(x >> 4, z >> 4);
    if (!c || !c.state) return 0;
    const s = c.secs[(y - WORLD_MIN_Y) >> 4];
    return s ? s.meta[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] : 0;
  }
  getLight(x, y, z) {
    if (y >= WORLD_MAX_Y) return 0xF0;
    if (y < WORLD_MIN_Y) return 0;
    const c = this.col(x >> 4, z >> 4);
    if (!c || !c.state) return 0xF0;
    const s = c.secs[(y - WORLD_MIN_Y) >> 4];
    return s ? s.light[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] : 0xF0;
  }
  isLoaded(x, z) { const c = this.col(x >> 4, z >> 4); return !!(c && c.state); }
  biomeAt(x, z) { const c = this.col(x >> 4, z >> 4); return c && c.biomes ? c.biomes[((z & 15) << 4) | (x & 15)] : 0; }

  // ------------------------------------------------------------------ worker messages
  onWorker(d) {
    if (d.t === 'ready') { if (++this.readyCount === this.pool.workers.length) this.ready = true; return; }
    if (d.t === 'gen') { this.genInFlight--; this.onGenerated(d); return; }
    if (d.t === 'mesh') {
      this.meshInFlight--;
      this.meshPool.push({ ids: d.ids, meta: d.meta, light: d.light });
      const sy = d.sy;
      const c = this.col(d.cx, d.cz);
      if (!c) return;
      c.meshBusy &= ~(1 << sy);
      if (d.ver !== c.meshVer[sy]) { c.dirty |= 1 << sy; }
      c.meshed |= 1 << sy;
      if (this.onMeshResult) this.onMeshResult(c, sy, d);
    }
  }

  requestColumn(cx, cz) {
    const k = colKey(cx, cz);
    if (this.cols.has(k)) return;
    const slot = ((cx & COL_MASK) << 6) | (cz & COL_MASK);
    const old = this.grid[slot];
    if (old) this.unloadColumn(old);
    const c = new Column(cx, cz);
    this.cols.set(k, c);
    this.grid[slot] = c;
    this.genInFlight++;
    this.pool.post({ t: 'gen', cx, cz });
  }

  onGenerated(d) {
    const c = this.col(d.cx, d.cz);
    if (!c || c.state) return;
    for (let s = 0; s < SECTIONS; s++) {
      const src = d.sections[s];
      if (!src) continue;
      const sec = new Section(false);
      sec.ids = src.ids; sec.meta = src.meta;
      c.secs[s] = sec;
    }
    // allocate air sections below the top so the light engine can store values
    let top = -1;
    for (let s = SECTIONS - 1; s >= 0; s--) if (c.secs[s]) { top = s; break; }
    for (let s = 0; s < top; s++) if (!c.secs[s]) c.secs[s] = new Section(false);
    c.biomes = d.biomes;
    const saved = this.savedEdits.get(c.key);
    if (saved) { c.edits = saved; this.applyEdits(c); }
    this.skyInit(c);
    c.state = 1;
    this.stats.gen++;
    // this column and its neighbours may now be lightable
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
      const n = this.col(c.cx + dx, c.cz + dz);
      if (n && n.state === 1) this.pendingLit.add(n);
    }
  }

  applyEdits(c) {
    for (const [idx, v] of c.edits) {
      const y = (idx >> 8) + WORLD_MIN_Y, s = (y - WORLD_MIN_Y) >> 4;
      let sec = c.secs[s];
      if (!sec) {
        for (let k = 0; k <= s; k++) if (!c.secs[k]) c.secs[k] = new Section(false);
        sec = c.secs[s];
      }
      const li = ((y & 15) << 8) | (idx & 255);
      sec.ids[li] = v & 0xFFFF; sec.meta[li] = (v >>> 16) & 255;
    }
  }

  unloadColumn(c) {
    if (c.edits && c.edits.size) this.savedEdits.set(c.key, c.edits);
    if (this.onColumnUnload) this.onColumnUnload(c);
    this.cols.delete(c.key);
    const slot = ((c.cx & COL_MASK) << 6) | (c.cz & COL_MASK);
    if (this.grid[slot] === c) this.grid[slot] = null;
    this.pendingLit.delete(c);
    c.state = -1;
  }

  // ------------------------------------------------------------------ lighting
  // vertical sky light fill for one column + emitters
  skyInit(c) {
    const secs = c.secs;
    let top = -1;
    for (let s = SECTIONS - 1; s >= 0; s--) if (secs[s]) { top = s; break; }
    const em = [];
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      let sky = 15, hm = WORLD_MIN_Y + (top + 1) * 16;
      const col = (z << 4) | x;
      for (let s = top; s >= 0; s--) {
        const sec = secs[s];
        const ids = sec.ids, L = sec.light;
        for (let ly = 15; ly >= 0; ly--) {
          const i = (ly << 8) | col, id = ids[i];
          if (sky > 0) {
            const cost = LCOST[id];
            if (cost >= 15) sky = 0;
            else if (sky < 15 || VDIM[id]) sky = Math.max(0, sky - cost);
            if (sky === 15) hm = WORLD_MIN_Y + s * 16 + ly;
          }
          const e = EMIT[id];
          L[i] = (sky << 4) | e;
          if (e) em.push(c.cx * 16 + x, WORLD_MIN_Y + s * 16 + ly, c.cz * 16 + z);
        }
      }
      c.hm[col] = hm;
    }
    c.emitters = em;
  }

  // horizontal propagation pass; requires the 8 neighbours to have sky init
  litPass(c) {
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
      const n = this.col(c.cx + dx, c.cz + dz);
      if (!n || n.state < 1) return false;
    }
    const x0 = c.cx * 16, z0 = c.cz * 16;
    this.lqh = this.lqt = 0;
    // sky seeds: cells in the band between own heightmap and neighbours' heightmaps (incl. ring)
    for (let z = -1; z <= 16; z++) for (let x = -1; x <= 16; x++) {
      const wx = x0 + x, wz = z0 + z;
      const cc = this.col(wx >> 4, wz >> 4);
      const hm = cc.hm[((wz & 15) << 4) | (wx & 15)];
      let top = hm;
      for (let k = 0; k < 4; k++) {
        const nx = wx + (k === 0 ? 1 : k === 1 ? -1 : 0), nz = wz + (k === 2 ? 1 : k === 3 ? -1 : 0);
        const nc = this.col(nx >> 4, nz >> 4);
        if (!nc || nc.state < 1) continue;
        const h = nc.hm[((nz & 15) << 4) | (nx & 15)];
        if (h > top) top = h;
      }
      // scan down from top until sky reaches 0 in this column
      for (let y = top; y >= WORLD_MIN_Y; y--) {
        const l = this.getLight(wx, y, wz), s = l >> 4;
        if (y < hm && s <= 1) break;
        if (s > 1) this.pushL(wx, y, wz, s);
      }
    }
    this.propagateAdd(true);
    this.lqh = this.lqt = 0;
    const em = c.emitters || [];
    for (let i = 0; i < em.length; i += 3) this.pushL(em[i], em[i + 1], em[i + 2], this.getLight(em[i], em[i + 1], em[i + 2]) & 15);
    // emitters of neighbours that were lit before this column arrived are already spread into it
    this.propagateAdd(false);
    c.state = 2;
    this.stats.lit++;
    return true;
  }

  pushL(x, y, z, v) {
    let t = this.lqt;
    if (t + 4 > this.lq.length) {
      if (this.lqh > 0) { this.lq.copyWithin(0, this.lqh, t); t -= this.lqh; this.lqh = 0; }
      if (t + 4 > this.lq.length) { const n = new Int32Array(this.lq.length * 2); n.set(this.lq); this.lq = n; }
    }
    const q = this.lq; q[t] = x; q[t + 1] = y; q[t + 2] = z; q[t + 3] = v; this.lqt = t + 4;
  }
  pushR(x, y, z, v) {
    let t = this.rqt;
    if (t + 4 > this.rq.length) {
      if (this.rqh > 0) { this.rq.copyWithin(0, this.rqh, t); t -= this.rqh; this.rqh = 0; }
      if (t + 4 > this.rq.length) { const n = new Int32Array(this.rq.length * 2); n.set(this.rq); this.rq = n; }
    }
    const q = this.rq; q[t] = x; q[t + 1] = y; q[t + 2] = z; q[t + 3] = v; this.rqt = t + 4;
  }

  // raw light set; allocates sections when needed; marks meshes dirty
  setLightRaw(c, x, y, z, v) {
    const s = (y - WORLD_MIN_Y) >> 4;
    let sec = c.secs[s];
    if (!sec) {
      if (v === 0xF0) return;
      for (let k = 0; k <= s; k++) if (!c.secs[k]) c.secs[k] = new Section(true);
      sec = c.secs[s];
    }
    sec.light[((y & 15) << 8) | ((z & 15) << 4) | (x & 15)] = v;
    if (c.state === 2 || c.meshed) this.markDirtyAt(x, y, z);
  }

  propagateAdd(sky) {
    const DX = [1, -1, 0, 0, 0, 0], DY = [0, 0, 1, -1, 0, 0], DZ = [0, 0, 0, 0, 1, -1];
    while (this.lqh < this.lqt) {
      const q = this.lq, h = this.lqh;
      const x = q[h], y = q[h + 1], z = q[h + 2];
      this.lqh = h + 4;
      const cur = sky ? this.getLight(x, y, z) >> 4 : this.getLight(x, y, z) & 15;
      if (cur <= 1) continue;
      for (let d = 0; d < 6; d++) {
        const nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
        if (ny < WORLD_MIN_Y || ny >= WORLD_MAX_Y) continue;
        const c = this.col(nx >> 4, nz >> 4);
        if (!c || c.state < 1) continue;
        const sec = c.secs[(ny - WORLD_MIN_Y) >> 4];
        const li = ((ny & 15) << 8) | ((nz & 15) << 4) | (nx & 15);
        const id = sec ? sec.ids[li] : 0;
        const cost = LCOST[id];
        if (cost >= 15) continue;
        const old = sec ? sec.light[li] : 0xF0;
        let nv;
        if (sky) {
          nv = (d === 3 && cur === 15 && !VDIM[id]) ? 15 : cur - cost;
          if (nv > (old >> 4)) { this.setLightRaw(c, nx, ny, nz, (nv << 4) | (old & 15)); if (nv > 1) this.pushL(nx, ny, nz, nv); }
        } else {
          nv = cur - cost;
          if (nv > (old & 15)) { this.setLightRaw(c, nx, ny, nz, (old & 0xF0) | nv); if (nv > 1) this.pushL(nx, ny, nz, nv); }
        }
      }
    }
    this.lqh = this.lqt = 0;
  }

  // removal pass starting from the cells in rq (value = old light); relight sources go to lq
  propagateRemove(sky) {
    const DX = [1, -1, 0, 0, 0, 0], DY = [0, 0, 1, -1, 0, 0], DZ = [0, 0, 0, 0, 1, -1];
    while (this.rqh < this.rqt) {
      const q = this.rq, h = this.rqh;
      const x = q[h], y = q[h + 1], z = q[h + 2], v = q[h + 3];
      this.rqh = h + 4;
      for (let d = 0; d < 6; d++) {
        const nx = x + DX[d], ny = y + DY[d], nz = z + DZ[d];
        if (ny < WORLD_MIN_Y || ny >= WORLD_MAX_Y) continue;
        const c = this.col(nx >> 4, nz >> 4);
        if (!c || c.state < 1) continue;
        const sec = c.secs[(ny - WORLD_MIN_Y) >> 4];
        const li = ((ny & 15) << 8) | ((nz & 15) << 4) | (nx & 15);
        const old = sec ? sec.light[li] : 0xF0;
        const nv = sky ? old >> 4 : old & 15;
        if (nv === 0) continue;
        const id = sec ? sec.ids[li] : 0;
        if (nv < v || (sky && d === 3 && v === 15 && nv === 15)) {
          if (!sky && EMIT[id]) { this.pushL(nx, ny, nz, nv); continue; }
          this.setLightRaw(c, nx, ny, nz, sky ? (old & 15) : (old & 0xF0));
          this.pushR(nx, ny, nz, nv);
          if (!sky && EMIT[id]) this.pushL(nx, ny, nz, EMIT[id]);
        } else {
          this.pushL(nx, ny, nz, nv);
        }
      }
    }
    this.rqh = this.rqt = 0;
  }

  // relight after a block change at x,y,z (old/new ids known)
  relightAt(x, y, z, oldId, newId) {
    const c = this.col(x >> 4, z >> 4);
    if (!c || c.state < 1) return;
    const lt = this.getLight(x, y, z);
    const oldSky = lt >> 4, oldBlk = lt & 15;
    const newCost = LCOST[newId];
    // ---- block light
    this.lqh = this.lqt = 0; this.rqh = this.rqt = 0;
    if (oldBlk > 0) {
      this.setLightRaw(c, x, y, z, lt & 0xF0);
      this.pushR(x, y, z, oldBlk);
      this.propagateRemove(false);
    }
    const e = EMIT[newId];
    if (e) {
      const cur = this.getLight(x, y, z);
      this.setLightRaw(c, x, y, z, (cur & 0xF0) | e);
      this.pushL(x, y, z, e);
    }
    if (newCost < 15) this.pushNeighbours(x, y, z, false);
    this.propagateAdd(false);
    // ---- sky light
    this.lqh = this.lqt = 0; this.rqh = this.rqt = 0;
    if (oldSky > 0 && (newCost > LCOST[oldId] || VDIM[newId] > VDIM[oldId])) {
      const cur = this.getLight(x, y, z);
      this.setLightRaw(c, x, y, z, cur & 15);
      this.pushR(x, y, z, oldSky);
      this.propagateRemove(true);
    }
    if (newCost < 15) this.pushNeighbours(x, y, z, true);
    this.propagateAdd(true);
    // heightmap maintenance (approximate: only raises/lowers at this column)
    const hmi = ((z & 15) << 4) | (x & 15);
    const shades = newCost > 1 || VDIM[newId];
    if (shades && y >= c.hm[hmi]) c.hm[hmi] = y + 1;
    else if (!shades && y + 1 === c.hm[hmi]) {
      let yy = y;
      while (yy > WORLD_MIN_Y && (this.getLight(x, yy, z) >> 4) === 15) yy--;
      c.hm[hmi] = yy + 1;
    }
  }
  pushNeighbours(x, y, z, sky) {
    const N = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];
    for (const n of N) {
      const l = this.getLight(x + n[0], y + n[1], z + n[2]);
      const v = sky ? l >> 4 : l & 15;
      if (v > 1) this.pushL(x + n[0], y + n[1], z + n[2], v);
    }
  }

  // ------------------------------------------------------------------ edits
  markDirtyAt(x, y, z) {
    const lx = x & 15, lz = z & 15, ly = (y - WORLD_MIN_Y) & 15, sy = (y - WORLD_MIN_Y) >> 4;
    const cx = x >> 4, cz = z >> 4;
    const dxs = lx === 0 ? -1 : lx === 15 ? 1 : 0, dzs = lz === 0 ? -1 : lz === 15 ? 1 : 0, dys = ly === 0 ? -1 : ly === 15 ? 1 : 0;
    this.markSec(cx, cz, sy);
    if (dxs) this.markSec(cx + dxs, cz, sy);
    if (dzs) this.markSec(cx, cz + dzs, sy);
    if (dys) this.markSec(cx, cz, sy + dys);
    if (dxs && dzs) this.markSec(cx + dxs, cz + dzs, sy);
    if (dxs && dys) this.markSec(cx + dxs, cz, sy + dys);
    if (dzs && dys) this.markSec(cx, cz + dzs, sy + dys);
    if (dxs && dzs && dys) this.markSec(cx + dxs, cz + dzs, sy + dys);
  }
  markSec(cx, cz, sy) {
    if (sy < 0 || sy >= SECTIONS) return;
    const c = this.col(cx, cz);
    if (!c) return;
    c.dirty |= 1 << sy;
    c.meshVer[sy]++;
    if (c.state === 2) this.urgent = true;
  }

  // set a block with full side effects (light, edits, neighbour updates)
  setBlock(x, y, z, id, meta, opts) {
    if (y < WORLD_MIN_Y || y >= WORLD_MAX_Y) return false;
    const c = this.col(x >> 4, z >> 4);
    if (!c || c.state < 1) return false;
    const s = (y - WORLD_MIN_Y) >> 4;
    let sec = c.secs[s];
    if (!sec) {
      if (id === 0) return false;
      for (let k = 0; k <= s; k++) if (!c.secs[k]) c.secs[k] = new Section(true);
      sec = c.secs[s];
    }
    const li = ((y & 15) << 8) | ((z & 15) << 4) | (x & 15);
    const oldId = sec.ids[li], oldMeta = sec.meta[li];
    meta = meta | 0;
    if (oldId === id && oldMeta === meta) return false;
    sec.ids[li] = id; sec.meta[li] = meta;
    if (!c.edits) c.edits = new Map();
    c.edits.set(((y - WORLD_MIN_Y) << 8) | ((z & 15) << 4) | (x & 15), id | (meta << 16));
    this.dirtyCols.add(c.key);
    this.markDirtyAt(x, y, z);
    if (LCOST[oldId] !== LCOST[id] || VDIM[oldId] !== VDIM[id] || EMIT[oldId] !== EMIT[id]) this.relightAt(x, y, z, oldId, id);
    if (!(opts && opts.noUpdate)) {
      this.scheduleAround(x, y, z);
      this.neighbourChanged(x, y, z);
    }
    if (this.onBlockChanged) this.onBlockChanged(x, y, z, oldId, id);
    return true;
  }

  // ------------------------------------------------------------------ scheduled updates (fluids, falling)
  schedule(x, y, z, delay) {
    const k = `${x},${y},${z}`;
    const due = this.tick + delay;
    const cur = this.sched.get(k);
    if (cur === undefined || cur > due) this.sched.set(k, due);
  }
  scheduleAround(x, y, z) {
    this.schedule(x, y, z, 1);
    this.schedule(x + 1, y, z, 1); this.schedule(x - 1, y, z, 1);
    this.schedule(x, y + 1, z, 1); this.schedule(x, y - 1, z, 1);
    this.schedule(x, y, z + 1, 1); this.schedule(x, y, z - 1, 1);
  }

  // support rules for attached blocks
  neighbourChanged(x, y, z) {
    const check = (xx, yy, zz) => {
      const id = this.getBlock(xx, yy, zz);
      if (!id) return;
      if (!this.canStay(xx, yy, zz, id, this.getMeta(xx, yy, zz))) {
        this.breakBlock(xx, yy, zz, true);
      }
    };
    check(x, y + 1, z); check(x, y - 1, z); check(x + 1, y, z); check(x - 1, y, z); check(x, y, z + 1); check(x, y, z - 1);
  }
  canStay(x, y, z, id, m) {
    const sh = SHAPE[id], below = this.getBlock(x, y - 1, z);
    const fl = FLAGS[id];
    if (sh === SH.WATER || sh === SH.LAVA) return true;
    if (fl & BF_AQUATIC) return SOLID[below] || below === id;
    if (sh === SH.CROSS) {
      if (fl & BF_HANG) { const a = this.getBlock(x, y + 1, z); return SOLID[a] || a === id; }
      if (id === B.SUGAR_CANE || id === B.CACTUS || id === B.BAMBOO_PLANT) return below === id || SOLID[below];
      if (id === B.COBWEB) return true;
      return SOLID[below] && below !== B.GLASS;
    }
    if (sh === SH.TALL) {
      if (m & 1) return this.getBlock(x, y - 1, z) === id;
      return this.getBlock(x, y + 1, z) === id && SOLID[below];
    }
    if (sh === SH.DOOR) {
      if ((m >> 2) & 1) return this.getBlock(x, y - 1, z) === id;
      return this.getBlock(x, y + 1, z) === id && SOLID[below];
    }
    if (sh === SH.TORCH) {
      if (m === 0) return SOLID[below] && SHAPE[below] !== SH.PANE;
      const d = m - 1;
      return OPAQUE[this.getBlock(x + DIRX_W[d], y, z + DIRZ_W[d])] === 1;
    }
    if (sh === SH.CARPET || sh === SH.PLATE || sh === SH.RAIL || sh === SH.LILY) {
      if (sh === SH.LILY) return below === B.WATER;
      return SOLID[below] === 1;
    }
    if (sh === SH.LADDER) {
      const f = m & 3, d = (f + 2) & 3;
      return OPAQUE[this.getBlock(x + DIRX_W[d], y, z + DIRZ_W[d])] === 1;
    }
    if (sh === SH.BED) {
      const f = m & 3, head = (m >> 2) & 1;
      const ox = head ? -DIRX_W[f] : DIRX_W[f], oz = head ? -DIRZ_W[f] : DIRZ_W[f];
      return this.getBlock(x + ox, y, z + oz) === id;
    }
    return true;
  }

  // remove block (drops handled by caller via onBreak hook)
  breakBlock(x, y, z, byUpdate) {
    const id = this.getBlock(x, y, z);
    if (!id) return;
    const m = this.getMeta(x, y, z);
    if (this.onBreak) this.onBreak(x, y, z, id, m, byUpdate);
    const fill = (FLAGS[id] & BF_AQUATIC) ? B.WATER : 0;
    this.setBlock(x, y, z, fill, 0);
    const sh = SHAPE[id];
    if (sh === SH.DOOR || sh === SH.TALL) {
      const up = sh === SH.DOOR ? (m >> 2) & 1 : m & 1;
      const oy = up ? y - 1 : y + 1;
      if (this.getBlock(x, oy, z) === id) this.setBlock(x, oy, z, 0, 0);
    }
    if (sh === SH.BED) {
      const f = m & 3, head = (m >> 2) & 1;
      const ox = head ? -DIRX_W[f] : DIRX_W[f], oz = head ? -DIRZ_W[f] : DIRZ_W[f];
      if (this.getBlock(x + ox, y, z + oz) === id) this.setBlock(x + ox, y, z + oz, 0, 0);
    }
  }

  // one game tick (20 per second)
  gameTick() {
    this.tick++;
    if (!this.sched.size) return;
    const due = [];
    for (const [k, t] of this.sched) if (t <= this.tick) due.push(k);
    let n = 0;
    for (const k of due) {
      this.sched.delete(k);
      const p = k.split(',');
      this.updateBlock(+p[0], +p[1], +p[2]);
      if (++n > 400) break;
    }
  }

  updateBlock(x, y, z) {
    if (!this.isLoaded(x, z)) return;
    const id = this.getBlock(x, y, z);
    if (FLAGS[id] & BF_FALL) {
      const b = this.getBlock(x, y - 1, z);
      if (b === 0 || SHAPE[b] === SH.WATER || SHAPE[b] === SH.LAVA || (FLAGS[b] & BF_REPLACE)) {
        const m = this.getMeta(x, y, z);
        this.setBlock(x, y, z, 0, 0);
        this.setBlock(x, y - 1, z, id, m);
        this.schedule(x, y - 1, z, 2);
      }
      return;
    }
    if (id === B.WATER || id === B.LAVA || id === 0 || (FLAGS[id] & BF_REPLACE)) this.fluidUpdate(x, y, z, id);
  }

  // Minecraft-like fluid flow. meta: bits0-2 level (0 = source), bit3 falling
  fluidUpdate(x, y, z, id) {
    for (const fid of [B.WATER, B.LAVA]) {
      const isW = fid === B.WATER;
      const same = (q) => isW ? isWaterId(q) : q === B.LAVA;
      const drop = isW ? 1 : 2;
      const delay = isW ? 5 : 30;
      const cur = this.getBlock(x, y, z);
      if (cur !== 0 && cur !== fid && !(FLAGS[cur] & BF_REPLACE)) continue;
      if (cur !== fid && cur !== 0 && !(FLAGS[cur] & BF_REPLACE)) continue;
      const m = cur === fid ? this.getMeta(x, y, z) : 0;
      const isSource = cur === fid && (m & 7) === 0 && !(m & 8);
      if (cur === fid && (FLAGS[cur] & BF_AQUATIC)) continue;
      // compute what this cell should be
      let want = -1; // -1 none, else level (0..7), +8 falling
      if (isSource) want = 0;
      else {
        const up = this.getBlock(x, y + 1, z);
        if (same(up)) want = 8;
        else {
          let best = 99, sources = 0;
          for (let d = 0; d < 4; d++) {
            const nx = x + DIRX_W[d], nz = z + DIRZ_W[d];
            const nid = this.getBlock(nx, y, nz);
            if (!same(nid)) continue;
            const nm = nid === fid ? this.getMeta(nx, y, nz) : 0;
            let lv = (nm & 8) ? 0 : (nm & 7);
            if ((nm & 7) === 0 && !(nm & 8)) sources++;
            if (lv < best) best = lv;
          }
          if (isW && sources >= 2) {
            const b = this.getBlock(x, y - 1, z);
            if (SOLID[b] || (b === B.WATER && (this.getMeta(x, y - 1, z) & 15) === 0)) best = -1;
          }
          if (best === -1) want = 0;
          else if (best < 99 && best + drop <= 7) want = best + drop;
        }
      }
      if (cur === fid) {
        if (want === -1) { this.setBlock(x, y, z, 0, 0); continue; }
        if (want !== m) { this.setBlock(x, y, z, fid, want); }
      } else {
        if (want === -1) continue;
        if (cur !== 0 && (FLAGS[cur] & BF_REPLACE)) { if (this.onBreak) this.onBreak(x, y, z, cur, 0, true); }
        // lava meets water -> stone/cobble
        this.setBlock(x, y, z, fid, want);
      }
      // spread from this cell
      const nm = this.getMeta(x, y, z);
      if (this.getBlock(x, y, z) !== fid) continue;
      const lv = (nm & 8) ? 0 : (nm & 7);
      const below = this.getBlock(x, y - 1, z);
      if (below === 0 || (FLAGS[below] & BF_REPLACE && !same(below))) {
        this.schedule(x, y - 1, z, delay);
      } else if (!isW && below === B.WATER) {
        this.setBlock(x, y - 1, z, B.STONE, 0);
      } else if (SOLID[below] || same(below)) {
        if (lv + drop <= 7) for (let d = 0; d < 4; d++) {
          const nx = x + DIRX_W[d], nz = z + DIRZ_W[d];
          const nid = this.getBlock(nx, y, nz);
          if (nid === 0 || ((FLAGS[nid] & BF_REPLACE) && !same(nid))) this.schedule(nx, y, nz, delay);
          else if (!isW && nid === B.WATER) this.setBlock(nx, y, nz, B.COBBLE, 0);
          else if (isW && nid === B.LAVA) this.setBlock(nx, y, nz, (this.getMeta(nx, y, nz) & 7) === 0 ? B.OBSIDIAN : B.COBBLE, 0);
        }
      }
      // neighbours that depend on this cell
      for (let d = 0; d < 4; d++) { const nx = x + DIRX_W[d], nz = z + DIRZ_W[d]; if (this.getBlock(nx, y, nz) === fid) this.schedule(nx, y, nz, delay); }
      if (this.getBlock(x, y - 1, z) === fid) this.schedule(x, y - 1, z, delay);
    }
  }

  // ------------------------------------------------------------------ mesh jobs
  // build padded 18^3 arrays for section (cx, sy, cz)
  buildMeshJob(c, sy) {
    let arr = this.meshPool.pop();
    if (!arr) arr = { ids: new Uint16Array(5832), meta: new Uint8Array(5832), light: new Uint8Array(5832) };
    const ids = arr.ids, meta = arr.meta, light = arr.light;
    const cx = c.cx, cz = c.cz;
    // neighbour columns 3x3
    const cols = [];
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) cols.push(this.col(cx + dx, cz + dz));
    for (let y = -1; y <= 16; y++) {
      const wy = sy * 16 + y; // section-relative y from world bottom
      const s = wy >> 4, ly = wy & 15;
      const inRange = wy >= 0 && wy < WORLD_H;
      for (let z = -1; z <= 16; z++) {
        const cz3 = z < 0 ? 0 : z > 15 ? 2 : 1, lz = z & 15;
        let o = ((y + 1) * 18 + (z + 1)) * 18;
        for (let x = -1; x <= 16; x++, o++) {
          const cx3 = x < 0 ? 0 : x > 15 ? 2 : 1;
          const col = cols[cz3 * 3 + cx3];
          if (!inRange || !col) {
            ids[o] = wy < 0 ? B.BEDROCK : 0; meta[o] = 0; light[o] = wy < 0 ? 0 : 0xF0;
            continue;
          }
          const sec = col.secs[s];
          if (!sec) { ids[o] = 0; meta[o] = 0; light[o] = 0xF0; continue; }
          const li = (ly << 8) | (lz << 4) | (x & 15);
          ids[o] = sec.ids[li]; meta[o] = sec.meta[li]; light[o] = sec.light[li];
        }
      }
    }
    return arr;
  }

  sectionEmpty(c, sy) {
    const s = c.secs[sy];
    if (!s) return true;
    const ids = s.ids;
    for (let i = 0; i < 4096; i++) if (ids[i]) return false;
    return true;
  }

  submitMesh(c, sy, fancyLeaves, sway) {
    const arr = this.buildMeshJob(c, sy);
    c.meshBusy |= 1 << sy;
    c.dirty &= ~(1 << sy);
    this.meshInFlight++;
    this.pool.post({ t: 'mesh', cx: c.cx, cz: c.cz, sy, ver: c.meshVer[sy], ids: arr.ids, meta: arr.meta, light: arr.light, fancyLeaves, sway },
      [arr.ids.buffer, arr.meta.buffer, arr.light.buffer]);
  }
}

const DIRX_W = [0, -1, 0, 1], DIRZ_W = [1, 0, -1, 0];

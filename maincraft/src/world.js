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
    // integer fields: coordinates that arrive as doubles (from arrays holding fractions too) would
    // make V8 keep these fields as boxed doubles, and every read of them allocate
    cx |= 0; cz |= 0;
    this.cx = cx; this.cz = cz; this.key = colKey(cx, cz);
    this.secs = new Array(SECTIONS).fill(null);
    this.state = 0;          // 0 requested, 1 have blocks + sky init, 2 fully lit
    this.dirty = 0;          // bitmask of sections that need a remesh
    this.meshVer = new Uint32Array(SECTIONS);
    this.meshBusy = 0;       // bitmask of sections with a mesh job in flight
    this.meshed = 0;         // bitmask of sections that have (possibly empty) meshes
    this.meshes = new Array(SECTIONS).fill(null);
    this.leafMode = -1;      // leaf mesh mode its sections are built with (-1: not chosen yet)
    this.visFrame = new Uint32Array(SECTIONS);   // last frame each section was reached by the visibility walk
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
    this.savedEdits = new Map();   // colKey -> Map: edits of unloaded columns not yet in the database
    this.editKeys = new Set();     // columns with edits in the database (loaded when requested)
    this.editStore = null;         // colKey -> Promise of its stored edits (Map or null)
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
    this.sched = new Map(); this.schedB = new Map();
    this.springCols = new Set();
    this.spawnerCols = new Set();   // columns holding generated monster spawners (ticked near the player)   // columns whose generated fluid springs have not started yet
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
  // height of the fluid surface in a cell as a fraction of the block (FluidState.getHeight): 1 with
  // more of the same fluid above, else its amount / 9 (sources, falling fluid, water plants and
  // waterlogged blocks 8/9); -1 without fluid
  fluidHeight(x, y, z) {
    const id = this.getBlock(x, y, z), w = isWaterId(id);
    if (!w && id !== B.LAVA) return -1;
    const up = this.getBlock(x, y + 1, z);
    if (w ? isWaterId(up) : up === B.LAVA) return 1;
    if (FLAGS[id] & (BF_AQUATIC | BF_WET)) return 8 / 9;
    const m = this.getMeta(x, y, z);
    return (m & 8) ? 8 / 9 : (8 - (m & 7)) / 9;
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
  // biome at a block: the cave biome of its 4x4x4 cell underground, else the surface biome
  biomeAt3(x, y, z) {
    const c = this.col(x >> 4, z >> 4), G = c && c.caveBiomes;
    if (G && y > WORLD_MIN_Y && y < WORLD_MAX_Y) { const b = G[(((y - WORLD_MIN_Y) >> 2) * 4 + ((z & 15) >> 2)) * 4 + ((x & 15) >> 2)]; if (b) return b; }
    return this.biomeAt(x, z);
  }
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
    cx |= 0; cz |= 0;
    const k = colKey(cx, cz);
    if (this.cols.has(k)) return;
    const slot = ((cx & COL_MASK) << 6) | (cz & COL_MASK);
    const old = this.grid[slot];
    if (old) this.unloadColumn(old);
    const c = new Column(cx, cz);
    this.cols.set(k, c);
    this.grid[slot] = c;
    // its stored edits are read while it generates; generation waits for them (onGenerated)
    if (this.editStore && this.editKeys.has(k) && !this.savedEdits.has(k)) {
      c.editsWait = true;
      this.editStore(k).then(m => {
        c.editsWait = false;
        if (c.state < 0) return;
        if (m && m.size && !this.savedEdits.has(k)) this.savedEdits.set(k, m);
        const d = c.genData;
        if (d) { c.genData = null; this.onGenerated(d); }
      });
    }
    this.genInFlight++;
    this.pool.post({ t: 'gen', cx, cz });
  }

  onGenerated(d) {
    const c = this.col(d.cx, d.cz);
    if (!c || c.state) return;
    if (c.editsWait) { c.genData = d; return; }
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
    c.biomes = d.biomes; c.caveBiomes = d.caveBiomes;
    // fluid springs start flowing once the column and its neighbours are lit (see gameTick)
    if (d.springs && d.springs.length) { c.springs = d.springs; this.springCols.add(c); }
    if (d.spawners && d.spawners.length) { c.spawners = d.spawners; this.spawnerCols.add(c); }
    const saved = this.savedEdits.get(c.key);
    if (saved) { c.edits = saved; this.savedEdits.delete(c.key); this.applyEdits(c); }
    // redstone parts among the edits pick up their state again (pending changes are not saved)
    if (c.edits && this.rs) for (const [li, v] of c.edits) if (RS_KIND[v & 0xFFFF]) this.rs.mark(c.cx * 16 + (li & 15), WORLD_MIN_Y + (li >> 8), c.cz * 16 + ((li >> 4) & 15));
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
    if (this.rs && !(opts && opts.quiet)) this.rs.blockChanged(x, y, z, oldId, id);
    return true;
  }

  // ------------------------------------------------------------------ scheduled updates (fluids, falling)
  // Block ticks as Minecraft's LevelTicks: due time per position (the earliest wins), kept in
  // buckets by tick, at most 65536 run per game tick.
  schedule(x, y, z, delay) {
    const k = ((x + 1048576) * 2097152 + (z + 1048576)) * 512 + (y - WORLD_MIN_Y), due = this.tick + Math.max(1, delay);
    const cur = this.sched.get(k);
    if (cur !== undefined && cur <= due) return;
    this.sched.set(k, due);
    let bk = this.schedB.get(due);
    if (!bk) this.schedB.set(due, bk = []);
    bk.push(k);
  }
  // a block changed: it and its six neighbours get a tick, after the delay of what is there
  // (water 5, lava 30, falling blocks 2, anything else 1)
  scheduleAround(x, y, z) {
    const d = (xx, yy, zz) => { const id = this.getBlock(xx, yy, zz); return isWaterId(id) ? 5 : id === B.LAVA ? 30 : (FLAGS[id] & BF_FALL) ? 2 : 1; };
    this.schedule(x, y, z, d(x, y, z));
    for (const [dx, dy, dz] of FACE_DIR) this.schedule(x + dx, y + dy, z + dz, d(x + dx, y + dy, z + dz));
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
  // sugar cane: on cane, or on soil / sand with water right next to that block (Minecraft rule)
  caneSupported(x, y, z) {
    const b = this.getBlock(x, y - 1, z);
    if (b === B.SUGAR_CANE) return true;
    if (b !== B.GRASS && b !== B.DIRT && b !== B.PODZOL && b !== B.COARSE_DIRT && b !== B.MUD && b !== B.SAND && b !== B.RED_SAND) return false;
    return isWaterId(this.getBlock(x + 1, y - 1, z)) || isWaterId(this.getBlock(x - 1, y - 1, z)) ||
      isWaterId(this.getBlock(x, y - 1, z + 1)) || isWaterId(this.getBlock(x, y - 1, z - 1));
  }
  canStay(x, y, z, id, m) {
    const sh = SHAPE[id], below = this.getBlock(x, y - 1, z);
    const fl = FLAGS[id];
    if (sh === SH.WATER || sh === SH.LAVA) return true;
    if (fl & BF_AQUATIC) return SOLID[below] || below === id;
    if (sh === SH.CROSS) {
      if (fl & BF_HANG) { const a = this.getBlock(x, y + 1, z); return SOLID[a] || a === id || (FLAGS[a] & BF_HANG) !== 0; }
      if (id === B.SUGAR_CANE) return this.caneSupported(x, y, z);
      if (id === B.CACTUS || id === B.BAMBOO_PLANT) return below === id || SOLID[below];
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
    // a cocoa pod hangs on a jungle log in its facing direction
    // redstone: dust, repeaters and comparators on a full block, a lever on the block it hangs on
    if (id === B.REDSTONE_WIRE || id === B.REPEATER || id === B.COMPARATOR) return OPAQUE[below] === 1;
    if (id === B.LEVER) {
      const face = (m >> 2) & 3, d = face === 0 ? 5 : face === 2 ? 4 : RS_OPP[m & 3];
      return OPAQUE[this.getBlock(x + RS_DX[d], y + RS_DY[d], z + RS_DZ[d])] === 1;
    }
    // a button hangs on the block on its back side, like a wall torch
    if (sh === SH.BUTTON) { const d = m & 3; return OPAQUE[this.getBlock(x + DIRX_W[d], y, z + DIRZ_W[d])] === 1; }
    if (id === B.COCOA) { const d = m & 3; return isJungleLog(this.getBlock(x + DIRX_W[d], y, z + DIRZ_W[d])); }
    if (sh === SH.CRYSTAL) {
      const n = FACE_DIR[m % 6];
      return OPAQUE[this.getBlock(x - n[0], y - n[1], z - n[2])] === 1;
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

  // remove block (drops handled by caller via onBreak hook; noDrop: destroyed without drops, e.g. by lava)
  breakBlock(x, y, z, byUpdate, noDrop) {
    const id = this.getBlock(x, y, z);
    if (!id) return;
    const m = this.getMeta(x, y, z);
    if (this.onBreak && !noDrop) this.onBreak(x, y, z, id, m, byUpdate);
    const fill = (FLAGS[id] & (BF_AQUATIC | BF_WET)) ? B.WATER : 0;
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
    // Minecraft schedules a fluid tick for every spring it generates; here once the column and
    // its neighbours are lit, so the water can run over the chunk border
    for (const c of this.springCols) {
      if (this.cols.get(c.key) !== c) { this.springCols.delete(c); continue; }
      let ready = c.state === 2;
      for (let dz = -1; dz <= 1 && ready; dz++) for (let dx = -1; dx <= 1 && ready; dx++) { const n = this.col(c.cx + dx, c.cz + dz); if (!n || n.state !== 2) ready = false; }
      if (!ready) continue;
      const sp = c.springs;
      for (let i = 0; i < sp.length; i += 3) this.schedule(sp[i], sp[i + 1], sp[i + 2], 1);
      c.springs = null; this.springCols.delete(c);
    }
    let n = 0;
    for (const [t, bk] of this.schedB) {
      if (t > this.tick) continue;
      while (bk.length && n < 65536) {
        const k = bk.pop();
        if (this.sched.get(k) !== t) continue;      // moved to an earlier tick, or done
        this.sched.delete(k);
        const y = (k % 512) + WORLD_MIN_Y, xz = Math.floor(k / 512), z = (xz % 2097152) - 1048576, x = Math.floor(xz / 2097152) - 1048576;
        this.updateBlock(x, y, z);
        n++;
      }
      if (!bk.length) this.schedB.delete(t);
    }
    if (this.rs) this.rs.tick();
  }

  updateBlock(x, y, z) {
    if (!this.isLoaded(x, z)) return;
    const id = this.getBlock(x, y, z);
    // concrete powder touching water sets into concrete (ConcretePowderBlock)
    const cc = CONCRETE_OF[id];
    if (cc) for (const [dx, dy, dz] of FACE_DIR) if (isWaterId(this.getBlock(x + dx, y + dy, z + dz))) { this.setBlock(x, y, z, cc, 0); return; }
    if (FLAGS[id] & BF_FALL) {
      const b = this.getBlock(x, y - 1, z);
      if (b === 0 || SHAPE[b] === SH.WATER || SHAPE[b] === SH.LAVA || (FLAGS[b] & BF_REPLACE)) {
        const m = this.getMeta(x, y, z);
        this.setBlock(x, y, z, 0, 0);
        // the game turns it into a falling block entity (Minecraft's FallingBlockEntity)
        if (this.onFall) this.onFall(x, y, z, id, m);
        else { this.setBlock(x, y - 1, z, id, m); this.schedule(x, y - 1, z, 2); }
      }
      return;
    }
    if (id === B.WATER || id === B.LAVA) this.fluidUpdate(x, y, z, id);
    // water plants and waterlogged blocks hold a water source, which spreads like one
    else if (FLAGS[id] & (BF_AQUATIC | BF_WET)) this.spread(x, y, z, B.WATER, 0);
  }

  // Minecraft's FlowingFluid. meta: bits 0-2 level (0 = source, amount = 8 - level), bit 3 falling
  // (amount 8). A ticked flowing cell recomputes its state from its neighbours (getNewLiquid), then
  // every cell spreads: down when it can (sideways too only with 3+ source neighbours), else, if it
  // is a source or stands on ground, sideways one level lower (lava two) toward the nearest drop
  // within 4 blocks (lava 2). Spreading sets the target directly; setBlock then schedules the
  // neighbours' ticks (water 5, lava 30), so water advances a block every 5 ticks.
  fluidUpdate(x, y, z, fid) {
    const isW = fid === B.WATER;
    let m = this.getMeta(x, y, z);
    // LiquidBlock.shouldSpreadLiquid: lava touching water (beside or above) hardens
    if (!isW) for (const [dx, dy, dz] of FACE_DIR) {
      if (dy < 0 || !isWaterId(this.getBlock(x + dx, y + dy, z + dz))) continue;
      this.setBlock(x, y, z, m === 0 ? B.OBSIDIAN : B.COBBLE, 0);
      return;
    }
    if (m !== 0) {
      const nw = this.newLiquid(x, y, z, fid);
      if (nw < 0) { this.setBlock(x, y, z, 0, 0); return; }
      if (nw !== m) { this.setBlock(x, y, z, fid, nw); m = nw; }
    }
    this.spread(x, y, z, fid, m);
  }
  sameFluid(id, fid) { return fid === B.WATER ? isWaterId(id) : id === fid; }
  fluidMeta(x, y, z, id) { return id === B.WATER || id === B.LAVA ? this.getMeta(x, y, z) : 0; }
  // the fluid can flow into this cell: nothing there, or a block the flow washes away
  canFlowInto(id) { return id === 0 || (fluidBreaks(id) && !isLiquidId(id)); }
  newLiquid(x, y, z, fid) {
    const isW = fid === B.WATER;
    let maxA = 0, sources = 0;
    for (let d = 0; d < 4; d++) {
      const nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = this.getBlock(nx, y, nz);
      if (!this.sameFluid(n, fid)) continue;
      const nm = this.fluidMeta(nx, y, nz, n);
      if (nm === 0) sources++;
      maxA = Math.max(maxA, (nm & 8) ? 8 : 8 - (nm & 7));
    }
    if (isW && sources >= 2) {
      const b = this.getBlock(x, y - 1, z);
      if ((SOLID[b] && !isLiquidId(b)) || (this.sameFluid(b, fid) && this.fluidMeta(x, y - 1, z, b) === 0)) return 0;
    }
    if (this.sameFluid(this.getBlock(x, y + 1, z), fid)) return 8;
    const a = maxA - (isW ? 1 : 2);
    return a <= 0 ? -1 : 8 - a;
  }
  spread(x, y, z, fid, m) {
    const isW = fid === B.WATER, below = this.getBlock(x, y - 1, z);
    if (!isW && isWaterId(below)) { this.setBlock(x, y - 1, z, B.STONE, 0); return; }   // lava falling onto water
    if (this.canFlowInto(below)) {
      this.spreadTo(x, y - 1, z, fid, 8, below);
      let src = 0;
      for (let d = 0; d < 4; d++) { const n = this.getBlock(x + DIRX_W[d], y, z + DIRZ_W[d]); if (this.sameFluid(n, fid) && this.fluidMeta(x + DIRX_W[d], y, z + DIRZ_W[d], n) === 0) src++; }
      if (src >= 3) this.spreadSides(x, y, z, fid, m);
    } else if (m === 0 || !this.sameFluid(below, fid)) this.spreadSides(x, y, z, fid, m);
  }
  spreadTo(x, y, z, fid, meta, cur) {
    if (cur !== 0) this.breakBlock(x, y, z, true, fid === B.LAVA);   // water drops the block, lava burns it
    this.setBlock(x, y, z, fid, meta);
  }
  // a drop below this cell the fluid could fall into
  isHole(x, y, z, fid) { const b = this.getBlock(x, y - 1, z); return this.canFlowInto(b) || (this.sameFluid(b, fid) && b === fid); }
  slopeDistance(x, y, z, depth, from, fid, maxD) {
    let best = 1000;
    for (let d = 0; d < 4; d++) {
      if (d === from) continue;
      const nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = this.getBlock(nx, y, nz);
      if (!(this.canFlowInto(n) || (n === fid && this.getMeta(nx, y, nz) !== 0))) continue;
      if (this.isHole(nx, y, nz, fid)) return depth;
      if (depth < maxD) best = Math.min(best, this.slopeDistance(nx, y, nz, depth + 1, (d + 2) & 3, fid, maxD));
    }
    return best;
  }
  spreadSides(x, y, z, fid, m) {
    const isW = fid === B.WATER;
    const amount = (m & 8) ? 7 : 8 - (m & 7) - (isW ? 1 : 2);
    if (amount <= 0) return;
    // FlowingFluid.getSpread: the directions with the shortest way to a drop
    const maxD = isW ? 4 : 2, dist = [1000, 1000, 1000, 1000];
    let min = 1000;
    for (let d = 0; d < 4; d++) {
      // flowing fluid of the same kind takes part in choosing the way (it cannot be spread into)
      const nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = this.getBlock(nx, y, nz);
      if (!this.canFlowInto(n) && !(n === fid && this.getMeta(nx, y, nz) !== 0)) continue;
      dist[d] = this.isHole(nx, y, nz, fid) ? 0 : this.slopeDistance(nx, y, nz, 1, (d + 2) & 3, fid, maxD);
      if (dist[d] < min) min = dist[d];
    }
    for (let d = 0; d < 4; d++) {
      const nx = x + DIRX_W[d], nz = z + DIRZ_W[d], n = this.getBlock(nx, y, nz);
      if (!this.canFlowInto(n) || dist[d] > min) continue;
      this.spreadTo(nx, y, nz, fid, 8 - amount, n);
    }
  }

  // ------------------------------------------------------------------ mesh jobs
  // build padded 18^3 arrays for section (cx, sy, cz)
  buildMeshJob(c, sy) {
    let arr = this.meshPool.pop();
    if (!arr) arr = { ids: new Uint16Array(5832), meta: new Uint8Array(5832), light: new Uint8Array(5832) };
    const ids = arr.ids, meta = arr.meta, light = arr.light;
    const cx = c.cx, cz = c.cz;
    const cols = this._jobCols || (this._jobCols = new Array(9));
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) cols[(dz + 1) * 3 + dx + 1] = this.col(cx + dx, cz + dz);
    for (let y = -1; y <= 16; y++) {
      const wy = sy * 16 + y, s = wy >> 4, ly = wy & 15;
      const inRange = wy >= 0 && wy < WORLD_H;
      for (let z = -1; z <= 16; z++) {
        const cz3 = z < 0 ? 0 : z > 15 ? 2 : 1, lz = z & 15;
        const o = ((y + 1) * 18 + (z + 1)) * 18;
        const row = (ly << 8) | (lz << 4);
        for (let k = 0; k < 3; k++) {
          // k=0: x=-1 (west column), k=1: x=0..15, k=2: x=16 (east column)
          const col = cols[cz3 * 3 + k];
          const sec = inRange && col ? col.secs[s] : null;
          const dst = k === 0 ? o : k === 1 ? o + 1 : o + 17, n = k === 1 ? 16 : 1;
          if (!sec) {
            // outside the world is open (below: the void, so the world's underside is drawn as in
            // Minecraft); missing sections inside it are air with full sky light
            const fid = 0, fl = inRange && col ? 0xF0 : (wy < 0 ? 0 : 0xF0);
            ids.fill(fid, dst, dst + n); meta.fill(0, dst, dst + n); light.fill(fl, dst, dst + n);
            continue;
          }
          if (k === 1) {
            const si = sec.ids, sm = sec.meta, sl = sec.light;
            for (let x = 0; x < 16; x++) { ids[dst + x] = si[row + x]; meta[dst + x] = sm[row + x]; light[dst + x] = sl[row + x]; }
          } else {
            const li = row | (k === 0 ? 15 : 0);
            ids[dst] = sec.ids[li]; meta[dst] = sec.meta[li]; light[dst] = sec.light[li];
          }
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

  submitMesh(c, sy, leaves, sway) {
    const arr = this.buildMeshJob(c, sy);
    c.meshBusy |= 1 << sy;
    c.dirty &= ~(1 << sy);
    this.meshInFlight++;
    this.pool.post({ t: 'mesh', cx: c.cx, cz: c.cz, sy, ver: c.meshVer[sy], ids: arr.ids, meta: arr.meta, light: arr.light, leaves, sway,
      tints: this.columnTints(c) }, [arr.ids.buffer, arr.meta.buffer, arr.light.buffer]);
  }

  // Grass / foliage / water colour of every column (3 x 256, RGB565), averaged over the 5x5 columns
  // around it like Minecraft's biome blend, so colours fade across biome borders. Cached once all
  // neighbour biomes are known (biomes never change afterwards).
  columnTints(c) {
    if (c.tints) return c.tints;
    const R = 2, N = 16 + 2 * R;
    const bio = new Uint8Array(N * N);
    let complete = true;
    for (let dz = -1; dz <= 1; dz++) for (let dx = -1; dx <= 1; dx++) {
      const n = dx || dz ? this.col(c.cx + dx, c.cz + dz) : c;
      const nb = n && n.biomes;
      if (!nb) complete = false;
      for (let z = 0; z < 16; z++) {
        const gz = dz * 16 + z + R;
        if (gz < 0 || gz >= N) continue;
        for (let x = 0; x < 16; x++) {
          const gx = dx * 16 + x + R;
          if (gx < 0 || gx >= N) continue;
          // unknown neighbour: repeat this column's edge biome
          bio[gz * N + gx] = nb ? nb[z * 16 + x] : c.biomes[Math.min(15, Math.max(0, gz - R)) * 16 + Math.min(15, Math.max(0, gx - R))];
        }
      }
    }
    const out = new Uint16Array(768), T = BIOME_TINT, D = 2 * R + 1, cnt = D * D;
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
      for (let k = 0; k < 3; k++) {
        let r = 0, g = 0, b = 0;
        for (let j = 0; j < D; j++) for (let i = 0; i < D; i++) {
          const v = T[bio[(z + j) * N + x + i] * 3 + k];
          r += v >> 16; g += (v >> 8) & 255; b += v & 255;
        }
        r /= cnt; g /= cnt; b /= cnt;
        out[k * 256 + z * 16 + x] = (Math.round(r * 31 / 255) << 11) | (Math.round(g * 63 / 255) << 5) | Math.round(b * 31 / 255);
      }
    }
    if (complete) c.tints = out;
    return out;
  }
}

const DIRX_W = [0, -1, 0, 1], DIRZ_W = [1, 0, -1, 0];
// face directions in the mesher's order: +x, -x, +y, -y, +z, -z
const FACE_DIR = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];

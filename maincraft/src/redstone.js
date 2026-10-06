'use strict';
// Redstone: signals 0-15 between sources, dust and the blocks they power, after Minecraft's rules.
//
// A source sends a signal to its neighbours (getSignal). Some also "strongly" power a block
// (getDirectSignal): a torch the block above it, a lever / button the block it hangs on, a pressure
// plate the block below, repeaters, comparators and observers the block in front. Dust only weakly
// powers the block it points into and the one under it. A conductive (full opaque) block passes a
// strong signal on to dust and components, a weak one only to components (lamps, pistons, doors,
// repeaters, torches). Dust levels drop by one per block along a line of dust.
// Delays are in game ticks (a redstone tick is two): repeater 2-8, torch 2, comparator 2, lamp off
// 4, observer pulse 2 + 2. Pistons push up to 12 blocks; sticky pistons pull one back.

const RK = { NONE: 0, WIRE: 1, TORCH: 2, TORCH_OFF: 3, LEVER: 4, BUTTON: 5, PLATE: 6, BLOCK: 7, REPEATER: 8, COMPARATOR: 9,
  LAMP: 10, LAMP_ON: 11, PISTON: 12, HEAD: 13, OBSERVER: 14, DOOR: 15, TRAPDOOR: 16, GATE: 17, TNT: 18 };
const RS_KIND = new Uint8Array(NBX);
(function () {
  const k = (id, v) => { if (id) RS_KIND[id] = v; };
  k(B.REDSTONE_WIRE, RK.WIRE); k(B.REDSTONE_TORCH, RK.TORCH); k(B.REDSTONE_TORCH_OFF, RK.TORCH_OFF); k(B.LEVER, RK.LEVER);
  k(B.REDSTONE_BLOCK, RK.BLOCK); k(B.REPEATER, RK.REPEATER); k(B.COMPARATOR, RK.COMPARATOR); k(B.REDSTONE_LAMP, RK.LAMP);
  k(B.REDSTONE_LAMP_ON, RK.LAMP_ON); k(B.PISTON, RK.PISTON); k(B.STICKY_PISTON, RK.PISTON); k(B.PISTON_HEAD, RK.HEAD);
  k(B.OBSERVER, RK.OBSERVER); k(B.TNT, RK.TNT);
  for (let id = 1; id < NB; id++) {
    const sh = SHAPE[id];
    if (sh === SH.BUTTON) RS_KIND[id] = RK.BUTTON;
    else if (sh === SH.PLATE) RS_KIND[id] = RK.PLATE;
    else if (sh === SH.DOOR) RS_KIND[id] = RK.DOOR;
    else if (sh === SH.TRAPDOOR) RS_KIND[id] = RK.TRAPDOOR;
    else if (sh === SH.GATE) RS_KIND[id] = RK.GATE;
  }
})();
// blocks a piston can not move, and blocks it breaks instead of moving
const RS_IMMOVABLE = new Uint8Array(NBX), RS_FRAGILE = new Uint8Array(NBX);
(function () {
  for (const k of ['BEDROCK', 'OBSIDIAN', 'CRYING_OBSIDIAN', 'SPAWNER', 'CHEST', 'FURNACE', 'FURNACE_LIT', 'BARREL', 'PISTON_HEAD', 'SHIP_WHEEL', 'END_PORTAL_FRAME'])
    if (B[k]) RS_IMMOVABLE[B[k]] = 1;
  for (let id = 1; id < NB; id++) {
    const sh = SHAPE[id];
    if (!SOLID[id] || sh === SH.DOOR || sh === SH.BED || sh === SH.WATER || sh === SH.LAVA || id === B.COCOA ||
      id === B.REPEATER || id === B.COMPARATOR || (FLAGS[id] & BF_REPLACE)) RS_FRAGILE[id] = 1;
  }
})();
const RS_STICK = id => id === B.STICKY_PISTON;
const rsKey = (x, y, z) => ((x + 1048576) * 2097152 + (z + 1048576)) * 512 + (y - WORLD_MIN_Y);

class Redstone {
  constructor(world) {
    this.w = world;
    this.queue = []; this.queued = new Set();     // cells to re-evaluate
    this.timers = new Map();                      // key -> due tick of a pending state change
    this.solved = new Set();                      // dust whose network was solved this tick
    this.powered = new Map();                     // doors, trapdoors, gates, TNT: last powered state (act on edges)
    this.entityCells = null;                      // () -> [x, y, z, ...] cells entities stand in (pressure plates)
    this.container = null;                        // (x, y, z) -> comparator level of a container, or -1
    this.onTnt = null;                            // (x, y, z) primed TNT
    this.sound = null;                            // (kind, x, y, z)
  }
  // ---------------------------------------------------------------- bookkeeping
  mark(x, y, z) {
    const k = rsKey(x, y, z);
    if (this.queued.has(k)) return;
    this.queued.add(k); this.queue.push(x, y, z);
  }
  markAround(x, y, z, deep) {
    this.mark(x, y, z);
    for (let d = 0; d < 6; d++) {
      const nx = x + RS_DX[d], ny = y + RS_DY[d], nz = z + RS_DZ[d];
      this.mark(nx, ny, nz);
      if (deep) for (let e = 0; e < 6; e++) this.mark(nx + RS_DX[e], ny + RS_DY[e], nz + RS_DZ[e]);
    }
  }
  later(x, y, z, delay) {
    const k = rsKey(x, y, z);
    if (!this.timers.has(k)) this.timers.set(k, this.w.tick + delay);
  }
  // every block change: what redstone cares about around it re-evaluates; observers watching it fire
  blockChanged(x, y, z, oldId, newId) {
    const w = this.w;
    let near = RS_KIND[oldId] || RS_KIND[newId];
    for (let d = 0; d < 6 && !near; d++) if (RS_KIND[w.getBlock(x + RS_DX[d], y + RS_DY[d], z + RS_DZ[d])]) near = 1;
    if (!near) return;
    // a source, dust or conductive block changing reaches components two blocks away
    const deep = RS_KIND[oldId] || RS_KIND[newId] || OPAQUE[oldId] || OPAQUE[newId];
    this.markAround(x, y, z, deep);
    for (let d = 0; d < 6; d++) {
      const ox = x + RS_DX[d], oy = y + RS_DY[d], oz = z + RS_DZ[d];
      if (w.getBlock(ox, oy, oz) === B.OBSERVER && (w.getMeta(ox, oy, oz) & 7) === RS_OPP[d]) this.observe(ox, oy, oz);
    }
  }
  observe(x, y, z) {
    const m = this.w.getMeta(x, y, z);
    if (!(m & 8)) this.later(x, y, z, 2);
  }

  // ---------------------------------------------------------------- signals
  // signal the source at (x, y, z) sends to its neighbour in direction d; strong: only a direct one
  emit(x, y, z, d, strong) {
    const w = this.w, id = w.getBlock(x, y, z), k = RS_KIND[id];
    if (!k) return 0;
    const m = w.getMeta(x, y, z);
    switch (k) {
      case RK.TORCH: {
        const attach = m === 0 || m > 4 ? 5 : m - 1;
        if (d === attach) return 0;
        return strong ? (d === 4 ? 15 : 0) : 15;
      }
      case RK.LEVER: {
        if (!(m & 16)) return 0;
        const face = (m >> 2) & 3, attach = face === 0 ? 5 : face === 2 ? 4 : RS_OPP[m & 3];
        return strong && d !== attach ? 0 : 15;
      }
      case RK.BUTTON: {
        if (!(m & 8)) return 0;
        return strong && d !== (((m & 3) + 2) & 3) ? 0 : 15;
      }
      case RK.PLATE: return (m & 1) && (!strong || d === 5) ? 15 : 0;
      case RK.BLOCK: return strong ? 0 : 15;
      case RK.REPEATER: return (m & 16) && d === (m & 3) ? 15 : 0;
      case RK.COMPARATOR: return d === (m & 3) ? (m >> 3) & 15 : 0;
      case RK.OBSERVER: return (m & 8) && d === RS_OPP[m & 7] ? 15 : 0;
      case RK.WIRE: {
        if (strong || d === 4 || !m) return 0;
        if (d === 5) return m & 15;
        return this.wirePoints(x, y, z, d) ? m & 15 : 0;
      }
    }
    return 0;
  }
  // dust points into its neighbour in horizontal direction d: it links that way, links nowhere (a
  // dot reaches all sides), or links only the opposite way (a line runs on)
  wirePoints(x, y, z, d) {
    const L = this.links(x, y, z);
    if (L[d]) return true;
    let n = 0; for (let i = 0; i < 4; i++) if (L[i]) n++;
    return n === 0 || (n === 1 && L[RS_OPP[d]]);
  }
  links(x, y, z) {
    const w = this.w, L = this._L || (this._L = [0, 0, 0, 0]);
    const get = (dx, dy, dz) => w.getBlock(x + dx, y + dy, z + dz), getM = (dx, dy, dz) => w.getMeta(x + dx, y + dy, z + dz);
    for (let d = 0; d < 4; d++) L[d] = wireLink(get, getM, d);
    return L;
  }
  // signal going into the block at (x, y, z) from its neighbours (strong: direct signals only)
  into(x, y, z, strong, skip) {
    let best = 0;
    for (let d = 0; d < 6 && best < 15; d++) {
      if (d === skip) continue;
      const s = this.emit(x + RS_DX[d], y + RS_DY[d], z + RS_DZ[d], RS_OPP[d], strong);
      if (s > best) best = s;
    }
    return best;
  }
  // signal a component at (x, y, z) receives from its neighbour in direction d
  from(x, y, z, d) {
    const w = this.w, nx = x + RS_DX[d], ny = y + RS_DY[d], nz = z + RS_DZ[d], id = w.getBlock(nx, ny, nz);
    if (RS_KIND[id]) return this.emit(nx, ny, nz, RS_OPP[d], false);
    if (OPAQUE[id]) return this.into(nx, ny, nz, false, RS_OPP[d]);
    return 0;
  }
  // strongest signal a component at (x, y, z) receives (skip: a side that does not count)
  powerAt(x, y, z, skip) {
    let best = 0;
    for (let d = 0; d < 6 && best < 15; d++) { if (d === skip) continue; const s = this.from(x, y, z, d); if (s > best) best = s; }
    return best;
  }

  // ---------------------------------------------------------------- dust networks
  // the dust connected to (x, y, z) settles at once: each takes the strongest input around it (not
  // counting other dust; conductive blocks only pass on strong signals), and the levels spread
  // along the dust dropping by one per block
  solveWire(x, y, z) {
    const w = this.w;
    const keys = [], pos = [], index = new Map();
    const add = (a, b, c) => { const k = rsKey(a, b, c); if (index.has(k)) return; index.set(k, keys.length); keys.push(k); pos.push(a, b, c); };
    add(x, y, z);
    const nb = [];
    for (let i = 0; i < keys.length && keys.length < 8192; i++) {
      const a = pos[i * 3], b = pos[i * 3 + 1], c = pos[i * 3 + 2], L = this.links(a, b, c), list = [];
      for (let d = 0; d < 4; d++) {
        if (!L[d]) continue;
        const nx = a + RS_DX[d], nz = c + RS_DZ[d];
        for (const dy of L[d] === 2 ? [1] : [0, -1]) if (w.getBlock(nx, b + dy, nz) === B.REDSTONE_WIRE) { add(nx, b + dy, nz); list.push(index.get(rsKey(nx, b + dy, nz))); break; }
      }
      // dust a step up that links down to this one
      for (let d = 0; d < 4; d++) {
        const nx = a + RS_DX[d], nz = c + RS_DZ[d];
        if (w.getBlock(nx, b + 1, nz) === B.REDSTONE_WIRE && !OPAQUE[w.getBlock(a, b + 1, c)]) { add(nx, b + 1, nz); list.push(index.get(rsKey(nx, b + 1, nz))); }
      }
      nb[i] = list;
    }
    const n = keys.length, lvl = new Int8Array(n);
    for (let i = 0; i < n; i++) {
      this.solved.add(keys[i]);
      const a = pos[i * 3], b = pos[i * 3 + 1], c = pos[i * 3 + 2];
      let s = 0;
      for (let d = 0; d < 6 && s < 15; d++) {
        const ox = a + RS_DX[d], oy = b + RS_DY[d], oz = c + RS_DZ[d], id = w.getBlock(ox, oy, oz);
        let v = 0;
        if (id === B.REDSTONE_WIRE) continue;
        if (RS_KIND[id]) v = this.emit(ox, oy, oz, RS_OPP[d], false);
        else if (OPAQUE[id]) v = this.into(ox, oy, oz, true, RS_OPP[d]);
        if (v > s) s = v;
      }
      lvl[i] = s;
    }
    // spread: strongest first
    const buckets = []; for (let l = 0; l <= 15; l++) buckets.push([]);
    for (let i = 0; i < n; i++) if (lvl[i] > 0) buckets[lvl[i]].push(i);
    for (let l = 15; l > 1; l--) for (const i of buckets[l]) {
      if (lvl[i] !== l) continue;
      for (const j of nb[i]) if (lvl[j] < l - 1) { lvl[j] = l - 1; buckets[l - 1].push(j); }
    }
    for (let i = 0; i < n; i++) {
      const a = pos[i * 3], b = pos[i * 3 + 1], c = pos[i * 3 + 2];
      if ((w.getMeta(a, b, c) & 15) === lvl[i]) continue;
      w.setBlock(a, b, c, B.REDSTONE_WIRE, lvl[i], { noUpdate: true, quiet: true });
      this.markAround(a, b, c, true);
    }
  }

  // ---------------------------------------------------------------- evaluation
  evaluate(x, y, z) {
    const w = this.w, id = w.getBlock(x, y, z), k = RS_KIND[id];
    if (!k) return;
    const m = w.getMeta(x, y, z);
    switch (k) {
      case RK.WIRE: if (!this.solved.has(rsKey(x, y, z))) this.solveWire(x, y, z); return;
      case RK.TORCH: case RK.TORCH_OFF: {
        const a = m === 0 || m > 4 ? 5 : m - 1, ax = x + RS_DX[a], ay = y + RS_DY[a], az = z + RS_DZ[a];
        const want = !(OPAQUE[w.getBlock(ax, ay, az)] && this.into(ax, ay, az, false, RS_OPP[a]) > 0);
        if (want !== (k === RK.TORCH)) this.later(x, y, z, 2);
        return;
      }
      case RK.REPEATER: {
        const f = m & 3, back = RS_OPP[f];
        let locked = false;
        for (const s of [(f + 1) & 3, (f + 3) & 3]) {
          const sx = x + RS_DX[s], sz = z + RS_DZ[s], sid = w.getBlock(sx, y, sz);
          if ((sid === B.REPEATER || sid === B.COMPARATOR) && this.emit(sx, y, sz, RS_OPP[s], false) > 0) locked = true;
        }
        if (locked !== !!(m & 32)) w.setBlock(x, y, z, id, (m & ~32) | (locked ? 32 : 0), { noUpdate: true, quiet: true });
        if (locked) return;
        const on = this.from(x, y, z, back) > 0;
        if (on !== !!(m & 16)) this.later(x, y, z, (((m >> 2) & 3) + 1) * 2);
        return;
      }
      case RK.COMPARATOR: if (this.comparatorOut(x, y, z, m) !== ((m >> 3) & 15)) this.later(x, y, z, 2); return;
      case RK.LAMP: if (this.powerAt(x, y, z) > 0) { w.setBlock(x, y, z, B.REDSTONE_LAMP_ON, 0); } return;
      case RK.LAMP_ON: if (this.powerAt(x, y, z) === 0) this.later(x, y, z, 4); return;
      case RK.PISTON: this.piston(x, y, z, id, m); return;
      case RK.HEAD: {
        const f = m & 7, bx = x - RS_DX[f], by = y - RS_DY[f], bz = z - RS_DZ[f], b = w.getBlock(bx, by, bz);
        if (RS_KIND[b] !== RK.PISTON || (w.getMeta(bx, by, bz) & 15) !== (f | 8)) w.setBlock(x, y, z, 0, 0);
        return;
      }
      case RK.DOOR: {
        const lower = (m >> 2) & 1 ? y - 1 : y;
        if (w.getBlock(x, lower, z) !== id) return;
        const p = this.powerAt(x, lower, z) > 0 || this.powerAt(x, lower + 1, z) > 0;
        if (!this.edge(x, lower, z, p)) return;
        for (const yy of [lower, lower + 1]) if (w.getBlock(x, yy, z) === id) { const mm = w.getMeta(x, yy, z); w.setBlock(x, yy, z, id, p ? mm | 8 : mm & ~8); }
        if (this.sound) this.sound('door', x, lower, z, p);
        return;
      }
      case RK.TRAPDOOR: case RK.GATE: {
        const p = this.powerAt(x, y, z) > 0, bit = k === RK.GATE ? 4 : 8;
        if (!this.edge(x, y, z, p)) return;
        w.setBlock(x, y, z, id, p ? m | bit : m & ~bit);
        if (this.sound) this.sound('door', x, y, z, p);
        return;
      }
      case RK.TNT:
        if (this.powerAt(x, y, z) > 0) { w.setBlock(x, y, z, 0, 0); if (this.onTnt) this.onTnt(x, y, z); }
        return;
    }
  }
  // doors, trapdoors and gates follow a change of power, not the power itself (they stay usable by hand)
  edge(x, y, z, p) {
    const k = rsKey(x, y, z), was = this.powered.get(k) || false;
    if (p) this.powered.set(k, true); else this.powered.delete(k);
    return p !== was;
  }
  comparatorOut(x, y, z, m) {
    const w = this.w, f = m & 3, back = RS_OPP[f], bx = x + RS_DX[back], bz = z + RS_DZ[back];
    let rear = this.from(x, y, z, back);
    if (this.container) { const c = this.container(bx, y, bz); if (c >= 0) rear = Math.max(rear, c); }
    let side = 0;
    for (const s of [(f + 1) & 3, (f + 3) & 3]) {
      const sx = x + RS_DX[s], sz = z + RS_DZ[s], sid = w.getBlock(sx, y, sz);
      if (sid === B.REDSTONE_WIRE || sid === B.REPEATER || sid === B.COMPARATOR || sid === B.REDSTONE_BLOCK) side = Math.max(side, this.emit(sx, y, sz, RS_OPP[s], false));
    }
    return (m & 4) ? Math.max(0, rear - side) : rear >= side ? rear : 0;
  }
  // a scheduled change comes due
  fire(x, y, z) {
    const w = this.w, id = w.getBlock(x, y, z), k = RS_KIND[id], m = w.getMeta(x, y, z);
    switch (k) {
      case RK.TORCH: case RK.TORCH_OFF: {
        const a = m === 0 || m > 4 ? 5 : m - 1, ax = x + RS_DX[a], ay = y + RS_DY[a], az = z + RS_DZ[a];
        const want = !(OPAQUE[w.getBlock(ax, ay, az)] && this.into(ax, ay, az, false, RS_OPP[a]) > 0);
        if (want !== (k === RK.TORCH)) w.setBlock(x, y, z, want ? B.REDSTONE_TORCH : B.REDSTONE_TORCH_OFF, m);
        return;
      }
      // a repeater makes the change it scheduled, then looks again (so short pulses last its delay)
      case RK.REPEATER: if (!(m & 32)) { w.setBlock(x, y, z, id, m ^ 16); this.mark(x, y, z); } return;
      case RK.COMPARATOR: w.setBlock(x, y, z, id, (m & 7) | (this.comparatorOut(x, y, z, m) << 3)); return;
      case RK.LAMP_ON: if (this.powerAt(x, y, z) === 0) w.setBlock(x, y, z, B.REDSTONE_LAMP, 0); return;
      case RK.OBSERVER:
        w.setBlock(x, y, z, id, m ^ 8);
        if (!(m & 8)) this.later(x, y, z, 2);       // the pulse ends two ticks later
        return;
      case RK.BUTTON: if (m & 8) { w.setBlock(x, y, z, id, m & ~8); if (this.sound) this.sound('click', x, y, z, false); } return;
      case RK.PLATE:
        if (!(m & 1)) return;
        if (this.stoodOn(x, y, z)) this.later(x, y, z, 20);
        else { w.setBlock(x, y, z, id, m & ~1); if (this.sound) this.sound('click', x, y, z, false); }
        return;
    }
  }
  stoodOn(x, y, z) {
    const c = this.entityCells ? this.entityCells() : [];
    for (let i = 0; i < c.length; i += 3) if (c[i] === x && c[i + 1] === y && c[i + 2] === z) return true;
    return false;
  }

  // ---------------------------------------------------------------- pistons
  piston(x, y, z, id, m) {
    const f = m & 7, ext = (m & 8) !== 0;
    // powered from any side but the front, or (quasi-connectivity) at the block above it
    let p = this.powerAt(x, y, z, f) > 0;
    if (!p && f !== 4) p = this.powerAt(x, y + 1, z, 5) > 0;
    const hx = x + RS_DX[f], hy = y + RS_DY[f], hz = z + RS_DZ[f], w = this.w;
    if (ext && w.getBlock(hx, hy, hz) !== B.PISTON_HEAD) { w.setBlock(x, y, z, id, f); return; }   // head gone
    if (p && !ext) this.extend(x, y, z, id, f);
    else if (!p && ext) this.retract(x, y, z, id, f);
  }
  extend(x, y, z, id, f) {
    const w = this.w, dx = RS_DX[f], dy = RS_DY[f], dz = RS_DZ[f], line = [];
    let cx = x + dx, cy = y + dy, cz = z + dz, breakAt = null;
    for (;;) {
      if (cy < WORLD_MIN_Y || cy >= WORLD_MAX_Y || !w.isLoaded(cx, cz)) return;
      const b = w.getBlock(cx, cy, cz);
      if (!b) break;
      if (RS_FRAGILE[b]) { breakAt = [cx, cy, cz]; break; }
      if (RS_IMMOVABLE[b] || HARD[b] < 0 || (RS_KIND[b] === RK.PISTON && (w.getMeta(cx, cy, cz) & 8))) return;
      if (line.length >= 12) return;
      line.push(cx, cy, cz, b, w.getMeta(cx, cy, cz));
      cx += dx; cy += dy; cz += dz;
    }
    if (breakAt) w.breakBlock(breakAt[0], breakAt[1], breakAt[2], true);
    for (let i = line.length - 5; i >= 0; i -= 5) w.setBlock(line[i] + dx, line[i + 1] + dy, line[i + 2] + dz, line[i + 3], line[i + 4]);
    w.setBlock(x + dx, y + dy, z + dz, B.PISTON_HEAD, f | (RS_STICK(id) ? 8 : 0));
    w.setBlock(x, y, z, id, f | 8);
    if (this.sound) this.sound('piston', x, y, z, true);
  }
  retract(x, y, z, id, f) {
    const w = this.w, dx = RS_DX[f], dy = RS_DY[f], dz = RS_DZ[f];
    w.setBlock(x, y, z, id, f);
    w.setBlock(x + dx, y + dy, z + dz, 0, 0);
    if (RS_STICK(id)) {
      const px = x + 2 * dx, py = y + 2 * dy, pz = z + 2 * dz, b = w.getBlock(px, py, pz);
      if (b && !RS_FRAGILE[b] && !RS_IMMOVABLE[b] && HARD[b] >= 0 && !(RS_KIND[b] === RK.PISTON && (w.getMeta(px, py, pz) & 8))) {
        const bm = w.getMeta(px, py, pz);
        w.setBlock(px, py, pz, 0, 0);
        w.setBlock(x + dx, y + dy, z + dz, b, bm);
      }
    }
    if (this.sound) this.sound('piston', x, y, z, false);
  }

  // ---------------------------------------------------------------- per game tick
  tick() {
    const w = this.w, now = w.tick;
    this.solved.clear();
    // due changes
    if (this.timers.size) {
      const due = [];
      for (const [k, t] of this.timers) if (t <= now) due.push(k);
      for (const k of due) {
        this.timers.delete(k);
        const y = (k % 512) + WORLD_MIN_Y, xz = Math.floor(k / 512), z = (xz % 2097152) - 1048576, x = Math.floor(xz / 2097152) - 1048576;
        if (w.isLoaded(x, z)) this.fire(x, y, z);
      }
    }
    // pressure plates under entities
    if (this.entityCells) {
      const c = this.entityCells();
      for (let i = 0; i < c.length; i += 3) {
        const x = c[i], y = c[i + 1], z = c[i + 2], id = w.getBlock(x, y, z);
        if (RS_KIND[id] !== RK.PLATE) continue;
        const m = w.getMeta(x, y, z);
        if (!(m & 1)) { w.setBlock(x, y, z, id, m | 1); this.later(x, y, z, 20); if (this.sound) this.sound('click', x, y, z, true); }
      }
    }
    // everything marked settles (instant changes cascade within the tick)
    let n = 0;
    while (this.queue.length && n < 65536) {
      const q = this.queue; this.queue = []; this.queued.clear();
      for (let i = 0; i < q.length; i += 3, n++) if (w.isLoaded(q[i], q[i + 2])) this.evaluate(q[i], q[i + 1], q[i + 2]);
    }
  }
  // a button pressed by the player: on for 1 s (stone) or 1.5 s (wood)
  press(x, y, z) {
    const w = this.w, id = w.getBlock(x, y, z), m = w.getMeta(x, y, z);
    if (m & 8) return;
    w.setBlock(x, y, z, id, m | 8);
    const k = rsKey(x, y, z);
    this.timers.delete(k);
    this.later(x, y, z, /PLANKS|WOOD|BAMBOO/.test(B_KEY[id] || '') ? 30 : 20);
  }
}

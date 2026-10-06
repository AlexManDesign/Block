'use strict';
// Boats, after Minecraft's Boat: placed from the item on water (or ground), boarded with the use
// key, steered with W / A / S / D, left with Shift, broken by hitting it (it drops the item).
// Per game tick (20/s): the boat floats up by how deep it sits in water (0.0615 x depth / height)
// against gravity 0.04, is slowed by 0.9 in water, by the block under it on land (0.6, ice 0.98)
// and 0.45 under water; the rider turns it by 1 degree a tick and pushes it 0.04 forward
// (0.005 back), so it tops out near 8 blocks a second on open water.

const BOAT_W = 1.375, BOAT_H = 0.5625;

class Boats {
  constructor(game) {
    this.game = game;
    this.list = [];
    this.riding = null;
    this.acc = 0;
  }
  place(x, y, z, yaw) {
    const b = { pos: [x, y, z], prev: [x, y, z], vel: [0, 0, 0], yaw, pyaw: yaw, drot: 0, damage: 0, onGround: false, status: 'air' };
    this.list.push(b);
    return b;
  }
  // nearest boat on the ray (eye, unit dir) within range: {b, t}
  raycast(eye, dir, range) {
    let best = null;
    for (const b of this.list) {
      const hw = BOAT_W / 2, lo = [b.pos[0] - hw, b.pos[1], b.pos[2] - hw], hi = [b.pos[0] + hw, b.pos[1] + BOAT_H, b.pos[2] + hw];
      let t0 = 0, t1 = range, ok = true;
      for (let a = 0; a < 3 && ok; a++) {
        if (Math.abs(dir[a]) < 1e-9) { if (eye[a] < lo[a] || eye[a] > hi[a]) ok = false; continue; }
        let ta = (lo[a] - eye[a]) / dir[a], tb = (hi[a] - eye[a]) / dir[a];
        if (ta > tb) { const t = ta; ta = tb; tb = t; }
        t0 = Math.max(t0, ta); t1 = Math.min(t1, tb);
        if (t0 > t1) ok = false;
      }
      if (ok && (!best || t0 < best.t)) best = { b, t: t0 };
    }
    return best;
  }
  hit(b, dmg) {
    const g = this.game;
    if (!this.list.includes(b)) return;
    b.damage += dmg * 10;
    if (g.mode !== 'survival' || b.damage > 40) {
      if (this.riding === b) this.leave();
      this.list.splice(this.list.indexOf(b), 1);
      if (g.mode === 'survival') g.ents.dropItem(IT.BOAT, 1, b.pos[0], b.pos[1] + 0.5, b.pos[2]);
      Sfx.block(B.PLANKS, 'break');
    } else Sfx.block(B.PLANKS, 'hit');
  }
  board(b) {
    this.riding = b;
    const p = this.game.player;
    p.flying = false; p.vel = [0, 0, 0]; p.fallDist = 0;
    this.seat();
  }
  leave() {
    const b = this.riding, p = this.game.player, w = this.game.world;
    this.riding = null;
    if (!b) return;
    // step off beside the boat where there is room, else on top of it
    for (const [dx, dz] of [[1.2, 0], [-1.2, 0], [0, 1.2], [0, -1.2]]) {
      const x = b.pos[0] + dx, z = b.pos[2] + dz;
      for (const y of [b.pos[1] + 0.5, b.pos[1] + 1.5]) {
        if (!entCollides(w, x, y, z, PLAYER_W, 1.8)) { p.pos = [x, y, z]; p.prev = p.pos.slice(); p.vel = [0, 0, 0]; return; }
      }
    }
    p.pos = [b.pos[0], b.pos[1] + BOAT_H + 0.1, b.pos[2]]; p.prev = p.pos.slice(); p.vel = [0, 0, 0];
  }
  // the rider sits in the middle, a little down into the hull
  seat() {
    const b = this.riding, p = this.game.player;
    p.pos[0] = b.pos[0]; p.pos[1] = b.pos[1] - 0.25; p.pos[2] = b.pos[2];
    p.prev = p.pos.slice(); p.prevOf = p.pos;
    p.vel[0] = p.vel[1] = p.vel[2] = 0; p.onGround = true; p.inWater = false; p.fallDist = 0;
  }

  // ---------------------------------------------------------------- physics
  update(dt, keys) {
    if (this.riding && keys && keys.sneak) { this.leave(); return; }
    this.acc += dt;
    let n = 0;
    while (this.acc >= 0.05 && n < 10) {
      this.acc -= 0.05; n++;
      for (const b of this.list) { b.prev = b.pos.slice(); b.pyaw = b.yaw; this.tick(b, b === this.riding ? keys : null); }
    }
    if (n >= 10) this.acc = 0;
    if (this.riding) this.seat();
  }
  tick(b, keys) {
    const w = this.game.world, P = b.pos, V = b.vel, hw = BOAT_W / 2;
    if (!w.isLoaded(Math.floor(P[0]), Math.floor(P[2]))) return;
    // water around the hull: the highest surface over the cells under it, and whether it is covered
    let level = -Infinity, under = false;
    const y0 = Math.floor(P[1]), ytop = P[1] + BOAT_H;
    for (let bx = Math.floor(P[0] - hw); bx <= Math.floor(P[0] + hw); bx++) for (let bz = Math.floor(P[2] - hw); bz <= Math.floor(P[2] + hw); bz++) {
      for (let by = y0; by <= Math.floor(ytop); by++) {
        const h = w.fluidHeight(bx, by, bz);
        if (h < 0 || !isWaterId(w.getBlock(bx, by, bz))) continue;
        const s = by + h;
        if (by >= Math.floor(ytop) && s > ytop + 0.001) under = true;
        if (s > level) level = s;
      }
    }
    const inWater = level > P[1];
    // Boat.getStatus, then floatBoat
    const status = under ? 'under' : inWater ? 'water' : b.onGround ? 'land' : 'air';
    let inv = 0.05, lift = 0, grav = -0.04;
    if (b.status === 'air' && (status === 'water' || status === 'under')) {
      // dropping in from the air: it settles on the surface at once
      P[1] = level - BOAT_H + 0.101; V[1] = 0; b.status = 'water';
    } else {
      b.status = status;
      if (status === 'water') { inv = 0.9; lift = (level - P[1]) / BOAT_H; }
      else if (status === 'under') { inv = 0.45; lift = 0.01; }
      else if (status === 'land') {
        const id = w.getBlock(Math.floor(P[0]), Math.floor(P[1] - 0.001), Math.floor(P[2]));
        inv = id === B.ICE || id === B.PACKED_ICE ? 0.98 : id === B.BLUE_ICE ? 0.989 : 0.6;
        if (keys) inv /= 2;
      } else inv = 0.9;
      V[0] *= inv; V[2] *= inv; V[1] += grav;
      b.drot *= inv;
      if (lift > 0) V[1] = (V[1] + lift * 0.06153846) * 0.75;
    }
    // Boat.controlBoat
    if (keys) {
      let f = 0;
      if (keys.left) b.drot -= 1;
      if (keys.right) b.drot += 1;
      if (keys.right !== keys.left && !keys.forward && !keys.back) f += 0.005;
      if (keys.forward) f += 0.04;
      if (keys.back) f -= 0.005;
      const r = b.drot * Math.PI / 180;
      b.yaw += r;
      this.game.player.yaw += r;
      V[0] += Math.sin(b.yaw) * f; V[2] -= Math.cos(b.yaw) * f;
    } else b.yaw += b.drot * Math.PI / 180;
    // move, axis by axis
    b.onGround = false;
    for (let a = 0; a < 3; a++) {
      const d = V[a];
      if (!d) continue;
      const o = P[a];
      P[a] = o + d;
      if (entCollides(w, P[0], P[1], P[2], BOAT_W, BOAT_H)) {
        P[a] = o;
        if (a === 1 && d < 0) b.onGround = true;
        V[a] = 0;
      }
    }
    if (b.damage > 0) b.damage = Math.max(0, b.damage - 1);
  }

  // ---------------------------------------------------------------- drawing
  render(cam, env) {
    if (!this.list.length) return;
    const g = this.game, r = g.r, al = this.acc / 0.05;
    const v = this.vb || (this.vb = new VBuf()), tmp = this.tmp || (this.tmp = new VBuf()), C = this.col || (this.col = new Float64Array(4));
    v.n = 0; C[3] = 1;
    const L = g.assets.layers.oak_planks;
    // own design: a flat bottom, low sides and ends, a bench across the middle (local x across, z along)
    const parts = [[-0.62, 0, -0.85, 0.62, 0.12, 0.85], [-0.69, 0.06, -0.85, -0.56, 0.5, 0.85], [0.56, 0.06, -0.85, 0.69, 0.5, 0.85],
      [-0.56, 0.06, -0.92, 0.56, 0.44, -0.8], [-0.56, 0.06, 0.8, 0.56, 0.44, 0.92], [-0.56, 0.28, -0.12, 0.56, 0.36, 0.12]];
    for (const b of this.list) {
      const x = b.prev[0] + (b.pos[0] - b.prev[0]) * al, y = b.prev[1] + (b.pos[1] - b.prev[1]) * al, z = b.prev[2] + (b.pos[2] - b.prev[2]) * al;
      const yaw = b.pyaw + (b.yaw - b.pyaw) * al, c = Math.cos(yaw), s = Math.sin(yaw);
      C[0] = C[1] = C[2] = g.lightAt(x, y + 0.5, z, env);
      tmp.n = 0;
      for (const q of parts) g.pushBox(tmp, q[0], q[1], q[2], q[3], q[4], q[5], L, C, 0, null, ITEM_SHADE);
      const T = tmp.a, n = tmp.n, o = v.n, a = v.reserve(n);
      for (let i = 0; i < n; i += 10) {
        const lx = T[i], lz = T[i + 2];
        a[o + i] = lx * c - lz * s + x - cam[0]; a[o + i + 1] = T[i + 1] + y - cam[1]; a[o + i + 2] = lx * s + lz * c + z - cam[2];
        for (let j = 3; j < 10; j++) a[o + i + j] = T[i + j];
      }
      v.n = o + n;
    }
    r.drawArr(v.view(), v.n / 10, env, 0.5);
    // Minecraft's water mask: a depth-only patch over the inside of the hull, so the water surface
    // drawn later does not show inside the boat
    v.n = 0;
    const W = g.assets.layers.white;
    for (const b of this.list) {
      const x = b.prev[0] + (b.pos[0] - b.prev[0]) * al, y = b.prev[1] + (b.pos[1] - b.prev[1]) * al + 0.42, z = b.prev[2] + (b.pos[2] - b.prev[2]) * al;
      const yaw = b.pyaw + (b.yaw - b.pyaw) * al, c = Math.cos(yaw), s = Math.sin(yaw);
      const P = (lx, lz) => [lx * c - lz * s + x - cam[0], y - cam[1], lx * s + lz * c + z - cam[2]];
      const q = [P(-0.56, -0.8), P(0.56, -0.8), P(0.56, 0.8), P(-0.56, 0.8)];
      for (const i of [0, 1, 2, 0, 2, 3]) v.push(q[i][0], q[i][1], q[i][2], 0, 0, W, 1, 1, 1, 1);
    }
    const gl = r.gl;
    gl.colorMask(false, false, false, false); gl.disable(gl.CULL_FACE);
    r.drawArr(v.view(), v.n / 10, env, 0.5);
    gl.colorMask(true, true, true, true); gl.enable(gl.CULL_FACE);
  }

  // ---------------------------------------------------------------- saving
  serialize() { return this.list.map(b => [b.pos[0], b.pos[1], b.pos[2], b.yaw]); }
  restore(d) {
    this.list = []; this.riding = null;
    if (Array.isArray(d)) for (const [x, y, z, yaw] of d) this.place(x, y, z, yaw || 0);
  }
}

'use strict';
// Player physics (main thread). Units: blocks, seconds.

const PLAYER_W = 0.6, PLAYER_H = 1.8, PLAYER_SNEAK_H = 1.5, SWIM_H = 0.85, EYE = 1.62;

class Player {
  constructor() {
    this.pos = [0.5, 100, 0.5];
    this.vel = [0, 0, 0];
    this.yaw = 0; this.pitch = 0;
    this.h = PLAYER_H;
    this.onGround = false; this.flying = false; this.sneaking = false; this.sprinting = false;
    this.inWater = false; this.headInWater = false; this.inLava = false; this.onLadder = false; this.inWeb = false;
    this.swimming = false;
    this.fallStart = null;
    this.stepAcc = 0;
    this.bob = 0; this.bobAmt = 0;          // limb swing of the player model
    this.walkDist = 0; this.bobA = 0;       // Minecraft view bobbing: distance walked x0.6, amplitude
    this.hitWall = false;
    this.fallDamage = 0;
    this.eyeOffset = EYE;
  }
  eye() { return [this.pos[0], this.pos[1] + this.eyeOffset, this.pos[2]]; }
  look() {
    const cp = Math.cos(this.pitch);
    return [Math.sin(this.yaw) * cp, Math.sin(this.pitch), -Math.cos(this.yaw) * cp];
  }
  // horizontal facing index: 0 S(+z), 1 W(-x), 2 N(-z), 3 E(+x)
  facingDir() {
    const l = this.look();
    if (Math.abs(l[0]) > Math.abs(l[2])) return l[0] > 0 ? 3 : 1;
    return l[2] > 0 ? 0 : 2;
  }

  // AABB collision test against world at feet position p with height h
  collides(world, px, py, pz, h) {
    const hw = PLAYER_W / 2;
    const x0 = px - hw, x1 = px + hw, y0 = py, y1 = py + h, z0 = pz - hw, z1 = pz + hw;
    const bx0 = Math.floor(x0), bx1 = Math.floor(x1 - 1e-7), by0 = Math.floor(y0 - 0.5), by1 = Math.floor(y1 - 1e-7), bz0 = Math.floor(z0), bz1 = Math.floor(z1 - 1e-7);
    for (let y = by0; y <= by1; y++) for (let z = bz0; z <= bz1; z++) for (let x = bx0; x <= bx1; x++) {
      const id = world.getBlock(x, y, z);
      if (!id || !SOLID[id]) continue;
      const boxes = collisionBoxes(world, id, world.getMeta(x, y, z), x, y, z);
      if (!boxes) continue;
      for (const b of boxes) {
        if (x + b[0] < x1 && x + b[3] > x0 && y + b[1] < y1 && y + b[4] > y0 && z + b[2] < z1 && z + b[5] > z0) return true;
      }
    }
    return false;
  }

  moveAxis(world, axis, d) {
    if (d === 0) return 0;
    const p = this.pos;
    const steps = Math.max(1, Math.ceil(Math.abs(d) / 0.45));
    const sd = d / steps;
    let moved = 0;
    for (let i = 0; i < steps; i++) {
      const np = [p[0], p[1], p[2]];
      np[axis] += sd;
      if (!this.collides(world, np[0], np[1], np[2], this.h)) { p[axis] = np[axis]; moved += sd; continue; }
      // binary refine towards the obstacle
      let lo = 0, hi = 1;
      for (let k = 0; k < 8; k++) {
        const mid = (lo + hi) / 2;
        np[axis] = p[axis] + sd * mid;
        if (this.collides(world, np[0], np[1], np[2], this.h)) hi = mid; else lo = mid;
      }
      p[axis] += sd * lo; moved += sd * lo;
      return moved - d; // non-zero -> blocked
    }
    return 0;
  }

  update(world, dt, input, mode) {
    const p = this.pos, v = this.vel, px0 = p[0], pz0 = p[2];
    // stand back up (from sneak / swim pose) when there is room
    if (this.h < PLAYER_H && !this.collides(world, p[0], p[1], p[2], PLAYER_H)) this.h = PLAYER_H;
    const bAt = (x, y, z) => world.getBlock(Math.floor(x), Math.floor(y), Math.floor(z));
    const k = input.keys;
    let fx = 0, fz = 0;
    if (k.forward) fz -= 1; if (k.back) fz += 1; if (k.left) fx -= 1; if (k.right) fx += 1;
    if (input.joy) { fx += input.joy[0]; fz += input.joy[1]; }
    const len = Math.hypot(fx, fz);
    if (len > 1) { fx /= len; fz /= len; }
    const moving = len > 0.01;
    const sneakKey = !!k.sneak;
    const sneakPose = sneakKey && this.onGround && !this.flying;
    let sprint = moving && fz < -0.3 && !!(k.sprint || this.sprintLatch) && !sneakPose;   // Minecraft: sprint only forward
    if (mode === 'survival' && input.hunger !== undefined && input.hunger <= 6) sprint = false;
    this.sprinting = sprint;
    // Minecraft speeds (blocks/s): walk 4.317, sprint 5.612, sneak 1.295, creative flight 10.92 / 21.6
    let speed = this.flying ? (sprint ? 21.6 : 10.92) : sprint ? 5.612 : 4.317;
    if (sneakPose) speed = 1.3;
    const ground = this.onGround && !this.flying ? bAt(p[0], p[1] - 0.01, p[2]) : 0;
    if (ground === B.SOUL_SAND) speed *= 0.4;
    const feetB = bAt(p[0], p[1] + 0.2, p[2]), midB = bAt(p[0], p[1] + 1.1, p[2]);
    this.inWeb = !this.flying && (feetB === B.COBWEB || midB === B.COBWEB);
    if (this.inWeb) speed *= 0.22;
    const sy = Math.sin(this.yaw), cy = Math.cos(this.yaw);
    const wx = -fz * sy + fx * cy, wz = fz * cy + fx * sy;
    let accel = 12;
    if (ground === B.ICE || ground === B.PACKED_ICE) accel = 2.2; else if (ground === B.BLUE_ICE) accel = 1.3;
    // velocity follows the input at Minecraft's rates: ground friction 0.546/tick (~12/s),
    // air drag 0.91/tick (~1.9/s, momentum is kept in jumps), creative flight ~12/s
    const a = Math.min(1, dt * (this.onGround || this.flying ? (this.flying ? 12 : accel) : 1.9));
    v[0] += (wx * speed - v[0]) * a; v[2] += (wz * speed - v[2]) * a;
    // fluids: head probe (top - 0.4) and body probe (+0.5)
    const head = bAt(p[0], p[1] + this.h - 0.4, p[2]), body = bAt(p[0], p[1] + 0.5, p[2]), foot = bAt(p[0], p[1], p[2]);
    const isL = id => id === B.LAVA;
    const fl = id => isWaterId(id) || isL(id);
    const headIn = fl(head);
    this.inWater = headIn || fl(body);
    this.headInWater = isWaterId(bAt(p[0], p[1] + this.eyeOffset, p[2]));
    this.inLava = isL(head) || isL(body) || isL(foot);
    let ladder = false;
    if (!this.flying) {
      const y0 = Math.floor(p[1]), hw = PLAYER_W / 2;
      for (let x = Math.floor(p[0] - hw); x <= Math.floor(p[0] + hw) && !ladder; x++)
        for (let z = Math.floor(p[2] - hw); z <= Math.floor(p[2] + hw); z++) {
          const id = world.getBlock(x, y0, z);
          if ((FLAGS[id] & BF_CLIMB) || SHAPE[id] === SH.LADDER || SHAPE[id] === SH.VINE) { ladder = true; break; }
        }
    }
    this.onLadder = ladder;
    const jump = !!k.jump, down = sneakKey;
    this.swimming = false;
    if (this.flying) {
      let vy = 0; if (jump) vy += 1; if (down) vy -= 1;
      v[1] += (vy * 8 - v[1]) * Math.min(1, dt * 10);
    } else if (this.inWater) {
      const sw = sprint && headIn;
      this.swimming = sw;
      if (sw) {
        if (this.h > SWIM_H) this.h = SWIM_H;
        const cp = Math.cos(this.pitch), sp = Math.sin(this.pitch);
        const tx = (-fz * sy * cp + fx * cy) * speed, tz = (fz * cy * cp + fx * sy) * speed, ty = -fz * sp * speed;
        const aa = Math.min(1, dt * 5);
        v[0] += (tx - v[0]) * aa; v[2] += (tz - v[2]) * aa; v[1] += (ty - v[1]) * aa;
        if (jump) v[1] += 10 * dt; if (down) v[1] -= 14 * dt;
        if (v[1] > speed) v[1] = speed; if (v[1] < -speed) v[1] = -speed;
      } else {
        v[1] -= 5.5 * dt; if (jump) v[1] += 16 * dt; if (down) v[1] -= 22 * dt;
        const minV = down ? -5 : -3.2;
        if (v[1] < minV) v[1] = minV;
        if (v[1] > 3.4 && performance.now() > (this.boostUntil || 0)) v[1] = 3.4;
        const damp = Math.min(1, dt * (sprint ? 0.8 : 2));
        v[0] *= 1 - damp; v[2] *= 1 - damp;
      }
    } else if (ladder) {
      v[1] = jump ? 4 : (down || k.back) ? -4 : moving ? 2.6 : -1.4;
    } else {
      v[1] -= 23 * dt;
      if (this.inWeb && v[1] < -1.2) v[1] = -1.2;
      if (v[1] < -78.4) v[1] = -78.4;   // Minecraft terminal velocity
      if (jump && this.onGround) {
        v[1] = 7.6; this.onGround = false;
        // sprint jump boost. Minecraft adds 0.2 blocks/tick, part of which its landing tick's friction
        // takes back; with this continuous model 1.9 blocks/s gives Minecraft's ~7.1 blocks/s average
        // for sprint jumping (steady state: 5.612 + 0.8 * boost)
        if (sprint) { v[0] += Math.sin(this.yaw) * 1.9; v[2] -= Math.cos(this.yaw) * 1.9; }
      }
    }
    const sneaking = sneakKey && this.onGround && !this.flying && !this.inWater && !ladder;
    this.sneaking = sneaking;
    // ground contact is only established by an actual collision below during this frame's movement
    this.onGround = false; this.hitWall = false;
    let dx = v[0] * dt, dz = v[2] * dt;
    if (sneaking) {
      // don't walk off edges: shrink the step until there's support 0.6 below
      const st = 0.05, sup = (ox, oz) => this.collides(world, p[0] + ox, p[1] - 0.6, p[2] + oz, this.h);
      const dec = d => Math.abs(d) <= st ? 0 : d - Math.sign(d) * st;
      while (dx !== 0 && !sup(dx, 0)) dx = dec(dx);
      while (dz !== 0 && !sup(0, dz)) dz = dec(dz);
      while (dx !== 0 && dz !== 0 && !sup(dx, dz)) { dx = dec(dx); dz = dec(dz); }
      if (dx === 0) v[0] = 0; if (dz === 0) v[2] = 0;
    }
    for (const [ax, d] of [[0, dx], [2, dz]]) {
      if (!d) continue;
      const rest = this.moveAxis(world, ax, d);
      if (rest === 0) continue;
      let stepped = false;
      if (!this.flying && v[1] <= 0.08) {
        const tp = [p[0], p[1] + 0.6, p[2]]; tp[ax] -= rest;
        if (!this.collides(world, tp[0], tp[1], tp[2], this.h)) {
          let y = tp[1];
          while (y - 0.05 > p[1] && !this.collides(world, tp[0], y - 0.05, tp[2], this.h)) y -= 0.05;
          p[ax] = tp[ax]; p[1] = y; this.moveAxis(world, 1, -0.06); this.onGround = true; stepped = true;
        }
      }
      if (!stepped) { v[ax] = 0; this.hitWall = true; }
    }
    const vd = v[1] * dt;
    if (this.moveAxis(world, 1, vd) !== 0) { if (vd < 0) this.onGround = true; v[1] = 0; }
    if (sneaking && this.h > PLAYER_SNEAK_H && !this.collides(world, p[0], p[1], p[2], PLAYER_SNEAK_H)) this.h = PLAYER_SNEAK_H;
    // climb out of water onto a ledge
    if (this.inWater && this.hitWall && moving && !this.flying && !down) {
      const hl = Math.hypot(wx, wz) || 1, qx = p[0] + wx / hl * 0.4, qz = p[2] + wz / hl * 0.4;
      for (let y = 0.6; y <= 1.5 + 1e-6; y += 0.45) {
        if (!this.collides(world, qx, p[1] + y, qz, this.h)) { v[1] = Math.max(v[1], headIn ? 4.5 : 7); this.boostUntil = performance.now() + 350; break; }
      }
    }
    if (this.flying && this.onGround) this.flying = false;
    this.eyeOffset += (this.h - 0.18 - this.eyeOffset) * Math.min(1, dt * 14);
    // fall tracking
    this.fallDamage = 0;
    if (this.flying || this.inWater || ladder || this.inWeb) this.fallStart = null;
    else if (!this.onGround) { if (this.fallStart === null || p[1] > this.fallStart) this.fallStart = p[1]; }
    else if (this.fallStart !== null) { const d = this.fallStart - p[1]; if (d > 3.5) this.fallDamage = Math.floor(d - 3); this.fallStart = null; }
    // view bobbing as in Minecraft: amplitude eases (0.4 per tick) toward min(0.1, blocks moved per
    // tick) while on the ground, walk distance grows by 0.6 per block
    const moved = Math.hypot(p[0] - px0, p[2] - pz0);
    this.walkDist += moved * 0.6;
    const bobT = this.onGround && !this.flying && dt > 0 ? Math.min(0.1, moved / dt / 20) : 0;
    this.bobA += (bobT - this.bobA) * (1 - Math.pow(0.6, dt * 20));
    // limb swing for the third-person model
    const hs = Math.hypot(v[0], v[2]);
    if (this.onGround && !this.flying && hs > 0.1) { this.bob += dt * hs * 1.9; this.bobAmt += (Math.min(1, hs / 5) - this.bobAmt) * Math.min(1, dt * 8); }
    else this.bobAmt += (0 - this.bobAmt) * Math.min(1, dt * 6);
    if (p[1] < WORLD_MIN_Y - 64) { p[1] = WORLD_MIN_Y - 64; v[1] = 0; }
  }
}

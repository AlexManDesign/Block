'use strict';
// Player physics (main thread). Units: blocks, seconds.

const PLAYER_W = 0.6, PLAYER_H = 1.8, PLAYER_SNEAK_H = 1.5, EYE = 1.62;

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
    this.bob = 0; this.bobAmt = 0;
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
    const p = this.pos, v = this.vel;
    // environment probes
    const bAt = (x, y, z) => world.getBlock(Math.floor(x), Math.floor(y), Math.floor(z));
    const head = bAt(p[0], p[1] + this.h - 0.2, p[2]), mid = bAt(p[0], p[1] + 0.5, p[2]), feet = bAt(p[0], p[1] + 0.05, p[2]);
    const isW = id => isWaterId(id), isL = id => id === B.LAVA;
    this.headInWater = isW(bAt(p[0], p[1] + this.eyeOffset, p[2]));
    this.inWater = isW(head) || isW(mid) || isW(feet);
    this.inLava = isL(head) || isL(mid) || isL(feet);
    this.onLadder = !this.flying && ((FLAGS[feet] & BF_CLIMB) || SHAPE[feet] === SH.LADDER || SHAPE[feet] === SH.VINE || SHAPE[mid] === SH.LADDER || SHAPE[mid] === SH.VINE || (FLAGS[mid] & BF_CLIMB));
    this.inWeb = !this.flying && (feet === B.COBWEB || mid === B.COBWEB);
    const k = input.keys;
    let fx = 0, fz = 0;
    if (k.forward) fz -= 1; if (k.back) fz += 1; if (k.left) fx -= 1; if (k.right) fx += 1;
    if (input.joy) { fx += input.joy[0]; fz += input.joy[1]; }
    const len = Math.hypot(fx, fz);
    if (len > 1) { fx /= len; fz /= len; }
    const moving = len > 0.01;
    const sneakKey = !!k.sneak;
    const sneaking = sneakKey && this.onGround && !this.flying;
    let sprint = moving && (k.sprint || this.sprintLatch) && fz < 0 && !sneaking;
    if (mode === 'survival' && input.hunger !== undefined && input.hunger <= 6) sprint = false;
    this.sprinting = sprint;
    let speed = this.flying ? (sprint ? 16 : 10) : sprint ? 5.6 : 4.3;
    if (sneaking) speed = 1.3;
    const ground = this.onGround && !this.flying ? world.getBlock(Math.floor(p[0]), Math.floor(p[1] - 0.05), Math.floor(p[2])) : 0;
    if (ground === B.SOUL_SAND) speed *= 0.4;
    if (this.inWeb) speed *= 0.22;
    const sy = Math.sin(this.yaw), cy = Math.cos(this.yaw);
    const wx = -fz * sy + fx * cy, wz = fz * cy + fx * sy;
    let accel = 12;
    if (ground === B.ICE || ground === B.PACKED_ICE) accel = 2.2; else if (ground === B.BLUE_ICE) accel = 1.3;
    const a = Math.min(1, dt * (this.onGround || this.flying ? (this.flying ? 10 : accel) : 4.5));
    const jump = !!k.jump, down = !!k.sneak;
    this.swimming = false;
    if (this.flying) {
      v[0] += (wx * speed - v[0]) * a; v[2] += (wz * speed - v[2]) * a;
      let vy = 0; if (jump) vy += 1; if (down) vy -= 1;
      v[1] += (vy * 8 - v[1]) * Math.min(1, dt * 10);
    } else if (this.inWater || this.inLava) {
      const lava = this.inLava && !this.inWater;
      const sw = sprint && this.headInWater && !lava;
      this.swimming = sw;
      const sp = lava ? 1.4 : sw ? 5.2 : 2.2;
      if (sw) {
        const cp = Math.cos(this.pitch), sp2 = Math.sin(this.pitch);
        const tx = (-fz * sy * cp + fx * cy) * sp, tz = (fz * cy * cp + fx * sy) * sp, ty = -fz * sp2 * sp;
        const aa = Math.min(1, dt * 5);
        v[0] += (tx - v[0]) * aa; v[2] += (tz - v[2]) * aa; v[1] += (ty - v[1]) * aa;
        if (jump) v[1] += 10 * dt; if (down) v[1] -= 14 * dt;
      } else {
        const aa = Math.min(1, dt * 6);
        v[0] += (wx * sp - v[0]) * aa; v[2] += (wz * sp - v[2]) * aa;
        v[1] -= (lava ? 4 : 5.5) * dt;
        if (jump) v[1] += (lava ? 11 : 16) * dt;
        if (down) v[1] -= 22 * dt;
        const minV = down ? -5 : lava ? -2 : -3.2;
        if (v[1] < minV) v[1] = minV;
        if (v[1] > 3.4 && performance.now() > (this.boostUntil || 0)) v[1] = 3.4;
      }
    } else {
      v[0] += (wx * speed - v[0]) * a; v[2] += (wz * speed - v[2]) * a;
      if (this.onLadder) {
        v[0] *= 0.85; v[2] *= 0.85;
        v[1] = jump ? 3.4 : down ? (sneakKey ? 0 : -3) : (moving && this.hitWall ? 3.4 : Math.max(v[1] - 23 * dt, -2.4));
        if (sneakKey && !jump) v[1] = 0;
      } else {
        v[1] -= 32 * dt;
        if (this.inWeb && v[1] < -1.2) v[1] = -1.2;
        if (v[1] < -78) v[1] = -78;
        if (jump && this.onGround) {
          v[1] = 9.2;
          this.onGround = false;
          if (sprint) { v[0] += sy * 2.0 * 0; v[2] += 0; }
        }
      }
    }
    // sneaking: stay on edges
    let dx = v[0] * dt, dz = v[2] * dt;
    const wasGround = this.onGround;
    if (sneaking && wasGround) {
      const test = (ox, oz) => this.collides(world, p[0] + ox, p[1] - 0.6, p[2] + oz, 0.55);
      if (dx && !test(dx, 0)) { dx = 0; v[0] = 0; }
      if (dz && !test(0, dz)) { dz = 0; v[2] = 0; }
      if (dx && dz && !test(dx, dz)) { dx = 0; dz = 0; v[0] = v[2] = 0; }
    }
    this.hitWall = false;
    const oldY = p[1];
    // horizontal with step-up
    for (const [ax, d] of [[0, dx], [2, dz]]) {
      if (!d) continue;
      const rest = this.moveAxis(world, ax, d);
      if (rest !== 0) {
        let stepped = false;
        if ((wasGround || this.inWater) && !this.flying) {
          const save = p[1];
          if (!this.collides(world, p[0], p[1] + 0.6, p[2], this.h)) {
            p[1] += 0.6;
            const r2 = this.moveAxis(world, ax, rest);
            if (Math.abs(r2) < Math.abs(rest) - 1e-4) {
              // settle down
              this.moveAxis(world, 1, -0.6);
              stepped = true;
            } else p[1] = save;
          }
        }
        if (!stepped) { v[ax] = 0; this.hitWall = true; }
      }
    }
    const rest = this.moveAxis(world, 1, v[1] * dt);
    if (rest !== 0) {
      if (v[1] < 0) this.onGround = true;
      v[1] = 0;
    } else {
      this.onGround = this.collides(world, p[0], p[1] - 0.02, p[2], this.h) && v[1] <= 0;
    }
    if (this.onGround && this.flying) this.flying = false;
    // water exit boost (climb out of water onto a ledge)
    if (this.inWater && this.hitWall && moving && !this.flying && !down) {
      if (!this.collides(world, p[0] + wx * 0.4, p[1] + 1.1, p[2] + wz * 0.4, this.h)) { v[1] = Math.max(v[1], 5.5); this.boostUntil = performance.now() + 350; }
    }
    // sneak height
    const wantH = sneaking || (sneakKey && this.onGround) ? PLAYER_SNEAK_H : PLAYER_H;
    if (wantH > this.h) { if (!this.collides(world, p[0], p[1], p[2], wantH)) this.h = wantH; } else this.h = wantH;
    this.sneaking = sneaking;
    const targetEye = this.h === PLAYER_SNEAK_H ? 1.27 : EYE;
    this.eyeOffset += (targetEye - this.eyeOffset) * Math.min(1, dt * 14);
    // fall tracking
    this.fallDamage = 0;
    if (this.flying || this.inWater || this.onLadder || this.inWeb) this.fallStart = null;
    else if (!this.onGround) { if (this.fallStart === null || p[1] > this.fallStart) this.fallStart = Math.max(this.fallStart ?? p[1], p[1]); }
    else if (this.fallStart !== null) { const d = this.fallStart - p[1]; if (d > 3.5) this.fallDamage = Math.floor(d - 3); this.fallStart = null; }
    // view bobbing
    const hs = Math.hypot(v[0], v[2]);
    if (this.onGround && !this.flying && hs > 0.1) { this.bob += dt * hs * 1.9; this.bobAmt += (Math.min(1, hs / 5) - this.bobAmt) * Math.min(1, dt * 8); }
    else this.bobAmt += (0 - this.bobAmt) * Math.min(1, dt * 6);
    void oldY;
    if (p[1] < WORLD_MIN_Y - 64) { p[1] = WORLD_MIN_Y - 64; v[1] = 0; }
  }
}

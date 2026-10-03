'use strict';
// Player physics (main thread): Minecraft's movement, one step per game tick (1/20 s).

const PLAYER_W = 0.6, PLAYER_H = 1.8, PLAYER_SNEAK_H = 1.5, SWIM_H = 0.6, EYE = 1.62;

class Player {
  constructor() {
    this.pos = [0.5, 100, 0.5];
    this.vel = [0, 0, 0];
    this.yaw = 0; this.pitch = 0;
    this.h = PLAYER_H;
    this.onGround = false; this.flying = false; this.sneaking = false; this.sprinting = false;
    this.inWater = false; this.headInWater = false; this.inLava = false; this.onLadder = false; this.inWeb = false;
    this.swimming = false;
    this.bob = 0; this.bobAmt = 0;          // limb swing of the player model (position, amount)
    this.walkDist = 0; this.bobA = 0;       // Minecraft view bobbing: distance walked x0.6, amplitude
    this.hitWall = false;
    this.fallDamage = 0; this.fallDist = 0; this.noJumpDelay = 0; this.crouching = false;
    this.eyeOffset = EYE;
  }
  // eye at the drawn (interpolated) position, as Minecraft's camera and block picking use
  eye() { const r = this.rpos || this.pos; return [r[0], r[1] + (this.reye ?? this.eyeOffset), r[2]]; }
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

  // One game tick (1/20 s) of Minecraft's player movement, following LocalPlayer.aiStep,
  // LivingEntity.aiStep / travel / move and Player.travel. Inside, velocity is in blocks per tick
  // as in Minecraft; this.vel keeps blocks per second for the rest of the game. The camera and the
  // model are drawn between the last two ticks (interp).
  tick(world, input, mode) {
    const p = this.pos;
    this.prev = [p[0], p[1], p[2]]; this.prevOf = p;
    this.eyePrev = this.eyeOffset; this.walkPrev = this.walkDist; this.bobAPrev = this.bobA; this.limbPrev = this.bob;
    const v = [this.vel[0] / 20, this.vel[1] / 20, this.vel[2] / 20];
    const bAt = (x, y, z) => world.getBlock(Math.floor(x), Math.floor(y), Math.floor(z));
    const k = input.keys;
    // ---- input (LocalPlayer): forward / strafe impulses
    let fx = 0, fz = 0;
    if (k.forward) fz -= 1; if (k.back) fz += 1; if (k.left) fx -= 1; if (k.right) fx += 1;
    if (input.joy) { fx += input.joy[0]; fz += input.joy[1]; }
    const len = Math.hypot(fx, fz);
    if (len > 1) { fx /= len; fz /= len; }
    const jump = !!k.jump, shift = !!k.sneak;
    // ---- fluids and blocks around the body
    const fluid = this.fluidDepth(world);
    this.inWater = fluid.water > 0; this.inLava = fluid.lava > 0;
    this.headInWater = isWaterId(bAt(p[0], p[1] + this.eyeOffset, p[2]));
    const feet = bAt(p[0], p[1], p[2]);
    this.onLadder = !this.flying && ((FLAGS[feet] & BF_CLIMB) !== 0 || SHAPE[feet] === SH.LADDER || SHAPE[feet] === SH.VINE);
    this.inWeb = !this.flying && (feet === B.COBWEB || bAt(p[0], p[1] + 1.1, p[2]) === B.COBWEB);
    // ---- sprinting and swimming (LocalPlayer.aiStep / Player.updateSwimming)
    const forward = -fz >= 0.8;
    const canSprint = mode !== 'survival' || this.flying || input.hunger === undefined || input.hunger > 6;
    if (!this.sprinting && forward && canSprint && (k.sprint || this.sprintLatch) && !this.crouching && (!this.inWater || this.headInWater)) this.sprinting = true;
    if (this.sprinting && (!forward || !canSprint || (this.hitWall && !this.swimming) || (this.inWater && !this.headInWater && !this.swimming))) this.sprinting = false;
    this.swimming = !this.flying && this.sprinting && (this.swimming ? this.inWater : this.headInWater);
    if (this.swimming && !this.inWater) this.swimming = false;
    // pose: swimming 0.6 high, crouching 1.5, standing 1.8 (when there is room)
    this.crouching = shift && !this.flying && !this.swimming;
    const wantH = this.swimming ? SWIM_H : this.crouching ? PLAYER_SNEAK_H : PLAYER_H;
    if (wantH < this.h || !this.collides(world, p[0], p[1], p[2], wantH)) this.h = wantH;
    else if (this.h < PLAYER_SNEAK_H && wantH > PLAYER_SNEAK_H && !this.collides(world, p[0], p[1], p[2], PLAYER_SNEAK_H)) this.h = PLAYER_SNEAK_H;
    this.sneaking = this.h === PLAYER_SNEAK_H && !this.flying && this.onGround;
    // impulses: x0.3 while crouching (or squeezed into the crawl pose), x0.98 always
    const slow = this.crouching || (this.h < PLAYER_SNEAK_H && !this.swimming) ? 0.3 : 1;
    const ix = fx * slow * 0.98, iz = fz * slow * 0.98;
    // creative flight: jump / shift move up and down by 3 x flying speed per tick
    if (this.flying) v[1] += ((jump ? 1 : 0) - (shift ? 1 : 0)) * 0.15;
    // ---- LivingEntity.aiStep
    if (this.noJumpDelay > 0) this.noJumpDelay--;
    for (let i = 0; i < 3; i++) if (Math.abs(v[i]) < 0.003) v[i] = 0;
    if (jump && !this.flying) {
      const depth = Math.max(fluid.water, fluid.lava), thr = this.eyeOffset < 0.4 ? 0 : 0.4;
      if (depth > 0 && !(this.onGround && depth <= thr)) v[1] += 0.04;            // swim up
      else if (this.onGround && this.noJumpDelay === 0) {
        v[1] = 0.42;
        if (this.sprinting) { v[0] += Math.sin(this.yaw) * 0.2; v[2] -= Math.cos(this.yaw) * 0.2; }
        this.noJumpDelay = 10;
      }
    } else this.noJumpDelay = 0;
    // ---- travel
    const sy = Math.sin(this.yaw), cy = Math.cos(this.yaw);
    const moveRelative = (speed) => {
      let ax = ix, az = iz;
      const l2 = ax * ax + az * az;
      if (l2 < 1e-7) return;
      if (l2 > 1) { const l = Math.sqrt(l2); ax /= l; az /= l; }
      ax *= speed; az *= speed;
      v[0] += -az * sy + ax * cy; v[2] += az * cy + ax * sy;
    };
    if (this.swimming) {
      // Player.travel: swimming steers vertically toward the look direction
      const ly = Math.sin(this.pitch), d4 = ly < -0.2 ? 0.085 : 0.06;
      if (ly <= 0 || jump || isWaterId(bAt(p[0], p[1] + 0.9, p[2]))) v[1] += (ly - v[1]) * d4;
    }
    const vy0 = v[1], falling = v[1] <= 0, wasGround = this.onGround;
    const fluidFall = () => {
      if (this.sprinting) return;
      v[1] = falling && Math.abs(v[1] - 0.005) >= 0.003 && Math.abs(v[1] - 0.005) < 0.003 ? -0.003 : v[1] - 0.005;
    };
    if (this.inWater && !this.flying) {
      const f = this.sprinting ? 0.9 : 0.8;
      moveRelative(0.02);
      const y0 = p[1];
      this.move(world, v, shift, wasGround);
      if (this.hitWall && this.onLadder) v[1] = 0.2;
      v[0] *= f; v[1] *= 0.8; v[2] *= f;
      fluidFall();
      if (this.hitWall && this.isFree(world, p[0] + v[0], p[1] + v[1] + 0.6 - p[1] + y0, p[2] + v[2])) v[1] = 0.3;
    } else if (this.inLava && !this.flying) {
      moveRelative(0.02);
      const y0 = p[1];
      this.move(world, v, shift, wasGround);
      if (fluid.lava <= 0.4) { v[0] *= 0.5; v[1] *= 0.8; v[2] *= 0.5; fluidFall(); }
      else { v[0] *= 0.5; v[1] *= 0.5; v[2] *= 0.5; }
      v[1] -= 0.02;
      if (this.hitWall && this.isFree(world, p[0] + v[0], p[1] + v[1] + 0.6 - p[1] + y0, p[2] + v[2])) v[1] = 0.3;
    } else {
      const below = bAt(p[0], p[1] - 0.5, p[2]);
      const bf = below === B.ICE || below === B.PACKED_ICE ? 0.98 : below === B.BLUE_ICE ? 0.989 : below === B.SLIME_BLOCK ? 0.8 : 0.6;
      const f3 = this.onGround ? bf * 0.91 : 0.91;
      const base = this.sprinting ? 0.13 : 0.1;
      const air = this.flying ? (this.sprinting ? 0.1 : 0.05) : this.sprinting ? 0.026 : 0.02;
      moveRelative(this.onGround ? base * (0.216 / (bf * bf * bf)) : air);
      if (this.onLadder) {
        // LivingEntity.handleOnClimbable
        v[0] = Math.max(-0.15, Math.min(0.15, v[0])); v[2] = Math.max(-0.15, Math.min(0.15, v[2]));
        v[1] = Math.max(v[1], -0.15);
        if (v[1] < 0 && shift) v[1] = 0;
        this.fallDist = 0;
      }
      this.move(world, v, shift, wasGround);
      if ((this.hitWall || jump) && this.onLadder) v[1] = 0.2;
      if (!this.flying) v[1] -= 0.08;
      v[0] *= f3; v[1] *= 0.98; v[2] *= f3;
    }
    if (this.flying) v[1] = vy0 * 0.6;
    // creative flight ends on touching the ground
    if (this.flying && this.onGround) this.flying = false;
    // ---- fall damage (Minecraft: ceil(fall distance - 3) on landing)
    if (this.flying || this.inWater || this.onLadder || this.inWeb) this.fallDist = 0;
    else if (this.onGround) { if (this.fallDist > 3) this.fallDamage += Math.ceil(this.fallDist - 3); this.fallDist = 0; }
    else if (p[1] < this.prev[1]) this.fallDist += this.prev[1] - p[1];
    this.vel[0] = v[0] * 20; this.vel[1] = v[1] * 20; this.vel[2] = v[2] * 20;
    // ---- camera eye height eases toward the pose (Camera: 0.5 per tick); view bobbing
    const eyeT = this.h === SWIM_H ? 0.4 : this.h === PLAYER_SNEAK_H ? 1.27 : EYE;
    this.eyeOffset += (eyeT - this.eyeOffset) * 0.5;
    const moved = Math.hypot(p[0] - this.prev[0], p[2] - this.prev[2]);
    this.walkDist += moved * 0.6;
    this.bobA += ((this.onGround && !this.swimming ? Math.min(0.1, moved) : 0) - this.bobA) * 0.4;
    // limb swing of the player model (LivingEntity.calculateEntityAnimation)
    this.bobAmt += (Math.min(1, moved * 4) - this.bobAmt) * 0.4;
    this.bob += this.bobAmt;
    if (p[1] < WORLD_MIN_Y - 64) { p[1] = WORLD_MIN_Y - 64; this.vel[1] = 0; }
  }
  // Fluid heights at the body (Minecraft's updateFluidHeightAndDoFluidPushing, along the centre
  // column): how deep the feet are in water / lava, 8/9 of a block for a full source block.
  fluidDepth(world) {
    const p = this.pos, x = Math.floor(p[0]), z = Math.floor(p[2]);
    let water = 0, lava = 0;
    for (let y = Math.floor(p[1]); y <= Math.floor(p[1] + this.h - 0.001); y++) {
      const id = world.getBlock(x, y, z), w = isWaterId(id), l = id === B.LAVA;
      if (!w && !l) continue;
      let top = 8 / 9;
      if (!(FLAGS[id] & (BF_AQUATIC | BF_WET))) { const m = world.getMeta(x, y, z); if (!(m & 8)) top = (8 - (m & 7)) / 9; }
      const up = world.getBlock(x, y + 1, z);
      if ((w && isWaterId(up)) || (l && up === B.LAVA)) top = 1;
      const d = y + top - p[1];
      if (d <= 0) continue;
      if (w) water = Math.max(water, d); else lava = Math.max(lava, d);
    }
    return { water, lava };
  }
  collidesAt(world, x, y, z) { return this.collides(world, x, y, z, this.h); }
  // Entity.isFree: no collision and no fluid of any height in the cells the box touches
  // (Level.containsAnyLiquid)
  isFree(world, px, py, pz) {
    if (this.collides(world, px, py, pz, this.h)) return false;
    const hw = PLAYER_W / 2;
    for (let y = Math.floor(py); y < Math.ceil(py + this.h); y++) for (let z = Math.floor(pz - hw); z < Math.ceil(pz + hw); z++)
      for (let x = Math.floor(px - hw); x < Math.ceil(px + hw); x++) if (isLiquidId(world.getBlock(x, y, z))) return false;
    return true;
  }
  // Entity.move: cobweb slow-down, sneaking stops at edges, collision Y first then X and Z, step
  // up to 0.6 blocks, collided velocity components cleared, soul sand speed factor.
  move(world, v, shift, wasGround) {
    const p = this.pos;
    let dx = v[0], dy = v[1], dz = v[2];
    if (this.inWeb) { dx *= 0.25; dy *= 0.05; dz *= 0.25; v[0] = v[1] = v[2] = 0; this.fallDist = 0; }
    if (shift && !this.flying && dy <= 0 && wasGround) {
      const st = 0.05, sup = (ox, oz) => this.collides(world, p[0] + ox, p[1] - 0.6, p[2] + oz, this.h);
      const dec = d => Math.abs(d) <= st ? 0 : d - Math.sign(d) * st;
      while (dx !== 0 && !sup(dx, 0)) dx = dec(dx);
      while (dz !== 0 && !sup(0, dz)) dz = dec(dz);
      while (dx !== 0 && dz !== 0 && !sup(dx, dz)) { dx = dec(dx); dz = dec(dz); }
    }
    this.hitWall = false;
    const restY = this.moveAxis(world, 1, dy);
    this.onGround = restY !== 0 && dy < 0;
    if (restY !== 0) v[1] = 0;
    const order = Math.abs(dz) > Math.abs(dx) ? [[2, dz], [0, dx]] : [[0, dx], [2, dz]];
    for (const [ax, d] of order) {
      if (!d) continue;
      const rest = this.moveAxis(world, ax, d);
      if (rest === 0) continue;
      // step up (maxUpStep 0.6) when standing or landing
      if (!this.flying && (this.onGround || wasGround)) {
        const tp = [p[0], p[1] + 0.6, p[2]]; tp[ax] += rest * -1;
        if (!this.collides(world, p[0], p[1] + 0.6, p[2], this.h) && !this.collides(world, tp[0], tp[1], tp[2], this.h)) {
          let y = tp[1];
          while (y - 0.01 > p[1] && !this.collides(world, tp[0], y - 0.01, tp[2], this.h)) y -= 0.01;
          p[ax] = tp[ax]; p[1] = y; this.onGround = true;
          continue;
        }
      }
      v[ax] = 0; this.hitWall = true;
    }
    if (this.onGround && !this.flying) {
      const b = world.getBlock(Math.floor(p[0]), Math.floor(p[1] - 0.5), Math.floor(p[2]));
      if (b === B.SOUL_SAND) { v[0] *= 0.4; v[2] *= 0.4; }
    }
  }
  // drawn position between the last two ticks (alpha 0..1); a teleport (pos replaced) snaps
  interp(alpha) {
    const p = this.pos, q = this.prevOf === p ? this.prev : null, r = this.rpos || (this.rpos = [0, 0, 0]);
    for (let i = 0; i < 3; i++) r[i] = q ? q[i] + (p[i] - q[i]) * alpha : p[i];
    const L = (a, b) => a === undefined || !q ? b : a + (b - a) * alpha;
    this.reye = L(this.eyePrev, this.eyeOffset);
    this.rwalk = L(this.walkPrev, this.walkDist);
    this.rbobA = L(this.bobAPrev, this.bobA);
    this.rlimb = L(this.limbPrev, this.bob);
  }
}

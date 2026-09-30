'use strict';
// Mobs, item drops, arrows: models (Minecraft Java geometry), AI, spawning, physics, rendering.

// Part: { box:[x,y,z,w,h,d] (MC y-down pixel space, relative to pivot), pivot:[x,y,z], uv:[u,v], rot:[rx,ry,rz], anim, mirror, inflate }
// anim: 'head', 'legA', 'legB', 'armA', 'armB', 'body90', 'zarmR','zarmL','wingL','wingR', 'spiderL'...
const MODELS = {
  pig: { tex: 'pig', tw: 64, th: 32, parts: [
    { box: [-4, -4, -8, 8, 8, 8], pivot: [0, 12, -6], uv: [0, 0], anim: 'head' },
    { box: [-2, 0, -9, 4, 3, 1], pivot: [0, 12, -6], uv: [16, 16], anim: 'head' },
    { box: [-5, -10, -7, 10, 16, 8], pivot: [0, 11, 2], uv: [28, 8], rot: [Math.PI / 2, 0, 0] },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [-3, 18, 7], uv: [0, 16], anim: 'legA' },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [3, 18, 7], uv: [0, 16], anim: 'legB', mirror: true },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [-3, 18, -5], uv: [0, 16], anim: 'legB' },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [3, 18, -5], uv: [0, 16], anim: 'legA', mirror: true },
  ] },
  cow: { tex: 'cow', tw: 64, th: 32, parts: [
    { box: [-4, -4, -6, 8, 8, 6], pivot: [0, 4, -8], uv: [0, 0], anim: 'head' },
    { box: [-5, -5, -4, 1, 3, 1], pivot: [0, 4, -8], uv: [22, 0], anim: 'head' },
    { box: [4, -5, -4, 1, 3, 1], pivot: [0, 4, -8], uv: [22, 0], anim: 'head' },
    { box: [-6, -10, -7, 12, 18, 10], pivot: [0, 5, 2], uv: [18, 4], rot: [Math.PI / 2, 0, 0] },
    { box: [-2, 2, -8, 4, 6, 1], pivot: [0, 5, 2], uv: [52, 0], rot: [Math.PI / 2, 0, 0] },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [-4, 12, 7], uv: [0, 16], anim: 'legA' },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [4, 12, 7], uv: [0, 16], anim: 'legB', mirror: true },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [-4, 12, -6], uv: [0, 16], anim: 'legB' },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [4, 12, -6], uv: [0, 16], anim: 'legA', mirror: true },
  ] },
  sheep: { tex: 'sheep', tw: 64, th: 32, parts: [
    { box: [-3, -4, -6, 6, 6, 8], pivot: [0, 6, -8], uv: [0, 0], anim: 'head' },
    { box: [-4, -10, -7, 8, 16, 6], pivot: [0, 5, 2], uv: [28, 8], rot: [Math.PI / 2, 0, 0] },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [-3, 12, 7], uv: [0, 16], anim: 'legA' },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [3, 12, 7], uv: [0, 16], anim: 'legB', mirror: true },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [-3, 12, -5], uv: [0, 16], anim: 'legB' },
    { box: [-2, 0, -2, 4, 12, 4], pivot: [3, 12, -5], uv: [0, 16], anim: 'legA', mirror: true },
  ], fur: { tex: 'sheep_fur', tw: 64, th: 32, parts: [
    { box: [-3, -4, -4, 6, 6, 6], pivot: [0, 6, -8], uv: [0, 0], anim: 'head', inflate: 0.6 },
    { box: [-4, -10, -7, 8, 16, 6], pivot: [0, 5, 2], uv: [28, 8], rot: [Math.PI / 2, 0, 0], inflate: 1.75 },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [-3, 12, 7], uv: [0, 16], anim: 'legA', inflate: 0.5 },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [3, 12, 7], uv: [0, 16], anim: 'legB', inflate: 0.5, mirror: true },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [-3, 12, -5], uv: [0, 16], anim: 'legB', inflate: 0.5 },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [3, 12, -5], uv: [0, 16], anim: 'legA', inflate: 0.5, mirror: true },
  ] } },
  chicken: { tex: 'chicken', tw: 64, th: 32, parts: [
    { box: [-2, -6, -2, 4, 6, 3], pivot: [0, 15, -4], uv: [0, 0], anim: 'head' },
    { box: [-2, -4, -4, 4, 2, 2], pivot: [0, 15, -4], uv: [14, 0], anim: 'head' },
    { box: [-1, -2, -3, 2, 2, 2], pivot: [0, 15, -4], uv: [14, 4], anim: 'head' },
    { box: [-3, -4, -3, 6, 8, 6], pivot: [0, 16, 0], uv: [0, 9], rot: [Math.PI / 2, 0, 0] },
    { box: [-1, 0, -3, 3, 5, 3], pivot: [-2, 19, 1], uv: [26, 0], anim: 'legA' },
    { box: [-1, 0, -3, 3, 5, 3], pivot: [1, 19, 1], uv: [26, 0], anim: 'legB', mirror: true },
    { box: [0, 0, -3, 1, 4, 6], pivot: [-4, 13, 0], uv: [24, 13], anim: 'wingR' },
    { box: [-1, 0, -3, 1, 4, 6], pivot: [4, 13, 0], uv: [24, 13], anim: 'wingL', mirror: true },
  ] },
  zombie: { tex: 'zombie', tw: 64, th: 64, parts: humanoid(4, true, true) },
  skeleton: { tex: 'skeleton', tw: 64, th: 32, parts: humanoid(2, false) },
  player: { tex: 'player', tw: 64, th: 64, parts: humanoid(4, true, true) },
  creeper: { tex: 'creeper', tw: 64, th: 32, parts: [
    { box: [-4, -8, -4, 8, 8, 8], pivot: [0, 6, 0], uv: [0, 0], anim: 'head' },
    { box: [-4, 0, -2, 8, 12, 4], pivot: [0, 6, 0], uv: [16, 16] },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [-2, 18, 4], uv: [0, 16], anim: 'legA' },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [2, 18, 4], uv: [0, 16], anim: 'legB' },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [-2, 18, -4], uv: [0, 16], anim: 'legB' },
    { box: [-2, 0, -2, 4, 6, 4], pivot: [2, 18, -4], uv: [0, 16], anim: 'legA' },
  ] },
  spider: { tex: 'spider', tw: 64, th: 32, parts: (function () {
    const p = [
      { box: [-4, -4, -8, 8, 8, 8], pivot: [0, 15, -3], uv: [32, 4], anim: 'head' },
      { box: [-3, -3, -3, 6, 6, 6], pivot: [0, 15, 0], uv: [0, 0] },
      { box: [-5, -4, -6, 10, 8, 12], pivot: [0, 15, 9], uv: [0, 12] },
    ];
    const ys = [0.785, 0.393, -0.393, -0.785], zs = [2, 1, 0, -1];
    for (let i = 0; i < 4; i++) {
      p.push({ box: [-15, -1, -1, 16, 2, 2], pivot: [-4, 15, zs[i]], uv: [18, 0], rot: [0, ys[i], -0.785], anim: 'spiderR' + i });
      p.push({ box: [-1, -1, -1, 16, 2, 2], pivot: [4, 15, zs[i]], uv: [18, 0], rot: [0, -ys[i], 0.785], anim: 'spiderL' + i, mirror: true });
    }
    return p;
  })() },
};
// Converts a part in the original maincraft layout ({b:[w,h,d], at: bottom-centre, y up, +z = front})
// into the MC model space used here (y down, ground at 24, front = -z).
function opart(b, at, uv, o) {
  o = o || {};
  const [w, h, d] = b, [ax, ay, az] = at;
  const piv = o.piv ? [o.piv[0], 24 - o.piv[1], -o.piv[2]] : [ax, 24 - (ay + h / 2), -az];
  const x0 = ax - w / 2, y0 = 24 - (ay + h), z0 = -(az + d / 2);
  const p = { box: [x0 - piv[0], y0 - piv[1], z0 - piv[2], w, h, d], pivot: piv, uv };
  if (o.anim) p.anim = o.anim;
  if (o.rot) p.rot = [o.rot[0], -o.rot[1], -o.rot[2]];
  return p;
}
MODELS.enderman = { tex: 'enderman', tw: 64, th: 32, halfSwing: true, parts: [
  { box: [-4, -8, -4, 8, 8, 8], pivot: [0, -14, 0], uv: [0, 0], anim: 'head', creepy: true },
  { box: [-4, -8, -4, 8, 8, 8], pivot: [0, -14, 0], uv: [0, 16], anim: 'head', inflate: -0.5 },
  { box: [-4, 0, -2, 8, 12, 4], pivot: [0, -15, 0], uv: [32, 16] },
  { box: [-1, -2, -1, 2, 30, 2], pivot: [-5, -13, 0], uv: [56, 0], anim: 'armR' },
  { box: [-1, -2, -1, 2, 30, 2], pivot: [5, -13, 0], uv: [56, 0], anim: 'armL', mirror: true },
  { box: [-1, 0, -1, 2, 30, 2], pivot: [-2, -6, 0], uv: [56, 0], anim: 'legA' },
  { box: [-1, 0, -1, 2, 30, 2], pivot: [2, -6, 0], uv: [56, 0], anim: 'legB', mirror: true },
] };
// slime: opaque core, eyes and mouth; the outer jelly cube is drawn translucent afterwards
MODELS.slime = { tex: 'slime', tw: 64, th: 32, parts: [
  { box: [-3, 17, -3, 6, 6, 6], pivot: [0, 0, 0], uv: [0, 16] },
  { box: [-3.25, 18, -3.5, 2, 2, 2], pivot: [0, 0, 0], uv: [32, 0] },
  { box: [1.25, 18, -3.5, 2, 2, 2], pivot: [0, 0, 0], uv: [32, 4] },
  { box: [0, 21, -3.5, 1, 1, 1], pivot: [0, 0, 0], uv: [32, 8] },
  { box: [-4, 16, -4, 8, 8, 8], pivot: [0, 0, 0], uv: [0, 0], trans: true },
] };
MODELS.salmon = { tex: 'salmon', tw: 32, th: 32, pitchY: 2.5, parts: [
  opart([2, 4, 3], [0, 0.5, 1.5], [22, 0]),
  opart([3, 5, 8], [0, 0, -4], [0, 0]),
  opart([3, 5, 8], [0, 0, -12], [0, 13], { piv: [0, 2.5, -8], anim: 'tail' }),
  opart([0, 5, 6], [0, 0, -19], [20, 10], { piv: [0, 2.5, -8], anim: 'tail' }),
] };
MODELS.shark = { tex: 'shark', tw: 64, th: 64, scale: 1.25, pitchY: 5.5, parts: [
  opart([8, 7, 13], [0, 2, -2], [0, 0]),
  opart([7, 6, 6], [0, 2.5, 6], [30, 20]),
  opart([4, 3, 4], [0, 3.5, 10.5], [0, 36]),
  opart([1, 5, 5], [0, 9, -1], [16, 36]),
  opart([5, 1, 4], [-5.5, 3, 2], [42, 36], { rot: [0, 0, -0.25] }),
  opart([5, 1, 4], [5.5, 3, 2], [42, 41], { rot: [0, 0, 0.25] }),
  opart([4, 5, 11], [0, 3.5, -13], [0, 20], { piv: [0, 5.5, -8], anim: 'tail' }),
  opart([1, 10, 6], [0, 1, -21], [28, 36], { piv: [0, 5.5, -8], anim: 'tail' }),
] };

function humanoid(armW, wide, hat) {
  const a4 = armW === 4;
  const arm = a4 ? [-3, -2, -2, 4, 12, 4] : [-1, -2, -1, 2, 12, 2];
  const armL = a4 ? [-1, -2, -2, 4, 12, 4] : [-1, -2, -1, 2, 12, 2];
  const leg = a4 ? [-2, 0, -2, 4, 12, 4] : [-1, 0, -1, 2, 12, 2];
  const parts = [
    { box: [-4, -8, -4, 8, 8, 8], pivot: [0, 0, 0], uv: [0, 0], anim: 'head' },
    { box: [-4, 0, -2, 8, 12, 4], pivot: [0, 0, 0], uv: [16, 16] },
    { box: arm, pivot: [-5, 2, 0], uv: [40, 16], anim: 'armR' },
    { box: armL, pivot: [5, 2, 0], uv: wide ? [32, 48] : [40, 16], mirror: !wide, anim: 'armL' },
    { box: leg, pivot: [-1.9, 12, 0], uv: [0, 16], anim: 'legA' },
    { box: leg, pivot: [1.9, 12, 0], uv: wide ? [16, 48] : [0, 16], mirror: !wide, anim: 'legB' },
  ];
  if (hat) parts.push({ box: [-4, -8, -4, 8, 8, 8], pivot: [0, 0, 0], uv: [32, 0], anim: 'head', inflate: 0.5 });
  return parts;
}

const MOB_DEFS = {
  pig: { w: 0.9, h: 0.9, hp: 10, speed: 1.2, drops: () => [[IT.PORKCHOP, 1 + (Math.random() * 3 | 0)]], passive: true },
  cow: { w: 0.9, h: 1.4, hp: 10, speed: 1.1, drops: () => [[IT.BEEF, 1 + (Math.random() * 3 | 0)], [IT.LEATHER, Math.random() * 3 | 0]], passive: true },
  sheep: { w: 0.9, h: 1.3, hp: 8, speed: 1.1, drops: (e) => [[IT.MUTTON, 1 + (Math.random() * 2 | 0)], [e.sheared ? 0 : sheepWool(e), 1]], passive: true },
  chicken: { w: 0.4, h: 0.7, hp: 4, speed: 1.0, drops: () => [[IT.CHICKEN, 1], [IT.FEATHER, Math.random() * 3 | 0]], passive: true },
  zombie: { w: 0.6, h: 1.95, hp: 20, speed: 2.3, dmg: 3, drops: () => [[IT.ROTTEN_FLESH, Math.random() * 3 | 0]], hostile: true, burns: true },
  skeleton: { w: 0.6, h: 1.99, hp: 20, speed: 2.4, dmg: 3, drops: () => [[IT.BONE, Math.random() * 3 | 0], [IT.ARROW, Math.random() * 3 | 0]], hostile: true, burns: true, ranged: true },
  creeper: { w: 0.6, h: 1.7, hp: 20, speed: 2.2, drops: () => [[IT.GUNPOWDER, Math.random() * 3 | 0]], hostile: true, creeper: true },
  spider: { w: 1.4, h: 0.9, hp: 16, speed: 3.0, dmg: 2, drops: () => [[IT.STRING, Math.random() * 3 | 0], [IT.SPIDER_EYE, Math.random() < 0.33 ? 1 : 0]], hostile: true },
  enderman: { w: 0.6, h: 2.9, hp: 40, speed: 3.2, dmg: 7, drops: () => [[IT.ENDER_PEARL, Math.random() * 2 | 0]], hostile: true, neutral: true },
  slime: { w: 1.02, h: 1.02, hp: 4, speed: 2.7, dmg: 2, drops: (e) => [[IT.SLIME_BALL, e.size === 1 ? Math.random() * 3 | 0 : 0]], hostile: true, slime: true },
  salmon: { w: 0.7, h: 0.4, hp: 3, speed: 1.6, drops: (e) => [[e.fire > 0 ? IT.COOKED_SALMON : IT.RAW_SALMON, 1]], passive: true, aquatic: true },
  shark: { w: 0.9, h: 0.8, hp: 24, speed: 4.2, dmg: 6, drops: (e) => [[e.fire > 0 ? IT.COOKED_COD : IT.RAW_COD, 1 + (Math.random() * 2 | 0)], [IT.BONE, Math.random() < 0.35 ? 1 : 0]], hostile: true, aquatic: true },
};
// slime sizes as in the original: [size, hp, contact damage, speed]
const SLIME_SIZES = { 4: [16, 4, 2.1], 2: [4, 2, 2.7], 1: [1, 0, 3.2] };
// sheep colours with the original's odds, wool block and fur tint
const SHEEP_COLORS = [['black', 0.05, [30, 30, 34]], ['gray', 0.1, [125, 125, 125]], ['light_gray', 0.15, [190, 190, 185]],
  ['brown', 0.18, [114, 71, 40]], ['pink', 0.1816, [237, 141, 172]]];
function sheepWool(e) { return B['WOOL_' + (e.color || 'white').toUpperCase()] || B.WOOL_WHITE; }
function sheepTint(e) { for (const c of SHEEP_COLORS) if (c[0] === e.color) return c[2].map(v => v / 255); return null; }
const MOB_TYPES = Object.keys(MOB_DEFS);

// entity AABB collision with the world
function entCollides(world, x, y, z, w, h) {
  const hw = w / 2;
  const x0 = x - hw, x1 = x + hw, y0 = y, y1 = y + h, z0 = z - hw, z1 = z + hw;
  for (let by = Math.floor(y0 - 0.5); by <= Math.floor(y1 - 1e-7); by++)
    for (let bz = Math.floor(z0); bz <= Math.floor(z1 - 1e-7); bz++)
      for (let bx = Math.floor(x0); bx <= Math.floor(x1 - 1e-7); bx++) {
        const id = world.getBlock(bx, by, bz);
        if (!id || !SOLID[id]) continue;
        const boxes = collisionBoxes(world, id, world.getMeta(bx, by, bz), bx, by, bz);
        if (!boxes) continue;
        for (const b of boxes) if (bx + b[0] < x1 && bx + b[3] > x0 && by + b[1] < y1 && by + b[4] > y0 && bz + b[2] < z1 && bz + b[5] > z0) return true;
      }
  return false;
}
function entMove(world, e, dt) {
  const moveAx = (ax, d) => {
    if (!d) return false;
    const n = Math.max(1, Math.ceil(Math.abs(d) / 0.4)), s = d / n;
    for (let i = 0; i < n; i++) {
      const p = e.pos.slice(); p[ax] += s;
      if (entCollides(world, p[0], p[1], p[2], e.w, e.h)) return true;
      e.pos[ax] = p[ax];
    }
    return false;
  };
  e.hitWall = false;
  const hx = moveAx(0, e.vel[0] * dt), hz = moveAx(2, e.vel[2] * dt);
  if (hx) { e.vel[0] = 0; e.hitWall = true; }
  if (hz) { e.vel[2] = 0; e.hitWall = true; }
  const hy = moveAx(1, e.vel[1] * dt);
  if (hy) { if (e.vel[1] < 0) e.onGround = true; e.vel[1] = 0; }
  else e.onGround = entCollides(world, e.pos[0], e.pos[1] - 0.03, e.pos[2], e.w, e.h) && e.vel[1] <= 0;
}

function slimeChunk(cx, cz, seed) {
  let t = Math.imul(cx, 522133279) ^ Math.imul(cz, 668265261) ^ (seed | 0);
  t = Math.imul(t ^ (t >>> 15), 2246822507); t ^= t >>> 13;
  return (t >>> 0) % 10 === 0;
}

class Entities {
  constructor(game) {
    this.game = game;
    this.mobs = [];
    this.items = [];
    this.arrows = [];
    this.spawnT = 0;
  }
  clear() { this.mobs.length = 0; this.items.length = 0; this.arrows.length = 0; }

  spawnMob(type, x, y, z, opts) {
    const d = MOB_DEFS[type];
    const e = { type, pos: [x, y, z], vel: [0, 0, 0], yaw: Math.random() * 6.28, headYaw: 0, bodyYaw: Math.random() * 6.28, hp: d.hp, w: d.w, h: d.h,
      onGround: false, walk: 0, walkAmt: 0, hurtT: 0, deathT: 0, ai: 0, target: null, attackT: 0, fuse: 0, fire: 0, age: 0 };
    if (d.slime) {
      const size = (opts && opts.size) || [1, 2, 4][Math.random() * 3 | 0], S = SLIME_SIZES[size];
      e.size = size; e.hp = S[0]; e.w = e.h = 0.51 * size; e.squish = 0; e.tsq = 0; e.jumpT = 1 + Math.random() * 2;
    }
    if (type === 'sheep') {
      const r = Math.random(); e.color = 'white';
      for (const c of SHEEP_COLORS) if (r < c[1]) { e.color = c[0]; break; }
    }
    if (d.aquatic) e.swimPitch = 0;
    this.mobs.push(e);
    return e;
  }
  dropItem(id, count, x, y, z, vel) {
    if (!id || count <= 0) return;
    this.items.push({ id, count, pos: [x, y, z], vel: vel || [(Math.random() - 0.5) * 2, 3, (Math.random() - 0.5) * 2], age: 0, w: 0.25, h: 0.25, onGround: false });
  }

  update(dt) {
    const g = this.game, w = g.world, p = g.player;
    if (!w) return;
    const night = g.envObj.day < 0.35;
    // ---- spawning
    this.spawnT -= dt;
    if (this.spawnT <= 0) {
      this.spawnT = 1.0;
      let passive = 0, hostile = 0, fish = 0, sharks = 0;
      for (const m of this.mobs) { const md = MOB_DEFS[m.type]; if (md.aquatic) { fish++; if (m.type === 'shark') sharks++; } else if (md.hostile) hostile++; else passive++; }
      if (passive < 14) this.trySpawn(false);
      if (hostile < (night ? 22 : 8) && g.mode === 'survival') this.trySpawn(true);
      this.fishT = (this.fishT || 0) - 1;
      if (this.fishT <= 0) { this.fishT = 5; if (fish < 10) this.trySpawnFish(); }
      this.sharkT = (this.sharkT || 0) - 1;
      if (this.sharkT <= 0) { this.sharkT = 12; if (sharks < 2) this.trySpawnShark(); }
    }
    // ---- mobs
    for (let i = this.mobs.length - 1; i >= 0; i--) {
      const e = this.mobs[i], d = MOB_DEFS[e.type];
      e.age += dt;
      const dx = p.pos[0] - e.pos[0], dz = p.pos[2] - e.pos[2], dist = Math.hypot(dx, dz);
      if (dist > (d.hostile ? 96 : 140) || !w.isLoaded(Math.floor(e.pos[0]), Math.floor(e.pos[2]))) { this.mobs.splice(i, 1); continue; }
      if (e.deathT > 0) { e.deathT += dt; if (e.deathT > 1.0) { this.mobs.splice(i, 1); } continue; }
      if (e.hurtT > 0) e.hurtT -= dt;
      this.ai(e, d, dt, dx, dz, dist);
      // physics
      const inW = isWaterId(w.getBlock(Math.floor(e.pos[0]), Math.floor(e.pos[1] + (d.aquatic ? e.h * 0.5 : 0.3)), Math.floor(e.pos[2])));
      const inL = w.getBlock(Math.floor(e.pos[0]), Math.floor(e.pos[1] + 0.3), Math.floor(e.pos[2])) === B.LAVA;
      e.inWater = inW;
      if (d.aquatic && inW) { /* swimming: the AI steers all three axes */ }
      else if (inW) { e.vel[1] += (1.2 - e.vel[1]) * Math.min(1, dt * 3); if (e.type === 'chicken') e.vel[1] = Math.max(e.vel[1], 0.5); }
      else e.vel[1] -= (e.type === 'chicken' && !e.onGround ? 8 : 28) * dt;
      if (d.aquatic && !inW) {
        // out of water: flop around and slowly suffocate
        e.dryT = (e.dryT || 0) + dt;
        if (e.onGround && Math.random() < dt * 2) { e.vel[1] = 4; e.vel[0] = (Math.random() - 0.5) * 3; e.vel[2] = (Math.random() - 0.5) * 3; }
        if (e.dryT > 4) { e.dryHurtT = (e.dryHurtT || 0) - dt; if (e.dryHurtT <= 0) { e.dryHurtT = 1; this.damageMob(e, 1, null); } }
      } else e.dryT = 0;
      if (e.type === 'enderman' && inW) {
        // endermen hate water: hurt and teleport away
        e.wetT = (e.wetT || 0) - dt;
        if (e.wetT <= 0) { e.wetT = 0.6; this.damageMob(e, 1, null); this.teleport(e); }
      }
      if (e.type === 'sheep' && e.sheared) { e.woolT = (e.woolT ?? 120) - dt; if (e.woolT <= 0) { e.sheared = false; e.woolT = undefined; } }
      if (e.type === 'chicken' && e.vel[1] < -2.5) e.vel[1] = -2.5;
      if (e.vel[1] < -50) e.vel[1] = -50;
      // soft collision with other mobs and the player
      for (let j = 0; j < this.mobs.length; j++) {
        const o = this.mobs[j];
        if (o === e || o.deathT > 0) continue;
        const sx = e.pos[0] - o.pos[0], sz = e.pos[2] - o.pos[2], r = (e.w + o.w) * 0.5;
        if (Math.abs(sx) < r && Math.abs(sz) < r && Math.abs(e.pos[1] - o.pos[1]) < 1.5) {
          const l = Math.hypot(sx, sz) || 0.01, push = (r - l) * 4;
          e.vel[0] += sx / l * push; e.vel[2] += sz / l * push;
        }
      }
      { const r = (e.w + PLAYER_W) * 0.5, l = Math.hypot(dx, dz) || 0.01;
        if (l < r && Math.abs(p.pos[1] - e.pos[1]) < 1.8) { e.vel[0] -= dx / l * (r - l) * 6; e.vel[2] -= dz / l * (r - l) * 6; } }
      const fallStart = e.onGround ? e.pos[1] : e.fallY ?? e.pos[1];
      e.fallY = Math.max(fallStart, e.pos[1]);
      const wasGround = e.onGround;
      entMove(w, e, dt);
      if (d.slime && e.onGround && !wasGround) e.tsq = -0.5;
      if (e.onGround) { const fh = e.fallY - e.pos[1]; if (fh > 3.5 && e.type !== 'chicken' && !d.slime && !inW) this.damageMob(e, Math.floor(fh - 3), null); e.fallY = e.pos[1]; }
      if (d.slime) { e.squish += (e.tsq - e.squish) * 0.5 * Math.min(1, dt * 20); e.tsq *= Math.pow(0.6, dt * 20); }
      if (d.aquatic) { const hs0 = Math.hypot(e.vel[0], e.vel[2]); e.swimPitch += ((inW ? Math.atan2(e.vel[1], hs0 + 0.01) : 0) - e.swimPitch) * Math.min(1, dt * 6); }
      const hs = Math.hypot(e.vel[0], e.vel[2]);
      e.walk += hs * dt * 1.9;
      e.walkAmt += ((hs > 0.2 ? Math.min(1, hs / 2) : 0) - e.walkAmt) * Math.min(1, dt * 8);
      if (hs > 0.2) {
        const ty = Math.atan2(e.vel[0], -e.vel[2]);
        let dy = ty - e.bodyYaw; while (dy > Math.PI) dy -= 6.2832; while (dy < -Math.PI) dy += 6.2832;
        e.bodyYaw += dy * Math.min(1, dt * 8);
      }
      if (inL) { e.fire = 3; if ((e.lavaT = (e.lavaT || 0) - dt) <= 0) { e.lavaT = 0.5; this.damageMob(e, 4, null); } }
      // daylight burning
      if (d.burns && g.envObj.day > 0.6 && !inW) {
        const lt = w.getLight(Math.floor(e.pos[0]), Math.floor(e.pos[1] + 1.6), Math.floor(e.pos[2]));
        if ((lt >> 4) === 15) e.fire = Math.max(e.fire, 1);
      }
      if (e.fire > 0) { e.fire -= dt; e.fireT = (e.fireT || 0) - dt; if (e.fireT <= 0) { e.fireT = 1; this.damageMob(e, 1, null); } if (inW) e.fire = 0; }
      if (e.pos[1] < WORLD_MIN_Y - 30) this.mobs.splice(i, 1);
    }
    // ---- item entities
    for (let i = this.items.length - 1; i >= 0; i--) {
      const it = this.items[i];
      it.age += dt;
      if (it.age > 300) { this.items.splice(i, 1); continue; }
      if (!w.isLoaded(Math.floor(it.pos[0]), Math.floor(it.pos[2]))) continue;
      const dx = p.pos[0] - it.pos[0], dy = p.pos[1] + 0.8 - it.pos[1], dz = p.pos[2] - it.pos[2];
      const d2 = dx * dx + dy * dy + dz * dz;
      if (it.age > 0.6 && d2 < 9 && !g.surv.dead) {
        if (d2 < 1.6) {
          const rest = g.giveItem(it.id, it.count);
          if (rest > 0) it.count = rest; else { this.items.splice(i, 1); continue; }
        } else { const f = 12 / Math.sqrt(d2); it.vel[0] = dx * f * 0.3; it.vel[1] = dy * f * 0.3; it.vel[2] = dz * f * 0.3; }
      }
      const inW = isWaterId(w.getBlock(Math.floor(it.pos[0]), Math.floor(it.pos[1]), Math.floor(it.pos[2])));
      if (inW) it.vel[1] += (1 - it.vel[1]) * Math.min(1, dt * 4); else it.vel[1] -= 16 * dt;
      entMove(w, it, dt);
      if (it.onGround) { it.vel[0] *= Math.max(0, 1 - dt * 8); it.vel[2] *= Math.max(0, 1 - dt * 8); }
      if (entCollides(w, it.pos[0], it.pos[1], it.pos[2], it.w, it.h)) it.pos[1] += dt * 3;
      // merge stacks
      if ((i & 7) === (g.frameN & 7)) for (let j = 0; j < this.items.length; j++) {
        const o = this.items[j];
        if (o === it || o.id !== it.id || o.dur !== undefined) continue;
        if (Math.abs(o.pos[0] - it.pos[0]) + Math.abs(o.pos[1] - it.pos[1]) + Math.abs(o.pos[2] - it.pos[2]) < 0.8 && o.count + it.count <= maxStack(it.id)) {
          o.count += it.count; this.items.splice(i, 1); break;
        }
      }
    }
    // ---- arrows
    for (let i = this.arrows.length - 1; i >= 0; i--) {
      const a = this.arrows[i];
      a.age += dt;
      if (a.age > 10) { this.arrows.splice(i, 1); continue; }
      if (a.stuck) continue;
      a.vel[1] -= 12 * dt;
      const nx = a.pos[0] + a.vel[0] * dt, ny = a.pos[1] + a.vel[1] * dt, nz = a.pos[2] + a.vel[2] * dt;
      if (SOLID[w.getBlock(Math.floor(nx), Math.floor(ny), Math.floor(nz))]) { a.stuck = true; continue; }
      a.pos = [nx, ny, nz];
      const px = p.pos[0] - nx, py = p.pos[1] + 0.9 - ny, pz = p.pos[2] - nz;
      if (a.hostile && Math.abs(px) < 0.4 && Math.abs(py) < 0.95 && Math.abs(pz) < 0.4) {
        g.hurt(a.dmg); p.vel[0] += a.vel[0] * 0.15; p.vel[2] += a.vel[2] * 0.15; this.arrows.splice(i, 1);
      }
    }
  }

  ai(e, d, dt, dx, dz, dist) {
    if (d.aquatic) return this.aiFish(e, d, dt, dx, dz, dist);
    if (d.slime) return this.aiSlime(e, d, dt, dx, dz, dist);
    const g = this.game, w = g.world, p = g.player;
    let tx = 0, tz = 0, speed = d.speed;
    const canHunt = g.mode === 'survival' && !g.surv.dead;
    if (e.type === 'enderman') {
      // provoked by being looked at (in the head) or by being hit; wanders and teleports otherwise
      if (!e.aggro && canHunt && dist < 64) {
        const eye = p.eye(), look = p.look();
        const vx = e.pos[0] - eye[0], vy = e.pos[1] + 2.55 - eye[1], vz = e.pos[2] - eye[2], l = Math.hypot(vx, vy, vz) || 1;
        if ((vx * look[0] + vy * look[1] + vz * look[2]) / l > 1 - 0.025 / l && !this.rayBlocked(eye, [e.pos[0], e.pos[1] + 2.55, e.pos[2]])) { e.aggro = true; e.stareT = 0.6; }
      }
      if (e.aggro && (!canHunt || dist > 64)) e.aggro = false;
      e.tpT = (e.tpT ?? 10 + Math.random() * 30) - dt;
      if (e.tpT <= 0) { e.tpT = 10 + Math.random() * 30; if (!e.aggro || dist > 12) this.teleport(e, e.aggro ? p.pos : null); }
      if (e.stareT > 0) { e.stareT -= dt; e.headYaw = Math.atan2(dx, -dz); e.vel[0] *= 0.5; e.vel[2] *= 0.5; return; }
    }
    const chase = d.hostile && canHunt && dist < (e.type === 'enderman' ? 64 : d.ranged ? 18 : 16) &&
      (e.type !== 'spider' || g.envObj.day < 0.5 || e.hurtT > 0 || e.aggro) && (!d.neutral || e.aggro);
    if (e.panic > 0) {
      e.panic -= dt;
      if (!e.target || e.ai <= 0) { const a = Math.random() * 6.28; e.target = [e.pos[0] + Math.cos(a) * 8, e.pos[2] + Math.sin(a) * 8]; e.ai = 1; }
      speed *= 2;
    } else if (chase) {
      e.target = [p.pos[0], p.pos[2]];
      e.headYaw = Math.atan2(dx, -dz);
      const dy = p.pos[1] - e.pos[1];
      if (d.ranged) {
        if (dist < 7) e.target = [e.pos[0] - dx, e.pos[2] - dz];
        else if (dist < 12) e.target = null;
        e.attackT -= dt;
        if (e.attackT <= 0 && dist < 16) {
          e.attackT = 1.8;
          const tgt = [p.pos[0], p.pos[1] + 1.2, p.pos[2]], src = [e.pos[0], e.pos[1] + 1.5, e.pos[2]];
          const vx = tgt[0] - src[0], vy = tgt[1] - src[1], vz = tgt[2] - src[2], l = Math.hypot(vx, vy, vz) || 1;
          const sp = 22;
          this.arrows.push({ pos: src, vel: [vx / l * sp, vy / l * sp + l * 0.3, vz / l * sp], age: 0, hostile: true, dmg: 2 + (Math.random() * 3 | 0) });
        }
      } else if (d.creeper) {
        if (dist < 3 && Math.abs(dy) < 2.5) {
          e.fuse += dt; e.target = null;
          if (e.fuse > 1.5) { g.explode(e.pos[0], e.pos[1] + 0.8, e.pos[2], 3); e.deathT = 0.9; e.hp = 0; this.mobs.splice(this.mobs.indexOf(e), 1); return; }
        } else e.fuse = Math.max(0, e.fuse - dt);
      } else {
        e.attackT -= dt;
        if (dist < 1.2 + e.w * 0.5 && Math.abs(dy) < 1.6 && e.attackT <= 0) {
          e.attackT = 1.0;
          g.hurt(d.dmg);
          const l = dist || 1; p.vel[0] += dx / l * 6; p.vel[2] += dz / l * 6; p.vel[1] = Math.max(p.vel[1], 4);
        }
      }
    } else {
      e.ai -= dt;
      if (e.ai <= 0) {
        e.ai = 2 + Math.random() * 6;
        if (Math.random() < 0.7) { const a = Math.random() * 6.28, r = 3 + Math.random() * 7; e.target = [e.pos[0] + Math.cos(a) * r, e.pos[2] + Math.sin(a) * r]; }
        else e.target = null;
        e.lookT = Math.random() * 6.28;
      }
      speed *= 0.85;
      // look at the player sometimes
      if (dist < 8) e.headYaw = Math.atan2(dx, -dz); else e.headYaw = e.bodyYaw;
    }
    if (chase && !d.ranged && dist < 0.55 + e.w * 0.5) e.target = null; // stay in front of the player, not inside
    if (e.target) {
      tx = e.target[0] - e.pos[0]; tz = e.target[1] - e.pos[2];
      const l = Math.hypot(tx, tz);
      if (l < 0.5) { e.target = chase ? e.target : null; tx = tz = 0; }
      else { tx /= l; tz /= l; }
    }
    // avoid walking off high edges when wandering
    if ((tx || tz) && !chase && e.onGround) {
      const fx = Math.floor(e.pos[0] + tx * (e.w * 0.5 + 0.4)), fz = Math.floor(e.pos[2] + tz * (e.w * 0.5 + 0.4));
      let drop = 0; for (let k = 1; k <= 4; k++) { if (SOLID[w.getBlock(fx, Math.floor(e.pos[1]) - k, fz)]) break; drop++; }
      const ahead = w.getBlock(fx, Math.floor(e.pos[1]), fz);
      if (drop > 2 || ahead === B.LAVA || (isWaterId(w.getBlock(fx, Math.floor(e.pos[1]) - 1, fz)) && d.passive && Math.random() < 0.8)) { e.target = null; tx = tz = 0; }
    }
    const a = Math.min(1, dt * (e.onGround ? 10 : 2));
    e.vel[0] += (tx * speed - e.vel[0]) * a; e.vel[2] += (tz * speed - e.vel[2]) * a;
    if ((tx || tz) && e.hitWall && e.onGround) e.vel[1] = 8.4;
    if (e.type === 'spider' && e.hitWall && (tx || tz)) e.vel[1] = 3; // climbing
  }

  // ---- fish (salmon, shark): swim in 3D inside water, flop on land
  aiFish(e, d, dt, dx, dz, dist) {
    const g = this.game, w = g.world, p = g.player;
    const water = (x, y, z) => isWaterId(w.getBlock(Math.floor(x), Math.floor(y), Math.floor(z)));
    if (!e.inWater) { e.headYaw = e.bodyYaw; return; }
    let speed = d.speed, tgt = null;
    const dy = p.pos[1] + 0.9 - e.pos[1];
    const hunt = e.type === 'shark' && g.mode === 'survival' && !g.surv.dead && p.inWater && Math.hypot(dist, dy) < 20;
    if (hunt) {
      tgt = [p.pos[0], p.pos[1] + 0.9, p.pos[2]];
      e.attackT -= dt;
      if (Math.hypot(dist, dy) < 1.4 + e.w * 0.5 && e.attackT <= 0) {
        e.attackT = 1.0; g.hurt(d.dmg);
        const l = dist || 1; p.vel[0] += dx / l * 4; p.vel[2] += dz / l * 4;
      }
    } else {
      if (e.panic > 0) { e.panic -= dt; speed *= 2.2; }
      e.ai -= dt;
      if (e.ai <= 0 || !e.target || e.target.length < 3) {
        e.ai = 1.5 + Math.random() * 4;
        e.target = null;
        for (let k = 0; k < 6; k++) {
          let a = Math.random() * 6.28;
          if (e.panic > 0 && dist > 0.1) a = Math.atan2(-dz, -dx) + (Math.random() - 0.5);
          const r = 2 + Math.random() * 6, t = [e.pos[0] + Math.cos(a) * r, e.pos[1] + (Math.random() - 0.5) * 3, e.pos[2] + Math.sin(a) * r];
          if (water(t[0], t[1], t[2]) && water(t[0], t[1] + e.h, t[2])) { e.target = t; break; }
        }
      }
      tgt = e.target;
      speed *= 0.7;
    }
    let vx = 0, vy = 0, vz = 0;
    if (tgt) {
      vx = tgt[0] - e.pos[0]; vy = tgt[1] - e.pos[1]; vz = tgt[2] - e.pos[2];
      const l = Math.hypot(vx, vy, vz);
      if (l < 0.4) { if (!hunt) e.target = null; vx = vy = vz = 0; } else { vx /= l; vy /= l; vz /= l; }
    }
    // stay inside the water body
    if (vy > 0 && !water(e.pos[0], e.pos[1] + e.h + 0.2, e.pos[2])) vy = Math.min(vy, -0.2);
    if ((vx || vz) && !water(e.pos[0] + vx * 0.8, e.pos[1] + e.h * 0.5, e.pos[2] + vz * 0.8)) { vx = vz = 0; e.target = null; }
    const a = Math.min(1, dt * 3);
    e.vel[0] += (vx * speed - e.vel[0]) * a; e.vel[1] += (vy * speed * 0.6 - e.vel[1]) * a; e.vel[2] += (vz * speed - e.vel[2]) * a;
    e.headYaw = e.bodyYaw;
  }

  // ---- slime: hops toward the player (or randomly), damages on contact
  aiSlime(e, d, dt, dx, dz, dist) {
    const g = this.game, p = g.player, S = SLIME_SIZES[e.size];
    const chase = g.mode === 'survival' && !g.surv.dead && dist < 16;
    e.headYaw = chase ? Math.atan2(dx, -dz) : e.bodyYaw;
    if (e.onGround) {
      e.vel[0] *= Math.max(0, 1 - dt * 12); e.vel[2] *= Math.max(0, 1 - dt * 12);
      e.jumpT -= dt;
      if (e.jumpT <= 0) {
        let a;
        if (chase) a = Math.atan2(dz, dx);
        else { a = e.hopDir ?? Math.random() * 6.28; if (Math.random() < 0.3) a = Math.random() * 6.28; }
        e.hopDir = a;
        e.vel[0] = Math.cos(a) * S[2]; e.vel[2] = Math.sin(a) * S[2]; e.vel[1] = 6 + e.size * 0.35;
        e.bodyYaw = Math.atan2(Math.cos(a), -Math.sin(a));
        e.tsq = 1; e.onGround = false;
        e.jumpT = chase ? 0.4 + Math.random() * 0.9 : 1 + Math.random() * 2.5;
      }
    }
    e.attackT -= dt;
    const dy = p.pos[1] - e.pos[1];
    if (chase && S[1] > 0 && dist < e.w * 0.5 + 0.55 && dy > -1 && dy < e.h && e.attackT <= 0) {
      e.attackT = 1.0; g.hurt(S[1]);
      const l = dist || 1; p.vel[0] += dx / l * 4; p.vel[2] += dz / l * 4;
    }
  }

  rayBlocked(a, b) {
    const w = this.game.world, n = Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]) * 2);
    for (let i = 1; i < n; i++) {
      const t = i / n, id = w.getBlock(Math.floor(a[0] + (b[0] - a[0]) * t), Math.floor(a[1] + (b[1] - a[1]) * t), Math.floor(a[2] + (b[2] - a[2]) * t));
      if (id && OPAQUE[id]) return true;
    }
    return false;
  }
  // enderman teleport: a random dry spot with room for its 3-block height (near `toward` if given)
  teleport(e, toward) {
    const w = this.game.world, cx = toward ? toward[0] : e.pos[0], cz = toward ? toward[2] : e.pos[2], cy = toward ? toward[1] : e.pos[1];
    for (let k = 0; k < 16; k++) {
      const x = Math.floor(cx + (Math.random() - 0.5) * (toward ? 10 : 32)), z = Math.floor(cz + (Math.random() - 0.5) * (toward ? 10 : 32));
      if (!w.isLoaded(x, z)) continue;
      for (let y = Math.floor(cy) + 8; y > cy - 16; y--) {
        const g = w.getBlock(x, y - 1, z);
        if (!SOLID[g] || !OPAQUE[g] || isWaterId(g)) continue;
        if (w.getBlock(x, y, z) || w.getBlock(x, y + 1, z) || w.getBlock(x, y + 2, z)) break;
        this.game.spawnParticles(e.pos[0] - 0.5, e.pos[1] + 0.8, e.pos[2] - 0.5, B.OBSIDIAN, 12);
        e.pos = [x + 0.5, y, z + 0.5]; e.vel = [0, 0, 0]; e.target = null; e.fallY = y;
        this.game.spawnParticles(e.pos[0] - 0.5, e.pos[1] + 0.8, e.pos[2] - 0.5, B.OBSIDIAN, 12);
        return true;
      }
    }
    return false;
  }

  trySpawnFish() {
    const g = this.game, w = g.world, p = g.player;
    for (let tries = 0; tries < 8; tries++) {
      const a = Math.random() * 6.2832, r = 10 + Math.random() * 30;
      const x = Math.floor(p.pos[0] + Math.cos(a) * r), z = Math.floor(p.pos[2] + Math.sin(a) * r);
      if (!w.isLoaded(x, z)) continue;
      const ys = [];
      for (let y = SEA; y >= SEA - 24; y--) if (isWaterId(w.getBlock(x, y, z)) && isWaterId(w.getBlock(x, y + 1, z))) ys.push(y);
      if (ys.length < 2) continue;
      const n = 3 + (Math.random() * 3 | 0);
      for (let k = 0; k < n; k++) {
        const ox = x + (Math.random() * 5 | 0) - 2, oz = z + (Math.random() * 5 | 0) - 2, oy = ys[Math.random() * ys.length | 0];
        if (isWaterId(w.getBlock(ox, oy, oz)) && isWaterId(w.getBlock(ox, oy + 1, oz))) this.spawnMob('salmon', ox + 0.5, oy + 0.2, oz + 0.5);
      }
      return;
    }
  }
  trySpawnShark() {
    const g = this.game, w = g.world, p = g.player;
    for (let tries = 0; tries < 6; tries++) {
      const a = Math.random() * 6.2832, r = 18 + Math.random() * 30;
      const x = Math.floor(p.pos[0] + Math.cos(a) * r), z = Math.floor(p.pos[2] + Math.sin(a) * r);
      if (!w.isLoaded(x, z) || !BPROP[w.biomeAt(x, z)] || !BPROP[w.biomeAt(x, z)].ocean || BPROP[w.biomeAt(x, z)].base === BI.RIVER || BPROP[w.biomeAt(x, z)].base === BI.FROZEN_RIVER) continue;
      let depth = 0; while (depth < 30 && isWaterId(w.getBlock(x, SEA - depth, z))) depth++;
      if (depth < 6) continue;
      this.spawnMob('shark', x + 0.5, SEA - Math.floor(depth * (0.4 + Math.random() * 0.4)) + 0.2, z + 0.5);
      return;
    }
  }

  trySpawn(hostile) {
    const g = this.game, w = g.world, p = g.player;
    for (let tries = 0; tries < 6; tries++) {
      const a = Math.random() * 6.2832, r = 24 + Math.random() * 40;
      const x = Math.floor(p.pos[0] + Math.cos(a) * r), z = Math.floor(p.pos[2] + Math.sin(a) * r);
      if (!w.isLoaded(x, z)) continue;
      let y;
      if (hostile) {
        y = Math.floor(p.pos[1]) + Math.floor((Math.random() - 0.5) * 40);
        // find a floor near y
        let ok = false;
        for (let k = 0; k < 20; k++, y--) if (SOLID[w.getBlock(x, y - 1, z)] && OPAQUE[w.getBlock(x, y - 1, z)] && !w.getBlock(x, y, z) && !w.getBlock(x, y + 1, z)) { ok = true; break; }
        if (!ok) continue;
        const lt = w.getLight(x, y, z);
        const night = g.envObj.day < 0.5;
        const sky = (lt >> 4) * (night ? 0.2 : 1);
        if (Math.random() < 0.15) {
          // slimes: slime chunks below y 40, or swamps at night (y 50..70)
          const b = BPROP[w.biomeAt(x, z)], swamp = b && (b.base === BI.SWAMP || b.base === BI.MANGROVE_SWAMP);
          if ((slimeChunk(x >> 4, z >> 4, w.seed) && y < 40) || (swamp && night && y >= 50 && y <= 70)) {
            const e = this.spawnMob('slime', x + 0.5, y, z + 0.5, { size: [1, 2, 4][Math.random() * 3 | 0] });
            if (entCollides(w, e.pos[0], e.pos[1], e.pos[2], e.w, e.h)) this.mobs.pop();
            return;
          }
        }
        if ((lt & 15) > 7 || sky > 7) continue;
        const type = ['zombie', 'zombie', 'skeleton', 'creeper', 'spider', 'spider', 'enderman'][Math.random() * 7 | 0];
        if (type === 'enderman' && w.getBlock(x, y + 2, z)) continue;
        const e = this.spawnMob(type, x + 0.5, y, z + 0.5);
        if (entCollides(w, e.pos[0], e.pos[1], e.pos[2], e.w, e.h)) this.mobs.pop();
        return;
      } else {
        y = WORLD_MAX_Y - 2;
        while (y > WORLD_MIN_Y && !w.getBlock(x, y, z)) y--;
        if (w.getBlock(x, y, z) !== B.GRASS || (w.getLight(x, y + 1, z) >> 4) < 9) continue;
        const type = ['pig', 'cow', 'sheep', 'chicken'][Math.random() * 4 | 0];
        const n = 2 + (Math.random() * 3 | 0);
        for (let k = 0; k < n; k++) {
          const ox = x + (Math.random() * 5 | 0) - 2, oz = z + (Math.random() * 5 | 0) - 2;
          let oy = y + 3; while (oy > y - 4 && !w.getBlock(ox, oy - 1, oz)) oy--;
          if (w.getBlock(ox, oy - 1, oz) !== B.GRASS || w.getBlock(ox, oy, oz)) continue;
          this.spawnMob(type, ox + 0.5, oy, oz + 0.5);
        }
        return;
      }
    }
  }

  // ray vs mobs; returns {e, t}
  raycastMob(o, dir, maxD) {
    let best = null;
    for (const e of this.mobs) {
      if (e.deathT > 0) continue;
      const hw = e.w / 2;
      const h = rayBox(o[0], o[1], o[2], dir[0], dir[1], dir[2], e.pos[0] - hw, e.pos[1], e.pos[2] - hw, e.pos[0] + hw, e.pos[1] + e.h, e.pos[2] + hw);
      if (h && h.t <= maxD && (!best || h.t < best.t)) best = { e, t: h.t };
    }
    return best;
  }
  damageMob(e, dmg, from) {
    if (e.deathT > 0 || e.hurtT > 0.35) return;
    e.hp -= dmg; e.hurtT = 0.5;
    if (from) {
      const dx = e.pos[0] - from[0], dz = e.pos[2] - from[2], l = Math.hypot(dx, dz) || 1;
      e.vel[0] = dx / l * 7; e.vel[2] = dz / l * 7; e.vel[1] = 5.5;
    }
    if (MOB_DEFS[e.type].passive) e.panic = 5;
    if (e.type === 'spider') e.aggro = true;
    if (e.type === 'enderman' && from) { e.aggro = true; if (e.hp > 0 && Math.random() < 0.35) this.teleport(e, this.game.player.pos); }
    Sfx.hurt();
    if (e.hp <= 0) {
      e.deathT = 0.001;
      for (const [id, n] of MOB_DEFS[e.type].drops(e)) if (id && n > 0) this.dropItem(id, n, e.pos[0], e.pos[1] + 0.5, e.pos[2]);
      if (e.type === 'slime' && e.size > 1) {
        const n = 2 + (Math.random() * 3 | 0);
        for (let k = 0; k < n; k++) {
          const c = this.spawnMob('slime', e.pos[0] + (Math.random() - 0.5) * e.w * 0.5, e.pos[1] + 0.2, e.pos[2] + (Math.random() - 0.5) * e.w * 0.5, { size: e.size / 2 });
          c.vel = [(Math.random() - 0.5) * 3, 3, (Math.random() - 0.5) * 3];
        }
      }
    }
  }

  // ------------------------------------------------------------------ rendering
  render(env, cam) {
    const g = this.game, r = g.r;
    const byTex = new Map(), transTex = new Map();
    const add = (tex, arr, map) => { map = map || byTex; let a = map.get(tex); if (!a) map.set(tex, a = []); a.push(arr); };
    const R2 = (env.renderDist * 16) ** 2;
    for (const e of this.mobs) {
      const dx = e.pos[0] - cam[0], dz = e.pos[2] - cam[2];
      if (dx * dx + dz * dz > R2) continue;
      if (!r.boxVisible(dx - 1.5, e.pos[1] - cam[1] - 0.5, dz - 1.5, dx + 1.5, e.pos[1] - cam[1] + 2.5, dz + 1.5)) continue;
      const li = g.lightAt(e.pos[0], e.pos[1] + e.h * 0.6, e.pos[2], env);
      const tint = e.hurtT > 0 || e.deathT > 0 ? [1, 0.45, 0.45] : e.fuse > 0 && Math.sin(e.fuse * 20) > 0 ? [1.6, 1.6, 1.6] : null;
      const m = MODELS[e.type];
      const v = [], tv = [];
      this.buildModel(v, m, e, cam, li, tint, tv);
      add(m.tex, v);
      if (tv.length) add(m.tex, tv, transTex);
      if (e.type === 'sheep' && !e.sheared) {
        const fv = [], c = sheepTint(e);
        this.buildModel(fv, m.fur, e, cam, li, c ? (tint ? c.map((k, i) => k * tint[i]) : c) : tint);
        add('sheep_fur', fv);
      }
    }
    if (g.camMode) {
      const p = g.player;
      const e = { type: 'player', pos: p.pos, bodyYaw: p.yaw, headYaw: p.yaw, pitch: p.pitch, walk: p.bob * 2.2, walkAmt: p.bobAmt, sneak: p.sneaking, swing: g.swingT };
      const v = [];
      this.buildModel(v, MODELS.player, e, cam, g.lightAt(p.pos[0], p.pos[1] + 1, p.pos[2], env), null);
      add('player', v);
    }
    const gl = r.gl;
    const flush = (map) => {
      for (const [tex, list] of map) {
        const t = r.entityTexture(tex);
        if (!t) continue;
        let n = 0; for (const a of list) n += a.length;
        const all = new Float32Array(n); let o = 0;
        for (const a of list) { all.set(a, o); o += a.length; }
        r.drawEnt(all, n / 10, t, env);
      }
    };
    gl.disable(gl.CULL_FACE);
    flush(byTex);
    if (transTex.size) {
      // translucent shells (slime jelly) after the opaque bodies
      gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA); gl.depthMask(false);
      flush(transTex);
      gl.depthMask(true); gl.disable(gl.BLEND);
    }
    gl.enable(gl.CULL_FACE);
    this.renderItems(env, cam);
  }
  buildModel(out, m, e, cam, li, tint, transOut) {
    const g = this.game;
    const by = e.bodyYaw, cy = Math.cos(by), sy = Math.sin(by);
    let hy = (e.headYaw !== undefined ? e.headYaw : by) - by;
    while (hy > Math.PI) hy -= 6.2832; while (hy < -Math.PI) hy += 6.2832;
    hy = Math.max(-1.2, Math.min(1.2, hy));
    let walkA = Math.cos(e.walk * 3.3) * 1.2 * e.walkAmt;
    if (m.halfSwing) walkA = Math.max(-0.4, Math.min(0.4, walkA * 0.5));
    // model scale: fixed per model, slime size with MC's squish (wide on landing, tall when jumping)
    let S = (m.scale || 1) * (e.size ? e.size * 0.999 : 1), sxz = 1, syy = 1;
    if (e.size) { const f = (e.squish || 0) / (e.size * 0.5 + 1), f1 = 1 / (f + 1); sxz = f1; syy = 1 / f1; }
    const sp = e.swimPitch || 0, cpt = Math.cos(sp), spt = Math.sin(sp), pY = (m.pitchY || 0) / 16 * S;
    const tail = Math.sin(performance.now() / 1000 * (e.type === 'shark' ? 7 : 12)) * (e.type === 'shark' ? 0.3 : 0.25) * (e.inWater === false ? 1.5 : 1);
    const death = e.deathT > 0 ? Math.min(1, e.deathT * 2.5) * Math.PI / 2 : 0;
    const t = performance.now() / 1000;
    const pitch = e.pitch !== undefined ? e.pitch : 0;
    const sw = e.swing ? Math.sin((1 - e.swing) * Math.PI) : 0;
    const sneak = e.sneak ? 1 : 0;
    const scaleY = e.type === 'creeper' && e.fuse > 0 ? 1 + e.fuse * 0.08 : 1;
    for (const part of m.parts) {
      const dst = part.trans && transOut ? transOut : out;
      let rx = 0, ry = 0, rz = 0;
      if (part.rot) { rx = part.rot[0]; ry = part.rot[1]; rz = part.rot[2]; }
      const an = part.anim;
      if (an === 'head') { ry += hy; rx += -pitch; }
      else if (an === 'legA') rx += walkA;
      else if (an === 'legB') rx -= walkA;
      else if (an === 'armR') { rx += (e.type === 'zombie' ? -Math.PI / 2 : -walkA) - sw * 1.4; rz += Math.sin(t * 1.1) * 0.05 + 0.05; }
      else if (an === 'armL') { rx += e.type === 'zombie' ? -Math.PI / 2 : walkA; rz -= Math.sin(t * 1.1) * 0.05 + 0.05; }
      else if (an === 'wingR') rz += e.onGround === false ? Math.sin(t * 30) * 0.8 : 0;
      else if (an === 'wingL') rz -= e.onGround === false ? Math.sin(t * 30) * 0.8 : 0;
      else if (an === 'tail') ry += tail;
      else if (an && an.startsWith('spider')) { const k = +an.slice(-1); ry += Math.sin(e.walk * 3.3 + k * 1.57) * 0.4 * e.walkAmt; rz += Math.abs(Math.cos(e.walk * 3.3 + k)) * 0.2 * e.walkAmt * (an[6] === 'R' ? 1 : -1); }
      const inf = part.inflate || 0;
      const [bx, byy, bz, bw, bh, bd] = part.box;
      // y-up space
      const x0 = -(bx + bw) - inf, x1 = -bx + inf, y0 = -(byy + bh) - inf, y1 = -byy + inf, z0 = bz - inf, z1 = bz + bd + inf;
      const px = -part.pivot[0], py = -(part.pivot[1] - (part.creepy && e.aggro ? 5 : 0)), pz = part.pivot[2];
      const crx = Math.cos(-rx), srx = Math.sin(-rx), cry = Math.cos(-ry), sry = Math.sin(-ry), crz = Math.cos(rz), srz = Math.sin(rz);
      const sneakBody = sneak && m === MODELS.player;
      const M = (x, y, z) => {
        // part rotation Z, Y, X (MC order: Z*Y*X applied to vertex -> X first)
        let y1 = y * crx - z * srx, z1 = y * srx + z * crx; y = y1; z = z1;
        let x1 = x * cry + z * sry; z1 = -x * sry + z * cry; x = x1; z = z1;
        x1 = x * crz - y * srz; y1 = x * srz + y * crz; x = x1; y = y1;
        x += px; y += py; z += pz;
        let wy = (24 + y) / 16 * scaleY * S * syy, wx = x / 16 * S * sxz, wz = z / 16 * S * sxz;
        if (sp) { const yy = wy - pY; const y2 = yy * cpt - wz * spt; wz = yy * spt + wz * cpt; wy = y2 + pY; }
        if (sneakBody) wy -= 0.12;
        if (death) { const yy = wy * Math.cos(death) - wx * Math.sin(death); wx = wy * Math.sin(death) + wx * Math.cos(death); wy = yy; }
        const rx2 = wx * cy - wz * sy, rz2 = wx * sy + wz * cy;
        return [e.pos[0] + rx2 - cam[0], e.pos[1] + wy - cam[1], e.pos[2] + rz2 - cam[2]];
      };
      g.pushModelBox(dst, M, x0, y0, z0, x1, y1, z1, part.uv[0], part.uv[1], bw, bh, bd, m.tw, m.th, li, tint, part.mirror);
    }
  }
  renderItems(env, cam) {
    const g = this.game, r = g.r;
    const list = this.items.concat(this.arrows.map(a => ({ arrow: a, pos: a.pos })));
    if (!list.length) return;
    const v = [];
    const t = performance.now() / 1000;
    for (const it of list) {
      const li = g.lightAt(it.pos[0], it.pos[1] + 0.2, it.pos[2], env);
      if (it.arrow) {
        const a = it.arrow, l = Math.hypot(...a.vel) || 1, d = a.vel.map(x => x / l * 0.5);
        const x = a.pos[0] - cam[0], y = a.pos[1] - cam[1], z = a.pos[2] - cam[2];
        g.pushBox(v, x - 0.03 - Math.max(0, -d[0]), y - 0.03, z - 0.03 - Math.max(0, -d[2]), x + 0.03 + Math.max(0, d[0]), y + 0.03, z + 0.03 + Math.max(0, d[2]), g.assets.layers.oak_planks, [li, li, li, 1]);
        continue;
      }
      const id = it.id;
      const bob = Math.sin(it.age * 2.5) * 0.05 + 0.12;
      const rot = it.age * 1.2;
      const cx = it.pos[0] - cam[0], cy = it.pos[1] + bob - cam[1], cz = it.pos[2] - cam[2];
      const cube = !isItem(id) && (SHAPE[id] === SH.CUBE || SHAPE[id] === SH.SLAB || SHAPE[id] === SH.STAIRS || SHAPE[id] === SH.CHEST || SHAPE[id] === SH.FENCE || SHAPE[id] === SH.WALL || SHAPE[id] === SH.CACTUS);
      const copies = Math.min(3, 1 + Math.floor(it.count / 16));
      for (let k = 0; k < copies; k++) {
        const ox = k * 0.06, oy = k * 0.05;
        if (cube) {
          const s = 0.125, L = [0, 1, 2, 3, 4, 5].map(f => FTEX[id * 6 + f]);
          const tmp = [];
          g.pushBox(tmp, -s, -s, -s, s, s, s, 0, [li, li, li, 1], 0, L, [0.72, 0.72, 1, 0.55, 0.82, 0.82]);
          const c = Math.cos(rot), sn = Math.sin(rot);
          for (let i = 0; i < tmp.length; i += 10) { const x = tmp[i], z = tmp[i + 2]; tmp[i] = x * c - z * sn + cx + ox; tmp[i + 1] += cy + oy; tmp[i + 2] = x * sn + z * c + cz; }
          for (const q of tmp) v.push(q);
        } else {
          const layer = isItem(id) ? g.assets.layers[itemDef(id)[3]] : (SHAPE[id] === SH.TALL || SHAPE[id] === SH.DOOR ? g.assets.layers[TEXNAMES[id][0]] : FTEX[id * 6 + 2]);
          const s = 0.2, c = Math.cos(rot) * s, sn = Math.sin(rot) * s;
          const P = [[-c, -s, -sn, 0, 1], [c, -s, sn, 1, 1], [c, s, sn, 1, 0], [-c, s, -sn, 0, 0]];
          for (const i of [0, 1, 2, 0, 2, 3]) v.push(P[i][0] + cx + ox, P[i][1] + cy + s + oy, P[i][2] + cz, P[i][3], P[i][4], layer, li, li, li, 1);
        }
      }
    }
    void t;
    r.gl.disable(r.gl.CULL_FACE);
    r.drawArr(new Float32Array(v), v.length / 10, env, 0.5);
    r.gl.enable(r.gl.CULL_FACE);
  }
}

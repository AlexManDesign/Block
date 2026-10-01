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
  skeleton: { tex: 'skeleton', tw: 64, th: 32, parts: humanoid(2, false), heldItem: 'item_bow' },
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
  pig: { w: 0.9, h: 0.9, hp: 10, attr: 0.25, tempt: 1.2, food: [IT.CARROT, IT.POTATO], panic: 1.25, drops: () => [[IT.PORKCHOP, 1 + (Math.random() * 3 | 0)]], passive: true },
  cow: { w: 0.9, h: 1.4, hp: 10, attr: 0.2, tempt: 1.25, food: [IT.WHEAT], panic: 2.0, drops: () => [[IT.BEEF, 1 + (Math.random() * 3 | 0)], [IT.LEATHER, Math.random() * 3 | 0]], passive: true },
  sheep: { w: 0.9, h: 1.3, hp: 8, attr: 0.23, tempt: 1.1, food: [IT.WHEAT], panic: 1.25, drops: (e) => [[IT.MUTTON, 1 + (Math.random() * 2 | 0)], [e.sheared ? 0 : sheepWool(e), 1]], passive: true },
  chicken: { w: 0.5, h: 0.7, hp: 4, attr: 0.25, tempt: 1.0, food: [IT.WHEAT_SEEDS, IT.PUMPKIN_SEEDS, IT.MELON_SEEDS], panic: 1.4, drops: () => [[IT.CHICKEN, 1], [IT.FEATHER, Math.random() * 3 | 0]], passive: true },
  zombie: { w: 0.6, h: 1.95, hp: 20, attr: 0.23, dmg: 3, drops: () => [[IT.ROTTEN_FLESH, Math.random() * 3 | 0]], hostile: true, burns: true },
  skeleton: { w: 0.6, h: 1.95, hp: 20, attr: 0.25, dmg: 3, drops: () => [[IT.BONE, Math.random() * 3 | 0], [IT.ARROW, Math.random() * 3 | 0]], hostile: true, burns: true, ranged: true },
  creeper: { w: 0.6, h: 1.6, hp: 20, attr: 0.25, wander: 0.8, drops: () => [[IT.GUNPOWDER, Math.random() * 3 | 0]], hostile: true, creeper: true },
  spider: { w: 1.4, h: 0.9, hp: 16, attr: 0.3, wander: 0.8, dmg: 2, climber: true, neutralLight: 12, drops: () => [[IT.STRING, Math.random() * 3 | 0], [IT.SPIDER_EYE, Math.random() < 0.33 ? 1 : 0]], hostile: true },
  enderman: { w: 0.6, h: 2.9, hp: 40, attr: 0.3, dmg: 7, detect: 64, drops: () => [[IT.ENDER_PEARL, Math.random() * 2 | 0]], hostile: true, neutral: true },
  slime: { w: 1.02, h: 1.02, hp: 4, speed: 2.7, dmg: 2, drops: (e) => [[IT.SLIME_BALL, e.size === 1 ? Math.random() * 3 | 0 : 0]], hostile: true, slime: true },
  salmon: { w: 0.7, h: 0.4, hp: 3, speed: 1.6, drops: (e) => [[e.fire > 0 ? IT.COOKED_SALMON : IT.RAW_SALMON, 1]], passive: true, aquatic: true },
  shark: { w: 0.9, h: 0.8, hp: 24, speed: 4.2, dmg: 6, drops: (e) => [[e.fire > 0 ? IT.COOKED_COD : IT.RAW_COD, 1 + (Math.random() * 2 | 0)], [IT.BONE, Math.random() < 0.35 ? 1 : 0]], hostile: true, aquatic: true },
};
// Ground mobs move like Minecraft's: MoveControl sets both the speed and the forward input to
// modifier * movement-speed attribute, ground drag is 0.546 per tick, so the steady speed is
// (modifier * attribute)^2 * 44.05 blocks/s. speed = that at modifier 1; a goal's modifier m
// therefore scales it by m * m (panic, tempt, wander, follow parent).
for (const k in MOB_DEFS) { const d = MOB_DEFS[k]; if (d.attr) d.speed = 44.05 * d.attr * d.attr; }
const goalSpeed = (speed, m) => speed * m * m;
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

const MOB_DT = 0.05;   // mob simulation step (20 Hz, as in the original)
const PATH_NODES = 900, PATH_RANGE = 28;
const NB8 = [1, 0, -1, 0, 0, 1, 0, -1, 1, 1, 1, -1, -1, 1, -1, -1];
// binary heap on node.f for A*
function heapPush(h, n) {
  h.push(n);
  let i = h.length - 1;
  while (i > 0) { const p = (i - 1) >> 1; if (h[p].f <= h[i].f) break; const t = h[p]; h[p] = h[i]; h[i] = t; i = p; }
}
function heapPop(h) {
  const top = h[0], last = h.pop();
  if (!h.length) return top;
  h[0] = last;
  for (let i = 0, n = h.length; ;) {
    const a = i * 2 + 1, b = a + 1;
    let m = i;
    if (a < n && h[a].f < h[m].f) m = a;
    if (b < n && h[b].f < h[m].f) m = b;
    if (m === i) break;
    const t = h[m]; h[m] = h[i]; h[i] = t; i = m;
  }
  return top;
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
    this.falling = [];
    this.spawnT = 0;
  }
  clear() { this.mobs.length = 0; this.items.length = 0; this.arrows.length = 0; this.falling.length = 0; }

  // ------------------------------------------------------------------ falling blocks
  // Minecraft's FallingBlockEntity: per tick motion.y -= 0.04, move, motion *= 0.98. It passes
  // through blocks without collision (plants, torches, water) and lands on the first solid one; it
  // becomes a block again where the cell is free, otherwise it drops as an item.
  spawnFalling(id, m, x, y, z) {
    this.falling.push({ id, m, x: x + 0.5, z: z + 0.5, y, py: y, vy: 0, t: 0 });
  }
  tickFalling() {
    const w = this.game.world, F = this.falling;
    for (let i = F.length - 1; i >= 0; i--) {
      const f = F[i];
      f.py = f.y;
      const bx = Math.floor(f.x), bz = Math.floor(f.z);
      if (!w.isLoaded(bx, bz)) continue;
      f.t++;
      f.vy = (f.vy - 0.04) * 0.98;
      const ny = f.y + f.vy;
      let landed = false, y = ny;
      for (let cy = Math.ceil(f.y) - 1; cy >= Math.floor(ny); cy--) {
        if (SOLID[w.getBlock(bx, cy, bz)]) { y = cy + 1; landed = true; break; }
      }
      f.y = y;
      if (y < WORLD_MIN_Y - 8 || f.t > 600) { F.splice(i, 1); continue; }
      if (landed) { this.landFalling(f, bx, Math.round(y), bz); F.splice(i, 1); }
    }
  }
  landFalling(f, x, y, z) {
    const w = this.game.world, cur = w.getBlock(x, y, z);
    const free = cur === 0 || SHAPE[cur] === SH.WATER || SHAPE[cur] === SH.LAVA || (FLAGS[cur] & BF_REPLACE);
    if (free) {
      if (cur && (FLAGS[cur] & BF_REPLACE) && SHAPE[cur] !== SH.WATER && SHAPE[cur] !== SH.LAVA) w.breakBlock(x, y, z, true);
      w.setBlock(x, y, z, f.id, f.m);
    } else this.dropItem(f.id, 1, f.x, y + 0.3, f.z);
  }
  // before a save: every falling block lands at once, so none is lost with the world
  settleFalling() {
    const w = this.game.world;
    for (const f of this.falling) {
      const bx = Math.floor(f.x), bz = Math.floor(f.z);
      let y = Math.ceil(f.y);
      while (y > WORLD_MIN_Y && !SOLID[w.getBlock(bx, y - 1, bz)]) y--;
      this.landFalling(f, bx, y, bz);
    }
    this.falling.length = 0;
  }

  spawnMob(type, x, y, z, opts) {
    const d = MOB_DEFS[type];
    const yaw = Math.random() * 6.2832;
    const e = { type, pos: [x, y, z], vel: [0, 0, 0], wish: [0, 0, 0], bodyYaw: yaw, tgtYaw: yaw, headYaw: yaw, hp: d.hp, w: d.w, h: d.h,
      onGround: false, walk: 0, walkAmt: 0, hurtT: 0, deathT: 0, age: 0, fire: 0,
      // behaviour state (see ai / aiFish)
      aiT: Math.random() * 2, tgt: null, fleeT: 0, angryT: 0, kbT: 0, jumpT: 0, atkT: 0, shootT: Math.random(), fuse: -1,
      leapT: 1.5 + Math.random() * 1.5, hopT: Math.random() * 0.8, stuckT: 0, detourT: 0, detX: 0, detZ: 0,
      path: null, pathI: 0, pathT: Math.random() * 0.6, pathTx: 0, pathTz: 0, tpT: 0, stareT: 0, burnT: 0, wetT: 0, lavaT: 0,
      dryT: 0, swimT: 0, huntT: 0, prey: null, esc: null, escT: 0, escD: null, fleeing: false, aggro: false,
      seeT: Math.random() * 0.5, sees: false, unseenT: 0, hunting: false, seenT: 0, strafeT: 0, strafeCw: Math.random() < 0.5, strafeBack: false,
      lookT: 0, lookP: false, lookYaw: 0, loveT: 0, breedCd: 0, baby: false, growT: 0, eatT: 0 };
    if (opts && opts.baby && d.food) { e.baby = true; e.growT = 1200; e.w = d.w * 0.5; e.h = d.h * 0.5; e.persist = true; }
    if (d.slime) {
      const size = (opts && opts.size) || [1, 2, 4][Math.random() * 3 | 0], S = SLIME_SIZES[size];
      e.size = size; e.hp = S[0]; e.w = e.h = 0.51 * size; e.squish = 0; e.tsq = 0; e.hopT = 0.5 + Math.random() * 1.5;
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
      // hostile mobs spawn in creative too (as in Minecraft); they just never target a creative player
      if (hostile < (night ? 22 : 8)) this.trySpawn(true);
      this.fishT = (this.fishT || 0) - 1;
      if (this.fishT <= 0) { this.fishT = 5; if (fish < 10) this.trySpawnFish(); }
      this.sharkT = (this.sharkT || 0) - 1;
      if (this.sharkT <= 0) { this.sharkT = 12; if (sharks < 2) this.trySpawnShark(); }
    }
    // ---- mobs: fixed 20 Hz simulation (as in the original); rendering interpolates between ticks
    this.acc = Math.min((this.acc || 0) + dt, MOB_DT * 8);
    let steps = 0;
    while (this.acc >= MOB_DT && steps < 3) {
      for (const e of this.mobs) this.snapshot(e);
      this.tickMobs(MOB_DT);
      this.tickFalling();
      this.acc -= MOB_DT; steps++;
    }
    if (this.acc > MOB_DT) this.acc = MOB_DT;
    this.alpha = this.acc / MOB_DT;
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
    // ---- arrows: sub-stepped, swept against the player so fast arrows can't pass through
    for (let i = this.arrows.length - 1; i >= 0; i--) {
      const a = this.arrows[i];
      a.age += dt;
      if (a.stuck) {
        // stuck arrows can be picked up (as in the original); they vanish after a minute
        if (!g.surv.dead && Math.hypot(p.pos[0] - a.pos[0], p.pos[1] + 0.9 - a.pos[1], p.pos[2] - a.pos[2]) < 1.5 && g.giveItem(IT.ARROW, 1) === 0) {
          this.arrows.splice(i, 1); Sfx.pop(); continue;
        }
        if (a.age > 60) this.arrows.splice(i, 1);
        continue;
      }
      if (a.age > 8) { this.arrows.splice(i, 1); continue; }
      a.vel[1] -= 20 * dt;
      { const dr = Math.pow(0.99, dt * 20); a.vel[0] *= dr; a.vel[1] *= dr; a.vel[2] *= dr; }
      const n = Math.max(1, Math.ceil(Math.hypot(a.vel[0], a.vel[1], a.vel[2]) * dt / 0.3)), s = dt / n;
      for (let k = 0; k < n; k++) {
        const ox = a.pos[0], oy = a.pos[1], oz = a.pos[2], mx = a.vel[0] * s, my = a.vel[1] * s, mz = a.vel[2] * s;
        if (a.hostile && !g.surv.dead) {
          const hb = rayBox(ox, oy, oz, mx, my, mz, p.pos[0] - 0.3, p.pos[1], p.pos[2] - 0.3, p.pos[0] + 0.3, p.pos[1] + p.h, p.pos[2] + 0.3);
          if (hb && hb.t <= 1) { g.hurt(a.dmg); p.vel[0] += a.vel[0] * 0.15; p.vel[2] += a.vel[2] * 0.15; this.arrows.splice(i, 1); break; }
        }
        if (this.pointSolid(ox + mx, oy + my, oz + mz)) {
          let lo = 0, hi = 1;
          for (let q = 0; q < 6; q++) { const m = (lo + hi) / 2; if (this.pointSolid(ox + mx * m, oy + my * m, oz + mz * m)) hi = m; else lo = m; }
          const l = Math.hypot(a.vel[0], a.vel[1], a.vel[2]) || 1;
          a.dir = [a.vel[0] / l, a.vel[1] / l, a.vel[2] / l];
          a.pos = [ox + mx * hi + a.dir[0] * 0.1, oy + my * hi + a.dir[1] * 0.1, oz + mz * hi + a.dir[2] * 0.1];
          a.vel = [0, 0, 0]; a.stuck = true; a.age = 0;
          break;
        }
        a.pos = [ox + mx, oy + my, oz + mz];
      }
    }
  }
  snapshot(e) {
    if (!e.pp) e.pp = [0, 0, 0];
    e.pp[0] = e.pos[0]; e.pp[1] = e.pos[1]; e.pp[2] = e.pos[2];
    e.pyaw = e.bodyYaw; e.phead = e.headYaw; e.pwalk = e.walk; e.pitchP = e.swimPitch;
  }

  tickMobs(dt) {
    const g = this.game, w = g.world, p = g.player;
    const hunt = g.mode === 'survival' && !g.surv.dead, day = g.envObj.day > 0.55;
    for (let i = this.mobs.length - 1; i >= 0; i--) {
      const e = this.mobs[i], d = MOB_DEFS[e.type];
      e.age += dt;
      const dx = p.pos[0] - e.pos[0], dz = p.pos[2] - e.pos[2], dist = Math.hypot(dx, dz);
      const h = Math.hypot(dx, p.pos[1] + 0.9 - (e.pos[1] + e.h * 0.5), dz);
      if (dist > (d.hostile ? 96 : 140) || !w.isLoaded(Math.floor(e.pos[0]), Math.floor(e.pos[2])) || e.pos[1] < WORLD_MIN_Y - 8) { this.mobs.splice(i, 1); continue; }
      // far hostiles despawn now and then (original: beyond 32 blocks, ~1/25 per second)
      if (d.hostile && h > 32 && !e.persist && Math.random() < dt / 25) { this.mobs.splice(i, 1); continue; }
      if (e.deathT > 0) { e.deathT += dt; if (e.deathT > 1.0) this.mobs.splice(i, 1); continue; }
      if (e.hurtT > 0) e.hurtT -= dt;
      if (e.angryT > 0) e.angryT -= dt;
      if (e.tpT > 0) e.tpT -= dt;
      e.atkT += dt; e.shootT += dt;
      if (e.breedCd > 0) e.breedCd -= dt;
      // lava and fire
      if (e.lavaT > 0) e.lavaT -= dt;
      else {
        const fb = w.getBlock(Math.floor(e.pos[0]), Math.floor(e.pos[1] + 0.3), Math.floor(e.pos[2]));
        if (fb === B.LAVA || fb === B.FIRE) { e.lavaT = 0.7; e.fire = 3; this.damageMob(e, fb === B.LAVA ? 4 : 1, null); if (e.hp <= 0) continue; }
      }
      if (e.fire > 0) e.fire -= dt;
      if (d.aquatic) { this.aiFish(e, d, dt, dx, dz, h); continue; }
      const feet = w.getBlock(Math.floor(e.pos[0]), Math.floor(e.pos[1] + 0.3), Math.floor(e.pos[2]));
      const inW = isWaterId(feet);
      if (inW) e.fire = 0;
      // enderman: provoked by a stare, hates water
      if (e.type === 'enderman') {
        if (e.angryT <= 0 && hunt) {
          if (this.stared(e, h)) { e.stareT += dt; if (e.stareT > 0.25) { e.angryT = 30; e.stareT = 0; } } else e.stareT = 0;
        }
        const body = w.getBlock(Math.floor(e.pos[0]), Math.floor(e.pos[1] + e.h * 0.5), Math.floor(e.pos[2]));
        if (isWaterId(body) || inW) {
          e.wetT -= dt;
          if (e.wetT <= 0) { e.wetT = 0.5; this.damageMob(e, 1, null); if (e.hp <= 0) continue; }
          if (e.tpT <= 0 && this.teleportNear(e, e.pos[0], e.pos[2], 24)) e.tpT = 1;
        }
      }
      e.aggro = e.angryT > 0;
      // undead burn in daylight
      if (d.burns && day && !inW && (w.getLight(Math.floor(e.pos[0]), Math.floor(e.pos[1] + e.h), Math.floor(e.pos[2])) >> 4) >= 14) {
        e.fire = Math.max(e.fire, 0.5);
        e.burnT += dt;
        if (e.burnT >= 1.2) { e.burnT = 0; this.damageMob(e, 2, null); if (e.hp <= 0) continue; }
      }
      this.ai(e, d, dt, dx, dz, h, hunt);
      if (e.deathT > 0 || this.mobs[i] !== e) continue;
      // don't walk off cliffs (drops over 3 blocks) and keep animals out of water
      if (e.onGround && this.cliffAhead(e)) {
        const l = Math.hypot(e.wish[0], e.wish[2]) || 1, sgn = Math.random() < 0.5 ? 1 : -1;
        const px = e.wish[2] / l, pz = -e.wish[0] / l;
        this.stop(e); e.path = null; e.stuckT = 0; e.detourT = 0.5;
        e.detX = e.pos[0] + px * sgn * 3; e.detZ = e.pos[2] + pz * sgn * 3;
      }
      if ((!d.hostile || e.type === 'enderman') && !inW && this.waterAhead(e)) {
        this.stop(e); e.detourT = 0; e.tgt = null; e.aiT = Math.min(e.aiT, 0.5);
      }
      // soft collisions with other mobs and the player
      for (let j = 0; j < this.mobs.length; j++) {
        const o = this.mobs[j];
        if (o === e || o.deathT > 0 || MOB_DEFS[o.type].aquatic) continue;
        const sx = e.pos[0] - o.pos[0], sz = e.pos[2] - o.pos[2], r = (e.w + o.w) * 0.5;
        if (Math.abs(sx) < r && Math.abs(sz) < r && Math.abs(e.pos[1] - o.pos[1]) < 1.5) {
          const l = Math.hypot(sx, sz) || 0.01, push = (r - l) * 4;
          e.vel[0] += sx / l * push; e.vel[2] += sz / l * push;
        }
      }
      { const r = (e.w + PLAYER_W) * 0.5, l = dist || 0.01;
        if (l < r && Math.abs(p.pos[1] - e.pos[1]) < 1.8) { e.vel[0] -= dx / l * (r - l) * 6; e.vel[2] -= dz / l * (r - l) * 6; } }
      const wl = Math.hypot(e.wish[0], e.wish[2]), ox = e.pos[0], oz = e.pos[2], wasGround = e.onGround;
      const fallStart = e.onGround ? e.pos[1] : e.fallY ?? e.pos[1];
      e.fallY = Math.max(fallStart, e.pos[1]);
      this.walkPhysics(e, d, dt, inW);
      if (d.slime && e.onGround && !wasGround) e.tsq = -0.5;
      if (d.slime) { e.squish += (e.tsq - e.squish) * 0.5; e.tsq *= 0.6; }
      if (e.onGround) { const fh = e.fallY - e.pos[1]; if (fh > 3.5 && e.type !== 'chicken' && !d.slime && !inW) this.damageMob(e, Math.floor(fh - 3), null); e.fallY = e.pos[1]; }
      // stuck against something: take a short detour sideways
      if (wl > 0.5) {
        const moved = Math.hypot(e.pos[0] - ox, e.pos[2] - oz);
        if (moved < wl * dt * 0.3) e.stuckT += dt; else e.stuckT = Math.max(0, e.stuckT - dt * 2);
        if (e.stuckT > 0.5 && e.detourT <= 0) {
          e.stuckT = 0; e.detourT = 0.7; e.path = null;
          const sgn = Math.random() < 0.5 ? 1 : -1;
          e.detX = e.pos[0] + e.wish[2] / wl * 5 * sgn; e.detZ = e.pos[2] - e.wish[0] / wl * 5 * sgn;
        }
      } else e.stuckT = 0;
      const hs = Math.hypot(e.vel[0], e.vel[2]);
      e.walk += hs * dt * 1.9;
      e.walkAmt += ((hs > 0.3 ? Math.min(1, hs / 2.2) : 0) - e.walkAmt) * Math.min(1, dt * 8);
    }
  }

  // ------------------------------------------------------------------ land mob behaviour
  ai(e, d, dt, dx, dz, h, hunt) {
    const g = this.game, p = g.player, S = d.slime ? SLIME_SIZES[e.size] : null, speed = S ? S[2] : d.speed;
    e.aiming = false;
    const face = () => { e.headYaw = Math.atan2(dx, -dz); };
    const aggressive = e.angryT > 0 || (d.neutralLight ? this.lightAt(e) < d.neutralLight : !d.neutral);
    if (e.detourT > 0 && !(d.creeper && e.fuse >= 0)) {
      e.detourT -= dt; if (e.fleeT > 0) e.fleeT -= dt;
      this.steer(e, e.detX, e.detZ, goalSpeed(speed, e.fleeT > 0 ? d.panic || 2 : 1));
      e.headYaw = e.bodyYaw;
      return;
    }
    if (e.fleeT > 0) {
      e.fleeT -= dt;
      this.steer(e, e.pos[0] - dx, e.pos[2] - dz, goalSpeed(speed, d.panic || 2));
      e.headYaw = e.bodyYaw;
      return;
    }
    if (d.creeper && e.fuse >= 0) {
      e.fuse += dt; this.stop(e); face();
      if (h > 7) e.fuse = -1;
      else if (e.fuse > 1.5) {
        this.mobs.splice(this.mobs.indexOf(e), 1);
        g.explode(Math.round(e.pos[0]), Math.round(e.pos[1] + 0.5), Math.round(e.pos[2]), 3);
      }
      return;
    }
    const dy = p.pos[1] - e.pos[1];
    if (d.hostile && hunt && h < (d.detect || 16) && aggressive && this.targeting(e, dt)) {
      face();
      if (d.slime) {
        if (e.onGround) {
          e.hopT -= dt;
          if (e.hopT <= 0) { e.hopT = 0.6 + Math.random() * 0.6; this.hop(e, dx, dz, speed); }
          else this.stop(e);
        }
        if (S[1] > 0 && h < e.w * 0.5 + 1.1 && Math.abs(dy) < e.h + 0.6 && e.atkT > 0.5) { e.atkT = 0; this.hitPlayer(e, S[1], dx, dz); }
        return;
      }
      if (e.type === 'spider') {
        this.steer(e, p.pos[0], p.pos[2], speed);
        const hd = Math.hypot(dx, dz);
        if (e.onGround && hd >= 2 && hd <= 4 && Math.random() < 1 - Math.pow(0.8, dt * 20)) {
          const l = hd || 1;
          e.vel[0] = dx / l * 7; e.vel[2] = dz / l * 7; e.vel[1] = 6.5; e.kbT = 0.45;
        }
        if (h < 1.6 && Math.abs(dy) < 2 && e.atkT > 1) { e.atkT = 0; this.hitPlayer(e, d.dmg, dx, dz); }
        return;
      }
      if (e.type === 'enderman') {
        // close in to striking range (the original stopped at 3 but struck only within 2.2)
        // an angry enderman gets Minecraft's attacking speed bonus: attribute 0.3 + 0.15
        if (h > 2) this.goTo(e, p.pos[0], p.pos[2], goalSpeed(speed, 0.45 / 0.3), dt); else this.stop(e);
        if (e.tpT <= 0 && h > 6 && h < 32) {
          e.tpT = 2 + Math.random() * 2;
          const a = Math.random() * 6.2832;
          this.teleportNear(e, p.pos[0] + Math.cos(a) * 3, p.pos[2] + Math.sin(a) * 3, 2);
        }
        if (h < 2.2 && Math.abs(dy) < 3 && e.atkT > 1) { e.atkT = 0; this.hitPlayer(e, d.dmg, dx, dz); }
        return;
      }
      if (d.ranged) {
        e.aiming = true;                    // bow raised (Minecraft's skeleton pose while attacking)
        // Minecraft's bow attack: close in until within 15 blocks and in sight, then strafe around
        // the target (backing off under 3.75), shooting every 2 s after seeing it for a second
        const sees = e.sees;
        e.seenT = sees ? Math.max(0, e.seenT) + dt : Math.min(0, e.seenT) - dt;
        const hd = Math.hypot(dx, dz) || 1;
        if (h > 15 || !sees) { this.goTo(e, p.pos[0], p.pos[2], speed, dt); e.strafeT = 0; }
        else {
          e.strafeT += dt;
          if (e.strafeT >= 1) { if (Math.random() < 0.3) e.strafeCw = !e.strafeCw; if (Math.random() < 0.3) e.strafeBack = !e.strafeBack; e.strafeT = 0; }
          if (h > 11.25) e.strafeBack = false; else if (h < 3.75) e.strafeBack = true;
          const fx = dx / hd, fz = dz / hd, sd = e.strafeCw ? 1 : -1, fb = e.strafeBack ? -1 : 1, sp = speed;
          e.wish[0] = (fx * fb + -fz * sd) * sp; e.wish[2] = (fz * fb + fx * sd) * sp;
          e.path = null;
        }
        if (sees && e.seenT >= 1 && e.shootT >= 2 && h < 15) { e.shootT = 0; this.shootArrow(e); }
        return;
      }
      if (d.creeper) {
        this.goTo(e, p.pos[0], p.pos[2], speed, dt);
        if (h < 3) { e.fuse = 0; Sfx.fuse(); }
        return;
      }
      this.goTo(e, p.pos[0], p.pos[2], speed, dt);
      if (h < 1.4 && Math.abs(dy) < 2 && e.atkT > 1) { e.atkT = 0; this.hitPlayer(e, d.dmg, dx, dz); }
      return;
    }
    this.look(e, dt, dx, dz, h);
    if (!d.hostile && this.animalGoals(e, d, dt, dx, dz, h, speed)) return;
    // wander: every 3-8 s either pick a spot within 10 blocks (Minecraft's random stroll) or rest
    e.aiT -= dt;
    if (e.aiT <= 0) {
      e.aiT = 3 + Math.random() * 5;
      e.tgt = Math.random() < 0.6 ? [e.pos[0] + (Math.random() - 0.5) * 20, e.pos[2] + (Math.random() - 0.5) * 20] : null;
    }
    if (d.slime) {
      if (e.onGround) {
        e.hopT -= dt;
        if (e.hopT <= 0 && e.tgt) {
          e.hopT = 0.8 + Math.random() * 1.2;
          const tx = e.tgt[0] - e.pos[0], tz = e.tgt[1] - e.pos[2];
          if (Math.hypot(tx, tz) > 0.5) this.hop(e, tx, tz, speed * 0.8);
        } else this.stop(e);
      }
      return;
    }
    if (e.tgt) { if (this.goTo(e, e.tgt[0], e.tgt[1], goalSpeed(speed, d.wander || 1), dt)) e.tgt = null; }
    else this.stop(e);
  }
  // Minecraft sensing: a hostile notices the player only in line of sight (checked twice a second)
  // and gives up after 3 s without seeing it; a provoked mob keeps its target
  targeting(e, dt) {
    e.seeT -= dt;
    if (e.seeT <= 0) { e.seeT = 0.5; e.sees = this.canSee(e); }
    if (e.sees) { e.unseenT = 0; e.hunting = true; } else e.unseenT += dt;
    if (e.hunting && e.unseenT > 3 && e.angryT <= 0) e.hunting = false;
    return e.hunting || e.angryT > 0;
  }
  canSee(e) {
    const eye = this.game.player.eye();
    return !this.rayBlocked([e.pos[0], e.pos[1] + e.h * 0.85, e.pos[2]], eye);
  }
  shootArrow(e) {
    // Minecraft: aim at a third of the target's height, lead upwards by 0.2 per block of distance,
    // 1.6 blocks/tick with spread (inaccuracy 6 on normal)
    const p = this.game.player, src = [e.pos[0], e.pos[1] + 1.5, e.pos[2]];
    const dx = p.pos[0] - src[0], dz = p.pos[2] - src[2], dy = p.pos[1] + p.h / 3 - src[1], hd = Math.hypot(dx, dz);
    const v = [dx, dy + hd * 0.2, dz], l = Math.hypot(v[0], v[1], v[2]) || 1;
    const gauss = () => { let u = 0; for (let k = 0; k < 6; k++) u += Math.random(); return (u - 3) / Math.sqrt(0.5); };
    for (let k = 0; k < 3; k++) v[k] = v[k] / l + gauss() * 0.0075 * 6;
    this.arrows.push({ pos: src, vel: [v[0] * 32, v[1] * 32, v[2] * 32], age: 0, hostile: true, dmg: 2 + (Math.random() * 3 | 0) });
  }
  // head: now and then look at a nearby player or around (Minecraft LookAtPlayer / RandomLookAround)
  look(e, dt, dx, dz, h) {
    const chance = 1 - Math.pow(0.98, dt * 20);
    if (e.lookT > 0) {
      e.lookT -= dt;
      e.headYaw = e.lookP ? (h < 8 ? Math.atan2(dx, -dz) : e.bodyYaw) : e.lookYaw;
      return;
    }
    e.headYaw = e.bodyYaw;
    if (h < 8 && Math.random() < chance) { e.lookP = true; e.lookT = 2 + Math.random() * 2; }
    else if (Math.random() < chance) { e.lookP = false; e.lookT = 1 + Math.random(); e.lookYaw = e.bodyYaw + (Math.random() - 0.5) * 3; }
  }
  // animal goals by Minecraft's priorities: breed > tempt > follow parent > eat grass; true = busy
  animalGoals(e, d, dt, dx, dz, h, speed) {
    const g = this.game, p = g.player;
    if (e.loveT > 0) {
      e.loveT -= dt;
      if (Math.random() < dt * 3) g.spawnParticles(e.pos[0] - 0.5, e.pos[1] + e.h, e.pos[2] - 0.5, B.WOOL_RED, 1);
      let mate = null, md = 8;
      for (const o of this.mobs) {
        if (o === e || o.type !== e.type || o.loveT <= 0 || o.baby || o.deathT > 0) continue;
        const l = Math.hypot(o.pos[0] - e.pos[0], o.pos[2] - e.pos[2]);
        if (l < md) { md = l; mate = o; }
      }
      if (mate) {
        e.headYaw = Math.atan2(mate.pos[0] - e.pos[0], -(mate.pos[2] - e.pos[2]));
        if (md < 1.5) this.breed(e, mate);
        else this.goTo(e, mate.pos[0], mate.pos[2], speed, dt);
        return true;
      }
    }
    // tempted by the food in the player's hand (within 10 blocks)
    const held = g.inv[g.sel];
    if (d.food && held && d.food.includes(held.id) && h < 10 && !g.surv.dead) {
      e.headYaw = Math.atan2(dx, -dz);
      if (h > 2.5) this.goTo(e, p.pos[0], p.pos[2], goalSpeed(speed, d.tempt || 1), dt); else this.stop(e);
      return true;
    }
    if (e.baby) {
      e.growT -= dt;
      if (e.growT <= 0) { e.baby = false; e.w = d.w; e.h = d.h; }
      let par = null, pd = 8;
      for (const o of this.mobs) {
        if (o.type !== e.type || o.baby || o.deathT > 0) continue;
        const l = Math.hypot(o.pos[0] - e.pos[0], o.pos[2] - e.pos[2]);
        if (l < pd) { pd = l; par = o; }
      }
      if (par && pd > 3) { this.goTo(e, par.pos[0], par.pos[2], goalSpeed(speed, 1.1), dt); return true; }
    }
    // sheep graze: grass under them turns to dirt and their wool grows back
    if (e.type === 'sheep') {
      if (e.eatT > 0) {
        e.eatT -= dt; this.stop(e);
        if (e.eatT <= 0) {
          const w = g.world, x = Math.floor(e.pos[0]), y = Math.floor(e.pos[1]), z = Math.floor(e.pos[2]);
          const at = w.getBlock(x, y, z), below = w.getBlock(x, y - 1, z);
          if (at === B.TALL_GRASS) w.setBlock(x, y, z, 0, 0);
          else if (below === B.GRASS) w.setBlock(x, y - 1, z, B.DIRT, 0);
          else return true;
          e.sheared = false;
          if (e.baby) e.growT -= 60;
        }
        return true;
      }
      if (e.onGround && Math.random() < 1 - Math.pow(1 - (e.baby ? 1 / 50 : 1 / 1000), dt * 20)) {
        const w = g.world, x = Math.floor(e.pos[0]), y = Math.floor(e.pos[1]), z = Math.floor(e.pos[2]);
        if (w.getBlock(x, y, z) === B.TALL_GRASS || w.getBlock(x, y - 1, z) === B.GRASS) { e.eatT = 2; this.stop(e); return true; }
      }
    }
    return false;
  }
  breed(a, b) {
    const g = this.game;
    a.loveT = b.loveT = 0; a.breedCd = b.breedCd = 300;
    const c = this.spawnMob(a.type, (a.pos[0] + b.pos[0]) / 2, Math.max(a.pos[1], b.pos[1]), (a.pos[2] + b.pos[2]) / 2, { baby: true });
    if (a.type === 'sheep') c.color = Math.random() < 0.5 ? a.color : b.color;
    g.spawnParticles(c.pos[0] - 0.5, c.pos[1] + 0.5, c.pos[2] - 0.5, B.WOOL_RED, 7);
    Sfx.pop();
  }
  // right click with the right food: an adult falls in love, a baby grows 10% faster
  feed(e) {
    const d = MOB_DEFS[e.type];
    if (e.deathT > 0 || !d.food) return false;
    if (e.baby) { e.growT *= 0.9; return true; }
    if (e.loveT > 0 || e.breedCd > 0) return false;
    e.loveT = 30;
    this.game.spawnParticles(e.pos[0] - 0.5, e.pos[1] + e.h, e.pos[2] - 0.5, B.WOOL_RED, 5);
    return true;
  }
  hop(e, dx, dz, speed) {
    const l = Math.hypot(dx, dz) || 1;
    e.wish[0] = dx / l * speed; e.wish[2] = dz / l * speed;
    e.vel[0] = e.wish[0]; e.vel[2] = e.wish[2]; e.vel[1] = 7.4; e.kbT = 0.4;
    e.bodyYaw = e.tgtYaw = Math.atan2(dx, -dz);
    e.tsq = 1; e.onGround = false;
  }
  hitPlayer(e, dmg, dx, dz) {
    const g = this.game, p = g.player, l = Math.hypot(dx, dz) || 1;
    g.hurt(dmg);
    p.vel[0] += dx / l * 4; p.vel[2] += dz / l * 4; p.vel[1] = Math.max(p.vel[1], 4);
  }
  lightAt(e) {
    const lt = this.game.world.getLight(Math.floor(e.pos[0]), Math.floor(e.pos[1] + 0.5), Math.floor(e.pos[2]));
    return Math.max(lt & 15, this.game.envObj.day > 0.55 ? lt >> 4 : 0);
  }
  // the player looks the enderman in the face (original's cone test + line of sight)
  stared(e, h) {
    if (h > 64 || h < 0.5) return false;
    const p = this.game.player, eye = p.eye(), look = p.look();
    const hy = e.pos[1] + e.h * 0.9;
    const vx = e.pos[0] - eye[0], vy = hy - eye[1], vz = e.pos[2] - eye[2], u = Math.hypot(vx, vy, vz) || 1;
    const dot = (vx * look[0] + vy * look[1] + vz * look[2]) / u, r = e.w * 0.6 / u;
    if (dot <= Math.min(0.999, 1 - r * r * 0.5)) return false;
    return !this.rayBlocked(eye, [e.pos[0], hy, e.pos[2]]);
  }

  // ------------------------------------------------------------------ movement
  stop(e) { e.wish[0] = e.wish[1] = e.wish[2] = 0; }
  // steer straight at a point; true once it is reached
  steer(e, tx, tz, speed) {
    const dx = tx - e.pos[0], dz = tz - e.pos[2], l = Math.hypot(dx, dz);
    if (l < 0.4) { e.wish[0] = e.wish[2] = 0; return true; }
    e.wish[0] = dx / l * speed; e.wish[2] = dz / l * speed;
    return false;
  }
  // follow an A* path to (tx, tz); refreshed every 0.6 s or when the goal moved by more than 2
  goTo(e, tx, tz, speed, dt) {
    e.pathT -= dt;
    if (!e.path || e.pathT <= 0 || Math.abs(tx - e.pathTx) + Math.abs(tz - e.pathTz) > 2) {
      e.pathT = 0.6; e.pathTx = tx; e.pathTz = tz; e.path = this.findPath(e, tx, tz); e.pathI = 0;
    }
    const P = e.path;
    if (!P || e.pathI >= P.length) return this.steer(e, tx, tz, speed);
    let wp = P[e.pathI];
    if (Math.hypot(wp[0] - e.pos[0], wp[1] - e.pos[2]) < 0.55) {
      e.pathI++;
      if (e.pathI >= P.length) return this.steer(e, tx, tz, speed);
      wp = P[e.pathI];
    }
    this.steer(e, wp[0], wp[1], speed);
    return false;
  }
  pointSolid(x, y, z) {
    const w = this.game.world, bx = Math.floor(x), by = Math.floor(y), bz = Math.floor(z), id = w.getBlock(bx, by, bz);
    if (!id || !SOLID[id]) return false;
    if (SHAPE[id] === SH.CUBE) return true;
    const boxes = collisionBoxes(w, id, w.getMeta(bx, by, bz), bx, by, bz);
    if (!boxes) return false;
    for (const b of boxes) if (x >= bx + b[0] && x <= bx + b[3] && y >= by + b[1] && y <= by + b[4] && z >= bz + b[2] && z <= bz + b[5]) return true;
    return false;
  }
  hazard(x, y, z) { const id = this.game.world.getBlock(x, y, z); return id === B.LAVA || id === B.FIRE; }
  clear2(x, y, z) { return !this.pointSolid(x + 0.5, y + 0.4, z + 0.5) && !this.pointSolid(x + 0.5, y + 1.4, z + 0.5); }
  standable(x, y, z) {
    return this.pointSolid(x + 0.5, y - 0.2, z + 0.5) && this.clear2(x, y, z) && !this.hazard(x, y, z) && !this.hazard(x, y + 1, z);
  }
  // walkable height in column x,z near y: one step up or a drop of up to 3
  standY(x, y, z) {
    for (let t = 1; t >= -3; t--) if (this.standable(x, y + t, z)) return y + t;
    return null;
  }
  // A* over block columns (8 directions, no corner cutting), at most PATH_NODES nodes within
  // PATH_RANGE blocks (the original: 320 / 22; wider here so mobs find the way around long walls).
  // Returns waypoints to the goal, or to the closest reachable node when the goal is out of reach.
  findPath(e, tx, tz) {
    const sx = Math.floor(e.pos[0]), sy = Math.floor(e.pos[1] + 0.1), sz = Math.floor(e.pos[2]);
    const gx = Math.floor(tx), gz = Math.floor(tz);
    if (sx === gx && sz === gz) return null;
    const oct = (ax, az) => { ax = ax < 0 ? -ax : ax; az = az < 0 ? -az : az; return ax > az ? ax - az + Math.SQRT2 * az : az - ax + Math.SQRT2 * ax; };
    const key = (x, z) => (x - sx + 64) * 256 + (z - sz + 64);
    const nodes = new Map(), heap = [], memo = new Map();
    const stand = (x, y, z) => {
      const k = ((x - sx + 64) * 256 + (z - sz + 64)) * 1024 + (y - sy + 512);
      let v = memo.get(k);
      if (v === undefined) { v = this.standY(x, y, z); memo.set(k, v); }
      return v;
    };
    const start = { x: sx, y: sy, z: sz, from: null, g: 0, f: oct(sx - gx, sz - gz), done: false };
    nodes.set(key(sx, sz), start); heapPush(heap, start);
    let best = start, bestH = start.f, n = 0;
    while (heap.length && n < PATH_NODES) {
      const P = heapPop(heap);
      if (P.done) continue;
      P.done = true; n++;
      if (P.x === gx && P.z === gz) { best = P; break; }
      for (let k = 0; k < 8; k++) {
        const bx = NB8[k * 2], bz = NB8[k * 2 + 1], hx = P.x + bx, hz = P.z + bz;
        if (Math.abs(hx - sx) > PATH_RANGE || Math.abs(hz - sz) > PATH_RANGE) continue;
        const y = stand(hx, P.y, hz);
        if (y === null) continue;
        const diag = bx !== 0 && bz !== 0;
        if (diag && (y !== P.y || !this.clear2(P.x + bx, P.y, P.z) || !this.clear2(P.x, P.y, P.z + bz))) continue;
        const gc = P.g + (diag ? Math.SQRT2 : 1), kk = key(hx, hz), O = nodes.get(kk);
        if (O) {
          if (O.done || gc >= O.g) continue;
          O.g = gc; O.from = P; O.y = y; O.f = gc + oct(hx - gx, hz - gz); heapPush(heap, O);
          continue;
        }
        const hh = oct(hx - gx, hz - gz), N = { x: hx, y, z: hz, from: P, g: gc, f: gc + hh, done: false };
        nodes.set(kk, N);
        if (hh < bestH) { bestH = hh; best = N; }
        heapPush(heap, N);
      }
    }
    if (best === start) return null;
    const out = [];
    for (let q = best; q && q.from; q = q.from) out.push([q.x + 0.5, q.z + 0.5]);
    out.reverse();
    return out;
  }
  // a drop of more than 3 blocks right ahead
  cliffAhead(e) {
    const l = Math.hypot(e.wish[0], e.wish[2]);
    if (l < 0.2) return false;
    const x = Math.floor(e.pos[0] + e.wish[0] / l * 0.8), z = Math.floor(e.pos[2] + e.wish[2] / l * 0.8), y = Math.floor(e.pos[1] + 0.1);
    for (let a = 0; a <= 3; a++) if (this.pointSolid(x + 0.5, y - a - 0.2, z + 0.5)) return false;
    return true;
  }
  // water (or lava) right ahead, at feet level or just below
  waterAhead(e) {
    const l = Math.hypot(e.wish[0], e.wish[2]);
    if (l < 0.2) return false;
    const w = this.game.world, x = Math.floor(e.pos[0] + e.wish[0] / l * 0.9), z = Math.floor(e.pos[2] + e.wish[2] / l * 0.9), y = Math.floor(e.pos[1] + 0.3);
    const wet = id => isWaterId(id) || id === B.LAVA, a = w.getBlock(x, y, z);
    return wet(a) || (!a && (wet(w.getBlock(x, y - 1, z)) || wet(w.getBlock(x, y - 2, z))));
  }
  // move along one axis; on contact the position is refined to touch the obstacle. true = blocked
  moveAxis(e, ax, d) {
    if (!d) return false;
    const w = this.game.world, n = Math.max(1, Math.ceil(Math.abs(d) / 0.4)), s = d / n, p = e.pos;
    for (let i = 0; i < n; i++) {
      const o = p[ax];
      p[ax] = o + s;
      if (!entCollides(w, p[0], p[1], p[2], e.w, e.h)) continue;
      let lo = 0, hi = 1;
      for (let k = 0; k < 6; k++) { const m = (lo + hi) / 2; p[ax] = o + s * m; if (entCollides(w, p[0], p[1], p[2], e.w, e.h)) hi = m; else lo = m; }
      p[ax] = o + s * lo;
      return true;
    }
    return false;
  }
  // walking physics (original): steer velocity toward the wish, gravity, auto jump onto one
  // block steps, climbing spiders, turning at most 8 rad/s
  walkPhysics(e, d, dt, inW) {
    const w = this.game.world;
    if (e.jumpT > 0) e.jumpT -= dt;
    if (e.kbT > 0) e.kbT -= dt;
    if (inW) e.vel[1] += (2.5 - e.vel[1]) * Math.min(1, dt * 3);
    else {
      e.vel[1] -= 23 * dt;
      if (e.vel[1] < -40) e.vel[1] = -40;
      if (e.type === 'chicken' && e.vel[1] < -2.5) e.vel[1] = -2.5;   // chickens flutter down
    }
    const k = e.kbT > 0 ? 1.5 : e.onGround || inW ? 10 : 2.5, a = Math.min(1, dt * k);
    e.vel[0] += (e.wish[0] - e.vel[0]) * a; e.vel[2] += (e.wish[2] - e.vel[2]) * a;
    const wl = Math.hypot(e.wish[0], e.wish[2]);
    if (wl > 0.2) e.tgtYaw = Math.atan2(e.wish[0], -e.wish[2]);
    let dy = e.tgtYaw - e.bodyYaw;
    while (dy > Math.PI) dy -= 6.2832; while (dy < -Math.PI) dy += 6.2832;
    const tr = dt * 8; e.bodyYaw += dy > tr ? tr : dy < -tr ? -tr : dy;
    const vy = e.vel[1] * dt;
    if (this.moveAxis(e, 1, vy)) { if (vy < 0) e.onGround = true; e.vel[1] = 0; }
    else if (vy !== 0) e.onGround = false;
    let blocked = false;
    for (const ax of [0, 2]) {
      const dd = e.vel[ax] * dt;
      if (!dd) continue;
      const tx = e.pos[0] + (ax === 0 ? dd : 0), tz = e.pos[2] + (ax === 2 ? dd : 0);
      if (!this.moveAxis(e, ax, dd)) continue;
      if (e.onGround && e.jumpT <= 0 && e.vel[1] <= 0 && !entCollides(w, tx, e.pos[1] + 1.01, tz, e.w, e.h)) { e.vel[1] = 8.2; e.jumpT = 0.5; e.onGround = false; }
      else blocked = true;
    }
    e.climbing = false;
    if (d.climber && blocked && wl > 0.3 && !inW) {
      e.climbing = true;
      if (!this.moveAxis(e, 1, 3.4 * dt)) { e.vel[1] = 0; e.onGround = false; }
    }
    e.hitWall = blocked;
  }

  // ------------------------------------------------------------------ fish (original yp / pf)
  aiFish(e, d, dt, dx, dz, h) {
    const g = this.game, w = g.world, p = g.player;
    const water = (x, y, z) => isWaterId(w.getBlock(Math.floor(x), Math.floor(y), Math.floor(z)));
    e.inWater = water(e.pos[0], e.pos[1] + 0.05, e.pos[2]);
    if (!e.inWater) {
      // on land: flop and suffocate
      e.dryT += dt;
      if (e.dryT >= 1) { e.dryT -= 1; this.damageMob(e, 1, null); if (e.hp <= 0) return; }
      e.hopT -= dt;
      if (e.onGround && e.hopT <= 0) {
        e.hopT = 0.5 + Math.random() * 0.5;
        const a = Math.random() * 6.2832;
        e.vel[1] = 2.6; e.vel[0] = Math.cos(a) * 0.7; e.vel[2] = Math.sin(a) * 0.7; e.bodyYaw = e.tgtYaw = a;
      }
      this.stop(e); this.fishPhysics(e, d, dt);
      return;
    }
    e.dryT = 0;
    if (e.fleeT > 0) e.fleeT -= dt;
    e.swimT -= dt;
    if (d.hostile) {
      // sharks hunt the player in water, or other fish
      e.huntT -= dt;
      if (e.huntT <= 0) { e.huntT = 0.4; e.prey = this.findPrey(e, h); }
      let q = e.prey;
      if (q && q !== 'player' && (q.hp <= 0 || q.deathT > 0 || this.mobs.indexOf(q) < 0)) q = e.prey = null;
      if (q) {
        const pl = q === 'player', tx = pl ? p.pos[0] : q.pos[0], ty = pl ? p.pos[1] + 0.4 : q.pos[1], tz = pl ? p.pos[2] : q.pos[2];
        const vx = tx - e.pos[0], vy = ty - e.pos[1], vz = tz - e.pos[2], l = Math.hypot(vx, vy, vz) || 1;
        e.wish[0] = vx / l * d.speed; e.wish[1] = vy / l * d.speed * 0.6; e.wish[2] = vz / l * d.speed;
        e.fleeing = false; e.swimT = 0.5;
        if (l < e.w * 0.5 + (pl ? 1.1 : 0.9) && e.atkT > 1.2) {
          e.atkT = 0;
          if (pl) this.hitPlayer(e, d.dmg, p.pos[0] - e.pos[0], p.pos[2] - e.pos[2]); else this.damageMob(q, d.dmg, null);
        }
        if (!water(e.pos[0], e.pos[1] + 0.9, e.pos[2])) e.wish[1] = Math.min(e.wish[1], -0.15);
        this.fishPhysics(e, d, dt);
        return;
      }
    }
    if (!d.hostile && (h < 8 || e.fleeT > 0)) {
      // escape: a reachable water spot farther from the player
      e.escT -= dt;
      let need = !e.esc || e.escT <= 0 || Math.hypot(e.esc[0] - e.pos[0], e.esc[2] - e.pos[2]) < 1.5;
      if (!need && e.esc) {
        const ax = e.pos[0] - p.pos[0], az = e.pos[2] - p.pos[2], al = Math.hypot(ax, az) || 1;
        const bx = e.esc[0] - e.pos[0], bz = e.esc[2] - e.pos[2], bl = Math.hypot(bx, bz) || 1;
        if (Math.hypot(e.esc[0] - p.pos[0], e.esc[2] - p.pos[2]) < al || (bx / bl) * (ax / al) + (bz / bl) * (az / al) < -0.15) need = true;
      }
      if (need) {
        const t = this.escapeSpot(e, p.pos[0], p.pos[2]);
        if (t) { const l = Math.hypot(t[0] - e.pos[0], t[2] - e.pos[2]) || 1; e.escD = [(t[0] - e.pos[0]) / l, (t[2] - e.pos[2]) / l]; e.esc = t; e.escT = 8; }
        else { e.esc = null; e.escT = 0.6; }
      }
      if (e.esc) {
        const sp = d.speed * (e.fleeT > 0 ? 3.8 : 3.4), vx = e.esc[0] - e.pos[0], vy = e.esc[1] - e.pos[1], vz = e.esc[2] - e.pos[2], l = Math.hypot(vx, vy, vz) || 1;
        e.wish[0] = vx / l * sp; e.wish[1] = vy / l * sp * 0.5; e.wish[2] = vz / l * sp;
        e.fleeing = true; e.swimT = 0.5;
      } else e.fleeing = false;
    } else if (e.fleeing) { e.fleeing = false; e.esc = null; e.escT = 0; e.escD = null; e.swimT = 0; }
    if (!e.fleeing && e.swimT <= 0) {
      e.escD = null;
      e.swimT = 2.5 + Math.random() * 2.5;
      if (Math.random() < 0.35) {
        // drift slowly in the current direction
        const b = Math.hypot(e.wish[0], e.wish[2]), a = b > 0.01 ? Math.atan2(e.wish[0], e.wish[2]) : Math.random() * 6.2832;
        e.wish[0] = Math.sin(a) * d.speed * 0.2; e.wish[2] = Math.cos(a) * d.speed * 0.2; e.wish[1] = 0;
      } else {
        const a = Math.random() * 6.2832;
        e.wish[0] = Math.cos(a) * d.speed; e.wish[2] = Math.sin(a) * d.speed; e.wish[1] = (Math.random() - 0.5) * d.speed * 0.2;
        // schooling: toward the group, away from fish that are too close
        let cx = 0, cz = 0, n = 0, sx = 0, sz = 0;
        for (const o of this.mobs) {
          if (o === e || o.type !== e.type) continue;
          const l = Math.hypot(o.pos[0] - e.pos[0], o.pos[1] - e.pos[1], o.pos[2] - e.pos[2]);
          if (l > 8 || l < 0.001) continue;
          cx += o.pos[0]; cz += o.pos[2]; n++;
          if (l < 1.8) { sx += (e.pos[0] - o.pos[0]) / l; sz += (e.pos[2] - o.pos[2]) / l; }
        }
        if (n) { const ax = cx / n - e.pos[0], az = cz / n - e.pos[2], al = Math.hypot(ax, az); if (al > 4) { e.wish[0] += ax / al * d.speed * 0.4; e.wish[2] += az / al * d.speed * 0.4; } }
        const sl = Math.hypot(sx, sz);
        if (sl > 0) { e.wish[0] += sx / sl * d.speed * 0.8; e.wish[2] += sz / sl * d.speed * 0.8; }
      }
    }
    // stay below the surface and off the bottom
    if (water(e.pos[0], e.pos[1] + 0.9, e.pos[2])) { if (!water(e.pos[0], e.pos[1] - 0.6, e.pos[2])) e.wish[1] = Math.max(e.wish[1], 0.15); }
    else e.wish[1] = Math.min(e.wish[1], -0.15);
    this.fishPhysics(e, d, dt);
  }
  findPrey(e, h) {
    const g = this.game, det = 20;
    if (h < det && g.player.inWater && g.mode === 'survival' && !g.surv.dead) return 'player';
    let best = null, bd = det;
    for (const o of this.mobs) {
      if (o === e || !MOB_DEFS[o.type].aquatic || MOB_DEFS[o.type].hostile || o.hp <= 0 || o.deathT > 0) continue;
      const l = Math.hypot(o.pos[0] - e.pos[0], o.pos[1] - e.pos[1], o.pos[2] - e.pos[2]);
      if (l < bd) { bd = l; best = o; }
    }
    return best;
  }
  // water spot that is farther from the threat, reachable in a straight line through water
  escapeSpot(e, px, pz) {
    const w = this.game.world, water = (x, y, z) => isWaterId(w.getBlock(Math.floor(x), Math.floor(y), Math.floor(z)));
    const d0 = Math.hypot(e.pos[0] - px, e.pos[2] - pz), ed = e.escD || [0, 0];
    let ax = e.pos[0] - px, az = e.pos[2] - pz; const al = Math.hypot(ax, az) || 1; ax /= al; az /= al;
    const lineWet = (x, y, z) => {
      const l = Math.hypot(x - e.pos[0], y - e.pos[1], z - e.pos[2]), n = Math.max(2, Math.ceil(l / 0.8));
      for (let k = 1; k <= n; k++) { const t = k / n; if (!water(e.pos[0] + (x - e.pos[0]) * t, e.pos[1] + 0.2 + (y - e.pos[1]) * t, e.pos[2] + (z - e.pos[2]) * t)) return false; }
      return true;
    };
    let best = null, bs = 0;
    for (let pass = 0; pass < 2 && !best; pass++) {
      const r0 = pass === 0 ? 6 : 2, r1 = pass === 0 ? 16 : 6;
      for (let k = 0; k < 12; k++) {
        const a = Math.random() * 6.2832, r = r0 + Math.random() * (r1 - r0);
        const x = e.pos[0] + Math.cos(a) * r, z = e.pos[2] + Math.sin(a) * r, y = e.pos[1] + (Math.random() - 0.5) * 4;
        if (!water(x, y, z)) continue;
        const gain = Math.hypot(x - px, z - pz) - d0;
        if (gain <= 0) continue;
        const tl = Math.hypot(x - e.pos[0], z - e.pos[2]) || 1, ux = (x - e.pos[0]) / tl, uz = (z - e.pos[2]) / tl;
        if (ux * ax + uz * az < -0.15 || !lineWet(x, y, z)) continue;
        const open = (water(x + 2, y, z) ? 1 : 0) + (water(x - 2, y, z) ? 1 : 0) + (water(x, y, z + 2) ? 1 : 0) + (water(x, y, z - 2) ? 1 : 0);
        const s = gain + open * 1.5 + (ux * ed[0] + uz * ed[1]) * 4;
        if (s > bs) { bs = s; best = [x, y, z]; }
      }
    }
    if (best) return best;
    // fall back: the longest open water line in 16 directions
    let fb = null, fs = 0;
    for (let k = 0; k < 16; k++) {
      const a = k * Math.PI / 8, c = Math.cos(a), s = Math.sin(a);
      let b = 0;
      for (let r = 1; r <= 8 && water(e.pos[0] + c * r, e.pos[1] + 0.2, e.pos[2] + s * r); r++) b = r;
      if (!b || c * ax + s * az < -0.15) continue;
      const x = e.pos[0] + c * b, z = e.pos[2] + s * b, sc = b + (Math.hypot(x - px, z - pz) - d0) * 2 + (c * ed[0] + s * ed[1]) * 4;
      if (sc > fs) { fs = sc; fb = [x, e.pos[1], z]; }
    }
    return fb;
  }
  fishPhysics(e, d, dt) {
    const w = this.game.world, water = (x, y, z) => isWaterId(w.getBlock(Math.floor(x), Math.floor(y), Math.floor(z)));
    e.inWater = water(e.pos[0], e.pos[1] + 0.05, e.pos[2]);
    if (e.inWater) {
      const a = Math.min(1, dt * (e.fleeing ? 10 : 4));
      for (let k = 0; k < 3; k++) e.vel[k] += (e.wish[k] - e.vel[k]) * a;
    } else {
      e.vel[1] -= 23 * dt;
      const f = Math.max(0, 1 - dt * 4); e.vel[0] *= f; e.vel[2] *= f;
    }
    const sp = Math.hypot(e.vel[0], e.vel[1], e.vel[2]);
    if (sp > 0.15) {
      e.tgtYaw = Math.atan2(e.vel[0], -e.vel[2]);
      const pt = Math.atan2(e.vel[1], Math.hypot(e.vel[0], e.vel[2]));
      e.swimPitch += (pt - e.swimPitch) * Math.min(1, dt * 4);
    }
    if (!e.inWater) e.swimPitch += (0 - e.swimPitch) * Math.min(1, dt * 4);
    let dy = e.tgtYaw - e.bodyYaw;
    while (dy > Math.PI) dy -= 6.2832; while (dy < -Math.PI) dy += 6.2832;
    const tr = dt * 4; e.bodyYaw += dy > tr ? tr : dy < -tr ? -tr : dy;
    e.headYaw = e.bodyYaw;
    // in water a fish only moves to spots that are water too (it bounces off the edge)
    const ok = (x, y, z) => !entCollides(w, x, y, z, e.w, e.h) && (!e.inWater || water(x, y + 0.05, z));
    const nx = e.pos[0] + e.vel[0] * dt;
    if (ok(nx, e.pos[1], e.pos[2])) e.pos[0] = nx; else { e.vel[0] = 0; e.wish[0] = -e.wish[0]; }
    const nz = e.pos[2] + e.vel[2] * dt;
    if (ok(e.pos[0], e.pos[1], nz)) e.pos[2] = nz; else { e.vel[2] = 0; e.wish[2] = -e.wish[2]; }
    const vy = e.vel[1] * dt;
    if (ok(e.pos[0], e.pos[1] + vy, e.pos[2])) { e.pos[1] += vy; e.onGround = false; }
    else if (vy <= 0) { this.moveAxis(e, 1, vy); e.vel[1] = 0; e.onGround = true; }
    else e.vel[1] = 0;
    e.walk += dt * (0.6 + sp * 0.5);
    e.walkAmt = 0;
  }

  rayBlocked(a, b) {
    const w = this.game.world, n = Math.ceil(Math.hypot(b[0] - a[0], b[1] - a[1], b[2] - a[2]) * 2);
    for (let i = 1; i < n; i++) {
      const t = i / n, id = w.getBlock(Math.floor(a[0] + (b[0] - a[0]) * t), Math.floor(a[1] + (b[1] - a[1]) * t), Math.floor(a[2] + (b[2] - a[2]) * t));
      if (id && OPAQUE[id]) return true;
    }
    return false;
  }
  // enderman teleport (original): up to 32 random spots within r of (cx, cz), scanning 8 up to 16
  // down for dry ground with room for the body
  teleportNear(e, cx, cz, r) {
    const w = this.game.world;
    for (let k = 0; k < 32; k++) {
      const x = Math.floor(cx + (Math.random() - 0.5) * 2 * r) + 0.5, z = Math.floor(cz + (Math.random() - 0.5) * 2 * r) + 0.5;
      if (!w.isLoaded(Math.floor(x), Math.floor(z))) continue;
      for (let s = 8; s >= -16; s--) {
        const y = Math.floor(e.pos[1]) + s;
        if (y < WORLD_MIN_Y + 2 || y > WORLD_MAX_Y - 4 || !this.pointSolid(x, y - 0.2, z) || entCollides(w, x, y, z, e.w, e.h)) continue;
        const b = w.getBlock(Math.floor(x), y, Math.floor(z));
        if (isWaterId(b) || b === B.LAVA) continue;
        this.game.spawnParticles(e.pos[0] - 0.5, e.pos[1] + 0.8, e.pos[2] - 0.5, B.OBSIDIAN, 8);
        e.pos[0] = x; e.pos[1] = y; e.pos[2] = z; e.vel[0] = e.vel[1] = e.vel[2] = 0; e.path = null; e.fallY = y;
        if (e.pp) { e.pp[0] = x; e.pp[1] = y; e.pp[2] = z; }
        this.game.spawnParticles(x - 0.5, y + 0.8, z - 0.5, B.OBSIDIAN, 8);
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
        if ((lt & 15) > 0 || sky > 7) continue;   // Minecraft 1.18: monsters need block light 0
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
    if (e.deathT > 0 || e.hurtT > 0.3) return;
    const d = MOB_DEFS[e.type];
    e.hp -= dmg; e.hurtT = 0.4;
    if (from) {
      const dx = e.pos[0] - from[0], dz = e.pos[2] - from[2], l = Math.hypot(dx, dz) || 1;
      e.vel[0] += dx / l * 6; e.vel[2] += dz / l * 6; e.vel[1] = 4.5; e.kbT = 0.3; e.onGround = false;
    }
    if (!d.hostile) e.fleeT = 4;
    if (d.aquatic) { e.esc = null; e.escT = 0; }
    if (e.hp > 0 && from && (d.neutral || d.neutralLight)) e.angryT = 30;
    if (e.hp > 0 && e.type === 'enderman' && Math.random() < 0.5 && this.teleportNear(e, e.pos[0], e.pos[2], 24)) e.tpT = 1;
    Sfx.hurt();
    if (e.hp <= 0) {
      e.deathT = 0.001;
      for (const [id, n] of d.drops(e)) if (id && n > 0) this.dropItem(id, n, e.pos[0], e.pos[1] + 0.5, e.pos[2]);
      if (e.type === 'slime' && e.size > 1) {
        const n = 2 + (Math.random() * 3 | 0), r = e.w * 0.4;
        for (let k = 0; k < n; k++) {
          const a = k / n * 6.2832 + Math.random() * 0.5;
          const c = this.spawnMob('slime', e.pos[0] + Math.cos(a) * r, e.pos[1] + 0.1, e.pos[2] + Math.sin(a) * r, { size: e.size / 2 });
          c.vel = [Math.cos(a) * 2.5, 3, Math.sin(a) * 2.5]; c.angryT = e.angryT;
        }
      }
    }
  }

  // ------------------------------------------------------------------ rendering
  render(env, cam) {
    const g = this.game, r = g.r;
    const byTex = new Map(), transTex = new Map();
    const add = (tex, arr, map) => { map = map || byTex; let a = map.get(tex); if (!a) map.set(tex, a = []); a.push(arr); };
    const R2 = (env.renderDist * 16) ** 2, al = this.alpha ?? 1, fireV = [];
    const lerpA = (a, b) => { let d = b - a; while (d > Math.PI) d -= 6.2832; while (d < -Math.PI) d += 6.2832; return a + d * al; };
    for (const e of this.mobs) {
      // state between the last two simulation ticks
      const P = e.rpos || (e.rpos = [0, 0, 0]);
      if (e.pp) {
        for (let k = 0; k < 3; k++) P[k] = e.pp[k] + (e.pos[k] - e.pp[k]) * al;
        e.ryaw = lerpA(e.pyaw, e.bodyYaw); e.rhead = lerpA(e.phead, e.headYaw); e.rwalk = e.pwalk + (e.walk - e.pwalk) * al;
        e.rpitch = e.pitchP !== undefined ? e.pitchP + ((e.swimPitch || 0) - e.pitchP) * al : e.swimPitch;
      } else { P[0] = e.pos[0]; P[1] = e.pos[1]; P[2] = e.pos[2]; e.ryaw = e.bodyYaw; e.rhead = e.headYaw; e.rwalk = e.walk; e.rpitch = e.swimPitch; }
      const dx = P[0] - cam[0], dz = P[2] - cam[2], rr = Math.max(2, e.w * 0.5 + 1);
      if (dx * dx + dz * dz > R2) continue;
      if (!r.boxVisible(dx - rr, P[1] - cam[1] - 0.5, dz - rr, dx + rr, P[1] - cam[1] + e.h + 0.5, dz + rr)) continue;
      const li = g.lightAt(P[0], P[1] + e.h * 0.6, P[2], env);
      if (e.fire > 0 && !e.inWater) this.flame(fireV, P, e.w, e.h, cam);
      const tint = e.hurtT > 0 || e.deathT > 0 ? [1, 0.45, 0.45] : e.fuse > 0 && Math.sin(e.fuse * 20) > 0 ? [1.6, 1.6, 1.6] : null;
      const m = MODELS[e.type];
      const v = [], tv = [];
      this.buildModel(v, m, e, cam, li, tint, tv, fireV);
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
        const all = r.scratch(n); let o = 0;
        for (const a of list) { all.set(a, o); o += a.length; }
        r.drawEnt(all.subarray(0, n), n / 10, t, env);
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
    if (fireV.length) {
      gl.disable(gl.CULL_FACE);
      r.drawArr(r.f32(fireV), fireV.length / 10, env, 0.5);
      gl.enable(gl.CULL_FACE);
    }
    this.renderItems(env, cam);
  }
  // Minecraft's flame on a burning entity: camera-facing fire quads 1.4 x the entity width, stacked
  // every 0.45 (in those units) up to its height, each 10% narrower and a little nearer the camera,
  // mirrored every second pair; drawn at full brightness
  flame(v, P, w, h, cam) {
    const layer = this.game.assets.layers.fire;
    const s = w * 1.4, cx = P[0] - cam[0], cy = P[1] - cam[1], cz = P[2] - cam[2];
    const l = Math.hypot(cx, cz) || 1, rx = -cz / l, rz = cx / l, fx = -cx / l, fz = -cz / l;   // right, towards camera
    let hw = 0.5, y0 = 0, depth = 0.3, left = h / s, i = 0;
    while (left > 0) {
      const flip = ((i >> 1) & 1) === 1, u0 = flip ? 1 : 0, u1 = flip ? 0 : 1;
      const ox = cx + fx * depth * s, oz = cz + fz * depth * s;
      const X = (a) => ox + rx * a * s, Z = (a) => oz + rz * a * s, Y = (b) => cy + b * s;
      const q = [[X(hw), Y(y0), Z(hw), u1, 1], [X(-hw), Y(y0), Z(-hw), u0, 1], [X(-hw), Y(y0 + 1.4), Z(-hw), u0, 0], [X(hw), Y(y0 + 1.4), Z(hw), u1, 0]];
      for (const k of [0, 1, 2, 0, 2, 3]) v.push(q[k][0], q[k][1], q[k][2], q[k][3], q[k][4], layer, 1, 1, 1, 1);
      left -= 0.45; y0 += 0.45; hw *= 0.9; depth += 0.03; i++;
    }
  }
  buildModel(out, m, e, cam, li, tint, transOut, heldOut) {
    const g = this.game;
    // interpolated state for mobs (rpos / ryaw / ...), the raw one for the player model
    const EP = e.rpos || e.pos, by = e.ryaw !== undefined ? e.ryaw : e.bodyYaw, cy = Math.cos(by), sy = Math.sin(by);
    let hy = (e.rhead !== undefined ? e.rhead : e.headYaw !== undefined ? e.headYaw : by) - by;
    while (hy > Math.PI) hy -= 6.2832; while (hy < -Math.PI) hy += 6.2832;
    hy = Math.max(-1.2, Math.min(1.2, hy));
    let walkA = Math.cos((e.rwalk !== undefined ? e.rwalk : e.walk) * 3.3) * 1.2 * e.walkAmt;
    if (m.halfSwing) walkA = Math.max(-0.4, Math.min(0.4, walkA * 0.5));
    // model scale: fixed per model, slime size with MC's squish (wide on landing, tall when jumping)
    let S = (m.scale || 1) * (e.size ? e.size * 0.999 : 1) * (e.baby ? 0.5 : 1), sxz = 1, syy = 1;
    if (e.size) { const f = (e.squish || 0) / (e.size * 0.5 + 1), f1 = 1 / (f + 1); sxz = f1; syy = 1 / f1; }
    const sp = (e.rpitch !== undefined ? e.rpitch : e.swimPitch) || 0, cpt = Math.cos(sp), spt = Math.sin(sp), pY = (m.pitchY || 0) / 16 * S;
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
      if (an === 'head') { ry += hy; rx += -pitch; if (e.eatT > 0) rx += 0.9; }
      else if (an === 'legA') rx += walkA;
      else if (an === 'legB') rx -= walkA;
      else if (an === 'armR' && e.aiming) { rx += -Math.PI / 2; ry += -0.1 + hy; }
      else if (an === 'armL' && e.aiming) { rx += -Math.PI / 2; ry += 0.1 + hy + 0.4; }
      else if (an === 'armR') { rx += (e.type === 'zombie' ? -Math.PI / 2 : -walkA) - sw * 1.4; rz += Math.sin(t * 1.1) * 0.05 + 0.05; }
      else if (an === 'armL') { rx += e.type === 'zombie' ? -Math.PI / 2 : walkA; rz -= Math.sin(t * 1.1) * 0.05 + 0.05; }
      else if (an === 'wingR') rz += e.onGround === false ? Math.sin(t * 30) * 0.8 : 0;
      else if (an === 'wingL') rz -= e.onGround === false ? Math.sin(t * 30) * 0.8 : 0;
      else if (an === 'tail') ry += tail;
      else if (an && an.startsWith('spider')) { const k = +an.slice(-1); ry += Math.sin(e.walk * 3.3 + k * 1.57) * 0.4 * e.walkAmt; rz += Math.abs(Math.cos(e.walk * 3.3 + k)) * 0.2 * e.walkAmt * (an[6] === 'R' ? 1 : -1); }
      const inf = part.inflate || 0;
      const [bx, byy, bz, bw, bh, bd] = part.box;
      // y-up space
      let x0 = -(bx + bw) - inf, x1 = -bx + inf, y0 = -(byy + bh) - inf, y1 = -byy + inf, z0 = bz - inf, z1 = bz + bd + inf;
      // babies: half-size body with a relatively big head (the head grows around its pivot)
      if (e.baby && an === 'head') { x0 *= 1.5; x1 *= 1.5; y0 *= 1.5; y1 *= 1.5; z0 *= 1.5; z1 *= 1.5; }
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
        return [EP[0] + rx2 - cam[0], EP[1] + wy - cam[1], EP[2] + rz2 - cam[2]];
      };
      g.pushModelBox(dst, M, x0, y0, z0, x1, y1, z1, part.uv[0], part.uv[1], bw, bh, bd, m.tw, m.th, li, tint, part.mirror);
      // the skeleton's bow in its right hand: the item sprite (0.9 block) held at the hand,
      // across the arm, so it points forward with the arm down and stands upright when aiming
      if (heldOut && an === 'armR' && m.heldItem) {
        const layer = g.assets.layers[m.heldItem], h = 7.2, s2 = Math.SQRT1_2 * h, cy0 = -9, cz0 = -0.5;
        const P = (a, b) => M(0, cy0 + (-a + b) * s2, cz0 + (a + b) * s2);   // a: texture right, b: texture up
        const q = [[P(-1, -1), 0, 1], [P(1, -1), 1, 1], [P(1, 1), 1, 0], [P(-1, 1), 0, 0]];
        const c = tint ? [li * tint[0], li * tint[1], li * tint[2]] : [li, li, li];
        for (const k of [0, 1, 2, 0, 2, 3]) heldOut.push(q[k][0][0], q[k][0][1], q[k][0][2], q[k][1], q[k][2], layer, c[0], c[1], c[2], 1);
      }
    }
  }
  renderItems(env, cam) {
    const g = this.game, r = g.r;
    const list = this.items.concat(this.arrows.map(a => ({ arrow: a, pos: a.pos })));
    if (!list.length && !this.falling.length) return;
    const v = [];
    // falling blocks: the block model at the entity position, between the last two ticks
    const al = this.alpha ?? 1;
    for (const f of this.falling) {
      const y = f.py + (f.y - f.py) * al;
      const li = g.lightAt(f.x, y + 0.5, f.z, env);
      const L = [0, 1, 2, 3, 4, 5].map(k => FTEX[f.id * 6 + k]);
      g.pushBox(v, f.x - 0.5 - cam[0], y - cam[1], f.z - 0.5 - cam[2], f.x + 0.5 - cam[0], y + 1 - cam[1], f.z + 0.5 - cam[2], 0, [li, li, li, 1], 0, L, [0.6, 0.6, 1, 0.5, 0.8, 0.8]);
    }
    const t = performance.now() / 1000;
    for (const it of list) {
      const li = g.lightAt(it.pos[0], it.pos[1] + 0.2, it.pos[2], env);
      if (it.arrow) {
        const a = it.arrow, v = a.stuck && a.dir ? a.dir : a.vel, l = Math.hypot(...v) || 1, d = v.map(x => x / l * 0.5);
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
    r.drawArr(r.f32(v), v.length / 10, env, 0.5);
    r.gl.enable(r.gl.CULL_FACE);
  }
}

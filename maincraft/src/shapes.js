'use strict';
// Block collision / selection boxes (main thread). Boxes are [x0,y0,z0,x1,y1,z1] in block units.

function rotBoxU(b, turns) {
  let x0 = b[0], z0 = b[2], x1 = b[3], z1 = b[5];
  for (let t = 0; t < turns; t++) { const nx0 = 1 - z1, nx1 = 1 - z0; z0 = x0; z1 = x1; x0 = nx0; x1 = nx1; }
  return [x0, b[1], z0, x1, b[4], z1];
}
const FULL_BOX = [[0, 0, 0, 1, 1, 1]];

// Amethyst buds and cluster (AmethystClusterBlock(height, offset)): a box growing out of the face
// the block is attached to (meta = face, mesher order +x -x +y -y +z -z).
const CRYSTAL_SIZE = {};
function crystalBox(id, m) {
  const sz = CRYSTAL_SIZE[id] || (CRYSTAL_SIZE[id] = id === B.AMETHYST_CLUSTER ? [7, 3] : id === B.LARGE_AMETHYST_BUD ? [5, 3] : id === B.MEDIUM_AMETHYST_BUD ? [4, 3] : [3, 4]);
  const h = sz[0] / 16, o = sz[1] / 16, a = o, b = 1 - o;
  switch (m % 6) {
    case 0: return [0, a, a, h, b, b];
    case 1: return [1 - h, a, a, 1, b, b];
    case 2: return [a, 0, a, b, h, b];
    case 3: return [a, 1 - h, a, b, 1, b];
    case 4: return [a, a, 0, b, b, h];
    default: return [a, a, 1 - h, b, b, 1];
  }
}

// selection boxes (what the outline / raycast uses)
function blockBoxes(world, id, m, x, y, z) {
  const sh = SHAPE[id];
  switch (sh) {
    case SH.CUBE: return FULL_BOX;
    case SH.SLAB: return [(m & 1) ? [0, 0.5, 0, 1, 1, 1] : [0, 0, 0, 1, 0.5, 1]];
    case SH.STAIRS: {
      const top = (m >> 2) & 1, f = m & 3;
      const s = top ? [0, 0.5, 0, 1, 1, 1] : [0, 0, 0, 1, 0.5, 1];
      const u = rotBoxU(top ? [0, 0, 0, 1, 0.5, 0.5] : [0, 0.5, 0, 1, 1, 0.5], (f + 2) & 3);
      return [s, u];
    }
    case SH.FENCE: case SH.WALL: {
      const w = sh === SH.FENCE ? 0.375 : 0.25, bx = [[w, 0, w, 1 - w, 1, 1 - w]];
      for (let d = 0; d < 4; d++) {
        const n = world.getBlock(x + DIRX_W[d], y, z + DIRZ_W[d]);
        const ok = OPAQUE[n] || SHAPE[n] === SH.FENCE || SHAPE[n] === SH.GATE || (sh === SH.WALL && SHAPE[n] === SH.WALL);
        if (ok) bx.push(rotBoxU([w + 0.0625, 0, 0, 1 - w - 0.0625, sh === SH.FENCE ? 1 : 0.875, w], (d + 2) & 3));
      }
      return bx;
    }
    case SH.PANE: {
      const bx = [[7 / 16, 0, 7 / 16, 9 / 16, 1, 9 / 16]];
      for (let d = 0; d < 4; d++) {
        const n = world.getBlock(x + DIRX_W[d], y, z + DIRZ_W[d]);
        if (OPAQUE[n] || SHAPE[n] === SH.PANE || (FLAGS[n] & (BF_GLASS | BF_TRANS))) bx.push(rotBoxU([7 / 16, 0, 0, 9 / 16, 1, 7 / 16], (d + 2) & 3));
      }
      if (bx.length === 1) return [[7 / 16, 0, 0, 9 / 16, 1, 1], [0, 0, 7 / 16, 1, 1, 9 / 16]];
      return bx;
    }
    case SH.GATE: {
      const f = m & 3;
      return [rotBoxU([0, 0.3125, 0.4375, 1, 1, 0.5625], (f + 2) & 3)];
    }
    case SH.DOOR: {
      const open = (m >> 3) & 1, hingeR = (m >> 4) & 1, f = m & 3;
      let b = [0, 0, 13 / 16, 1, 1, 1];
      if (open) b = hingeR ? [13 / 16, 0, 0, 1, 1, 1] : [0, 0, 0, 3 / 16, 1, 1];
      return [rotBoxU(b, (f + 2) & 3)];
    }
    case SH.TRAPDOOR: {
      const f = m & 3, top = (m >> 2) & 1, open = (m >> 3) & 1;
      if (open) return [rotBoxU([0, 0, 13 / 16, 1, 1, 1], (f + 2) & 3)];
      return [top ? [0, 13 / 16, 0, 1, 1, 1] : [0, 0, 0, 1, 3 / 16, 1]];
    }
    case SH.LADDER: return [rotBoxU([0, 0, 13 / 16, 1, 1, 1], ((m & 3) + 2) & 3)];
    case SH.TORCH: {
      if (!m) return [[6 / 16, 0, 6 / 16, 10 / 16, 10 / 16, 10 / 16]];
      const d = m - 1;
      return [[0.5 - 0.15 + DIRX_W[d] * 0.3, 0.2, 0.5 - 0.15 + DIRZ_W[d] * 0.3, 0.5 + 0.15 + DIRX_W[d] * 0.3, 0.8, 0.5 + 0.15 + DIRZ_W[d] * 0.3]];
    }
    case SH.CARPET: case SH.LILY: return [[0, 0, 0, 1, 1 / 16, 1]];
    case SH.PLATE: return [[1 / 16, 0, 1 / 16, 15 / 16, 1 / 16, 15 / 16]];
    case SH.WIRE: return [[0, 0, 0, 1, 1 / 16, 1]];
    case SH.RAIL: return [[0, 0, 0, 1, 2 / 16, 1]];
    case SH.BUTTON: return [rotBoxU([5 / 16, 6 / 16, 0, 11 / 16, 10 / 16, 2 / 16], ((m & 3) + 2) & 3)];
    case SH.FARMLAND: return [[0, 0, 0, 1, 15 / 16, 1]];
    case SH.CACTUS: return [[1 / 16, 0, 1 / 16, 15 / 16, 1, 15 / 16]];
    case SH.CHEST: return [[1 / 16, 0, 1 / 16, 15 / 16, 14 / 16, 15 / 16]];
    case SH.BED: return [[0, 0, 0, 1, 9 / 16, 1]];
    case SH.BAMBOO: return [[6 / 16, 0, 6 / 16, 10 / 16, 1, 10 / 16]];
    case SH.POT: return [[5 / 16, 0, 5 / 16, 11 / 16, 6 / 16, 11 / 16]];
    case SH.PICKLE: return [[0.2, 0, 0.2, 0.8, 0.4, 0.8]];
    case SH.CROSS: case SH.TALL: case SH.FIRE: return [[0.15, 0, 0.15, 0.85, (id === B.TALL_GRASS ? 0.8 : 1), 0.85]];
    case SH.VINE: case SH.LICHEN: return [[0, 0, 0, 1, 1, 1]];
    case SH.SNOWLAYER: return [[0, 0, 0, 1, ((m & 7) + 1) / 8, 1]];
    case SH.CRYSTAL: return [crystalBox(id, m)];
    case SH.MODEL: { const bx = MODEL_BOX_ID[id]; return bx ? [bx[m] || bx[0]] : FULL_BOX; }
    case SH.WATER: case SH.LAVA: return FULL_BOX;
    default: return FULL_BOX;
  }
}

// collision boxes: none for non-solid blocks, fences/walls are 1.5 high
function collisionBoxes(world, id, m, x, y, z) {
  if (!COLLIDE[id]) return null;
  const sh = SHAPE[id];
  if (sh === SH.CUBE) return FULL_BOX;
  if (sh === SH.FENCE || sh === SH.WALL) {
    const b = blockBoxes(world, id, m, x, y, z);
    return b.map(q => [q[0], q[1], q[2], q[3], 1.5, q[5]]);
  }
  if (sh === SH.GATE) {
    if ((m >> 2) & 1) return null;
    const f = m & 3;
    return [rotBoxU([0, 0, 0.375, 1, 1.5, 0.625], (f + 2) & 3)];
  }
  if (sh === SH.CARPET || sh === SH.LILY) return [[0, 0, 0, 1, 1 / 16, 1]];
  if (id === B.COBWEB) return null;
  return blockBoxes(world, id, m, x, y, z);
}

// ray vs world: returns {x,y,z, face (0..5 per FN order), id, meta, px,py,pz (hit point)}
function raycast(world, ox, oy, oz, dx, dy, dz, maxDist, opts) {
  let x = Math.floor(ox), y = Math.floor(oy), z = Math.floor(oz);
  const sx = dx > 0 ? 1 : -1, sy = dy > 0 ? 1 : -1, sz = dz > 0 ? 1 : -1;
  const tdx = Math.abs(1 / dx), tdy = Math.abs(1 / dy), tdz = Math.abs(1 / dz);
  let tmx = dx !== 0 ? ((sx > 0 ? x + 1 - ox : ox - x) * tdx) : Infinity;
  let tmy = dy !== 0 ? ((sy > 0 ? y + 1 - oy : oy - y) * tdy) : Infinity;
  let tmz = dz !== 0 ? ((sz > 0 ? z + 1 - oz : oz - z) * tdz) : Infinity;
  const fluids = opts && opts.fluids;
  for (let i = 0; i < 256; i++) {
    const id = world.getBlock(x, y, z);
    if (id) {
      const sh = SHAPE[id];
      const isFluid = sh === SH.WATER || sh === SH.LAVA;
      if (!isFluid || (fluids && (world.getMeta(x, y, z) & 15) === 0)) {
        const m = world.getMeta(x, y, z);
        const boxes = isFluid ? FULL_BOX : blockBoxes(world, id, m, x, y, z);
        let best = null;
        for (const b of boxes) {
          const h = rayBox(ox, oy, oz, dx, dy, dz, x + b[0], y + b[1], z + b[2], x + b[3], y + b[4], z + b[5]);
          if (h && (!best || h.t < best.t)) best = h;
        }
        if (best && best.t <= maxDist) return { x, y, z, face: best.face, id, meta: m, t: best.t, px: ox + dx * best.t, py: oy + dy * best.t, pz: oz + dz * best.t };
      }
    }
    if (tmx < tmy && tmx < tmz) { if (tmx > maxDist) break; x += sx; tmx += tdx; }
    else if (tmy < tmz) { if (tmy > maxDist) break; y += sy; tmy += tdy; }
    else { if (tmz > maxDist) break; z += sz; tmz += tdz; }
  }
  return null;
}
function rayBox(ox, oy, oz, dx, dy, dz, x0, y0, z0, x1, y1, z1) {
  let tmin = -Infinity, tmax = Infinity, face = -1;
  const ax = [ox, oy, oz], ad = [dx, dy, dz], lo = [x0, y0, z0], hi = [x1, y1, z1];
  for (let a = 0; a < 3; a++) {
    if (Math.abs(ad[a]) < 1e-9) { if (ax[a] < lo[a] || ax[a] > hi[a]) return null; continue; }
    let t1 = (lo[a] - ax[a]) / ad[a], t2 = (hi[a] - ax[a]) / ad[a];
    let f1 = a * 2 + 1, f2 = a * 2; // entering through min side -> face "-a", via max side -> "+a"
    if (t1 > t2) { const t = t1; t1 = t2; t2 = t; const f = f1; f1 = f2; f2 = f; }
    if (t1 > tmin) { tmin = t1; face = f1; }
    if (t2 < tmax) tmax = t2;
    if (tmin > tmax) return null;
  }
  if (tmax < 0) return null;
  // face indices: 0 +x,1 -x,2 +y,3 -y,4 +z,5 -z
  return { t: Math.max(0, tmin), face };
}

'use strict';
// Own structures built in code: jungle temple, ruined portals, woodland mansion, ocean monument and
// stronghold. Each is designed here (layout, materials, rooms); only the placement scheme (one start
// per region of a structure set, pieces clipped to the chunk being generated) is shared with the
// other structures.

// a piece built by code: origin (x, y, z), rotation about the origin (WorldGen.tplPos), size; local
// facings are world facings at rotation 0 (0 S +z, 1 W -x, 2 N -z, 3 E +x)
function ownPiece(build, x, y, z, rot, sx, sy, sz, extra) {
  const a = WorldGen.tplPos(0, 0, rot, 0, 0), b = WorldGen.tplPos(sx - 1, sz - 1, rot, 0, 0);
  const p = Object.assign({ build, x, y, z, rot, sx, sy, sz }, extra || {}), m = p.margin || 0;
  p.box = [x + Math.min(a[0], b[0]) - m, y, z + Math.min(a[1], b[1]) - m, x + Math.max(a[0], b[0]) + m, y + sy - 1, z + Math.max(a[1], b[1]) + m];
  return p;
}

// block writer for a piece in the chunk being generated (cells outside the chunk are skipped)
WorldGen.prototype.ownBuilder = function (p) {
  const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0, rot = p.rot, seed = this.seed;
  const at = (x, y, z) => {
    const o = WorldGen.tplPos(x, z, rot, 0, 0), X = p.x + o[0], Z = p.z + o[1], Y = p.y + y;
    return X >= x0 && X < x0 + 16 && Z >= z0 && Z < z0 + 16 && Y > WORLD_MIN_Y && Y < WORLD_MAX_Y ? CI(X - x0, Y, Z - z0) : -1;
  };
  const world = (x, y, z) => { const o = WorldGen.tplPos(x, z, rot, 0, 0); return [p.x + o[0], p.y + y, p.z + o[1]]; };
  const W = {
    at, world,
    // a block that can hold water (ladder, stairs, fence...) set into water is waterlogged
    set(x, y, z, id, m) { const i = at(x, y, z); if (i >= 0) { if (WET[id] && isWaterId(ids[i])) id = WET[id]; ids[i] = id; meta[i] = id ? rotMeta(dryId(id), m || 0, rot) : 0; } },
    get(x, y, z) { const i = at(x, y, z); return i >= 0 ? ids[i] : -1; },
    // a random 0..1 for a cell, the same in every chunk
    r(x, y, z, k) { const w = world(x, y, z); return hash3(seed + 7001 + (k || 0) * 13, w[0], w[1], w[2]); },
    fill(ax, ay, az, bx, by, bz, id, m) { for (let y = ay; y <= by; y++) for (let z = az; z <= bz; z++) for (let x = ax; x <= bx; x++) W.set(x, y, z, id, m); },
    // id from pick(x, y, z) for every cell of the box
    fillBy(ax, ay, az, bx, by, bz, pick) { for (let y = ay; y <= by; y++) for (let z = az; z <= bz; z++) for (let x = ax; x <= bx; x++) W.set(x, y, z, pick(x, y, z)); },
    // the four side walls of a box
    walls(ax, ay, az, bx, by, bz, pick) {
      for (let y = ay; y <= by; y++) for (let z = az; z <= bz; z++) for (let x = ax; x <= bx; x++)
        if (x === ax || x === bx || z === az || z === bz) W.set(x, y, z, typeof pick === 'function' ? pick(x, y, z) : pick);
    },
    // a foundation column: the cell at y always, then down through air, liquids and plants to
    // solid ground (at most 24 more, so a column over a big cave stops instead of reaching its floor)
    down(x, y, z, id) {
      for (let yy = y; yy > y - 25 && p.y + yy > WORLD_MIN_Y + 1; yy--) {
        const i = at(x, yy, z); if (i < 0) return;
        const c = ids[i];
        if (yy < y && c && !isLiquidId(c) && !(FLAGS[c] & BF_REPLACE) && !(FLAGS[c] & BF_LEAVES)) return;
        ids[i] = id; meta[i] = 0;
      }
    },
    chest(x, y, z, facing, table) { const i = at(x, y, z); if (i >= 0) { ids[i] = isWaterId(ids[i]) ? WET[B.CHEST] : B.CHEST; meta[i] = rotMeta(B.CHEST, facing, rot) | (table << 2); } },
    // a vine on the side d (local facing toward the wall it hangs on)
    vine(x, y, z, d) { const i = at(x, y, z); if (i >= 0 && ids[i] === 0) { ids[i] = B.VINE; meta[i] = 1 << ((d + rot) & 3); } },
    spawner(x, y, z, mob, gen) { const i = at(x, y, z); if (i >= 0) { ids[i] = B.SPAWNER; meta[i] = SPAWNER_MOB[mob]; const w = world(x, y, z); gen.spawnerList.push(w[0], w[1], w[2]); } },
  };
  return W;
};

// lowest / average surface over a footprint (world box corners)
WorldGen.prototype.footprintH = function (box, mode) {
  let lo = 1e9, hi = -1e9, sum = 0, n = 0;
  for (let z = box[2]; z <= box[5]; z += 2) for (let x = box[0]; x <= box[3]; x += 2) {
    const h = mode === 'floor' ? this.floorH(x, z) : this.surfH(x, z);
    lo = Math.min(lo, h); hi = Math.max(hi, h); sum += h; n++;
  }
  return { lo, hi, avg: Math.round(sum / n) };
};

// ===================================================================== jungle temple
// A stepped stone shrine: a pillared hall (mossy cobble outside, stone bricks inside), a terrace
// with an upper hall, a little open shrine on top, and a crypt under the hall reached by a ladder.
const JT = { sx: 15, sy: 19, sz: 17 };
WorldGen.prototype.jungleTempleStart = function (cx, cz) {
  const bio = this.biomeAt(cx * 16 + 8, cz * 16 + 8);
  if (bio !== BI.JUNGLE && bio !== BI.BAMBOO_JUNGLE && bio !== BI.SPARSE_JUNGLE) return null;
  const rnd = mulberry(hash2i(this.seed + 61210487 * 3, cx, cz)), rot = (rnd() * 4) | 0;
  const p = ownPiece(this.jungleTemple, cx * 16, 0, cz * 16, rot, JT.sx, JT.sy, JT.sz, { noTrees: true, margin: 1 });
  const f = this.footprintH(p.box);
  if (f.hi - f.lo > 8 || f.lo <= SEA) return null;
  const y = f.avg - 1 - 6;            // the crypt (6 below the hall floor) sits in the ground
  p.y = y; p.box[1] = y; p.box[4] = y + JT.sy - 1;
  return [p];
};
WorldGen.prototype.jungleTemple = function (p) {
  const W = this.ownBuilder(p), F = 6;     // F: hall floor level inside the piece
  const rough = (x, y, z) => W.r(x, y, z) < 0.35 ? B.MOSSY : B.COBBLE;
  const brick = (x, y, z) => { const r = W.r(x, y, z, 1); return r < 0.2 ? B.MOSSY_STONE_BRICKS : r < 0.3 ? B.CRACKED_STONE_BRICKS : B.STONE_BRICKS; };
  // clear the hill above, then the foundation down to the ground
  W.fill(0, F + 1, 0, 14, 18, 16, 0);
  for (let z = 0; z < 17; z++) for (let x = 0; x < 15; x++) W.down(x, F, z, B.COBBLE);
  // crypt: 11 x 5 x 8 stone-brick vault under the back half
  W.fillBy(2, 0, 8, 12, F - 1, 15, brick);
  W.fill(3, 1, 9, 11, F - 2, 14, 0);
  W.fillBy(3, 0, 9, 11, 0, 14, (x, y, z) => W.r(x, y, z, 2) < 0.5 ? B.MOSSY_STONE_BRICKS : B.STONE_BRICKS);
  W.chest(4, 1, 13, 3, LOOT.JUNGLE_TEMPLE);
  W.set(10, 1, 9, B.COBWEB); W.set(3, F - 2, 9, B.COBWEB); W.set(11, F - 2, 14, B.COBWEB); W.set(3, 1, 14, B.COBWEB);
  W.set(7, 1, 14, B.CHISELED_STONE_BRICKS); W.set(7, 2, 14, B.LANTERN, 0);
  for (let y = 1; y <= F; y++) W.set(11, y, 14, B.LADDER, 2);     // up into the hall (the crypt wall on its south)
  // ground hall 15 x 17, walls 5 high
  W.fillBy(0, F, 0, 14, F, 16, brick);
  W.walls(0, F + 1, 0, 14, F + 4, 16, rough);
  W.fillBy(0, F + 5, 0, 14, F + 5, 16, rough);
  for (const x of [4, 10]) for (const z of [4, 8, 12]) W.fill(x, F + 1, z, x, F + 4, z, B.CHISELED_STONE_BRICKS);
  W.fill(6, F + 1, 0, 8, F + 3, 0, 0);                              // entrance
  W.set(5, F + 3, 0, B.COBBLE_STAIRS, 3 | 4); W.set(9, F + 3, 0, B.COBBLE_STAIRS, 1 | 4);
  W.set(7, F + 4, 8, B.LANTERN, 1); W.set(7, F + 4, 3, B.LANTERN, 1);
  // the altar at the back with a chest
  W.fill(5, F + 1, 15, 9, F + 1, 15, B.CHISELED_STONE_BRICKS);
  W.chest(7, F + 2, 15, 2, LOOT.JUNGLE_TEMPLE);
  W.set(5, F + 2, 15, B.TORCH, 1 + 0); W.set(9, F + 2, 15, B.TORCH, 1 + 0);
  // windows
  for (const z of [5, 11]) { W.set(0, F + 2, z, 0); W.set(14, F + 2, z, 0); }
  // stairs inside along the east wall up through the roof onto the terrace
  for (let k = 0; k < 5; k++) { W.set(13, F + 1 + k, 2 + k, B.COBBLE_STAIRS, 0); W.set(13, F + 2 + k, 2 + k, 0); W.set(13, F + 3 + k, 2 + k, 0); }
  W.set(13, F + 5, 6, B.COBBLE_STAIRS, 0);
  // upper hall on the terrace: 11 x 11, door at the front
  W.walls(2, F + 6, 3, 12, F + 9, 13, rough);
  W.fill(3, F + 6, 4, 11, F + 9, 12, 0);
  W.fillBy(2, F + 10, 3, 12, F + 10, 13, rough);
  W.fill(6, F + 6, 3, 8, F + 8, 3, 0);
  W.set(7, F + 9, 8, B.LANTERN, 1);
  for (const [x, z] of [[3, 4], [11, 4], [3, 12], [11, 12]]) W.fill(x, F + 6, z, x, F + 9, z, B.CHISELED_STONE_BRICKS);
  // the shrine on top: four pillars and a cap
  for (const [x, z] of [[5, 6], [9, 6], [5, 10], [9, 10]]) W.fill(x, F + 11, z, x, F + 12, z, B.CHISELED_STONE_BRICKS);
  W.fillBy(5, F + 13, 6, 9, F + 13, 10, rough);
  W.fill(6, F + 13, 7, 8, F + 13, 9, B.MOSSY_STONE_BRICKS);
  W.set(7, F + 11, 8, B.CHISELED_STONE_BRICKS);
  // vines on the outer walls
  for (let y = F + 1; y <= F + 9; y++) for (let k = 0; k < 17; k++) {
    if (W.r(0, y, k, 3) < 0.3) W.vine(-1, y, k, 3);
    if (W.r(14, y, k, 4) < 0.3) W.vine(15, y, k, 1);
    if (k < 15 && W.r(k, y, 16, 5) < 0.3) W.vine(k, y, 17, 2);
  }
};

// ===================================================================== ruined portal
// A fallen gate of obsidian on a cracked stone floor, the ground around scorched to netherrack with
// a few magma blocks; some frame blocks lie broken on the ground. Standing or toppled.
WorldGen.prototype.ruinedPortalStart = function (cx, cz) {
  const bio = this.biomeAt(cx * 16 + 8, cz * 16 + 8), bp = BPROP[bio];
  if (bp && bp.ocean) return null;
  const rnd = mulberry(hash2i(this.seed + 33590147 * 3, cx, cz)), rot = (rnd() * 4) | 0;
  const p = ownPiece(this.ruinedPortal, cx * 16 + 2, 0, cz * 16 + 2, rot, 11, 9, 11, { noTrees: true, fallen: rnd() < 0.35, big: rnd() < 0.3, salt: (rnd() * 1e6) | 0 });
  const f = this.footprintH(p.box), fl = this.footprintH(p.box, 'floor');
  if (f.hi - f.lo > 6 || fl.lo < SEA) return null;
  p.y = f.avg - 2; p.box[1] = p.y; p.box[4] = p.y + 8;
  return [p];
};
WorldGen.prototype.ruinedPortal = function (p) {
  const W = this.ownBuilder(p), G = 1;          // G: floor level
  const r = (x, y, z, k) => W.r(x, y, z, 20 + k + p.salt % 97);
  // round scorched patch, the floor in the middle
  for (let z = 0; z < 11; z++) for (let x = 0; x < 11; x++) {
    const d = Math.hypot(x - 5, z - 5);
    if (d > 5.2 + r(x, 0, z, 0) * 0.8) continue;
    // find the ground in this column (within the piece)
    let y = 8; while (y > 0 && W.get(x, y, z) === 0) y--;
    const cur = W.get(x, y, z);
    if (d < 3.6) {
      W.set(x, G, z, r(x, G, z, 1) < 0.25 ? B.CRACKED_STONE_BRICKS : r(x, G, z, 1) < 0.4 ? B.MOSSY_STONE_BRICKS : B.STONE_BRICKS);
      W.down(x, G - 1, z, B.STONE_BRICKS);
      for (let yy = G + 1; yy < 9; yy++) W.set(x, yy, z, 0);
    } else if (OPAQUE[cur] && OPAQUE[W.get(x, y - 1, z)] && r(x, y, z, 2) < 0.75) W.set(x, y, z, r(x, y, z, 3) < 0.12 ? B.MAGMA_BLOCK : B.NETHERRACK);   // only real ground
  }
  const frame = (x, y, z) => {
    const k = r(x, y, z, 4);
    if (k < 0.2) return;                                              // broken off
    W.set(x, y, z, k < 0.45 ? B.CRYING_OBSIDIAN : B.OBSIDIAN);
  };
  const w = p.big ? 5 : 4, h = p.big ? 7 : 5, x0 = 5 - (w >> 1);
  if (!p.fallen) {
    for (let x = x0; x < x0 + w; x++) { frame(x, G + 1, 5); frame(x, G + h, 5); }
    for (let y = G + 2; y < G + h; y++) { frame(x0, y, 5); frame(x0 + w - 1, y, 5); }
  } else {
    for (let x = x0; x < x0 + w; x++) { frame(x, G + 1, 2); frame(x, G + 1, 1 + h); }
    for (let z = 3; z < 1 + h; z++) { frame(x0, G + 1, z); frame(x0 + w - 1, G + 1, z); }
  }
  // broken pieces, gold, a chest
  for (let k = 0; k < 4; k++) { const x = 1 + ((r(k, 0, 0, 5) * 9) | 0), z = 1 + ((r(0, 0, k, 6) * 9) | 0); if (W.get(x, G + 1, z) === 0 && W.get(x, G, z) > 0) W.set(x, G + 1, z, B.OBSIDIAN); }
  if (r(1, 1, 1, 7) < 0.6) W.set(8, G + 1, 5, B.GOLD_BLOCK);
  W.chest(2, G + 1, 5, 3, LOOT.RUINED_PORTAL);
  W.set(3, G + 1, 3, B.NETHERRACK); W.set(3, G + 2, 3, B.FIRE);
};

// ===================================================================== woodland mansion
// A dark-wood manor: cobblestone plinth, dark oak frame with birch walls and glass windows, two
// floors of four rooms around a middle hall with a staircase, a steep dark roof with dormer ridge.
const MAN = { sx: 35, sy: 30, sz: 27, floor: 5 };
WorldGen.prototype.mansionStart = function (cx, cz) {
  const bio = this.biomeAt(cx * 16 + 8, cz * 16 + 8);
  if (bio !== BI.DARK_FOREST && bio !== BI.PALE_GARDEN) return null;
  const rnd = mulberry(hash2i(this.seed + 10387319 * 3, cx, cz)), rot = (rnd() * 4) | 0;
  const p = ownPiece(this.mansion, cx * 16, 0, cz * 16, rot, MAN.sx, MAN.sy, MAN.sz, { noTrees: true, margin: 1, salt: (rnd() * 1e6) | 0 });
  const f = this.footprintH(p.box);
  if (f.lo <= SEA) return null;
  p.y = f.avg - MAN.floor; p.box[1] = p.y; p.box[4] = p.y + MAN.sy - 1;
  return [p];
};
WorldGen.prototype.mansion = function (p) {
  const W = this.ownBuilder(p), F = MAN.floor, SX = MAN.sx, SZ = MAN.sz;
  const rr = (x, y, z, k) => W.r(x, y, z, 40 + k);
  // ground cleared over the footprint (and a 1 block margin), plinth down to the ground
  W.fill(0, F + 1, 0, SX - 1, MAN.sy - 1, SZ - 1, 0);
  for (let z = 0; z < SZ; z++) for (let x = 0; x < SX; x++) W.down(x, F, z, B.COBBLE);
  W.fillBy(0, F, 0, SX - 1, F, SZ - 1, (x, y, z) => (x === 0 || z === 0 || x === SX - 1 || z === SZ - 1) ? (rr(x, y, z, 0) < 0.3 ? B.MOSSY : B.COBBLE) : B.DARK_PLANKS);
  const wallAt = (x, z) => x === 0 || z === 0 || x === SX - 1 || z === SZ - 1;
  const post = (x, z) => (x % 6 === 0 || x === SX - 1) && (z % 6 === 0 || z === SZ - 1) || ((x === 0 || x === SX - 1) && z % 6 === 0) || ((z === 0 || z === SZ - 1) && x % 6 === 0);
  // two storeys: walls 5 high, a dark band between them
  for (const base of [F + 1, F + 7]) {
    for (let y = base; y < base + 5; y++) for (let z = 0; z < SZ; z++) for (let x = 0; x < SX; x++) {
      if (!wallAt(x, z)) continue;
      if (post(x, z)) { W.set(x, y, z, B.DARK_LOG); continue; }
      const win = (y === base + 1 || y === base + 2) && ((x % 3 === 0 && (z === 0 || z === SZ - 1)) || (z % 3 === 0 && (x === 0 || x === SX - 1)));
      W.set(x, y, z, win ? B.GLASS_PANE : B.BIRCH_PLANKS);
    }
    W.fill(1, base + 5, 1, SX - 2, base + 5, SZ - 2, B.DARK_PLANKS);                // ceiling / floor above
    W.walls(0, base + 5, 0, SX - 1, base + 5, SZ - 1, B.STRIPPED_DARK_OAK_LOG);
    // inner walls: a hall along z in the middle (x 14..20), each side split in two rooms at z 13
    for (let y = base; y < base + 5; y++) for (let z = 1; z < SZ - 1; z++) { W.set(14, y, z, B.DARK_PLANKS); W.set(20, y, z, B.DARK_PLANKS); }
    for (let y = base; y < base + 5; y++) for (let x = 1; x < SX - 1; x++) if (x < 14 || x > 20) W.set(x, y, 13, B.DARK_PLANKS);
    // doors from the hall into the four rooms
    for (const z of [6, 20]) for (const x of [14, 20]) { W.set(x, base, z, 0); W.set(x, base + 1, z, 0); }
    // hall: red carpet and torches
    for (let z = 1; z < SZ - 1; z++) for (let x = 15; x <= 19; x++) W.set(x, base, z, x === 17 || x === 16 || x === 18 ? B.RED_CARPET : 0);
    for (const z of [4, 10, 16, 22]) { W.set(15, base + 2, z, B.TORCH, 1 + 1); W.set(19, base + 2, z, B.TORCH, 1 + 3); }
  }
  // front door (double) and porch
  for (const x of [17]) { W.set(x, F + 1, 0, B.DARK_OAK_DOOR, 2); W.set(x, F + 2, 0, B.DARK_OAK_DOOR, 2 | 4); }
  W.set(16, F + 1, 0, B.DARK_OAK_DOOR, 2 | 16); W.set(16, F + 2, 0, B.DARK_OAK_DOOR, 2 | 4 | 16);
  W.set(18, F + 1, 0, B.DARK_OAK_DOOR, 2); W.set(18, F + 2, 0, B.DARK_OAK_DOOR, 2 | 4);
  for (let x = 15; x <= 19; x++) { W.set(x, F, -1, B.COBBLE_STAIRS, 0); }
  // staircase at the back of the hall (z 15..20 rising toward +z), the opening above it
  for (let k = 0; k < 6; k++) { W.set(16, F + 1 + k, 15 + k, B.DARK_PLANKS_STAIRS, 0); W.set(17, F + 1 + k, 15 + k, B.DARK_PLANKS_STAIRS, 0); W.set(16, F + 2 + k, 15 + k, 0); W.set(17, F + 2 + k, 15 + k, 0); }
  for (let z = 15; z <= 20; z++) { W.set(16, F + 6, z, 0); W.set(17, F + 6, z, 0); W.set(16, F + 7, z, z > 19 ? B.RED_CARPET : 0); W.set(17, F + 7, z, z > 19 ? B.RED_CARPET : 0); }
  for (let z = 15; z <= 20; z++) { W.set(15, F + 7, z, B.DARK_PLANKS_FENCE); }
  // the rooms
  const kinds = ['library', 'bedroom', 'dining', 'storage', 'bedroom', 'library', 'storage', 'web'];
  let n = 0;
  for (const base of [F + 1, F + 7]) for (const [ax, bx] of [[1, 13], [21, 33]]) for (const [az, bz] of [[1, 12], [14, 25]]) {
    const kind = kinds[(n + (p.salt >> 3)) % kinds.length]; n++;
    this.mansionRoom(W, kind, ax, base, az, bx, bz, ax === 1 ? 3 : 1, rr);
  }
  // roof: a steep gable along x, overhanging by one
  const R = MAN.floor + 12;
  for (let k = 0; k <= 13; k++) {
    const y = R + k;
    for (let x = -1; x <= SX; x++) {
      W.set(x, y, k - 1, B.DARK_PLANKS_STAIRS, 0); W.set(x, y, SZ - k, B.DARK_PLANKS_STAIRS, 2);
      if (k > 0) for (let z = k; z < SZ - k; z++) if (x === 0 || x === SX - 1) W.set(x, y, z, B.DARK_PLANKS);
    }
    if (SZ - k - (k - 1) <= 2) { for (let x = -1; x <= SX; x++) for (let z = k - 1; z <= SZ - k; z++) W.set(x, y + 1, z, B.DARK_PLANKS_SLAB); break; }
  }
  // gable windows and chimneys
  for (const x of [0, SX - 1]) { W.set(x, R + 3, 12, B.GLASS_PANE); W.set(x, R + 3, 14, B.GLASS_PANE); }
  for (const x of [6, 28]) { W.fill(x, R + 4, 12, x + 1, R + 13, 13, B.COBBLE); W.set(x, R + 14, 12, B.COBBLE_WALL); }
};
WorldGen.prototype.mansionRoom = function (W, kind, ax, y, az, bx, bz, wallSide, rr) {
  const cxm = (ax + bx) >> 1, czm = (az + bz) >> 1;
  W.set(cxm, y + 4, czm, B.LANTERN, 1);
  if (kind === 'library') {
    for (let x = ax; x <= bx; x++) for (const z of [az, bz]) for (let yy = y; yy < y + 3; yy++) if (x !== (wallSide === 3 ? bx : ax)) W.set(x, yy, z, B.BOOKSHELF);
    for (let z = az + 3; z <= bz - 3; z += 2) for (let x = ax + 2; x <= bx - 2; x++) if (x !== cxm) W.set(x, y, z, B.BOOKSHELF);
    W.chest(cxm, y, czm, 0, LOOT.WOODLAND_MANSION);
  } else if (kind === 'bedroom') {
    W.fill(ax + 2, y, az + 2, bx - 2, y, bz - 2, B.BLUE_CARPET);
    W.set(ax + 1, y, czm, B.BED, 1 | 4); W.set(ax + 2, y, czm, B.BED, 1);
    W.set(ax + 1, y, czm + 2, B.BED, 1 | 4); W.set(ax + 2, y, czm + 2, B.BED, 1);
    W.chest(bx, y, az, 2, LOOT.WOODLAND_MANSION);
    W.set(bx, y, bz, B.CRAFTING_TABLE);
  } else if (kind === 'dining') {
    for (let x = ax + 3; x <= bx - 3; x++) { W.set(x, y, czm, B.DARK_PLANKS_FENCE); W.set(x, y + 1, czm, B.WHITE_CARPET); }
    for (let x = ax + 3; x <= bx - 3; x += 2) { W.set(x, y, czm - 1, B.DARK_PLANKS_STAIRS, 0); W.set(x, y, czm + 1, B.DARK_PLANKS_STAIRS, 2); }
    W.set(ax, y, az, B.FURNACE, 0); W.set(ax + 1, y, az, B.SMOKER, 0); W.set(ax + 2, y, az, B.BARREL, 4);
  } else if (kind === 'storage') {
    for (let x = ax; x <= bx; x += 2) { W.set(x, y, az, B.BARREL, 4); W.set(x, y, bz, B.BARREL, 4); if (rr(x, y, az, 1) < 0.5) W.set(x, y + 1, az, B.BARREL, 4); }
    W.chest(cxm, y, czm, 0, LOOT.WOODLAND_MANSION); W.chest(cxm + 1, y, czm, 0, LOOT.WOODLAND_MANSION);
    W.set(ax + 1, y, czm, B.HAY_BLOCK); W.set(ax + 1, y + 1, czm, B.HAY_BLOCK);
  } else {
    for (let z = az; z <= bz; z++) for (let x = ax; x <= bx; x++) for (let yy = y; yy < y + 4; yy++) if (rr(x, yy, z, 2) < 0.12) W.set(x, yy, z, B.COBWEB);
    W.chest(cxm, y, czm, 0, LOOT.WOODLAND_MANSION);
  }
};

// ===================================================================== ocean monument
// A sunken citadel of prismarine: a broad plinth on the sea floor, four corner towers joined by
// arcaded walls, a stepped keep with a treasure chamber at its heart, sea lanterns for light.
const MON = { s: 41, sy: 24 };
WorldGen.prototype.monumentStart = function (cx, cz) {
  const bio = this.biomeAt(cx * 16 + 8, cz * 16 + 8);
  if (bio !== BI.DEEP_OCEAN && bio !== BI.DEEP_COLD_OCEAN && bio !== BI.DEEP_LUKEWARM_OCEAN && bio !== BI.DEEP_FROZEN_OCEAN) return null;
  const rnd = mulberry(hash2i(this.seed + 10387313 * 3, cx, cz)), rot = (rnd() * 4) | 0;
  const p = ownPiece(this.monument, cx * 16 - 12, 0, cz * 16 - 12, rot, MON.s, MON.sy, MON.s, { noTrees: true });
  for (const [x, z] of [[p.box[0], p.box[2]], [p.box[3], p.box[2]], [p.box[0], p.box[5]], [p.box[3], p.box[5]]]) {
    const b = this.biomeAt(x, z); if (!BPROP[b] || !BPROP[b].ocean || b === BI.RIVER) return null;
  }
  const f = this.footprintH(p.box, 'floor');
  p.y = Math.min(f.avg, SEA - MON.sy - 1); p.box[1] = p.y; p.box[4] = p.y + MON.sy - 1;
  return [p];
};
WorldGen.prototype.monument = function (p) {
  const W = this.ownBuilder(p), S = MON.s, M = (S - 1) >> 1;
  const prism = (x, y, z) => W.r(x, y, z, 60) < 0.3 ? B.PRISMARINE : B.PRISMARINE_BRICKS;
  const WATER = (x, y, z) => W.set(x, y, z, B.WATER, 0);
  // everything in the box is water first (cuts the sea floor), then the plinth with legs down
  for (let y = 1; y < MON.sy; y++) for (let z = 0; z < S; z++) for (let x = 0; x < S; x++) WATER(x, y, z);
  W.fillBy(0, 0, 0, S - 1, 0, S - 1, prism);
  for (let z = 0; z < S; z += 4) for (let x = 0; x < S; x += 4) W.down(x, -1, z, B.PRISMARINE_BRICKS);
  // four corner towers 7 x 7, 16 high, dark bands, lantern crowns
  for (const [tx, tz] of [[0, 0], [S - 7, 0], [0, S - 7], [S - 7, S - 7]]) {
    for (let y = 1; y <= 16; y++) W.walls(tx, y, tz, tx + 6, y, tz + 6, y % 5 === 0 ? B.DARK_PRISMARINE : prism);
    W.fill(tx + 1, 17, tz + 1, tx + 5, 17, tz + 5, B.DARK_PRISMARINE);
    W.set(tx + 3, 18, tz + 3, B.SEA_LANTERN);
    for (const [ox, oz] of [[1, 1], [5, 1], [1, 5], [5, 5]]) W.set(tx + ox, 18, tz + oz, B.PRISMARINE_WALL);
    W.set(tx + 3, 8, tz, 0); W.set(tx + 3, 8, tz + 6, 0); W.set(tx, 8, tz + 3, 0); W.set(tx + 6, 8, tz + 3, 0);
    for (const y of [8, 9]) { WATER(tx + 3, y, tz); WATER(tx + 3, y, tz + 6); WATER(tx, y, tz + 3); WATER(tx + 6, y, tz + 3); }
    W.set(tx + 3, 15, tz + 3, B.SEA_LANTERN);
  }
  // arcaded walls between the towers: piers every 4, arches over the gaps
  for (let k = 7; k < S - 7; k++) for (const [x, z] of [[k, 2], [k, S - 3], [2, k], [S - 3, k]]) {
    const pier = (k - 7) % 4 === 0;
    for (let y = 1; y <= 9; y++) {
      if (!pier && y < 6) continue;
      W.set(x, y, z, y === 9 ? B.DARK_PRISMARINE : prism(x, y, z));
    }
    if (pier) W.set(x, 10, z, B.PRISMARINE_WALL);
  }
  // the keep: 19 x 19, 11 high, stepped roof
  const K0 = M - 9, K1 = M + 9;
  for (let y = 1; y <= 11; y++) W.walls(K0, y, K0, K1, y, K1, y === 6 ? B.DARK_PRISMARINE : prism);
  for (let s = 0; s < 5; s++) {
    const y = 12 + s, a = K0 + s * 2, b = K1 - s * 2;
    if (a > b) break;
    W.fillBy(a, y, a, b, y, b, prism);
    if (b - a > 2) W.fill(a + 1, y, a + 1, b - 1, y, b - 1, B.PRISMARINE_BRICKS);
    for (const [x, z] of [[a, a], [b, a], [a, b], [b, b]]) W.set(x, y + 1, z, B.SEA_LANTERN);
  }
  W.set(M, 22, M, B.SEA_LANTERN);
  // gates on all four sides, the front one wide
  for (let y = 1; y <= 4; y++) for (let k = -1; k <= 1; k++) { WATER(M + k, y, K0); WATER(M + k, y, K1); WATER(K0, y, M + k); WATER(K1, y, M + k); }
  for (let y = 1; y <= 5; y++) for (let k = -2; k <= 2; k++) WATER(M + k, y, 2);
  // inner ring of pillars with lanterns
  for (const [x, z] of [[K0 + 3, K0 + 3], [K1 - 3, K0 + 3], [K0 + 3, K1 - 3], [K1 - 3, K1 - 3]]) { W.fill(x, 1, z, x, 10, z, B.DARK_PRISMARINE); W.set(x, 6, z, B.SEA_LANTERN); }
  // treasure chamber: dark prismarine room with one door, a dais, gold and a chest
  const T0 = M - 3, T1 = M + 3;
  for (let y = 1; y <= 5; y++) W.walls(T0, y, T0, T1, y, T1, B.DARK_PRISMARINE);
  W.fill(T0, 6, T0, T1, 6, T1, B.DARK_PRISMARINE);
  for (let y = 1; y <= 3; y++) WATER(M, y, T0);
  W.fill(M - 1, 1, M - 1, M + 1, 1, M + 1, B.PRISMARINE_BRICKS);
  W.set(M, 2, M, B.GOLD_BLOCK);
  W.set(M - 1, 2, M - 1, B.SEA_LANTERN); W.set(M + 1, 2, M + 1, B.SEA_LANTERN);
  W.chest(M + 1, 2, M - 1, 2, LOOT.MONUMENT_TREASURE);
  W.set(M, 5, M, B.SEA_LANTERN);
  // a few wet sponges in the corners of the keep, like forgotten stores
  for (const [x, z] of [[K0 + 1, K1 - 1], [K1 - 1, K0 + 1]]) if (W.r(x, 1, z, 61) < 0.7) W.set(x, 1, z, B.WET_SPONGE);
};

// ===================================================================== stronghold
// An underground warren on a grid of 15-block cells: from a fountain hub a random tree of rooms
// (library, prison, storeroom, pillar crossing, well) joined by stone-brick corridors, and at the
// far end the sealed gate chamber over a lava moat with a skeleton spawner.
const SH_CELL = 15, SH_N = 7;
WorldGen.prototype.strongholdStart = function (cx, cz) {
  const bio = this.biomeAt(cx * 16 + 8, cz * 16 + 8);
  if (BPROP[bio] && BPROP[bio].ocean) return null;
  const rnd = mulberry(hash2i(this.seed + 72411097 * 3, cx, cz));
  const ground = this.surfH(cx * 16 + 8, cz * 16 + 8);
  const y = Math.max(WORLD_MIN_Y + 12, Math.min(ground - 30, 10 + ((rnd() * 25) | 0)));
  // random spanning tree over a 7 x 7 grid from the centre, up to 16 cells
  const C = (SH_N >> 1), cell = new Map(), key = (i, j) => i * 16 + j;
  cell.set(key(C, C), { i: C, j: C, kind: 'hub', d: 0, links: [] });
  const open = [cell.get(key(C, C))];
  while (open.length && cell.size < 16) {
    const a = open[(rnd() * open.length) | 0], dirs = [[1, 0], [-1, 0], [0, 1], [0, -1]];
    const free = dirs.filter(([di, dj]) => { const i = a.i + di, j = a.j + dj; return i >= 0 && j >= 0 && i < SH_N && j < SH_N && !cell.has(key(i, j)); });
    if (!free.length) { open.splice(open.indexOf(a), 1); continue; }
    const [di, dj] = free[(rnd() * free.length) | 0], b = { i: a.i + di, j: a.j + dj, kind: '', d: a.d + 1, links: [] };
    a.links.push([di, dj]); b.links.push([-di, -dj]);
    cell.set(key(b.i, b.j), b); open.push(b);
  }
  const list = [...cell.values()];
  let far = list[0]; for (const c of list) if (c.d > far.d) far = c;
  far.kind = 'gate';
  const kinds = ['library', 'prison', 'store', 'crossing', 'well', 'crossing', 'store', 'prison'];
  for (const c of list) if (!c.kind) c.kind = kinds[(rnd() * kinds.length) | 0];
  const X = cx * 16 - C * SH_CELL, Z = cz * 16 - C * SH_CELL;
  return list.map(c => ownPiece(this.strongholdCell, X + c.i * SH_CELL, y, Z + c.j * SH_CELL, 0, SH_CELL, 11, SH_CELL, { cell: c, salt: (rnd() * 1e6) | 0, underground: true }));
};
WorldGen.prototype.strongholdCell = function (p) {
  const W = this.ownBuilder(p), c = p.cell, M = 7;
  const brick = (x, y, z) => { const r = W.r(x, y, z, 80); return r < 0.15 ? B.CRACKED_STONE_BRICKS : r < 0.3 ? B.MOSSY_STONE_BRICKS : B.STONE_BRICKS; };
  // corridors to the linked neighbours: 5 wide, 5 high, from the room edge to the cell edge
  for (const [di, dj] of c.links) {
    const vert = di === 0, len = M;
    for (let t = 0; t <= len; t++) for (let k = -2; k <= 2; k++) for (let y = 0; y <= 4; y++) {
      const along = di > 0 || dj > 0 ? M + t : M - t, x = vert ? M + k : along, z = vert ? along : M + k;
      const edge = k === -2 || k === 2 || y === 0 || y === 4;
      W.set(x, y, z, edge ? brick(x, y, z) : 0);
    }
    // a torch now and then
    const t = 7, along = di > 0 || dj > 0 ? M + t : M - t;
    if (vert) W.set(M - 1, 2, along, B.TORCH, 1 + 1); else W.set(along, 2, M - 1, B.TORCH, 1 + 2);
  }
  const room = (r, h) => {
    for (let y = 0; y <= h; y++) for (let z = M - r; z <= M + r; z++) for (let x = M - r; x <= M + r; x++) {
      const edge = x === M - r || x === M + r || z === M - r || z === M + r || y === 0 || y === h;
      W.set(x, y, z, edge ? brick(x, y, z) : 0);
    }
    // doorways where corridors arrive
    for (const [di, dj] of c.links) {
      const x = M + di * r, z = M + dj * r;
      for (let k = -1; k <= 1; k++) for (let y = 1; y <= 3; y++) W.set(di ? x : M + k, y, dj ? z : M + k, 0);
    }
  };
  if (c.kind === 'hub') {
    room(6, 7);
    W.fill(M - 2, 1, M - 2, M + 2, 1, M + 2, B.STONE_BRICKS); W.fill(M - 1, 1, M - 1, M + 1, 1, M + 1, B.WATER);
    W.fill(M, 1, M, M, 3, M, B.CHISELED_STONE_BRICKS); W.set(M, 4, M, B.LANTERN, 0);
    for (const [x, z] of [[M - 5, M - 5], [M + 5, M - 5], [M - 5, M + 5], [M + 5, M + 5]]) { W.fill(x, 1, z, x, 6, z, B.CHISELED_STONE_BRICKS); W.set(x, 5, z + (z < M ? 1 : -1), B.TORCH, 1 + (z < M ? 2 : 0)); }
  } else if (c.kind === 'library') {
    room(6, 10);
    for (let y = 1; y <= 9; y++) for (let k = M - 5; k <= M + 5; k++) for (const [x, z] of [[M - 5, k], [M + 5, k]]) if (y !== 5 && !(Math.abs(k - M) <= 1 && y <= 3)) W.set(x, y, z, B.BOOKSHELF);
    for (let x = M - 4; x <= M + 4; x++) for (const z of [M - 5, M + 5]) for (let y = 1; y <= 4; y++) if (Math.abs(x - M) > 1) W.set(x, y, z, B.BOOKSHELF);
    W.fill(M - 4, 5, M - 4, M + 4, 5, M + 4, B.DARK_PLANKS); W.fill(M - 2, 5, M - 2, M + 2, 5, M + 2, 0);   // gallery
    for (let k = M - 3; k <= M + 3; k++) { W.set(k, 6, M - 3, B.OAK_FENCE); W.set(k, 6, M + 3, B.OAK_FENCE); W.set(M - 3, 6, k, B.OAK_FENCE); W.set(M + 3, 6, k, B.OAK_FENCE); }
    for (let y = 1; y <= 5; y++) W.set(M + 4, y, M + 4, B.LADDER, 2);
    W.set(M, 9, M, B.LANTERN, 1);
    W.chest(M - 4, 1, M - 3, 3, LOOT.STRONGHOLD_LIBRARY); W.chest(M - 4, 6, M + 3, 3, LOOT.STRONGHOLD_LIBRARY);
  } else if (c.kind === 'prison') {
    room(6, 6);
    for (const zs of [-1, 1]) for (let x = M - 5; x <= M + 5; x++) for (let y = 1; y <= 5; y++) {
      const z = M + zs * 2;
      if (Math.abs(x - M) <= 1) continue;
      W.set(x, y, z, (x - M) % 3 === 0 ? B.STONE_BRICKS : B.IRON_BARS);
    }
    W.set(M - 4, 1, M - 4, B.COBWEB); W.set(M + 4, 1, M + 4, B.COBWEB);
    W.set(M, 5, M, B.LANTERN, 1);
  } else if (c.kind === 'store') {
    room(5, 5);
    W.chest(M - 4, 1, M, 3, LOOT.STRONGHOLD_STORE);
    for (let z = M - 4; z <= M + 4; z += 2) { W.set(M + 4, 1, z, B.BARREL, 4); }
    W.set(M - 4, 1, M - 4, B.CRAFTING_TABLE); W.set(M - 4, 1, M + 4, B.FURNACE, 3);
    W.set(M, 4, M, B.LANTERN, 1);
  } else if (c.kind === 'well') {
    room(5, 6);
    W.fill(M - 1, 0, M - 1, M + 1, 0, M + 1, B.WATER); W.walls(M - 2, 1, M - 2, M + 2, 1, M + 2, B.STONEBRICK_SLAB);
    W.fill(M - 1, 1, M - 1, M + 1, 1, M + 1, 0);
    for (const [x, z] of [[M - 2, M - 2], [M + 2, M + 2]]) W.fill(x, 2, z, x, 5, z, B.STONE_BRICKS_WALL);
    W.set(M, 5, M, B.LANTERN, 1);
  } else if (c.kind === 'crossing') {
    room(5, 7);
    for (const [x, z] of [[M - 2, M - 2], [M + 2, M - 2], [M - 2, M + 2], [M + 2, M + 2]]) { W.fill(x, 1, z, x, 6, z, B.STONE_BRICKS); W.set(x, 3, z + (z < M ? -1 : 1), B.TORCH, 1 + (z < M ? 0 : 2)); }
  } else if (c.kind === 'gate') {
    room(6, 8);
    // lava moat round a raised dais, the sealed gate of end stone bricks and obsidian, a stair up
    W.fill(M - 4, 0, M - 4, M + 4, 0, M + 4, B.LAVA);
    W.fill(M - 2, 0, M - 2, M + 2, 1, M + 2, B.STONE_BRICKS);
    for (let k = -2; k <= 2; k++) for (const [x, z] of [[M + k, M - 2], [M + k, M + 2], [M - 2, M + k], [M + 2, M + k]]) W.set(x, 2, z, (k + 2) % 2 ? B.END_STONE_BRICKS : B.OBSIDIAN);
    W.fill(M - 1, 1, M - 1, M + 1, 1, M + 1, B.CHISELED_STONE_BRICKS);
    for (let k = M - 4; k < M - 2; k++) W.set(M, 0, k, B.STONE_BRICKS);
    W.set(M, 1, M - 3, B.STONEBRICK_STAIRS, 0);
    W.spawner(M, 2, M, 'skeleton', this);
    for (const [x, z] of [[M - 5, M - 5], [M + 5, M + 5], [M - 5, M + 5], [M + 5, M - 5]]) W.set(x, 6, z, B.LANTERN, 1);
  }
};

// ===================================================================== sand temple (desert)
// A walled courtyard with sandstone obelisks at the corners; in it a square temple behind a portico
// of two columns, a stepped crown on its flat roof, an inlaid floor (own pattern of terracotta), and
// under it a burial vault with a sarcophagus and three chests, reached by a ladder.
WorldGen.prototype.desertPyramidStart = function (cx, cz) {
  if (this.biomeAt(cx * 16 + 8, cz * 16 + 8) !== BI.DESERT) return null;
  const rnd = mulberry(hash2i(this.seed + 47120389 * 3, cx, cz)), rot = (rnd() * 4) | 0;
  const p = ownPiece(this.sandTemple, cx * 16, 0, cz * 16, rot, 19, 19, 19, { noTrees: true });
  const f = this.footprintH(p.box);
  if (f.hi - f.lo > 7) return null;
  p.y = f.lo - 8; p.box[1] = p.y; p.box[4] = p.y + 18;
  return [p];
};
WorldGen.prototype.sandTemple = function (p) {
  const W = this.ownBuilder(p), F = 8;
  const sandy = (x, y, z) => W.r(x, y, z, 90) < 0.25 ? B.SANDSTONE : B.CUT_SANDSTONE;
  W.fill(0, F + 1, 0, 18, 18, 18, 0);
  for (let z = 0; z < 19; z++) for (let x = 0; x < 19; x++) W.down(x, F, z, B.SANDSTONE);
  W.fill(0, F, 0, 18, F, 18, B.SMOOTH_SANDSTONE);
  // courtyard wall (2 high) with a gate, obelisks at the corners
  W.walls(0, F + 1, 0, 18, F + 2, 18, sandy);
  W.fill(8, F + 1, 0, 10, F + 2, 0, 0);
  for (const [x, z] of [[0, 0], [18, 0], [0, 18], [18, 18]]) { W.fill(x, F + 1, z, x, F + 6, z, B.CHISELED_SANDSTONE); W.set(x, F + 7, z, B.SMOOTH_SANDSTONE_SLAB); }
  // the temple 11 x 11, walls 6 high, a portico of two columns with a lintel
  W.walls(4, F + 1, 5, 14, F + 6, 15, sandy);
  W.fill(5, F + 1, 6, 13, F + 6, 14, 0);
  W.fill(8, F + 1, 5, 10, F + 4, 5, 0);
  for (const x of [7, 11]) W.fill(x, F + 1, 3, x, F + 5, 3, B.CHISELED_SANDSTONE);
  W.fill(6, F + 6, 2, 12, F + 6, 4, B.SMOOTH_SANDSTONE_SLAB);
  for (const z of [8, 12]) for (const x of [4, 14]) W.set(x, F + 3, z, 0);
  // roof and a stepped crown
  W.fill(4, F + 7, 5, 14, F + 7, 15, B.SMOOTH_SANDSTONE);
  for (let s = 1; s <= 3; s++) W.fill(4 + s * 2 - 1, F + 7 + s, 5 + s * 2 - 1, 14 - s * 2 + 1, F + 7 + s, 15 - s * 2 + 1, s === 3 ? B.CHISELED_SANDSTONE : B.CUT_SANDSTONE);
  // inlaid floor: an orange border, a blue diamond, white corners
  for (let z = 6; z <= 14; z++) for (let x = 5; x <= 13; x++) {
    const dx = Math.abs(x - 9), dz = Math.abs(z - 10), d = dx + dz;
    const id = x === 5 || x === 13 || z === 6 || z === 14 ? B.TERRA_ORANGE : d === 3 ? B.BLUE_TERRACOTTA : d === 0 ? B.YELLOW_TERRACOTTA : (dx === 3 && dz === 3) ? B.TERRA_WHITE : B.SMOOTH_SANDSTONE;
    W.set(x, F, z, id);
  }
  // the vault 7 x 7 below, ladder in the back corner
  W.walls(5, F - 6, 6, 13, F - 1, 14, B.SANDSTONE);
  W.fill(5, F - 7, 6, 13, F - 7, 14, B.SANDSTONE);
  W.fill(6, F - 6, 7, 12, F - 2, 13, 0);
  W.fill(6, F - 1, 7, 12, F - 1, 13, B.SANDSTONE);
  for (let y = F - 6; y <= F; y++) W.set(12, y, 13, B.LADDER, 2);
  W.fill(8, F - 6, 9, 10, F - 6, 11, B.CUT_SANDSTONE);
  W.set(9, F - 5, 10, B.SMOOTH_SANDSTONE); W.set(9, F - 5, 11, B.SMOOTH_SANDSTONE_SLAB);
  W.chest(6, F - 6, 10, 3, LOOT.DESERT_PYRAMID); W.chest(12, F - 6, 9, 1, LOOT.DESERT_PYRAMID); W.chest(9, F - 6, 7, 0, LOOT.DESERT_PYRAMID);
  W.set(6, F - 4, 7, B.TORCH, 1 + 2); W.set(12, F - 4, 7, B.TORCH, 1 + 2);
};

// ===================================================================== bog shack (swamp)
// A mud-brick shack on a plank deck raised on mangrove stilts with braces, a porch with a ladder,
// a thatched hay roof, and inside a cauldron, a brewing stand, a barrel and a potted mushroom.
WorldGen.prototype.swampHutStart = function (cx, cz) {
  const bio = this.biomeAt(cx * 16 + 8, cz * 16 + 8);
  if (bio !== BI.SWAMP && bio !== BI.MANGROVE_SWAMP) return null;
  const rnd = mulberry(hash2i(this.seed + 55301927 * 3, cx, cz)), rot = (rnd() * 4) | 0;
  const p = ownPiece(this.bogShack, cx * 16 + 3, 0, cz * 16 + 3, rot, 9, 8, 9, { noTrees: true, margin: 1 });
  const f = this.footprintH(p.box);
  p.y = Math.max(f.avg, SEA + 1) + 2; p.box[1] = p.y - 4; p.box[4] = p.y + 7;
  return [p];
};
WorldGen.prototype.bogShack = function (p) {
  const W = this.ownBuilder(p);
  W.fill(0, 1, 0, 8, 7, 8, 0);
  W.fill(0, 0, 0, 8, 0, 8, B.SPRUCE_PLANKS);
  for (const [x, z] of [[0, 0], [8, 0], [0, 8], [8, 8], [4, 8]]) { W.down(x, -1, z, B.MANGROVE_LOG); }
  for (const [x, z, d] of [[1, 0, 1], [7, 0, 3], [1, 8, 1], [7, 8, 3]]) W.set(x, -1, z, B.MANGROVE_PLANKS_FENCE);
  // walls 3 high, mangrove log corners, a door and fence windows
  W.walls(1, 1, 2, 7, 3, 7, (x, y, z) => (x === 1 || x === 7) && (z === 2 || z === 7) ? B.MANGROVE_LOG : B.MUD_BRICKS);
  W.set(4, 1, 2, B.SPRUCE_DOOR, 2); W.set(4, 2, 2, B.SPRUCE_DOOR, 2 | 4);
  for (const z of [4, 5]) { W.set(1, 2, z, B.MANGROVE_PLANKS_FENCE); W.set(7, 2, z, B.MANGROVE_PLANKS_FENCE); }
  // porch railing and a ladder down from it
  for (let x = 0; x <= 8; x++) if (x !== 4) W.set(x, 1, 0, B.MANGROVE_PLANKS_FENCE);
  W.set(0, 1, 1, B.MANGROVE_PLANKS_FENCE); W.set(8, 1, 1, B.MANGROVE_PLANKS_FENCE);
  // the ladder hangs on a post under the deck edge and reaches down to the ground
  W.down(4, -1, 1, B.MANGROVE_LOG);
  for (let y = 0; y >= -8; y--) {
    const c = W.get(4, y, 0);
    if (y < 0 && c > 0 && !isLiquidId(c) && !(FLAGS[c] & BF_REPLACE)) break;
    W.set(4, y, 0, B.LADDER, 2);
  }
  W.set(4, 0, 1, B.SPRUCE_PLANKS);
  // thatched roof: three stepped layers of hay
  W.fill(0, 4, 1, 8, 4, 8, B.HAY_BLOCK); W.fill(1, 5, 2, 7, 5, 7, B.HAY_BLOCK); W.fill(2, 6, 3, 6, 6, 6, B.HAY_BLOCK); W.fill(3, 7, 4, 5, 7, 5, B.HAY_BLOCK);
  // inside
  W.set(2, 1, 6, B.CAULDRON, 3); W.set(3, 1, 6, B.BREWING_STAND, 0); W.set(6, 1, 6, B.BARREL, 4);
  W.set(6, 1, 3, B.CRAFTING_TABLE); W.set(2, 1, 3, B.FLOWER_POT); W.set(4, 3, 5, B.LANTERN, 1);
};

// ===================================================================== old mines
// A hub cavern with a dirt floor; from it timbered tunnels grow: straight runs (with rails one time
// in three), junction chambers, turns, ramps down a level, dead ends with a supply chest. Posts and
// beams every four blocks, plank bridges over gaps, cobwebs; one tunnel in fifteen is a spider den.
const MINE_MAX = 40;
WorldGen.prototype.mineStart = function (cx, cz) {
  const rnd = mulberry(hash2i(this.seed + 22037 * 3, cx, cz)), ri = (n) => (rnd() * n) | 0;
  const ground = this.floorH(cx * 16 + 8, cz * 16 + 8);
  const y = Math.min(ground - 16, 6 + ri(36));
  if (y < WORLD_MIN_Y + 12) return null;
  const mesa = !!(BPROP[this.biomeAt(cx * 16 + 8, cz * 16 + 8)] || {}).badlands;
  const segs = [], boxes = [];
  // overlap is tested on the dug cores only (a tunnel's walls may touch its neighbours)
  const free = (b) => { for (const a of boxes) if (a[0] <= b[3] && a[3] >= b[0] && a[1] <= b[4] && a[4] >= b[1] && a[2] <= b[5] && a[5] >= b[2]) return false; return true; };
  const add = (s) => { segs.push(s); boxes.push(s.core || s.box); return s; };
  const X = cx * 16 + 4, Z = cz * 16 + 4, DX = [0, -1, 0, 1], DZ = [1, 0, -1, 0];
  add({ kind: 'hub', box: [X - 4, y, Z - 4, X + 4, y + 5, Z + 4], y });
  const queue = [[X, y, Z - 5, 2, 0], [X, y, Z + 5, 0, 0], [X - 5, y, Z, 1, 0], [X + 5, y, Z, 3, 0]];
  while (queue.length && segs.length < MINE_MAX) {
    const [x, yy, z, d, depth] = queue.shift();
    if (depth > 9 || Math.abs(x - X) > 72 || Math.abs(z - Z) > 72) continue;
    const r = rnd();
    if (r < 0.12) {                                         // ramp: 8 long, 4 down
      const ex = x + DX[d] * 7, ez = z + DZ[d] * 7;
      const box = [Math.min(x, ex) - 1, yy - 5, Math.min(z, ez) - 1, Math.max(x, ex) + 1, yy + 3, Math.max(z, ez) + 1];
      if (!free(box)) continue;
      add({ kind: 'ramp', box, x, y: yy, z, d });
      queue.push([ex + DX[d], yy - 4, ez + DZ[d], d, depth + 1]);
      continue;
    }
    const len = 4 * (2 + ri(4)), ex = x + DX[d] * (len - 1), ez = z + DZ[d] * (len - 1);
    const lx = d & 1 ? 0 : 1, lz = d & 1 ? 1 : 0;      // sideways extent of the dug part
    const core = [Math.min(x, ex) - lx, yy - 1, Math.min(z, ez) - lz, Math.max(x, ex) + lx, yy + 3, Math.max(z, ez) + lz];
    const box = [core[0] - lx, core[1], core[2] - lz, core[3] + lx, core[4], core[5] + lz];
    if (!free(core)) continue;
    const kr = rnd();
    add({ kind: 'tunnel', box, core, x, y: yy, z, d, len, rails: kr < 0.33, spider: kr > 0.93, chest: rnd() < 0.15 });
    // side branches every 8 blocks
    for (let k = 4; k < len - 2; k += 8) if (rnd() < 0.3) {
      const sx = x + DX[d] * k, sz = z + DZ[d] * k, sd = rnd() < 0.5 ? (d + 1) & 3 : (d + 3) & 3;
      queue.push([sx + DX[sd] * 2, yy, sz + DZ[sd] * 2, sd, depth + 1]);
    }
    const e = rnd(), nx = ex + DX[d], nz = ez + DZ[d];
    if (e < 0.3) {                                          // junction chamber 5 x 5
      const jx = nx + DX[d] * 2, jz = nz + DZ[d] * 2, jb = [jx - 2, yy - 1, jz - 2, jx + 2, yy + 4, jz + 2];
      if (free(jb)) {
        add({ kind: 'junction', box: jb, x: jx, y: yy, z: jz });
        for (const nd of [d, (d + 1) & 3, (d + 3) & 3]) queue.push([jx + DX[nd] * 3, yy, jz + DZ[nd] * 3, nd, depth + 1]);
      }
    } else if (e < 0.75) queue.push([nx, yy, nz, rnd() < 0.5 ? d : (rnd() < 0.5 ? (d + 1) & 3 : (d + 3) & 3), depth + 1]);
  }
  return segs.map(sg => {
    const b = sg.box;
    return { build: this.mineSegment, x: b[0], y: b[1], z: b[2], rot: 0, box: b.slice(), seg: sg, mesa };
  });
};
WorldGen.prototype.mineSegment = function (p) {
  const ids = this.ids, meta = this.meta, x0 = this.x0, z0 = this.z0, sg = p.seg, seed = this.seed;
  const PL = p.mesa ? B.DARK_PLANKS : B.PLANKS, LG = p.mesa ? B.DARK_LOG : B.LOG;
  const at = (x, y, z) => x >= x0 && x < x0 + 16 && z >= z0 && z < z0 + 16 && y > WORLD_MIN_Y + 1 && y < WORLD_MAX_Y ? CI(x - x0, y, z - z0) : -1;
  const liquid = (i) => { const c = ids[i]; return isLiquidId(c) || (FLAGS[c] & BF_AQUATIC) !== 0; };
  const set = (x, y, z, id, m) => { const i = at(x, y, z); if (i >= 0 && !liquid(i)) { ids[i] = id; meta[i] = m || 0; } };
  const get = (x, y, z) => { const i = at(x, y, z); return i >= 0 ? ids[i] : -1; };
  const r = (x, y, z, k) => hash3(seed + 2300 + k, x, y, z);
  // air for a walkway cell column (floor at y), bridging the floor with planks over gaps
  const walk = (x, y, z, h) => {
    if (at(x, y, z) < 0) return;
    for (let k = 0; k < h; k++) set(x, y + k, z, 0);
    const u = get(x, y - 1, z);
    if (u >= 0 && !OPAQUE[u]) set(x, y - 1, z, PL);
  };
  if (sg.kind === 'hub' || sg.kind === 'junction') {
    const [ax, ay, az, bx, by, bz] = sg.box, fy = sg.y;
    for (let z = az; z <= bz; z++) for (let x = ax; x <= bx; x++) {
      walk(x, fy, z, by - fy);
      if (sg.kind === 'hub') set(x, fy - 1, z, r(x, fy, z, 1) < 0.15 ? B.GRAVEL : B.DIRT);
    }
    for (const [x, z] of [[ax, az], [bx, az], [ax, bz], [bx, bz]]) for (let y = fy; y < by; y++) set(x, y, z, LG);
    for (let x = ax; x <= bx; x++) { set(x, by - 1, az, PL); set(x, by - 1, bz, PL); }
    set((ax + bx) >> 1, by - 2, (az + bz) >> 1, B.LANTERN, 1);
    set((ax + bx) >> 1, by - 1, (az + bz) >> 1, PL);
    return;
  }
  const DX = [0, -1, 0, 1], DZ = [1, 0, -1, 0], d = sg.d, sxd = DZ[d], szd = -DX[d];   // (sxd, szd): sideways
  if (sg.kind === 'ramp') {
    for (let k = 0; k < 8; k++) {
      const fy = sg.y - (k >> 1), cx = sg.x + DX[d] * k, cz = sg.z + DZ[d] * k;
      for (let c = -1; c <= 1; c++) walk(cx + sxd * c, fy, cz + szd * c, 3 + (k & 1));
    }
    return;
  }
  // tunnel
  for (let k = 0; k < sg.len; k++) {
    const cx = sg.x + DX[d] * k, cz = sg.z + DZ[d] * k, fy = sg.y;
    for (let c = -1; c <= 1; c++) {
      const x = cx + sxd * c, z = cz + szd * c;
      walk(x, fy, z, 3);
      if (sg.spider ? r(x, fy, z, 2) < 0.35 : (c !== 0 && r(x, fy + 2, z, 3) < 0.05)) set(x, fy + (sg.spider ? (r(x, fy, z, 4) * 3) | 0 : 2), z, B.COBWEB);
    }
    if (sg.rails && get(cx, fy - 1, cz) > 0) set(cx, fy, cz, B.RAIL, d & 1 ? 1 : 0);
    if (k % 4 === 2) {
      // timber frame: two posts and a beam (now and then fallen: only the posts)
      for (const c of [-1, 1]) { set(cx + sxd * c, fy, cz + szd * c, LG); set(cx + sxd * c, fy + 1, cz + szd * c, B.OAK_FENCE); }
      if (r(cx, fy, cz, 5) > 0.15) for (let c = -1; c <= 1; c++) set(cx + sxd * c, fy + 2, cz + szd * c, PL);
      else set(cx, fy, cz, B.GRAVEL);
      if (r(cx, fy, cz, 6) < 0.12) { const tx = cx - DX[d], tz = cz - DZ[d]; set(tx, fy + 2, tz, B.TORCH, 1 + d); }
    }
  }
  const mx = sg.x + DX[d] * (sg.len >> 1), mz = sg.z + DZ[d] * (sg.len >> 1);
  if (sg.spider) {
    const i = at(mx, sg.y, mz);
    if (i >= 0) { ids[i] = B.SPAWNER; meta[i] = SPAWNER_MOB.cave_spider; this.spawnerList.push(mx, sg.y, mz); }
  }
  if (sg.chest) {
    // a niche in the side wall with a supply chest facing the tunnel
    const nx = mx + sxd * 2, nz = mz + szd * 2, i = at(nx, sg.y, nz);
    if (i >= 0 && !liquid(i)) { ids[i] = B.CHEST; meta[i] = ((d + 1) & 3) | (LOOT.ABANDONED_MINESHAFT << 2); set(nx, sg.y + 1, nz, 0); }
  }
};

// ===================================================================== dungeons
// A monster den dug into the rock: a cobblestone room with rounded corners (solid corner columns),
// a vaulted ceiling (its outer ring one lower), a floor of mossy cobble, cobble and gravel, the
// spawner in the middle, one or two chests standing against the walls, a heap of bones in a corner
// and cobwebs up under the vault. Kept only where the rock around is solid and a cave or tunnel
// opens into it at one to five places.
WorldGen.prototype.dungeons = function () {
  const ids = this.ids, meta = this.meta;
  const rnd = mulberry(hash2i(this.seed + 1301, this.cx, this.cz)), ri = (n) => (rnd() * n) | 0;
  const solid = (x, y, z) => OPAQUE[ids[CI(x, y, z)]] === 1;
  const air = (x, y, z) => ids[CI(x, y, z)] === 0;
  const room = (yLo, yHi) => {
    const xr = 2 + ri(2), zr = 2 + ri(2);
    const ox = xr + 1 + ri(14 - 2 * xr), oz = zr + 1 + ri(14 - 2 * zr), oy = yLo + ri(yHi - yLo + 1);
    if (oy - 1 <= WORLD_MIN_Y || oy + 5 >= WORLD_MAX_Y) return;
    const ax = ox - xr - 1, bx = ox + xr + 1, az = oz - zr - 1, bz = oz + zr + 1, H = 4;
    let open = 0;
    for (let x = ax; x <= bx; x++) for (let z = az; z <= bz; z++) {
      if (!solid(x, oy - 1, z) || !solid(x, oy + H, z)) return;
      if ((x === ax || x === bx || z === az || z === bz) && air(x, oy, z) && air(x, oy + 1, z)) open++;
    }
    if (open < 1 || open > 5) return;
    const stone = () => rnd() < 0.4 ? B.MOSSY : B.COBBLE;
    const floor = () => { const r = rnd(); return r < 0.55 ? B.MOSSY : r < 0.88 ? B.COBBLE : B.GRAVEL; };
    for (let x = ax; x <= bx; x++) for (let z = az; z <= bz; z++) for (let y = oy - 1; y <= oy + H; y++) {
      const i = CI(x, y, z), wall = x === ax || x === bx || z === az || z === bz;
      // rounded corners: the cells next to the corners stay solid inside the room
      const corner = (x === ax + 1 || x === bx - 1) && (z === az + 1 || z === bz - 1);
      // the vault: the outer ring of the top layer is solid
      const vault = y === oy + H - 1 && (x === ax + 1 || x === bx - 1 || z === az + 1 || z === bz - 1);
      if (!wall && y >= oy && y < oy + H && !corner && !vault) { ids[i] = 0; meta[i] = 0; continue; }
      if (wall && y >= oy && y < oy + 2 && ids[i] === 0) continue;       // the openings stay open
      ids[i] = y === oy - 1 ? floor() : stone(); meta[i] = 0;
    }
    // chests on the floor against a wall, facing into the room
    let chests = 1 + ri(2);
    for (let t = 0; t < 10 && chests > 0; t++) {
      const side = ri(4), k = side < 2 ? ax + 2 + ri(bx - ax - 3) : az + 2 + ri(bz - az - 3);
      const [x, z, face] = side === 0 ? [k, az + 1, 0] : side === 1 ? [k, bz - 1, 2] : side === 2 ? [ax + 1, k, 3] : [bx - 1, k, 1];
      if (Math.abs(x - ox) + Math.abs(z - oz) < 2) continue;
      const i = CI(x, oy, z);
      if (ids[i] !== 0) continue;
      ids[i] = B.CHEST; meta[i] = face | (LOOT.SIMPLE_DUNGEON << 2); chests--;
    }
    // a heap of bones beside a corner column, cobwebs under the vault
    const cx2 = rnd() < 0.5 ? ax + 2 : bx - 2, cz2 = rnd() < 0.5 ? az + 1 : bz - 1;
    if (ids[CI(cx2, oy, cz2)] === 0 && rnd() < 0.6) ids[CI(cx2, oy, cz2)] = B.BONE_BLOCK;
    for (let x = ax + 2; x <= bx - 2; x++) for (let z = az + 2; z <= bz - 2; z++) {
      const i = CI(x, oy + H - 2, z);
      if (ids[i] === 0 && (x === ax + 2 || x === bx - 2 || z === az + 2 || z === bz - 2) && rnd() < 0.15) ids[i] = B.COBWEB;
    }
    const i = CI(ox, oy, oz);
    ids[i] = B.SPAWNER; meta[i] = [SPAWNER_MOB.skeleton, SPAWNER_MOB.zombie, SPAWNER_MOB.zombie, SPAWNER_MOB.spider][ri(4)];
    this.spawnerList.push(this.x0 + ox, oy, this.z0 + oz);
  };
  for (let k = 0; k < 10; k++) room(0, WORLD_MAX_Y - 1);
  for (let k = 0; k < 4; k++) room(WORLD_MIN_Y + 6, -1);
};

// structure sets of these (spacing / separation in chunks, own salts)
STRUCTURE_SETS.push(
  { salt: 47120389, spacing: 32, separation: 8, reach: 2, clearTrees: true, start: function (x, z) { return this.desertPyramidStart(x, z); } },
  { salt: 55301927, spacing: 32, separation: 8, reach: 1, clearTrees: true, start: function (x, z) { return this.swampHutStart(x, z); } },
  { salt: 22037, spacing: 16, separation: 4, reach: 6, start: function (x, z) { return this.mineStart(x, z); } },
  { salt: 61210487, clearTrees: true, spacing: 32, separation: 8, reach: 1, start: function (x, z) { return this.jungleTempleStart(x, z); } },
  { salt: 33590147, clearTrees: true, spacing: 28, separation: 10, reach: 1, start: function (x, z) { return this.ruinedPortalStart(x, z); } },
  { salt: 10387319, clearTrees: true, spacing: 48, separation: 16, reach: 3, start: function (x, z) { return this.mansionStart(x, z); } },
  { salt: 10387313, clearTrees: true, spacing: 32, separation: 5, reach: 3, start: function (x, z) { return this.monumentStart(x, z); } },
  { salt: 72411097, spacing: 64, separation: 24, reach: 4, start: function (x, z) { return this.strongholdStart(x, z); } },
);
const OWN_TREELESS_SETS = STRUCTURE_SETS.filter(s => s.clearTrees);
// boxes of nearby structures that keep trees out (added to the village boxes the tree check uses)
WorldGen.prototype.structureTreeBoxes = function () {
  const cx = this.cx, cz = this.cz, out = this.vilBoxes || (this.vilBoxes = []);
  for (const set of OWN_TREELESS_SETS) {
    const R = set.reach + 2;
    for (let rz = Math.floor((cz - R) / set.spacing); rz <= Math.floor((cz + R) / set.spacing); rz++)
      for (let rx = Math.floor((cx - R) / set.spacing); rx <= Math.floor((cx + R) / set.spacing); rx++) {
        const [sx, sz] = this.spreadStart(set, rx, rz);
        if (Math.abs(sx - cx) > R || Math.abs(sz - cz) > R) continue;
        const st = this.structStart(set, sx, sz);
        if (st) for (const p of st) if (p.noTrees) out.push([p.box[0] - 9, p.box[1], p.box[2] - 9, p.box[3] + 9, p.box[4], p.box[5] + 9]);   // crowns reach up to 9 out
      }
  }
};

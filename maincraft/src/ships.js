'use strict';
// Ships and rafts: blocks built on water around a ship wheel sail as one vessel.
//
// Using the wheel gathers every block connected to it (building materials only: not ground, stone,
// sand, plants, leaves or fluids), lifts them out of the world (the cells below the waterline turn
// to water) and makes them a vessel: one mesh drawn with the vessel's turn and position, the player
// standing at the wheel. W / S drive it forward and back, A / D turn it; it runs aground on banks
// and shallows. Shift drops anchor: the turn is rounded to a quarter, the position to a block, and
// the blocks go back into the world there (chests and furnaces keep what is in them). A vessel
// under way is saved with the world.

const SHIP_MAX = 6000;
// blocks a vessel never takes along: the ground, rock, loose material, plants and fluids
const SHIP_NATURAL = /^(GRASS|SNOWY_GRASS|DIRT|COARSE_DIRT|ROOTED_DIRT|PODZOL|MYCELIUM|MUD|CLAY|FARMLAND.*|DIRT_PATH|STONE|DEEPSLATE|GRANITE|DIORITE|ANDESITE|TUFF|CALCITE|DRIPSTONE|SMOOTH_BASALT|BASALT|BEDROCK|SAND|RED_SAND|GRAVEL|SANDSTONE|RED_SANDSTONE|SNOW|SNOW_LAYER|ICE|PACKED_ICE|BLUE_ICE|NETHERRACK|MAGMA_BLOCK|SOUL_SAND|SOUL_SOIL|OBSIDIAN|CRYING_OBSIDIAN|TERRACOTTA|TERRA_.*|.*_TERRACOTTA|MOSS_BLOCK|MOSS_CARPET|PALE_MOSS_BLOCK|PALE_MOSS_CARPET|SCULK.*|AMETHYST_BLOCK|BUDDING_AMETHYST|.*AMETHYST_BUD|AMETHYST_CLUSTER|.*_ORE|.*LEAVES|.*CORAL.*|SEA_PICKLE|KELP.*|SEAGRASS|TALL_SEAGRASS|LILY_PAD|BEDROCK|SPAWNER|POINTED_DRIPSTONE|MANGROVE_ROOTS.*|MUDDY_MANGROVE_ROOTS)$/;
const DIR_X = [0, -1, 0, 1], DIR_Z = [1, 0, -1, 0];

function shipMaterial(id) {
  if (!id) return false;
  const d = dryId(id);
  if (isLiquidId(d) || d === B.WATER || d === B.LAVA) return false;
  if (FLAGS[d] & BF_REPLACE) return false;
  return !SHIP_NATURAL.test(B_KEY[d] || '');
}
// cells a vessel may move through or anchor in
function shipFree(id) { return !id || isLiquidId(id) || (FLAGS[id] & BF_REPLACE) !== 0; }

class Ships {
  constructor(game) {
    this.game = game;
    this.ship = null;            // the vessel under way (one at a time: the one being steered)
    this.mesher = null;
    this.msgEl = null;
  }

  // ------------------------------------------------------------------ assembling
  // gather the vessel from the wheel at (x, y, z); returns false (with a message) when it can not sail
  assemble(x, y, z) {
    const g = this.game, w = g.world;
    if (this.ship) return false;
    const key = (a, b, c) => (a - x + 512) * 1048576 + (b - y + 512) * 1024 + (c - z + 512);
    const seen = new Set([key(x, y, z)]), list = [[x, y, z]];
    for (let i = 0; i < list.length; i++) {
      if (list.length > SHIP_MAX) { this.say(T('shipTooBig')); return false; }
      const [cx, cy, cz] = list[i];
      for (let f = 0; f < 6; f++) {
        const nx = cx + (f === 0 ? 1 : f === 1 ? -1 : 0), ny = cy + (f === 2 ? 1 : f === 3 ? -1 : 0), nz = cz + (f === 4 ? 1 : f === 5 ? -1 : 0);
        const k = key(nx, ny, nz);
        if (seen.has(k)) continue;
        seen.add(k);
        if (!w.isLoaded(nx, nz) || ny <= WORLD_MIN_Y || ny >= WORLD_MAX_Y) continue;
        if (shipMaterial(w.getBlock(nx, ny, nz))) list.push([nx, ny, nz]);
      }
    }
    // the blocks with their cells relative to the wheel
    const blocks = [];
    for (const [bx, by, bz] of list) {
      const id = w.getBlock(bx, by, bz);
      blocks.push({ x: bx - x, y: by - y, z: bz - z, id: dryId(id), m: w.getMeta(bx, by, bz) });
    }
    const S = this.makeShip(blocks, [x, y, z], 0, w.getMeta(x, y, z) & 3);
    if (this.support(S, S.pos, 0) <= 0) { this.say(T('shipNoWater')); return false; }
    // the waterline: the highest still water touching the hull from outside
    let line = -1e9;
    for (const [bx, by, bz] of list) for (let f = 0; f < 6; f++) {
      const nx = bx + (f === 0 ? 1 : f === 1 ? -1 : 0), ny = by + (f === 2 ? 1 : f === 3 ? -1 : 0), nz = bz + (f === 4 ? 1 : f === 5 ? -1 : 0);
      if (seen.has(key(nx, ny, nz)) && shipMaterial(w.getBlock(nx, ny, nz))) continue;
      if (w.getBlock(nx, ny, nz) === B.WATER && w.getMeta(nx, ny, nz) === 0) line = Math.max(line, ny);
    }
    // containers travel with their contents
    S.store = this.takeStores(list, x, y, z);
    for (const [bx, by, bz] of list) w.setBlock(bx, by, bz, by <= line ? B.WATER : 0, 0);
    this.ship = S;
    this.board();
    this.say(T('shipHelp'));
    return true;
  }
  makeShip(blocks, pos, yaw, face) {
    const S = { blocks, pos: [pos[0], pos[1], pos[2]], yaw, face, speed: 0, turn: 0, map: new Map(), meshes: null, t: 0 };
    let x0 = 1e9, y0 = 1e9, z0 = 1e9, x1 = -1e9, y1 = -1e9, z1 = -1e9;
    for (const b of blocks) {
      S.map.set(`${b.x},${b.y},${b.z}`, b);
      x0 = Math.min(x0, b.x); y0 = Math.min(y0, b.y); z0 = Math.min(z0, b.z); x1 = Math.max(x1, b.x); y1 = Math.max(y1, b.y); z1 = Math.max(z1, b.z);
    }
    S.box = [x0, y0, z0, x1, y1, z1];
    // hull: blocks with an open side (only those can touch the outside); keel: the lowest block of
    // every column (what rests in the water)
    S.hull = []; const low = new Map();
    for (const b of blocks) {
      let open = false;
      for (const [dx, dy, dz] of [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]]) if (!S.map.has(`${b.x + dx},${b.y + dy},${b.z + dz}`)) { open = true; break; }
      if (open) S.hull.push(b);
      const k = b.x * 4096 + b.z, l = low.get(k);
      if (!l || b.y < l.y) low.set(k, b);
    }
    S.keel = [...low.values()];
    return S;
  }
  // chest / furnace contents of the vessel's cells, keyed by the cell relative to the wheel
  takeStores(list, x, y, z) {
    const g = this.game, out = { chests: {}, furnaces: {} };
    const C = g.meta.chests || (g.meta.chests = {}), F = g.meta.furnaces || (g.meta.furnaces = {});
    g.chests = C;
    for (const [bx, by, bz] of list) {
      const k = `${bx},${by},${bz}`, l = `${bx - x},${by - y},${bz - z}`;
      if (C[k]) { out.chests[l] = C[k]; delete C[k]; }
      if (F[k]) { out.furnaces[l] = F[k]; delete F[k]; }
    }
    return out;
  }

  // ------------------------------------------------------------------ geometry
  // world point of a vessel-local point (cell units, the wheel cell's corner at 0, turned about the
  // wheel cell's centre)
  toWorld(S, pos, yaw, lx, ly, lz) {
    const c = Math.cos(yaw), s = Math.sin(yaw), ax = lx - 0.5, az = lz - 0.5;
    return [pos[0] + 0.5 + ax * c - az * s, pos[1] + ly, pos[2] + 0.5 + ax * s + az * c];
  }
  // how much of the keel sits in water (fraction of its columns) at a pose
  support(S, pos, yaw) {
    const w = this.game.world;
    let n = 0;
    for (const b of S.keel) {
      const p = this.toWorld(S, pos, yaw, b.x + 0.5, b.y + 0.5, b.z + 0.5), X = Math.floor(p[0]), Y = Math.floor(p[1]), Z = Math.floor(p[2]);
      if (isWaterId(w.getBlock(X, Y, Z)) || isWaterId(w.getBlock(X, Y - 1, Z))) n++;
    }
    return n / Math.max(1, S.keel.length);
  }
  // true when a hull block would sit in something solid at a pose
  blocked(S, pos, yaw) {
    const w = this.game.world;
    for (const b of S.hull) {
      const p = this.toWorld(S, pos, yaw, b.x + 0.5, b.y + 0.5, b.z + 0.5);
      if (!w.isLoaded(Math.floor(p[0]), Math.floor(p[2]))) return true;
      if (!shipFree(w.getBlock(Math.floor(p[0]), Math.floor(p[1]), Math.floor(p[2])))) return true;
    }
    return false;
  }

  // ------------------------------------------------------------------ sailing
  board() {
    const p = this.game.player;
    p.vel = [0, 0, 0]; p.flying = false; p.fallDist = 0;
    this.placePlayer(0);
  }
  // the player stands in front of the wheel, turned with the vessel
  placePlayer(dyaw) {
    const S = this.ship, p = this.game.player;
    const fx = DIR_X[S.face], fz = DIR_Z[S.face];
    const q = this.toWorld(S, S.pos, S.yaw, fx + 0.5, 0, fz + 0.5);
    p.pos[0] = q[0]; p.pos[1] = S.pos[1] + this.bob(); p.pos[2] = q[2];
    p.prev = p.pos.slice(); p.prevOf = p.pos;
    p.vel[0] = p.vel[1] = p.vel[2] = 0;
    p.onGround = true; p.inWater = false; p.fallDist = 0;
    p.yaw += dyaw;
  }
  bob() { return this.ship ? Math.sin(this.ship.t * 1.3) * 0.04 : 0; }
  update(dt, keys) {
    const S = this.ship;
    if (!S) return;
    S.t += dt;
    if (keys.sneak) { this.anchor(); return; }
    // throttle and rudder: speed builds up and fades, the turn is quicker when under way
    const thrust = (keys.forward ? 1 : 0) - (keys.back ? 1 : 0);
    const target = thrust > 0 ? 5 : thrust < 0 ? -2 : 0;
    S.speed += Math.sign(target - S.speed) * Math.min(Math.abs(target - S.speed), (thrust ? 1.6 : 1.0) * dt);
    const rud = (keys.left ? 1 : 0) - (keys.right ? 1 : 0);
    S.turn += (rud * (0.35 + Math.min(1, Math.abs(S.speed) / 4) * 0.45) - S.turn) * Math.min(1, dt * 3);
    const dyaw = -S.turn * dt;
    // forward is away from the wheel's face
    const c = Math.cos(S.yaw), s = Math.sin(S.yaw), fx = -DIR_X[S.face], fz = -DIR_Z[S.face];
    const vx = (fx * c - fz * s) * S.speed * dt, vz = (fx * s + fz * c) * S.speed * dt;
    let moved = false;
    const tryPose = (pos, yaw) => {
      if (this.blocked(S, pos, yaw) || this.support(S, pos, yaw) < 0.25) return false;
      S.pos = pos; S.yaw = yaw; return true;
    };
    if (dyaw && tryPose(S.pos, S.yaw + dyaw)) { moved = true; this.placePlayer(dyaw); }
    else if (dyaw) S.turn = 0;
    if (vx || vz) {
      if (tryPose([S.pos[0] + vx, S.pos[1], S.pos[2] + vz], S.yaw)) moved = true;
      else if (tryPose([S.pos[0] + vx, S.pos[1], S.pos[2]], S.yaw) || tryPose([S.pos[0], S.pos[1], S.pos[2] + vz], S.yaw)) { moved = true; S.speed *= 0.5; }
      else S.speed = 0;            // aground
    }
    void moved;
    this.placePlayer(0);
  }

  // ------------------------------------------------------------------ anchoring
  anchor() {
    const S = this.ship, g = this.game, w = g.world;
    if (!S) return true;
    const r = ((Math.round(S.yaw / (Math.PI / 2)) % 4) + 4) % 4;
    const bx = Math.round(S.pos[0]), by = S.pos[1], bz = Math.round(S.pos[2]);
    const fits = (X, Z) => {
      for (const b of S.blocks) {
        const o = WorldGen.tplPos(b.x, b.z, r, 0, 0);
        if (!shipFree(w.getBlock(X + o[0], by + b.y, Z + o[1]))) return false;
      }
      return true;
    };
    let at = null;
    for (const [dx, dz] of [[0, 0], [1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [-1, -1], [1, -1], [-1, 1]]) if (fits(bx + dx, bz + dz)) { at = [bx + dx, bz + dz]; break; }
    if (!at) { this.say(T('shipNoAnchor')); S.speed = 0; return false; }
    const [X, Z] = at;
    const C = g.meta.chests || (g.meta.chests = {}), F = g.meta.furnaces || (g.meta.furnaces = {});
    g.chests = C;
    for (const b of S.blocks) {
      const o = WorldGen.tplPos(b.x, b.z, r, 0, 0), wx = X + o[0], wy = by + b.y, wz = Z + o[1];
      const cur = w.getBlock(wx, wy, wz);
      const id = WET[b.id] && cur === B.WATER ? WET[b.id] : b.id;
      w.setBlock(wx, wy, wz, id, rotMeta(b.id, b.m, r));
      const l = `${b.x},${b.y},${b.z}`, k = `${wx},${wy},${wz}`;
      if (S.store && S.store.chests[l]) C[k] = S.store.chests[l];
      if (S.store && S.store.furnaces[l]) F[k] = S.store.furnaces[l];
    }
    // the player steps off in front of the wheel
    const p = g.player, o = WorldGen.tplPos(DIR_X[S.face], DIR_Z[S.face], r, 0, 0);
    p.pos = [X + o[0] + 0.5, by, Z + o[1] + 0.5]; p.prev = p.pos.slice(); p.prevOf = p.pos; p.vel = [0, 0, 0];
    this.freeMeshes(S);
    this.ship = null;
    this.say(T('shipAnchored'));
    return true;
  }

  // ------------------------------------------------------------------ saving
  serialize() {
    const S = this.ship;
    if (!S) return null;
    return { b: S.blocks.map(b => [b.x, b.y, b.z, B_KEY[b.id], b.m]), pos: S.pos, yaw: S.yaw, face: S.face, store: S.store };
  }
  restore(d) {
    this.ship = null;
    if (!d || !Array.isArray(d.b)) return;
    const blocks = [];
    for (const [x, y, z, k, m] of d.b) if (B[k] !== undefined) blocks.push({ x, y, z, id: B[k], m });
    if (!blocks.length) return;
    this.ship = this.makeShip(blocks, d.pos, d.yaw || 0, d.face | 0);
    this.ship.store = d.store || { chests: {}, furnaces: {} };
    this.board();
  }

  // ------------------------------------------------------------------ drawing
  // the vessel's blocks meshed in 16-cell sections of its own grid, lit by the open sky
  buildMeshes(S) {
    const g = this.game, r = g.r, gl = r.gl;
    if (!this.mesher) { initMesherTextures(g.assets.layers); this.mesher = new Mesher(); this.mesher.sway = false; }
    const [x0, y0, z0, x1, y1, z1] = S.box, out = [];
    for (let sy = y0; sy <= y1; sy += 16) for (let sz = z0; sz <= z1; sz += 16) for (let sx = x0; sx <= x1; sx += 16) {
      const N = 18 * 18 * 18, ids = new Uint16Array(N), meta = new Uint8Array(N), light = new Uint8Array(N).fill(0xF0);
      let any = false;
      for (let y = -1; y <= 16; y++) for (let z = -1; z <= 16; z++) for (let x = -1; x <= 16; x++) {
        const b = S.map.get(`${sx + x},${sy + y},${sz + z}`);
        if (!b) continue;
        const i = ((y + 1) * 18 + (z + 1)) * 18 + (x + 1);
        ids[i] = b.id; meta[i] = b.m; light[i] = 0;
        if (x >= 0 && x < 16 && y >= 0 && y < 16 && z >= 0 && z < 16) any = true;
      }
      if (!any) continue;
      const d = this.mesher.mesh({ ids, meta, light, leaves: 2 });
      const total = d.counts[0] + d.counts[1] + d.counts[2];
      if (!total) continue;
      const vbo = gl.createBuffer(), vao = gl.createVertexArray();
      gl.bindVertexArray(vao);
      gl.bindBuffer(gl.ARRAY_BUFFER, vbo);
      gl.bufferData(gl.ARRAY_BUFFER, d.data, gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0);
      gl.vertexAttribIPointer(0, 3, gl.UNSIGNED_INT, 12, 0);
      gl.disableVertexAttribArray(1);
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, r.quadIbo);
      gl.bindVertexArray(null);
      out.push({ vbo, vao, counts: d.counts, origin: [sx, sy, sz] });
    }
    S.meshes = out;
  }
  freeMeshes(S) {
    const gl = this.game.r.gl;
    if (S.meshes) for (const m of S.meshes) { gl.deleteBuffer(m.vbo); gl.deleteVertexArray(m.vao); }
    S.meshes = null;
  }
  render(cam, env, time, tick) {
    const S = this.ship;
    if (!S) return;
    if (!S.meshes) this.buildMeshes(S);
    this.game.r.drawShip(S, cam, env, time, tick, this.bob());
  }

  // ------------------------------------------------------------------ messages
  say(text) { this.game.say(text); }
}

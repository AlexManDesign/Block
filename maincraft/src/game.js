'use strict';
// Game: world lifecycle, streaming, loop, environment, interaction, HUD.

const SETTINGS_KEY = 'maincraft.settings.v1';
const IS_TOUCH = (() => { try { return 'ontouchstart' in window || matchMedia('(pointer: coarse)').matches; } catch (e) { return false; } })();
const Settings = {
  renderDist: IS_TOUCH ? 5 : 8, fov: 70, sens: 1, bright: 0.5, clouds: true, leaves: IS_TOUCH ? 0 : 3, sway: true, bobView: true,
  scale: IS_TOUCH ? 0.75 : 1, autoScale: IS_TOUCH, fps: false, sound: true, lang: '',
  load() {
    try { Object.assign(this, JSON.parse(localStorage.getItem(SETTINGS_KEY) || '{}')); } catch (e) { }
    // older saves kept an on/off "fancyLeaves" switch
    if (this.fancyLeaves !== undefined) { if (this.fancyLeaves === false) this.leaves = 0; delete this.fancyLeaves; }
    if (this.lang) CUR_LANG = this.lang;
  },
  save() { try { const o = {}; for (const k in this) if (typeof this[k] !== 'function') o[k] = this[k]; localStorage.setItem(SETTINGS_KEY, JSON.stringify(o)); } catch (e) { } },
};

const DAY_LEN = 1200;
// Minecraft's difficulties (Difficulty.getId: peaceful 0 .. hard 3); a world starts on normal
const DIFFICULTIES = ['peaceful', 'easy', 'normal', 'hard'];
// water fog colours of the biomes (Minecraft's water_fog_color); the rest use 0x050533
// the empty hand's arm, local -> view: rotate around x by -60 deg minus the swing, around y by
// 20 deg, translate (P: [swing, view bob x, view bob y])
function handXform(P, x, y, z, o) {
  const sw2 = P[0], ax = -1.05 - sw2 * 0.5, ay = 0.45 + sw2 * 0.3;
  let y1 = y * Math.cos(ax) - z * Math.sin(ax), z1 = y * Math.sin(ax) + z * Math.cos(ax);
  let x2 = x * Math.cos(ay) + z1 * Math.sin(ay), z2 = -x * Math.sin(ay) + z1 * Math.cos(ay);
  o[0] = x2 + 0.56 + P[1] - sw2 * 0.2; o[1] = y1 - 0.62 + P[2] + sw2 * 0.15; o[2] = z2 - 0.72 - sw2 * 0.15;
}
// a held item, local -> view: rotate around y by 0.78 and around x by 0.32, translate by P
const HELD_CY = Math.cos(0.78), HELD_SY = Math.sin(0.78), HELD_CX = Math.cos(0.32), HELD_SX = Math.sin(0.32);
function heldXform(P, x, y, z, o) {
  const x1 = x * HELD_CY + z * HELD_SY, z0 = -x * HELD_SY + z * HELD_CY;
  const y1 = y * HELD_CX - z0 * HELD_SX, z1 = y * HELD_SX + z0 * HELD_CX;
  o[0] = x1 + P[0]; o[1] = y1 + P[1]; o[2] = z1 + P[2];
}
// leaf mesh modes (mesher: 0 fast, 1 optimized, 2 fancy, 3 shell) from coarse to fine
const LEAF_RANK = new Uint8Array([0, 2, 3, 1]);
// adaptive leaves for a column at squared chunk distance d2, now built with mode cur: fancy within
// 2 chunks, optimized within 6, shell beyond; a column keeps a finer mode one chunk further
// (2 -> 3, 6 -> 7) so walking along a border does not rebuild it back and forth
function leafLod(d2, cur) {
  const t = d2 <= 4 ? 2 : d2 <= 36 ? 1 : 3;
  if (cur === 2 && t !== 2 && d2 <= 9) return 2;
  if (cur === 1 && t === 3 && d2 <= 49) return 1;
  return t;
}
const BUILD_ID = '@BUILD@';   // filled in by build.py
const WATER_FOG = { WARM_OCEAN: 0x041F33, LUKEWARM_OCEAN: 0x041633, DEEP_LUKEWARM_OCEAN: 0x041633, SWAMP: 0x232317, MANGROVE_SWAMP: 0x4D7A60 }; // seconds
// model box faces (+x, -x, top, bottom, back, front) as corner indices (bit0 x1, bit1 y1, bit2 z1)
const MB_FACES = new Int8Array([5, 1, 3, 7, 0, 4, 6, 2, 6, 7, 3, 2, 5, 4, 0, 1, 4, 5, 7, 6, 1, 0, 2, 3]);
const MB_SHADE = [0.72, 0.72, 1.0, 0.55, 0.8, 0.9], MB_TRI = [0, 1, 2, 0, 2, 3];
// pushBox faces (+x, -x, top, bottom, +z, -z): per corner whether x, y, z take the max side
const BOX_C = new Uint8Array([1, 0, 1, 1, 0, 0, 1, 1, 0, 1, 1, 1, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0, 1, 0,
  0, 1, 1, 1, 1, 1, 1, 1, 0, 0, 1, 0, 1, 0, 1, 0, 0, 1, 0, 0, 0, 1, 0, 0,
  0, 0, 1, 1, 0, 1, 1, 1, 1, 0, 1, 1, 1, 0, 0, 0, 0, 0, 0, 1, 0, 1, 1, 0]);
const WHITE4 = [1, 1, 1, 1], SEL_COLOR = new Float32Array([0, 0, 0, 0.55]);
const BOX_TRI = new Uint8Array([0, 1, 2, 0, 2, 3]), BOX_U = new Uint8Array([0, 1, 1, 0]), BOX_V = new Uint8Array([1, 1, 0, 0]);

// random-tick behaviour per block id, derived once from the block names
// kind: 1 crop 0-2, 2 ripe stem, 3 sapling, 4 dirt, 5 grass, 6 fire, 7 farmland; next: crop stage / stem fruit
const RT = (function () {
  const n = B_KEY.length, kind = new Uint8Array(n), next = new Uint16Array(n), flam = new Uint8Array(n);
  for (let id = 1; id < n; id++) {
    const k = B_KEY[id] || '';
    const crop = /^(WHEAT|CARROTS|POTATOES|BEETROOTS|PUMPKIN_STEM|MELON_STEM)_([0-2])$/.exec(k);
    if (crop) { kind[id] = 1; next[id] = B[crop[1] + '_' + (+crop[2] + 1)]; }
    else if (k === 'PUMPKIN_STEM_3') { kind[id] = 2; next[id] = B.PUMPKIN; }
    else if (k === 'MELON_STEM_3') { kind[id] = 2; next[id] = B.MELON; }
    else if (/SAPLING$/.test(k)) kind[id] = 3;
    if ((FLAGS[id] & BF_LEAVES) || /PLANKS|LOG|WOOL|WOOD|BOOKSHELF/.test(k)) flam[id] = 1;
  }
  kind[B.DIRT] = 4; kind[B.GRASS] = 5; kind[B.FIRE] = 6; kind[B.FARMLAND] = 7; kind[B.FARMLAND_MOIST] = 7; kind[B.COCOA] = 8;
  return { kind, next, flam };
})();

class Game {
  constructor(assets, workerSrc) {
    this.assets = assets; this.workerSrc = workerSrc;
    this.canvas = $('gl');
    this.r = new Renderer(this.canvas, assets);
    this.r.whiteLayer = assets.layers.white;
    this.r.overlayLayer = assets.layers.grass_block_side_overlay | 0;
    this.world = null;
    this.player = new Player();
    this.keys = {};
    this.input = { keys: {}, joy: null };
    this.inv = new Array(36).fill(null);
    this.sel = 0;
    this.mode = 'creative';
    this.time = 0.25;
    this.playing = false; this.paused = true;
    this.lastT = 0; this.acc = 0;
    this.fpsAcc = 0; this.fpsN = 0; this.fps = 0;
    this.particles = [];
    this.breaking = null;
    this.mouse = { l: false, r: false, lT: 0, rT: 0 };
    this.cam = [0, 0, 0];
    this.camMode = 0;
    this.debug = false;
    this.hideHud = false;
    this.surv = { hp: 20, food: 20, sat: 5, air: 300, exh: 0, regenT: 0, starveT: 0, dead: false, hurtT: 0, lavaT: 0, invT: 0, lastHurt: 0, poisonT: 0, poisonAcc: 0 };
    this.atkT = 10; this.atkBarV = -1; this.lastHeld = null;
    // column offsets around the player, nearest first: [dx, dz, dx² + dz²], integers only (an array
    // holding a fraction would hand out dx and dz as doubles)
    this.spiral = [];
    this.litIdleAt = -1;   // stream(): stats.gen at the last light pass that lit nothing
    for (let dz = -40; dz <= 40; dz++) for (let dx = -40; dx <= 40; dx++) this.spiral.push([dx, dz, dx * dx + dz * dz]);
    this.spiral.sort((a, b) => a[2] - b[2]);
    this.lastSpace = 0; this.lastW = 0;
    this.saveTimer = 0;
    this.envObj = { fogColor: new Float32Array(3), zenith: new Float32Array(3), sunDir: [0, 1, 0], sunsetColor: [0.95, 0.5, 0.27], cloudColor: [1, 1, 1] };
    this.r.buildClouds(12345);
    this.resize();
    window.addEventListener('resize', () => this.resize());
    this.bindInput();
    requestAnimationFrame(t => this.loop(t));
  }

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2) * Settings.scale * (Settings.autoScale ? this.dynScale || 1 : 1);
    const w = Math.max(1, Math.round(window.innerWidth * dpr)), h = Math.max(1, Math.round(window.innerHeight * dpr));
    if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; }
  }

  // ------------------------------------------------------------------ world lifecycle
  async startWorld(meta) {
    this.meta = meta;
    this.mode = meta.mode || 'creative';
    this.difficulty = DIFFICULTIES.includes(meta.difficulty) ? meta.difficulty : 'normal';
    // ticks the player has spent near each column (LevelChunk.inhabitedTime), for the local difficulty
    this.inhabited = new Map();
    if (meta.inhab && meta.inhab.length) for (let i = 0; i + 1 < meta.inhab.length; i += 2) this.inhabited.set(meta.inhab[i], meta.inhab[i + 1]);
    this.time = typeof meta.time === 'number' ? meta.time : 0.25;
    this.inv = Array.isArray(meta.inv) && meta.inv.length === 36 ? meta.inv.map(s => s && s.id ? s : null) : this.defaultInv();
    this.sel = meta.sel | 0;
    Object.assign(this.surv, { hp: 20, food: 20, sat: 5, air: 300, exh: 0, dead: false }, meta.surv || {});
    show('title', false); show('loading', true);
    $('loadText').textContent = T('loading');
    const editKeys = await DB.loadColKeys(meta.id);
    if (this.world) { this.world.pool.terminate(); }
    const nw = Math.max(1, Math.min(6, (navigator.hardwareConcurrency || 4) - 1));
    const w = new World(meta.seed, this.workerSrc, this.assets.layers, { workers: nw });
    // edited columns: only their keys stay in memory, the edits load with the column
    w.editKeys = editKeys; w.editStore = k => DB.loadCol(meta.id, k);
    this.meshQueue = [];
    w.onMeshResult = (c, sy, d) => this.meshQueue.push(c, sy, d);
    w.onColumnUnload = (c) => { this.r.freeColumn(c); if (c.edits && c.edits.size) this.pendingSave.set(c.key, c.edits); };
    w.onBreak = (x, y, z, id, m, byUpdate) => this.onBlockBroken(x, y, z, id, m, byUpdate);
    w.onFall = (x, y, z, id, m) => this.ents.spawnFalling(id, m, x, y, z);
    // redstone: pressure plates feel the player, mobs and items; comparators read containers
    const rs = w.rs = new Redstone(w), cells = [];
    rs.entityCells = () => {
      cells.length = 0;
      const add = (p) => cells.push(Math.floor(p[0]), Math.floor(p[1] + 0.05), Math.floor(p[2]));
      if (this.player) add(this.player.pos);
      if (this.ents) { for (const e of this.ents.mobs) add(e.pos); for (const it of this.ents.items) add(it.pos); }
      return cells;
    };
    rs.container = (x, y, z) => {
      const id = dryId(w.getBlock(x, y, z));
      if (id !== B.CHEST && id !== B.FURNACE && id !== B.FURNACE_LIT) return -1;
      const k = `${x},${y},${z}`, M = this.meta, v = id === B.CHEST ? M.chests && M.chests[k] : M.furnaces && M.furnaces[k];
      const slots = Array.isArray(v) ? v : v && v.slots ? v.slots : null, n = id === B.CHEST ? 27 : 3;
      if (!slots) return 0;
      // Minecraft's AbstractContainerMenu.getRedstoneSignalFromContainer
      let f = 0, any = false;
      for (const s of slots) if (s && s.count) { f += s.count / maxStack(s.id); any = true; }
      return any ? Math.floor(1 + (f / n) * 14) : 0;
    };
    rs.onTnt = (x, y, z) => { Sfx.fuse(); setTimeout(() => { if (this.world === w) this.explode(x + 0.5, y + 0.0625, z + 0.5, 4, { x, y, z }); }, 4000); };
    rs.sound = (kind, x, y, z, on) => { if (kind === 'door') Sfx.door(on); else if (kind === 'piston') Sfx.door(on); else Sfx.click(); };
    this.world = w;
    this.pendingSave = new Map();
    this.particles.length = 0;
    this.ents = new Entities(this);
    this.ents.loadMobs(meta.mobs);
    this.ents.loadPopulated(meta.popd);
    this.ships = this.ships || new Ships(this);
    this.ships.ship = null;
    this.boats = new Boats(this);
    this.r.buildClouds(meta.seed | 0);
    const p = this.player;
    p.vel = [0, 0, 0]; p.flying = false; p.fallDist = 0;
    if (meta.player) {
      p.pos = meta.player.pos.slice(); p.yaw = meta.player.yaw; p.pitch = meta.player.pitch; p.flying = !!meta.player.flying;
      this.spawn = meta.spawn || p.pos.slice();
      this.bedSpawn = meta.bedSpawn || null;
      this.needSpawnDrop = false;
    } else {
      $('loadText').textContent = T('loading');
      const sp = await this.findSpawn();
      p.pos = [sp[0] + 0.5, 200, sp[1] + 0.5]; p.yaw = 0; p.pitch = 0;
      this.spawn = null; this.bedSpawn = null;
      this.needSpawnDrop = true;
    }
    this.pendingShip = meta.ship || null;
    this.pendingBoats = meta.boats || null;
    this.playing = true;
    this.loadingWorld = true;
    this.updateHotbar();
    this.updateSurvivalHud();
  }

  defaultInv() {
    const inv = new Array(36).fill(null);
    if (this.mode === 'creative') {
      const def = [B.GRASS, B.DIRT, B.STONE, B.COBBLE, B.PLANKS, B.LOG, B.GLASS, B.TORCH, B.BRICK];
      def.forEach((id, i) => inv[i] = { id, count: 1 });
    }
    return inv;
  }

  findSpawn() {
    return new Promise(res => {
      const w = this.world;
      let i = 0, id = 1;
      const cands = [];
      for (let r = 0; r < 60; r++) for (let a = 0; a < Math.max(1, r * 3); a++) {
        const ang = a / Math.max(1, r * 3) * Math.PI * 2;
        cands.push([Math.round(Math.cos(ang) * r * 48), Math.round(Math.sin(ang) * r * 48)]);
      }
      const check = () => {
        const wk = w.pool.workers[0];
        const onm = (e) => {
          const d = e.data;
          if (d.t !== 'surface' || d.id !== id) return;
          wk.removeEventListener('message', onm);
          if (d.y >= SEA + 1 && d.biome > 7 && d.biome !== 10) res(cands[i]);
          else { i++; id++; if (i >= cands.length) res([0, 0]); else check(); }
        };
        wk.addEventListener('message', onm);
        w.pool.busy[0]++;
        wk.postMessage({ t: 'surface', id, x: cands[i][0], z: cands[i][1] });
      };
      const waitReady = () => { if (w.ready) check(); else setTimeout(waitReady, 30); };
      waitReady();
    });
  }

  async saveWorld(quitting) {
    if (!this.world || !this.meta) return;
    if (this.ents) this.ents.settleFalling();
    const w = this.world;
    const entries = [];
    for (const [k, m] of this.pendingSave) entries.push([k, m]);
    this.pendingSave.clear();
    for (const k of w.dirtyCols) { const c = w.cols.get(k); if (c && c.edits) entries.push([k, c.edits]); }
    w.dirtyCols.clear();
    const p = this.player;
    Object.assign(this.meta, {
      lastPlayed: Date.now(), time: this.time, mode: this.mode, inv: this.inv, sel: this.sel,
      player: { pos: (this.sleeping ? this.sleeping.back : p.pos).slice(), yaw: p.yaw, pitch: p.pitch, flying: p.flying }, spawn: this.spawn, bedSpawn: this.bedSpawn || null, surv: { ...this.surv },
      // a vessel and boats still waiting to come back (the world loaded but not yet played) keep
      // their saved state
      ship: this.pendingShip || (this.ships ? this.ships.serialize() : null),
      boats: this.pendingBoats || (this.boats ? this.boats.serialize() : null),
      mobs: this.ents ? this.ents.saveMobs() : null,
      difficulty: this.difficulty,
      inhab: this.inhabited ? Float64Array.from([].concat(...this.inhabited)) : null,
      popd: this.ents ? this.ents.savePopulated() : null,
    });
    await DB.putWorld(this.meta);
    if (await DB.saveCols(this.meta.id, entries)) {
      // stored: unloaded columns' edits leave memory (read back when the column loads again)
      for (const [k, m] of entries) {
        w.editKeys.add(k);
        if (w.savedEdits.get(k) === m && !w.cols.has(k)) w.savedEdits.delete(k);
      }
    }
    void quitting;
  }

  async quitToTitle() {
    this.playing = false;
    if (this.bench) this.endBenchmark(true);
    await this.saveWorld(true);
    if (this.world) { this.world.pool.terminate(); for (const c of this.world.cols.values()) this.r.freeColumn(c); }
    this.world = null;
    if (document.pointerLockElement) document.exitPointerLock();
    UI.showTitle();
  }

  // ------------------------------------------------------------------ streaming
  stream() {
    const w = this.world;
    if (!w || !w.ready) return;
    const p = this.player.pos;
    const pcx = Math.floor(p[0]) >> 4, pcz = Math.floor(p[2]) >> 4;
    const R = Math.min(Settings.renderDist, 27), L = R + 3; // +3: lit needs sky-init neighbours, meshing needs lit neighbours
    const maxGen = w.pool.workers.length * 3;
    // squared column distances are integers, so d2 > x is d2 > floor(x): integer bounds, no fractions
    const LL = Math.floor((L + 0.5) * (L + 0.5)), RR = Math.floor((R + 0.5) * (R + 0.5)), UL = Math.floor((L + 2.5) * (L + 2.5));
    // request columns
    // (index loops over the spiral and the column grid lookup: no per-cell objects every frame)
    const SP = this.spiral;
    for (let i = 0; i < SP.length; i++) {
      const e = SP[i];
      if (e[2] > LL) break;
      if (w.genInFlight >= maxGen) break;
      const cx = pcx + e[0], cz = pcz + e[1];
      if (!w.col(cx, cz)) w.requestColumn(cx, cz);
    }
    // unload far columns
    if ((this.frameN & 31) === 0) {
      // (a Map may drop entries while it is iterated)
      for (const c of w.cols.values()) {
        const dx = c.cx - pcx, dz = c.cz - pcz;
        if (dx * dx + dz * dz > UL) w.unloadColumn(c);
      }
    }
    // light passes (budgeted)
    // Columns wait here until their neighbours exist; the ones on the edge of the loaded area wait
    // for good. A full pass that lit nothing is not repeated until another column has arrived
    // (only arrivals let a waiting column proceed), so the waiting list is not copied, sorted and
    // walked again every frame for nothing.
    const t0 = performance.now();
    if (w.pendingLit.size && this.litIdleAt !== w.stats.gen) {
      const arr = Array.from(w.pendingLit);
      arr.sort((a, b) => ((a.cx - pcx) ** 2 + (a.cz - pcz) ** 2) - ((b.cx - pcx) ** 2 + (b.cz - pcz) ** 2));
      let lit = false, cut = false;
      for (const c of arr) {
        if (performance.now() - t0 > 3.5) { cut = true; break; }
        if (c.state !== 1) { w.pendingLit.delete(c); continue; }
        if (w.litPass(c)) {
          lit = true;
          w.pendingLit.delete(c);
          let top = -1; for (let s = SECTIONS - 1; s >= 0; s--) if (c.secs[s]) { top = s; break; }
          c.dirty = top >= 0 ? ((1 << (top + 1)) - 1) : 0;
        }
      }
      this.litIdleAt = lit || cut ? -1 : w.stats.gen;
    }
    this.meshPass(w, pcx, pcz, RR);
  }
  // send dirty sections of columns within RR (squared column distance) whose neighbours are all
  // lit to the mesh workers, nearest columns first
  meshPass(w, pcx, pcz, RR) {
    const SP = this.spiral, p = this.player.pos;
    const maxMesh = w.pool.workers.length * 4, LV = Settings.leaves;
    let lowered = false;
    for (let i = 0; i < SP.length; i++) {
      const e = SP[i];
      if (e[2] > RR) break;
      if (w.meshInFlight >= maxMesh) break;
      const c = w.col(pcx + e[0], pcz + e[1]);
      if (!c || c.state !== 2) continue;
      // adaptive leaves: fancy within 2 chunks, optimized within 6, the crown's outer shell beyond
      const want = LV === 3 ? leafLod(e[2], c.leafMode) : LV;
      if (c.leafMode < 0) c.leafMode = want;
      else if (want !== c.leafMode) {
        // finer leaves at once; coarser ones one column a frame (crossing a chunk border would
        // otherwise rebuild a whole ring of columns in one go). Only sections with leaves rebuild.
        const finer = LEAF_RANK[want] > LEAF_RANK[c.leafMode];
        if (finer || !lowered) {
          if (!finer) lowered = true;
          c.leafMode = want;
          for (let s = 0; s < SECTIONS; s++) { const m = c.meshes[s]; if ((m && m.leafy) || (c.meshBusy & (1 << s))) { c.meshVer[s]++; c.dirty |= 1 << s; } }
        }
      }
      if (!c.dirty) continue;
      let ok = true;
      for (let k = 0; k < 9 && ok; k++) { const n = w.col(c.cx + (k % 3) - 1, c.cz + ((k / 3) | 0) - 1); if (!n || n.state !== 2) ok = false; }
      if (!ok) continue;
      // nearest sections first (vertical distance from the player; on a tie the lower one)
      const psy = (Math.floor(p[1]) - WORLD_MIN_Y) >> 4;
      const lim = Math.max(psy, SECTIONS - 1 - psy);
      for (let d = 0; d <= lim; d++) for (let t = d ? -1 : 1; t <= 1 && w.meshInFlight < maxMesh; t += 2) {
        const s = psy + t * d;
        if (s < 0 || s >= SECTIONS || !(c.dirty & (1 << s)) || (c.meshBusy & (1 << s))) continue;
        if (w.sectionEmpty(c, s)) {
          c.dirty &= ~(1 << s);
          if (c.meshes[s]) this.r.freeSectionMesh(c.meshes[s]);
          c.meshes[s] = null; c.meshed |= 1 << s;
          continue;
        }
        w.submitMesh(c, s, c.leafMode, Settings.sway);
      }
    }
  }

  // upload finished section meshes to the GPU, nearest first, within a time budget
  uploadMeshes(budgetMs) {
    const q = this.meshQueue;
    if (!q || !q.length) return;
    const t0 = performance.now();
    let i = 0;
    for (; i < q.length; i += 3) {
      const c = q[i];
      if (c.state >= 0) this.r.uploadSection(c, q[i + 1], q[i + 2]);
      if (performance.now() - t0 > budgetMs) { i += 3; break; }
    }
    q.splice(0, i);
  }

  // ------------------------------------------------------------------ environment
  environment() {
    const e = this.envObj;
    const a = this.time * Math.PI * 2;
    const s = Math.sin(a);
    const day = Math.max(0, Math.min(1, (s + 0.14) / 0.3));
    const sunset = Math.max(0, 1 - Math.abs(s) * 3.2) * (0.3 + 0.7 * day);
    // (the colours and the sun direction are written into the env object's own arrays)
    const sd = e.sunDir, ca = Math.cos(a), l = Math.hypot(ca, s, 0.18);
    sd[0] = ca / l; sd[1] = s / l; sd[2] = 0.18 / l;
    e.day = day; e.sunset = sunset;
    const Z = e.zenith, FC = e.fogColor;
    Z[0] = 0.02 + 0.44 * day; Z[1] = 0.03 + 0.62 * day; Z[2] = 0.08 + 0.92 * day;
    let f0 = 0.03 + 0.7 * day, f1 = 0.04 + 0.8 * day, f2 = 0.09 + 0.91 * day;
    // sunset tints the horizon when looking towards the sun
    const pl = this.player, lookX = Math.sin(pl.yaw) * Math.cos(pl.pitch);   // player.look()[0]
    const towards = Math.max(0, lookX * Math.sign(sd[0]));
    const k = sunset * (0.25 + 0.55 * towards);
    f0 += (0.98 - f0) * k; f1 += (0.55 - f1) * k; f2 += (0.3 - f2) * k;
    FC[0] = f0; FC[1] = f1; FC[2] = f2;
    const SC = e.sunsetColor, CC = e.cloudColor;
    SC[0] = 1.0; SC[1] = 0.45 + 0.1 * day; SC[2] = 0.2;
    e.stars = Math.max(0, 1 - day * 1.6);
    e.moonPhase = Math.floor(this.days || 0) % 8;
    CC[0] = 0.25 + 0.75 * day; CC[1] = 0.25 + 0.75 * day; CC[2] = 0.3 + 0.7 * day;
    const R = Math.min(Settings.renderDist, 27);
    e.renderDist = R;
    // the rendered area is a circle of whole chunks: fog must be complete before the nearest missing chunk
    e.fogEnd = Math.max(24, (R - 1) * 16); e.fogStart = Math.max(e.fogEnd * 0.55, e.fogEnd - 40);
    e.far = Math.max(420, R * 16 * 1.5);
    e.fov = Settings.fov + (this.player.sprinting ? 8 : 0) + (this.player.flying && this.player.sprinting ? 4 : 0);
    this.fovCur = (this.fovCur || e.fov) + (e.fov - (this.fovCur || e.fov)) * 0.2;
    e.fov = this.fovCur;
    e.sway = Settings.sway ? 1 : 0;
    // underwater / lava
    // the camera is in a fluid only below its surface (Camera.getFluidInCamera): a shallow flowing
    // cell around the eye with its surface lower down does not count
    const eye = this.cam, ex = Math.floor(eye[0]), ey = Math.floor(eye[1]), ez = Math.floor(eye[2]);
    const fh = this.world ? this.world.fluidHeight(ex, ey, ez) : -1;
    const ib = fh >= 0 && eye[1] < ey + fh ? this.world.getBlock(ex, ey, ez) : 0;
    e.underwater = false;
    if (isWaterId(ib)) {
      // Minecraft's underwater fog (FogRenderer): the biome's water fog colour (blended over 5 s
      // when it changes), brightened toward full by the water vision, which grows over 30 s under
      // water; fog from -8 to 96 x max(0.25, vision) blocks (x0.85 in swamps), at most the view.
      e.underwater = true;
      const v = this.waterVision();
      const bn = BIOME_LIST[this.world.biomeAt(Math.floor(eye[0]), Math.floor(eye[2]))][0];
      const fc = WATER_FOG[bn] ?? 0x050533, W = this.wfog || (this.wfog = { from: fc, to: fc, t: 0 });
      if (fc !== W.to) { W.from = this.wfogNow ?? W.to; W.to = fc; W.t = performance.now(); }
      const k = Math.min(1, (performance.now() - W.t) / 5000), col = [0, 0, 0];
      for (let i = 0; i < 3; i++) { const sh = 16 - i * 8, a = (W.from >> sh) & 255, b = (W.to >> sh) & 255; col[i] = (a + (b - a) * k) / 255; }
      this.wfogNow = (Math.round(col[0] * 255) << 16) | (Math.round(col[1] * 255) << 8) | Math.round(col[2] * 255);
      const m = Math.min(1 / Math.max(col[0], 1e-3), 1 / Math.max(col[1], 1e-3), 1 / Math.max(col[2], 1e-3));
      e.fogColor.set(col.map(c => c * (1 - v) + c * m * v));
      e.fogStart = -8; e.fogEnd = Math.min(e.fogEnd, 96 * Math.max(0.25, v) * (bn === 'SWAMP' || bn === 'MANGROVE_SWAMP' ? 0.85 : 1));
    } else if (ib === B.LAVA) {
      e.underwater = true; e.fogColor.set([0.6, 0.12, 0.0]); e.fogStart = 0; e.fogEnd = 2.5;
    }
    return e;
  }

  // ------------------------------------------------------------------ loop
  loop(t) {
    requestAnimationFrame(tt => this.loop(tt));
    this.frameN = (this.frameN || 0) + 1;
    const dt = Math.min(0.1, (t - (this.lastT || t)) / 1000);
    this.lastT = t;
    this.fpsAcc += dt; this.fpsN++;
    if (this.fpsAcc >= 0.5) { this.fps = Math.round(this.fpsN / this.fpsAcc); this.fpsAcc = 0; this.fpsN = 0; }
    if (!this.playing || !this.world) return;
    this.resize();
    const w = this.world;
    // frame-time history for the F3 performance report (average, 1% low, worst)
    if (!this.ftHist) { this.ftHist = new Float32Array(600); this.ftI = 0; }
    this.ftHist[this.ftI++ % 600] = dt * 1000;
    this.noteSpike(dt * 1000);
    const tf0 = performance.now();
    this.stream();
    const tf1 = performance.now();
    this.uploadMeshes(this.loadingWorld ? 50 : 3);
    const tf2 = performance.now();
    this.perfT('stream', tf1 - tf0); this.perfT('upload', tf2 - tf1);
    if (this.loadingWorld) {
      const p = this.player;
      const pcx = Math.floor(p.pos[0]) >> 4, pcz = Math.floor(p.pos[2]) >> 4;
      let ready = 0, need = 0;
      for (let dz = -2; dz <= 2; dz++) for (let dx = -2; dx <= 2; dx++) { need++; const c = w.col(pcx + dx, pcz + dz); if (c && c.state === 2 && !c.dirty && !c.meshBusy) ready++; }
      $('loadBar').style.width = Math.round(100 * ready / need) + '%';
      if (ready < need) { this.renderFrame(0); return; }
      if (this.needSpawnDrop) {
        const sx = Math.floor(p.pos[0]), sz = Math.floor(p.pos[2]);
        // nearest column whose top solid block is natural ground (not leaves, water or a tree)
        // under open sky, where the player's box is free: spawn on dry land, never in a tree
        let best = null;
        const ground = (g) => g === B.GRASS || g === B.SNOWY_GRASS || g === B.SAND || g === B.SNOW || g === B.DIRT || g === B.PODZOL ||
          g === B.MYCELIUM || g === B.RED_SAND || g === B.STONE || g === B.COARSE_DIRT || g === B.GRAVEL || g === B.MUD;
        for (let r = 0; r <= 32 && !best; r++) for (let dx = -r; dx <= r && !best; dx++) for (let dz = -r; dz <= r && !best; dz++) {
          if (Math.max(Math.abs(dx), Math.abs(dz)) !== r) continue;
          const x = sx + dx, z = sz + dz;
          if (!w.isLoaded(x, z)) continue;
          let y = WORLD_MAX_Y - 1;
          while (y > WORLD_MIN_Y && !SOLID[w.getBlock(x, y, z)] && !isWaterId(w.getBlock(x, y, z))) y--;
          if (!ground(w.getBlock(x, y, z)) || (w.getLight(x, y + 1, z) >> 4) !== 15) continue;
          if (p.collides(w, x + 0.5, y + 1.01, z + 0.5, PLAYER_H)) continue;
          best = [x, y + 1, z];
        }
        if (!best) {
          let y = WORLD_MAX_Y - 1;
          while (y > WORLD_MIN_Y && !SOLID[w.getBlock(sx, y, sz)] && !isWaterId(w.getBlock(sx, y, sz))) y--;
          best = [sx, y + 1, sz];
        }
        p.pos = [best[0] + 0.5, best[1] + 0.01, best[2] + 0.5];
        this.spawn = p.pos.slice();
        this.needSpawnDrop = false;
      }
      this.loadingWorld = false;
      show('loading', false);
      UI.onWorldReady();
    }
    let tu = performance.now();
    if (!this.paused && !this.surv.dead) {
      this.update(dt);
    }
    this.perfT('update', performance.now() - tu);
    tu = performance.now();
    // world ticks (20/s) keep running while paused? no
    if (!this.paused) {
      this.acc += dt;
      let n = 0;
      while (this.acc >= 0.05 && n < 5) { this.acc -= 0.05; w.gameTick(); this.randomTicks(); n++; }
      if (n >= 5) this.acc = 0;
      this.time = (this.time + dt / DAY_LEN) % 1;
      if (this.time < this.prevTime) this.days = (this.days || 0) + 1;
      this.prevTime = this.time;
      this.saveTimer += dt;
      if (this.saveTimer > 30) { this.saveTimer = 0; this.saveWorld(false); }
    }
    this.perfT('ticks', performance.now() - tu);
    if (this.bench) this.benchTick(dt);
    tu = performance.now();
    this.renderFrame(dt);
    this.perfT('render', performance.now() - tu);
    tu = performance.now();
    this.updateDebug();
    this.perfT('hud', performance.now() - tu);
    this.perfT('frame', performance.now() - tf0);
    this.autoScaleTick(dt * 1000, performance.now() - tf0);
  }
  // Dynamic resolution ("Auto Resolution"): when frames are slow because of the GPU (our JS takes
  // well under the frame interval), the render resolution steps down below the Render Scale
  // setting; when frames keep up with the display it steps back up. Decisions use the median
  // frame interval of one-second windows so garbage-collection hiccups do not count, and a scale
  // that was too slow is not retried for 30 s, so the picture does not pump up and down.
  autoScaleTick(ms, js) {
    const A = this.autoS || (this.autoS = { iv: [], js: 0, t: 0, good: 0, failAt: 0, failScale: 2 });
    if (!Settings.autoScale || this.bench || this.paused || this.loadingWorld || !this.playing) { A.iv.length = 0; A.js = A.t = 0; return; }
    A.iv.push(ms); A.js += js; A.t += ms;
    if (A.t < 1000) return;
    const iv = A.iv.sort((a, b) => a - b), med = iv[iv.length >> 1], jsAvg = A.js / iv.length;
    A.iv.length = 0; A.js = A.t = 0;
    const cur = this.dynScale || 1, now = performance.now();
    let next = cur;
    if (med > 20 && jsAvg < med * 0.6) {
      A.good = 0;
      if (cur > 0.5) { next = Math.max(0.5, cur * 0.85); A.failAt = now; A.failScale = cur; }
    } else if (med < 17.5) {
      if (++A.good >= 3 && cur < 1) {
        const up = Math.min(1, cur / 0.85);
        if (up < A.failScale - 1e-6 || now - A.failAt > 30000) { next = up; A.good = 0; }
      }
    } else A.good = 0;
    if (next !== cur) { this.dynScale = next; this.resize(); }
  }
  // Stutter attribution: a frame interval over 25 ms is charged to the biggest JS stage of the
  // frame before it when JS took most of that time, otherwise to work outside our JS (garbage
  // collection, the GPU or the browser). Counts cover the last 600 frames.
  noteSpike(ms) {
    const F = this.frameStages, S = this.spikes || (this.spikes = { list: [] });
    if (ms > 25 && F && F.frame !== undefined) {
      const stages = ['stream', 'upload', 'update', 'ticks', 'render', 'hud'];
      let big = 'вне JS (GC / видеокарта / браузер)', bv = 0;
      if (F.frame > ms * 0.5) for (const k of stages) if ((F[k] || 0) > bv) { bv = F[k]; big = k; }
      S.list.push({ i: this.ftI, ms, why: big, js: F.frame });
    }
    while (S.list.length && S.list[0].i < this.ftI - 600) S.list.shift();
  }
  spikeReport() {
    const S = this.spikes;
    if (!S || !S.list.length) return 'рывки (>25 мс за 600 кадров): нет';
    const by = {};
    for (const s of S.list) { const b = by[s.why] || (by[s.why] = { n: 0, ms: 0, js: 0 }); b.n++; b.ms += s.ms; b.js += s.js; }
    const names = { stream: 'чанки', upload: 'загрузка в GPU', update: 'мир', ticks: 'тики', render: 'рендер', hud: 'HUD' };
    return `рывки (>25 мс за 600 кадров): ${S.list.length} · ` + Object.entries(by).sort((a, b) => b[1].n - a[1].n)
      .map(([k, b]) => `${names[k] || k}: ${b.n} (кадр ~${(b.ms / b.n).toFixed(0)} мс, JS ~${(b.js / b.n).toFixed(0)} мс)`).join(' · ');
  }
  // smoothed CPU milliseconds per frame stage (shown with F3)
  perfT(name, ms) {
    const F = this.frameStages || (this.frameStages = {});
    F[name] = ms;
    const P = this.perf || (this.perf = {});
    P[name] = P[name] === undefined ? ms : P[name] * 0.92 + ms * 0.08;
    const b = this.bench;
    if (b && b.phase === 'run') b.cpu[name] = (b.cpu[name] || 0) + ms;
  }

  // ------------------------------------------------------------------ benchmark (F3 + B)
  // Repeatable measurement in place: waits until every column within the render distance is
  // generated and meshed, then turns the camera a full circle in 12 s from where the player
  // stands, averaging frame times, GPU and CPU time per pass and the geometry drawn. The result
  // opens in a window with a copy button. Run it, change one setting, run it again on the same spot.
  startBenchmark() {
    if (this.bench) { this.endBenchmark(true); return; }
    const p = this.player;
    this.bench = { phase: 'wait', t: 0, ready: 0, pos: p.pos.slice(), yaw: p.yaw, pitch: p.pitch, flying: p.flying,
      frames: [], cpu: {}, sec: 0, draws: 0, quads: 0, fog: 0, n: 0 };
    p.flying = true;
    this.r.gpuAcc = null;
    show('benchBox', false);
    UI.benchStatus(T('benchWait') + '…');
  }
  worldReady() {
    const w = this.world, p = this.player.pos;
    if (w.genInFlight || w.meshInFlight || this.meshQueue.length) return false;
    const pcx = Math.floor(p[0]) >> 4, pcz = Math.floor(p[2]) >> 4, R = Math.min(Settings.renderDist, 27);
    for (let i = 0; i < this.spiral.length; i++) {
      const e = this.spiral[i];
      if (e[2] > R * R) break;
      const c = w.col(pcx + e[0], pcz + e[1]);
      if (!c || c.state !== 2 || c.dirty || c.meshBusy) return false;
    }
    return true;
  }
  benchTick(dt) {
    const b = this.bench, p = this.player;
    p.pos[0] = b.pos[0]; p.pos[1] = b.pos[1]; p.pos[2] = b.pos[2]; p.vel[0] = p.vel[1] = p.vel[2] = 0;
    b.t += dt;
    if (b.phase === 'wait') {
      p.yaw = b.yaw; p.pitch = b.pitch;
      b.ready = this.worldReady() ? b.ready + dt : 0;
      if (b.ready > 1.5 || b.t > 45) { b.loadedAll = b.ready > 1.5; b.waited = b.t; b.phase = 'run'; b.t = 0; this.r.gpuAcc = {}; }
      else this.benchProgress(`${T('benchWait')}… ${Math.floor(b.t)} ${CUR_LANG === 'ru' ? 'с' : 's'}`);
      return;
    }
    p.yaw = b.yaw + Math.PI * 2 * Math.min(1, b.t / 12); p.pitch = b.pitch;
    b.frames.push(dt * 1000);
    this.benchProgress(`${T('benchRun')}… ${Math.min(100, Math.floor(b.t / 12 * 100))}%`);
    const r = this.r, ps = r.pstat;
    b.sec += r.visCount; b.draws += r.stats.draws; b.quads += ps[0].quads + ps[1].quads + ps[2].quads; b.fog += r.stats.fogCulled || 0; b.n++;
    if (b.t >= 12) this.endBenchmark(false);
  }
  benchProgress(text) {
    if (text !== this.benchLast) { this.benchLast = text; UI.benchStatus(text); }
  }
  endBenchmark(cancelled) {
    const b = this.bench, p = this.player, r = this.r;
    if (!b) return;
    this.bench = null; this.benchLast = '';
    UI.benchStatus('');
    p.yaw = b.yaw; p.pitch = b.pitch; p.flying = b.flying;
    const acc = r.gpuAcc; r.gpuAcc = null;
    // back to the game menu; the result window opens above it with a copy button (a click is a
    // user gesture, so copying works everywhere, phones included)
    if (document.pointerLockElement) document.exitPointerLock();
    else if (this.playing && !this.surv.dead) { this.paused = true; UI.showPause(); }
    if (cancelled || !b.frames.length) { this.benchText = 'бенчмарк отменён'; return; }
    const f = (v, d = 2) => (v || 0).toFixed(d);
    const a = b.frames.slice(1).sort((x, y) => x - y), n = a.length;
    const avg = a.reduce((s, v) => s + v, 0) / n, p99 = a[Math.min(n - 1, Math.floor(n * 0.99))], worst = a[n - 1];
    const fr = b.n || 1, C = (k) => f((b.cpu[k] || 0) / fr);
    const Gv = (k) => acc && acc[k] ? acc[k].s / acc[k].n : 0;
    const names = ['solid', 'cutout', 'sky', 'clouds', 'particles', 'entities', 'water', 'hand'];
    let gsum = 0; for (const k of names) gsum += Gv(k);
    const cv = r.canvas, w = this.world, pb = w.biomeAt(Math.floor(b.pos[0]), Math.floor(b.pos[2]));
    const L = [];
    L.push(`== БЕНЧМАРК (12 с, полный оборот камеры) · сборка ${BUILD_ID} ==${b.loadedAll ? '' : '  [ВНИМАНИЕ: мир не успел догрузиться за 45 с]'}`);
    L.push(`Maincraft · seed ${this.meta && this.meta.seed} · XYZ ${b.pos.map(v => v.toFixed(1)).join(' ')} · pitch ${b.pitch.toFixed(2)} · биом ${BIOME_LIST[pb] ? BIOME_LIST[pb][0] : '?'}`);
    L.push(`GPU: ${r.gpuName || '?'}`);
    L.push(`${navigator.userAgent}`);
    L.push(`настройки: экран ${cv.width}x${cv.height} · dpr ${f(window.devicePixelRatio || 1)} · масштаб ${Settings.scale}${Settings.autoScale ? ` (авто ×${f(this.dynScale || 1)})` : ''} · дальность ${Settings.renderDist} · листва ${['быстрая', 'оптим.', 'красивая', 'адаптивная'][Settings.leaves]} · облака ${Settings.clouds ? 'да' : 'нет'} · колыхание ${Settings.sway ? 'да' : 'нет'}`);
    L.push(`FPS ${f(1000 / avg, 1)} · кадр ${f(avg)} мс · 1% low ${f(1000 / p99, 1)} fps · худший ${f(worst, 1)} мс · кадров ${n}`);
    if (acc) L.push(`GPU мс (отброшено замеров ${r.gpuBad || 0}): всего ${f(gsum)} | рельеф ${f(Gv('solid'))} листва ${f(Gv('cutout'))} небо ${f(Gv('sky'))} облака ${f(Gv('clouds'))} частицы ${f(Gv('particles'))} мобы ${f(Gv('entities'))} вода ${f(Gv('water'))} рука ${f(Gv('hand'))}`);
    else L.push('GPU мс: таймер видеокарты недоступен');
    L.push(`CPU мс: кадр ${C('frame')} | мир ${C('update')} (мобы ${C('mobs')}) · тики ${C('ticks')} · чанки ${C('stream')} · загрузка ${C('upload')} · рендер ${C('render')} (видимость ${C('rVis')} · рельеф ${C('rTerrain')} · мобы ${C('rEntities')} · вода ${C('rWater')}) · HUD ${C('hud')}`);
    L.push(`в среднем за кадр: секций ${f(b.sec / fr, 0)} · отброшено туманом ${f(b.fog / fr, 0)} · вызовов отрисовки ${f(b.draws / fr, 0)} · треугольников ${f(b.quads * 2 / fr / 1000, 0)}k`);
    const mem = r.meshMemory(w);
    L.push(`память мешей ${f(mem.bytes / 1048576, 1)} МБ · колонок ${w.cols.size} · ожидание загрузки ${f(b.waited, 1)} с`);
    L.push(this.spikeReport());
    this.benchText = L.join('\n');
    console.log(this.benchText);
    UI.showBench(this.benchText);
  }

  update(dt) {
    const p = this.player, w = this.world;
    const k = this.keys;
    const inp = this.input;
    inp.keys.forward = k.KeyW || k.ArrowUp || this.touchKeys?.forward;
    inp.keys.back = k.KeyS || k.ArrowDown;
    inp.keys.left = k.KeyA || k.ArrowLeft;
    inp.keys.right = k.KeyD || k.ArrowRight;
    inp.keys.jump = k.Space || this.touch?.jump;
    inp.keys.sneak = k.ShiftLeft || k.ShiftRight || this.touch?.sneak;
    inp.keys.sprint = k.ControlLeft || k.ControlRight || (this.touch && this.touch.sprint);
    inp.joy = this.touch && this.touch.joy ? this.touch.joy : null;
    inp.hunger = this.surv.food;
    if (!inp.keys.forward && !(inp.joy && inp.joy[1] < -0.2)) p.sprintLatch = false;
    // only simulate when the column under the player is present
    if (!w.isLoaded(Math.floor(p.pos[0]), Math.floor(p.pos[2]))) return;
    // a vessel saved under way comes back once the world around it is there
    if (this.pendingShip && !this.loadingWorld) { this.ships.restore(this.pendingShip); this.pendingShip = null; }
    if (this.pendingBoats && !this.loadingWorld) { this.boats.restore(this.pendingBoats); this.pendingBoats = null; }
    // in bed: the player lies still, the night passes after 100 ticks
    if (this.sleeping || this.sleepFade) {
      this.sleepTick(dt, inp.keys);
      if (this.sleeping) {
        if (this.mode === 'survival') this.survivalTick(dt);
        this.updateCamera();
        this.ents.update(dt);
        this.tickFurnaces(dt);
        this.updateParticles(dt);
        return;
      }
    }
    // boats drift; the one being ridden takes the movement keys and carries the player
    if (this.boats && this.boats.riding) {
      this.boats.update(dt, inp.keys);
      p.interp(this.boats.acc / 0.05);
      if (this.mode === 'survival') this.survivalTick(dt);
      this.updateCamera();
      this.interact(dt);
      this.ents.update(dt);
      this.tickFurnaces(dt);
      this.updateParticles(dt);
      return;
    }
    if (this.boats) this.boats.update(dt, null);
    if (this.ships && this.ships.ship) {
      // at the wheel: the keys steer the vessel, the player rides along
      this.ships.update(dt, inp.keys);
      p.interp(1);
      if (this.mode === 'survival') this.survivalTick(dt);
      this.updateCamera();
      this.interact(dt);
      this.ents.update(dt);
      this.tickFurnaces(dt);
      this.updateParticles(dt);
      return;
    }
    const wasWater = p.inWater;
    // player movement runs in game ticks (20/s) like Minecraft's; drawn between the last two
    this.pAcc = (this.pAcc || 0) + dt;
    let n = 0;
    p.fallDamage = 0;
    while (this.pAcc >= 0.05 && n < 10) { this.pAcc -= 0.05; p.extraBoxes = this.boats ? this.boats.colliders(p) : null; p.tick(w, inp, this.mode); n++; }
    if (n >= 10) this.pAcc = 0;
    p.interp(this.pAcc / 0.05);
    if (!wasWater && p.inWater && p.vel[1] < -4) Sfx.splash();
    // footsteps
    const hs = Math.hypot(p.vel[0], p.vel[2]);
    if (p.onGround && hs > 1 && !p.sneaking) {
      this.stepDist = (this.stepDist || 0) + hs * dt;
      if (this.stepDist > 1.7) { this.stepDist = 0; Sfx.block(w.getBlock(Math.floor(p.pos[0]), Math.floor(p.pos[1] - 0.2), Math.floor(p.pos[2])), 'step'); }
    }
    if (this.mode === 'survival') this.survivalTick(dt);
    this.updateCamera();
    this.waterVisT = isWaterId(w.getBlock(Math.floor(this.cam[0]), Math.floor(this.cam[1]), Math.floor(this.cam[2]))) ? (this.waterVisT || 0) + dt * 20 : 0;
    this.interact(dt);
    let tm = performance.now();
    this.ents.update(dt);
    this.perfT('mobs', performance.now() - tm);
    this.tickFurnaces(dt);
    tm = performance.now();
    this.updateParticles(dt);
    this.perfT('particlesSim', performance.now() - tm);
  }

  // LocalPlayer.getWaterVision: 0..1 over the first 30 s (600 ticks) with the eye under water
  waterVision() {
    const t = this.waterVisT || 0;
    if (t >= 600) return 1;
    return Math.min(1, t / 100) * 0.6 + (t < 100 ? 0 : Math.min(1, (t - 100) / 500)) * 0.4;
  }
  updateCamera() {
    const p = this.player;
    const e = p.eye();
    // Minecraft bobView: view-space offset (sin(pi f) a / 2, -|cos(pi f) a|), roll sin(pi f) a 3 deg,
    // nod |cos(pi f - 0.2) a| 5 deg, with f = -walkDist and a = bob amplitude
    const f = -p.rwalk, a = Settings.bobView ? p.rbobA : 0, B = this.bobView || (this.bobView = new Float32Array(4));
    B[0] = Math.sin(f * Math.PI) * a * 0.5; B[1] = -Math.abs(Math.cos(f * Math.PI) * a);
    B[2] = Math.sin(f * Math.PI) * a * 3 * Math.PI / 180; B[3] = Math.abs(Math.cos(f * Math.PI - 0.2) * a) * 5 * Math.PI / 180;
    this.r.bob = B;
    if (this.camMode) {
      const l = p.look(), s = this.camMode === 1 ? -1 : 1;
      let d = 4;
      const h = raycast(this.world, e[0], e[1], e[2], l[0] * s, l[1] * s, l[2] * s, 4);
      if (h) d = Math.max(0.5, h.t - 0.3);
      e[0] += l[0] * s * d; e[1] += l[1] * s * d; e[2] += l[2] * s * d;
    }
    this.cam = e;
  }

  // ------------------------------------------------------------------ interaction
  interact(dt) {
    const p = this.player, w = this.world;
    const eye = p.eye(), l = p.look();
    const reach = this.mode === 'creative' ? 5 : 4.5;
    const held = this.inv[this.sel];
    const hid = held ? held.id : 0;
    const fluids = hid === IT.BUCKET;
    this.target = this.pointerFree() ? null : raycast(w, eye[0], eye[1], eye[2], l[0], l[1], l[2], reach, { fluids });
    const now = performance.now();
    // breaking
    if (this.mouse.l && this.target) {
      const tg = this.target;
      if (this.mode === 'creative') {
        if (now - this.mouse.lT > 220) {
          this.mouse.lT = now;
          if (!(held && toolInfo(held) && toolInfo(held).tool === 'sword')) this.doBreak(tg.x, tg.y, tg.z);
          this.swing();
        }
      } else {
        const b = this.breaking;
        if (!b || b.x !== tg.x || b.y !== tg.y || b.z !== tg.z || b.id !== tg.id) {
          this.breaking = { x: tg.x, y: tg.y, z: tg.z, id: tg.id, p: 0, time: breakTime(tg.id, held), snd: 0 };
        }
        const bb = this.breaking;
        let tt = bb.time;
        if (p.inWater && !p.onGround) tt *= 5; else if (p.inWater || !p.onGround) tt *= (p.inWater ? 5 : !p.onGround && !p.flying ? 5 : 1);
        bb.p += dt / Math.max(0.05, tt);
        bb.snd += dt;
        if (bb.snd > 0.25) { bb.snd = 0; Sfx.block(bb.id, 'hit'); }
        // a crack particle every game tick while hitting, on the face being mined
        bb.pt = (bb.pt || 0) + dt;
        while (bb.pt >= 0.05) { bb.pt -= 0.05; this.crackParticle(bb.x, bb.y, bb.z, bb.id, tg.face); }
        this.swingT = Math.max(this.swingT || 0, 0.001);
        if (bb.p >= 1) { this.doBreak(bb.x, bb.y, bb.z); this.breaking = null; this.mouse.lT = now; this.damageTool(); }
      }
    } else this.breaking = null;
    // placing / using
    if (this.mouse.r && now - this.mouse.rT > (this.mouse.rFirst ? 0 : 230)) {
      this.mouse.rT = now; this.mouse.rFirst = false;
      this.use();
    }
    if (this.swingT > 0) this.swingT = Math.max(0, this.swingT - dt * 3.3);
    this.atkT += dt;
    this.updateAttackBar();
    // eating
    if (this.eating) {
      this.eating.t += dt;
      if (!this.mouse.r) this.eating = null;
      else if (this.eating.t > 1.6) { this.eatNow(); this.eating = null; }
    }
  }
  pointerFree() { return this.paused || UI.invOpen; }
  // melee attack on a mob under the crosshair; returns true when a mob was hit
  attack() {
    if (!this.ents) return false;
    const p = this.player, eye = p.eye(), l = p.look();
    const hit = this.ents.raycastMob(eye, l, 3.5);
    // a boat in front of the mob and the block takes the hit (Player.attack, scaled by the attack
    // strength like any hit); the boat being ridden only when nothing else is under the crosshair
    const R = this.boats ? this.boats.riding : null;
    let bh = this.boats && this.boats.raycast(eye, l, 3.5, R);
    if (!bh && R && !hit && !this.target) bh = { b: R, t: 0 };
    if (bh && (!hit || bh.t < hit.t) && (!this.target || bh.t < this.target.t)) {
      const a = attackStats(this.inv[this.sel]), f = this.attackStrength(a.speed);
      this.boats.hit(bh.b, a.dmg * (0.2 + f * f * 0.8));
      this.atkT = 0; this.swing();
      if (this.mode === 'survival') this.surv.exh += 0.1;
      return true;
    }
    if (!hit || (this.target && this.target.t < hit.t)) return false;
    // Player.attack: base damage scaled by the attack strength (cooldown) of the held item
    const a = attackStats(this.inv[this.sel]), f = this.attackStrength(a.speed);
    let dmg = a.dmg * (0.2 + f * f * 0.8);
    const strong = f > 0.9;
    const crit = strong && p.fallDist > 0 && !p.onGround && !p.onLadder && !p.inWater && !p.flying && !p.sprinting;
    if (crit) dmg *= 1.5;
    let extra = null;
    if (strong && p.sprinting) {
      const yaw = p.yaw;
      extra = [0.5, Math.sin(yaw), -Math.cos(yaw)];
      p.vel[0] *= 0.6; p.vel[2] *= 0.6; p.sprinting = false;
    }
    this.ents.damageMob(hit.e, dmg, p.pos, extra);
    if (crit) this.spawnParticles(hit.e.pos[0] - 0.5, hit.e.pos[1] + hit.e.h * 0.5, hit.e.pos[2] - 0.5, B.WOOL_WHITE, 8);
    this.atkT = 0;
    this.swing();
    this.damageTool();
    this.surv.exh += 0.1;
    return true;
  }
  // Player.getAttackStrengthScale(0.5): ticks since the last attack over the item's cooldown
  attackStrength(speed) { return Math.min(1, (this.atkT * 20 + 0.5) / (20 / speed)); }
  swing() { this.swingT = 1; }

  doBreak(x, y, z) {
    const w = this.world;
    const id = w.getBlock(x, y, z);
    if (!id || (HARD[id] < 0 && this.mode !== 'creative')) return;
    if (id === B.BEDROCK && this.mode !== 'creative') return;
    Sfx.block(id, 'break');
    this.destroyParticles(x, y, z, id);
    this.breakingPlayer = true;
    w.breakBlock(x, y, z, false);
    this.breakingPlayer = false;
    // ice broken by hand in survival melts: still water where something (ground or water) holds it
    if (id === B.ICE && this.mode === 'survival' && w.getBlock(x, y - 1, z) !== 0 && !w.getBlock(x, y, z)) w.setBlock(x, y, z, B.WATER, 0);
  }
  onBlockBroken(x, y, z, id, m, byUpdate) {
    // a broken chest spills its contents in every mode (Containers.dropContents)
    if (dryId(id) === B.CHEST) {
      const C = this.chests = this.meta.chests || (this.meta.chests = {}), k = `${x},${y},${z}`;
      const items = C[k] || ((m >> 2) ? rollLoot(m >> 2) : null);
      delete C[k];
      if (items) for (const it of items) if (it) this.ents.dropItem(it.id, it.count, x + 0.5, y + 0.5, z + 0.5);
    }
    // the player breaking blocks drops nothing in creative; blocks broken by the world (lost
    // support, washed away by water) drop in every mode, as in Minecraft
    if (this.mode !== 'survival' && !byUpdate) return;
    const drops = dropsFor(id, m, byUpdate ? null : this.inv[this.sel]);
    for (const d of drops) this.ents.dropItem(d.id, d.count, x + 0.5, y + 0.3, z + 0.5);
    if (!byUpdate) this.surv.exh += 0.005;
  }

  use() {
    const w = this.world, p = this.player, tg = this.target;
    const held = this.inv[this.sel];
    const hid = held ? held.id : 0;
    // boats: board one under the crosshair, or put one on the water (or ground) in front
    if (this.boats) {
      const eye = p.eye(), l = p.look(), bh = this.boats.raycast(eye, l, 3.5);
      if (bh && (!tg || bh.t < tg.t) && !this.boats.riding) { this.boats.board(bh.b); this.swing(); return; }
      if (hid === IT.BOAT) {
        let at = null;
        for (let t = 0.5; t <= 5 && !at; t += 0.1) {
          const x = eye[0] + l[0] * t, y = eye[1] + l[1] * t, z = eye[2] + l[2] * t;
          if (tg && t > tg.t) break;
          if (isWaterId(w.getBlock(Math.floor(x), Math.floor(y), Math.floor(z)))) at = [x, Math.floor(y) + 0.6, z];
        }
        if (!at && tg && tg.face === 2) at = [tg.x + 0.5, tg.y + 1, tg.z + 0.5];
        if (at && !entCollides(w, at[0], at[1], at[2], BOAT_W, BOAT_H)) {
          this.boats.place(at[0], at[1], at[2], p.yaw);
          this.consumeHeld(); this.swing();
        }
        return;
      }
    }
    // feeding an animal its breeding food comes before eating it yourself
    if (hid && this.ents) {
      const h = this.ents.raycastMob(p.eye(), p.look(), 3.5);
      const md = h && MOB_DEFS[h.e.type];
      if (md && md.food && md.food.includes(hid) && (!tg || h.t < tg.t) && this.ents.feed(h.e)) {
        if (this.mode === 'survival') this.consumeHeld();
        this.swing(); return;
      }
    }
    // food
    if (hid && isItem(hid) && itemDef(hid)[5] && itemDef(hid)[5].food && this.mode === 'survival' && this.surv.food < 20) {
      if (!this.eating) this.eating = { t: 0 };
      return;
    }
    if (hid === IT.SHEARS && this.ents) {
      const h = this.ents.raycastMob(p.eye(), p.look(), 3.5);
      if (h && h.e.type === 'sheep' && !h.e.sheared && !h.e.baby) { h.e.sheared = true; this.ents.dropItem(sheepWool(h.e), 1 + (Math.random() * 3 | 0), h.e.pos[0], h.e.pos[1] + 1, h.e.pos[2]); this.swing(); this.damageTool(); return; }
    }
    if (!tg) return;
    const id = tg.id, m = tg.meta;
    const edef = hid && isItem(hid) ? itemDef(hid)[5] : null;
    if (edef && edef.mob) {
      const n = FACE_N[tg.face];
      this.ents.spawnMob(edef.mob, tg.x + n[0] + 0.5, tg.y + n[1] + (n[1] < 0 ? -1 : 0), tg.z + n[2] + 0.5);
      this.consumeHeld(); this.swing(); return;
    }
    const sh = SHAPE[id];
    // interactions
    if (!this.keys.ShiftLeft && !this.keys.ShiftRight) {
      if (sh === SH.DOOR && id !== B.IRON_DOOR) {
        const up = (m >> 2) & 1, oy = up ? tg.y - 1 : tg.y + 1;
        w.setBlock(tg.x, tg.y, tg.z, id, m ^ 8);
        if (w.getBlock(tg.x, oy, tg.z) === id) w.setBlock(tg.x, oy, tg.z, id, w.getMeta(tg.x, oy, tg.z) ^ 8);
        Sfx.door(!(m & 8)); this.swing(); return;
      }
      if (sh === SH.TRAPDOOR && id !== B.IRON_TRAPDOOR) { w.setBlock(tg.x, tg.y, tg.z, id, m ^ 8); Sfx.door(!(m & 8)); this.swing(); return; }
      if (sh === SH.GATE) {
        let nm = m ^ 4;
        if (nm & 4) { const fd = p.facingDir(); if (((m & 3) + 2 & 3) === fd) nm = (nm & ~3) | fd; }
        w.setBlock(tg.x, tg.y, tg.z, id, nm); Sfx.door(!!(nm & 4)); this.swing(); return;
      }
      if (sh === SH.BED) { this.useBed(tg.x, tg.y, tg.z); this.swing(); return; }
      if (id === B.CRAFTING_TABLE && this.mode === 'survival') { UI.openInventory('craft3'); return; }
      if (id === B.SHIP_WHEEL && this.ships) { if (this.ships.assemble(tg.x, tg.y, tg.z)) this.swing(); return; }
      if (dryId(id) === B.CHEST) { UI.openChest(tg.x, tg.y, tg.z); return; }
      if (id === B.FURNACE || id === B.FURNACE_LIT) { UI.openFurnace(tg.x, tg.y, tg.z); return; }
      // redstone controls
      if (id === B.LEVER) { w.setBlock(tg.x, tg.y, tg.z, id, m ^ 16); Sfx.click(); this.swing(); return; }
      if (sh === SH.BUTTON) { w.rs.press(tg.x, tg.y, tg.z); Sfx.click(); this.swing(); return; }
      if (id === B.REPEATER) { w.setBlock(tg.x, tg.y, tg.z, id, (m & ~12) | ((((m >> 2) & 3) + 1) & 3) << 2); Sfx.click(); this.swing(); return; }
      if (id === B.COMPARATOR) { w.setBlock(tg.x, tg.y, tg.z, id, m ^ 4); Sfx.click(); this.swing(); return; }
      // flint and steel primes TNT: it blows after its 80-tick fuse (PrimedTnt)
      if (id === B.TNT && hid === IT.FLINT_AND_STEEL) { w.rs.onTnt(tg.x, tg.y, tg.z); this.swing(); this.damageTool(); return; }
      // an axe strips a log or wood (the axis stays)
      if (STRIPPED_OF[id] && hid && isItem(hid) && (itemDef(hid)[5] || {}).tool === 'axe') {
        w.setBlock(tg.x, tg.y, tg.z, STRIPPED_OF[id], m);
        this.swing(); this.damageTool(); return;
      }
      // shears carve a pumpkin (face toward the player) and it drops 4 seeds, as in Minecraft
      if (id === B.PUMPKIN && hid === IT.SHEARS) {
        w.setBlock(tg.x, tg.y, tg.z, B.CARVED_PUMPKIN, (p.facingDir() + 2) & 3);
        this.ents.dropItem(IT.PUMPKIN_SEEDS, 4, tg.x + 0.5, tg.y + 1.1, tg.z + 0.5);
        this.swing(); this.damageTool(); return;
      }
    }
    if (!hid) return;
    // buckets
    if (hid === IT.BUCKET) {
      if ((id === B.WATER || id === B.LAVA) && (m & 15) === 0) {
        w.setBlock(tg.x, tg.y, tg.z, 0, 0);
        this.replaceHeld(id === B.WATER ? IT.WATER_BUCKET : IT.LAVA_BUCKET);
        Sfx.splash(); this.swing();
      }
      return;
    }
    if (hid === IT.WATER_BUCKET || hid === IT.LAVA_BUCKET) {
      const n = FACE_N[tg.face];
      const x = tg.x + n[0], y = tg.y + n[1], z = tg.z + n[2];
      const cur = w.getBlock(x, y, z);
      if (cur && !(FLAGS[cur] & BF_REPLACE)) return;
      w.setBlock(x, y, z, hid === IT.WATER_BUCKET ? B.WATER : B.LAVA, 0);
      w.scheduleAround(x, y, z);
      if (this.mode === 'survival') this.replaceHeld(IT.BUCKET);
      Sfx.splash(); this.swing();
      return;
    }
    if (hid === IT.FLINT_AND_STEEL) {
      const n = FACE_N[tg.face];
      const x = tg.x + n[0], y = tg.y + n[1], z = tg.z + n[2];
      if (!w.getBlock(x, y, z) && SOLID[w.getBlock(x, y - 1, z)]) { w.setBlock(x, y, z, B.FIRE, 0); this.swing(); }
      return;
    }
    if (hid === IT.BONE_MEAL) { this.boneMeal(tg); return; }
    if (isItem(hid)) {
      const td = toolInfo(held);
      if (td && td.tool === 'hoe' && (id === B.GRASS || id === B.DIRT || id === B.DIRT_PATH) && !w.getBlock(tg.x, tg.y + 1, tg.z)) {
        w.setBlock(tg.x, tg.y, tg.z, B.FARMLAND, 0); Sfx.block(B.DIRT, 'place'); this.swing(); this.damageTool(); return;
      }
      if (td && td.tool === 'shovel' && id === B.GRASS && !w.getBlock(tg.x, tg.y + 1, tg.z)) {
        w.setBlock(tg.x, tg.y, tg.z, B.DIRT_PATH, 0); Sfx.block(B.DIRT, 'place'); this.swing(); this.damageTool(); return;
      }
      // redstone dust goes on top of a full block
      if (hid === IT.REDSTONE) {
        const n = FACE_N[tg.face], px = tg.x + n[0], py = tg.y + n[1], pz = tg.z + n[2], cur = w.getBlock(px, py, pz);
        if ((!cur || (FLAGS[cur] & BF_REPLACE)) && OPAQUE[w.getBlock(px, py - 1, pz)]) {
          w.setBlock(px, py, pz, B.REDSTONE_WIRE, 0); Sfx.block(B.STONE, 'place'); this.consumeHeld(); this.swing();
        }
        return;
      }
      // cocoa beans go on the side of a jungle log (the pod faces the log)
      if (hid === IT.COCOA_BEANS && isJungleLog(id) && tg.face !== 2 && tg.face !== 3) {
        const n = FACE_N[tg.face], px = tg.x + n[0], pz = tg.z + n[2];
        if (!w.getBlock(px, tg.y, pz)) { w.setBlock(px, tg.y, pz, B.COCOA, [1, 3, 0, 0, 2, 0][tg.face]); this.consumeHeld(); this.swing(); }
        return;
      }
      const seeds = { [IT.WHEAT_SEEDS]: B.WHEAT_0, [IT.BEETROOT_SEEDS]: B.BEETROOTS_0, [IT.CARROT]: B.CARROTS_0, [IT.POTATO]: B.POTATOES_0, [IT.PUMPKIN_SEEDS]: B.PUMPKIN_STEM_0, [IT.MELON_SEEDS]: B.MELON_STEM_0 };
      if (seeds[hid] && (id === B.FARMLAND || id === B.FARMLAND_MOIST) && tg.face === 2 && !w.getBlock(tg.x, tg.y + 1, tg.z)) {
        w.setBlock(tg.x, tg.y + 1, tg.z, seeds[hid], 0); this.consumeHeld(); this.swing(); return;
      }
      return;
    }
    // place block
    const pl = placementFor(w, hid, tg, p.facingDir(), p);
    if (!pl) return;
    // do not place inside the player
    for (const b of pl) {
      if (SOLID[b.id]) {
        const boxes = collisionBoxes(w, b.id, b.m, b.x, b.y, b.z) || [];
        const hw = PLAYER_W / 2, pp = p.pos;
        for (const q of boxes) {
          if (b.x + q[0] < pp[0] + hw && b.x + q[3] > pp[0] - hw && b.y + q[1] < pp[1] + p.h && b.y + q[4] > pp[1] && b.z + q[2] < pp[2] + hw && b.z + q[5] > pp[2] - hw) return;
        }
      }
    }
    for (const b of pl) {
      const cur = w.getBlock(b.x, b.y, b.z);
      if (cur && (FLAGS[cur] & BF_REPLACE) && SHAPE[cur] !== SH.WATER && SHAPE[cur] !== SH.LAVA && this.mode === 'survival') this.onBlockBroken(b.x, b.y, b.z, cur, 0, true);
      w.setBlock(b.x, b.y, b.z, b.id, b.m);
      if (FLAGS[b.id] & BF_FALL) w.schedule(b.x, b.y, b.z, 2);
    }
    Sfx.block(hid, 'place');
    this.swing();
    this.consumeHeld();
  }

  boneMeal(tg) {
    const w = this.world, id = tg.id;
    const k = B_KEY[id];
    let ok = false;
    const crop = /^(WHEAT|CARROTS|POTATOES|PUMPKIN_STEM|MELON_STEM)_([0-3])$/.exec(k);
    if (crop) { const st = Math.min(3, +crop[2] + 1 + (Math.random() < 0.5 ? 1 : 0)); w.setBlock(tg.x, tg.y, tg.z, B[crop[1] + '_' + st], 0); ok = true; }
    else if (/SAPLING$/.test(k)) { ok = this.growSapling(tg.x, tg.y, tg.z, id); }
    else if (id === B.GRASS) {
      for (let i = 0; i < 24; i++) {
        const x = tg.x + Math.round((Math.random() - 0.5) * 6), z = tg.z + Math.round((Math.random() - 0.5) * 6);
        for (let dy = -2; dy <= 2; dy++) {
          const y = tg.y + dy;
          if (w.getBlock(x, y, z) === B.GRASS && !w.getBlock(x, y + 1, z)) { w.setBlock(x, y + 1, z, Math.random() < 0.85 ? B.TALL_GRASS : (Math.random() < 0.5 ? B.DANDELION : B.POPPY), 0); break; }
        }
      }
      ok = true;
    }
    if (ok) { this.consumeHeld(); this.swing(); this.spawnParticles(tg.x, tg.y + 0.5, tg.z, B.GRASS, 6); }
  }
  growSapling(x, y, z, id) {
    const w = this.world;
    const k = B_KEY[id];
    const logs = { OAK: [B.LOG, B.LEAVES], BIRCH: [B.BIRCH_LOG, B.BIRCH_LEAVES], SPRUCE: [B.SPRUCE_LOG, B.SPRUCE_LEAVES], JUNGLE: [B.JUNGLE_LOG, B.JUNGLE_LEAVES], ACACIA: [B.ACACIA_LOG, B.ACACIA_LEAVES], DARK_OAK: [B.DARK_LOG, B.DARK_LEAVES] }[k.replace('_SAPLING', '')];
    if (!logs) return false;
    const h = 4 + ((Math.random() * 3) | 0);
    for (let i = 1; i <= h + 1; i++) if (w.getBlock(x, y + i, z) && !(FLAGS[w.getBlock(x, y + i, z)] & BF_REPLACE)) return false;
    const spruce = logs[0] === B.SPRUCE_LOG;
    for (let dy = -3; dy <= 1; dy++) {
      const r = spruce ? (dy === 1 ? 0 : dy === 0 ? 1 : (dy & 1) ? 2 : 1) : dy >= 0 ? 1 : 2;
      for (let dz = -r; dz <= r; dz++) for (let dx = -r; dx <= r; dx++) {
        if (Math.abs(dx) === r && Math.abs(dz) === r && r > 0 && Math.random() < 0.5) continue;
        const yy = y + h + dy;
        const c = w.getBlock(x + dx, yy, z + dz);
        if (!c || (FLAGS[c] & BF_REPLACE)) w.setBlock(x + dx, yy, z + dz, logs[1], 0, { noUpdate: true });
      }
    }
    for (let i = 0; i < h; i++) w.setBlock(x, y + i, z, logs[0], 0, { noUpdate: true });
    return true;
  }

  // Minecraft's Explosion. Blocks: 1352 rays leave the centre (toward the surface of a 16 x 16 x 16
  // grid), each with a strength of radius x 0.7..1.3 that drops by 0.225 every 0.3 step and by
  // (resistance + 0.3) x 0.3 in each block it crosses; the blocks reached with strength left are
  // destroyed, each dropping as an item one time in radius (explosion decay), a chest its contents.
  // Entities within 2 x radius: impact = (1 - distance / 2r) x the part of the body the centre sees,
  // damage (impact^2 + impact) / 2 x 7 x 2r + 1, pushed away (from the centre to the eyes) by impact.
  // byMob: set off by a creeper (its damage then scales with the difficulty)
  explode(cx, cy, cz, r, tg, byMob) {
    const w = this.world, E = this.ents;
    if (tg) w.setBlock(tg.x, tg.y, tg.z, 0, 0);
    Sfx.noise && Sfx.ctx && Sfx.noise(Sfx.ctx.currentTime, 1.2, 0.9, 'lowpass', 600, 60, 0.7);
    const blow = new Map();
    for (let j = 0; j < 16; j++) for (let k = 0; k < 16; k++) for (let l = 0; l < 16; l++) {
      if (j > 0 && j < 15 && k > 0 && k < 15 && l > 0 && l < 15) continue;
      let dx = j / 15 * 2 - 1, dy = k / 15 * 2 - 1, dz = l / 15 * 2 - 1;
      const n = Math.hypot(dx, dy, dz); dx /= n; dy /= n; dz /= n;
      let x = cx, y = cy, z = cz;
      for (let h = r * (0.7 + Math.random() * 0.6); h > 0; h -= 0.22500001) {
        const bx = Math.floor(x), by = Math.floor(y), bz = Math.floor(z);
        if (by < WORLD_MIN_Y || by >= WORLD_MAX_Y) break;
        const id = w.getBlock(bx, by, bz);
        if (id) {
          // a block standing in water resists like the water
          h -= ((FLAGS[id] & (BF_AQUATIC | BF_WET) ? Math.max(BLAST[id], 100) : BLAST[id]) + 0.3) * 0.3;
          if (h > 0) blow.set(bx + ',' + by + ',' + bz, [bx, by, bz]);
        }
        x += dx * 0.3; y += dy * 0.3; z += dz * 0.3;
      }
    }
    // the entities first (the blocks' drops come after and are not hit)
    const q = r * 2, centre = [cx, cy, cz];
    const seen = (x0, y0, z0, x1, y1, z1) => {
      // Explosion.getSeenPercent: a grid over the body, each point checked against the centre
      const a = 1 / ((x1 - x0) * 2 + 1), b = 1 / ((y1 - y0) * 2 + 1), c = 1 / ((z1 - z0) * 2 + 1);
      const ox = (1 - Math.floor(1 / a) * a) / 2, oz = (1 - Math.floor(1 / c) * c) / 2;
      let hit = 0, all = 0;
      for (let u = 0; u <= 1; u += a) for (let v = 0; v <= 1; v += b) for (let t = 0; t <= 1; t += c) {
        if (E && !E.rayBlocked([x0 + (x1 - x0) * u + ox, y0 + (y1 - y0) * v, z0 + (z1 - z0) * t + oz], centre)) hit++;
        all++;
      }
      return all ? hit / all : 0;
    };
    // (out of reach: -1; within it even a hidden body takes the 1)
    const impact = (pos, w2, h2) => {
      const d = Math.hypot(pos[0] - cx, pos[1] - cy, pos[2] - cz) / q;
      return d > 1 || d === 0 ? -1 : (1 - d) * seen(pos[0] - w2 / 2, pos[1], pos[2] - w2 / 2, pos[0] + w2 / 2, pos[1] + h2, pos[2] + w2 / 2);
    };
    const push = (vel, pos, eye, f) => {
      const vx = pos[0] - cx, vy = pos[1] + eye - cy, vz = pos[2] - cz, l = Math.hypot(vx, vy, vz);
      if (l) { vel[0] += vx / l * f * 20; vel[1] += vy / l * f * 20; vel[2] += vz / l * f * 20; }
    };
    const p = this.player;
    {
      const f = impact(p.pos, PLAYER_W, p.h);
      if (f >= 0) {
        if (this.mode === 'survival') this.hurt(Math.floor((f * f + f) / 2 * 7 * q + 1), byMob);
        if (!(this.mode !== 'survival' && p.flying)) push(p.vel, p.pos, p.h * 0.9, f);
      }
    }
    if (E) {
      for (const e of E.mobs.slice()) {
        if (e.deathT > 0) continue;
        const f = impact(e.pos, e.w, e.h);
        if (f < 0) continue;
        E.damageMob(e, Math.floor((f * f + f) / 2 * 7 * q + 1), null, null, true);
        push(e.vel, e.pos, e.h * 0.85, f);
      }
      // dropped items have 5 health
      E.items = E.items.filter(it => { const f = impact(it.pos, 0.25, 0.25); return f < 0 || Math.floor((f * f + f) / 2 * 7 * q + 1) < 5; });
    }
    const pick = { id: IT.DIAMOND_PICKAXE, count: 1 };
    for (const [x, y, z] of blow.values()) {
      const id = w.getBlock(x, y, z);
      if (!id || HARD[id] < 0) continue;
      if (id === B.TNT) { w.setBlock(x, y, z, 0, 0); setTimeout(() => this.world && this.explode(x + 0.5, y + 0.0625, z + 0.5, 4), 500 + Math.random() * 1000); continue; }
      const m = w.getMeta(x, y, z);
      if (dryId(id) === B.CHEST) this.onBlockBroken(x, y, z, id, m, true);
      else if (E && Math.random() < 1 / r) for (const d of dropsFor(id, m, pick)) E.dropItem(d.id, d.count, x + 0.5, y + 0.3, z + 0.5);
      w.setBlock(x, y, z, (FLAGS[id] & (BF_AQUATIC | BF_WET)) ? B.WATER : 0, 0, { noUpdate: true });
      // the other half of a door or a bed goes with it
      const sh = SHAPE[id];
      if (sh === SH.DOOR || sh === SH.TALL) { const oy = ((sh === SH.DOOR ? m >> 2 : m) & 1) ? y - 1 : y + 1; if (w.getBlock(x, oy, z) === id) w.setBlock(x, oy, z, 0, 0, { noUpdate: true }); }
      if (sh === SH.BED) {
        const f = m & 3, head = (m >> 2) & 1, ox = head ? -DIRX_W[f] : DIRX_W[f], oz = head ? -DIRZ_W[f] : DIRZ_W[f];
        if (w.getBlock(x + ox, y, z + oz) === id) w.setBlock(x + ox, y, z + oz, 0, 0, { noUpdate: true });
      }
      if (Math.random() < 0.08) this.spawnParticles(x, y, z, id, 3);
    }
    for (const [x, y, z] of blow.values()) w.scheduleAround(x, y, z);
  }

  // ------------------------------------------------------------------ inventory helpers
  consumeHeld() {
    if (this.mode !== 'survival') return;
    const s = this.inv[this.sel];
    if (!s) return;
    if (--s.count <= 0) this.inv[this.sel] = null;
    this.updateHotbar();
  }
  replaceHeld(id) {
    if (this.mode !== 'survival') { this.inv[this.sel] = { id, count: 1 }; this.updateHotbar(); return; }
    const s = this.inv[this.sel];
    if (s && s.count > 1) { s.count--; this.giveItem(id, 1); }
    else this.inv[this.sel] = { id, count: 1 };
    this.updateHotbar();
  }
  damageTool() {
    if (this.mode !== 'survival') return;
    const s = this.inv[this.sel], t = toolInfo(s);
    if (!t) return;
    if (s.dur === undefined) { s.maxDur = [0, 59, 131, 250, 32, 1561][t.tier] || 100; s.dur = s.maxDur; }
    if (--s.dur <= 0) { this.inv[this.sel] = null; Sfx.block(B.GLASS, 'break'); }
    this.updateHotbar();
  }
  giveItem(id, count) {
    const ms = maxStack(id);
    for (let i = 0; i < 36 && count > 0; i++) {
      const s = this.inv[i];
      if (s && s.id === id && s.count < ms && s.dur === undefined) { const n = Math.min(count, ms - s.count); s.count += n; count -= n; }
    }
    for (let i = 0; i < 36 && count > 0; i++) {
      if (!this.inv[i]) { const n = Math.min(count, ms); this.inv[i] = { id, count: n }; count -= n; }
    }
    Sfx.pop();
    this.updateHotbar();
    if (UI.invOpen) UI.refreshInventory();
    return count;
  }
  pickBlock() {
    const tg = this.target;
    // a boat under the crosshair (nearer than the block) gives its item (Entity.getPickResult)
    const bh = this.boats && this.boats.raycast(this.player.eye(), this.player.look(), 4.5, this.boats.riding);
    if (!tg && !bh) return;
    let id = bh && (!tg || bh.t < tg.t) ? IT.BOAT : dryId(tg.id);
    if (id === B.FARMLAND_MOIST) id = B.FARMLAND;
    if (id === B.MANGROVE_ROOTS_WET) id = B.MANGROVE_ROOTS;
    if (id === B.SNOWY_GRASS) id = B.GRASS;
    const inHot = this.inv.slice(0, 9).findIndex(s => s && s.id === id);
    if (inHot >= 0) { this.sel = inHot; this.updateHotbar(); return; }
    if (this.mode === 'creative') {
      let slot = this.inv[this.sel] ? this.inv.slice(0, 9).findIndex(s => !s) : this.sel;
      if (slot < 0) slot = this.sel;
      this.inv[slot] = { id, count: 1 }; this.sel = slot; this.updateHotbar();
    } else {
      const i = this.inv.findIndex(s => s && s.id === id);
      if (i >= 9) { const t = this.inv[this.sel]; this.inv[this.sel] = this.inv[i]; this.inv[i] = t; this.updateHotbar(); }
    }
  }
  eatNow() {
    const s = this.inv[this.sel];
    if (!s) return;
    const d = itemDef(s.id);
    if (!d || !d[5] || !d[5].food) return;
    this.surv.food = Math.min(20, this.surv.food + d[5].food);
    this.surv.sat = Math.min(this.surv.food, this.surv.sat + d[5].food * 0.6);
    this.consumeHeld();
    if (s.id === IT.MUSHROOM_STEW) this.giveItem(IT.BOWL, 1);
    Sfx.pop();
    this.updateSurvivalHud();
  }

  // ------------------------------------------------------------------ survival
  survivalTick(dt) {
    const p = this.player, s = this.surv;
    if (p.fallDamage > 0) { this.hurt(p.fallDamage); }
    // air
    if (p.headInWater) { s.air -= dt * 20; if (s.air <= -20) { s.air = 0; this.hurt(2); } }
    else s.air = Math.min(300, s.air + dt * 100);
    if (p.inLava) { s.lavaT -= dt; if (s.lavaT <= 0) { s.lavaT = 0.5; this.hurt(4); } }
    const fb = this.world.getBlock(Math.floor(p.pos[0]), Math.floor(p.pos[1] + 0.2), Math.floor(p.pos[2]));
    if (fb === B.FIRE || fb === B.MAGMA_BLOCK || fb === B.CACTUS) { s.lavaT -= dt; if (s.lavaT <= 0) { s.lavaT = 0.5; this.hurt(1); } }
    if (p.pos[1] < WORLD_MIN_Y - 40) this.hurt(4);
    // exhaustion: sprinting 0.1 and swimming 0.01 a block, a jump 0.05 (0.2 sprinting)
    const hs = Math.hypot(p.vel[0], p.vel[2]);
    s.exh += dt * (p.sprinting ? 0.1 * hs : p.swimming ? 0.01 * hs : 0);
    if (p.jumped) { s.exh += p.jumped === 2 ? 0.2 : 0.05; p.jumped = 0; }
    s.tickAcc = (s.tickAcc || 0) + dt;
    while (s.tickAcc >= 0.05) { s.tickAcc -= 0.05; this.foodTick(); }
    if (s.hurtT > 0) s.hurtT -= dt;
    if (s.invT > 0) s.invT -= dt;
    // MobEffects.POISON (level I): 1 damage every 25 ticks while health is above 1
    if (s.poisonT > 0) {
      s.poisonT -= dt; s.poisonAcc += dt;
      if (s.poisonAcc >= 1.25) { s.poisonAcc -= 1.25; if (s.hp > 1) this.hurt(1); }
    }
    this.updateSurvivalHud();
  }
  // FoodData.tick and Player.aiStep's peaceful regeneration, one game tick
  foodTick() {
    const s = this.surv, d = this.difficulty;
    s.ticks = ((s.ticks | 0) + 1) % 2000000;
    // peaceful: health comes back 1 a second and food 1 every 10 ticks
    if (d === 'peaceful') {
      if (s.hp < 20 && s.ticks % 20 === 0) s.hp = Math.min(20, s.hp + 1);
      if (s.food < 20 && s.ticks % 10 === 0) s.food++;
    }
    // every 4 of exhaustion takes 1 saturation, or with none left 1 food (never on peaceful)
    if (s.exh > 4) { s.exh -= 4; if (s.sat > 0) s.sat = Math.max(0, s.sat - 1); else if (d !== 'peaceful') s.food = Math.max(0, s.food - 1); }
    const hurt = s.hp < 20;
    if (s.sat > 0 && hurt && s.food >= 20) {
      // full and saturated: every 10 ticks heals a sixth of the saturation spent (up to 6)
      if (++s.regenT >= 10) { const f = Math.min(s.sat, 6); s.hp = Math.min(20, s.hp + f / 6); s.exh += f; s.regenT = 0; }
    } else if (s.food >= 18 && hurt) {
      if (++s.regenT >= 80) { s.hp = Math.min(20, s.hp + 1); s.exh += 6; s.regenT = 0; }
    } else if (s.food <= 0) {
      // starving: 1 every 80 ticks, down to 10 health on easy (and peaceful), to 1 on normal, to death on hard
      if (++s.regenT >= 80) { if (s.hp > 10 || d === 'hard' || (s.hp > 1 && d === 'normal')) this.hurt(1); s.regenT = 0; }
    } else s.regenT = 0;
  }
  setDifficulty(d) {
    if (!DIFFICULTIES.includes(d)) return;
    this.difficulty = d;
    if (this.meta) this.meta.difficulty = d;
  }
  // DifficultyInstance: the local difficulty at a column, base (0 .. 3) x (0.75 + the world's age
  // (after the first hour, up to 0.25 at 21 hours) + the time spent near the column (up to 1, x0.75
  // below hard, after 50 hours) + the moon (up to the age part)); halved locally on easy. special:
  // its part between 2 and 4 (what spawns with extras goes by it)
  localDifficulty(x, z) {
    const id = DIFFICULTIES.indexOf(this.difficulty);
    if (id <= 0) return { eff: 0, special: 0 };
    const age = ((this.days || 0) + this.time) * 24000;
    const g = Math.min(1, Math.max(0, (age - 72000) / 1440000)) * 0.25;
    const moon = [1, 0.75, 0.5, 0.25, 0, 0.25, 0.5, 0.75][((this.days || 0) % 8 + 8) % 8];
    let h = Math.min(1, (this.inhabited.get(colKey(x >> 4, z >> 4)) || 0) / 3600000) * (id === 3 ? 1 : 0.75);
    h += Math.min(g, moon * 0.25);
    if (id === 1) h *= 0.5;
    const eff = id * (0.75 + g + h);
    return { eff, special: eff < 2 ? 0 : eff > 4 ? 1 : (eff - 2) / 2 };
  }
  // LivingEntity.hurt: 20 ticks of invulnerability; in the first 10 only a bigger hit lands, by the
  // difference. byMob: damage a mob (not the player) caused, which Player.hurt scales by the
  // difficulty: none on peaceful, half + 1 on easy (at most the whole), x1.5 on hard
  hurt(n, byMob) {
    const s = this.surv;
    if (byMob) {
      const d = this.difficulty;
      if (d === 'peaceful') n = 0; else if (d === 'easy') n = Math.min(n / 2 + 1, n); else if (d === 'hard') n *= 1.5;
    }
    if (this.mode !== 'survival' || s.dead || n <= 0) return false;
    if (s.invT > 0.5) {
      if (n <= s.lastHurt) return false;
      const d = n - s.lastHurt; s.lastHurt = n;
      s.hp = Math.max(0, s.hp - d);
      if (s.hp <= 0) this.die();
      this.updateSurvivalHud();
      return false;
    }
    s.lastHurt = n; s.invT = 1;
    s.exh += 0.1;                                  // Player.actuallyHurt: the damage source's food exhaustion
    if (this.sleeping) this.wake();                // LivingEntity.hurt: a hit wakes the sleeper
    s.hp = Math.max(0, s.hp - n);
    s.hurtT = 0.4;
    Sfx.hurt();
    $('hurt').classList.remove('flash'); void $('hurt').offsetWidth; $('hurt').classList.add('flash');
    if (s.hp <= 0) this.die();
    this.updateSurvivalHud();
    return true;
  }
  poison(sec) { if (this.mode === 'survival') { if (this.surv.poisonT <= 0) this.surv.poisonAcc = 0; this.surv.poisonT = Math.max(this.surv.poisonT, sec); this.updateSurvivalHud(); } }
  // ---------------------------------------------------------------- beds
  // a line of text above the hotbar (Minecraft's action bar / chat line)
  say(text) {
    let el = this.msgEl;
    if (!el) {
      el = this.msgEl = document.createElement('div');
      el.style.cssText = 'position:fixed;left:50%;bottom:96px;transform:translateX(-50%);padding:6px 12px;background:rgba(0,0,0,.55);color:#fff;' +
        'font:14px monospace;border-radius:4px;pointer-events:none;transition:opacity .4s;z-index:20;text-align:center;max-width:90vw';
      document.body.appendChild(el);
    }
    el.textContent = text; el.style.opacity = '1';
    clearTimeout(this.msgT);
    this.msgT = setTimeout(() => { el.style.opacity = '0'; }, 4000);
  }
  // Level.skyDarken (clear weather): 0 at noon .. 11 at midnight; Level.isDay() is skyDarken < 4,
  // so a bed works from about 18:32 (tick 12542) to 5:27 (tick 23459)
  skyDarken() {
    const d0 = ((this.time - 0.25) % 1 + 1) % 1, d1 = 0.5 - Math.cos(d0 * Math.PI) / 2;
    const tod = (d0 * 2 + d1) / 3;
    const d2 = 0.5 + 2 * Math.max(-0.25, Math.min(0.25, Math.cos(tod * Math.PI * 2)));
    return Math.floor((1 - d2) * 11);
  }
  // the cell beside a bed the player stands up on (BedBlock.findStandUpPosition: the sides of the
  // bed first, then its ends, then on top)
  standUpSpot(hx, hy, hz, f) {
    const w = this.world, p = this.player, fx = hx - DIRX_W[f], fz = hz - DIRZ_W[f];
    const sx = DIRZ_W[f], sz = -DIRX_W[f];                 // sideways from the bed
    const cand = [[hx + sx, hz + sz], [hx - sx, hz - sz], [fx + sx, fz + sz], [fx - sx, fz - sz],
      [hx + DIRX_W[f], hz + DIRZ_W[f]], [fx - DIRX_W[f], fz - DIRZ_W[f]],
      [hx + sx + DIRX_W[f], hz + sz + DIRZ_W[f]], [hx - sx + DIRX_W[f], hz - sz + DIRZ_W[f]],
      [fx + sx - DIRX_W[f], fz + sz - DIRZ_W[f]], [fx - sx - DIRX_W[f], fz - sz - DIRZ_W[f]]];
    for (const dy of [0, -1, 1]) for (const [x, z] of cand) {
      const y = hy + dy;
      if (!SOLID[w.getBlock(x, y - 1, z)] || isWaterId(w.getBlock(x, y, z)) || SHAPE[w.getBlock(x, y - 1, z)] === SH.BED) continue;
      if (!p.collides(w, x + 0.5, y, z + 0.5, PLAYER_H)) return [x + 0.5, y, z + 0.5];
    }
    if (!p.collides(w, hx + 0.5, hy + 0.5625, hz + 0.5, PLAYER_H)) return [hx + 0.5, hy + 0.5625, hz + 0.5];
    return null;
  }
  // BedBlock.use -> ServerPlayer.startSleepInBed, with its checks in Minecraft's order
  useBed(x, y, z) {
    const w = this.world, p = this.player, id = w.getBlock(x, y, z), m = w.getMeta(x, y, z), f = m & 3;
    if (this.sleeping || this.surv.dead) return;
    // the head half
    if (!((m >> 2) & 1)) { x += DIRX_W[f]; z += DIRZ_W[f]; if (w.getBlock(x, y, z) !== id) return; }
    const fx = x - DIRX_W[f], fz = z - DIRZ_W[f], P = p.pos;
    const near = (bx, bz) => Math.abs(P[0] - (bx + 0.5)) <= 3 && Math.abs(P[1] - y) <= 2 && Math.abs(P[2] - (bz + 0.5)) <= 3;
    if (!near(x, z) && !near(fx, fz)) { this.say(T('bedTooFar')); return; }
    // obstructed: a suffocating block over either half
    const sufo = (bx, bz) => { const a = w.getBlock(bx, y + 1, bz); return SOLID[a] && SHAPE[a] === SH.CUBE; };
    if (sufo(x, z) || sufo(fx, fz)) { this.say(T('bedObstructed')); return; }
    const k = this.bedSpawn;
    if (!k || k[0] !== x || k[1] !== y || k[2] !== z) { this.bedSpawn = [x, y, z]; this.say(T('spawnSet')); }
    if (this.skyDarken() < 4) { this.say(T('bedNight')); return; }
    if (this.mode !== 'creative') {
      // any monster within 8 blocks across and 5 up or down of the bed
      const lo = [x + 0.5 - 8, y - 5, z + 0.5 - 8], hi = [x + 0.5 + 8, y + 5, z + 0.5 + 8];
      for (const e of this.ents.mobs) {
        const d = MOB_DEFS[e.type];
        if (!d.hostile || d.slime || e.deathT > 0) continue;
        const hw = d.w / 2;
        if (e.pos[0] + hw > lo[0] && e.pos[0] - hw < hi[0] && e.pos[1] + d.h > lo[1] && e.pos[1] < hi[1] && e.pos[2] + hw > lo[2] && e.pos[2] - hw < hi[2]) { this.say(T('bedMonsters')); return; }
      }
    }
    // lie down: on the head half, 11/16 up, eye 0.2 above, looking along the bed toward its foot
    this.sleeping = { head: [x, y, z], f, t: 0, acc: 0, back: P.slice(), yaw: p.yaw, pitch: p.pitch };
    if (this.boats && this.boats.riding) this.boats.leave();
    p.pos = [x + 0.5, y + 0.6875, z + 0.5]; p.prev = p.pos.slice(); p.prevOf = p.pos;
    p.vel = [0, 0, 0]; p.flying = false; p.fallDist = 0; p.eyeOffset = 0.2; p.eyePrev = 0.2;
    p.yaw = Math.atan2(-DIRX_W[f], DIRZ_W[f]); p.pitch = 0;
    p.interp(1);
    this.sleepFade = 0;
    this.say(T('leaveBed'));
  }
  // 20 ticks a second in bed; at 100 ticks (all players asleep) the day comes: time to the next
  // morning, everyone wakes (ServerLevel.tick). The dark overlay fades in over the 100 ticks and
  // out over 10 after waking (Gui.renderSleepOverlay)
  sleepTick(dt, keys) {
    const S = this.sleeping, ov = this.sleepEl || (this.sleepEl = $('sleep'));
    if (!S) {
      this.sleepFade += dt * 20;
      const a = this.sleepFade >= 10 ? 0 : (1 - this.sleepFade / 10) * 220 / 255;
      if (ov) ov.style.opacity = a.toFixed(3);
      if (this.sleepFade >= 10) this.sleepFade = 0;
      return;
    }
    if (keys && keys.sneak) { this.wake(); return; }
    const w = this.world, [x, y, z] = S.head;
    if (SHAPE[w.getBlock(x, y, z)] !== SH.BED) { this.wake(); return; }
    S.acc += dt;
    while (S.acc >= 0.05) { S.acc -= 0.05; S.t++; }
    if (ov) ov.style.opacity = (Math.min(1, S.t / 100) * 220 / 255).toFixed(3);
    const p = this.player;
    p.yaw = Math.atan2(-DIRX_W[S.f], DIRZ_W[S.f]); p.pitch = 0;
    p.vel[0] = p.vel[1] = p.vel[2] = 0; p.fallDist = 0;
    if (S.t >= 100) {
      this.time = 0;                                   // dayTime + 24000 - dayTime % 24000: 6:00
      this.wake();
    }
  }
  wake() {
    const S = this.sleeping, p = this.player;
    if (!S) return;
    this.sleeping = null;
    const [x, y, z] = S.head, at = this.standUpSpot(x, y, z, S.f) || S.back;
    p.pos = at.slice(); p.prev = p.pos.slice(); p.prevOf = p.pos; p.vel = [0, 0, 0];
    p.eyeOffset = EYE; p.eyePrev = EYE; p.pitch = S.pitch;
    p.interp(1);
    this.sleepFade = S.t >= 100 ? 0.001 : 9.999;
  }
  die() {
    this.surv.dead = true;
    if (document.pointerLockElement) document.exitPointerLock();
    UI.showDeath();
  }
  respawn() {
    const p = this.player;
    Object.assign(this.surv, { hp: 20, food: 20, sat: 5, air: 300, exh: 0, dead: false, poisonT: 0 });
    this.inv = this.inv.map(() => null);
    if (this.sleeping) { this.sleeping = null; p.eyeOffset = EYE; }
    // ServerPlayer respawn: at the bed when it is still there and has room beside it, else at the
    // world spawn with a message
    let at = null;
    if (this.bedSpawn) {
      const [x, y, z] = this.bedSpawn, id = this.world.getBlock(x, y, z);
      if (SHAPE[id] === SH.BED) at = this.standUpSpot(x, y, z, this.world.getMeta(x, y, z) & 3);
      if (!at) { this.bedSpawn = null; this.say(T('noBed')); }
    }
    p.pos = (at || this.spawn || [0.5, 100, 0.5]).slice(); p.vel = [0, 0, 0]; p.fallDist = 0;
    this.updateHotbar(); this.updateSurvivalHud();
    UI.hideDeath();
  }

  randomTicks() {
    // a few random ticks per section near the player (20/s): crops, stems, saplings, grass, fire, farmland
    const w = this.world, p = this.player, kind = RT.kind;
    const pcx = Math.floor(p.pos[0]) >> 4, pcz = Math.floor(p.pos[2]) >> 4;
    // block picks from an integer xorshift generator (a few thousand a second; Math.random would
    // return each as a new number object here)
    const RS = this.rtSeed || (this.rtSeed = new Int32Array([(Math.random() * 0x7fffffff) | 1]));
    let r = RS[0];
    for (let dz = -4; dz <= 4; dz++) for (let dx = -4; dx <= 4; dx++) {
      const c = w.col(pcx + dx, pcz + dz);
      if (!c || c.state !== 2) continue;
      for (let s = 0; s < SECTIONS; s++) {
        const sec = c.secs[s];
        if (!sec) continue;
        for (let k = 0; k < 2; k++) {
          r ^= r << 13; r ^= r >>> 17; r ^= r << 5;
          const i = r & 4095, id = sec.ids[i];
          if (!kind[id]) continue;
          this.randomTick(c.cx * 16 + (i & 15), WORLD_MIN_Y + s * 16 + (i >> 8), c.cz * 16 + ((i >> 4) & 15), id);
        }
      }
    }
    RS[0] = r;
  }
  randomTick(x, y, z, id) {
    const w = this.world;
    switch (RT.kind[id]) {
      case 1: // crop stage 0-2
        if (Math.random() < 0.3 && (w.getLight(x, y, z) >> 4) > 8) w.setBlock(x, y, z, RT.next[id], 0);
        return;
      case 2: // ripe stem: grow the fruit next to it
        if (Math.random() < 0.2) {
          const d = (Math.random() * 4) | 0, nx = x + DIRX_W[d], nz = z + DIRZ_W[d];
          if (!w.getBlock(nx, y, nz) && isSoil(w.getBlock(nx, y - 1, nz))) w.setBlock(nx, y, nz, RT.next[id], 0);
        }
        return;
      case 3: // sapling
        if (Math.random() < 0.05) this.growSapling(x, y, z, id);
        return;
      case 4: // dirt: grass spreads onto it
        if (!OPAQUE[w.getBlock(x, y + 1, z)] && (w.getLight(x, y + 1, z) >> 4) >= 9 && !isWaterId(w.getBlock(x, y + 1, z))) {
          for (let t = 0; t < 4; t++) if (w.getBlock(x + ((Math.random() * 3) | 0) - 1, y + ((Math.random() * 3) | 0) - 1, z + ((Math.random() * 3) | 0) - 1) === B.GRASS) { w.setBlock(x, y, z, B.GRASS, 0); break; }
        }
        return;
      case 5: // grass dies under an opaque block
        if (OPAQUE[w.getBlock(x, y + 1, z)]) w.setBlock(x, y, z, B.DIRT, 0);
        return;
      case 6: // fire burns out or spreads to flammable blocks
        if (Math.random() < 0.4) { w.setBlock(x, y, z, 0, 0); return; }
        for (let t = 0; t < 3; t++) {
          const nx = x + ((Math.random() * 3) | 0) - 1, ny = y + ((Math.random() * 3) | 0) - 1, nz = z + ((Math.random() * 3) | 0) - 1;
          if (RT.flam[w.getBlock(nx, ny, nz)] && Math.random() < 0.3) w.setBlock(nx, ny, nz, B.FIRE, 0);
        }
        return;
      case 8: { // cocoa: one age in five random ticks (CocoaBlock), up to 2
        const m = w.getMeta(x, y, z);
        if ((m >> 2) < 2 && Math.random() < 0.2) w.setBlock(x, y, z, id, m + 4);
        return;
      }
      case 7: { // farmland: moist within 4 blocks of water
        let wet = false;
        for (let dz = -4; dz <= 4 && !wet; dz++) for (let dx = -4; dx <= 4 && !wet; dx++) if (w.getBlock(x + dx, y, z + dz) === B.WATER) wet = true;
        const want = wet ? B.FARMLAND_MOIST : B.FARMLAND;
        if (want !== id) w.setBlock(x, y, z, want, 0);
      }
    }
  }

  tickFurnaces(dt) {
    const F = this.meta && this.meta.furnaces;
    if (!F) return;
    for (const k in F) {
      const f = F[k], s = f.slots;
      const out = s[0] ? SMELT[s[0].id] : 0;
      const canOut = out && (!s[2] || (s[2].id === out && s[2].count < maxStack(out)));
      if (f.burn <= 0 && canOut && s[1] && fuelValue(s[1].id)) {
        f.burnMax = f.burn = fuelValue(s[1].id) / 8;
        if (s[1].id === IT.LAVA_BUCKET) s[1] = { id: IT.BUCKET, count: 1 }; else if (--s[1].count <= 0) s[1] = null;
      }
      const was = f.lit;
      if (f.burn > 0) { f.burn -= dt; f.lit = true; if (canOut) { f.cook += dt; if (f.cook >= 10) { f.cook = 0; if (s[2]) s[2].count++; else s[2] = { id: out, count: 1 }; if (--s[0].count <= 0) s[0] = null; } } else f.cook = 0; }
      else { f.lit = false; f.cook = Math.max(0, f.cook - dt * 2); }
      if (was !== f.lit) {
        const [x, y, z] = k.split(',').map(Number);
        if (!this.world.isLoaded(x, z)) continue;
        const cur = this.world.getBlock(x, y, z);
        if (cur === B.FURNACE || cur === B.FURNACE_LIT) this.world.setBlock(x, y, z, f.lit ? B.FURNACE_LIT : B.FURNACE, this.world.getMeta(x, y, z));
        else delete F[k];
      }
      if (UI.invOpen && UI.invKind === 'furnace' && UI.furnace === f && UI.furnaceEls && (this.frameN & 7) === 0) UI.refreshInventory();
    }
  }

  // ------------------------------------------------------------------ particles
  // Minecraft's TerrainParticle: a random 4x4-pixel patch of the block's particle texture, colour
  // x0.6, 0.1..0.2 blocks across, lifetime 4 / (r * 0.9 + 0.1) ticks; per tick vy -= 0.04, move
  // with collision, velocity x0.98 (x0.7 sideways on the ground). Simulated at 20 ticks/s and
  // drawn between ticks.
  particleLayer(id) {
    // a block's particle texture: grass-like blocks use dirt, logs their bark, others the side
    if (id === B.GRASS || id === B.PODZOL || id === B.MYCELIUM || id === B.DIRT_PATH || id === B.SNOWY_GRASS) return FTEX[B.DIRT * 6];
    return FTEX[id * 6 + (SHAPE[id] === SH.CUBE ? 0 : 2)];
  }
  addParticle(x, y, z, vx, vy, vz, layer) {
    // Particle(): random spread of the given direction, normalised to (r + r + 1) * 0.15 * 0.4, +0.1 up
    vx += (Math.random() * 2 - 1) * 0.4; vy += (Math.random() * 2 - 1) * 0.4; vz += (Math.random() * 2 - 1) * 0.4;
    const sp = (Math.random() + Math.random() + 1) * 0.15, l = Math.hypot(vx, vy, vz) || 1;
    const q = { x, y, z, px: x, py: y, pz: z, vx: vx / l * sp * 0.4, vy: vy / l * sp * 0.4 + 0.1, vz: vz / l * sp * 0.4,
      age: 0, life: Math.floor(4 / (Math.random() * 0.9 + 0.1)), layer, u: Math.random() * 12, v: Math.random() * 12,
      s: 0.1 * (Math.random() * 0.5 + 0.5), ground: false };
    this.particles.push(q);
    return q;
  }
  // block broken: 4 x 4 x 4 particles spread through the block, flying out from its centre
  destroyParticles(x, y, z, id) {
    if (!id) return;
    const layer = this.particleLayer(id);
    for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) for (let k = 0; k < 4; k++) {
      const ox = (i + 0.5) / 4, oy = (j + 0.5) / 4, oz = (k + 0.5) / 4;
      this.addParticle(x + ox, y + oy, z + oz, ox - 0.5, oy - 0.5, oz - 0.5, layer);
    }
    this.trimParticles();
  }
  // while mining: one particle per tick just outside the hit face, slow (power 0.2) and small (0.6)
  crackParticle(x, y, z, id, face) {
    if (!id) return;
    const f = 0.1, r = () => Math.random() * (1 - 2 * f) + f;
    let px = x + r(), py = y + r(), pz = z + r();
    const n = FACE_N[face];
    if (n[0]) px = x + (n[0] > 0 ? 1 + f : -f);
    if (n[1]) py = y + (n[1] > 0 ? 1 + f : -f);
    if (n[2]) pz = z + (n[2] > 0 ? 1 + f : -f);
    const q = this.addParticle(px, py, pz, 0, 0, 0, this.particleLayer(id));
    q.vx *= 0.2; q.vy = (q.vy - 0.1) * 0.2 + 0.1; q.vz *= 0.2; q.s *= 0.6;
    this.trimParticles();
  }
  // a few loose particles (explosion debris, bone meal)
  spawnParticles(x, y, z, id, n) {
    if (!id) return;
    const layer = this.particleLayer(id);
    for (let i = 0; i < n; i++) this.addParticle(x + Math.random(), y + Math.random(), z + Math.random(), Math.random() - 0.5, Math.random() - 0.5, Math.random() - 0.5, layer);
    this.trimParticles();
  }
  trimParticles() { if (this.particles.length > 4000) this.particles.splice(0, this.particles.length - 4000); }
  updateParticles(dt) {
    this.partAcc = Math.min((this.partAcc || 0) + dt, 0.25);
    while (this.partAcc >= 0.05) { this.partAcc -= 0.05; this.tickParticles(); }
    this.partAlpha = this.partAcc / 0.05;
  }
  tickParticles() {
    const w = this.world, P = this.particles;
    const solid = (x, y, z) => SOLID[w.getBlock(Math.floor(x), Math.floor(y), Math.floor(z))] === 1;
    for (let i = P.length - 1; i >= 0; i--) {
      const q = P[i];
      q.px = q.x; q.py = q.y; q.pz = q.z;
      if (++q.age >= q.life) { P.splice(i, 1); continue; }
      q.vy -= 0.04;
      // move axis by axis, stopping at solid blocks (the particle box is 0.2 wide)
      const ny = q.y + q.vy;
      if (solid(q.x, q.vy < 0 ? ny - 0.1 : ny + 0.1, q.z)) { q.ground = q.vy < 0; q.vy = 0; } else { q.y = ny; q.ground = false; }
      const nx = q.x + q.vx;
      if (solid(nx + Math.sign(q.vx) * 0.1, q.y, q.z)) q.vx = 0; else q.x = nx;
      const nz = q.z + q.vz;
      if (solid(q.x, q.y, nz + Math.sign(q.vz) * 0.1)) q.vz = 0; else q.z = nz;
      q.vx *= 0.98; q.vy *= 0.98; q.vz *= 0.98;
      if (q.ground) { q.vx *= 0.7; q.vz *= 0.7; }
    }
  }

  // ------------------------------------------------------------------ render
  lightAt(x, y, z, env) {
    const l = this.world.getLight(Math.floor(x), Math.floor(y), Math.floor(z));
    const s = (l >> 4) / 15, b = (l & 15) / 15;
    const sv = s / (4 - 3 * s) * (0.08 + 0.92 * env.day), bv = b / (4 - 3 * b);
    return Math.min(1, 0.05 + Math.max(sv, bv) * 0.95);
  }
  renderFrame(dt) {
    const r = this.r, w = this.world, p = this.player;
    if (this.loadingWorld) this.updateCamera();
    const env = this.environment();
    r.updateLightmap(env, Settings.bright, false);
    const cam = this.cam;
    const time = performance.now() / 1000;
    const tick = Math.floor(performance.now() / 50);
    const vy = this.camMode === 2 ? p.yaw + Math.PI : p.yaw, vpch = this.camMode === 2 ? -p.pitch : p.pitch;
    r.gpuOn = this.debug || !!this.bench;
    r.gpuPoll();
    let t = performance.now();
    r.renderWorld(w, cam, vy, vpch, env, time, tick);
    if (this.ships) this.ships.render(cam, env, time, tick);
    if (this.boats) this.boats.render(cam, env);
    const gl = r.gl;
    this.perfT('rWorld', performance.now() - t);
    // clouds
    t = performance.now(); r.gpuBegin('clouds');
    if (Settings.clouds && !env.underwater) r.drawClouds(cam, env, time + this.time * DAY_LEN);
    this.perfT('rClouds', performance.now() - t);
    // entities / particles / player model
    t = performance.now(); r.gpuBegin('particles');
    this.drawParticles(env);
    this.perfT('rParticles', performance.now() - t);
    t = performance.now(); r.gpuBegin('entities');
    if (this.ents) this.ents.render(env, cam);
    this.perfT('rEntities', performance.now() - t);
    t = performance.now();
    r.renderTranslucent(cam, env, time, tick);
    this.perfT('rWater', performance.now() - t);
    // selection outline + cracks
    t = performance.now(); r.gpuBegin('hand');
    if (this.target && !this.hideHud) this.drawSelection(env);
    if (this.ents && this.ents.cracks.size) this.drawMobCracks();
    // hand
    if (!this.camMode && !this.hideHud && !this.sleeping) this.drawHand(env, dt);   // no hand in bed (GameRenderer.renderItemInHand)
    this.drawInWall();
    r.gpuEnd();
    this.perfT('rHand', performance.now() - t);
    this.perfT('rVis', r.cpuVis || 0); this.perfT('rTerrain', r.cpuTerrain || 0); this.perfT('rSort', r.cpuSort || 0);
    gl.enable(gl.DEPTH_TEST);
  }
  // Minecraft's in-wall overlay (ScreenEffectRenderer): with the eye inside an opaque block the
  // screen shows that block's texture at 1/10 brightness, so one cannot look through the ground.
  drawInWall() {
    if (this.camMode) return;
    const w = this.world, e = this.cam, hw = PLAYER_W * 0.4;
    let id = 0;
    for (let i = 0; i < 8 && !id; i++) {
      const b = w.getBlock(Math.floor(e[0] + ((i & 1) - 0.5) * 2 * hw), Math.floor(e[1] + (((i >> 1) & 1) - 0.5) * 0.1), Math.floor(e[2] + (((i >> 2) & 1) - 0.5) * 2 * hw));
      if (OPAQUE[b] && SOLID[b]) id = b;
    }
    if (!id) return;
    const gl = this.r.gl, L = this.particleLayer(id), c = 0.1;
    const I = this.inWallVP || (this.inWallVP = new Float32Array([1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1]));
    const v = this.inWallV || (this.inWallV = new Float32Array([-1, -1, 0, 0, 1, 0, c, c, c, 1, 1, -1, 0, 1, 1, 0, c, c, c, 1, 1, 1, 0, 1, 0, 0, c, c, c, 1,
      -1, -1, 0, 0, 1, 0, c, c, c, 1, 1, 1, 0, 1, 0, 0, c, c, c, 1, -1, 1, 0, 0, 0, 0, c, c, c, 1]));
    for (let i = 5; i < 60; i += 10) v[i] = L;
    gl.disable(gl.DEPTH_TEST);
    this.r.drawArr(v, 6, null, 0, I);
    gl.enable(gl.DEPTH_TEST);
  }
  drawSelection(env) {
    const r = this.r, gl = r.gl, t = this.target, cam = this.cam;
    const boxes = blockBoxes(this.world, t.id, t.meta, t.x, t.y, t.z);
    // the outline of the whole shape (no lines where its boxes meet), each point pulled a little
    // toward the eye so the lines stay in front of the faces they lie on
    const E = shapeEdges(boxes), need = E.length;
    let pts = this.selPts;
    if (!pts || pts.length < need) pts = this.selPts = new Float32Array(Math.max(need, 72 * 8));
    let n = 0;
    for (let i = 0; i < E.length; i += 3) {
      const x = t.x + E[i] - cam[0], y = t.y + E[i + 1] - cam[1], z = t.z + E[i + 2] - cam[2];
      const k = 1 - 0.004 / (Math.sqrt(x * x + y * y + z * z) || 1);
      pts[n++] = x * k; pts[n++] = y * k; pts[n++] = z * k;
    }
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(false);
    r.drawLines(pts.subarray(0, n), SEL_COLOR);
    // crack overlay
    if (this.breaking && this.breaking.p > 0) {
      const stage = Math.min(9, Math.floor(this.breaking.p * 10));
      const layer = this.assets.layers['destroy_stage_' + stage];
      const v = this.crackVB || (this.crackVB = new VBuf());
      v.n = 0;
      for (const b of boxes) this.pushBox(v, t.x + b[0] - cam[0], t.y + b[1] - cam[1], t.z + b[2] - cam[2], t.x + b[3] - cam[0], t.y + b[4] - cam[1], t.z + b[5] - cam[2], layer, WHITE4, 0.004);
      gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(-1, -1);
      r.drawArr(v.view(), v.n / 10, null, 0.01);
      gl.disable(gl.POLYGON_OFFSET_FILL);
    }
    gl.depthMask(true); gl.disable(gl.BLEND);
  }
  // the cracks of blocks a mob is breaking (Level.destroyBlockProgress: a zombie at a door)
  drawMobCracks() {
    const r = this.r, gl = r.gl, cam = this.cam, w = this.world, v = this.mobCrackVB || (this.mobCrackVB = new VBuf());
    v.n = 0;
    for (const c of this.ents.cracks.values()) {
      const id = w.getBlock(c.x, c.y, c.z);
      if (!id) continue;
      const layer = this.assets.layers['destroy_stage_' + Math.min(9, c.stage)];
      for (const b of blockBoxes(w, id, w.getMeta(c.x, c.y, c.z), c.x, c.y, c.z) || []) this.pushBox(v, c.x + b[0] - cam[0], c.y + b[1] - cam[1], c.z + b[2] - cam[2], c.x + b[3] - cam[0], c.y + b[4] - cam[1], c.z + b[5] - cam[2], layer, WHITE4, 0.004);
    }
    if (!v.n) return;
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA); gl.depthMask(false);
    gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(-1, -1);
    r.drawArr(v.view(), v.n / 10, null, 0.01);
    gl.disable(gl.POLYGON_OFFSET_FILL);
    gl.depthMask(true); gl.disable(gl.BLEND);
  }
  // push a textured box (camera relative coords), 6 faces, into vertex list [x,y,z,u,v,layer,r,g,b,a]
  pushBox(v, x0, y0, z0, x1, y1, z1, layer, col, grow, faceLayers, shades, uvFromBox) {
    const g = grow || 0;
    x0 -= g; y0 -= g; z0 -= g; x1 += g; y1 += g; z1 += g;
    for (let f = 0; f < 6; f++) {
      const L = faceLayers ? faceLayers[f] : layer, sh = shades ? shades[f] : 1;
      const uv = uvFromBox ? uvFromBox[f] : null;
      const cr = col[0] * sh, cg = col[1] * sh, cb = col[2] * sh, ca = col[3];
      for (let k = 0; k < 6; k++) {
        const i = BOX_TRI[k], c = (f * 4 + i) * 3;
        const u = uv ? uv[i * 2] : BOX_U[i], w = uv ? uv[i * 2 + 1] : BOX_V[i];
        v.push(BOX_C[c] ? x1 : x0, BOX_C[c + 1] ? y1 : y0, BOX_C[c + 2] ? z1 : z0, u, w, L, cr, cg, cb, ca);
      }
    }
  }
  drawParticles(env) {
    if (!this.particles.length) return;
    const r = this.r, cam = this.cam, p = this.player;
    const cy = Math.cos(p.yaw), sy = Math.sin(p.yaw), cp = Math.cos(p.pitch), sp = Math.sin(p.pitch);
    const rx = cy, rz = sy; // right
    const ux = -sy * sp, uy = cp, uz = cy * sp; // up
    const v = this.partVB || (this.partVB = new VBuf());
    v.n = 0;
    const a = this.partAlpha ?? 1;
    for (const q of this.particles) {
      const qx = q.px + (q.x - q.px) * a, qy = q.py + (q.y - q.py) * a, qz = q.pz + (q.z - q.pz) * a;
      const x = qx - cam[0], y = qy - cam[1], z = qz - cam[2], s = q.s;
      const li = this.lightAt(qx, qy, qz, env) * 0.6;
      const u0 = q.u / 16, v0 = q.v / 16, u1 = u0 + 0.25, v1 = v0 + 0.25, L = q.layer;
      const Rx = rx * s, Rz = rz * s, Ux = ux * s, Uy = uy * s, Uz = uz * s;
      for (let k = 0; k < 6; k++) {
        const i = BOX_TRI[k], sr = i === 1 || i === 2 ? 1 : -1, su = i >= 2 ? 1 : -1;
        v.push(x + sr * Rx + su * Ux, y + su * Uy, z + sr * Rz + su * Uz, sr > 0 ? u1 : u0, su > 0 ? v0 : v1, L, li, li, li, 1);
      }
    }
    r.gl.disable(r.gl.CULL_FACE);
    r.drawArr(v.view(), v.n / 10, env, 0.5);
    r.gl.enable(r.gl.CULL_FACE);
  }

  drawHand(env, dt) {
    const r = this.r, gl = r.gl, p = this.player;
    const held = this.inv[this.sel];
    const li = this.lightAt(p.pos[0], p.pos[1] + 1.2, p.pos[2], env);
    // view-space projection
    const proj = this.handProj || (this.handProj = new Float32Array(16));
    M4.persp(proj, 70 * Math.PI / 180, r.canvas.width / r.canvas.height, 0.05, 10);
    const sw = this.swingT || 0, swingA = Math.sin((1 - sw) * Math.PI) * (sw > 0 ? 1 : 0);
    // the hand follows the same view bobbing
    const BV = this.bobView || [0, 0, 0, 0], bx = BV[0], by = BV[1];
    const v = this.heldVB || (this.heldVB = new VBuf());
    v.n = 0;
    gl.clear(gl.DEPTH_BUFFER_BIT);
    if (!held) {
      // arm from the player skin
      const tex = r.entityTexture('player');
      if (!tex) return;
      const ev = this.handVB || (this.handVB = new VBuf());
      ev.n = 0;
      const HP = this.handXP || (this.handXP = new Float64Array(3));
      HP[0] = swingA; HP[1] = bx; HP[2] = by;
      this.pushModelBox(ev, handXform, HP, -0.1, -0.6, -0.1, 0.1, 0.0, 0.1, 40, 16, 4, 12, 4, 64, 64, li);
      gl.enable(gl.CULL_FACE);
      const saveVP = r.vp; r.vp = proj;
      r.drawEnt(ev.view(), ev.n / 10, tex, { fogStart: 1e9, fogEnd: 1e9 + 1, fogColor: [0, 0, 0] });
      r.vp = saveVP;
      return;
    }
    const id = held.id;
    const cube = !isItem(id) && (SHAPE[id] === SH.CUBE || SHAPE[id] === SH.SLAB || SHAPE[id] === SH.STAIRS || SHAPE[id] === SH.CHEST || SHAPE[id] === SH.CACTUS || SHAPE[id] === SH.FARMLAND || SHAPE[id] === SH.FENCE || SHAPE[id] === SH.WALL);
    // held item local -> view (heldXform), offset by the swing, the view bobbing and eating
    const HP = this.heldXP || (this.heldXP = new Float64Array(3)), q = this.heldTQ || (this.heldTQ = new Float64Array(3));
    HP[0] = 0.56 + bx - swingA * 0.25; HP[1] = -0.52 + by + swingA * 0.12 - (this.eating ? Math.abs(Math.sin(this.eating.t * 18)) * 0.03 : 0); HP[2] = -1.0 - swingA * 0.2;
    if (cube) {
      const s = 0.105;
      const h = SHAPE[id] === SH.SLAB ? 0 : s;
      const L = this.heldL || (this.heldL = new Uint16Array(6)), C = this.heldC || (this.heldC = new Float64Array(4));
      for (let k = 0; k < 6; k++) L[k] = FTEX[id * 6 + k];
      if (FLAGS[id] & BF_FACING) L[4] = FRONT[id];
      C[0] = C[1] = C[2] = li; C[3] = 1;
      const tmp = this.heldTmp || (this.heldTmp = new VBuf());
      tmp.n = 0;
      this.pushBox(tmp, -s, -s, -s, s, h, s, 0, C, 0, L, ITEM_SHADE, null);
      const T = tmp.a, n = tmp.n, a = v.reserve(n);
      for (let i = 0; i < n; i += 10) {
        heldXform(HP, T[i], T[i + 1], T[i + 2], q);
        a[i] = q[0]; a[i + 1] = q[1]; a[i + 2] = q[2];
        for (let k = 3; k < 10; k++) a[i + k] = T[i + k];
      }
      v.n = n;
    } else {
      // flat item sprite (double sided), turned by -0.6 around y
      const layer = isItem(id) ? this.assets.layers[itemDef(id)[3]] : (SHAPE[id] === SH.TALL || SHAPE[id] === SH.DOOR ? this.assets.layers[TEXNAMES[id][0]] : FTEX[id * 6 + 2]);
      const s = 0.25, ca = Math.cos(-0.6), sa = Math.sin(-0.6);
      for (let k = 0; k < 6; k++) {
        const i = BOX_TRI[k], x = i === 1 || i === 2 ? s : -s, y = i >= 2 ? s : -s;
        heldXform(HP, x * ca + 0.05, y + 0.08, x * sa, q);
        v.push(q[0], q[1], q[2], BOX_U[i], BOX_V[i], layer, li, li, li, 1);
      }
    }
    gl.disable(gl.CULL_FACE);
    r.drawArr(v.view(), v.n / 10, null, 0.5, proj);
    gl.enable(gl.CULL_FACE);
  }

  // MC-style box with standard skin unwrap. M(MP, x, y, z, o) maps local coords to camera relative
  // ones, written into o (MP: its parameters; a plain function, not a closure made per call). The
  // 8 corners are transformed once and shared by the faces, through one reused buffer, and the
  // vertices go straight into the vertex buffer (no per-corner, per-face or per-vertex temporaries).
  pushModelBox(out, M, MP, x0, y0, z0, x1, y1, z1, u, v, w, h, d, tw, th, light, tint, mirror) {
    const C = this._mbC || (this._mbC = new Float64Array(24)), R = this._mbR || (this._mbR = new Float64Array(24)), q = this._mbQ || (this._mbQ = new Float64Array(3));
    for (let k = 0; k < 8; k++) { M(MP, k & 1 ? x1 : x0, k & 2 ? y1 : y0, k & 4 ? z1 : z0, q); C[k * 3] = q[0]; C[k * 3 + 1] = q[1]; C[k * 3 + 2] = q[2]; }
    // uv rects [u0, v0, u1, v1] of +x, -x, top, bottom, back (+z), front (-z)
    R[0] = u; R[1] = v + d; R[2] = u + d; R[3] = v + d + h;
    R[4] = u + d + w; R[5] = v + d; R[6] = u + d + w + d; R[7] = v + d + h;
    R[8] = u + d; R[9] = v; R[10] = u + d + w; R[11] = v + d;
    R[12] = u + d + w; R[13] = v; R[14] = u + d + w + w; R[15] = v + d;
    R[16] = u + d + w + d; R[17] = v + d; R[18] = u + d + w + d + w; R[19] = v + d + h;
    R[20] = u + d; R[21] = v + d; R[22] = u + d + w; R[23] = v + d + h;
    const tr = tint ? tint[0] : 1, tg = tint ? tint[1] : 1, tb = tint ? tint[2] : 1;
    // 36 vertices of 10 floats written straight into the vertex buffer (out: a VBuf)
    const a = out.reserve(360);
    let n = out.n;
    for (let f = 0; f < 6; f++) {
      const L = light * MB_SHADE[f], cr = L * tr, cg = L * tg, cb = L * tb;
      const ua = R[f * 4] / tw, va = R[f * 4 + 1] / th, ub = R[f * 4 + 2] / tw, vb = R[f * 4 + 3] / th;
      for (let t = 0; t < 6; t++) {
        const i = MB_TRI[t], c = MB_FACES[f * 4 + i] * 3;
        // corner uv: 0 (u0,v1) 1 (u1,v1) 2 (u1,v0) 3 (u0,v0), mirrored horizontally when asked
        const left = (i === 0 || i === 3) !== !!mirror;
        a[n] = C[c]; a[n + 1] = C[c + 1]; a[n + 2] = C[c + 2]; a[n + 3] = left ? ua : ub; a[n + 4] = i < 2 ? vb : va;
        a[n + 5] = 0; a[n + 6] = cr; a[n + 7] = cg; a[n + 8] = cb; a[n + 9] = 1;
        n += 10;
      }
    }
    out.n = n;
  }

  // ------------------------------------------------------------------ HUD
  updateHotbar() { if (this.inv[this.sel] !== this.lastHeld) { this.lastHeld = this.inv[this.sel]; this.atkT = 0; } UI.updateHotbar(); }
  // attack indicator under the crosshair (shown while the weapon recharges)
  updateAttackBar() {
    const f = this.attackStrength(attackStats(this.inv[this.sel]).speed);
    const v = f >= 1 ? -1 : Math.round(f * 16);
    if (v === this.atkBarV) return;
    this.atkBarV = v;
    const el = $('atk');
    el.style.display = v < 0 ? 'none' : 'block';
    if (v >= 0) el.firstChild.style.width = v + 'px';
  }
  updateSurvivalHud() { UI.updateSurvival(); }
  updateDebug() {
    const el = $('debug'), bi = $('biomeInfo');
    if ((this.frameN & 7) === 0 && this.world) {
      const p = this.player;
      const b = this.world.biomeAt3(Math.floor(p.pos[0]), Math.floor(p.pos[1] + 1.6), Math.floor(p.pos[2]));
      const name = BIOME_LIST[b] ? (CUR_LANG === 'ru' ? BIOME_LIST[b][2] : BIOME_LIST[b][1]) : '?';
      bi.textContent = (Settings.fps ? this.fps + ' FPS\n' : '') + T('biome') + ': ' + name + (this.player.flying ? '\n' + T('flying') : '');
    }
    bi.classList.toggle('hidden', this.debug);
    if (!this.debug) { el.classList.add('hidden'); return; }
    el.classList.remove('hidden');
    if ((this.frameN & 7) !== 0) return;
    const p = this.player, w = this.world, r = this.r;
    const x = Math.floor(p.pos[0]), y = Math.floor(p.pos[1]), z = Math.floor(p.pos[2]);
    const lt = w.getLight(x, y, z);
    const bid = w.biomeAt3(x, y + 1, z);
    const dirs = ['S (+Z)', 'W (-X)', 'N (-Z)', 'E (+X)'];
    let t = `Maincraft WebGL2 · сборка ${BUILD_ID}  ${this.fps} fps\n`;
    t += `XYZ: ${p.pos[0].toFixed(2)} / ${p.pos[1].toFixed(2)} / ${p.pos[2].toFixed(2)}\n`;
    t += `Chunk: ${x >> 4} ${z >> 4}  Facing: ${dirs[p.facingDir()]}\n`;
    t += `Biome: ${BIOME_LIST[bid] ? (CUR_LANG === "ru" ? BIOME_LIST[bid][2] : BIOME_LIST[bid][1]) : '?'}\n`;
    t += `Light: sky ${lt >> 4}  block ${lt & 15}\n`;
    t += `Columns: ${w.cols.size}  gen ${w.genInFlight}  mesh ${w.meshInFlight}  lit-wait ${w.pendingLit.size}\n`;
    t += `Sections: ${r.stats.sections}  draws ${r.stats.draws}  quads ${r.stats.quads}\n`;
    t += `Time: ${Math.floor((this.time * 24 + 6) % 24)}:${String(Math.floor((this.time * 1440) % 60)).padStart(2, '0')}  Day ${this.days || 0}\n`;
    { const ld = this.localDifficulty(x, z); t += `Local Difficulty: ${ld.eff.toFixed(2)} // ${ld.special.toFixed(2)} (${this.difficulty}, Day ${this.days || 0})\n`; }
    if (this.target) t += `Target: ${B_KEY[this.target.id]} [${this.target.meta}] @ ${this.target.x} ${this.target.y} ${this.target.z}\n`;
    if (this.bench) t += `\n>>> БЕНЧМАРК: ${this.bench.phase === 'wait' ? 'жду догрузки мира ' + this.bench.t.toFixed(0) + ' с' : 'идёт ' + this.bench.t.toFixed(1) + ' / 12 с'} (B — отменить)\n`;
    else if (this.benchText) t += '\n' + this.benchText + '\n';
    t += '\n' + this.perfReport();
    t += this.copiedT > performance.now() ? '\n>>> отчёт скопирован в буфер обмена' : '\nP — скопировать отчёт целиком · B — бенчмарк (12 с на месте, результат в окне; он же в меню паузы)';
    el.textContent = t;
  }

  // Performance report for F3 (and the P key, which copies it with the header as text)
  perfReport() {
    const r = this.r, w = this.world, P = this.perf || {}, G = r.gpuMs, cv = r.canvas;
    const f = (v, d = 2) => (v || 0).toFixed(d);
    const n = Math.min(this.ftI || 0, 600), a = Array.from((this.ftHist || new Float32Array(0)).subarray(0, n)).sort((x, y) => x - y);
    const avg = n ? a.reduce((s, v) => s + v, 0) / n : 0, p99 = n ? a[Math.min(n - 1, Math.floor(n * 0.99))] : 0, worst = n ? a[n - 1] : 0;
    // uploads to the GPU per second
    const now = performance.now();
    if (!this.upS || now - this.upS.t > 1000) {
      const prev = this.upS;
      this.upS = { t: now, n: r.upN || 0, b: r.upBytes || 0, rate: prev ? ((r.upN || 0) - prev.n) / ((now - prev.t) / 1000) : 0, bRate: prev ? ((r.upBytes || 0) - prev.b) / ((now - prev.t) / 1000) : 0 };
    }
    const L = [];
    L.push(`== ПРОИЗВОДИТЕЛЬНОСТЬ ==  FPS ${this.fps} · кадр ${f(avg)} мс · 1% low ${f(p99 ? 1000 / p99 : 0, 0)} fps · худший ${f(worst, 1)} мс`);
    L.push(`GPU: ${r.gpuName || '?'}`);
    L.push(`экран ${cv.width}x${cv.height} · dpr ${f(window.devicePixelRatio || 1)} · масштаб ${Settings.scale}${Settings.autoScale ? ` (авто ×${f(this.dynScale || 1)})` : ''} · дальность ${Settings.renderDist} · листва ${['быстрая', 'оптим.', 'красивая', 'адаптивная'][Settings.leaves]} · облака ${Settings.clouds ? 'да' : 'нет'} · колыхание ${Settings.sway ? 'да' : 'нет'} · multiDraw ${r.multiDraw ? 'да' : 'нет'}`);
    if (r.tq) {
      const names = ['solid', 'cutout', 'sky', 'clouds', 'particles', 'entities', 'water', 'hand'];
      let sum = 0; for (const k of names) sum += G[k] || 0;
      L.push(`GPU мс: всего ${f(sum)} | рельеф ${f(G.solid)} листва ${f(G.cutout)} небо ${f(G.sky)} облака ${f(G.clouds)} частицы ${f(G.particles)} мобы ${f(G.entities)} вода ${f(G.water)} рука ${f(G.hand)}`);
    } else L.push('GPU мс: таймер видеокарты недоступен в этом браузере (EXT_disjoint_timer_query_webgl2)');
    L.push(`CPU мс: кадр ${f(P.frame)} | мир ${f(P.update)} (мобы ${f(P.mobs)}, частицы ${f(P.particlesSim)}) · тики ${f(P.ticks)} · чанки ${f(P.stream)} · загрузка в GPU ${f(P.upload)} · HUD ${f(P.hud)}`);
    L.push(`CPU рендер ${f(P.render)}: видимость ${f(P.rVis)} · рельеф ${f(P.rTerrain)} · облака ${f(P.rClouds)} · частицы ${f(P.rParticles)} · мобы ${f(P.rEntities)} · вода ${f(P.rWater)} (сортировка ${f(P.rSort)}) · рука ${f(P.rHand)}`);
    const ps = r.pstat, q = ps[0].quads + ps[1].quads + ps[2].quads;
    L.push(`секции: видно ${r.visCount} · отброшено туманом ${r.stats.fogCulled || 0} · вызовов отрисовки ${r.stats.draws} · треугольников ${(q * 2 / 1000).toFixed(0)}k`);
    const pn = ['рельеф', 'листва', 'вода'];
    for (let i = 0; i < 3; i++) L.push(`  ${pn[i]}: ${ps[i].sec} секций · ${ps[i].draws} draw · ${(ps[i].quads / 1000).toFixed(1)}k квадов (отсечено по направлению ${(ps[i].skipped / 1000).toFixed(1)}k)`);
    const mem = r.meshMemory(w), heap = performance.memory ? performance.memory.usedJSHeapSize : 0;
    L.push(`память: меши ${f(mem.bytes / 1048576, 1)} МБ в ${mem.n} секциях${heap ? ' · JS ' + f(heap / 1048576, 0) + ' МБ' : ''}`);
    L.push(`чанки: колонок ${w.cols.size} · генерация ${w.genInFlight} · меши ${w.meshInFlight} (очередь ${this.meshQueue ? this.meshQueue.length : 0}) · свет ${w.pendingLit.size} · загрузка ${f(this.upS.rate, 0)} секц/с ${f(this.upS.bRate / 1048576, 2)} МБ/с`);
    L.push(this.spikeReport());
    L.push(`сущности: мобы ${this.ents ? this.ents.mobs.length : 0} · предметы ${this.ents ? this.ents.items.length : 0} · падающие ${this.ents ? this.ents.falling.length : 0} · частицы ${this.particles.length}`);
    return L.join('\n');
  }
  copyPerfReport() {
    const p = this.player, w = this.world, b = w.biomeAt(Math.floor(p.pos[0]), Math.floor(p.pos[2]));
    const head = `Maincraft · сборка ${BUILD_ID} · seed ${this.meta && this.meta.seed} · XYZ ${p.pos.map(v => v.toFixed(1)).join(' ')} · yaw ${p.yaw.toFixed(2)} pitch ${p.pitch.toFixed(2)} · биом ${BIOME_LIST[b] ? BIOME_LIST[b][0] : '?'} · ${this.mode}${p.flying ? ' (полёт)' : ''}\n${navigator.userAgent}\n`;
    const text = head + this.perfReport();
    console.log(text);
    const done = () => { this.copiedT = performance.now() + 2500; };
    if (navigator.clipboard && navigator.clipboard.writeText) navigator.clipboard.writeText(text).then(done, () => { this.copyFallback(text); done(); });
    else { this.copyFallback(text); done(); }
  }
  copyFallback(text) {
    const ta = document.createElement('textarea');
    ta.value = text; ta.style.position = 'fixed'; ta.style.opacity = '0';
    document.body.appendChild(ta); ta.select();
    try { document.execCommand('copy'); } catch (e) { }
    ta.remove();
  }

  // ------------------------------------------------------------------ input
  bindInput() {
    const cv = this.canvas;
    document.addEventListener('contextmenu', e => e.preventDefault());
    cv.addEventListener('mousedown', (e) => {
      Sfx.init(); Sfx.resume();
      if (!this.playing || this.loadingWorld) return;
      if (document.pointerLockElement !== cv) { if (!UI.invOpen && !this.surv.dead) { cv.requestPointerLock(); } return; }
      if (e.button === 0) { if (!this.attack()) { this.mouse.l = true; this.mouse.lT = 0; } }
      if (e.button === 2) { this.mouse.r = true; this.mouse.rFirst = true; }
      if (e.button === 1) { e.preventDefault(); this.pickBlock(); }
    });
    window.addEventListener('mouseup', (e) => { if (e.button === 0) this.mouse.l = false; if (e.button === 2) this.mouse.r = false; });
    document.addEventListener('pointerlockchange', () => {
      const locked = document.pointerLockElement === cv;
      if (locked) { this.paused = false; UI.hidePause(); }
      else if (this.bench) this.endBenchmark(true);
      else if (this.playing && !UI.invOpen && !this.surv.dead && !this.loadingWorld) { this.paused = true; this.mouse.l = this.mouse.r = false; this.keys = {}; UI.showPause(); }
    });
    document.addEventListener('mousemove', (e) => {
      if (document.pointerLockElement !== cv || this.paused || this.sleeping) return;
      const s = 0.0022 * Settings.sens;
      this.player.yaw += e.movementX * s;
      this.player.pitch = Math.max(-1.5707, Math.min(1.5707, this.player.pitch - e.movementY * s));
    });
    window.addEventListener('wheel', (e) => {
      if (!this.playing || this.paused || UI.invOpen) return;
      this.sel = (this.sel + (e.deltaY > 0 ? 1 : -1) + 9) % 9; this.updateHotbar(); UI.showHeldName();
    }, { passive: true });
    window.addEventListener('keydown', (e) => {
      if (document.activeElement && document.activeElement.tagName === 'INPUT') return;
      if (!this.playing) return;
      if (['Space', 'ArrowUp', 'ArrowDown', 'F3', 'F5', 'F1', 'Tab'].includes(e.code)) e.preventDefault();
      if (e.code === 'KeyE' && !e.repeat) { if (UI.invOpen) UI.closeInventory(); else if (!this.paused) UI.openInventory(); return; }
      if (this.bench && e.code === 'Escape') { this.endBenchmark(true); return; }
      if (UI.invOpen) { if (e.code === 'Escape') UI.closeInventory(); return; }
      if (this.paused) return;
      this.keys[e.code] = true;
      if (e.repeat) return;
      if (e.code.startsWith('Digit')) { const n = +e.code.slice(5); if (n >= 1 && n <= 9) { this.sel = n - 1; this.updateHotbar(); UI.showHeldName(); } }
      if (e.code === 'Space') {
        const now = performance.now();
        if (this.mode === 'creative' && now - this.lastSpace < 300) { this.player.flying = !this.player.flying; this.player.vel[1] = 0; this.lastSpace = 0; }
        else this.lastSpace = now;
      }
      if (e.code === 'KeyW') { const now = performance.now(); if (now - this.lastW < 280) this.player.sprintLatch = true; this.lastW = now; }
      if (e.code === 'F3') this.debug = !this.debug;
      if (e.code === 'KeyP' && this.debug) this.copyPerfReport();
      if (e.code === 'KeyB' && this.debug) this.startBenchmark();
      if (e.code === 'KeyF' && this.mode === 'creative') { this.player.flying = !this.player.flying; this.player.vel[1] = 0; }
      if (e.code === 'F5') this.camMode = (this.camMode + 1) % 3;
      if (e.code === 'F1') { this.hideHud = !this.hideHud; document.body.classList.toggle('nohud', this.hideHud); }
      if (e.code === 'KeyQ') this.dropHeld();
    });
    window.addEventListener('keyup', (e) => { delete this.keys[e.code]; });
    window.addEventListener('blur', () => { this.keys = {}; this.mouse.l = this.mouse.r = false; });
    window.addEventListener('pagehide', () => { if (this.playing) this.saveWorld(false); });
    document.addEventListener('visibilitychange', () => { if (document.hidden && this.playing) this.saveWorld(false); });
  }
  dropHeld() {
    const s = this.inv[this.sel];
    if (!s) return;
    const p = this.player, l = p.look(), e = p.eye();
    const one = { ...s, count: 1 };
    if (this.mode === 'survival') { if (--s.count <= 0) this.inv[this.sel] = null; }
    else this.inv[this.sel] = null;
    this.ents.dropItem(one.id, this.mode === 'survival' ? 1 : s.count, e[0] + l[0] * 0.4, e[1] - 0.3, e[2] + l[2] * 0.4, [l[0] * 6, l[1] * 6 + 2, l[2] * 6]);
    this.updateHotbar();
  }
}

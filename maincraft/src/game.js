'use strict';
// Game: world lifecycle, streaming, loop, environment, interaction, HUD.

const SETTINGS_KEY = 'maincraft.settings.v1';
const IS_TOUCH = (() => { try { return 'ontouchstart' in window || matchMedia('(pointer: coarse)').matches; } catch (e) { return false; } })();
const Settings = {
  renderDist: IS_TOUCH ? 5 : 8, fov: 70, sens: 1, bright: 0.5, clouds: true, fancyLeaves: true, sway: true,
  scale: IS_TOUCH ? 0.75 : 1, fps: false, sound: true, lang: '',
  load() { try { Object.assign(this, JSON.parse(localStorage.getItem(SETTINGS_KEY) || '{}')); } catch (e) { } if (this.lang) CUR_LANG = this.lang; },
  save() { try { const o = {}; for (const k in this) if (typeof this[k] !== 'function') o[k] = this[k]; localStorage.setItem(SETTINGS_KEY, JSON.stringify(o)); } catch (e) { } },
};

const DAY_LEN = 1200; // seconds

class Game {
  constructor(assets, workerSrc) {
    this.assets = assets; this.workerSrc = workerSrc;
    this.canvas = $('gl');
    this.r = new Renderer(this.canvas, assets);
    this.r.whiteLayer = assets.layers.white;
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
    this.surv = { hp: 20, food: 20, sat: 5, air: 300, exh: 0, regenT: 0, starveT: 0, dead: false, hurtT: 0, lavaT: 0 };
    this.spiral = [];
    for (let dz = -40; dz <= 40; dz++) for (let dx = -40; dx <= 40; dx++) this.spiral.push([dx, dz, Math.hypot(dx, dz)]);
    this.spiral.sort((a, b) => a[2] - b[2]);
    this.lastSpace = 0; this.lastW = 0;
    this.saveTimer = 0;
    this.envObj = { fogColor: new Float32Array(3), zenith: new Float32Array(3), sunDir: [0, 1, 0], sunsetColor: [0.95, 0.5, 0.27] };
    this.r.buildClouds(12345);
    this.resize();
    window.addEventListener('resize', () => this.resize());
    this.bindInput();
    requestAnimationFrame(t => this.loop(t));
  }

  resize() {
    const dpr = Math.min(window.devicePixelRatio || 1, 2) * Settings.scale;
    const w = Math.max(1, Math.round(window.innerWidth * dpr)), h = Math.max(1, Math.round(window.innerHeight * dpr));
    if (this.canvas.width !== w || this.canvas.height !== h) { this.canvas.width = w; this.canvas.height = h; }
  }

  // ------------------------------------------------------------------ world lifecycle
  async startWorld(meta) {
    this.meta = meta;
    this.mode = meta.mode || 'creative';
    this.time = typeof meta.time === 'number' ? meta.time : 0.25;
    this.inv = Array.isArray(meta.inv) && meta.inv.length === 36 ? meta.inv.map(s => s && s.id ? s : null) : this.defaultInv();
    this.sel = meta.sel | 0;
    Object.assign(this.surv, { hp: 20, food: 20, sat: 5, air: 300, exh: 0, dead: false }, meta.surv || {});
    show('title', false); show('loading', true);
    $('loadText').textContent = T('loading');
    const edits = await DB.loadCols(meta.id);
    if (this.world) { this.world.pool.terminate(); }
    const nw = Math.max(1, Math.min(6, (navigator.hardwareConcurrency || 4) - 1));
    const w = new World(meta.seed, this.workerSrc, this.assets.layers, { workers: nw });
    w.savedEdits = edits;
    this.meshQueue = [];
    w.onMeshResult = (c, sy, d) => this.meshQueue.push(c, sy, d);
    w.onColumnUnload = (c) => { this.r.freeColumn(c); if (c.edits && c.edits.size) this.pendingSave.set(c.key, c.edits); };
    w.onBreak = (x, y, z, id, m, byUpdate) => this.onBlockBroken(x, y, z, id, m, byUpdate);
    this.world = w;
    this.pendingSave = new Map();
    this.particles.length = 0;
    this.ents = new Entities(this);
    this.r.buildClouds(meta.seed | 0);
    const p = this.player;
    p.vel = [0, 0, 0]; p.flying = false; p.fallStart = null;
    if (meta.player) {
      p.pos = meta.player.pos.slice(); p.yaw = meta.player.yaw; p.pitch = meta.player.pitch; p.flying = !!meta.player.flying;
      this.spawn = meta.spawn || p.pos.slice();
      this.needSpawnDrop = false;
    } else {
      $('loadText').textContent = T('loading');
      const sp = await this.findSpawn();
      p.pos = [sp[0] + 0.5, 200, sp[1] + 0.5]; p.yaw = 0; p.pitch = 0;
      this.spawn = null;
      this.needSpawnDrop = true;
    }
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
    const w = this.world;
    const entries = [];
    for (const [k, m] of this.pendingSave) entries.push([k, m]);
    this.pendingSave.clear();
    for (const k of w.dirtyCols) { const c = w.cols.get(k); if (c && c.edits) entries.push([k, c.edits]); }
    w.dirtyCols.clear();
    const p = this.player;
    Object.assign(this.meta, {
      lastPlayed: Date.now(), time: this.time, mode: this.mode, inv: this.inv, sel: this.sel,
      player: { pos: p.pos.slice(), yaw: p.yaw, pitch: p.pitch, flying: p.flying }, spawn: this.spawn, surv: { ...this.surv },
    });
    await DB.putWorld(this.meta);
    await DB.saveCols(this.meta.id, entries);
    void quitting;
  }

  async quitToTitle() {
    this.playing = false;
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
    // request columns
    for (const [dx, dz, d] of this.spiral) {
      if (d > L + 0.5) break;
      if (w.genInFlight >= maxGen) break;
      const cx = pcx + dx, cz = pcz + dz;
      if (!w.cols.has(colKey(cx, cz))) w.requestColumn(cx, cz);
    }
    // unload far columns
    if ((this.frameN & 31) === 0) {
      for (const c of Array.from(w.cols.values())) {
        const dx = c.cx - pcx, dz = c.cz - pcz;
        if (dx * dx + dz * dz > (L + 2.5) * (L + 2.5)) w.unloadColumn(c);
      }
    }
    // light passes (budgeted)
    const t0 = performance.now();
    if (w.pendingLit.size) {
      const arr = Array.from(w.pendingLit);
      arr.sort((a, b) => ((a.cx - pcx) ** 2 + (a.cz - pcz) ** 2) - ((b.cx - pcx) ** 2 + (b.cz - pcz) ** 2));
      for (const c of arr) {
        if (performance.now() - t0 > 6) break;
        if (c.state !== 1) { w.pendingLit.delete(c); continue; }
        if (w.litPass(c)) {
          w.pendingLit.delete(c);
          let top = -1; for (let s = SECTIONS - 1; s >= 0; s--) if (c.secs[s]) { top = s; break; }
          c.dirty = top >= 0 ? ((1 << (top + 1)) - 1) : 0;
        }
      }
    }
    // meshing
    const maxMesh = w.pool.workers.length * 4;
    for (const [dx, dz, d] of this.spiral) {
      if (d > R + 0.5) break;
      if (w.meshInFlight >= maxMesh) break;
      const c = w.col(pcx + dx, pcz + dz);
      if (!c || c.state !== 2 || !c.dirty) continue;
      let ok = true;
      for (let k = 0; k < 9 && ok; k++) { const n = w.col(c.cx + (k % 3) - 1, c.cz + ((k / 3) | 0) - 1); if (!n || n.state !== 2) ok = false; }
      if (!ok) continue;
      // nearest sections first (vertical distance from the player)
      const psy = (Math.floor(p[1]) - WORLD_MIN_Y) >> 4;
      const order = [];
      for (let s = 0; s < SECTIONS; s++) if ((c.dirty & (1 << s)) && !(c.meshBusy & (1 << s))) order.push(s);
      order.sort((a, b) => Math.abs(a - psy) - Math.abs(b - psy));
      for (const s of order) {
        if (w.meshInFlight >= maxMesh) break;
        if (w.sectionEmpty(c, s)) {
          c.dirty &= ~(1 << s);
          if (c.meshes[s] && c.meshes[s].vbo) { this.r.gl.deleteBuffer(c.meshes[s].vbo); this.r.gl.deleteVertexArray(c.meshes[s].vao); }
          c.meshes[s] = null; c.meshed |= 1 << s;
          continue;
        }
        w.submitMesh(c, s, Settings.fancyLeaves, Settings.sway);
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
    let sd = [Math.cos(a), s, 0.18]; const l = Math.hypot(...sd); sd = sd.map(v => v / l);
    e.sunDir = sd; e.day = day; e.sunset = sunset;
    const zen = [0.02 + 0.44 * day, 0.03 + 0.62 * day, 0.08 + 0.92 * day];
    const fog = [0.03 + 0.7 * day, 0.04 + 0.8 * day, 0.09 + 0.91 * day];
    // sunset tints the horizon when looking towards the sun
    const look = this.player.look();
    const towards = Math.max(0, look[0] * Math.sign(sd[0]));
    const k = sunset * (0.25 + 0.55 * towards);
    fog[0] += (0.98 - fog[0]) * k; fog[1] += (0.55 - fog[1]) * k; fog[2] += (0.3 - fog[2]) * k;
    e.zenith.set(zen); e.fogColor.set(fog);
    e.sunsetColor = [1.0, 0.45 + 0.1 * day, 0.2];
    e.stars = Math.max(0, 1 - day * 1.6);
    e.moonPhase = Math.floor(this.days || 0) % 8;
    e.cloudColor = [0.25 + 0.75 * day, 0.25 + 0.75 * day, 0.3 + 0.7 * day];
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
    const eye = this.cam;
    const ib = this.world ? this.world.getBlock(Math.floor(eye[0]), Math.floor(eye[1]), Math.floor(eye[2])) : 0;
    e.underwater = false;
    if (isWaterId(ib)) {
      e.underwater = true;
      const lt = this.world.getLight(Math.floor(eye[0]), Math.floor(eye[1]), Math.floor(eye[2]));
      const b = Math.max(0.15, ((lt >> 4) / 15) * (0.2 + 0.8 * day));
      e.fogColor.set([0.05 * b, 0.2 * b, 0.45 * b]);
      e.fogStart = 1; e.fogEnd = 26 + 20 * b;
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
    this.stream();
    this.uploadMeshes(this.loadingWorld ? 50 : 3);
    if (this.loadingWorld) {
      const p = this.player;
      const pcx = Math.floor(p.pos[0]) >> 4, pcz = Math.floor(p.pos[2]) >> 4;
      let ready = 0, need = 0;
      for (let dz = -2; dz <= 2; dz++) for (let dx = -2; dx <= 2; dx++) { need++; const c = w.col(pcx + dx, pcz + dz); if (c && c.state === 2 && !c.dirty && !c.meshBusy) ready++; }
      $('loadBar').style.width = Math.round(100 * ready / need) + '%';
      if (ready < need) { this.renderFrame(0); return; }
      if (this.needSpawnDrop) {
        const sx = Math.floor(p.pos[0]), sz = Math.floor(p.pos[2]);
        let best = null;
        for (let r = 0; r <= 12 && !best; r++) for (let dx = -r; dx <= r && !best; dx++) for (let dz = -r; dz <= r && !best; dz++) {
          if (Math.max(Math.abs(dx), Math.abs(dz)) !== r) continue;
          const x = sx + dx, z = sz + dz;
          let y = WORLD_MAX_Y - 1;
          while (y > WORLD_MIN_Y && !w.getBlock(x, y, z)) y--;
          const g = w.getBlock(x, y, z);
          if ((g === B.GRASS || g === B.SAND || g === B.SNOW || g === B.DIRT || g === B.PODZOL || g === B.MYCELIUM || g === B.RED_SAND || g === B.STONE) &&
            (w.getLight(x, y + 1, z) >> 4) === 15) best = [x, y + 1, z];
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
    if (!this.paused && !this.surv.dead) {
      this.update(dt);
    }
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
    this.renderFrame(dt);
    this.updateDebug();
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
    const wasWater = p.inWater;
    let t = dt;
    while (t > 0) { const s = Math.min(t, 1 / 60); p.update(w, s, inp, this.mode); t -= s; }
    if (!wasWater && p.inWater && p.vel[1] < -4) Sfx.splash();
    // footsteps
    const hs = Math.hypot(p.vel[0], p.vel[2]);
    if (p.onGround && hs > 1 && !p.sneaking) {
      this.stepDist = (this.stepDist || 0) + hs * dt;
      if (this.stepDist > 1.7) { this.stepDist = 0; Sfx.block(w.getBlock(Math.floor(p.pos[0]), Math.floor(p.pos[1] - 0.2), Math.floor(p.pos[2])), 'step'); }
    }
    if (this.mode === 'survival') this.survivalTick(dt);
    this.updateCamera();
    this.interact(dt);
    this.ents.update(dt);
    this.tickFurnaces(dt);
    this.updateParticles(dt);
  }

  updateCamera() {
    const p = this.player;
    const e = p.eye();
    const bob = p.bobAmt * 0.06;
    e[1] += Math.abs(Math.cos(p.bob * Math.PI)) * bob - bob * 0.5;
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
        if (bb.snd > 0.25) { bb.snd = 0; Sfx.block(bb.id, 'hit'); this.spawnParticles(bb.x, bb.y, bb.z, bb.id, 2, tg); }
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
    if (!hit || (this.target && this.target.t < hit.t)) return false;
    const held = this.inv[this.sel], t = toolInfo(held);
    let dmg = 1;
    if (t) dmg = t.tool === 'sword' ? [0, 4, 5, 6, 4, 7][t.tier] : t.tool === 'axe' ? [0, 3, 4, 5, 3, 6][t.tier] : 2;
    const crit = !p.onGround && p.vel[1] < 0;
    this.ents.damageMob(hit.e, Math.round(dmg * (crit ? 1.5 : 1)), p.pos);
    this.swing();
    this.damageTool();
    this.surv.exh += 0.1;
    return true;
  }
  swing() { this.swingT = 1; }

  doBreak(x, y, z) {
    const w = this.world;
    const id = w.getBlock(x, y, z);
    if (!id || (HARD[id] < 0 && this.mode !== 'creative')) return;
    if (id === B.BEDROCK && this.mode !== 'creative') return;
    Sfx.block(id, 'break');
    this.spawnParticles(x, y, z, id, 14);
    this.breakingPlayer = true;
    w.breakBlock(x, y, z, false);
    this.breakingPlayer = false;
  }
  onBlockBroken(x, y, z, id, m, byUpdate) {
    if (this.mode !== 'survival') return;
    const drops = dropsFor(id, m, byUpdate ? null : this.inv[this.sel]);
    for (const d of drops) this.ents.dropItem(d.id, d.count, x + 0.5, y + 0.3, z + 0.5);
    this.surv.exh += 0.005;
  }

  use() {
    const w = this.world, p = this.player, tg = this.target;
    const held = this.inv[this.sel];
    const hid = held ? held.id : 0;
    // food
    if (hid && isItem(hid) && itemDef(hid)[5] && itemDef(hid)[5].food && this.mode === 'survival' && this.surv.food < 20) {
      if (!this.eating) this.eating = { t: 0 };
      return;
    }
    if (hid === IT.SHEARS && this.ents) {
      const h = this.ents.raycastMob(p.eye(), p.look(), 3.5);
      if (h && h.e.type === 'sheep' && !h.e.sheared) { h.e.sheared = true; this.ents.dropItem(B.WOOL_WHITE, 1 + (Math.random() * 3 | 0), h.e.pos[0], h.e.pos[1] + 1, h.e.pos[2]); this.swing(); this.damageTool(); return; }
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
      if (id === B.CRAFTING_TABLE && this.mode === 'survival') { UI.openInventory('craft3'); return; }
      if (id === B.CHEST) { UI.openChest(tg.x, tg.y, tg.z); return; }
      if (id === B.FURNACE || id === B.FURNACE_LIT) { UI.openFurnace(tg.x, tg.y, tg.z); return; }
      if (id === B.TNT && hid === IT.FLINT_AND_STEEL) { this.explode(tg.x + 0.5, tg.y + 0.5, tg.z + 0.5, 4, tg); return; }
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
      const seeds = { [IT.WHEAT_SEEDS]: B.WHEAT_0, [IT.CARROT]: B.CARROTS_0, [IT.POTATO]: B.POTATOES_0, [IT.PUMPKIN_SEEDS]: B.PUMPKIN_STEM_0, [IT.MELON_SEEDS]: B.MELON_STEM_0 };
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

  explode(cx, cy, cz, r, tg) {
    const w = this.world;
    if (tg) w.setBlock(tg.x, tg.y, tg.z, 0, 0);
    Sfx.noise && Sfx.ctx && Sfx.noise(Sfx.ctx.currentTime, 1.2, 0.9, 'lowpass', 600, 60, 0.7);
    const R = Math.ceil(r);
    for (let dy = -R; dy <= R; dy++) for (let dz = -R; dz <= R; dz++) for (let dx = -R; dx <= R; dx++) {
      const d = Math.hypot(dx, dy, dz);
      if (d > r * (0.75 + Math.random() * 0.4)) continue;
      const x = Math.floor(cx + dx), y = Math.floor(cy + dy), z = Math.floor(cz + dz);
      const id = w.getBlock(x, y, z);
      if (!id || HARD[id] < 0 || id === B.OBSIDIAN || id === B.WATER || id === B.LAVA) continue;
      if (id === B.TNT) { setTimeout(() => this.world && this.explode(x + 0.5, y + 0.5, z + 0.5, 4, { x, y, z }), 300 + Math.random() * 400); continue; }
      w.setBlock(x, y, z, 0, 0, { noUpdate: true });
      if (Math.random() < 0.08) this.spawnParticles(x, y, z, id, 3);
    }
    for (let dy = -R - 1; dy <= R + 1; dy++) for (let dz = -R - 1; dz <= R + 1; dz++) for (let dx = -R - 1; dx <= R + 1; dx++) {
      if (Math.abs(dx) === R + 1 || Math.abs(dy) === R + 1 || Math.abs(dz) === R + 1) w.scheduleAround(Math.floor(cx + dx), Math.floor(cy + dy), Math.floor(cz + dz));
    }
    const p = this.player;
    const d = Math.hypot(p.pos[0] - cx, p.pos[1] + 0.9 - cy, p.pos[2] - cz);
    if (d < r * 2) {
      const f = (1 - d / (r * 2));
      p.vel[0] += (p.pos[0] - cx) / (d + 0.1) * f * 18; p.vel[1] += 6 * f; p.vel[2] += (p.pos[2] - cz) / (d + 0.1) * f * 18;
      if (this.mode === 'survival') this.hurt(Math.round(f * 20));
    }
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
    if (!tg) return;
    let id = tg.id;
    if (id === B.FARMLAND_MOIST) id = B.FARMLAND;
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
    // exhaustion
    const hs = Math.hypot(p.vel[0], p.vel[2]);
    s.exh += dt * (p.sprinting ? 0.1 * hs : p.swimming ? 0.01 * hs : 0);
    if (this.keys.Space && p.onGround) s.exh += 0.01;
    if (s.exh >= 4) { s.exh -= 4; if (s.sat > 0) s.sat = Math.max(0, s.sat - 1); else s.food = Math.max(0, s.food - 1); }
    s.regenT += dt;
    if (s.regenT >= 4) {
      s.regenT = 0;
      if (s.food >= 18 && s.hp < 20) { s.hp = Math.min(20, s.hp + 1); s.exh += 6; }
      else if (s.food <= 0 && s.hp > 1) this.hurt(1);
    }
    if (s.hurtT > 0) s.hurtT -= dt;
    this.updateSurvivalHud();
  }
  hurt(n) {
    if (this.mode !== 'survival' || this.surv.dead || n <= 0) return;
    this.surv.hp = Math.max(0, this.surv.hp - n);
    this.surv.hurtT = 0.4;
    Sfx.hurt();
    $('hurt').classList.remove('flash'); void $('hurt').offsetWidth; $('hurt').classList.add('flash');
    if (this.surv.hp <= 0) this.die();
    this.updateSurvivalHud();
  }
  die() {
    this.surv.dead = true;
    if (document.pointerLockElement) document.exitPointerLock();
    UI.showDeath();
  }
  respawn() {
    const p = this.player;
    Object.assign(this.surv, { hp: 20, food: 20, sat: 5, air: 300, exh: 0, dead: false });
    this.inv = this.inv.map(() => null);
    p.pos = (this.spawn || [0.5, 100, 0.5]).slice(); p.vel = [0, 0, 0]; p.fallStart = null;
    this.updateHotbar(); this.updateSurvivalHud();
    UI.hideDeath();
  }

  randomTicks() {
    // a few random ticks per column near the player: crops, saplings, grass spread, fire, leaves nothing
    const w = this.world, p = this.player;
    const pcx = Math.floor(p.pos[0]) >> 4, pcz = Math.floor(p.pos[2]) >> 4;
    for (let dz = -4; dz <= 4; dz++) for (let dx = -4; dx <= 4; dx++) {
      const c = w.col(pcx + dx, pcz + dz);
      if (!c || c.state !== 2) continue;
      for (let s = 0; s < SECTIONS; s++) {
        const sec = c.secs[s];
        if (!sec) continue;
        for (let k = 0; k < 2; k++) {
          const i = (Math.random() * 4096) | 0, id = sec.ids[i];
          if (!id) continue;
          const x = c.cx * 16 + (i & 15), z = c.cz * 16 + ((i >> 4) & 15), y = WORLD_MIN_Y + s * 16 + (i >> 8);
          this.randomTick(x, y, z, id);
        }
      }
    }
  }
  randomTick(x, y, z, id) {
    const w = this.world, k = B_KEY[id];
    const crop = /^(WHEAT|CARROTS|POTATOES|PUMPKIN_STEM|MELON_STEM)_([0-2])$/.exec(k);
    if (crop) { if (Math.random() < 0.3 && (w.getLight(x, y, z) >> 4) > 8) w.setBlock(x, y, z, B[crop[1] + '_' + (+crop[2] + 1)], 0); return; }
    if ((k === 'PUMPKIN_STEM_3' || k === 'MELON_STEM_3') && Math.random() < 0.2) {
      const d = (Math.random() * 4) | 0, nx = x + DIRX_W[d], nz = z + DIRZ_W[d];
      if (!w.getBlock(nx, y, nz) && isSoil(w.getBlock(nx, y - 1, nz))) w.setBlock(nx, y, nz, k === 'PUMPKIN_STEM_3' ? B.PUMPKIN : B.MELON, 0);
      return;
    }
    if (/SAPLING$/.test(k)) { if (Math.random() < 0.05) this.growSapling(x, y, z, id); return; }
    if (id === B.DIRT) {
      if (!OPAQUE[w.getBlock(x, y + 1, z)] && (w.getLight(x, y + 1, z) >> 4) >= 9 && !isWaterId(w.getBlock(x, y + 1, z))) {
        for (let t = 0; t < 4; t++) if (w.getBlock(x + ((Math.random() * 3) | 0) - 1, y + ((Math.random() * 3) | 0) - 1, z + ((Math.random() * 3) | 0) - 1) === B.GRASS) { w.setBlock(x, y, z, B.GRASS, 0); break; }
      }
      return;
    }
    if (id === B.GRASS && OPAQUE[w.getBlock(x, y + 1, z)]) { w.setBlock(x, y, z, B.DIRT, 0); return; }
    if (id === B.FIRE) {
      if (Math.random() < 0.4) { w.setBlock(x, y, z, 0, 0); return; }
      for (let t = 0; t < 3; t++) {
        const nx = x + ((Math.random() * 3) | 0) - 1, ny = y + ((Math.random() * 3) | 0) - 1, nz = z + ((Math.random() * 3) | 0) - 1;
        const n = w.getBlock(nx, ny, nz);
        if ((FLAGS[n] & BF_LEAVES) || /PLANKS|LOG|WOOL|WOOD|BOOKSHELF/.test(B_KEY[n] || '')) { if (Math.random() < 0.3) w.setBlock(nx, ny, nz, B.FIRE, 0); }
      }
      return;
    }
    if (id === B.FARMLAND || id === B.FARMLAND_MOIST) {
      let wet = false;
      for (let dz = -4; dz <= 4 && !wet; dz++) for (let dx = -4; dx <= 4 && !wet; dx++) if (w.getBlock(x + dx, y, z + dz) === B.WATER) wet = true;
      const want = wet ? B.FARMLAND_MOIST : B.FARMLAND;
      if (want !== id) w.setBlock(x, y, z, want, 0);
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
  spawnParticles(x, y, z, id, n, hit) {
    if (!id) return;
    const layer = FTEX[id * 6 + (SHAPE[id] === SH.CUBE ? 0 : 2)];
    for (let i = 0; i < n; i++) {
      let px = x + Math.random(), py = y + Math.random(), pz = z + Math.random();
      if (hit) { px = hit.px + (Math.random() - 0.5) * 0.3; py = hit.py + (Math.random() - 0.5) * 0.3; pz = hit.pz + (Math.random() - 0.5) * 0.3; }
      this.particles.push({ x: px, y: py, z: pz, vx: (Math.random() - 0.5) * 3, vy: Math.random() * 3, vz: (Math.random() - 0.5) * 3,
        life: 0.5 + Math.random() * 0.6, layer, u: Math.floor(Math.random() * 12), v: Math.floor(Math.random() * 12), s: 0.08 + Math.random() * 0.06 });
    }
    if (this.particles.length > 600) this.particles.splice(0, this.particles.length - 600);
  }
  updateParticles(dt) {
    const w = this.world;
    for (let i = this.particles.length - 1; i >= 0; i--) {
      const q = this.particles[i];
      q.life -= dt;
      if (q.life <= 0) { this.particles.splice(i, 1); continue; }
      q.vy -= 14 * dt;
      const nx = q.x + q.vx * dt, ny = q.y + q.vy * dt, nz = q.z + q.vz * dt;
      if (SOLID[w.getBlock(Math.floor(nx), Math.floor(ny), Math.floor(nz))] && OPAQUE[w.getBlock(Math.floor(nx), Math.floor(ny), Math.floor(nz))]) { q.vx *= 0.3; q.vz *= 0.3; q.vy = 0; }
      else { q.x = nx; q.y = ny; q.z = nz; }
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
    r.renderWorld(w, cam, vy, vpch, env, time, tick);
    const gl = r.gl;
    // clouds
    if (Settings.clouds && !env.underwater) r.drawClouds(cam, env, time + this.time * DAY_LEN);
    // entities / particles / player model
    this.drawParticles(env);
    if (this.ents) this.ents.render(env, cam);
    r.renderTranslucent(cam, env, time, tick);
    // selection outline + cracks
    if (this.target && !this.hideHud) this.drawSelection(env);
    // hand
    if (!this.camMode && !this.hideHud) this.drawHand(env, dt);
    gl.enable(gl.DEPTH_TEST);
  }
  drawSelection(env) {
    const r = this.r, gl = r.gl, t = this.target, cam = this.cam;
    const boxes = blockBoxes(this.world, t.id, t.meta, t.x, t.y, t.z);
    const pts = [];
    const e = 0.002;
    for (const b of boxes) {
      const x0 = t.x + b[0] - e - cam[0], y0 = t.y + b[1] - e - cam[1], z0 = t.z + b[2] - e - cam[2];
      const x1 = t.x + b[3] + e - cam[0], y1 = t.y + b[4] + e - cam[1], z1 = t.z + b[5] + e - cam[2];
      const c = [[x0, y0, z0], [x1, y0, z0], [x1, y0, z1], [x0, y0, z1], [x0, y1, z0], [x1, y1, z0], [x1, y1, z1], [x0, y1, z1]];
      const E = [0, 1, 1, 2, 2, 3, 3, 0, 4, 5, 5, 6, 6, 7, 7, 4, 0, 4, 1, 5, 2, 6, 3, 7];
      for (const i of E) pts.push(...c[i]);
    }
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(false);
    r.drawLines(new Float32Array(pts), [0, 0, 0, 0.55]);
    // crack overlay
    if (this.breaking && this.breaking.p > 0) {
      const stage = Math.min(9, Math.floor(this.breaking.p * 10));
      const layer = this.assets.layers['destroy_stage_' + stage];
      const v = [];
      for (const b of boxes) this.pushBox(v, t.x + b[0] - cam[0], t.y + b[1] - cam[1], t.z + b[2] - cam[2], t.x + b[3] - cam[0], t.y + b[4] - cam[1], t.z + b[5] - cam[2], layer, [1, 1, 1, 1], 0.004);
      gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(-1, -1);
      r.drawArr(new Float32Array(v), v.length / 10, null, 0.01);
      gl.disable(gl.POLYGON_OFFSET_FILL);
    }
    gl.depthMask(true); gl.disable(gl.BLEND);
  }
  // push a textured box (camera relative coords), 6 faces, into vertex list [x,y,z,u,v,layer,r,g,b,a]
  pushBox(v, x0, y0, z0, x1, y1, z1, layer, col, grow, faceLayers, shades, uvFromBox) {
    const g = grow || 0;
    x0 -= g; y0 -= g; z0 -= g; x1 += g; y1 += g; z1 += g;
    const F = [
      [[x1, y0, z1], [x1, y0, z0], [x1, y1, z0], [x1, y1, z1]],
      [[x0, y0, z0], [x0, y0, z1], [x0, y1, z1], [x0, y1, z0]],
      [[x0, y1, z1], [x1, y1, z1], [x1, y1, z0], [x0, y1, z0]],
      [[x1, y0, z1], [x0, y0, z1], [x0, y0, z0], [x1, y0, z0]],
      [[x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]],
      [[x1, y0, z0], [x0, y0, z0], [x0, y1, z0], [x1, y1, z0]],
    ];
    const UV = [[0, 1], [1, 1], [1, 0], [0, 0]];
    for (let f = 0; f < 6; f++) {
      const q = F[f], L = faceLayers ? faceLayers[f] : layer, sh = shades ? shades[f] : 1;
      const uv = uvFromBox ? uvFromBox[f] : null;
      for (const i of [0, 1, 2, 0, 2, 3]) {
        const c = q[i];
        const u = uv ? uv[i * 2] : UV[i][0], w = uv ? uv[i * 2 + 1] : UV[i][1];
        v.push(c[0], c[1], c[2], u, w, L, col[0] * sh, col[1] * sh, col[2] * sh, col[3]);
      }
    }
  }
  drawParticles(env) {
    if (!this.particles.length) return;
    const r = this.r, cam = this.cam, p = this.player;
    const cy = Math.cos(p.yaw), sy = Math.sin(p.yaw), cp = Math.cos(p.pitch), sp = Math.sin(p.pitch);
    const rx = cy, rz = sy; // right
    const ux = -sy * sp, uy = cp, uz = cy * sp; // up
    const v = [];
    for (const q of this.particles) {
      const x = q.x - cam[0], y = q.y - cam[1], z = q.z - cam[2], s = q.s;
      const li = this.lightAt(q.x, q.y, q.z, env);
      const u0 = q.u / 16, v0 = q.v / 16, u1 = u0 + 0.25, v1 = v0 + 0.25;
      const c = [[x - rx * s - ux * s, y - uy * s, z - rz * s - uz * s, u0, v1], [x + rx * s - ux * s, y - uy * s, z + rz * s - uz * s, u1, v1],
        [x + rx * s + ux * s, y + uy * s, z + rz * s + uz * s, u1, v0], [x - rx * s + ux * s, y + uy * s, z - rz * s + uz * s, u0, v0]];
      for (const i of [0, 1, 2, 0, 2, 3]) v.push(c[i][0], c[i][1], c[i][2], c[i][3], c[i][4], q.layer, li, li, li, 1);
    }
    r.gl.disable(r.gl.CULL_FACE);
    r.drawArr(new Float32Array(v), v.length / 10, env, 0.5);
    r.gl.enable(r.gl.CULL_FACE);
  }

  drawHand(env, dt) {
    const r = this.r, gl = r.gl, p = this.player;
    const held = this.inv[this.sel];
    const li = this.lightAt(p.pos[0], p.pos[1] + 1.2, p.pos[2], env);
    // view-space projection
    const proj = new Float32Array(16);
    M4.persp(proj, 70 * Math.PI / 180, r.canvas.width / r.canvas.height, 0.05, 10);
    const sw = this.swingT || 0, swingA = Math.sin((1 - sw) * Math.PI) * (sw > 0 ? 1 : 0);
    const bob = p.bobAmt;
    const bx = Math.sin(p.bob * Math.PI) * 0.035 * bob, by = -Math.abs(Math.cos(p.bob * Math.PI)) * 0.04 * bob;
    const v = [];
    gl.clear(gl.DEPTH_BUFFER_BIT);
    if (!held) {
      // arm from the player skin
      const tex = r.entityTexture('player');
      if (!tex) return;
      const ev = [];
      const sw2 = swingA;
      const M = (x, y, z) => {
        // arm local -> view: rotate around x by -60deg-swing, around y by 20deg, translate
        const ax = -1.05 - sw2 * 0.5, ay = 0.45 + sw2 * 0.3;
        let y1 = y * Math.cos(ax) - z * Math.sin(ax), z1 = y * Math.sin(ax) + z * Math.cos(ax);
        let x2 = x * Math.cos(ay) + z1 * Math.sin(ay), z2 = -x * Math.sin(ay) + z1 * Math.cos(ay);
        return [x2 + 0.56 + bx - sw2 * 0.2, y1 - 0.62 + by + sw2 * 0.15, z2 - 0.72 - sw2 * 0.15];
      };
      this.pushModelBox(ev, M, -0.1, -0.6, -0.1, 0.1, 0.0, 0.1, 40, 16, 4, 12, 4, 64, 64, li);
      gl.enable(gl.CULL_FACE);
      const saveVP = r.vp; r.vp = proj;
      r.drawEnt(new Float32Array(ev), ev.length / 10, tex, { fogStart: 1e9, fogEnd: 1e9 + 1, fogColor: [0, 0, 0] });
      r.vp = saveVP;
      return;
    }
    const id = held.id;
    const cube = !isItem(id) && (SHAPE[id] === SH.CUBE || SHAPE[id] === SH.SLAB || SHAPE[id] === SH.STAIRS || SHAPE[id] === SH.CHEST || SHAPE[id] === SH.CACTUS || SHAPE[id] === SH.FARMLAND || SHAPE[id] === SH.FENCE || SHAPE[id] === SH.WALL);
    const rotY = 0.78, rotX = 0.32;
    const tf = (x, y, z) => {
      let x1 = x * Math.cos(rotY) + z * Math.sin(rotY), z1 = -x * Math.sin(rotY) + z * Math.cos(rotY);
      let y1 = y * Math.cos(rotX) - z1 * Math.sin(rotX); z1 = y * Math.sin(rotX) + z1 * Math.cos(rotX);
      return [x1 + 0.56 + bx - swingA * 0.25, y1 - 0.52 + by + swingA * 0.12 - (this.eating ? Math.abs(Math.sin(this.eating.t * 18)) * 0.03 : 0), z1 - 1.0 - swingA * 0.2];
    };
    if (cube) {
      const s = 0.105;
      const h = SHAPE[id] === SH.SLAB ? 0 : s;
      const L = [FTEX[id * 6], FTEX[id * 6 + 1], FTEX[id * 6 + 2], FTEX[id * 6 + 3], FTEX[id * 6 + 4], FTEX[id * 6 + 5]];
      if (FLAGS[id] & BF_FACING) L[4] = FRONT[id];
      const sh = [0.72, 0.72, 1, 0.55, 0.82, 0.82];
      const tmp = [];
      this.pushBox(tmp, -s, -s, -s, s, h, s, 0, [li, li, li, 1], 0, L, sh, null);
      for (let i = 0; i < tmp.length; i += 10) { const q = tf(tmp[i], tmp[i + 1], tmp[i + 2]); tmp[i] = q[0]; tmp[i + 1] = q[1]; tmp[i + 2] = q[2]; }
      v.push(...tmp);
    } else {
      // flat item sprite (double sided)
      const layer = isItem(id) ? this.assets.layers[itemDef(id)[3]] : (SHAPE[id] === SH.TALL || SHAPE[id] === SH.DOOR ? this.assets.layers[TEXNAMES[id][0]] : FTEX[id * 6 + 2]);
      const s = 0.25;
      const c = [[-s, -s, 0, 0, 1], [s, -s, 0, 1, 1], [s, s, 0, 1, 0], [-s, s, 0, 0, 0]];
      const rot = (x, y, z) => { const a = -0.6; return tf(x * Math.cos(a) - z * Math.sin(a) + 0.05, y + 0.08, x * Math.sin(a) + z * Math.cos(a)); };
      for (const i of [0, 1, 2, 0, 2, 3]) { const q = rot(c[i][0], c[i][1], c[i][2]); v.push(q[0], q[1], q[2], c[i][3], c[i][4], layer, li, li, li, 1); }
    }
    gl.disable(gl.CULL_FACE);
    r.drawArr(new Float32Array(v), v.length / 10, null, 0.5, proj);
    gl.enable(gl.CULL_FACE);
  }

  // MC-style box with standard skin unwrap. M maps local coords -> camera relative
  pushModelBox(out, M, x0, y0, z0, x1, y1, z1, u, v, w, h, d, tw, th, light, tint, mirror) {
    const tc = tint || [1, 1, 1];
    const faces = [
      // [corners(4) as [x,y,z]], uv rect [u0,v0,u1,v1], shade
      [[[x1, y0, z1], [x1, y0, z0], [x1, y1, z0], [x1, y1, z1]], [u, v + d, u + d, v + d + h], 0.72],             // +x (right side in skin = u..u+d)
      [[[x0, y0, z0], [x0, y0, z1], [x0, y1, z1], [x0, y1, z0]], [u + d + w, v + d, u + d + w + d, v + d + h], 0.72], // -x
      [[[x0, y1, z1], [x1, y1, z1], [x1, y1, z0], [x0, y1, z0]], [u + d, v, u + d + w, v + d], 1.0],               // top
      [[[x1, y0, z1], [x0, y0, z1], [x0, y0, z0], [x1, y0, z0]], [u + d + w, v, u + d + w + w, v + d], 0.55],      // bottom
      [[[x0, y0, z1], [x1, y0, z1], [x1, y1, z1], [x0, y1, z1]], [u + d + w + d, v + d, u + d + w + d + w, v + d + h], 0.8], // back (+z)
      [[[x1, y0, z0], [x0, y0, z0], [x0, y1, z0], [x1, y1, z0]], [u + d, v + d, u + d + w, v + d + h], 0.9],       // front (-z)
    ];
    for (const [q, uv, sh] of faces) {
      const U = mirror ? [[uv[2], uv[3]], [uv[0], uv[3]], [uv[0], uv[1]], [uv[2], uv[1]]] : [[uv[0], uv[3]], [uv[2], uv[3]], [uv[2], uv[1]], [uv[0], uv[1]]];
      const P = q.map(c => M(c[0], c[1], c[2]));
      for (const i of [0, 1, 2, 0, 2, 3]) {
        out.push(P[i][0], P[i][1], P[i][2], U[i][0] / tw, U[i][1] / th, 0, light * sh * tc[0], light * sh * tc[1], light * sh * tc[2], 1);
      }
    }
  }
  drawPlayerModel(env) {
    const r = this.r, p = this.player, cam = this.cam;
    const tex = r.entityTexture('player');
    if (!tex) return;
    const li = this.lightAt(p.pos[0], p.pos[1] + 1, p.pos[2], env);
    const v = [];
    const yaw = p.yaw;
    const walk = Math.sin(p.bob * Math.PI) * 0.7 * p.bobAmt;
    const sneak = p.sneaking ? 0.3 : 0;
    const part = (px, py, pz, rx, fn) => (x, y, z) => {
      // rotate around pivot (px,py,pz) by rx (pitch-like around x), then body yaw
      let y1 = y * Math.cos(rx) - z * Math.sin(rx), z1 = y * Math.sin(rx) + z * Math.cos(rx);
      let X = x + px, Y = y1 + py, Z = z1 + pz;
      if (fn) [X, Y, Z] = fn(X, Y, Z);
      const cy = Math.cos(yaw), sy = Math.sin(yaw);
      const wx = X * cy - Z * sy, wz = X * sy + Z * cy;
      return [p.pos[0] + wx - cam[0], p.pos[1] + Y - cam[1], p.pos[2] + wz - cam[2]];
    };
    const s = 1 / 16;
    const headPitch = (fn) => part(0, 24 * s - sneak * 0.3, 0, -p.pitch, fn);
    this.pushModelBox(v, headPitch(), -4 * s, 0, -4 * s, 4 * s, 8 * s, 4 * s, 0, 0, 8, 8, 8, 64, 64, li);
    this.pushModelBox(v, part(0, 12 * s, 0, sneak), -4 * s, 0, -2 * s, 4 * s, 12 * s, 2 * s, 16, 16, 8, 12, 4, 64, 64, li);
    this.pushModelBox(v, part(-6 * s, 22 * s, 0, walk), -2 * s, -10 * s, -2 * s, 2 * s, 2 * s, 2 * s, 40, 16, 4, 12, 4, 64, 64, li);
    this.pushModelBox(v, part(6 * s, 22 * s, 0, -walk), -2 * s, -10 * s, -2 * s, 2 * s, 2 * s, 2 * s, 32, 48, 4, 12, 4, 64, 64, li);
    this.pushModelBox(v, part(-2 * s, 12 * s, 0, -walk), -2 * s, -12 * s, -2 * s, 2 * s, 0, 2 * s, 0, 16, 4, 12, 4, 64, 64, li);
    this.pushModelBox(v, part(2 * s, 12 * s, 0, walk), -2 * s, -12 * s, -2 * s, 2 * s, 0, 2 * s, 16, 48, 4, 12, 4, 64, 64, li);
    r.gl.disable(r.gl.CULL_FACE);
    r.drawEnt(new Float32Array(v), v.length / 10, tex, env);
    r.gl.enable(r.gl.CULL_FACE);
  }

  // ------------------------------------------------------------------ HUD
  updateHotbar() { UI.updateHotbar(); }
  updateSurvivalHud() { UI.updateSurvival(); }
  updateDebug() {
    const el = $('debug'), bi = $('biomeInfo');
    if ((this.frameN & 7) === 0 && this.world) {
      const p = this.player;
      const b = this.world.biomeAt(Math.floor(p.pos[0]), Math.floor(p.pos[2]));
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
    const bid = w.biomeAt(x, z);
    const dirs = ['S (+Z)', 'W (-X)', 'N (-Z)', 'E (+X)'];
    let t = `Maincraft WebGL2  ${this.fps} fps\n`;
    t += `XYZ: ${p.pos[0].toFixed(2)} / ${p.pos[1].toFixed(2)} / ${p.pos[2].toFixed(2)}\n`;
    t += `Chunk: ${x >> 4} ${z >> 4}  Facing: ${dirs[p.facingDir()]}\n`;
    t += `Biome: ${BIOME_LIST[bid] ? (CUR_LANG === "ru" ? BIOME_LIST[bid][2] : BIOME_LIST[bid][1]) : '?'}\n`;
    t += `Light: sky ${lt >> 4}  block ${lt & 15}\n`;
    t += `Columns: ${w.cols.size}  gen ${w.genInFlight}  mesh ${w.meshInFlight}  lit-wait ${w.pendingLit.size}\n`;
    t += `Sections: ${r.stats.sections}  draws ${r.stats.draws}  quads ${r.stats.quads}\n`;
    t += `Time: ${Math.floor((this.time * 24 + 6) % 24)}:${String(Math.floor((this.time * 1440) % 60)).padStart(2, '0')}  Day ${this.days || 0}\n`;
    if (this.target) t += `Target: ${B_KEY[this.target.id]} [${this.target.meta}] @ ${this.target.x} ${this.target.y} ${this.target.z}\n`;
    el.textContent = t;
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
      else if (this.playing && !UI.invOpen && !this.surv.dead && !this.loadingWorld) { this.paused = true; this.mouse.l = this.mouse.r = false; this.keys = {}; UI.showPause(); }
    });
    document.addEventListener('mousemove', (e) => {
      if (document.pointerLockElement !== cv || this.paused) return;
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

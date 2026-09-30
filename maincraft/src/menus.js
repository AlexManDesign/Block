'use strict';
// Menus, HUD, inventories, touch controls.

const UI = {
  game: null,
  invOpen: false,
  cursor: null,          // item held by the mouse in inventory screens
  invKind: null,         // 'inv' | 'craft3' | 'chest' | 'furnace' | 'creative'
  craftGrid: null,
  selectedWorld: null,

  init(game) {
    this.game = game;
    this.buildHud();
    this.bindMenus();
    if (IS_TOUCH) this.buildTouch();
    this.applyLang();
    document.addEventListener('mousemove', (e) => {
      const c = $('cursorItem');
      c.style.left = (e.clientX - 20) + 'px'; c.style.top = (e.clientY - 20) + 'px';
    });
  },
  applyLang() {
    document.querySelectorAll('[data-t]').forEach(e => { e.textContent = T(e.dataset.t); });
    document.querySelectorAll('[data-ph]').forEach(e => { e.placeholder = T(e.dataset.ph); });
    $('hint').textContent = IS_TOUCH ? '' : T('hintDesktop');
  },

  // ---------------------------------------------------------------- title / worlds
  async showTitle() {
    show('title', true); show('worlds', false); show('createWorld', false); show('settingsBox', false);
    show('pause', false); show('death', false); show('loading', false); show('hud', false); show('titleMain', true);
    this.closeInventory(true);
  },
  async showWorlds() {
    show('titleMain', false); show('worlds', true);
    const list = $('worldList');
    list.innerHTML = '';
    const ws = await DB.listWorlds();
    this.selectedWorld = null;
    $('btnPlay').disabled = true; $('btnDelete').disabled = true;
    if (!ws.length) list.append(el('div', { class: 'empty' }, T('noWorlds')));
    for (const w of ws) {
      const d = new Date(w.lastPlayed || w.created || Date.now());
      const item = el('div', { class: 'world' },
        el('b', null, w.name), el('span', null, `${T(w.mode)} · ${T('lastPlayed')}: ${d.toLocaleDateString()} ${d.toLocaleTimeString().slice(0, 5)}`),
        el('small', null, 'seed ' + w.seed));
      item.onclick = () => {
        list.querySelectorAll('.world').forEach(x => x.classList.remove('sel'));
        item.classList.add('sel'); this.selectedWorld = w; $('btnPlay').disabled = false; $('btnDelete').disabled = false;
      };
      item.ondblclick = () => this.play(w);
      list.append(item);
    }
  },
  play(w) {
    Sfx.init(); Sfx.enabled = Settings.sound;
    show('worlds', false); show('title', false);
    show('hud', true);
    this.game.startWorld(w);
  },
  bindMenus() {
    $('btnSingle').onclick = () => this.showWorlds();
    $('btnSettingsTitle').onclick = () => this.showSettings('title');
    $('btnBackWorlds').onclick = () => { show('worlds', false); show('titleMain', true); };
    $('btnPlay').onclick = () => { if (this.selectedWorld) this.play(this.selectedWorld); };
    $('btnDelete').onclick = async () => {
      if (!this.selectedWorld || !confirm(T('confirmDel'))) return;
      await DB.deleteWorld(this.selectedWorld.id); this.showWorlds();
    };
    $('btnCreate').onclick = () => {
      show('worlds', false); show('createWorld', true);
      $('wName').value = (CUR_LANG === 'ru' ? 'Новый мир' : 'New World');
      $('wSeed').value = '';
      this.newMode = 'creative'; this.updateModeBtn();
    };
    $('btnMode').onclick = () => { this.newMode = this.newMode === 'creative' ? 'survival' : 'creative'; this.updateModeBtn(); };
    $('btnCancelCreate').onclick = () => { show('createWorld', false); this.showWorlds(); };
    $('btnDoCreate').onclick = async () => {
      let seedS = $('wSeed').value.trim(), seed;
      if (!seedS) seed = (Math.random() * 2147483647) | 0;
      else if (/^-?\d+$/.test(seedS)) seed = parseInt(seedS, 10) | 0;
      else { seed = 0; for (const ch of seedS) seed = (Math.imul(seed, 31) + ch.charCodeAt(0)) | 0; }
      const w = { id: 'w' + Date.now().toString(36) + Math.floor(Math.random() * 1e6).toString(36), name: $('wName').value.trim() || 'World',
        seed, mode: this.newMode, created: Date.now(), lastPlayed: Date.now() };
      await DB.putWorld(w);
      show('createWorld', false);
      this.play(w);
    };
    $('btnResume').onclick = () => this.resume();
    $('btnSettingsPause').onclick = () => this.showSettings('pause');
    $('btnQuit').onclick = () => { show('pause', false); this.game.quitToTitle(); };
    $('btnRespawn').onclick = () => { this.game.respawn(); this.resume(); };
    $('btnDeathQuit').onclick = () => { this.game.respawn(); show('death', false); this.game.quitToTitle(); };
    $('btnSettingsDone').onclick = () => { Settings.save(); show('settingsBox', false); if (this.settingsFrom === 'title') show('titleMain', true); else show('pause', true); };
    $('clickToPlay').onclick = () => this.resume();
  },
  updateModeBtn() { $('btnMode').textContent = T('mode') + ': ' + T(this.newMode); },
  resume() {
    if (IS_TOUCH) { this.game.paused = false; this.hidePause(); return; }
    this.game.canvas.requestPointerLock();
    show('clickToPlay', false);
  },
  onWorldReady() {
    clearTimeout(this.hintT); show('hint', true); this.hintT = setTimeout(() => show('hint', false), 20000);
    if (IS_TOUCH) { this.game.paused = false; return; }
    show('clickToPlay', true);
    $('clickToPlay').textContent = T('clickToPlay');
  },
  showPause() { if (!this.game.loadingWorld) show('pause', true); show('clickToPlay', false); },
  hidePause() { show('pause', false); show('clickToPlay', false); },
  showDeath() { show('death', true); },
  hideDeath() { show('death', false); },

  // ---------------------------------------------------------------- settings
  showSettings(from) {
    this.settingsFrom = from;
    show('titleMain', false); show('pause', false); show('settingsBox', true);
    const box = $('settingsList');
    box.innerHTML = '';
    const slider = (label, key, min, max, step, fmt, after) => {
      const val = el('span', null, fmt(Settings[key]));
      const inp = el('input', { type: 'range', min, max, step, value: Settings[key] });
      inp.oninput = () => { Settings[key] = +inp.value; val.textContent = fmt(Settings[key]); if (after) after(); };
      box.append(el('label', { class: 'opt' }, el('span', null, label), inp, val));
    };
    const toggle = (label, key, after) => {
      const b = el('button', { class: 'tog' }, label + ': ' + (Settings[key] ? T('on') : T('off')));
      b.onclick = () => { Settings[key] = !Settings[key]; b.textContent = label + ': ' + (Settings[key] ? T('on') : T('off')); if (after) after(); };
      box.append(b);
    };
    const remesh = () => { const w = this.game.world; if (w) for (const c of w.cols.values()) if (c.state === 2) { let top = -1; for (let s = SECTIONS - 1; s >= 0; s--) if (c.secs[s]) { top = s; break; } c.dirty |= top >= 0 ? (1 << (top + 1)) - 1 : 0; } };
    slider(T('renderDist'), 'renderDist', 2, 24, 1, v => v + ' ' + T('chunks'));
    slider(T('fov'), 'fov', 40, 110, 1, v => String(v));
    slider(T('sens'), 'sens', 0.2, 3, 0.05, v => Math.round(v * 100) + '%');
    slider(T('bright'), 'bright', 0, 1, 0.05, v => Math.round(v * 100) + '%');
    slider(T('scale'), 'scale', 0.4, 1, 0.05, v => Math.round(v * 100) + '%', () => this.game.resize());
    toggle(T('clouds'), 'clouds');
    toggle(T('leaves'), 'fancyLeaves', remesh);
    toggle(T('sway'), 'sway', remesh);
    toggle(T('bobView'), 'bobView');
    toggle(T('fps'), 'fps');
    toggle(T('sound'), 'sound', () => { Sfx.enabled = Settings.sound; });
    const lb = el('button', { class: 'tog' }, T('lang') + ': ' + (CUR_LANG === 'ru' ? 'Русский' : 'English'));
    lb.onclick = () => { CUR_LANG = CUR_LANG === 'ru' ? 'en' : 'ru'; Settings.lang = CUR_LANG; this.applyLang(); this.showSettings(from); };
    box.append(lb);
  },

  // ---------------------------------------------------------------- HUD
  buildHud() {
    const hb = $('hotbar');
    hb.innerHTML = '';
    this.hotSlots = [];
    for (let i = 0; i < 9; i++) {
      const s = slotEl(null, { cls: 'hs' });
      s.onclick = () => { if (IS_TOUCH) { this.game.sel = i; this.updateHotbar(); this.showHeldName(); } };
      hb.append(s); this.hotSlots.push(s);
    }
    this.icons = {
      heart: this.pixelIcon(['..##.##..', '.#######.', '#########', '#########', '.#######.', '..#####..', '...###...', '....#....'], '#e22', '#ffb0b0'),
      heartE: this.pixelIcon(['..##.##..', '.#######.', '#########', '#########', '.#######.', '..#####..', '...###...', '....#....'], '#3a1010', '#3a1010'),
      food: this.pixelIcon(['.....##..', '....####.', '...######', '..######.', '.#####...', '.###.....', '#.#......', '##.......'], '#c8742c', '#f0c080'),
      foodE: this.pixelIcon(['.....##..', '....####.', '...######', '..######.', '.#####...', '.###.....', '#.#......', '##.......'], '#3a2410', '#3a2410'),
      air: this.pixelIcon(['..####..', '.#....#.', '#..#...#', '#.#....#', '#......#', '#......#', '.#....#.', '..####..'], '#6af', '#fff'),
    };
  },
  pixelIcon(rows, col, hi) {
    const c = document.createElement('canvas'); c.width = 9; c.height = 9;
    const x = c.getContext('2d');
    rows.forEach((r, y) => { for (let i = 0; i < r.length; i++) if (r[i] === '#') { x.fillStyle = (y < 3 && i < 4) ? hi : col; x.fillRect(i, y, 1, 1); } });
    return c.toDataURL();
  },
  updateHotbar() {
    const g = this.game;
    if (!this.hotSlots) return;
    for (let i = 0; i < 9; i++) { setSlot(this.hotSlots[i], g.inv[i]); this.hotSlots[i].classList.toggle('sel', i === g.sel); }
    if (IS_TOUCH && $('tMode')) $('tMode').classList.toggle('on', !!this.touchBreak);
  },
  showHeldName() {
    const s = this.game.inv[this.game.sel];
    const e = $('heldName');
    e.textContent = s ? itemName(s.id) : '';
    e.classList.remove('fade'); void e.offsetWidth; e.classList.add('fade');
  },
  updateSurvival() {
    const g = this.game, s = g.surv;
    const on = g.mode === 'survival';
    show('survBars', on);
    if (!on) return;
    const key = `${s.hp}|${s.food}|${Math.ceil(s.air / 30)}`;
    if (key === this.lastSurvKey) return;
    this.lastSurvKey = key;
    const hp = $('hearts'), fd = $('food'), air = $('air');
    const row = (n, full, empty, rev) => {
      let h = '';
      for (let i = 0; i < 10; i++) {
        const v = n - i * 2;
        const src = v >= 1 ? full : empty;
        const half = v === 1;
        h += `<i style="background-image:url(${src});${half ? 'clip-path:inset(0 ' + (rev ? '0 0 50%' : '50% 0 0') + ')' : ''}"></i>`;
      }
      return h;
    };
    hp.innerHTML = row(s.hp, this.icons.heart, this.icons.heartE);
    fd.innerHTML = row(s.food, this.icons.food, this.icons.foodE, true);
    const bubbles = Math.ceil(Math.max(0, s.air) / 30);
    air.innerHTML = s.air < 300 ? Array.from({ length: bubbles }, () => `<i style="background-image:url(${this.icons.air})"></i>`).join('') : '';
  },

  // ---------------------------------------------------------------- inventory
  openInventory(kind) {
    const g = this.game;
    if (!g.playing || g.loadingWorld) return;
    this.invOpen = true;
    this.invKind = kind || (g.mode === 'creative' ? 'creative' : 'inv');
    this.craftGrid = new Array(this.invKind === 'craft3' ? 9 : 4).fill(null);
    if (document.pointerLockElement) document.exitPointerLock();
    g.mouse.l = g.mouse.r = false; g.keys = {};
    show('inventory', true);
    this.tab = this.tab || 'b';
    this.refreshInventory();
  },
  closeInventory(silent) {
    if (!this.invOpen) return;
    const g = this.game;
    // return crafting grid + cursor to the inventory
    if (this.craftGrid) for (const s of this.craftGrid) if (s) g.giveItem(s.id, s.count);
    if (this.cursor && this.invKind !== 'creative') g.giveItem(this.cursor.id, this.cursor.count);
    this.cursor = null; this.craftGrid = null;
    if (this.chestPos) this.saveChest();
    this.chestPos = null; this.furnacePos = null;
    this.invOpen = false;
    show('inventory', false);
    this.updateCursor();
    g.updateHotbar();
    if (!silent && !IS_TOUCH) g.canvas.requestPointerLock();
  },
  openChest(x, y, z) {
    const g = this.game;
    g.chests = g.meta.chests || (g.meta.chests = {});
    this.chestPos = `${x},${y},${z}`;
    this.chestItems = g.chests[this.chestPos] || new Array(27).fill(null);
    this.openInventory('chest');
  },
  openFurnace(x, y, z) {
    const g = this.game;
    const F = g.meta.furnaces || (g.meta.furnaces = {});
    this.furnacePos = `${x},${y},${z}`;
    this.furnace = F[this.furnacePos] || (F[this.furnacePos] = { slots: [null, null, null], burn: 0, burnMax: 0, cook: 0 });
    this.openInventory('furnace');
  },
  saveChest() {
    const g = this.game;
    if (this.chestItems.some(s => s)) g.chests[this.chestPos] = this.chestItems; else delete g.chests[this.chestPos];
  },
  updateCursor() {
    const c = $('cursorItem');
    if (this.cursor) { setSlot(c, this.cursor); c.classList.remove('hidden'); } else c.classList.add('hidden');
  },
  // generic MC click semantics on a slot array
  clickSlot(arr, i, button, shift) {
    const g = this.game;
    let s = arr[i];
    if (shift && s) {
      // quick move between hotbar and main or into player inventory
      const toRange = arr === g.inv ? (i < 9 ? [9, 36] : [0, 9]) : [0, 36];
      const target = arr === g.inv ? g.inv : g.inv;
      let rest = s.count;
      const ms = maxStack(s.id);
      for (let k = toRange[0]; k < toRange[1] && rest > 0; k++) { const t = target[k]; if (t && t.id === s.id && t.count < ms && t.dur === undefined) { const n = Math.min(rest, ms - t.count); t.count += n; rest -= n; } }
      for (let k = toRange[0]; k < toRange[1] && rest > 0; k++) if (!target[k]) { target[k] = { ...s, count: rest }; rest = 0; }
      if (rest > 0) s.count = rest; else arr[i] = null;
      return;
    }
    const c = this.cursor;
    if (button === 2) {
      if (!c && s) { const n = Math.ceil(s.count / 2); this.cursor = { ...s, count: n }; s.count -= n; if (!s.count) arr[i] = null; }
      else if (c && (!s || (s.id === c.id && s.count < maxStack(s.id)))) {
        if (!s) arr[i] = { ...c, count: 1 }; else s.count++;
        if (--c.count <= 0) this.cursor = null;
      }
      return;
    }
    if (!c) { this.cursor = s; arr[i] = null; }
    else if (!s) { arr[i] = c; this.cursor = null; }
    else if (s.id === c.id && s.dur === undefined) {
      const n = Math.min(c.count, maxStack(s.id) - s.count); s.count += n; c.count -= n; if (!c.count) this.cursor = null;
    } else { arr[i] = c; this.cursor = s; }
  },
  refreshInventory() {
    const g = this.game, root = $('invBody');
    root.innerHTML = '';
    const kind = this.invKind;
    const mkGrid = (arr, from, to, cols, onClick) => {
      const grid = el('div', { class: 'grid', style: `grid-template-columns:repeat(${cols},var(--slot))` });
      for (let i = from; i < to; i++) {
        const s = slotEl(arr[i]);
        s.onmousedown = (e) => { e.preventDefault(); onClick(i, e.button, e.shiftKey); this.refreshInventory(); };
        grid.append(s);
      }
      return grid;
    };
    const invClick = (i, b, sh) => this.clickSlot(g.inv, i, b, sh);
    if (kind === 'creative') {
      const tabs = el('div', { class: 'tabs' });
      for (const t of ['b', 'c', 'n', 'f', 't', 's']) {
        const first = { b: B.BRICK, c: B.WOOL_RED, n: B.GRASS, f: B.CRAFTING_TABLE, t: IT.IRON_PICKAXE, s: B.GLASS }[t];
        const tb = el('div', { class: 'tab' + (this.tab === t ? ' on' : ''), title: T('tab_' + t) }, el('img', { src: Icons.get(first) }));
        tb.onclick = () => { this.tab = t; this.refreshInventory(); };
        tabs.append(tb);
      }
      root.append(tabs);
      root.append(el('div', { class: 'invTitle' }, T('tab_' + this.tab)));
      if (this.tab === 's') {
        const inp = el('input', { class: 'search', placeholder: T('search'), value: this.searchText || '' });
        inp.oninput = () => { this.searchText = inp.value; this.fillCatalog(); };
        root.append(inp);
        setTimeout(() => inp.focus(), 0);
      }
      this.catalogEl = el('div', { class: 'catalog' });
      root.append(this.catalogEl);
      this.fillCatalog();
      root.append(el('div', { class: 'invTitle' }, T('inventory')));
      root.append(mkGrid(g.inv, 9, 36, 9, invClick));
      const hot = mkGrid(g.inv, 0, 9, 9, invClick); hot.classList.add('hotrow');
      root.append(hot);
      const trash = el('div', { class: 'slot trash', title: 'X' }, '✕');
      trash.onmousedown = (e) => { e.preventDefault(); this.cursor = null; this.updateCursor(); };
      root.append(trash);
    } else {
      const top = el('div', { class: 'invTop' });
      if (kind === 'furnace') {
        const f = this.furnace;
        root.append(el('div', { class: 'invTitle' }, T('furnace')));
        const sl = (i) => { const e = slotEl(f.slots[i]); e.onmousedown = (ev) => { ev.preventDefault(); if (i === 2) { if (f.slots[2] && (!this.cursor || this.cursor.id === f.slots[2].id)) { if (this.cursor) this.cursor.count += f.slots[2].count; else this.cursor = f.slots[2]; f.slots[2] = null; } } else this.clickSlot(f.slots, i, ev.button, false); this.refreshInventory(); }; return e; };
        const flame = el('div', { class: 'flame' }, el('i', { style: `height:${f.burnMax ? Math.round(100 * f.burn / f.burnMax) : 0}%` }));
        const prog = el('div', { class: 'prog' }, el('i', { style: `width:${Math.round(f.cook / 10 * 100)}%` }));
        this.furnaceEls = { flame, prog };
        root.append(el('div', { class: 'furnace' }, el('div', { class: 'fcol' }, sl(0), flame, sl(1)), prog, sl(2)));
      } else if (kind === 'chest') {
        root.append(el('div', { class: 'invTitle' }, T('chest')));
        root.append(mkGrid(this.chestItems, 0, 27, 9, (i, b, sh) => {
          if (sh && this.chestItems[i]) { const s = this.chestItems[i]; this.chestItems[i] = null; const r = g.giveItem(s.id, s.count); if (r) this.chestItems[i] = { ...s, count: r }; }
          else this.clickSlot(this.chestItems, i, b, false);
        }));
      } else {
        const n = kind === 'craft3' ? 3 : 2;
        const cg = mkGrid(this.craftGrid, 0, n * n, n, (i, b) => this.clickSlot(this.craftGrid, i, b, false));
        const res = craftResult(this.craftGrid.map(s => s ? s.id : 0), n, n);
        const rs = slotEl(res, { cls: 'result' });
        rs.onmousedown = (e) => {
          e.preventDefault();
          if (!res) return;
          const take = () => {
            const r2 = craftResult(this.craftGrid.map(s => s ? s.id : 0), n, n);
            if (!r2) return false;
            if (this.cursor && (this.cursor.id !== r2.id || this.cursor.count + r2.count > maxStack(r2.id))) return false;
            if (this.cursor) this.cursor.count += r2.count; else this.cursor = { id: r2.id, count: r2.count };
            for (let k = 0; k < this.craftGrid.length; k++) { const s = this.craftGrid[k]; if (s && --s.count <= 0) this.craftGrid[k] = null; }
            return true;
          };
          if (e.shiftKey) { let guard = 64; while (guard-- && take()) { const c = this.cursor; this.cursor = null; g.giveItem(c.id, c.count); } }
          else take();
          this.refreshInventory();
        };
        top.append(el('div', { class: 'craftWrap' }, el('div', { class: 'invTitle' }, T('crafting')), el('div', { class: 'craftRow' }, cg, el('span', { class: 'arrow' }, '➜'), rs)));
        root.append(top);
      }
      root.append(el('div', { class: 'invTitle' }, T('inventory')));
      root.append(mkGrid(g.inv, 9, 36, 9, invClick));
      const hot = mkGrid(g.inv, 0, 9, 9, invClick); hot.classList.add('hotrow');
      root.append(hot);
    }
    this.updateCursor();
    g.updateHotbar();
  },
  fillCatalog() {
    const g = this.game, cat = this.catalog || (this.catalog = creativeCatalog());
    let ids;
    if (this.tab === 's') {
      const q = (this.searchText || '').toLowerCase().trim();
      ids = [];
      if (q) for (const t of 'bcnft') for (const id of cat[t]) if (itemName(id).toLowerCase().includes(q) || (isItem(id) ? itemDef(id)[1] : B_EN[id]).toLowerCase().includes(q)) ids.push(id);
    } else ids = cat[this.tab];
    const c = this.catalogEl;
    c.innerHTML = '';
    const frag = document.createDocumentFragment();
    for (const id of ids) {
      const s = el('div', { class: 'slot' });
      const img = el('img', { draggable: 'false', loading: 'lazy' });
      img.src = Icons.get(id);
      s.append(img); s.title = itemName(id);
      s.onmousedown = (e) => {
        e.preventDefault();
        if (this.cursor) { this.cursor = null; }
        else if (e.shiftKey) { const slot = g.inv.slice(0, 9).findIndex(x => !x); g.inv[slot >= 0 ? slot : g.sel] = { id, count: maxStack(id) }; }
        else this.cursor = { id, count: e.button === 2 ? 1 : maxStack(id) };
        this.updateCursor(); g.updateHotbar();
        if (e.shiftKey) this.refreshInventory();
      };
      frag.append(s);
    }
    c.append(frag);
  },

  // ---------------------------------------------------------------- touch controls
  buildTouch() {
    document.body.classList.add('touch');
    const g = this.game;
    g.touch = { joy: null, jump: false, sneak: false };
    const tc = $('touch');
    show(tc, true);
    const joy = $('tJoy'), knob = $('tKnob');
    let joyId = null, jx = 0, jy = 0;
    joy.addEventListener('touchstart', (e) => { const t = e.changedTouches[0]; joyId = t.identifier; const r = joy.getBoundingClientRect(); jx = r.left + r.width / 2; jy = r.top + r.height / 2; e.preventDefault(); moveJ(t); }, { passive: false });
    const moveJ = (t) => {
      let dx = (t.clientX - jx) / 50, dy = (t.clientY - jy) / 50;
      const l = Math.hypot(dx, dy); if (l > 1) { dx /= l; dy /= l; }
      g.touch.joy = [dx, dy]; knob.style.transform = `translate(${dx * 40}px,${dy * 40}px)`;
      g.touch.sprint = dy < -0.95;
    };
    joy.addEventListener('touchmove', (e) => { for (const t of e.changedTouches) if (t.identifier === joyId) moveJ(t); e.preventDefault(); }, { passive: false });
    const endJ = (e) => { for (const t of e.changedTouches) if (t.identifier === joyId) { joyId = null; g.touch.joy = null; g.touch.sprint = false; knob.style.transform = ''; } };
    joy.addEventListener('touchend', endJ); joy.addEventListener('touchcancel', endJ);
    // look + tap/hold on the canvas
    const looks = new Map();
    const cv = g.canvas;
    cv.addEventListener('touchstart', (e) => {
      Sfx.init(); Sfx.resume();
      for (const t of e.changedTouches) looks.set(t.identifier, { x: t.clientX, y: t.clientY, sx: t.clientX, sy: t.clientY, t: performance.now(), moved: false, held: false });
      e.preventDefault();
    }, { passive: false });
    cv.addEventListener('touchmove', (e) => {
      for (const t of e.changedTouches) {
        const L = looks.get(t.identifier); if (!L) continue;
        const dx = t.clientX - L.x, dy = t.clientY - L.y;
        L.x = t.clientX; L.y = t.clientY;
        if (Math.hypot(t.clientX - L.sx, t.clientY - L.sy) > 12) L.moved = true;
        const s = 0.006 * Settings.sens;
        g.player.yaw += dx * s; g.player.pitch = Math.max(-1.57, Math.min(1.57, g.player.pitch - dy * s));
      }
      e.preventDefault();
    }, { passive: false });
    const endL = (e) => {
      for (const t of e.changedTouches) {
        const L = looks.get(t.identifier); if (!L) continue;
        looks.delete(t.identifier);
        if (L.held) { g.mouse.l = false; continue; }
        if (!L.moved && performance.now() - L.t < 300) {
          if (this.touchBreak) { g.mouse.l = true; g.mouse.lT = 0; setTimeout(() => { if (g.mode === 'creative') g.mouse.l = false; }, 60); if (g.mode !== 'creative') setTimeout(() => g.mouse.l = false, 60); }
          else { g.mouse.r = true; g.mouse.rFirst = true; setTimeout(() => g.mouse.r = false, 60); }
        }
      }
    };
    cv.addEventListener('touchend', endL); cv.addEventListener('touchcancel', endL);
    // hold to break
    setInterval(() => {
      for (const L of looks.values()) if (!L.moved && !L.held && performance.now() - L.t > 350) { L.held = true; g.mouse.l = true; g.mouse.lT = 0; }
    }, 50);
    const btn = (id, down, up) => {
      const b = $(id);
      b.addEventListener('touchstart', (e) => { e.preventDefault(); down(); b.classList.add('on'); }, { passive: false });
      b.addEventListener('touchend', (e) => { e.preventDefault(); if (up) up(); b.classList.remove('on'); }, { passive: false });
    };
    btn('tJump', () => {
      g.touch.jump = true;
      const now = performance.now();
      if (g.mode === 'creative' && now - (this.lastJumpTap || 0) < 300) { g.player.flying = !g.player.flying; }
      this.lastJumpTap = now;
    }, () => { g.touch.jump = false; });
    btn('tSneak', () => { g.touch.sneak = true; }, () => { g.touch.sneak = false; });
    btn('tMode', () => { this.touchBreak = !this.touchBreak; $('tMode').textContent = this.touchBreak ? '⛏' : '🧱'; });
    btn('tInv', () => { if (this.invOpen) this.closeInventory(true); else this.openInventory(); });
    btn('tPause', () => { g.paused = true; this.showPause(); });
    $('tMode').textContent = '🧱';
  },
};

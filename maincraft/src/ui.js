'use strict';
// DOM user interface: i18n, block icons, hotbar, inventory, menus.

const LANG = (navigator.language || 'en').toLowerCase().startsWith('ru') || (navigator.language || '').toLowerCase().startsWith('uk') ? 'ru' : 'en';
const STR = {
  en: {
    singleplayer: 'Singleplayer', settings: 'Settings', play: 'Play Selected World', create: 'Create New World',
    del: 'Delete', cancel: 'Cancel', back: 'Back', worldName: 'World Name', seed: 'Seed (optional)',
    mode: 'Game Mode', creative: 'Creative', survival: 'Survival', createBtn: 'Create', noWorlds: 'No worlds yet - create your first one!',
    loading: 'Generating world...', resume: 'Back to Game', saveQuit: 'Save & Quit to Title', paused: 'Game Menu',
    renderDist: 'Render Distance', fov: 'FOV', sens: 'Sensitivity', bright: 'Brightness', clouds: 'Clouds',
    leaves: 'Fancy Leaves', sway: 'Waving Plants', bobView: 'View Bobbing', on: 'ON', off: 'OFF', chunks: 'chunks', done: 'Done',
    scale: 'Render Scale', fps: 'Show FPS', lang: 'Language', inventory: 'Inventory', search: 'Search...',
    tab_b: 'Building Blocks', tab_c: 'Colored Blocks', tab_n: 'Natural Blocks', tab_f: 'Functional Blocks',
    tab_t: 'Tools & Items', tab_s: 'Search', confirmDel: 'Delete this world forever?', died: 'You Died!', respawn: 'Respawn',
    crafting: 'Crafting', chest: 'Chest', furnace: 'Furnace', clickToPlay: 'Click to play', sound: 'Sound',
    hintDesktop: 'WASD - move, Space - jump, F or double Space - fly, Shift - sneak, Ctrl - sprint, LMB - break, RMB - place/use, MMB - pick, E - inventory, F3 - debug, F5 - camera',
    biome: 'Biome', flying: 'Flying (F)', lastPlayed: 'Last played', gamemodeChanged: 'Game mode changed', mouse: 'Mouse',
  },
  ru: {
    singleplayer: 'Одиночная игра', settings: 'Настройки', play: 'Играть в выбранном мире', create: 'Создать новый мир',
    del: 'Удалить', cancel: 'Отмена', back: 'Назад', worldName: 'Название мира', seed: 'Сид (необязательно)',
    mode: 'Режим игры', creative: 'Творческий', survival: 'Выживание', createBtn: 'Создать', noWorlds: 'Миров пока нет — создай первый!',
    loading: 'Генерация мира...', resume: 'Вернуться к игре', saveQuit: 'Сохранить и выйти в меню', paused: 'Меню игры',
    renderDist: 'Дальность прорисовки', fov: 'Поле зрения', sens: 'Чувствительность', bright: 'Яркость', clouds: 'Облака',
    leaves: 'Красивая листва', sway: 'Колыхание растений', bobView: 'Покачивание камеры', on: 'ВКЛ', off: 'ВЫКЛ', chunks: 'чанков', done: 'Готово',
    scale: 'Масштаб рендера', fps: 'Показывать FPS', lang: 'Язык', inventory: 'Инвентарь', search: 'Поиск...',
    tab_b: 'Строительные блоки', tab_c: 'Цветные блоки', tab_n: 'Природные блоки', tab_f: 'Функциональные блоки',
    tab_t: 'Инструменты и предметы', tab_s: 'Поиск', confirmDel: 'Удалить этот мир навсегда?', died: 'Вы погибли!', respawn: 'Возродиться',
    crafting: 'Крафт', chest: 'Сундук', furnace: 'Печь', clickToPlay: 'Нажмите, чтобы играть', sound: 'Звук',
    hintDesktop: 'WASD — ходьба, Пробел — прыжок, F или двойной Пробел — полёт, Shift — присесть, Ctrl — бег, ЛКМ — сломать, ПКМ — поставить/использовать, СКМ — взять блок, E — инвентарь, F3 — отладка, F5 — камера',
    biome: 'Биом', flying: 'Полёт (F)', lastPlayed: 'Последняя игра', gamemodeChanged: 'Режим игры изменён', mouse: 'Мышь',
  },
};
let CUR_LANG = LANG;
function T(k) { return (STR[CUR_LANG] && STR[CUR_LANG][k]) || STR.en[k] || k; }
function itemName(id) {
  if (!id) return '';
  if (isItem(id)) { const d = itemDef(id); return d ? (CUR_LANG === 'ru' ? d[2] : d[1]) : '?'; }
  return CUR_LANG === 'ru' ? B_RU[id] : B_EN[id];
}
function maxStack(id) { if (isItem(id)) { const d = itemDef(id); return d ? d[4] : 64; } return 64; }

// ------------------------------------------------------------------ icons
const Icons = {
  cache: new Map(),
  cv: null, ctx: null, tile: null, tctx: null,
  init(assets) {
    this.assets = assets;
    this.cv = document.createElement('canvas'); this.cv.width = 64; this.cv.height = 64;
    this.ctx = this.cv.getContext('2d');
    this.tile = document.createElement('canvas'); this.tile.width = 16; this.tile.height = 16;
    this.tctx = this.tile.getContext('2d');
  },
  layerCanvas(layer, shade) {
    const A = this.assets, px = A.atlasPixels, W = A.atlasImage.width;
    const ox = (layer % A.cols) * 16, oy = Math.floor(layer / A.cols) * 16;
    const im = this.tctx.createImageData(16, 16);
    for (let y = 0; y < 16; y++) for (let x = 0; x < 16; x++) {
      const s = ((oy + y) * W + ox + x) * 4, d = (y * 16 + x) * 4;
      im.data[d] = px[s] * shade; im.data[d + 1] = px[s + 1] * shade; im.data[d + 2] = px[s + 2] * shade; im.data[d + 3] = px[s + 3];
    }
    const c = document.createElement('canvas'); c.width = 16; c.height = 16;
    c.getContext('2d').putImageData(im, 0, 0);
    return c;
  },
  get(id) {
    let u = this.cache.get(id);
    if (u) return u;
    u = this.render(id);
    this.cache.set(id, u);
    return u;
  },
  render(id) {
    const ctx = this.ctx, S = 64;
    ctx.setTransform(1, 0, 0, 1, 0, 0);
    ctx.clearRect(0, 0, S, S);
    ctx.imageSmoothingEnabled = false;
    const L = this.assets.layers;
    let flat = null;
    if (isItem(id)) flat = L[itemDef(id)[3]];
    else {
      const sh = SHAPE[id];
      if (sh === SH.CROSS || sh === SH.TALL || sh === SH.TORCH || sh === SH.LADDER || sh === SH.VINE || sh === SH.LICHEN ||
        sh === SH.LILY || sh === SH.RAIL || sh === SH.FIRE || sh === SH.DOOR || sh === SH.PANE || sh === SH.PICKLE || sh === SH.BAMBOO) {
        const t = TEXNAMES[id];
        flat = sh === SH.TALL ? L[t[0]] : sh === SH.DOOR ? L[t[0]] : FTEX[id * 6 + 2];
        if (sh === SH.PANE) flat = FTEX[id * 6];
        if (id === B.WATER) flat = null;
      }
      if (sh === SH.WATER || sh === SH.LAVA) flat = FTEX[id * 6 + 2];
    }
    if (flat !== null && flat !== undefined) {
      ctx.drawImage(this.layerCanvas(flat, 1), 4, 4, 56, 56);
      if (!isItem(id) && SHAPE[id] === SH.DOOR) {
        const t = TEXNAMES[id];
        ctx.clearRect(0, 0, S, S);
        ctx.drawImage(this.layerCanvas(L[t[0]], 1), 16, 2, 32, 30);
        ctx.drawImage(this.layerCanvas(L[t[1] || t[0]], 1), 16, 32, 32, 30);
      }
      return this.cv.toDataURL();
    }
    // isometric cube
    const sh = SHAPE[id];
    const top = FTEX[id * 6 + 2];
    let left = FTEX[id * 6 + 4], right = FTEX[id * 6];
    if (FLAGS[id] & BF_FACING) left = FRONT[id];
    const hFrac = sh === SH.SLAB ? 0.5 : sh === SH.CARPET ? 0.08 : sh === SH.PLATE ? 0.08 : sh === SH.FARMLAND ? 0.94 : sh === SH.BED ? 0.56 : 1;
    const inset = sh === SH.FENCE || sh === SH.WALL || sh === SH.GATE ? 0.2 : sh === SH.CACTUS || sh === SH.CHEST ? 0.06 : sh === SH.BUTTON ? 0.3 : sh === SH.POT ? 0.28 : 0;
    const cx = 32, s = 26 * (1 - inset * 1.3);
    const topY = 32 - s * 0.5 - s * hFrac * 0.62 + s * (1 - hFrac) * 0.1;
    const hh = s * 1.15 * hFrac;
    // top rhombus: map tile (0,0)-(16,16) to points A(cx, topY - s*0.5) ...
    const drawFace = (layer, shade, ax, ay, bx, by, cx2, cy2, srcH) => {
      const img = this.layerCanvas(layer, shade);
      // maps (0,0)->A, (16,0)->B, (0,16)->C
      ctx.setTransform((bx - ax) / 16, (by - ay) / 16, (cx2 - ax) / 16, (cy2 - ay) / 16, ax, ay);
      ctx.drawImage(img, 0, 16 - srcH, 16, srcH, 0, 0, 16, 16);
      ctx.setTransform(1, 0, 0, 1, 0, 0);
    };
    const y0 = topY;
    const P = { t: [cx, y0 - s * 0.5], l: [cx - s, y0], r: [cx + s, y0], b: [cx, y0 + s * 0.5] };
    // top face: tile x -> towards right-top edge, tile y -> towards left-bottom
    drawFace(top, 1.0, P.l[0], P.l[1], P.t[0], P.t[1], P.b[0], P.b[1], 16);
    // left face (front): from l to b, down by hh
    drawFace(left, 0.8, P.l[0], P.l[1], P.b[0], P.b[1], P.l[0], P.l[1] + hh, 16 * hFrac);
    // right face
    drawFace(right, 0.62, P.b[0], P.b[1], P.r[0], P.r[1], P.b[0], P.b[1] + hh, 16 * hFrac);
    return this.cv.toDataURL();
  },
};

// ------------------------------------------------------------------ helpers
function el(tag, attrs, ...kids) {
  const e = document.createElement(tag);
  if (attrs) for (const k in attrs) {
    if (k === 'class') e.className = attrs[k];
    else if (k === 'style') e.style.cssText = attrs[k];
    else if (k.startsWith('on')) e.addEventListener(k.slice(2), attrs[k]);
    else e.setAttribute(k, attrs[k]);
  }
  for (const c of kids) if (c != null) e.append(c.nodeType ? c : document.createTextNode(c));
  return e;
}
function $(id) { return document.getElementById(id); }
function show(id, v) { const e = typeof id === 'string' ? $(id) : id; if (e) e.classList.toggle('hidden', !v); }

function slotEl(item, opts) {
  const s = el('div', { class: 'slot' + (opts && opts.cls ? ' ' + opts.cls : '') });
  setSlot(s, item);
  return s;
}
function setSlot(s, item) {
  s.innerHTML = '';
  if (item && item.id) {
    const img = el('img', { src: Icons.get(item.id), draggable: 'false' });
    s.append(img);
    if (item.count > 1) s.append(el('span', { class: 'cnt' }, String(item.count)));
    if (item.dur !== undefined && item.maxDur) {
      const f = item.dur / item.maxDur;
      s.append(el('i', { class: 'dur', style: `width:${Math.round(f * 80)}%;background:hsl(${Math.round(f * 120)},90%,45%)` }));
    }
    s.title = itemName(item.id);
  } else s.title = '';
}

// list of everything shown in the creative inventory, grouped by tab
function creativeCatalog() {
  const tabs = { b: [], c: [], n: [], f: [], t: [] };
  const skip = new Set(['WATER', 'LAVA', 'FIRE', 'FARMLAND_MOIST', 'FURNACE_LIT', 'BEDROCK_X', 'SNOWY_GRASS']);  // snowy grass is how grass looks under snow
  for (let id = 1; id < NB; id++) {
    const k = B_KEY[id];
    if (skip.has(k)) continue;
    if (/^(WHEAT|CARROTS|POTATOES|PUMPKIN_STEM|MELON_STEM)_[123]$/.test(k)) continue;
    if (k === 'CAVE_VINES' || k === 'WEEPING_VINES' || k === 'TWISTING_VINES') { tabs.n.push(id); continue; }
    tabs[TABS[TAB[id]] || 'b'].push(id);
  }
  tabs.n.push(B.WATER, B.LAVA);
  for (let i = 0; i < ITEM_TABLE.length; i++) tabs.t.push(ITEM_BASE + i);
  return tabs;
}

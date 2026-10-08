'use strict';
// IndexedDB persistence: world list + per-column edit maps.

const DB = {
  db: null,
  open() {
    if (this.db) return Promise.resolve(this.db);
    return new Promise((res) => {
      let req;
      try { req = indexedDB.open('maincraft', 1); } catch (e) { res(null); return; }
      req.onupgradeneeded = () => {
        const db = req.result;
        if (!db.objectStoreNames.contains('worlds')) db.createObjectStore('worlds', { keyPath: 'id' });
        if (!db.objectStoreNames.contains('cols')) db.createObjectStore('cols');
      };
      req.onsuccess = () => { this.db = req.result; res(this.db); };
      req.onerror = () => res(null);
    });
  },
  async tx(store, mode, fn) {
    const db = await this.open();
    if (!db) return null;
    return new Promise((res) => {
      const t = db.transaction(store, mode);
      const s = t.objectStore(store);
      let out = null;
      const r = fn(s);
      if (r) r.onsuccess = () => { out = r.result; };
      t.oncomplete = () => res(out);
      t.onerror = () => res(null);
      t.onabort = () => res(null);
    });
  },
  listWorlds() { return this.tx('worlds', 'readonly', s => s.getAll()).then(r => (r || []).sort((a, b) => b.lastPlayed - a.lastPlayed)); },
  putWorld(w) { return this.tx('worlds', 'readwrite', s => s.put(w)); },
  async deleteWorld(id) {
    await this.tx('worlds', 'readwrite', s => s.delete(id));
    const db = await this.open();
    if (!db) return;
    await new Promise(res => {
      const t = db.transaction('cols', 'readwrite');
      const range = IDBKeyRange.bound(id + ':', id + ':￿');
      t.objectStore('cols').delete(range);
      t.oncomplete = res; t.onerror = res;
    });
  },
  // edits: Map localIndex -> packed ; stored as Int32Array pairs
  // the column keys of a world that have stored edits (the edits themselves load per column)
  async loadColKeys(id) {
    const out = new Set();
    const keys = await this.tx('cols', 'readonly', s => s.getAllKeys(IDBKeyRange.bound(id + ':', id + ':\uffff')));
    if (keys) for (const k of keys) out.add(+String(k).split(':')[1]);
    return out;
  },
  // one column's stored edits (null: none)
  async loadCol(id, key) {
    const a = await this.tx('cols', 'readonly', s => s.get(id + ':' + key));
    if (!a) return null;
    const m = new Map();
    for (let i = 0; i < a.length; i += 2) m.set(a[i], a[i + 1]);
    return m;
  },
  // resolves true once the entries are stored
  async saveCols(id, entries) {
    const db = await this.open();
    if (!db) return false;
    if (!entries.length) return true;
    return new Promise(res => {
      const t = db.transaction('cols', 'readwrite');
      const s = t.objectStore('cols');
      for (const [k, m] of entries) {
        const a = new Int32Array(m.size * 2); let i = 0;
        for (const [li, v] of m) { a[i++] = li; a[i++] = v; }
        s.put(a, id + ':' + k);
      }
      t.oncomplete = () => res(true); t.onerror = () => res(false); t.onabort = () => res(false);
    });
  },
};

// World file: a world's record and all its stored column edits in one gzip-compressed JSON file,
// to keep a world outside the browser and to bring it into any build or browser. Typed arrays are
// written as base64 ({"$ta": type, "b": data}).
const WorldFile = {
  b64(u8) { let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000)); return btoa(s); },
  unb64(s) { const r = atob(s), u = new Uint8Array(r.length); for (let i = 0; i < r.length; i++) u[i] = r.charCodeAt(i); return u; },
  TYPES: { Int8Array, Uint8Array, Int16Array, Uint16Array, Int32Array, Uint32Array, Float32Array, Float64Array },
  replacer(k, v) {
    if (ArrayBuffer.isView(v) && !(v instanceof DataView)) return { $ta: v.constructor.name, b: WorldFile.b64(new Uint8Array(v.buffer, v.byteOffset, v.byteLength)) };
    return v;
  },
  reviver(k, v) {
    if (v && typeof v === 'object' && typeof v.$ta === 'string' && typeof v.b === 'string') {
      const T = WorldFile.TYPES[v.$ta], u = WorldFile.unb64(v.b);
      return T ? new T(u.buffer, 0, u.byteLength / T.BYTES_PER_ELEMENT) : u;
    }
    return v;
  },
  async gzip(blob, on) {
    if (typeof CompressionStream === 'undefined') return blob;      // plain JSON where the browser has no gzip
    return new Response(blob.stream().pipeThrough(on ? new CompressionStream('gzip') : new DecompressionStream('gzip'))).blob();
  },
  // the world (by id) written to a file: a save dialog with the world's name where the browser has
  // one (showSaveFilePicker; asked first, while the click still counts), else a download.
  // before: run after the dialog (saving the open world). Returns false when there is no such
  // world, 'cancel' when the dialog was closed.
  async export(id, name, before) {
    let handle = null;
    if (typeof window.showSaveFilePicker === 'function') {
      try {
        handle = await window.showSaveFilePicker({ suggestedName: WorldFile.fileName(name), types: [{ description: 'Maincraft world', accept: { 'application/octet-stream': ['.mcworld'] } }] });
      } catch (e) {
        if (e && e.name === 'AbortError') return 'cancel';
        handle = null;
      }
    }
    if (before) await before();
    const meta = (await DB.listWorlds()).find(w => w.id === id);
    if (!meta) return false;
    // keys and values read in one transaction (both come in key order)
    const db = await DB.open(), range = IDBKeyRange.bound(id + ':', id + ':\uffff');
    const [keys, vals] = await new Promise((res) => {
      const t = db.transaction('cols', 'readonly'), s = t.objectStore('cols'), k = s.getAllKeys(range), v = s.getAll(range);
      t.oncomplete = () => res([k.result || [], v.result || []]); t.onerror = t.onabort = () => res([[], []]);
    });
    const cols = keys.map((k, i) => [String(k).slice(id.length + 1), vals[i]]);
    const json = JSON.stringify({ format: 'maincraft-world', version: 1, exported: Date.now(), meta, cols }, WorldFile.replacer);
    const blob = await WorldFile.gzip(new Blob([json], { type: 'application/json' }), true);
    if (handle) {
      const out = await handle.createWritable();
      await out.write(blob); await out.close();
      return true;
    }
    const a = document.createElement('a'), url = URL.createObjectURL(blob);
    a.href = url; a.download = WorldFile.fileName(meta.name);
    document.body.appendChild(a); a.click(); a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 60000);
    return true;
  },
  fileName(name) { return String(name || 'world').replace(/[\\/:*?"<>|]+/g, '_') + '.mcworld'; },
  // a world file read back as a new world (never over an existing one); returns its record
  async import(file) {
    let blob = file;
    const head = new Uint8Array(await file.slice(0, 2).arrayBuffer());
    if (head[0] === 0x1f && head[1] === 0x8b) {
      if (typeof DecompressionStream === 'undefined') throw new Error('gzip');
      blob = await WorldFile.gzip(file, false);
    }
    const o = JSON.parse(await blob.text(), WorldFile.reviver);
    if (!o || o.format !== 'maincraft-world' || !o.meta || typeof o.meta.seed !== 'number' || !Array.isArray(o.cols)) throw new Error('format');
    const id = 'w' + Date.now().toString(36) + Math.floor(Math.random() * 1e6).toString(36);
    const meta = Object.assign({}, o.meta, { id, lastPlayed: Date.now() });
    const db = await DB.open();
    if (!db) throw new Error('db');
    await new Promise((res, rej) => {
      const t = db.transaction(['cols', 'worlds'], 'readwrite');
      const cs = t.objectStore('cols');
      for (const [k, v] of o.cols) cs.put(v instanceof Int32Array ? v : new Int32Array(v), id + ':' + k);
      t.objectStore('worlds').put(meta);
      t.oncomplete = res; t.onerror = () => rej(t.error); t.onabort = () => rej(t.error);
    });
    return meta;
  },
};

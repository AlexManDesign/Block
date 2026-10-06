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

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
  async loadCols(id) {
    const db = await this.open();
    const out = new Map();
    if (!db) return out;
    return new Promise(res => {
      const t = db.transaction('cols', 'readonly');
      const req = t.objectStore('cols').openCursor(IDBKeyRange.bound(id + ':', id + ':￿'));
      req.onsuccess = () => {
        const c = req.result;
        if (!c) return;
        const key = +String(c.key).split(':')[1];
        const a = c.value; const m = new Map();
        for (let i = 0; i < a.length; i += 2) m.set(a[i], a[i + 1]);
        out.set(key, m);
        c.continue();
      };
      t.oncomplete = () => res(out); t.onerror = () => res(out);
    });
  },
  async saveCols(id, entries) {
    const db = await this.open();
    if (!db || !entries.length) return;
    await new Promise(res => {
      const t = db.transaction('cols', 'readwrite');
      const s = t.objectStore('cols');
      for (const [k, m] of entries) {
        const a = new Int32Array(m.size * 2); let i = 0;
        for (const [li, v] of m) { a[i++] = li; a[i++] = v; }
        s.put(a, id + ':' + k);
      }
      t.oncomplete = res; t.onerror = res;
    });
  },
};

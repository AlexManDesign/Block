// Evaluated inside the running reference game: per-block gameplay data computed by main.js itself.
(() => {
  const bc = window.__bc, o = bc.o, out = [];
  const names = Object.entries(o).sort((a, b) => a[1] - b[1]);
  for (const [key, id] of names) {
    let mine = null; try { mine = bc.Z6(id); } catch (e) { mine = { err: String(e) }; }
    out.push({ id, key, light: bc._u[id] || 0, mine,
      tab: (bc.Me.find(m => m.key === key) || bc.sr.find(m => m.key === key) || {}).tab || null,
      rgb: (bc.Me.find(m => m.key === key) || bc.sr.find(m => m.key === key) || {}).rgb || null });
  }
  const items = bc.Gn.map(g => ({ ...g, faces: bc.M[g.id] || null }));
  return JSON.stringify({ blocks: out, items, itemIdBase: bc.Vl });
})()

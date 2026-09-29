// Evaluated inside the headless reference game (tools/refgame/run.mjs): paints main.js Hn(id) for
// every creative-tab entry and returns {id: {s, px: base64 RGBA top-down}} plus the id list.
(async () => {
  const b = window.__bc, ids = [];
  const seen = new Set();
  for (const t of b.uo) for (const id of t.blocks) if (!seen.has(id)) { seen.add(id); ids.push(id); }
  const out = {};
  function b64(u8) { let s = ''; for (let i = 0; i < u8.length; i += 0x8000) s += String.fromCharCode.apply(null, u8.subarray(i, i + 0x8000)); return btoa(s); }
  for (const id of ids) {
    const url = b.Hn(id);
    const img = new Image();
    await new Promise((res, rej) => { img.onload = res; img.onerror = rej; img.src = url; });
    const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
    const g = c.getContext('2d'); g.drawImage(img, 0, 0);
    const d = g.getImageData(0, 0, img.width, img.height).data;
    out[id] = { s: img.width, px: b64(new Uint8Array(d.buffer)) };
  }
  return JSON.stringify({ ids, icons: out });
})()

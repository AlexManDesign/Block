// Benchmarks reference meshWorker.js on genWorker.js chunks (light baked in worker like main's iC(...,light=true)).
import { pathToFileURL } from 'node:url'; import path from 'node:path'; import fs from 'node:fs';
const seed = +(process.argv[2] || 56), ox = +(process.argv[3] || -67), oz = +(process.argv[4] || -178);
const cache = path.resolve(import.meta.dirname, '../.cache');
function load(name) { // load each worker in its own module instance with its own `self`
  const src = fs.readFileSync(path.resolve(import.meta.dirname, `../../reference/source/${name}.js`), 'utf8');
  const tmp = path.join(cache, `${name}.bench.mjs`);
  fs.writeFileSync(tmp, `const self={postMessage:(m)=>{self.last=m;}};\n` + src + `\nexport default self;`);
  return import(pathToFileURL(tmp).href).then(m => m.default);
}
const gen = await load('genWorker'), mesh = await load('meshWorker');
const ids = JSON.parse(fs.readFileSync(path.resolve(import.meta.dirname, 'source_block_ids.json'), 'utf8'));
const faces = {}; for (const k in ids) faces[ids[k]] = { top: 1, side: 2, bottom: 3 };
mesh.onmessage({ data: { t: 'init', faces, w: 256, h: 1024 } });
gen.onmessage({ data: { t: 'seed', seed, gen: 'deepslate' } });
const chunks = new Map(); let t0 = performance.now(); let n = 0;
for (let x = -2; x <= 2; x++) for (let z = -2; z <= 2; z++) {
  gen.onmessage({ data: { t: 'gen', id: 1, cx: ox + x, cz: oz + z, light: true } });
  chunks.set(`${ox + x},${oz + z}`, { cx: ox + x, cz: oz + z, sections: gen.last.sections }); n++;
}
console.log(`gen+light ${((performance.now() - t0) / n).toFixed(2)} ms/chunk`);
function nb(cx, cz) { const r = []; for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) { const c = chunks.get(`${cx + a},${cz + b}`); if (c) r.push(c); } return r; }
mesh.onmessage({ data: { t: 'mesh', id: 1, cx: ox, cz: oz, chunks: nb(ox, oz) } }); // warmup
t0 = performance.now(); n = 0; let verts = 0, tris = 0;
for (let rep = 0; rep < 3; rep++) for (let x = -1; x <= 1; x++) for (let z = -1; z <= 1; z++) {
  mesh.onmessage({ data: { t: 'mesh', id: 1, cx: ox + x, cz: oz + z, chunks: nb(ox + x, oz + z) } });
  const m = mesh.last; verts += (m.ov.length + m.wv.length + m.tv.length) / 8; tris += (m.oi.length + m.wi.length + m.ti.length) / 3; n++;
}
console.log(`mesh ${((performance.now() - t0) / n).toFixed(2)} ms/chunk  ${(verts / n) | 0} verts/chunk ${(tris / n) | 0} tris/chunk`);

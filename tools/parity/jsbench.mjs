// Times the reference workers the same way `Parity.dll pipeline` times the port:
// generate a 7x7 area (light baked), then mesh the central 5x5 with 3x3 neighbourhoods.
// usage: node jsbench.mjs <seed> <reps>
import { pathToFileURL } from 'node:url'; import path from 'node:path'; import fs from 'node:fs';
const [seed = '56', reps = '3'] = process.argv.slice(2);
const cache = path.resolve(import.meta.dirname, '../.cache'); fs.mkdirSync(cache, { recursive: true });
function load(name) {
  const src = fs.readFileSync(path.resolve(import.meta.dirname, `../../reference/source/${name}.js`), 'utf8');
  const tmp = path.join(cache, `${name}.bench.mjs`);
  fs.writeFileSync(tmp, `const self={postMessage:(m)=>{self.last=m;}};\n` + src + `\nexport default self;`);
  return import(pathToFileURL(tmp).href).then(m => m.default);
}
const gen = await load('genWorker'), mesh = await load('meshWorker');
const F = JSON.parse(fs.readFileSync(path.resolve(import.meta.dirname, '../../reference/data/faces.json'), 'utf8'));
mesh.onmessage({ data: { t: 'init', faces: F.faces, w: F.w, h: F.h } });
let bestGen = 1e9, bestMesh = 1e9, verts = 0;
for (let r = 0; r < +reps; r++) {
  gen.onmessage({ data: { t: 'seed', seed: +seed + r * 1000, gen: 'deepslate' } });
  const chunks = new Map();
  let t0 = performance.now();
  for (let cx = -3; cx <= 3; cx++) for (let cz = -3; cz <= 3; cz++) {
    gen.onmessage({ data: { t: 'gen', id: 1, cx, cz, light: true } });
    chunks.set(`${cx},${cz}`, { cx, cz, sections: gen.last.sections });
  }
  bestGen = Math.min(bestGen, (performance.now() - t0) / 49);
  t0 = performance.now(); verts = 0;
  for (let cx = -2; cx <= 2; cx++) for (let cz = -2; cz <= 2; cz++) {
    const nb = []; for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) nb.push(chunks.get(`${cx + a},${cz + b}`));
    mesh.onmessage({ data: { t: 'mesh', id: 1, cx, cz, chunks: nb } });
    const m = mesh.last; verts += (m.ov.length + m.wv.length + m.tv.length) / 8;
  }
  bestMesh = Math.min(bestMesh, (performance.now() - t0) / 25);
}
console.log(`reference gen+light ${bestGen.toFixed(2)} ms/chunk (49), mesh ${bestMesh.toFixed(2)} ms/chunk (25, ~${Math.round(verts / 25)} verts/chunk)`);

// Extracts per-block render semantics from reference meshWorker.js (+ main.js face table) into
// reference/data/blocks.json. Every value comes from evaluating the reference code itself.
import * as acorn from 'acorn'; import fs from 'node:fs'; import path from 'node:path'; import { pathToFileURL } from 'node:url';
const here = import.meta.dirname, root = path.resolve(here, '../..');
const mainTables = JSON.parse(fs.readFileSync(process.argv[2], 'utf8'));
const src = fs.readFileSync(path.join(root, 'reference/source/meshWorker.js'), 'utf8');
const ast = acorn.parse(src, { ecmaVersion: 'latest', sourceType: 'module' });
const names = [];
for (const n of ast.body) {
  if (n.type === 'VariableDeclaration') for (const d of n.declarations) if (d.id.type === 'Identifier') names.push(d.id.name);
  if (n.type === 'FunctionDeclaration' && n.id) names.push(n.id.name);
}
const tmp = path.join(root, 'tools/.cache/meshWorker.extract.mjs');
fs.writeFileSync(tmp, 'const self={postMessage(){}};\n' + src + `\nexport default {${names.map(n => `get ${n}(){return ${n}}`).join(',')}};`);
const W = (await import(pathToFileURL(tmp).href)).default;
W.RC(mainTables.M, mainTables.G0, mainTables.V0); // same as meshWorker 'init' message
const A = W.A, ids = Object.entries(A).sort((a, b) => a[1] - b[1]);
const flag = (t, id) => (t && t[id]) ? t[id] : 0;
const blocks = ids.map(([key, id]) => {
  const faces = W.q4(id) || null;
  return {
    id, key,
    en: mainTables.X[id] ?? null, ru: mainTables.W[id] ?? null,
    shape: W.h[id] ?? null, texBase: W.z[id] ?? null,
    faces,
    fullOpaque: !!W.FA(id), solidRender: !!W.TC(id), transparentLayer: !!W.y1(id),
    cross: !!W.cA(id), aquatic: !!W.hA(id), classicLeaf: !!W.T4(id), water: !!W.sA(id), lava: !!W.xA(id),
    glass: !!flag(W.dE, id), glassy: !!flag(W.m1, id), needsWater: !!flag(W.U1, id), hang: !!flag(W.iC, id), climb: !!flag(W.JC, id),
    xrayClass: flag(W.V1, id), axis: !!flag(W.b1, id), emissiveBoost: !!flag(W.LE, id),
    light: flag(mainTables.Tu, id), lightBlock: !!flag(mainTables.D0, id), falling: !!flag(mainTables.FC, id),
    baseBlock: mainTables.Te[id] ?? null,
  };
});
const out = { source: 'reference/source/meshWorker.js + main.js runtime tables', atlas: { w: mainTables.G0, h: mainTables.V0, tile: 16, cols: 16 }, blocks };
fs.writeFileSync(path.join(root, 'reference/data/blocks.json'), JSON.stringify(out, null, 0).replace(/\},\{"id"/g, '},\n{"id"'));
console.log('blocks', blocks.length, 'with faces', blocks.filter(b => b.faces).length);
const shapes = {}; for (const b of blocks) shapes[b.shape || 'cube'] = (shapes[b.shape || 'cube'] || 0) + 1; console.log(shapes);

import { pathToFileURL } from 'node:url'; import path from 'node:path';
let last = null;
globalThis.self = { postMessage: (m) => { last = m; } };
await import(pathToFileURL(path.resolve(import.meta.dirname, '../../reference/source/genWorker.js')).href);
const seed = +(process.argv[2] || 56), light = process.argv[3] === '1';
self.onmessage({ data: { t: 'seed', seed, gen: 'deepslate' } });
let t0 = performance.now();
self.onmessage({ data: { t: 'gen', id: 1, cx: 0, cz: 0, light } });
console.log(`first chunk ${(performance.now() - t0).toFixed(1)} ms`);
t0 = performance.now(); let n = 0;
for (let x = -4; x < 4; x++) for (let z = -4; z < 4; z++) { self.onmessage({ data: { t: 'gen', id: 2, cx: x, cz: z, light } }); n++; }
console.log(`${n} chunks ${((performance.now() - t0) / n).toFixed(2)} ms/chunk (light=${light})`);

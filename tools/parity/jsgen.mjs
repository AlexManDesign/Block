// Runs reference/source/genWorker.js in Node and dumps generated chunk sections.
// usage: node jsgen.mjs <seed> <gen> <cx> <cz> [light=0|1] > out.bin
import { pathToFileURL } from 'node:url';
import path from 'node:path';
const [seed='56', gen='classic', cx='0', cz='0', light='0'] = process.argv.slice(2);
let last = null;
globalThis.self = { postMessage: (m) => { last = m; } };
await import(pathToFileURL(path.resolve(import.meta.dirname, '../../reference/source/genWorker.js')).href);
self.onmessage({ data: { t: 'seed', seed: +seed, gen } });
const t0 = performance.now();
self.onmessage({ data: { t: 'gen', id: 1, cx: +cx, cz: +cz, light: light === '1' } });
const dt = performance.now() - t0;
const secs = last.sections;
process.stderr.write(`sections=${secs.length} ms=${dt.toFixed(1)}\n`);
for (let i = 0; i < secs.length; i++) {
  const s = secs[i];
  process.stderr.write(`${i}:${s ? Object.keys(s).join('/') + ' ' + s.blocks.length : 'null'} `);
}
process.stderr.write('\n');

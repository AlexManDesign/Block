// Dumps reference genWorker chunks: for each chunk writes blocks(u16), meta(u8), light(u8) in [x][z][y] order.
// usage: node jsdump.mjs <seed> <light 0|1> <outdir> cx,cz [cx,cz ...]
import { pathToFileURL } from 'node:url'; import path from 'node:path'; import fs from 'node:fs';
const [seed, light, outdir, ...coords] = process.argv.slice(2);
let last = null;
globalThis.self = { postMessage: (m) => { last = m; } };
await import(pathToFileURL(path.resolve(import.meta.dirname, '../../reference/source/genWorker.js')).href);
self.onmessage({ data: { t: 'seed', seed: +seed, gen: 'deepslate' } });
fs.mkdirSync(outdir, { recursive: true });
const H = 384;
for (const c of coords) {
  const [cx, cz] = c.split(',').map(Number);
  self.onmessage({ data: { t: 'gen', id: 1, cx, cz, light: light === '1' } });
  const blocks = new Uint16Array(16 * 16 * H), meta = new Uint8Array(16 * 16 * H), lt = new Uint8Array(16 * 16 * H);
  for (let s = 0; s < last.sections.length; s++) {
    const sec = last.sections[s];
    for (let x = 0; x < 16; x++) for (let z = 0; z < 16; z++) for (let y = 0; y < 16; y++) {
      const o = (x * 16 + z) * H + s * 16 + y;
      if (!sec) { lt[o] = 240; continue; }
      const i = (((x << 4) + z) << 4) + y;
      blocks[o] = sec.blocks[i]; meta[o] = sec.meta[i]; lt[o] = sec.light[i];
    }
  }
  fs.writeFileSync(path.join(outdir, `js_${cx}_${cz}.bin`), Buffer.concat([Buffer.from(blocks.buffer), Buffer.from(meta.buffer), Buffer.from(lt.buffer)]));
}

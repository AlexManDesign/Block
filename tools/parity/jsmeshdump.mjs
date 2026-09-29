// Generates a 3x3 neighbourhood with genWorker (light baked), meshes the centre with meshWorker and dumps
// both the block data (js_cx_cz.bin, same layout as jsdump.mjs) and the mesh (jsmesh_cx_cz.bin).
// usage: node jsmeshdump.mjs <seed> <outdir> cx,cz ...
import { pathToFileURL } from 'node:url'; import path from 'node:path'; import fs from 'node:fs';
const [seed, outdir, ...coords] = process.argv.slice(2);
const cache = path.resolve(import.meta.dirname, '../.cache');
function load(name) {
  const src = fs.readFileSync(path.resolve(import.meta.dirname, `../../reference/source/${name}.js`), 'utf8');
  const tmp = path.join(cache, `${name}.bench.mjs`);
  fs.writeFileSync(tmp, `const self={postMessage:(m)=>{self.last=m;}};\n` + src + `\nexport default self;`);
  return import(pathToFileURL(tmp).href).then(m => m.default);
}
const gen = await load('genWorker'), mesh = await load('meshWorker');
const ids = JSON.parse(fs.readFileSync(path.resolve(import.meta.dirname, 'source_block_ids.json'), 'utf8'));
// Face tile index = block id so UV parity can be checked through the port's own atlas mapping later.
const faces = {}; for (const k in ids) faces[ids[k]] = { top: 1, side: 2, bottom: 3 };
mesh.onmessage({ data: { t: 'init', faces, w: 256, h: 1024 } });
gen.onmessage({ data: { t: 'seed', seed: +seed, gen: 'deepslate' } });
fs.mkdirSync(outdir, { recursive: true });
const H = 384, chunks = new Map();
function get(cx, cz) {
  const k = `${cx},${cz}`; if (chunks.has(k)) return chunks.get(k);
  gen.onmessage({ data: { t: 'gen', id: 1, cx, cz, light: true } });
  const c = { cx, cz, sections: gen.last.sections }; chunks.set(k, c);
  const blocks = new Uint16Array(16 * 16 * H), meta = new Uint8Array(16 * 16 * H), lt = new Uint8Array(16 * 16 * H);
  for (let s = 0; s < c.sections.length; s++) {
    const sec = c.sections[s];
    for (let x = 0; x < 16; x++) for (let z = 0; z < 16; z++) for (let y = 0; y < 16; y++) {
      const o = (x * 16 + z) * H + s * 16 + y;
      if (!sec) { lt[o] = 240; continue; }
      const i = (((x << 4) + z) << 4) + y;
      blocks[o] = sec.blocks[i]; meta[o] = sec.meta[i]; lt[o] = sec.light[i];
    }
  }
  fs.writeFileSync(path.join(outdir, `js_${cx}_${cz}.bin`), Buffer.concat([Buffer.from(blocks.buffer), Buffer.from(meta.buffer), Buffer.from(lt.buffer)]));
  return c;
}
for (const co of coords) {
  const [cx, cz] = co.split(',').map(Number);
  const nb = []; for (let a = -1; a <= 1; a++) for (let b = -1; b <= 1; b++) nb.push(get(cx + a, cz + b));
  mesh.onmessage({ data: { t: 'mesh', id: 1, cx, cz, chunks: nb } });
  const m = mesh.last;
  const hdr = new Uint32Array([m.ov.length, m.oi.length, m.wv.length, m.wi.length, m.tv.length, m.ti.length]);
  fs.writeFileSync(path.join(outdir, `jsmesh_${cx}_${cz}.bin`), Buffer.concat([hdr, m.ov, m.oi, m.wv, m.wi, m.tv, m.ti].map(a => Buffer.from(a.buffer, a.byteOffset, a.byteLength))));
}

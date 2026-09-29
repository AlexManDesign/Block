// Synthetic mesher parity world: every reference block id x a set of metadata values, isolated on a
// 2-block grid inside chunk (0,0), with a deterministic pseudo-random light field. Writes js_0_0.bin
// (jsdump layout) and jsmesh_0_0.bin (meshWorker output with the real main.js face table).
// usage: node jssynth.mjs <outdir> [spacing=2] [metaSet=0,1,2,3,4,5,6,7,8,12,16,20]
import { pathToFileURL } from 'node:url'; import path from 'node:path'; import fs from 'node:fs';
const [outdir, spacingArg = '2', denseSeedArg] = process.argv.slice(2);
const spacing = +spacingArg;
// dense mode: every cell of a 16x16x48 slab gets a random block (weighted towards shaped/connecting ones).
// Valid metadata per shape (the reference mesher, like the game, never sees other values).
const META = {
  cube: [0], axis: [0, 1, 2], front: [0, 1, 2, 3, 4, 5, 6, 7, 12, 13, 14, 15],
  slab: [0, 1], stairs: [0, 1, 2, 3, 4, 5, 6, 7], fence: [0], gate: [0, 1, 8, 9], wall: [0], pane: [0], carpet: [0],
  plate: [0], button: [0, 1, 2, 3, 4, 5], trapdoor: [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15],
  door: [0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 16, 20, 24, 28], ladder: [0, 1, 2, 3], torch: [0, 1, 2, 3, 4], rail: [0, 1],
  vine: [1, 2, 4, 8, 16, 32, 5, 63], lichen: [1, 2, 4, 8, 16, 32, 5, 63], pot: [0, 1, 2, 5, 10, 15, 22, 27],
  seapickle: [0, 1, 2, 3, 4, 7], lilypad: [0], bamboo: [0], tallplant: [0, 4], shipwheel: [0, 1, 2, 3], bed: [0, 1, 2, 3, 4, 5, 6, 7],
};
const BLOCKS = JSON.parse(fs.readFileSync(path.resolve(import.meta.dirname, '../../reference/data/blocks.json'), 'utf8')).blocks;
function metasFor(b) {
  if (/^BED/.test(b.key)) return META.bed;
  if (b.shape) return META[b.shape];
  if (b.axis) return META.axis;
  if (b.faces && b.faces.front !== undefined) return META.front;
  if (b.key === 'CAVE_VINES') return [0, 1];
  return META.cube;
}
const root = path.resolve(import.meta.dirname, '../..');
const src = fs.readFileSync(path.join(root, 'reference/source/meshWorker.js'), 'utf8');
const tmp = path.join(root, 'tools/.cache/meshWorker.synth.mjs');
fs.writeFileSync(tmp, 'const self={postMessage:(m)=>{self.last=m;}};\n' + src + '\nexport default self;');
const mesh = (await import(pathToFileURL(tmp).href)).default;
const F = JSON.parse(fs.readFileSync(path.join(root, 'reference/data/faces.json'), 'utf8'));
mesh.onmessage({ data: { t: 'init', faces: F.faces, w: F.w, h: F.h } });
const ids = JSON.parse(fs.readFileSync(path.join(root, 'tools/parity/source_block_ids.json'), 'utf8'));
const maxId = Math.max(...Object.values(ids));
const H = 384, N = 16 * 16 * H;
const blocks = new Uint16Array(N), meta = new Uint8Array(N), light = new Uint8Array(N);
let h = 2166136261;
for (let i = 0; i < N; i++) { h = Math.imul(h ^ i, 16777619) >>> 0; light[i] = h & 0xff; }
const per = 16 / spacing;
let cell = 0;
if (denseSeedArg !== undefined) {
  let r = (+denseSeedArg) >>> 0 || 1;
  const rnd = () => { r ^= r << 13; r >>>= 0; r ^= r >> 17; r ^= r << 5; r >>>= 0; return r / 4294967296; };
  const shaped = BLOCKS.filter(b => b.id && (b.shape || b.water || b.lava || b.key === 'FIRE' || b.key === 'CHEST' || b.cross)).map(b => b.id);
  for (let x = 0; x < 16; x++) for (let z = 0; z < 16; z++) for (let y = 70; y < 118; y++) {
    const q = rnd(); if (q < 0.35) continue;
    const id = q < 0.7 ? shaped[Math.floor(rnd() * shaped.length)] : 1 + Math.floor(rnd() * maxId);
    const ms = metasFor(BLOCKS[id]); const o = (x * 16 + z) * H + y;
    blocks[o] = id; meta[o] = ms[Math.floor(rnd() * ms.length)]; cell++;
  }
} else
for (let id = 1; id <= maxId; id++) for (const m of metasFor(BLOCKS[id])) {
  const x = (cell % per) * spacing, z = (Math.floor(cell / per) % per) * spacing, y = 2 + Math.floor(cell / (per * per)) * spacing;
  if (y >= H - 2) throw new Error('world too small');
  const o = (x * 16 + z) * H + y; blocks[o] = id; meta[o] = m; cell++;
}
const sections = [];
for (let s = 0; s < 24; s++) {
  const sec = { blocks: new Uint16Array(4096), meta: new Uint8Array(4096), light: new Uint8Array(4096) };
  for (let x = 0; x < 16; x++) for (let z = 0; z < 16; z++) for (let y = 0; y < 16; y++) {
    const o = (x * 16 + z) * H + s * 16 + y, i = (((x << 4) + z) << 4) + y;
    sec.blocks[i] = blocks[o]; sec.meta[i] = meta[o]; sec.light[i] = light[o];
  }
  sections.push(sec);
}
fs.mkdirSync(outdir, { recursive: true });
fs.writeFileSync(path.join(outdir, 'js_0_0.bin'), Buffer.concat([Buffer.from(blocks.buffer), Buffer.from(meta.buffer), Buffer.from(light.buffer)]));
mesh.onmessage({ data: { t: 'mesh', id: 1, cx: 0, cz: 0, chunks: [{ cx: 0, cz: 0, sections }] } });
const m = mesh.last;
const hdr = new Uint32Array([m.ov.length, m.oi.length, m.wv.length, m.wi.length, m.tv.length, m.ti.length]);
fs.writeFileSync(path.join(outdir, 'jsmesh_0_0.bin'), Buffer.concat([hdr, m.ov, m.oi, m.wv, m.wi, m.tv, m.ti].map(a => Buffer.from(a.buffer, a.byteOffset, a.byteLength))));
console.log(`cells ${cell}, quads o/w/t ${m.oi.length / 6}/${m.wi.length / 6}/${m.ti.length / 6}`);

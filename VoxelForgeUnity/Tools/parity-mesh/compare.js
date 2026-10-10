// Compares cfg_js.json/cfg_cs.json (semantically) and js/*.bin vs cs/*.bin (bit-exactly); prints the first difference per job.
// Usage: node compare.js <outDir> [--all]   (exit code 1 on any mismatch)
"use strict";
const fs = require("fs");
const dir = process.argv[2], showAll = process.argv.includes("--all");
let bad = 0;
// ---- config ----
const cj = JSON.parse(fs.readFileSync(dir + "/cfg_js.json", "utf8")), cc = JSON.parse(fs.readFileSync(dir + "/cfg_cs.json", "utf8"));
const TILE_F = ["side", "top", "bottom", "front", "frontL", "frontR", "topL", "topR", "corner", "partTop", "partBottom"];
const BOOL_F = ["solid", "transparent", "cutout", "plant", "waterPlant", "needsWater", "axislog"];
function cmpDef(tag, j, c) {
  const out = [];
  if (!j || !c) { if (!!j !== !!c) out.push(tag + ": js " + (j ? "def" : "null") + " cs " + (c ? "def" : "null")); return out; }
  for (const f of TILE_F) { const a = j[f] == null ? null : j[f], b = c[f] == null ? null : c[f]; if (a !== b) out.push(tag + "." + f + ": js " + a + " cs " + b); }
  for (const f of BOOL_F) if (!!j[f] !== !!c[f]) out.push(tag + "." + f + ": js " + !!j[f] + " cs " + !!c[f]);
  if ((j.alpha ?? 1) !== c.alpha) out.push(tag + ".alpha: js " + (j.alpha ?? 1) + " cs " + c.alpha);
  if ((j.special || null) !== c.special) out.push(tag + ".special: js " + j.special + " cs " + c.special);
  if ((j.shape || null) !== c.shape) out.push(tag + ".shape: js " + j.shape + " cs " + c.shape);
  if ((j.light || 0) !== c.light) out.push(tag + ".light: js " + j.light + " cs " + c.light);
  return out;
}
const cfgDiff = [];
const nb = Math.max(cj.blocks.length, cc.blocks.length);
for (let i = 0; i < nb; i++) cfgDiff.push(...cmpDef("blocks[" + i + "]", cj.blocks[i], i < cc.blocks.length ? cc.blocks[i] : null));
for (const k of new Set([...Object.keys(cj.vblocks), ...Object.keys(cc.vblocks)])) cfgDiff.push(...cmpDef("vblocks." + k, cj.vblocks[k], cc.vblocks[k]));
if (cfgDiff.length) { bad++; console.log("CONFIG MISMATCH (" + cfgDiff.length + "):\n  " + cfgDiff.slice(0, 40).join("\n  ")); } else console.log("config: OK (" + cj.blocks.length + " blocks, " + Object.keys(cj.vblocks).length + " virtual)");
// ---- outputs ----
function parse(buf) {
  const u = new Uint32Array(buf.buffer, buf.byteOffset, buf.length >> 2); let p = 0; const r = () => u[p++];
  const o = { magic: r(), full: r(), sis: [] }; const ns = r(); for (let k = 0; k < ns; k++) o.sis.push(r());
  const bucket = () => { const nv = r(), ni = r(), v = u.slice(p, p + nv * 8); p += nv * 8; const i = u.slice(p, p + ni); p += ni; return { v, i }; };
  o.agg = r();
  if (o.agg) { o.b = [bucket(), bucket(), bucket(), bucket()]; o.lens = u.slice(p, p + 192); p += 192; }
  else { o.parts = []; const np = r(); for (let k = 0; k < np; k++) o.parts.push({ si: r(), b: [bucket(), bucket(), bucket(), bucket()] }); }
  return o;
}
const fb = new Float32Array(1), ub = new Uint32Array(fb.buffer);
const fl = (b) => { ub[0] = b; return fb[0]; };
function h2f(h) { const s = h >> 15 ? -1 : 1, e = (h >> 10) & 31, m = h & 1023; return s * (e === 0 ? m / 16777216 : e === 31 ? (m ? NaN : Infinity) : (1 + m / 1024) * Math.pow(2, e - 15)); }
const vstr = (v, k) => { const o = k * 8; return `pos(${fl(v[o])}, ${fl(v[o + 1])}, ${fl(v[o + 2])}) uv(${h2f(v[o + 3])}, ${h2f(v[o + 4])}) tile ${v[o + 5]} sa ${fl(v[o + 6])} light ${v[o + 7]}`; };
const BN = ["opaque", "cutout", "water", "transparent"];
function cmpBucket(tag, a, b) {
  if (a.v.length !== b.v.length || a.i.length !== b.i.length) {
    let msg = `${tag}: counts js v${a.v.length / 8} i${a.i.length} cs v${b.v.length / 8} i${b.i.length}`;
    const n = Math.min(a.v.length, b.v.length) / 8; for (let k = 0; k < n; k++) if (a.v.slice(k * 8, k * 8 + 8).some((x, q) => x !== b.v[k * 8 + q])) { msg += `\n    first vertex diff #${k}\n      js ${vstr(a.v, k)}\n      cs ${vstr(b.v, k)}`; break; }
    return msg;
  }
  for (let k = 0; k < a.v.length / 8; k++) for (let q = 0; q < 8; q++) if (a.v[k * 8 + q] !== b.v[k * 8 + q]) {
    let nd = 0; for (let t = 0; t < a.v.length; t++) if (a.v[t] !== b.v[t]) nd++;
    return `${tag}: vertex #${k} field ${q} (${nd} differing words)\n      js ${vstr(a.v, k)}\n      cs ${vstr(b.v, k)}`;
  }
  for (let k = 0; k < a.i.length; k++) if (a.i[k] !== b.i[k]) return `${tag}: index #${k} js ${a.i[k]} cs ${b.i[k]}`;
  return null;
}
function cmp(a, b) {
  const d = [];
  if (a.full !== b.full || a.agg !== b.agg || a.sis.join() !== b.sis.join()) return [`header: js full ${a.full} agg ${a.agg} sis ${a.sis} | cs full ${b.full} agg ${b.agg} sis ${b.sis}`];
  if (a.agg) {
    for (let k = 0; k < 4; k++) { const m = cmpBucket("agg." + BN[k], a.b[k], b.b[k]); if (m) d.push(m); }
    for (let k = 0; k < 192; k++) if (a.lens[k] !== b.lens[k]) { d.push(`secLens[${k >> 3}][${k & 7}] js ${a.lens[k]} cs ${b.lens[k]}`); break; }
  } else {
    if (a.parts.length !== b.parts.length) return [`parts: js ${a.parts.length} cs ${b.parts.length}`];
    a.parts.forEach((p, n) => { const q = b.parts[n]; if (p.si !== q.si) d.push(`part ${n}: si js ${p.si} cs ${q.si}`); for (let k = 0; k < 4; k++) { const m = cmpBucket(`part ${n} (si ${p.si}).${BN[k]}`, p.b[k], q.b[k]); if (m) d.push(m); } });
  }
  return d;
}
const names = fs.readFileSync(dir + "/jobs/index.txt", "utf8").split("\n").filter(Boolean);
let ok = 0, verts = 0;
for (const n of names) {
  const jf = dir + "/js/" + n + ".bin", cf = dir + "/cs/" + n + ".bin";
  if (!fs.existsSync(cf)) { bad++; console.log("MISSING cs output: " + n); continue; }
  const ja = fs.readFileSync(jf), ca = fs.readFileSync(cf);
  const a = parse(ja);
  if (a.agg) for (const q of a.b) verts += q.v.length / 8; else for (const p of a.parts) for (const q of p.b) verts += q.v.length / 8;
  if (ja.equals(ca)) { ok++; continue; }
  bad++; const d = cmp(a, parse(ca));
  console.log("MISMATCH " + n + ":\n  " + (showAll ? d : d.slice(0, 3)).join("\n  "));
}
console.log(`outputs: ${ok}/${names.length} jobs bit-identical (${verts} vertices)`);
process.exit(bad ? 1 : 0);

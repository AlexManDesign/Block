// Builds mesh jobs (real worldgen chunks + synthetic fuzz/edge cases), runs the reference JS mesher on them and writes
//   <out>/jobs/<name>.json   job payload exactly as the main thread posts it (sections/meta/sis/full)
//   <out>/js/<name>.bin      normalized JS mesher output (see dump format in README.md)
//   <out>/cfg_js.json        workerBlockCfg + VIRTUAL_BLOCKS as the JS worker receives them
// Usage: node gen-jobs.js <pretty.js> <outDir> [--quick] [--only=<substr>]
"use strict";
const fs = require("fs"), path = require("path");
const { load } = require("./jsenv.js");
const args = process.argv.slice(2), prettyPath = args[0], outDir = args[1];
if (!prettyPath || !outDir) { console.error("usage: node gen-jobs.js <pretty.js> <outDir> [--quick] [--only=substr]"); process.exit(2); }
const quick = args.includes("--quick"), onlyArg = (args.find((a) => a.startsWith("--only=")) || "").slice(7);
const env = load(prettyPath), B = env.B, CFG = env.workerBlockCfg, VB = env.VIRTUAL_BLOCKS;
fs.mkdirSync(outDir + "/jobs", { recursive: true }); fs.mkdirSync(outDir + "/js", { recursive: true });
fs.writeFileSync(outDir + "/cfg_js.json", JSON.stringify({ blocks: CFG, vblocks: VB, B }));
const core = env.createSharedMeshCore(B, CFG, VB);
fs.writeFileSync(outDir + "/jobs/index.txt", ""); // run order: both meshers reuse one core instance across all jobs
const VKEYS = Object.keys(VB), NIDS = CFG.length;

// ---------- normalized dump (shared with MeshParity.cs) ----------
const f32 = new Float32Array(1), u32 = new Uint32Array(f32.buffer);
function f32ToHalf(v) { // packTerrainVerts' converter
  f32[0] = v; const x = u32[0], s = (x >>> 16) & 32768, e = ((x >>> 23) & 255) - 112, m = x & 8388607;
  if (e <= 0) { if (e < -10) return s; return s | ((((m | 8388608) >> (1 - e)) + 4096) >> 13); }
  if (e >= 31) return s | 31744;
  return s | ((e << 10) + ((m + 4096) >> 13));
}
class W { constructor() { this.a = []; } u(v) { this.a.push(v >>> 0); } }
function bucket(w, V, I) {
  const n = V.length / 8; w.u(n); w.u(I.length);
  const fb = new Uint32Array(V.buffer, V.byteOffset, V.length), tile = new Uint16Array(1), lt = new Uint8Array(1);
  for (let k = 0; k < n; k++) {
    const o = k * 8; tile[0] = V[o + 5]; lt[0] = V[o + 7];
    w.u(fb[o]); w.u(fb[o + 1]); w.u(fb[o + 2]); w.u(f32ToHalf(V[o + 3])); w.u(f32ToHalf(V[o + 4])); w.u(tile[0]); w.u(fb[o + 6]); w.u(lt[0]);
  }
  for (let k = 0; k < I.length; k++) w.u(I[k]);
}
function dumpResult(r) {
  const w = new W(); w.u(0x4d564631); w.u(r.full ? 1 : 0); w.u(r.sis.length); for (const s of r.sis) w.u(s);
  if (r.full && r.agg) {
    w.u(1);
    for (const k of ["o", "c", "w", "t"]) bucket(w, new Float32Array(r.agg[k + "v"]), new Uint32Array(r.agg[k + "i"]));
    for (let si = 0; si < 24; si++) for (let k = 0; k < 8; k++) w.u(r.secLens[si][k] | 0);
  } else {
    w.u(0); w.u((r.parts || []).length);
    for (const p of r.parts || []) { w.u(p.si); for (const k of ["o", "c", "w", "t"]) bucket(w, new Float32Array(p[k + "v"]), new Uint32Array(p[k + "i"])); }
  }
  return Buffer.from(new Uint32Array(w.a).buffer);
}

// ---------- chunk store + payload builders (meshSectionPayload / meshMeta) ----------
const DIRS4 = [[1, 0], [-1, 0], [0, 1], [0, -1]];
function sectionHasBlocks(s) { if (!s) return false; for (let i = 0; i < 4096; i++) if (s.blocks[i] !== B.AIR) return true; return false; }
function makeJob(name, chunks, cx, cz, sis, full) {
  const c = chunks.get(cx + "," + cz);
  if (sis === "all") { sis = []; for (let si = 0; si < 24; si++) if (sectionHasBlocks(c.sections[si])) sis.push(si); }
  let mask = 0;
  for (const si of sis) { if (si < 0 || si >= 24) continue; mask |= 1 << si; if (si > 0) mask |= 1 << (si - 1); if (si + 1 < 24) mask |= 1 << (si + 1); }
  const sections = [];
  const add = (n, dx, dz) => { if (!n) return; for (let si = 0; si < 24; si++) { if (!(mask & (1 << si))) continue; const s = n.sections[si]; if (!s) continue; sections.push({ dx, dz, si, blocks: s.blocks.slice(), light: (s.light || new Uint8Array(4096)).slice() }); } };
  add(c, 0, 0); for (const d of DIRS4) add(chunks.get(cx + d[0] + "," + (cz + d[1])), d[0], d[1]);
  const meta = [];
  const addM = (n, sx, sz) => { if (!n || !n.gm) return; for (const [k, m] of n.gm) { const iy = k >> 8, r = k & 255; meta.push([(r & 15) + sx, iy, (r >> 4) + sz, m]); } };
  addM(c, 0, 0); for (const d of DIRS4) { const n = chunks.get(cx + d[0] + "," + (cz + d[1])); if (n) addM(n, d[0] * 16, d[1] * 16); }
  return { name, cx, cz, full, sis, sections, meta };
}
function gmFromEntries(entries) { // ingestGeneratedMeta()
  const gm = new Map();
  for (const q of entries) { if (!q || q.length < 4) continue; const lx = q[0] | 0, iy = q[1] | 0, lz = q[2] | 0, m = q[3]; if (lx < 0 || lx >= 16 || lz < 0 || lz >= 16 || iy < 0 || iy >= 384 || !m || typeof m !== "object") continue; gm.set((iy << 8) | (lz << 4) | lx, m); }
  return gm;
}
let jobCount = 0;
function runJob(job) {
  if (onlyArg && !job.name.includes(onlyArg)) return;
  const sections = job.sections.map((s) => ({ dx: s.dx, dz: s.dz, si: s.si, buffer: s.blocks.slice().buffer, light: s.light ? s.light.slice().buffer : null }));
  const t0 = Date.now();
  const r = core.run({ t: "mesh", id: ++jobCount, cx: job.cx, cz: job.cz, rev: 0, sections, meta: job.meta, sis: job.sis, full: job.full });
  const ms = Date.now() - t0;
  fs.writeFileSync(outDir + "/js/" + job.name + ".bin", dumpResult(r));
  const js = { name: job.name, cx: job.cx, cz: job.cz, full: job.full, sis: job.sis, meta: job.meta,
    sections: job.sections.map((s) => ({ dx: s.dx, dz: s.dz, si: s.si, b: Buffer.from(s.blocks).toString("base64"), l: s.light ? Buffer.from(s.light).toString("base64") : null })) };
  fs.writeFileSync(outDir + "/jobs/" + job.name + ".json", JSON.stringify(js));
  fs.appendFileSync(outDir + "/jobs/index.txt", job.name + "\n");
  let nv = 0; if (r.agg) for (const k of ["ov", "cv", "wv", "tv"]) nv += r.agg[k].byteLength / 32; else for (const p of r.parts) for (const k of ["ov", "cv", "wv", "tv"]) nv += p[k].byteLength / 32;
  console.log("job", job.name, "sis", job.sis.length, "sections", job.sections.length, "meta", job.meta.length, "verts", nv, ms + "ms");
}

// ---------- real worldgen chunks (the actual GEN_WORKER_SOURCE run in-process) ----------
function makeGen(seed) {
  let last = null; const self = { postMessage: (m) => { last = m; } };
  new Function("self", env.GEN_WORKER_SOURCE)(self);
  self.onmessage({ data: { t: "init", B, blocks: CFG, seed } });
  return (cx, cz) => { self.onmessage({ data: { t: "gen", id: 1, cx, cz, seed, delta: [] } }); return last; };
}
function realChunks(seed, centers) {
  const gen = makeGen(seed), chunks = new Map();
  const get = (cx, cz) => {
    const k = cx + "," + cz; if (chunks.has(k)) return; const g = gen(cx, cz), sections = new Array(24).fill(null);
    for (const s of g.sections) sections[s.si] = { blocks: new Uint8Array(s.buffer), light: new Uint8Array(s.light) };
    chunks.set(k, { sections, gm: gmFromEntries(g.meta) });
  };
  for (const [cx, cz] of centers) { get(cx, cz); for (const d of DIRS4) get(cx + d[0], cz + d[1]); }
  return chunks;
}
function structureCenters(seed, maxPerType) {
  const K = env.createWorldGenKernel(B, seed), out = [];
  for (const t of ["pyramid", "outpost", "portal", "ship"]) {
    let n = 0;
    for (let r = 0; r < 10 && n < maxPerType; r++) for (let rx = -r; rx <= r && n < maxPerType; rx++) for (let rz = -r; rz <= r && n < maxPerType; rz++) {
      if (Math.max(Math.abs(rx), Math.abs(rz)) !== r) continue;
      const s = K.structureCandidate(t, rx, rz); if (!s) continue;
      // pick the structure chunk with the most metadata in its 3x3 footprint
      let best = null, bm = -1;
      for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) { const m = K.structureMetaForChunk(s.acx + dx, s.acz + dz).length; if (m > bm) { bm = m; best = [s.acx + dx, s.acz + dz]; } }
      out.push({ t, c: best }); n++;
    }
  }
  return out;
}
const seeds = quick ? [12345] : [12345, -5, 7, 2147483647];
for (const seed of seeds) {
  const centers = [[0, 0], [3, -2]]; if (!quick) centers.push([-7, 11], [20, 5]);
  const st = structureCenters(seed, quick ? 1 : 2);
  for (const s of st) centers.push(s.c);
  const chunks = realChunks(seed, centers);
  centers.forEach(([cx, cz], i) => {
    const tag = i < centers.length - st.length ? "" : "_" + st[i - (centers.length - st.length)].t;
    runJob(makeJob("real_" + seed + "_" + cx + "_" + cz + tag, chunks, cx, cz, "all", true));
  });
  const [cx, cz] = centers[centers.length - 1];
  const c = chunks.get(cx + "," + cz), present = []; for (let si = 0; si < 24; si++) if (sectionHasBlocks(c.sections[si])) present.push(si);
  runJob(makeJob("realpart_" + seed + "_" + cx + "_" + cz, chunks, cx, cz, present.filter((_, k) => k % 3 === 1), false));
}

// ---------- synthetic chunks ----------
function rngOf(seed) { let a = seed >>> 0; return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const pick = (rng, a) => a[Math.floor(rng() * a.length)];
const VIRT = [B.VIRTUAL_OPAQUE, B.VIRTUAL_TRANSPARENT, B.VIRTUAL_CUTOUT, B.VIRTUAL_PLANT];
const GREEDY = [], ALL = [];
for (let id = 1; id < NIDS; id++) { const b = CFG[id]; if (!b) continue; ALL.push(id); if (b.solid && !b.transparent && !b.cutout && !b.plant && !b.waterPlant && !b.special) GREEDY.push(id); }
const WATERS = [B.WATER, B.WATER_FALLING, B.FLOW7, B.FLOW6, B.FLOW5, B.FLOW4, B.FLOW3, B.FLOW2, B.FLOW1], LAVAS = [B.LAVA, B.LAVA_FLOW2, B.LAVA_FLOW1];
const FACINGS = ["north", "south", "east", "west", "up", "down"], MOUNTS = ["floor", "ceiling", "north", "south", "east", "west"];
function randMeta(rng, id) {
  const m = {};
  if (VIRT.includes(id)) m.v = rng() < 0.04 ? "NO_SUCH_KEY" : pick(rng, VKEYS);
  if (rng() < 0.85) m.facing = pick(rng, FACINGS);
  if (rng() < 0.5) m.upper = rng() < 0.5;
  if (rng() < 0.3) m.open = rng() < 0.5;
  if (rng() < 0.4) m.axis = pick(rng, ["x", "y", "z"]);
  if (rng() < 0.3) m.mount = pick(rng, MOUNTS);
  if (rng() < 0.3) m.faces = Math.floor(rng() * 64);
  if (rng() < 0.3) m.count = 1 + Math.floor(rng() * 5);
  if (rng() < 0.3) { m.chestDouble = rng() < 0.7; const d = pick(rng, [[1, 0], [-1, 0], [0, 1], [0, -1]]); m.pairDX = d[0]; m.pairDZ = d[1]; m.chestSide = rng() < 0.5; }
  if (rng() < 0.4) m.part = pick(rng, ["top", "bottom"]);
  if (rng() < 0.3) m.potKey = pick(rng, ["b:" + B.POPPY, "b:" + B.RED_MUSHROOM, "b:" + B.FERN, "b:" + B.STONE, "b:999", "v:" + pick(rng, VKEYS), "v:NOPE", "x"]);
  if (rng() < 0.1) m.pressed = rng() < 0.5;
  if (rng() < 0.05) m.bedPart = true;
  if (rng() < 0.2) m.waterlogged = rng() < 0.5;
  return m;
}
function emptyChunk() { const sections = new Array(24).fill(null); return { sections, gm: new Map() }; }
function setB(c, x, y, z, id, light) {
  const si = y >> 4; let s = c.sections[si];
  if (!s) s = c.sections[si] = { blocks: new Uint8Array(4096), light: new Uint8Array(4096).fill(0xf0) };
  const i = ((y & 15) * 16 + z) * 16 + x; s.blocks[i] = id; if (light != null) s.light[i] = light;
}
function setM(c, x, y, z, m) { c.gm.set((y << 8) | (z << 4) | x, m); }
function fuzzChunk(seed, ys, density, pal, lightMode) {
  const rng = rngOf(seed), c = emptyChunk();
  for (const y of ys) for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) {
    const L = lightMode === "rand" ? Math.floor(rng() * 256) : lightMode === "pat" ? ((((x * 7 + y * 3 + z * 11) & 15) << 4) | ((x + z * 5 + y) % 16)) : 0xf0;
    if (rng() >= density) { if (lightMode !== "flat") setB(c, x, y, z, B.AIR, L); continue; }
    const id = pal(rng); setB(c, x, y, z, id, L);
    if (rng() < 0.8) setM(c, x, y, z, randMeta(rng, id));
  }
  return c;
}
const palAll = (rng) => { const r = rng(); return r < 0.25 ? pick(rng, GREEDY) : r < 0.35 ? pick(rng, VIRT) : pick(rng, ALL); };
const palWater = (rng) => { const r = rng(); return r < 0.55 ? pick(rng, WATERS) : r < 0.65 ? pick(rng, [B.SEAGRASS, B.KELP, B.SEA_PICKLE, B.LILY_PAD, B.TUBE_CORAL_FAN]) : r < 0.75 ? pick(rng, GREEDY) : r < 0.85 ? pick(rng, LAVAS) : pick(rng, ALL); };
const palShapes = (rng) => { const r = rng(); return r < 0.3 ? pick(rng, [B.OAK_STAIRS, B.STONE_STAIRS, B.DARK_STAIRS]) : r < 0.45 ? pick(rng, [B.OAK_FENCE, B.COBBLE_WALL, B.GLASS_PANE, B.OAK_GATE]) : r < 0.55 ? B.RAIL : r < 0.65 ? B.CHEST : r < 0.75 ? pick(rng, VIRT) : r < 0.85 ? pick(rng, GREEDY) : pick(rng, ALL); };
function synthJobs(name, centerFn, nbFn, sisList) {
  const chunks = new Map();
  chunks.set("0,0", centerFn());
  DIRS4.forEach((d, k) => { const n = nbFn(k); if (n) chunks.set(d[0] + "," + d[1], n); });
  for (const [suffix, sis, full] of sisList) runJob(makeJob(name + suffix, chunks, 0, 0, sis, full));
}
const range = (a, b) => { const r = []; for (let i = a; i < b; i++) r.push(i); return r; };
// dense random mix of every block id + virtual carriers with random metadata and random light
synthJobs("fuzz_dense", () => fuzzChunk(101, range(48, 80), 0.55, palAll, "rand"), (k) => fuzzChunk(200 + k, range(46, 82), 0.5, palAll, "rand"),
  [["_full", "all", true], ["_part", [4, 3], false], ["_dup", [3, 3], false], ["_one", [4], false]]);
// sparse mix at the world edges (sections 0, 1, 22, 23), flat light
synthJobs("fuzz_edges", () => fuzzChunk(102, range(0, 32).concat(range(352, 384)), 0.2, palAll, "flat"), (k) => fuzzChunk(300 + k, range(0, 20).concat(range(360, 384)), 0.3, palAll, "pat"),
  [["_full", "all", true], ["_part", [0, 23, 1], false]]);
// fluids: water levels, falling water, waterlogged plants, lava levels; neighbours too
synthJobs("fuzz_fluid", () => fuzzChunk(103, range(60, 100), 0.7, palWater, "pat"), (k) => fuzzChunk(400 + k, range(60, 100), 0.6, palWater, "rand"),
  [["_full", "all", true], ["_part", [4, 5], false]]);
// shapes: stairs corners, fences/walls/panes/gates connections, rails (slopes/curves), chests
synthJobs("fuzz_shapes", () => fuzzChunk(104, range(64, 72), 0.6, palShapes, "pat"), (k) => fuzzChunk(500 + k, range(63, 73), 0.5, palShapes, "pat"), [["_full", "all", true]]);
// lattice: every native id x several metas, isolated (air around) and against stone, plus every virtual key
synthJobs("lattice", () => {
  const c = emptyChunk(), rng = rngOf(7); let k = 0;
  const put = (id, m) => { const x = (k % 5) * 3 + 1, z = (Math.floor(k / 5) % 5) * 3 + 1, y = 2 + Math.floor(k / 25) * 3; k++; if (y >= 380) return; setB(c, x, y, z, id, ((k * 37) & 255)); if (m) setM(c, x, y, z, m); if (k % 2) setB(c, x + 1, y, z, B.STONE, 0xf0); };
  for (const id of ALL) { if (VIRT.includes(id)) continue; put(id, null); put(id, { facing: "east", upper: true, open: true, axis: "x", mount: "ceiling", count: 3, part: "top", chestDouble: true, pairDX: 1, pairDZ: 0, chestSide: true, potKey: "b:" + B.POPPY }); put(id, randMeta(rng, id)); }
  for (const key of VKEYS) { const b = VB[key], car = b.plant || ["tallplant", "vine", "lichen", "lilypad", "seapickle", "bamboo", "rail"].includes(b.shape) ? B.VIRTUAL_PLANT : b.lightBlock ? B.VIRTUAL_OPAQUE : b.transparent ? B.VIRTUAL_TRANSPARENT : b.cutout || b.shape === "door" || b.shape === "trapdoor" ? B.VIRTUAL_CUTOUT : B.VIRTUAL_OPAQUE; put(car, { v: key }); put(car, Object.assign(randMeta(rng, car), { v: key })); }
  return c;
}, () => null, [["_full", "all", true]]);
// clusters of one virtual key (same-key transparent culling, virtual fences connecting), across the chunk border
synthJobs("vclusters", () => {
  const c = emptyChunk(), rng = rngOf(9);
  VKEYS.forEach((key, n) => { const b = VB[key], car = b.plant ? B.VIRTUAL_PLANT : b.transparent ? B.VIRTUAL_TRANSPARENT : b.cutout ? B.VIRTUAL_CUTOUT : B.VIRTUAL_OPAQUE, y = 100 + (n % 60) * 2, x0 = (Math.floor(n / 60) * 5) % 14;
    for (let dx = 0; dx < 3; dx++) for (let dz = 0; dz < 2; dz++) { setB(c, x0 + dx, y, (n * 3 + dz) % 16, car, Math.floor(rng() * 256)); setM(c, x0 + dx, y, (n * 3 + dz) % 16, { v: key, facing: pick(rng, FACINGS), upper: rng() < 0.5 }); } });
  for (let y = 100; y < 220; y++) { setB(c, 15, y, 7, B.OAK_FENCE, 0xf0); setB(c, 0, y, 8, B.VIRTUAL_OPAQUE, 0xf0); setM(c, 0, y, 8, { v: VKEYS[y % VKEYS.length] }); }
  return c;
}, (k) => fuzzChunk(600 + k, range(98, 222), 0.25, palAll, "rand"), [["_full", "all", true]]);
// flat water ocean (flat-top greedy merge) with islands and mixed light groups
synthJobs("ocean", () => {
  const c = emptyChunk(), rng = rngOf(11);
  for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) { for (let y = 40; y < 63; y++) setB(c, x, y, z, B.WATER, (y === 62 && x > 8) ? 0xd0 : 0xf0); setB(c, x, 39, z, B.SAND, 0xf0); }
  for (let n = 0; n < 6; n++) { const x = Math.floor(rng() * 16), z = Math.floor(rng() * 16); setB(c, x, 62, z, pick(rng, [B.STONE, B.FLOW3, B.SEAGRASS, B.KELP, B.AIR])); }
  setB(c, 4, 63, 4, B.WATER); setB(c, 5, 62, 5, B.WATER_FALLING);
  return c;
}, (k) => { const c = emptyChunk(); for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) for (let y = 40; y < (k === 1 ? 60 : 63); y++) setB(c, x, y, z, k === 3 ? B.FLOW5 : B.WATER, 0xf0); return c; },
  [["_full", "all", true]]);
// odd metadata value types (JS truthiness / ToNumber / ToInt32 semantics): "", numbers, booleans, strings for numbers
function weirdMeta(rng, id) {
  const m = randMeta(rng, id), W = (a) => pick(rng, a);
  if (rng() < 0.5) m.facing = W(["", 3, true, "NORTH", 0, "west"]);
  if (rng() < 0.4) m.axis = W(["", 0, "x", 1, "z"]);
  if (rng() < 0.4) m.upper = W(["yes", 0, 1, "", false, "0"]);
  if (rng() < 0.4) m.open = W([1, "", "no", 0]);
  if (rng() < 0.5) m.count = W(["2", "abc", 2.7, -1, true, 0, 9, "", 3.5]);
  if (rng() < 0.4) m.faces = W(["12", 12.9, -1, 1e10, 63, -2147483649, 0]);
  if (rng() < 0.4) { m.chestDouble = W([1, "x", 0]); m.pairDX = W(["1", 1.5, true, -1, "-1", 0]); m.pairDZ = W([0, "1", -1.9, false]); m.chestSide = W([1, "", 0]); }
  if (rng() < 0.3) m.potKey = W([5, "", "b:", "b:abc", "b:28.0", "v:", "b:-1", "b: 28"]);
  if (rng() < 0.3) m.part = W(["TOP", 1, "top", ""]);
  if (rng() < 0.3) m.mount = W([5, "", "floor", "Floor"]);
  if (rng() < 0.2 && VIRT.includes(id)) m.v = W([5, "", true, VKEYS[0]]);
  if (rng() < 0.2) m.pressed = W([1, "", "y"]);
  if (rng() < 0.1) m.bedPart = W([1, "", 0]);
  if (rng() < 0.2) m.waterlogged = W([1, "", "y", 0]);
  return m;
}
synthJobs("fuzz_weird", () => { const c = fuzzChunk(105, range(120, 136), 0.5, palShapes, "rand"); const rng = rngOf(55); for (const [k, m] of c.gm) c.gm.set(k, weirdMeta(rng, 0)); return c; },
  (k) => { const c = fuzzChunk(700 + k, range(119, 137), 0.4, palAll, "rand"); const rng = rngOf(77 + k); for (const [kk] of c.gm) c.gm.set(kk, weirdMeta(rng, 0)); return c; }, [["_full", "all", true]]);
// payload oddities: missing light buffers, duplicate sections (last wins), out-of-range dx/dz/si entries, meta outside the 48x48 window
(() => {
  const chunks = new Map(); chunks.set("0,0", fuzzChunk(106, range(200, 232), 0.5, palAll, "rand")); DIRS4.forEach((d, k) => chunks.set(d[0] + "," + d[1], fuzzChunk(800 + k, range(198, 234), 0.5, palAll, "rand")));
  const j = makeJob("payload_odd", chunks, 0, 0, "all", true), rng = rngOf(99);
  j.sections.forEach((s, n) => { if (n % 4 === 1) s.light = null; });
  const extra = [];
  for (const s of j.sections.slice(0, 6)) { const b = s.blocks.slice(); for (let i = 0; i < 4096; i += 7) b[i] = pick(rng, ALL); extra.push({ dx: s.dx, dz: s.dz, si: s.si, blocks: b, light: null }); }
  extra.push({ dx: 2, dz: 0, si: 13, blocks: new Uint8Array(4096).fill(B.STONE), light: null }, { dx: 0, dz: 0, si: 30, blocks: new Uint8Array(4096).fill(B.STONE), light: null }, { dx: 1, dz: 1, si: 13, blocks: new Uint8Array(4096).fill(B.STONE), light: null });
  j.sections.push(...extra);
  j.meta.push([-20, 210, 3, { v: VKEYS[1] }], [40, 210, 3, { facing: "east" }], [3, 500, 3, { upper: true }], [3, -1, 3, {}], [5, 210, 5, null], [5, 211, 5, { v: VKEYS[2] }], [5, 211, 5, { v: VKEYS[3], facing: "west" }]);
  runJob(j);
  const p = makeJob("payload_odd_part", chunks, 0, 0, [13, 12, 14], false); p.sections.forEach((s, n) => { if (n % 3 === 0) s.light = null; }); runJob(p);
})();
synthJobs("empty", () => emptyChunk(), () => null, [["_full", "all", true], ["_none", [], false], ["_oob", [-1, 24, 99], false]]);
console.log("jobs:", jobCount);

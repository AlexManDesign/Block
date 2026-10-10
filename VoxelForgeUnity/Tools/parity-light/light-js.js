// JS side of the light parity harness. Runs the ORIGINAL main-thread light engine of pretty.js (jsenv-light.js) on
// chunks produced by the ORIGINAL generation worker (GEN_WORKER_SOURCE, in-process), generates a deterministic
// scenario of chunk loads/unloads and block edits next to chunk borders, executes it and writes
//   <out>/<win>/scenario.txt   the command list (replayed verbatim by LightParity.cs)
//   <out>/<win>/js.txt         state after every light-relevant command (per-section FNV hashes of blocks+light,
//                              chunk dirty/meshRev/fullMeshDirty/dirtySections, light queue sizes, return values)
//   <out>/rules_js.txt         light predicates for every block id / virtual key / meta variant
// Usage: node light-js.js <pretty.js> <outDir> <window> [--dump=<step>] [--rounds=N]
//   window: one of the WINDOWS names below. --dump writes <out>/<win>/js_dump_<step>.bin (full blocks+light).
"use strict";
const fs = require("fs");
const { load } = require("./jsenv-light.js");
const args = process.argv.slice(2), prettyPath = args[0], outDir = args[1], winName = args[2];
const WINDOWS = {
  // seed 12345: water, lava, caves, every leaves kind, cave vines with berries (generated meta)
  caves: { seed: 12345, cx0: 2, cx1: 5, cz0: -5, cz1: -3, rounds: 70 },
  // seed -5: ocean shore, kelp/seagrass (waterPlant attenuation), lava, caves, leaves
  ocean: { seed: -5, cx0: 5, cx1: 7, cz0: -3, cz1: -1, rounds: 60 },
};
if (!prettyPath || !outDir || !WINDOWS[winName]) { console.error("usage: node light-js.js <pretty.js> <outDir> <" + Object.keys(WINDOWS).join("|") + "> [--dump=step] [--rounds=N]"); process.exit(2); }
const dumpStep = +((args.find((a) => a.startsWith("--dump=")) || "--dump=-1").slice(7));
const roundsArg = args.find((a) => a.startsWith("--rounds="));
const WIN = WINDOWS[winName], SEED = WIN.seed, ROUNDS = roundsArg ? +roundsArg.slice(9) : WIN.rounds;
const env = load(prettyPath), W = env.W, B = env.B, BL = env.blocks, VB = env.VIRTUAL_BLOCKS;
const CHUNK = W.CHUNK, MINY = W.WORLD_MIN_Y, MAXY = W.WORLD_MAX_Y;
const dir = outDir + "/" + winName; fs.mkdirSync(dir, { recursive: true });

// ---------- output ----------
const outLines = [];
const out = (s) => outLines.push(s);
function fnv(a, h) { for (let i = 0; i < a.length; i++) h = Math.imul(h ^ a[i], 16777619); return h >>> 0; }
let step = 0;
function state() {
  const keys = [...W.chunks.keys()].sort();
  out("S " + step + " q=" + W.lightDirtyCount() + " k=" + W.lightDirtyKeys.size + " n=" + keys.length);
  for (const k of keys) {
    const c = W.chunks.get(k); let s = k + " d=" + (c.dirty ? 1 : 0) + " f=" + (c.fullMeshDirty ? 1 : 0) + " r=" + (c.meshRev | 0) + " ds=" + [...(c.dirtySections || [])].sort((a, b) => a - b).join(",");
    for (let si = 0; si < 24; si++) { const sec = c.sections[si]; if (!sec) continue; s += " " + si + ":" + fnv(sec.blocks, 2166136261).toString(16) + ":" + (sec.light ? fnv(sec.light, 2166136261).toString(16) : "-"); }
    out(s);
  }
  if (step === dumpStep) {
    const parts = [];
    for (const k of keys) {
      const c = W.chunks.get(k), hdr = Buffer.alloc(8); hdr.writeInt32LE(c.cx, 0); hdr.writeInt32LE(c.cz, 4); parts.push(hdr);
      for (let si = 0; si < 24; si++) { const sec = c.sections[si]; parts.push(Buffer.from([sec ? 1 : 0])); if (sec) { parts.push(Buffer.from(sec.blocks)); parts.push(Buffer.from(sec.light || new Uint8Array(4096).fill(0xf0))); } }
    }
    fs.writeFileSync(dir + "/js_dump_" + step + ".bin", Buffer.concat(parts));
  }
}

// ---------- generation worker (original source, in-process) + integrateGenResult/createChunkFromData light part ----------
let lastGen = null; const self = { postMessage: (m) => { lastGen = m; } };
new Function("self", env.GEN_WORKER_SOURCE)(self);
self.onmessage({ data: { t: "init", B, blocks: env.workerBlockCfg, seed: SEED } });
env.setStrict(true);
let geoChunks = 0;
function genChunk(cx, cz) {
  const k = W.ckey(cx, cz);
  if (W.chunks.has(k)) { out("G " + k + " skip"); return; }
  const es = W.editsByChunk.get(k), delta = es ? [...es.values()].map((q) => q.slice()) : []; // queueChunkGeneration
  self.onmessage({ data: { t: "gen", id: 1, cx, cz, seed: SEED, delta } });
  const m = lastGen;
  // initial light of the generation worker, before stitching, plus main-thread buildInitialPackedLightDense on the same data
  const dense = new Uint8Array(CHUNK * W.WORLD_H * CHUNK); let gh = 2166136261;
  for (const q of m.sections) { dense.set(new Uint8Array(q.buffer), q.si * 4096); gh = fnv([q.si], gh); gh = fnv(new Uint8Array(q.light), gh); }
  const ip = W.buildInitialPackedLightDense(dense, m.meta);
  out("G " + k + " sec=" + m.sections.length + " meta=" + m.meta.length + " sim=" + new Uint32Array(m.sim).length + " fb=" + new Uint32Array(m.fluidBoundary).length + " genlight=" + (gh >>> 0).toString(16) + " initdense=" + fnv(ip, 2166136261).toString(16));
  // integrateGenResult
  W.installGeneratedMeta(m.cx, m.cz, m.meta || []);
  const sections = W.emptySections();
  for (const q of m.sections || []) if (q && q.si >= 0 && q.si < 24) { const light = q.light ? new Uint8Array(q.light) : new Uint8Array(4096); if (!q.light) light.fill(0xf0); sections[q.si] = { blocks: new Uint8Array(q.buffer), light }; }
  // createChunkFromData (light-relevant part). Every second chunk pretends to own section geometry so that
  // markLightDirtyChunks takes its dirtySections branch.
  const geo = (geoChunks++ & 1) === 1;
  const c = { cx, cz, key: k, sections, dirty: true, fullMeshDirty: !geo, dirtySections: new Set(), sectionGeo: geo ? [] : null, meshRev: 0, boundaryRev: 1, mapRev: 0 };
  W.chunks.set(k, c);
  W.chunkFastSet(c);
  W.stitchChunkLight(c);
  // installMetadataSimulationSeeds: light part
  const mm = W.metaByChunk.get(c.key);
  if (mm && mm.size) for (const [code, mt] of mm) {
    if (!mt) continue;
    const lx = code & 15, lz = (code >>> 4) & 15, y = (code >>> 8) + MINY, x = c.cx * CHUNK + lx, z = c.cz * CHUNK + lz, d = W.virtualDefFromMeta(mt);
    if (d && d.light) W.queueLightUpdate(x, y, z);
  }
}
function unloadChunk(cx, cz) { // deleteChunk: storage part
  const k = W.ckey(cx, cz), c = W.chunks.get(k); if (!c) return;
  W.chunks.delete(k); W.generatedMetaByChunk.delete(k); W.chunkFastDelete(c);
}
// setBlock(): every step that matters for light (metadata maps, persisted edit index, chunk storage, light signature
// comparison -> queueLightUpdate + notePlayerEditLight). Mesh/simulation side effects are not part of this harness.
function setB(x, y, z, id, meta) {
  x |= 0; y |= 0; z |= 0;
  if (!(y >= MINY && y < MAXY) || y === MINY) return;
  const oldId = W.getBlock(x, y, z), k = W.key3(x, y, z), oldMeta = W.getBlockMeta(x, y, z), oldLightSig = W.lightSignatureState(oldId, oldMeta);
  const mk = W.ckey(Math.floor(x / CHUNK), Math.floor(z / CHUNK)); let mm = W.metaByChunk.get(mk);
  if (meta && typeof meta === "object") { const mv = { ...meta }; W.blockMeta.set(k, mv); if (!mm) W.metaByChunk.set(mk, (mm = new Map())); mm.set(W.metaLocalKey(x, y, z), mv); }
  else { W.blockMeta.delete(k); if (mm) { mm.delete(W.metaLocalKey(x, y, z)); if (!mm.size) W.metaByChunk.delete(mk); } }
  W.addEditIndex(x, y, z, id, false);
  const cx = Math.floor(x / CHUNK), cz = Math.floor(z / CHUNK), c = W.chunkFastGet(cx, cz);
  if (c) { W.chunkSet(c, x - cx * CHUNK, y, z - cz * CHUNK, id); c.mapRev = (c.mapRev || 0) + 1; }
  const newMeta = meta && typeof meta === "object" ? meta : null, newLightSig = W.lightSignatureState(id, newMeta);
  if (oldLightSig !== newLightSig) { W.queueLightUpdate(x, y, z); W.notePlayerEditLight(x, y, z); }
}

// ---------- command interpreter (identical semantics in LightParity.cs) ----------
const scenario = [];
function exec(line) {
  scenario.push(line); step++;
  const a = line.split(" "), n = (i) => +a[i];
  out("C " + step + " " + line);
  switch (a[0]) {
    case "gen": genChunk(n(1), n(2)); state(); break;
    case "unload": unloadChunk(n(1), n(2)); state(); break;
    case "set": setB(n(1), n(2), n(3), n(4), a[5] === "-" ? null : JSON.parse(a[5])); out("Q " + W.lightDirtyCount() + " " + W.lightDirtyKeys.size); break;
    case "light": out("L " + W.processLightDirty(Infinity)); state(); break;
    case "pbegin": if (W.getPlayerEditDepth() === 0) W.resetPlayerEditLightN(); W.setPlayerEditDepth(W.getPlayerEditDepth() + 1); break;
    case "pend": W.setPlayerEditDepth(W.getPlayerEditDepth() - 1); if (W.getPlayerEditDepth() === 0) out("P " + W.flushImmediatePlayerEditLighting()); state(); break;
    case "repair": W.repairLightAt(n(1), n(2), n(3)); state(); break;
    case "probe": { // point queries used by meshing/gameplay
      const x = n(1), y = n(2), z = n(3);
      out("R " + W.getPackedLightWorld(x, y, z) + " " + W.blockEmissionAt(x, y, z) + " " + (W.lightStopsAt(x, y, z) ? 1 : 0) + " " + W.lightCostAt(x, y, z) + " " + fnv(W.directSkyColumn(x, z), 2166136261).toString(16));
      break;
    }
    default: throw new Error("bad command " + line);
  }
}

// ---------- scenario generation (decisions use the JS world; C# only replays) ----------
function rngOf(seed) { let a = seed >>> 0; return () => { a = (a + 0x6d2b79f5) >>> 0; let t = a; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }; }
const rng = rngOf(SEED ^ 0x5eed);
const ri = (n) => Math.floor(rng() * n), pick = (a) => a[ri(a.length)];
const loaded = () => [...W.chunks.values()];
const isLoaded = (cx, cz) => W.chunks.has(W.ckey(cx, cz));
// edits only inside loaded chunks (an edit in an unloaded chunk would read worldgen baseBlock, not light code)
const set = (x, y, z, id, meta) => isLoaded(Math.floor(x / 16), Math.floor(z / 16)) && exec("set " + x + " " + y + " " + z + " " + id + " " + (meta ? JSON.stringify(meta) : "-"));
function borderXZ(edgeOfLoaded) {
  // a column on a chunk border: between two loaded chunks, or (edgeOfLoaded) next to an unloaded chunk; sometimes a corner
  for (let t = 0; t < 200; t++) {
    const c = pick(loaded()), d = pick([[1, 0], [-1, 0], [0, 1], [0, -1]]), nb = isLoaded(c.cx + d[0], c.cz + d[1]);
    if (nb === !!edgeOfLoaded) continue;
    let lx = ri(16), lz = ri(16);
    if (d[0]) lx = d[0] > 0 ? 15 - ri(2) : ri(2); else lz = d[1] > 0 ? 15 - ri(2) : ri(2);
    if (rng() < 0.2) { lx = rng() < 0.5 ? 0 : 15; lz = rng() < 0.5 ? 0 : 15; }
    return [c.cx * 16 + lx, c.cz * 16 + lz];
  }
  const c = pick(loaded()); return [c.cx * 16 + ri(16), c.cz * 16 + ri(16)];
}
function surfaceY(x, z) { for (let y = MAXY - 1; y > MINY; y--) if (W.getBlock(x, y, z) !== B.AIR) return y; return MINY; }
function undergroundAir(x, z) { // air cells below the first sky-stopping block of the column (caves)
  const r = []; let roof = false;
  for (let y = MAXY - 1; y > MINY + 1; y--) { const id = W.getBlock(x, y, z); if (W.lightStopsState(id, W.getBlockMeta(x, y, z))) roof = true; else if (roof && id === B.AIR) r.push(y); }
  return r;
}
function findNear(pred, edge) { // a border column cell matching pred(id, meta, x, y, z)
  for (let t = 0; t < 60; t++) {
    const [x, z] = borderXZ(edge), ys = [];
    for (let y = MINY + 1; y < MAXY; y++) { const id = W.getBlock(x, y, z); if (id !== B.AIR && pred(id, W.getBlockMeta(x, y, z), x, y, z)) ys.push(y); }
    if (ys.length) return [x, pick(ys), z];
  }
  return null;
}
function caveCell(edge) { for (let t = 0; t < 40; t++) { const [x, z] = borderXZ(edge), ys = undergroundAir(x, z); if (ys.length) return [x, pick(ys), z]; } return null; }
const LEAVES = [B.LEAVES, B.BIRCH_LEAVES, B.SPRUCE_LEAVES, B.DARK_LEAVES, B.JUNGLE_LEAVES, B.ACACIA_LEAVES];
const WATERS = [B.WATER, B.WATER_FALLING, B.FLOW7, B.FLOW6, B.FLOW5, B.FLOW4, B.FLOW3, B.FLOW2, B.FLOW1];
const VLIGHT = Object.keys(VB).filter((k) => VB[k].light), VWPLANT = Object.keys(VB).filter((k) => VB[k].waterPlant), VTRANS = Object.keys(VB).filter((k) => VB[k].solid && VB[k].transparent);
const placed = []; // emitters / occluders placed earlier, removed later
const ACTIONS = {
  dig(edge) { const [x, z] = borderXZ(edge), y = surfaceY(x, z), d = 1 + ri(6); for (let i = 0; i < d; i++) set(x, y - i, z, B.AIR); },
  roof(edge) { const [x, z] = borderXZ(edge), y = surfaceY(x, z) + 2 + ri(3), id = pick([B.STONE, B.STONE, B.GLASS, pick(LEAVES), B.OAK_SLAB, B.STONE_STAIRS]); for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) set(x + dx, y, z + dz, id); placed.push([x, y, z, "roof"]); },
  pillar(edge) { const [x, z] = borderXZ(edge), y = surfaceY(x, z), h = 2 + ri(5), id = pick([B.STONE, B.GLASS, pick(LEAVES), pick(WATERS)]); for (let i = 1; i <= h; i++) set(x, y + i, z, id); },
  emitter(edge) {
    const p = rng() < 0.7 ? caveCell(edge) : (() => { const [x, z] = borderXZ(edge); return [x, surfaceY(x, z) + 1, z]; })(); if (!p) return;
    const r = rng();
    if (r < 0.3) set(p[0], p[1], p[2], B.TORCH); else if (r < 0.45) set(p[0], p[1], p[2], B.LAVA); else if (r < 0.55) set(p[0], p[1], p[2], B.CRYING_OBSIDIAN);
    else if (r < 0.62) set(p[0], p[1], p[2], B.FURNACE_LIT); else if (r < 0.7) set(p[0], p[1], p[2], B.GLOW_LICHEN); else if (r < 0.76) set(p[0], p[1], p[2], B.MAGMA_BLOCK);
    else if (r < 0.88) set(p[0], p[1], p[2], B.VIRTUAL_OPAQUE, { v: pick(VLIGHT) }); else set(p[0], p[1], p[2], B.FIRE);
    placed.push([p[0], p[1], p[2], "emit"]);
  },
  removePlaced() { if (!placed.length) return ACTIONS.emitter(false); const i = ri(placed.length), p = placed.splice(i, 1)[0];
    if (p[3] === "roof") { for (let dx = -1; dx <= 1; dx++) for (let dz = -1; dz <= 1; dz++) set(p[0] + dx, p[1], p[2] + dz, B.AIR); } else set(p[0], p[1], p[2], rng() < 0.7 ? B.AIR : B.STONE); },
  removeGenerated(edge) { // break a generated emitter / occluder / attenuator next to a border
    const kinds = [(id) => BL[id].light > 0, (id, m) => id === B.CAVE_VINES && m && m.berries, (id) => WATERS.includes(id), (id) => LEAVES.includes(id), (id) => id === B.KELP || id === B.SEAGRASS || id === B.SEA_PICKLE];
    const p = findNear(pick(kinds), edge) || findNear(kinds[2], edge); if (!p) return;
    set(p[0], p[1], p[2], rng() < 0.8 ? B.AIR : B.STONE);
  },
  water(edge) { const [x, z] = borderXZ(edge), y = surfaceY(x, z), id = W.getBlock(x, y, z);
    if (WATERS.includes(id)) { if (rng() < 0.5) set(x, y, z, B.STONE); else for (let i = 0; i < 3; i++) set(x, y - i, z, B.AIR); }
    else { set(x, y + 1, z, pick(WATERS)); if (rng() < 0.5) set(x, y + 2, z, B.SEA_PICKLE); } },
  virtual(edge) { const p = caveCell(edge) || (() => { const [x, z] = borderXZ(edge); return [x, surfaceY(x, z) + 1, z]; })();
    const r = rng();
    if (r < 0.35) set(p[0], p[1], p[2], B.VIRTUAL_TRANSPARENT, { v: pick(VTRANS), waterlogged: rng() < 0.6 });
    else if (r < 0.6) set(p[0], p[1], p[2], B.VIRTUAL_PLANT, { v: pick(VWPLANT) });
    else if (r < 0.8) { set(p[0], p[1], p[2], B.VIRTUAL_OPAQUE, { v: pick(VLIGHT) }); set(p[0], p[1], p[2], B.VIRTUAL_OPAQUE, { v: "ACACIA_PLANKS" }); }
    else { set(p[0], p[1], p[2], B.GLASS, { waterlogged: true }); }
    placed.push([p[0], p[1], p[2], "emit"]); },
  vines(edge) { const p = caveCell(edge); if (!p) return; set(p[0], p[1], p[2], B.CAVE_VINES, { berries: true }); exec("repair " + p[0] + " " + p[1] + " " + p[2]); placed.push([p[0], p[1], p[2], "emit"]); },
  extremes() { const c = pick(loaded()), x = c.cx * 16 + pick([0, 15, ri(16)]), z = c.cz * 16 + pick([0, 15, ri(16)]), y = pick([MAXY - 1, MAXY - 2, MINY + 1, MINY + 2, 250 + ri(60)]);
    set(x, y, z, pick([B.STONE, B.TORCH, B.LAVA, B.WATER, B.GLASS])); if (rng() < 0.5) set(x, y, z, B.AIR); },
  repair(edge) { const [x, z] = borderXZ(edge); exec("repair " + x + " " + (surfaceY(x, z) - ri(20)) + " " + z); },
  probe(edge) { const [x, z] = borderXZ(edge); exec("probe " + x + " " + (surfaceY(x, z) + 1 - ri(30)) + " " + z); },
};
const ANAMES = Object.keys(ACTIONS);
function round(edge) {
  const mode = rng(), nAct = mode < 0.2 ? 2 + ri(4) : 1;
  const player = mode >= 0.2 && mode < 0.45;
  if (player) exec("pbegin");
  for (let i = 0; i < nAct; i++) ACTIONS[pick(ANAMES)](edge && rng() < 0.6);
  if (player) exec("pend");
  exec("light");
}

// ---------- the scenario ----------
const cxs = [], czs = []; for (let x = WIN.cx0; x <= WIN.cx1; x++) cxs.push(x); for (let z = WIN.cz0; z <= WIN.cz1; z++) czs.push(z);
// phase A: load all but the last column, centre-out
const first = []; for (const cz of czs) for (const cx of cxs.slice(0, -1)) first.push([cx, cz]);
const mcx = cxs[(cxs.length - 2) >> 1], mcz = czs[czs.length >> 1];
first.sort((a, b) => (Math.abs(a[0] - mcx) + Math.abs(a[1] - mcz)) - (Math.abs(b[0] - mcx) + Math.abs(b[1] - mcz)) || a[0] - b[0] || a[1] - b[1]);
for (const [cx, cz] of first) exec("gen " + cx + " " + cz);
exec("light");
// phase B: edits on inner borders and on the edge of the loaded area
for (let r = 0; r < ROUNDS; r++) round(r % 3 === 2);
// phase C: load the last column (stitches against edited neighbours), edit across the new borders
for (const cz of czs) exec("gen " + cxs[cxs.length - 1] + " " + cz);
exec("light");
for (let r = 0; r < (ROUNDS >> 1); r++) round(r % 4 === 3);
// phase D: unload chunks with edits and regenerate them (generation worker applies the saved delta; virtual light meta re-queues)
const victims = [...W.chunks.values()].filter((c) => W.editsByChunk.has(c.key)).slice(0, 3);
for (const c of victims) { exec("unload " + c.cx + " " + c.cz); exec("light"); }
for (const c of victims) exec("gen " + c.cx + " " + c.cz);
exec("light");
for (let r = 0; r < 10; r++) round(false);
// final full-column probes
for (let i = 0; i < 20; i++) ACTIONS.probe(false);

fs.writeFileSync(dir + "/scenario.txt", scenario.join("\n") + "\n");
fs.writeFileSync(dir + "/js.txt", outLines.join("\n") + "\n");

// ---------- light rule predicates ----------
const rl = [];
const metas = [null, {}, { waterlogged: true }, { waterlogged: 1 }, { waterlogged: 0 }, { waterlogged: "" }, { waterlogged: "x" }, { berries: true }];
for (let id = 0; id < 256; id++) {
  let s = id + " " + (W.isOpaque(id) ? 1 : 0) + (W.lightStops(id) ? 1 : 0) + (W.lightAttenuation(id) ? 1 : 0) + " " + W.lightSignature(id) + " " + W.lightCost(id);
  for (const m of metas) s += " " + (W.lightStopsState(id, m) ? 1 : 0) + (W.lightAttenuationState(id, m) ? 1 : 0) + ":" + W.lightSignatureState(id, m);
  rl.push(s);
}
for (const vid of [B.VIRTUAL_OPAQUE, B.VIRTUAL_TRANSPARENT, B.VIRTUAL_CUTOUT, B.VIRTUAL_PLANT])
  for (const k of Object.keys(VB).concat(["NO_SUCH_KEY"])) {
    let s = vid + " " + k;
    for (const m of [{ v: k }, { v: k, waterlogged: true }, { v: k, waterlogged: 0 }]) s += " " + (W.lightStopsState(vid, m) ? 1 : 0) + (W.lightAttenuationState(vid, m) ? 1 : 0) + ":" + W.lightSignatureState(vid, m);
    rl.push(s);
  }
fs.writeFileSync(outDir + "/rules_js.txt", rl.join("\n") + "\n");
console.log("js " + winName + ": " + scenario.length + " commands, " + W.chunks.size + " chunks, " + outLines.length + " state lines");

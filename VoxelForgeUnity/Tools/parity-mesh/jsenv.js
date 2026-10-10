// Loads the reference pretty.js in node with every browser API replaced by an inert universal stub and
// returns the real runtime objects the mesher depends on: B, blocks, VIRTUAL_BLOCKS, createSharedMeshCore,
// createWorldGenKernel, plus workerBlockCfg built exactly like initWorkerEngine() does.
"use strict";
const fs = require("fs"), path = require("path");
function load(prettyPath, resDir) {
  const R = resDir || path.resolve(__dirname, "../../Assets/VoxelForge/Resources/VoxelForge") + "/";
  let src = fs.readFileSync(prettyPath, "utf8");
  src = src.replace(/^"use strict";/, "");
  const rep = (tag, val) => { const k = src.indexOf(tag); if (k < 0) throw new Error("placeholder " + tag); const e = src.indexOf(val, k); src = src.slice(0, k) + src.slice(e); };
  const sub = (tag, dflt, text) => { const t = "/*EXTRACTED " + tag + "*/ " + dflt; if (src.indexOf(t) < 0) throw new Error("placeholder " + tag); src = src.replace(t, text); };
  sub("TILE_NAMES.json", "null", fs.readFileSync(R + "tile_names.json", "utf8"));
  sub("VIRTUAL_BLOCKS.json", "null", fs.readFileSync(R + "virtual_blocks.json", "utf8"));
  sub("VIRTUAL_CREATIVE_KEYS.json", "null", fs.readFileSync(R + "virtual_creative_keys.json", "utf8"));
  sub("MAIN_CLIMATE_B64.bin", '""', JSON.stringify(fs.readFileSync(R + "main_climate.bytes").toString("base64")));
  sub("MAIN_SHIPS.json", "null", fs.readFileSync(R + "main_ships.json", "utf8"));
  sub("MAIN_PYRAMID_BLOCKS.json", "null", fs.readFileSync(R + "main_pyramid_blocks.json", "utf8"));
  sub("MAIN_OUTPOST_BLOCKS.json", "null", fs.readFileSync(R + "main_outpost_blocks.json", "utf8"));
  // Universal stub: callable, constructible, every property is the stub again, coerces to 0.
  const stubFn = function () {};
  const stub = new Proxy(stubFn, {
    get(t, k) { if (k === Symbol.toPrimitive) return () => 0; if (k === "then") return undefined; if (k === Symbol.iterator) return function* () {}; if (k === "length") return 0; return stub; },
    set() { return true; }, apply() { return stub; }, construct() { return stub; }, has() { return true; },
  });
  const own = new Map();
  const sandbox = new Proxy({}, {
    has(t, k) { if (typeof k !== "string" || k.startsWith("__")) return false; if (own.has(k)) return true; return !(k in globalThis) || k === "window" || k === "self" || k === "document" || k === "navigator" || k === "location" || k === "localStorage"; },
    get(t, k) { if (k === Symbol.unscopables) return undefined; if (own.has(k)) return own.get(k); return stub; },
    set(t, k, v) { own.set(k, v); return true; },
  });
  const lines = src.split("\n");
  const fnSrc = (name) => { const a = lines.findIndex((l) => l.startsWith("function " + name + "(")); if (a < 0) throw new Error("no " + name); let b = a; while (lines[b] !== "}") b++; return lines.slice(a, b + 1).join("\n"); };
  // Top level is executed only up to BLOCK_SYMBOL_BY_ID (block tables, B, VIRTUAL_BLOCKS are complete and never mutated
  // afterwards; later top-level UI code spins forever on stubs);
  // the two worker-side factories are appended verbatim.
  const cut = process.env.JSENV_CUT ? +process.env.JSENV_CUT : lines.findIndex((l) => l.startsWith("const BLOCK_SYMBOL_BY_ID = "));
  const ga = lines.findIndex((l) => l.startsWith("const GEN_WORKER_SOURCE =")); let gb = ga; while (!lines[gb].startsWith('  "self.onmessage=')) gb++;
  // structureCandidate is exposed on the kernel (test-only addition) so the driver can find structure chunks.
  const kernel = fnSrc("createWorldGenKernel").replace("    generateDense,\n    structureMetaForChunk,", "    generateDense,\n    structureCandidate,\n    structureMetaForChunk,");
  src = lines.slice(0, cut).join("\n") + "\n" + kernel + "\n" + fnSrc("applyChunkDeltaDense") + "\n" + fnSrc("createSharedMeshCore") + "\n" + lines.slice(ga, gb + 1).join("\n") + "\n";
  const tail = "\n;__out.B=B;__out.blocks=blocks;__out.VIRTUAL_BLOCKS=VIRTUAL_BLOCKS;__out.createSharedMeshCore=createSharedMeshCore;__out.createWorldGenKernel=createWorldGenKernel;__out.TILE_NAMES=TILE_NAMES;__out.GEN_WORKER_SOURCE=GEN_WORKER_SOURCE;";
  const out = {};
  // Run the whole top level; a late browser-only failure is tolerated as long as the block tables are complete.
  const body = "with(__sb){try{" + src + tail + "}catch(e){__out.err=e;try{" + tail + "}catch(e2){}}}";
  if (process.env.JSENV_DUMP) fs.writeFileSync(process.env.JSENV_DUMP, JSON.stringify({ body, meshFn: fnSrc("createSharedMeshCore"), meshLine: lines.findIndex((l) => l.startsWith("function createSharedMeshCore(")) + 1 }));
  new Function("__sb", "__out", body)(sandbox, out);
  if (!out.blocks) throw out.err || new Error("pretty.js did not initialize");
  // initWorkerEngine(): workerBlockCfg mapping.
  out.workerBlockCfg = out.blocks.map((b) => b ? { side: b.side, top: b.top, bottom: b.bottom, front: b.front, frontL: b.frontL, frontR: b.frontR, topL: b.topL, topR: b.topR, corner: b.corner,
    solid: !!b.solid, transparent: !!b.transparent, alpha: b.alpha ?? 1, cutout: !!b.cutout, plant: !!b.plant, waterPlant: !!b.waterPlant, needsWater: !!b.needsWater, special: b.special || null, light: b.light || 0 } : null);
  return out;
}
module.exports = { load };
if (require.main === module) {
  const o = load(process.argv[2]);
  console.log("err:", o.err && String(o.err).slice(0, 300));
  console.log("blocks:", o.blocks.length, "nonnull:", o.blocks.filter(Boolean).length, "B keys:", Object.keys(o.B).length, "vblocks:", Object.keys(o.VIRTUAL_BLOCKS).length);
}

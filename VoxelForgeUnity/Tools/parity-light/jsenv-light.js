// Loads the reference pretty.js in node (browser APIs replaced by an inert stub) and returns the real main-thread
// light engine together with the world storage it works on: chunk maps, block/meta lookup, section helpers,
// queueLightUpdate/processLightDirty/updateBlockLightAt/updateSkyLightAt/repairLightAt/stitchChunkLight,
// the player-edit light path and the light rule predicates. All code is the original JS, executed verbatim.
// After loading, the sandbox is switched to strict mode: any lookup that would fall through to the stub throws,
// so the light code can never silently run against a stubbed dependency.
"use strict";
const fs = require("fs"), path = require("path");
function load(prettyPath, resDir) {
  const R = resDir || path.resolve(__dirname, "../../Assets/VoxelForge/Resources/VoxelForge") + "/";
  let src = fs.readFileSync(prettyPath, "utf8");
  src = src.replace(/^"use strict";/, "");
  const sub = (tag, dflt, text) => { const t = "/*EXTRACTED " + tag + "*/ " + dflt; if (src.indexOf(t) < 0) throw new Error("placeholder " + tag); src = src.replace(t, text); };
  sub("TILE_NAMES.json", "null", fs.readFileSync(R + "tile_names.json", "utf8"));
  sub("VIRTUAL_BLOCKS.json", "null", fs.readFileSync(R + "virtual_blocks.json", "utf8"));
  sub("VIRTUAL_CREATIVE_KEYS.json", "null", fs.readFileSync(R + "virtual_creative_keys.json", "utf8"));
  sub("MAIN_CLIMATE_B64.bin", '""', JSON.stringify(fs.readFileSync(R + "main_climate.bytes").toString("base64")));
  sub("MAIN_SHIPS.json", "null", fs.readFileSync(R + "main_ships.json", "utf8"));
  sub("MAIN_PYRAMID_BLOCKS.json", "null", fs.readFileSync(R + "main_pyramid_blocks.json", "utf8"));
  sub("MAIN_OUTPOST_BLOCKS.json", "null", fs.readFileSync(R + "main_outpost_blocks.json", "utf8"));
  let strict = false;
  const stubFn = function () {};
  const stub = new Proxy(stubFn, {
    get(t, k) { if (k === Symbol.toPrimitive) return () => 0; if (k === "then") return undefined; if (k === Symbol.iterator) return function* () {}; if (k === "length") return 0; return stub; },
    set() { return true; }, apply() { return stub; }, construct() { return stub; }, has() { return true; },
  });
  const own = new Map();
  const sandbox = new Proxy({}, {
    has(t, k) { if (typeof k !== "string" || k.startsWith("__")) return false; if (own.has(k)) return true; return !(k in globalThis) || k === "window" || k === "self" || k === "document" || k === "navigator" || k === "location" || k === "localStorage"; },
    get(t, k) { if (k === Symbol.unscopables) return undefined; if (own.has(k)) return own.get(k); if (strict) throw new Error("light code touched stubbed global '" + String(k) + "'"); return stub; },
    set(t, k, v) { own.set(k, v); return true; },
  });
  const lines = src.split("\n");
  const fnSrc = (name) => { const a = lines.findIndex((l) => l.startsWith("function " + name + "(")); if (a < 0) throw new Error("no " + name); let b = a; while (lines[b] !== "}") b++; return lines.slice(a, b + 1).join("\n"); };
  // Top level up to BLOCK_SYMBOL_BY_ID: block tables, chunk storage, metadata maps, the whole light engine (5659-6140).
  const cut = lines.findIndex((l) => l.startsWith("const BLOCK_SYMBOL_BY_ID = "));
  const lightFnsBefore = ["getPackedLightWorld", "processLightDirty", "stitchChunkLight", "flushImmediatePlayerEditLighting", "buildInitialPackedLightDense"];
  for (const f of lightFnsBefore) { const i = lines.findIndex((l) => l.startsWith("function " + f + "(")); if (i < 0 || i >= cut) throw new Error(f + " is not inside the executed prefix"); }
  // Light predicates live later in the file (deterministic voxel light block, axisLogIdSync); appended verbatim.
  const late = ["axisLogIdSync", "isOpaque", "lightStops", "lightAttenuation", "lightSignature", "lightStopsState", "lightAttenuationState", "lightSignatureState", "lightStopsAt", "lightCostAt"];
  const ga = lines.findIndex((l) => l.startsWith("const GEN_WORKER_SOURCE =")); let gb = ga; while (!lines[gb].startsWith('  "self.onmessage=')) gb++;
  src = lines.slice(0, cut).join("\n") + "\n" + late.map(fnSrc).join("\n") + "\n" + fnSrc("createWorldGenKernel") + "\n" + fnSrc("applyChunkDeltaDense") + "\n" + lines.slice(ga, gb + 1).join("\n") + "\n";
  const tail = `
;__out.B=B;__out.blocks=blocks;__out.VIRTUAL_BLOCKS=VIRTUAL_BLOCKS;__out.GEN_WORKER_SOURCE=GEN_WORKER_SOURCE;
__out.W={chunks,edits,editsByChunk,blockMeta,metaByChunk,generatedMetaByChunk,lightDirtyKeys,
 chunkFastGet,chunkFastSet,chunkFastDelete,ckey,key3,metaLocalKey,emptySections,chunkGet,chunkSet,getBlock,getBlockMeta,
 installGeneratedMeta,addEditIndex,chunkEditsArray,virtualDefFromMeta,isVirtualId,
 getPackedLightWorld,setPackedLightWorld,queueLightUpdate,processLightDirty,lightDirtyCount,updateBlockLightAt,updateSkyLightAt,
 repairLightAt,stitchChunkLight,markLightDirtyChunks,directSkyColumn,blockEmissionAt,buildInitialPackedLightDense,
 notePlayerEditLight,flushImmediatePlayerEditLighting,
 setPlayerEditDepth(v){playerEditDepth=v;},getPlayerEditDepth(){return playerEditDepth;},resetPlayerEditLightN(){playerEditLightN=0;},
 isOpaque,lightStops,lightAttenuation,lightSignature,lightStopsState,lightAttenuationState,lightSignatureState,lightStopsAt,lightCostAt,lightCost,
 WORLD_MIN_Y,WORLD_MAX_Y,WORLD_H,CHUNK,SECTION_COUNT};`;
  const out = {};
  const body = "with(__sb){try{" + src + tail + "}catch(e){__out.err=e;try{" + tail + "}catch(e2){__out.err2=e2}}}";
  new Function("__sb", "__out", body)(sandbox, out);
  if (!out.W) throw out.err2 || out.err || new Error("pretty.js did not initialize");
  if (out.err) throw new Error("pretty.js top level failed (light engine may be in TDZ): " + out.err);
  out.workerBlockCfg = out.blocks.map((b) => b ? { side: b.side, top: b.top, bottom: b.bottom, front: b.front, frontL: b.frontL, frontR: b.frontR, topL: b.topL, topR: b.topR, corner: b.corner,
    solid: !!b.solid, transparent: !!b.transparent, alpha: b.alpha ?? 1, cutout: !!b.cutout, plant: !!b.plant, waterPlant: !!b.waterPlant, needsWater: !!b.needsWater, special: b.special || null, light: b.light || 0 } : null);
  out.setStrict = (v) => { strict = !!v; };
  return out;
}
module.exports = { load };

// Reports which parts of the JS light engine the scenarios never executed (V8 precise block coverage).
// Usage: node cover.js <pretty.js> <outDir>   after light-js.js ran with NODE_V8_COVERAGE=<outDir>/cov JSENV_DUMP=<outDir>/jsenv.json
"use strict";
const fs = require("fs");
const [prettyPath, dir] = process.argv.slice(2);
const pretty = fs.readFileSync(prettyPath, "utf8").split("\n"), dump = JSON.parse(fs.readFileSync(dir + "/jsenv.json", "utf8"));
const src = "(function anonymous(__sb,__out\n) {\n" + dump.body + "\n})";
const FNS = ["getPackedLightWorld", "noteLightDirty", "setPackedLightWorld", "skyAt", "blockAtLight", "setSkyAt", "setBlockAtLight", "lightCost", "blockEmissionAt", "queueLightUpdate",
  "markLightDirtyChunks", "directSkyColumn", "encodeLightPos", "seedLight", "propagateLightIncrease", "updateBlockLightAt", "updateSkyLightAt", "repairLightAt", "lightDirtyCount",
  "takeLightDirty", "processLightDirty", "lightNeighborhoodTopY", "chunkPackedLocal", "setChunkPackedLocal", "stitchChunkLight", "notePlayerEditLight", "flushImmediatePlayerEditLighting",
  "buildInitialPackedLightDense", "ensureSectionLight", "axisLogIdSync", "isOpaque", "lightStops", "lightAttenuation", "lightSignature", "lightStopsState", "lightAttenuationState",
  "lightSignatureState", "lightStopsAt", "lightCostAt"];
const scripts = [];
for (const f of fs.readdirSync(dir + "/cov")) { const j = JSON.parse(fs.readFileSync(dir + "/cov/" + f, "utf8")); for (const s of j.result) if (s.functions.some((fn) => fn.functionName === "processLightDirty")) scripts.push(s); }
if (!scripts.length) throw new Error("no coverage for the light engine");
let total = 0, unhitLines = 0;
for (const name of FNS) {
  const a = pretty.findIndex((l) => l.startsWith("function " + name + "(")); let b = a; while (pretty[b] !== "}") b++;
  const text = pretty.slice(a, b + 1).join("\n"), at = src.indexOf(text), end = at + text.length;
  if (at < 0) throw new Error("source of " + name + " not found in the executed body");
  const cnt = new Int32Array(text.length).fill(-1), merged = new Int32Array(text.length);
  for (const script of scripts) {
    cnt.fill(-1);
    const ranges = [];
    for (const fn of script.functions) for (const r of fn.ranges) if (r.endOffset > at && r.startOffset < end) ranges.push(r);
    ranges.sort((x, y) => (y.endOffset - y.startOffset) - (x.endOffset - x.startOffset));
    for (const r of ranges) for (let k = Math.max(r.startOffset, at); k < Math.min(r.endOffset, end); k++) cnt[k - at] = r.count;
    for (let k = 0; k < text.length; k++) merged[k] += Math.max(0, cnt[k]);
  }
  const unhit = new Map(); let ln = a + 1;
  for (let k = 0; k < text.length; k++) { if (text.charCodeAt(k) === 10) { ln++; continue; } if (merged[k] === 0 && !/\s/.test(text[k])) unhit.set(ln, (unhit.get(ln) || "") + text[k]); }
  total += b - a + 1; unhitLines += unhit.size;
  for (const [l, t] of unhit) console.log("  " + name + "  pretty.js:" + l + "  " + t.slice(0, 110));
}
console.log(`light engine: ${FNS.length} functions, ${total} lines, ${unhitLines} lines with never-executed code`);

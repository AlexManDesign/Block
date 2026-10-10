// Reports which parts of the JS createSharedMeshCore() the job set never executed (V8 precise block coverage).
// Usage: node cover.js <outDir>   (after gen-jobs.js ran with NODE_V8_COVERAGE=<outDir>/cov JSENV_DUMP=<outDir>/jsenv.json)
"use strict";
const fs = require("fs");
const dir = process.argv[2], dump = JSON.parse(fs.readFileSync(dir + "/jsenv.json", "utf8"));
const src = "(function anonymous(__sb,__out\n) {\n" + dump.body + "\n})";
const meshAt = src.indexOf(dump.meshFn), meshEnd = meshAt + dump.meshFn.length;
if (meshAt < 0) throw new Error("mesher source not found");
const lnArr = new Int32Array(meshEnd - meshAt); for (let k = meshAt, n = 0; k < meshEnd; k++) { lnArr[k - meshAt] = n; if (src.charCodeAt(k) === 10) n++; }
const lineOf = (off) => dump.meshLine + lnArr[off - meshAt];
let script = null;
for (const f of fs.readdirSync(dir + "/cov")) {
  const j = JSON.parse(fs.readFileSync(dir + "/cov/" + f, "utf8"));
  for (const s of j.result) if (s.functions.some((fn) => fn.functionName === "createSharedMeshCore")) script = s;
}
if (!script) throw new Error("no coverage for createSharedMeshCore");
// innermost-range-wins count per character of the mesher text
const cnt = new Int32Array(meshEnd - meshAt).fill(-1);
const ranges = [];
for (const fn of script.functions) for (const r of fn.ranges) if (r.endOffset > meshAt && r.startOffset < meshEnd) ranges.push(r);
ranges.sort((a, b) => (b.endOffset - b.startOffset) - (a.endOffset - a.startOffset));
for (const r of ranges) for (let k = Math.max(r.startOffset, meshAt); k < Math.min(r.endOffset, meshEnd); k++) cnt[k - meshAt] = r.count;
const unhit = new Map();
for (let k = 0; k < cnt.length; k++) if (cnt[k] === 0 && !/\s/.test(src[meshAt + k])) { const ln = lineOf(meshAt + k); unhit.set(ln, (unhit.get(ln) || "") + src[meshAt + k]); }
const lines = [...unhit.keys()].sort((a, b) => a - b);
const total = dump.meshFn.split("\n").length;
console.log(`mesher: ${total} lines, ${lines.length} lines with never-executed code`);
for (const ln of lines) console.log("  pretty.js:" + ln + "  " + unhit.get(ln).slice(0, 110));

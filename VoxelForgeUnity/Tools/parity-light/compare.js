// Compares the JS and C# light parity outputs.
//   node compare.js <outDir> <win>                state traces js.txt vs cs.txt; prints the first differences and
//                                                 "FIRST_STEP=<n>" (the step whose state differs first), exit 1 on mismatch
//   node compare.js --rules <outDir>              rules_js.txt vs rules_cs.txt (as sets of lines)
//   node compare.js --dump <outDir> <win> <step>  voxel-level diff of js_dump_<step>.bin / cs_dump_<step>.bin
"use strict";
const fs = require("fs");
const a = process.argv.slice(2);
if (a[0] === "--rules") {
  const r = (f) => fs.readFileSync(a[1] + "/" + f, "utf8").split("\n").filter(Boolean);
  const js = r("rules_js.txt"), cs = r("rules_cs.txt"), sc = new Set(cs), sj = new Set(js);
  const onlyJ = js.filter((l) => !sc.has(l)), onlyC = cs.filter((l) => !sj.has(l));
  if (!onlyJ.length && !onlyC.length && js.length === cs.length) { console.log("rules: " + js.length + "/" + js.length + " identical"); process.exit(0); }
  console.log("rules: MISMATCH (" + onlyJ.length + " js-only, " + onlyC.length + " cs-only)");
  for (const l of onlyJ.slice(0, 15)) console.log("  js: " + l);
  for (const l of onlyC.slice(0, 15)) console.log("  cs: " + l);
  process.exit(1);
}
if (a[0] === "--dump") {
  const [dir, win, step] = a.slice(1), rd = (p) => fs.readFileSync(dir + "/" + win + "/" + p + "_dump_" + step + ".bin");
  const parse = (b) => { const m = new Map(); let o = 0; while (o < b.length) { const cx = b.readInt32LE(o), cz = b.readInt32LE(o + 4); o += 8; const secs = [];
    for (let si = 0; si < 24; si++) { const p = b[o++]; if (p) { secs.push({ blocks: b.subarray(o, o + 4096), light: b.subarray(o + 4096, o + 8192) }); o += 8192; } else secs.push(null); }
    m.set(cx + "," + cz, { cx, cz, secs }); } return m; };
  const J = parse(rd("js")), C = parse(rd("cs")); let n = 0;
  for (const [k, cj] of J) {
    const cc = C.get(k); if (!cc) { console.log("chunk " + k + " missing in C#"); n++; continue; }
    for (let si = 0; si < 24; si++) {
      const sj = cj.secs[si], sc = cc.secs[si];
      if (!sj !== !sc) { console.log("chunk " + k + " si " + si + ": section " + (sj ? "present only in JS" : "present only in C#")); n++; continue; }
      if (!sj) continue;
      for (let i = 0; i < 4096; i++) if (sj.blocks[i] !== sc.blocks[i] || sj.light[i] !== sc.light[i]) {
        if (++n > 40) continue;
        const lx = i & 15, lz = (i >> 4) & 15, ly = i >> 8, x = cj.cx * 16 + lx, z = cj.cz * 16 + lz, y = -64 + si * 16 + ly;
        console.log(`  (${x},${y},${z}) block js=${sj.blocks[i]} cs=${sc.blocks[i]}  sky js=${sj.light[i] >> 4} cs=${sc.light[i] >> 4}  blk js=${sj.light[i] & 15} cs=${sc.light[i] & 15}`);
      }
    }
  }
  for (const k of C.keys()) if (!J.has(k)) { console.log("chunk " + k + " only in C#"); n++; }
  console.log(n ? "dump step " + step + ": " + n + " differing voxels/sections" : "dump step " + step + ": identical");
  process.exit(n ? 1 : 0);
}
const [dir, win] = a, rd = (f) => fs.readFileSync(dir + "/" + win + "/" + f, "utf8").split("\n");
const js = rd("js.txt"), cs = rd("cs.txt");
let firstStep = -1, shown = 0, lastCmd = "", nDiff = 0, step = 0;
const len = Math.max(js.length, cs.length);
for (let i = 0; i < len; i++) {
  const lj = js[i], lc = cs[i];
  if (lj !== undefined && lj.startsWith("C ")) { lastCmd = lj; step = +lj.split(" ")[1]; }
  if (lj === lc) continue;
  nDiff++;
  if (firstStep < 0) firstStep = step;
  if (shown++ < 6) {
    console.log("after " + lastCmd);
    console.log("  js: " + (lj === undefined ? "<eof>" : lj.slice(0, 400)));
    console.log("  cs: " + (lc === undefined ? "<eof>" : lc.slice(0, 400)));
    if (lj && lc && lj.split(" ").length > 3) { // point at differing section tokens
      const tj = lj.split(" "), tc = lc.split(" "), d = [];
      for (let t = 0; t < Math.max(tj.length, tc.length); t++) if (tj[t] !== tc[t]) d.push((tj[t] || "-") + " vs " + (tc[t] || "-"));
      console.log("  tokens: " + d.slice(0, 8).join(" | "));
    }
  }
}
const nState = js.filter((l) => l.startsWith("S ")).length, nCmd = js.filter((l) => l.startsWith("C ")).length;
if (!nDiff) { console.log(win + ": " + nCmd + " commands, " + nState + " full-world light snapshots, " + js.length + " lines: identical"); process.exit(0); }
console.log(win + ": MISMATCH, " + nDiff + " differing lines; FIRST_STEP=" + firstStep);
process.exit(1);

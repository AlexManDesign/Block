// Compares every js_* output with its cs_* twin in <outDir>; prints per-kind counts and the first differences.
'use strict';
const fs = require('fs'), path = require('path');
const dir = process.argv[2], show = +(process.argv[3] || 8);
const kinds = {}; let diffs = 0, shown = 0;
for (const f of fs.readdirSync(dir).filter(f => f.startsWith('js_')).sort()) {
  const ext = path.extname(f).slice(1), cs = path.join(dir, 'cs_' + f.slice(3));
  const k = kinds[ext] || (kinds[ext] = { files: 0, same: 0, units: 0 });
  k.files++;
  if (!fs.existsSync(cs)) { diffs++; if (shown++ < show) console.log('MISSING ' + cs); continue; }
  const A = fs.readFileSync(path.join(dir, f)), Bf = fs.readFileSync(cs);
  k.units += ext === 'bin' ? A.length : A.toString().split('\n').length - 1;
  if (A.equals(Bf)) { k.same++; continue; }
  diffs++;
  if (shown++ >= show) continue;
  if (ext === 'bin') {
    let i = 0; while (i < A.length && A[i] === Bf[i]) i++;
    console.log(`DIFF ${f}: byte ${i} js=${A[i]} cs=${Bf[i]} (sizes ${A.length}/${Bf.length})`);
  } else {
    const la = A.toString().split('\n'), lb = Bf.toString().split('\n'); let i = 0; while (i < la.length && la[i] === lb[i]) i++;
    console.log(`DIFF ${f}: line ${i + 1}\n  js: ${String(la[i]).slice(0, 400)}\n  cs: ${String(lb[i]).slice(0, 400)}`);
  }
}
for (const [e, k] of Object.entries(kinds)) console.log(`${e.padEnd(5)} ${k.same}/${k.files} files identical (${k.units} ${e === 'bin' ? 'bytes' : 'lines'})`);
console.log(diffs === 0 ? 'worldgen parity: OK' : `worldgen parity: FAIL (${diffs} files differ)`);
process.exitCode = diffs ? 1 : 0;

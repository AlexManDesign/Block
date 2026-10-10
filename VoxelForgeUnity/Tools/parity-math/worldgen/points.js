// Deterministic chunk / point lists for the worldgen parity run.
//   node points.js chunks <seed> <structList> <outList> [far]   origin 3x3 + random near/far chunks + structure chunks
//   node points.js pts    <seed> <count> <outFile>             random x y z points (near and far), plus fixed regression points
'use strict';
const fs = require('fs');
const [, , MODE, SEED, A1, A2, A3] = process.argv;
let s = (parseInt(SEED) ^ 0x2545F491) >>> 0;
const rnd = () => { s = (s + 0x6D2B79F5) >>> 0; let t = s; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; };
const ri = (a, b) => a + Math.floor(rnd() * (b - a + 1));
// regression point: the 1-ulp dryAt difference found between Node and Mono libm (seed -5)
const FIXED_PTS = [[-45791, 70, -9148], [-45791, -20, -9148], [-45790, 64, -9148], [0, 64, 0]];
if (MODE === 'chunks') {
  const out = new Set(), far = A3 ? parseInt(A3) : 15;
  for (let x = -1; x <= 1; x++) for (let z = -1; z <= 1; z++) out.add(x + ' ' + z);
  for (const p of FIXED_PTS) out.add((p[0] >> 4) + ' ' + (p[2] >> 4));
  for (let i = 0; i < 30; i++) out.add(ri(-400, 400) + ' ' + ri(-400, 400));
  for (let i = 0; i < far; i++) out.add(ri(-1500000, 1500000) + ' ' + ri(-1500000, 1500000));
  for (const l of fs.readFileSync(A1, 'utf8').trim().split('\n')) if (l.trim()) out.add(l.trim());
  fs.writeFileSync(A2, [...out].join('\n') + '\n');
} else if (MODE === 'pts') {
  const n = parseInt(A1), o = FIXED_PTS.map(p => p.join(' '));
  for (let i = 0; i < n; i++) { const R = i % 3 === 0 ? 24000000 : i % 3 === 1 ? 60000 : 3000; o.push(ri(-R, R) + ' ' + ri(-64, 319) + ' ' + ri(-R, R)); }
  fs.writeFileSync(A2, o.join('\n') + '\n');
} else throw new Error('mode');

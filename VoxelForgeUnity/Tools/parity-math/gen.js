// Emits test vectors for JsMath: one line per sample, all values as 16-digit hex IEEE-754 bit patterns:
//   <fn> <a> [<b> [<c>]] <expected>
// Expected results come from this node's V8 (Math.*). Inputs: specials, random bit patterns, uniform ranges,
// subnormals, huge (Payne-Hanek) arguments, near multiples of pi/2, integer exponents, overflow/underflow edges,
// and the exact argument sets / ranges the world generator uses.
// Usage: node gen.js [--n=5000000] [--fns=sin,cos,...] [--seed=1] > vectors.txt   (or pipe into MathParity.exe)
'use strict';
const v8 = require('v8');
v8.setFlagsFromString('--allow-natives-syntax');
const args = Object.fromEntries(process.argv.slice(2).map(a => { const m = /^--([^=]+)=?(.*)$/.exec(a); return m ? [m[1], m[2]] : [a, '']; }));
const N = +(args.n || 5000000), SEED = +(args.seed || 1) >>> 0;
const ALL = ['sin', 'cos', 'tan', 'log', 'log10', 'atan', 'pow', 'atan2', 'hypot2', 'hypot3'];
const FNS = args.fns ? args.fns.split(',') : ALL;
const IMPL = { sin: Math.sin, cos: Math.cos, tan: Math.tan, log: Math.log, log10: Math.log10, atan: Math.atan, pow: Math.pow, atan2: Math.atan2,
  hypot2: (a, b) => Math.hypot(a, b), hypot3: (a, b, c) => Math.hypot(a, b, c) };
const ARITY = { sin: 1, cos: 1, tan: 1, log: 1, log10: 1, atan: 1, pow: 2, atan2: 2, hypot2: 2, hypot3: 3 };

// ---- bits helpers
const F = new Float64Array(1), U = new Uint32Array(F.buffer);
const fromBits = (hi, lo) => { U[1] = hi >>> 0; U[0] = lo >>> 0; return F[0]; };
const hiOf = v => { F[0] = v; return U[1]; };
const nextUp = (v, k) => { F[0] = v; let lo = U[0] + k, hi = U[1]; const c = Math.floor(lo / 4294967296); lo -= c * 4294967296; return fromBits(hi + c, lo); }; // k ulps along the bit pattern
// ---- deterministic PRNG (mulberry32 x2)
let s1 = SEED ^ 0x9E3779B9, s2 = (SEED * 2654435761) ^ 0x85EBCA6B;
function u32() { s1 = (s1 + 0x6D2B79F5) | 0; let t = s1; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return (t ^ (t >>> 14)) >>> 0; }
function u32b() { s2 = (s2 + 0x6D2B79F5) | 0; let t = s2; t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return (t ^ (t >>> 14)) >>> 0; }
const rnd = () => (u32() * 4294967296 + u32b()) / 18446744073709551616; // [0,1) with 53+ bits
const uni = (a, b) => a + (b - a) * rnd();
const ri = (a, b) => a + Math.floor(rnd() * (b - a + 1));
const anyBits = () => fromBits(u32(), u32b());
const finiteBits = () => { for (;;) { const v = anyBits(); if (isFinite(v)) return v; } };
const subnormal = () => fromBits((u32() & 0x800FFFFF) >>> 0, u32b());
const expUni = (emin, emax) => { const e = ri(emin, emax); const m = 1 + rnd(); const s = u32() & 1 ? -1 : 1; return s * m * Math.pow(2, e); };
const nearPio2 = kmax => { const k = Math.floor(rnd() * kmax) * (u32() & 1 ? -1 : 1); return nextUp(k * (Math.PI / 2), ri(-12, 12)); };
const SPECIAL = [0, -0, Infinity, -Infinity, NaN, 1, -1, 2, -2, 0.5, -0.5, 3, -3, 10, 0.1, 1e-300, -1e-300, 1e300, -1e300,
  Number.MIN_VALUE, -Number.MIN_VALUE, Number.MAX_VALUE, -Number.MAX_VALUE, 2.2250738585072014e-308, 2.225073858507201e-308,
  Math.PI, -Math.PI, Math.PI / 2, -Math.PI / 2, Math.PI / 4, -Math.PI / 4, 3 * Math.PI / 4, 2 ** 19 * Math.PI / 2, 2 ** 20, 2 ** 31, 2 ** 52, 2 ** 53,
  2 ** 63, 2 ** 64, 2 ** 66, 2 ** 1023, 2 ** -1022, 2 ** -1074, 0.4375, 0.6875, 1.1875, 2.4375, 0.3, 0.78125, 0.6744, 1 + 2 ** -20, 1 - 2 ** -20,
  1 + 2 ** -52, 1 - 2 ** -53, 1.4, 1.15, 1e6 - 0.01, 0.99, 1024, -1075, 1074, 709.78, -745.13, 1e-10, 7, -7, 0.75];
// boundary bit patterns used by the fdlibm branch tests (hi words), each with lo = 0 / all ones / random
const HIWORDS = [0x3FE921FB, 0x4002D97C, 0x413921FB, 0x3E400000, 0x3E300000, 0x3FD33333, 0x3FE90000, 0x3FE59428, 0x3FF921FB, 0x400921FB,
  0x4012D97C, 0x401921FB, 0x404921FB, 0x44100000, 0x3FDC0000, 0x3FE60000, 0x3FF30000, 0x40038000, 0x00100000, 0x7FEFFFFF, 0x3FEFFFFF, 0x3FF00000,
  0x41E00000, 0x43F00000, 0x43400000, 0x40900000, 0x4090CC00, 0x3FE00000, 0x0006147A, 0x3FF6147A, 0x3FF6B851, 0x3FF95F64];
const edges = [];
for (const h of HIWORDS) for (const sgn of [0, 0x80000000]) for (const lo of [0, 1, 0xFFFFFFFF, 0x54442D18, 0x80000000]) for (const d of [-1, 0, 1]) edges.push(fromBits((h + d) | sgn, lo));
const SPEC = SPECIAL.concat(edges);

// ---- the exact worldgen argument sets
const ringAng = []; for (let i = 0; i < 8; i++) ringAng.push((i * Math.PI) / 4);
const ravineP = []; for (let steps = 2; steps <= 84; steps++) for (let i = 0; i < steps; i++) ravineP.push((i / (steps - 1)) * Math.PI);
const groupLog = []; for (let n = 1; n <= 20000; n++) groupLog.push(n - 0.01);
// ravine heading walk: ang = rng()*2pi, then += (rng()-0.5)*0.55 per step (<= 84 steps)
const ravineAng = () => { let a = rnd() * Math.PI * 2; const st = ri(0, 84); for (let i = 0; i < st; i++) a += (rnd() - 0.5) * 0.55; return a; };

const trigGens = [anyBits, finiteBits, () => uni(-1, 1), () => uni(-Math.PI * 4, Math.PI * 4), () => uni(-1e3, 1e3), () => uni(-1e6, 1e6), () => uni(-1e9, 1e9),
  () => expUni(-1074, 1023), () => expUni(-30, 30), () => expUni(19, 1023), subnormal, () => nearPio2(64), () => nearPio2(1 << 20), () => nearPio2(2 ** 40),
  () => ri(-1e6, 1e6), () => ravineAng(), () => rnd() * Math.PI, () => uni(-0.8, 0.8)];
const logGens = [anyBits, finiteBits, () => uni(0, 2), () => uni(0, 1e6), () => expUni(-1074, 1023), subnormal, () => Math.abs(subnormal()),
  () => 1 + uni(-(2 ** -19), 2 ** -19), () => nextUp(1, ri(-40, 40)), () => ri(1, 1e7) - 0.01, () => ri(1, 1e9), () => Math.abs(expUni(-1074, 1023)), () => 10 ** ri(-320, 308)];
const atanGens = [anyBits, finiteBits, () => uni(-3, 3), () => uni(-1e3, 1e3), () => expUni(-1074, 1023), () => expUni(-30, 70), subnormal,
  () => nextUp([0.4375, 0.6875, 1.1875, 2.4375, 2 ** 66, 2 ** -27][ri(0, 5)] * (u32() & 1 ? -1 : 1), ri(-20, 20))];
const pick = a => a[ri(0, a.length - 1)];
const powGens = [
  () => [anyBits(), anyBits()],
  () => [finiteBits(), finiteBits()],
  () => [rnd(), 1.4], () => [uni(0, 1.6), 1.15], () => [rnd(), uni(0, 3)],
  () => [expUni(-10, 10) * 1, uni(-100, 100)].map((v, i) => i ? v : Math.abs(v)),
  () => [Math.abs(expUni(-1074, 1023)), uni(-2, 2)],
  () => [expUni(-20, 20), ri(-1100, 1100)],                       // negative / positive bases with integer exponents
  () => [-Math.abs(expUni(-5, 5)), ri(-(2 ** 53), 2 ** 53)],       // huge integer exponents (k > 20 branch)
  () => [-Math.abs(expUni(-5, 5)), ri(-60, 60) + (u32() & 1 ? 0.5 : 0)],
  () => [ri(-20, 20), ri(-40, 40)],
  () => [10, ri(-400, 400)],
  () => [nextUp(1, ri(-(2 ** 20), 2 ** 20)) , uni(2 ** 31, 2 ** 64) * (u32() & 1 ? -1 : 1)], // |y| > 2^31 with x near 1
  () => [1 + uni(-(2 ** -19), 2 ** -19), expUni(31, 70)],
  () => { const y = expUni(-3, 12), t = (u32() & 1 ? uni(1022, 1025) : uni(-1078, -1070)); return [Math.pow(2, t / y), y]; },  // o/uflow edges
  () => { const y = expUni(-3, 12), t = (u32() & 1 ? 1024 : -1075); return [nextUp(Math.pow(2, t / y), ri(-3, 3)), y]; },
  () => [subnormal(), uni(-2, 2)],
  () => [Math.abs(subnormal()), uni(0, 1.2)],
  () => [pick(SPEC), pick(SPEC)],
  () => [pick(SPEC), anyBits()], () => [anyBits(), pick(SPEC)],
];
const atan2Gens = [() => [anyBits(), anyBits()], () => [finiteBits(), finiteBits()], () => [uni(-10, 10), uni(-10, 10)],
  () => [expUni(-1074, 1023), expUni(-1074, 1023)], () => [expUni(-40, 40), expUni(-40, 40)], () => [pick(SPEC), pick(SPEC)],
  () => [pick(SPEC), anyBits()], () => [anyBits(), pick(SPEC)], () => [uni(-1e3, 1e3), 1], () => [subnormal(), subnormal()]];
const hyGen = () => { switch (ri(0, 6)) { case 0: return anyBits(); case 1: return uni(-1e3, 1e3); case 2: return expUni(-1074, 1023); case 3: return pick(SPEC);
  case 4: return uni(-1, 1); case 5: return subnormal(); default: return expUni(-30, 30); } };
const hy2Gens = [() => [hyGen(), hyGen()]], hy3Gens = [() => [hyGen(), hyGen(), hyGen()]];
const GENS = { sin: trigGens, cos: trigGens, tan: trigGens, log: logGens, log10: logGens, atan: atanGens, pow: powGens, atan2: atan2Gens, hypot2: hy2Gens, hypot3: hy3Gens };
const FIXED = {
  sin: SPEC.concat(ringAng, ravineP), cos: SPEC.concat(ringAng, ravineP), tan: SPEC.concat(ringAng), log: SPEC.concat(groupLog), log10: SPEC.concat(groupLog), atan: SPEC,
  pow: [].concat(...SPEC.map(a => SPEC.map(b => [a, b]))), atan2: [].concat(...SPEC.map(a => SPEC.map(b => [a, b]))),
  hypot2: [].concat(...SPEC.map(a => SPEC.map(b => [a, b]))), hypot3: [],
};

// ---- output (hex written straight into a byte buffer)
const HEX = Buffer.from('0123456789abcdef');
const BUF = Buffer.allocUnsafe(1 << 22); let off = 0;
function flush() { if (off) { require('fs').writeSync(1, BUF, 0, off); off = 0; } }
function putHex(v) { F[0] = v; let w = U[1]; for (let i = 7; i >= 0; i--) { BUF[off + i] = HEX[w & 15]; w >>>= 4; } w = U[0]; for (let i = 15; i >= 8; i--) { BUF[off + i] = HEX[w & 15]; w >>>= 4; } off += 16; }
function putStr(s) { for (let i = 0; i < s.length; i++) BUF[off++] = s.charCodeAt(i); }
function emit(name, inp, r) { if (off > BUF.length - 128) flush(); putStr(name); for (let i = 0; i < inp.length; i++) { BUF[off++] = 32; putHex(inp[i]); } BUF[off++] = 32; putHex(r); BUF[off++] = 10; }

// interpreter-only twins: the bulk loop below is TurboFan-compiled, these stay in Ignition, so both tiers are checked
const cold = {}; for (const k of ALL) { cold[k] = new Function('f', 'return function(a,b,c){return f(a,b,c)}')(IMPL[k]); new Function('g', '%NeverOptimizeFunction(g)')(cold[k]); }
let tierMismatch = 0;
const same = (x, y) => (x !== x && y !== y) || Object.is(x, y);
for (const name of FNS) {
  const f = IMPL[name], ar = ARITY[name], gens = GENS[name], fixed = FIXED[name];
  if (!f) throw new Error('unknown fn ' + name);
  let count = 0;
  for (const v of fixed) { const inp = ar === 1 ? [v] : v; const r = f(inp[0], inp[1], inp[2]); if (!same(r, cold[name](inp[0], inp[1], inp[2]))) tierMismatch++; emit(name, inp, r); count++; }
  for (let i = 0; count < N; i++, count++) {
    const g = gens[i % gens.length]; const inp = ar === 1 ? [g()] : g();
    const r = f(inp[0], inp[1], inp[2]);
    if ((i & 1023) === 0 && !same(r, cold[name](inp[0], inp[1], inp[2]))) tierMismatch++;
    emit(name, inp, r);
  }
}
flush();
process.stderr.write(`gen: node ${process.version} v8 ${process.versions.v8}, ${FNS.length} fns x ${N} samples, interpreter-vs-optimized mismatches: ${tierMismatch}\n`);
if (tierMismatch) process.exitCode = 3;

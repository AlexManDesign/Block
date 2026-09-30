'use strict';
// Seeded simplex noise (2D/3D) + small hash helpers. Worker side.

function mulberry(seed) {
  let s = seed >>> 0;
  return function () {
    s = (s + 0x6D2B79F5) | 0;
    let t = Math.imul(s ^ (s >>> 15), 1 | s);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
function hash2i(seed, x, z) {
  let h = Math.imul(x | 0, 0x27d4eb2d) ^ Math.imul(z | 0, 0x165667b1) ^ Math.imul(seed | 0, 0x9E3779B1);
  h = Math.imul(h ^ (h >>> 15), 0x85ebca6b);
  h = Math.imul(h ^ (h >>> 13), 0xc2b2ae35);
  return (h ^ (h >>> 16)) >>> 0;
}
function hash2(seed, x, z) { return hash2i(seed, x, z) / 4294967296; }
function hash3(seed, x, y, z) { return hash2i(seed + Math.imul(y | 0, 0x3C6EF372), x, z) / 4294967296; }

const F2 = 0.5 * (Math.sqrt(3) - 1), G2 = (3 - Math.sqrt(3)) / 6, F3 = 1 / 3, G3 = 1 / 6;
const GRAD3 = new Float32Array([1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1, 0, 1, 0, 1, -1, 0, 1, 1, 0, -1, -1, 0, -1,
  0, 1, 1, 0, -1, 1, 0, 1, -1, 0, -1, -1]);

class Simplex {
  constructor(seed) {
    const rnd = mulberry(seed);
    const p = new Uint8Array(256);
    for (let i = 0; i < 256; i++) p[i] = i;
    for (let i = 255; i > 0; i--) { const j = (rnd() * (i + 1)) | 0; const t = p[i]; p[i] = p[j]; p[j] = t; }
    this.perm = new Uint8Array(512); this.pm12 = new Uint8Array(512);
    for (let i = 0; i < 512; i++) { this.perm[i] = p[i & 255]; this.pm12[i] = (this.perm[i] % 12) * 3; }
    this.ox = rnd() * 4096; this.oz = rnd() * 4096; this.oy = rnd() * 4096;
  }
  n2(xin, yin) {
    xin += this.ox; yin += this.oz;
    const perm = this.perm, pm = this.pm12;
    const s = (xin + yin) * F2;
    const i = Math.floor(xin + s), j = Math.floor(yin + s);
    const t = (i + j) * G2;
    const x0 = xin - (i - t), y0 = yin - (j - t);
    let i1, j1; if (x0 > y0) { i1 = 1; j1 = 0; } else { i1 = 0; j1 = 1; }
    const x1 = x0 - i1 + G2, y1 = y0 - j1 + G2, x2 = x0 - 1 + 2 * G2, y2 = y0 - 1 + 2 * G2;
    const ii = i & 255, jj = j & 255;
    let n = 0, tt, g;
    tt = 0.5 - x0 * x0 - y0 * y0;
    if (tt > 0) { g = pm[ii + perm[jj]]; tt *= tt; n += tt * tt * (GRAD3[g] * x0 + GRAD3[g + 1] * y0); }
    tt = 0.5 - x1 * x1 - y1 * y1;
    if (tt > 0) { g = pm[ii + i1 + perm[jj + j1]]; tt *= tt; n += tt * tt * (GRAD3[g] * x1 + GRAD3[g + 1] * y1); }
    tt = 0.5 - x2 * x2 - y2 * y2;
    if (tt > 0) { g = pm[ii + 1 + perm[jj + 1]]; tt *= tt; n += tt * tt * (GRAD3[g] * x2 + GRAD3[g + 1] * y2); }
    return 70 * n;
  }
  n3(xin, yin, zin) {
    xin += this.ox; yin += this.oy; zin += this.oz;
    const perm = this.perm, pm = this.pm12;
    const s = (xin + yin + zin) * F3;
    const i = Math.floor(xin + s), j = Math.floor(yin + s), k = Math.floor(zin + s);
    const t = (i + j + k) * G3;
    const x0 = xin - (i - t), y0 = yin - (j - t), z0 = zin - (k - t);
    let i1, j1, k1, i2, j2, k2;
    if (x0 >= y0) {
      if (y0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
      else if (x0 >= z0) { i1 = 1; j1 = 0; k1 = 0; i2 = 1; j2 = 0; k2 = 1; }
      else { i1 = 0; j1 = 0; k1 = 1; i2 = 1; j2 = 0; k2 = 1; }
    } else {
      if (y0 < z0) { i1 = 0; j1 = 0; k1 = 1; i2 = 0; j2 = 1; k2 = 1; }
      else if (x0 < z0) { i1 = 0; j1 = 1; k1 = 0; i2 = 0; j2 = 1; k2 = 1; }
      else { i1 = 0; j1 = 1; k1 = 0; i2 = 1; j2 = 1; k2 = 0; }
    }
    const x1 = x0 - i1 + G3, y1 = y0 - j1 + G3, z1 = z0 - k1 + G3;
    const x2 = x0 - i2 + 2 * G3, y2 = y0 - j2 + 2 * G3, z2 = z0 - k2 + 2 * G3;
    const x3 = x0 - 1 + 3 * G3, y3 = y0 - 1 + 3 * G3, z3 = z0 - 1 + 3 * G3;
    const ii = i & 255, jj = j & 255, kk = k & 255;
    let n = 0, tt, g;
    tt = 0.6 - x0 * x0 - y0 * y0 - z0 * z0;
    if (tt > 0) { g = pm[ii + perm[jj + perm[kk]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x0 + GRAD3[g + 1] * y0 + GRAD3[g + 2] * z0); }
    tt = 0.6 - x1 * x1 - y1 * y1 - z1 * z1;
    if (tt > 0) { g = pm[ii + i1 + perm[jj + j1 + perm[kk + k1]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x1 + GRAD3[g + 1] * y1 + GRAD3[g + 2] * z1); }
    tt = 0.6 - x2 * x2 - y2 * y2 - z2 * z2;
    if (tt > 0) { g = pm[ii + i2 + perm[jj + j2 + perm[kk + k2]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x2 + GRAD3[g + 1] * y2 + GRAD3[g + 2] * z2); }
    tt = 0.6 - x3 * x3 - y3 * y3 - z3 * z3;
    if (tt > 0) { g = pm[ii + 1 + perm[jj + 1 + perm[kk + 1]]]; tt *= tt; n += tt * tt * (GRAD3[g] * x3 + GRAD3[g + 1] * y3 + GRAD3[g + 2] * z3); }
    return 32 * n;
  }
  // fractal sums, normalised to roughly [-1,1]
  f2(x, z, oct) {
    let s = 0, a = 1, n = 0;
    for (let o = 0; o < oct; o++) { s += this.n2(x, z) * a; n += a; a *= 0.5; x = x * 2.03 + 17.1; z = z * 2.03 - 9.3; }
    return s / n;
  }
  f3(x, y, z, oct) {
    let s = 0, a = 1, n = 0;
    for (let o = 0; o < oct; o++) { s += this.n3(x, y, z) * a; n += a; a *= 0.5; x = x * 2.01 + 7.7; y = y * 2.01 - 3.1; z = z * 2.01 + 11.3; }
    return s / n;
  }
}

// Value noise of the original maincraft generator (hash lattice + smoothstep, rotated octaves).
// Its thresholds (coral reefs, kelp forests, icebergs, podzol / packed-ice patches, windswept
// gravel) are tuned to this distribution, so it is ported as is rather than replaced by simplex.
class ValueNoise {
  constructor(seed) { this.s = seed | 0; }
  g(a, c) {
    let w = ((a + this.s) | 0) * 374761393 + ((c - this.s) | 0) * 668265263 + 527595518;
    w = (w ^ (w >>> 13)) | 0; w = Math.imul(w, 1274126177); w = (w ^ (w >>> 16)) >>> 0;
    return w / 4294967296;
  }
  g3(a, c, e) {
    let q = ((a + this.s) | 0) * 374761393 + (c | 0) * 668265263 + ((e - this.s) | 0) * 2147483423;
    q = (q ^ (q >>> 13)) | 0; q = Math.imul(q, 1274126177); q = (q ^ (q >>> 16)) >>> 0;
    return q / 4294967296;
  }
  v2(x, z) {
    const xi = Math.floor(x), zi = Math.floor(z), fx = x - xi, fz = z - zi;
    const a = this.g(xi, zi), b = this.g(xi + 1, zi), c = this.g(xi, zi + 1), d = this.g(xi + 1, zi + 1);
    const u = fx * fx * (3 - 2 * fx), v = fz * fz * (3 - 2 * fz);
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
  }
  // 4 octaves, each rotated 30 degrees further; result in [0, 1]
  f2(x, z) {
    let s = 0, amp = 0.5, fr = 1, tot = 0;
    for (let k = 0; k < 4; k++) {
      const r = VN_ROT[k], u = x * r[0] - z * r[1], v = x * r[1] + z * r[0];
      s += this.v2(u * fr + k * 119.7, v * fr - k * 53.3) * amp; tot += amp; amp *= 0.5; fr *= 2.17;
    }
    return s / tot;
  }
}
const VN_ROT = [[1, 0], [0.866, 0.5], [0.5, 0.866], [0, 1]];

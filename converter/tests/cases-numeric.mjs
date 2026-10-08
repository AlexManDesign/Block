// Differential cases for numbers as compiled code types them: int32 locals
// (i32), doubles (f64), -0, NaN and the infinities, int32 wrapping, the
// remainder and Math intrinsics emitted inline, and typed arrays read and
// written through the taGet/taSet helpers (elements are "nu": a number or
// undefined out of bounds). Most code runs inside functions so the typed
// tier sees it; top-level bindings are globals and stay dynamic.
export const cases = {
  // ---------- int32 wrapping ----------
  'numeric: int32 wrap via |0 and >>>0 near 2^31 and 2^32': `
    function run() {
      const big = 2147483647, small = -2147483648;
      let a = big | 0, b = small | 0;
      log(a + 1, (a + 1) | 0, b - 1, (b - 1) | 0, a + a, (a + a) | 0, (a + a + a + a) | 0);
      log(a >>> 0, b >>> 0, (b >>> 0) + 1, ((b >>> 0) + (b >>> 0)) | 0, -1 >>> 0, (-1 >>> 0) | 0);
      let u = 4294967295;
      log(u | 0, u >>> 0, (u + 1) | 0, (u + 2) >>> 0, u + 1, ~u, ~~u, -u | 0);
      let p53 = 9007199254740992;
      log(p53 | 0, (p53 + 2) | 0, (p53 - 1) | 0, (p53 - 1) >>> 0, (p53 * 3) | 0, -p53 | 0);
      let x = 1073741823;
      log(x + 1, (x + 1) | 0, (x * 2 + 2) | 0, (x * 4) | 0, (x << 1) + 2, x << 2, (x + 1) << 1);
      let sum = 0;
      for (let i = 0; i < 6; i++) sum = (sum + 1000000000) | 0;
      log(sum);
      let usum = 0;
      for (let i = 0; i < 6; i++) usum = (usum + 1000000000) >>> 0;
      log(usum, usum | 0);
      return [a, b, u, x, sum, usum];
    }
    log(run());`,
  'numeric: shifts and bitwise ops with odd counts, NaN and Infinity': `
    function sh(x, n) { return [x << n, x >> n, x >>> n]; }
    function run() {
      const counts = [0, 1, 31, 32, 33, -1, -31, 1.9, -0.5, 64, NaN];
      for (const c of counts) log(c, sh(1, c), sh(-1, c), sh(-2147483648, c), sh(1234567, c));
      let n = 31, v = 1;
      log(v << n, (v << n) >> n, (v << n) >>> n, v << (n + 1), -16 >> 2, -16 >>> 2, 5 >> 40);
      let f = 3.75, g = -3.75;
      log(f << 1, g << 1, f >> 1, g >> 1, g >>> 1, NaN << 3, Infinity >> 1, 2 ** 40 << 1);
    }
    run();
    function bits(n, inf, big) {
      log(n | 0, n >>> 0, ~n, n << 1, n & -1, n ^ 5, inf | 0, -inf >>> 0, inf >> 1, ~inf);
      log(big | 0, (big + 1) | 0, -big | 0, big >>> 0, 1e21 | 0, -1e21 | 0, 2 ** 84 | 0, (2 ** 84 + 2 ** 33) | 0, 2 ** 63 | 0);
      log(1.5e10 | 0, -1.5e10 >>> 0, 4294967296.7 | 0, -4294967296.7 | 0, 2147483648.5 | 0, -2147483648.5 | 0, -2147483649.9 | 0);
    }
    bits(NaN, Infinity, 2 ** 53);`,
  'numeric: Math.imul and Math.clz32 edges': `
    function run(a, b) {
      return [Math.imul(a, b), Math.clz32(a), Math.clz32(b), Math.imul(a, a), Math.clz32(a * b)];
    }
    log(run(3, 4), run(-1, 8), run(2147483647, 2), run(-2147483648, -1), run(65537, 65537));
    log(run(4294967295, 5), run(0.5, 7.9), run(NaN, 3), run(Infinity, -Infinity), run(2 ** 32 + 3, 2 ** 31));
    function hash(s) { let h = 2166136261 >>> 0; for (let i = 0; i < s.length; i++) { h ^= s.charCodeAt(i); h = Math.imul(h, 16777619); } return h >>> 0; }
    log(hash('voxel'), hash(''), hash('aaaaaaaaaaaaaaaa'));
    log(Math.imul('3', '5'), Math.clz32('8'), Math.clz32(), Math.imul(2), Math.clz32(-0), Math.clz32(0.9));`,
  'numeric: values at the i31 boundary through arrays objects and closures': `
    function make(base) {
      const vals = [];
      for (let d = -2; d <= 1; d++) vals.push(base + d);
      return vals;
    }
    function run() {
      const edges = [1073741824, -1073741823, 2147483647, -2147483647];
      for (const e of edges) {
        const vs = make(e), o = {v: vs[1]}, get = () => vs[2];
        let w = vs[0] | 0;
        w++;
        log(vs, o.v, get(), w, vs[3] === e + 1, o.v + 1 === vs[2], typeof vs[2]);
      }
      let i31 = 1073741823;
      const arr = [i31, i31 + 1, -i31 - 1, -i31 - 2];
      const m = new Map(arr.map((v, k) => [v, k]));
      log(arr, m.get(1073741824), m.get(-1073741824), m.get(-1073741825), arr.indexOf(1073741824), arr.includes(-1073741825));
      const cl = () => { i31 += 1; return i31; };
      log(cl(), cl(), i31 - 1, JSON.stringify({a: i31, b: -i31 - 3}));
    }
    run();`,
  // ---------- -0 ----------
  'numeric: -0 production in typed locals': `
    function zero(k) { return k * 0; }
    function run(x, y) {
      const a = 0 * -1, b = -x, c = Math.round(-0.4), d = Math.ceil(-0.5), e = Math.trunc(-0.5);
      const f = -4 % 2, g = y % 1, h = x / -5, i = Math.sign(-x), j = Math.min(0, -0), k = Math.max(-0, -0);
      const l = Math.abs(-0), m = -0 + 0, n = -0 - 0, o = -0 * 0, p = zero(-3), q = Math.round(-0.5), r = Math.floor(-0);
      log(a, b, c, d, e, f, g, h, i, j, k, l, m, n, o, p, q, r);
      log(1 / a, 1 / b, Object.is(c, -0), Object.is(p, -0), a === 0, a < 0, a == 0, !a, a ? 't' : 'f');
      log(String(b), b + '', \`\${b}\`, JSON.stringify([b, c]), [b].join(), (-0).toFixed(1), b.toString());
      const s = -x;
      log(s | 0, s >>> 0, ~s, s + 0, s - 0, 0 - s, s * -1, Math.sqrt(s), s ** 2, s / 3, Math.round(s), Math.max(s, -0));
    }
    run(0, -3);`,
  'numeric: -0 through calls, closures, arrays and typed arrays': `
    function id(v) { return v; }
    function neg(v) { return -v; }
    function run() {
      let z = neg(0);
      const box = {z}, arr = [z, -z], cl = () => z;
      const f64 = new Float64Array(2), f32 = new Float32Array(2), i32 = new Int32Array(2), u8c = new Uint8ClampedArray(2);
      f64[0] = z; f32[0] = z; i32[0] = z; u8c[0] = z; f64[1] = -f64[0];
      log(id(z), box.z, arr, cl(), f64[0], f32[0], i32[0], u8c[0], f64[1]);
      log(Object.is(f64[0], -0), Object.is(f32[0], -0), Object.is(i32[0], -0), 1 / f64[0], 1 / i32[0]);
      let acc = -0;
      for (let i = 0; i < 3; i++) acc *= 1;
      log(acc, Object.is(acc, -0), acc + 0, -acc);
      const m = new Map([[-0, 'a']]);
      log(m.get(0), m.has(-0), [z].includes(0), [z].indexOf(0), Object.is(Math.max(z, -0), -0));
    }
    run();`,
  // ---------- remainder ----------
  'numeric: remainder with non-int32 and special operands': `
    // Each probe runs as its own task, so one failing probe does not hide the others.
    function frac(x, y) { return x % y; }
    const u32 = new Uint32Array([4294967295, 7]), i8 = new Int8Array(2);
    let dyn = 1e10;
    const probes = [
      () => log('fractions', frac(5.5, 2), frac(-5.5, 2), frac(5.5, -2), frac(7, 2.5), frac(-7, 2.5), frac(0.3, 0.1), frac(1, 0), frac(-1, -0)),
      () => log('big dividend', frac(1e10, 7), frac(-1e10, 7), frac(2147483648, 10), frac(-2147483649, 10), frac(4294967296, 3)),
      () => log('nan dividend', frac(NaN, 3), frac(NaN, 0.5)),
      () => log('infinite dividend', frac(Infinity, 2), frac(-Infinity, 3)),
      () => log('infinite divisor', frac(2, Infinity), frac(-2, -Infinity), frac(7.5, Infinity)),
      () => log('big divisor', frac(3, 1e10), frac(-3, 4294967296)),
      () => log('huge', frac(1e300, 7), frac(5e-324, 1)),
      () => log('uint32 element', u32[0] % 7, u32[0] % u32[1], u32[1] % 3),
      () => log('oob element', i8[5] % 3, 5 % i8[5]),
      () => log('dynamic', dyn % 7, dyn % 2.5),
    ];
    probes.forEach((p, i) => setTimeout(p, i));
    window.__done = new Promise(r => setTimeout(r, 60));`,
  'numeric: remainder of zero and negative zero dividends': `
    function run(z, nz) {
      const r1 = z % 5, r2 = nz % 5, r3 = nz % -5, r4 = z % -5, r5 = nz % 2.5, r6 = (z - 0) % 3;
      log(r1, r2, r3, r4, r5, r6, Object.is(r2, -0), 1 / r2);
    }
    run(0, -0);
    function run2() { let z = -0; log(z % 5, z % 0.5, Object.is(z % 7, -0)); }
    run2();`,
  'numeric: int32 remainder in loops and with INT_MIN': `
    function run() {
      const out = [];
      for (let i = -6; i <= 6; i += 3) for (let j = -4; j <= 4; j += 2) {
        const r = i % j;
        out.push(Object.is(r, -0) ? '-0' : String(r));
      }
      log(out.join(' '));
      let m = -2147483648 | 0, n = -1 | 0, k = 2147483647 | 0;
      log(m % n, m % 2, m % k, k % m, m % -2147483648, k % -1, -k % 1, m % 3);
      let acc = 0;
      for (let i = 0; i < 100; i++) acc += (i * 7 - 300) % 11;
      log(acc);
    }
    run();`,
  // ---------- NaN and Infinity ----------
  'numeric: NaN and Infinity in comparisons and conditions': `
    function run(n, inf) {
      log(n < 1, n > 1, n <= n, n >= n, n == n, n != n, n === n, n !== n, !n, n ? 1 : 2, n || 'or', n && 'and', n ?? 'nn');
      log(inf > 1e308, -inf < -1e308, inf === inf + 1, inf - inf, inf * 0, -inf * -1, 1 / inf, -1 / inf, inf / inf);
      log(Math.max(n, 1), Math.min(inf, n), Math.max(-inf, inf), n > inf, n < -inf, isNaN(n), isFinite(inf), Number.isNaN(n));
      let c = 0;
      for (let i = 0; i < 5; i++) if (n < i || n >= i) c++;
      log(c, n !== n ? 'nan' : 'num');
      const arr = [3, n, 1, inf, -inf, 2];
      log(arr.sort((a, b) => a - b), arr.indexOf(n), arr.includes(n), arr.findIndex(v => v !== v));
    }
    run(NaN, Infinity);`,
  // ---------- division and exponent ----------
  'numeric: division producing fractions from ints': `
    function avg(a, b) { return (a + b) / 2; }
    function run(n) {
      const q = n / 4, r = 7 / 2, s = -7 / 2, t = 0 / n, u = n / 0, v = -n / 0, w = 0 / 0;
      log(q, r, s, t, u, v, w, avg(3, 4), avg(-3, 4), avg(2147483647, 2147483647));
      const arr = [10, 20, 30, 40];
      log(arr[n / 2], arr[n / 4], arr[(n / 4) | 0], arr[Math.floor(n / 4)], arr[n >> 2]);
      let k = 1;
      for (let i = 0; i < 5; i++) k = k / 3;
      log(k, k * 243, (k * 243) === 1, 1 / 3 + 1 / 3 + 1 / 3, 10 / 3 * 3);
      const ints = [];
      for (let i = -3; i <= 3; i++) ints.push(i / 2, (i / 2) | 0, Math.trunc(i / 2), Math.floor(i / 2));
      log(ints);
    }
    run(6);`,
  'numeric: exponent operator edges': `
    function run(a, b) {
      log(a ** b, (-a) ** b, a ** -b, b ** 0.5, (-8) ** (1 / 3), 0 ** 0, NaN ** 0, 1 ** Infinity, 1 ** NaN, (-0) ** -1, (-0) ** -2, 0 ** -1);
      log(2 ** 31, 2 ** 32, 2 ** 53 + 1, 2 ** -1074, 2 ** 1024, (-2) ** 31, (-2) ** 1024, 10 ** 21, 10 ** -7, Infinity ** -1, (-Infinity) ** 3);
      let x = 3;
      for (let i = 0; i < 3; i++) x **= 2;
      let y = 2;
      y **= -2;
      log(x, y, 2 ** 3 ** 2, (2 ** 3) ** 2, a ** b | 0, (2 ** 31) | 0, Math.pow(a, b) === a ** b);
    }
    run(2, 10);`,
  // ---------- Math ----------
  'numeric: Math.min and Math.max with NaN, -0 and coercions': `
    function run(n, z, nz) {
      log(Math.min(1, n), Math.max(n, 1), Math.min(z, nz), Math.max(nz, z), Math.min(nz, z), Math.max(z, nz));
      log(Object.is(Math.min(z, nz), -0), Object.is(Math.max(nz, z), 0), Math.min(), Math.max(), Math.min(nz), Math.max(-Infinity, nz));
      log(Math.min('2', 1), Math.max('x', 1), Math.max(true, 0.5), Math.min(null, 1), Math.max(undefined, 1), Math.min([3], 4), Math.max(1, 2, 3, n, 4));
      let lo = Infinity, hi = -Infinity;
      const vals = [5, -2.5, 0, -0, 7.25, -3];
      for (let i = 0; i < vals.length; i++) { lo = Math.min(lo, vals[i]); hi = Math.max(hi, vals[i]); }
      log(lo, hi);
    }
    run(NaN, 0, -0);`,
  'numeric: Math.round, trunc, sign, fround and hypot': `
    function run(xs) {
      for (const x of xs) log(x, Math.round(x), Math.trunc(x), Math.sign(x), Math.fround(x), Math.ceil(x), Math.floor(x), Math.abs(x));
    }
    run([0.5, -0.5, 1.5, -1.5, 2.5, -2.5, 0.49999999999999994, -0.49999999999999994, 4503599627370495.5, 4503599627370497, -4503599627370497, 1e300, -1e-300, 0, -0, NaN, Infinity, -Infinity, 2147483647.5, -2147483648.5, 16777217, 3.4028235677973366e38, 1e-46]);
    function hyp(a, b, c) { return [Math.hypot(a, b), Math.hypot(a, b, c), Math.hypot(), Math.hypot(-0), Math.hypot(NaN, Infinity), Math.hypot(1e200, 1e200)]; }
    log(hyp(3, 4, 12), hyp(-0, 0, -0));
    function r(x) { return Math.round(x) | 0; }
    log(r(2147483647.4), r(-2147483648.6), r(2.5), r(-2.5), r(1e10));`,
  // ---------- increments ----------
  'numeric: ++ and -- on floats and near limits': `
    function run(x) {
      let a = x, b = 0.1, c = 2 ** 53, d = -0, e = NaN, f = Infinity, g = 2147483647, h = -2147483648, k = 1073741823;
      log(a++, a, ++a, a--, --a, a);
      log(b++, b, b--, b, ++b, b === 1.1, --b === 0.1);
      log(c++, c, ++c, c - 2 ** 53, d++, d, --d, Object.is(--d, -1));
      log(e++, e, f--, f, g++, g, g | 0, h--, h, h >>> 0, k++, k, ++k);
      let s = '5', t = '0x10', u = null, v;
      s++; t--; u++; v++;
      log(s, typeof s, t, u, v);
      let step = 0;
      for (let i = 0.5; i < 3; i++) step += i;
      log(step);
    }
    run(1.25);`,
  // ---------- formatting ----------
  'numeric: number to string formatting of computed results': `
    function fmt(x) { return x + '|' + String(x) + '|' + \`\${x}\` + '|' + [x].join(); }
    function run(a, b) {
      const vals = [a / b, a * 1e20, a * 1e21, a / 1e6, a / 1e7, -a / 1e7, 2 ** 53 * a, 2 ** 64, 0.1 + 0.2, 123e-20, 5e-324 * a, 1.7976931348623157e308, 1 / 3, -100 / 3, 1e21 - 1, 999999999999999999999, 0.000001, 1e-7, 2147483648 * a, -2147483649];
      for (const v of vals) log(fmt(v));
      log(fmt(a | 0), fmt(-a | 0), fmt(-(2 ** 31)), fmt(2 ** 31 - 1), fmt(-0 * a), fmt(NaN * a), fmt(-Infinity * a));
      log((a / b).toFixed(3), (a * 1e21).toFixed(2), (1.005).toFixed(2), (a / b).toPrecision(4), (255 * a).toString(16), (-255.5 * a).toString(2), (a * 35).toString(36), (1e21 * a).toLocaleString === undefined);
    }
    run(1, 3);`,
  // ---------- typed array element kinds ----------
  'numeric: typed array stores convert per element kind': `
    function run(vals) {
      const kinds = [Uint8Array, Uint8ClampedArray, Int8Array, Int16Array, Uint16Array, Int32Array, Uint32Array, Float32Array, Float64Array];
      for (const K of kinds) {
        const a = new K(vals.length);
        for (let i = 0; i < vals.length; i++) a[i] = vals[i];
        log(K.name, a);
      }
    }
    run([0.5, 1.5, 2.5, -0.5, -1, 127, 128, 255, 255.5, 256, -129, 32768, 65535, 65536, 2147483648, 4294967295, 4294967296, -2147483649, NaN, Infinity, -Infinity, 16777217, 0.1, 1e40, 1e-50]);`,
  'numeric: typed array stores from typed locals': `
    function run() {
      const c = new Uint8ClampedArray(8), i8 = new Int8Array(8), u16 = new Uint16Array(8), f32 = new Float32Array(8);
      for (let i = 0; i < 8; i++) {
        const v = i * 64 - 100;
        const h = v + 0.5;
        c[i] = h; i8[i] = v; u16[i] = v * 300; f32[i] = h / 3;
      }
      log(c, i8, u16, f32);
      const t = true, s = '300', n = null, u = undefined, o = {valueOf() { return 7.9; }};
      c[0] = t; c[1] = s; c[2] = n; c[3] = u; c[4] = o; c[5] = '1e3'; c[6] = '-5'; c[7] = '0x7f';
      i8[0] = t; i8[1] = s; i8[2] = n; i8[3] = u; i8[4] = o; i8[5] = '1e3'; i8[6] = '-5'; i8[7] = '0x7f';
      log(c, i8);
      log((c[0] = 1000), (i8[1] = 1000), (f32[0] = 0.1), (u16[0] = -1), f32[0], u16[0]);
    }
    run();`,
  'numeric: Uint32Array and Float32Array values read into variables and compared': `
    function run() {
      const u = new Uint32Array([4294967295, 2147483648, 2147483647, 0, 1]);
      let a = u[0], b = u[1], c = u[2];
      log(a, b, c, a > b, b > c, a | 0, b | 0, a >>> 0, a + 1, b * 2, a - b, a === 4294967295, b === -2147483648, (b | 0) === -2147483648);
      let s = 0;
      for (let i = 0; i < u.length; i++) s += u[i];
      log(s, s | 0, s >>> 0, u[0] + u[1], (u[0] + u[1]) | 0, u[0] & u[1], u[0] ^ 1, ~u[1]);
      const arr = [10, 20, 30];
      log(arr[u[4]], arr[u[3]], arr[u[0]], u[u[4]]);
      const f = new Float32Array([0.1, 16777217, 1e40, -1e-50, 3.14159]);
      let x = f[0], y = f[1];
      log(x, y, x === 0.1, x === Math.fround(0.1), y === 16777216, f[2], Object.is(f[3], -0), f[4] * 2, f[0] + f[4]);
    }
    run();
    function run2() {
      const a = new Uint8Array([7, 255, 0]), b = new Int8Array([7, -1, 0]), c = new Float32Array([7, 255, -0]), d = new Uint32Array([7, 4294967295, 0]);
      for (let i = 0; i < 4; i++) {
        const x = a[i], y = b[i], z = c[i], w = d[i];
        log(i, x === y, x === z, y === w, z === w, x == z, (x & 0xff) === (y & 0xff), (y >>> 0) === w, (y | 0) === (w | 0), x < z, y < x, x === undefined, y == undefined);
      }
      const keep = [];
      for (let i = 0; i < 3; i++) { const p = a[i], q = b[i]; keep.push(p === q ? 'eq' : p > q ? 'gt' : 'lt'); }
      log(keep);
    }
    run2();`,
  // ---------- out of bounds ----------
  'numeric: out-of-bounds element reads into variables': `
    function run() {
      const a = new Int16Array(3), f = new Float64Array([1.5, -0]);
      a[0] = 5;
      const v = a[3], w = a[-1], x = a[0], y = f[2], z = f[1];
      log(v, w, x, y, z, typeof v, typeof x, v === undefined, x === undefined, v === w, v == w, v === y, v == null, x == null);
      log(v === 0, v == 0, v !== 0, v < 1, v >= 0, v === v, v !== v, x === 5, z === 0, z === -0, Object.is(z, -0));
      log(v + 1, v + '!', '' + v, \`\${v}\`, v | 0, v >>> 0, ~v, -v, +v, !v, v ? 'y' : 'n', v ?? 'dflt', v || 'or', v && 'and', [v], {v}.v);
      let cnt = 0;
      for (let i = -2; i < 6; i++) { const e = a[i]; if (e === undefined) cnt += 100; else if (e === 0) cnt += 10; else cnt += 1; }
      log(cnt);
      let m = a[9];
      m++;
      let n = f[9];
      n += 1;
      let p = a[7];
      p = p * 2;
      log(m, n, p, Math.max(a[9], 1), Math.min(a[0], a[1]), isNaN(a[9]), String(a[9]), a[9] + a[10]);
    }
    run();`,
  'numeric: out-of-bounds, fractional and non-index keys': `
    function run() {
      const a = new Uint8Array(4);
      a[4] = 1; a[-1] = 2; a[1.5] = 3; a['2'] = 4; a[-0] = 5; a[NaN] = 6; a[Infinity] = 7; a['01'] = 8; a[2 ** 32] = 9;
      log(a, a[4], a[-1], a[1.5], a['01'], a.length, Object.keys(a).join());
      let k = 3.0;
      a[k] = 200; a[k + 0.25] = 9; a[k - 3.5] = 9;
      log(a, a[k], a[k + 1]);
      const b = new Float32Array(2), idx = [0, 1, 2, -1, 0.5];
      for (let i = 0; i < idx.length; i++) b[idx[i]] = i + 0.25;
      log(b, b[2], b[0.5]);
    }
    run();
    function run2() {
      const a = new Int16Array(10);
      for (let i = 0; i < a.length; i++) a[i] = i * i - 20;
      let s = 0;
      for (let i = 0; i < a.length; i += 0.5) s += a[i] === undefined ? 1000 : a[i];
      log(s);
      let t = 0;
      for (let i = a.length - 1; i >= 0; i--) t = t * 3 + a[i] | 0;
      log(t);
      const sub = a.subarray(3, 7);
      log(sub.length, sub[sub.length - 1], sub[sub.length], a[a.length / 2], a[a.length / 4], a[a.length - 0.5]);
      let k = 0;
      for (let i = 0; i < 4; i++) k += a[Math.floor(i * 2.5)] + a[i * 2.5 | 0];
      log(k);
      const g = new Uint8Array(0);
      log(g.length, g[0], g[0] === undefined);
    }
    run2();`,
  'numeric: boolean and undefined keys on typed arrays': `
    function run() {
      const a = new Uint8Array(4), t = true, f = false;
      a[t] = 7; a[f] = 9;
      log(a[1], a[0], a[t], a[f], a.true, a, Object.keys(a).join());
      const b = new Int32Array(3), probe = new Int32Array(1);
      const key = probe[5];
      b[key] = 4;
      log(b, b[key], b.undefined, b[key] + 1);
      const c = new Float64Array(2);
      c[1] = 2.5;
      log(c[t] + 1, c[t] === 2.5, c[t] | 0);
      const cnt = new Int32Array(2);
      for (let i = 0; i < 4; i++) cnt[i > 1]++;
      log(cnt, cnt.true, cnt.false);
    }
    run();`,
  // ---------- compound assignment and increments on elements ----------
  'numeric: compound assignment on typed array elements': `
    function run() {
      const u = new Uint8Array([250, 10, 20, 30, 40, 50, 60, 70]);
      log(u[0] += 10, u[1] -= 20, u[2] *= 13, u[3] /= 4, u[4] %= 7, u[5] **= 2, u[6] <<= 3, u[7] >>= 1);
      log(u);
      const i8 = new Int8Array([100, -100, 7, -7, 1, 64]);
      log(i8[0] += 100, i8[1] -= 100, i8[2] |= 0x80, i8[3] &= 0x7f, i8[4] ^= -1, i8[5] >>>= 1);
      log(i8);
      const f = new Float32Array([1, 2, 3, 4]);
      log(f[0] += 0.1, f[1] /= 3, f[2] -= '1.5', f[3] *= '2');
      log(f);
      const s = new Uint16Array([1, 2, 3, 4]);
      log(s[0] += '5', s[1] -= '5', s[2] |= '8', s[3] += true);
      log(s);
      const w = new Uint32Array([4294967295, 1]);
      log(w[0] += 1, w[1] -= 2, w[0] |= 0, w[1] >>>= 0, w);
      const o = new Int16Array(2);
      log(o[5] += 1, o[5] |= 1, o[-1] -= 3, o, Object.keys(o).join());
    }
    run();`,
  'numeric: increments on typed array elements': `
    function run() {
      const u = new Uint8Array([255, 0]), c = new Uint8ClampedArray([255, 0]), i = new Int8Array([127, -128]), f = new Float32Array([16777216, 0.5]);
      log(u[0]++, u[0], u[1]--, u[1], ++u[0], --u[1]);
      log(c[0]++, c[0], c[1]--, c[1], ++c[0], --c[1]);
      log(i[0]++, i[0], i[1]--, i[1], ++i[0], --i[1]);
      log(f[0]++, f[0], ++f[0], f[1]--, f[1], --f[1]);
      log(u[9]++, ++u[9], u[-1]--, u.length);
      const h = new Int32Array(4);
      for (let k = 0; k < 20; k++) h[k & 3]++;
      for (let k = 0; k < 7; k++) h[(k * 3) % 4]--;
      log(h);
      const m = new Int32Array([2147483647, -2147483648]);
      m[0]++; m[1]--;
      log(m, m[0] + 1, m[1] - 1);
    }
    run();`,
  // ---------- views ----------
  'numeric: subarray aliasing and views on one buffer': `
    function run() {
      const base = new Uint8Array(16);
      for (let i = 0; i < 16; i++) base[i] = i * 3;
      const sub = base.subarray(4, 12), sub2 = sub.subarray(-3), neg = base.subarray(-4, -1);
      sub[0] = 255; sub2[0] = 254; neg[2] = 253; sub[100] = 1;
      log(base, sub, sub2, neg, sub.length, sub2.byteOffset, neg.byteOffset, sub[8], sub2[-1]);
      const u32 = new Uint32Array(base.buffer, 4, 2), f32 = new Float32Array(base.buffer, 8, 1), i16 = new Int16Array(base.buffer);
      log(u32[0], u32[1], f32[0], i16[2], i16[7]);
      u32[0] = 0xdeadbeef; i16[7] = -2;
      log(base[4], base[5], base[6], base[7], base[14], base[15], sub[0], sub[3]);
      const sl = base.slice(4, 8);
      sl[0] = 1;
      log(base[4], sl[0], sl.buffer === base.buffer, sub.buffer === base.buffer);
    }
    run();`,
  'numeric: set and fill with conversions and overlaps': `
    function run() {
      const a = new Int8Array(8);
      a.fill(200); log(a);
      a.fill(-1.9, 2, 4); log(a);
      a.fill(5, -3); log(a);
      a.fill('7', 1, -6); log(a);
      a.set([1, 2, 300, -129]); log(a);
      a.set(new Float32Array([1.5, -2.5]), 6); log(a);
      const b = new Uint8Array([1, 2, 3, 4, 5, 6, 7, 8]);
      b.set(b.subarray(0, 6), 2); log(b);
      b.set(b.subarray(3), 0); log(b);
      const f = new Float64Array(4);
      f.set(new Int16Array([-1, 2]), 1); f.fill(NaN, 3); log(f);
      try { a.set([1, 2], 7); } catch (e) { logError(e); }
      const c = new Uint8ClampedArray(4);
      c.fill(2.5); log(c); c.fill(-3); log(c); c.set([1.5, 0.5, 300, NaN]); log(c);
    }
    run();`,
  // ---------- element values in expressions ----------
  'numeric: element values in arithmetic, conditions and concatenation': `
    function run() {
      const u = new Uint8Array([0, 1, 2, 200]), f = new Float32Array([0, -0, 0.5, NaN]);
      let s = '';
      for (let i = 0; i < 5; i++) s += u[i] + ',';
      log(s);
      let t = '';
      for (let i = 0; i < 5; i++) t += f[i] + ';' + (f[i] ? 'T' : 'F') + ';';
      log(t);
      log(u[3] + u[3], u[3] * u[3], u[3] << 24, (u[3] << 24) >>> 0, u[1] - u[2], u[1] / u[0], u[2] % u[0], u[3] + '' + u[2], u[3] + u[2] + 'x');
      log(f[0] === f[1], f[3] === f[3], f[3] !== f[3], f[2] > f[1], f[1] < f[2], f[0] == u[0], u[1] == true, u[2] == true, u[1] === true);
      log(u[0] ? 1 : 2, u[1] && u[2], u[0] || u[3], u[9] || 'oob', u[0] ?? 'nn', u[9] ?? 'nn', !u[0], !u[9], !!f[3]);
      log(\`\${u[3]}-\${f[2]}-\${u[7]}\`, [u[0], u[9], f[1]], String(f[1]), u[3].toString(16), f[2].toFixed(2));
      let n = 0;
      if (u[3] > 100 && f[2] < 1) n += 1;
      if (u[9] > 0 || u[9] <= 0) n += 10;
      if (f[3] < 1 || f[3] >= 1) n += 100;
      while (u[n & 3] && n < 1000) n += 3;
      log(n);
    }
    run();`,
  'numeric: one variable holding typed arrays of different kinds': `
    function pick(k) { return k === 0 ? new Int8Array(3) : k === 1 ? new Float32Array(3) : new Uint8ClampedArray(3); }
    function run() {
      for (let k = 0; k < 3; k++) {
        let arr = k === 0 ? new Int8Array(3) : k === 1 ? new Float32Array(3) : new Uint8ClampedArray(3);
        arr[0] = 300.7; arr[1] = -1.5; arr[2] = 0.1;
        arr[0] += 1;
        log(arr, arr[0] | 0, arr[1] < 0, arr[2] * 10);
        const p = pick(k);
        p[0] = 1e10; p[1]--; p[2] = arr[2];
        log(p);
      }
    }
    run();`,
  // ---------- BigInt arrays and DataView ----------
  'numeric: BigInt64Array and BigUint64Array': `
    function run() {
      const a = new BigInt64Array(4), b = new BigUint64Array(3);
      a[0] = 2n ** 63n; a[1] = -1n; a[2] = 2n ** 64n + 5n; a[3] = 123n;
      b[0] = -1n; b[1] = 2n ** 64n; b[2] = 2n ** 63n;
      log(a, b, a[0], b[0], typeof a[1], a[5]);
      a[3] += 1n; a[3] *= -2n; b[2] >>= 3n;
      a[1]++; ++a[1]; b[1]--;
      log(a, b, a[3] + 10n, a[3] === -248n, a[1] == 1, a[9] === undefined);
      try { a[0] = 1; } catch (e) { logError(e); }
      try { a[0] += 1; } catch (e) { logError(e); }
      try { const x = a[0] + 1; log(x); } catch (e) { logError(e); }
      let s = 0n;
      for (let i = 0; i < a.length; i++) s += a[i];
      log(s, BigInt.asIntN(64, s), BigInt.asUintN(64, s));
      log(new BigInt64Array([5n, -5n]).map(v => v * v), Array.from(b, v => v.toString(16)));
    }
    run();`,
  'numeric: DataView get and set': `
    function run() {
      const buf = new ArrayBuffer(16), dv = new DataView(buf), u8 = new Uint8Array(buf);
      dv.setInt8(0, -1); dv.setUint16(1, 0x1234); dv.setUint16(3, 0x1234, true);
      dv.setFloat32(5, 1.1); dv.setInt32(9, -2, true); dv.setUint8(13, 300); dv.setInt16(14, 40000);
      log(u8);
      log(dv.getUint8(0), dv.getInt8(0), dv.getUint16(1), dv.getUint16(1, true), dv.getInt16(3, true), dv.getFloat32(5), dv.getFloat32(5) === Math.fround(1.1));
      log(dv.getInt32(9, true), dv.getUint32(9, true), dv.getUint32(9), dv.getInt16(14), dv.getUint16(14), dv.getUint8(13));
      dv.setFloat64(0, -0); log(dv.getFloat64(0), Object.is(dv.getFloat64(0), -0), u8[0]);
      dv.setFloat64(8, NaN); log(dv.getFloat64(8), dv.getUint32(8) >>> 0);
      dv.setBigInt64(0, -2n); log(dv.getBigUint64(0), dv.getBigInt64(0, true), dv.getInt32(4));
      let sum = 0;
      for (let i = 0; i < 16; i += 2) sum += dv.getInt16(i, (i & 2) === 0);
      log(sum);
      try { dv.getInt32(14); } catch (e) { logError(e); }
      try { dv.setUint8(-1, 0); } catch (e) { logError(e); }
      log(new DataView(buf, 4, 4).getInt8(0), dv.byteLength);
    }
    run();`,
  // ---------- loops ----------
  'numeric: voxel meshing with neighbor reads past chunk edges': `
    function run() {
      const S = 8, vox = new Uint8Array(S * S * S);
      for (let y = 0; y < S; y++) for (let z = 0; z < S; z++) for (let x = 0; x < S; x++) {
        const h = ((x * 7 + z * 13) & 7) >> 1;
        vox[x | (z << 3) | (y << 6)] = y <= h ? 1 + ((x + y + z) % 3) : 0;
      }
      const at = (x, y, z) => vox[x + (z << 3) + (y << 6)];
      let faces = 0, oob = 0, edge = 0, sumType = 0;
      for (let y = 0; y < S; y++) for (let z = 0; z < S; z++) for (let x = 0; x < S; x++) {
        const i = x + (z << 3) + (y << 6), t = vox[i];
        if (!t) continue;
        sumType += t;
        const nb = [vox[i + 1], vox[i - 1], vox[i + 8], vox[i - 8], vox[i + 64], vox[i - 64]];
        for (let k = 0; k < 6; k++) {
          const n = nb[k];
          if (n === undefined) oob++;
          else if (n === 0) faces++;
          if (x === S - 1 && k === 0 && n !== undefined) edge++;
        }
        if (at(x, y + 1, z) === undefined && y + 1 >= S) edge += 1000;
      }
      log(faces, oob, edge, sumType);
      const quad = new Float32Array(faces * 4);
      let q = 0;
      for (let i = 0; i < vox.length && q < quad.length; i++) if (vox[i] && !vox[i + 64]) { quad[q++] = i & 7; quad[q++] = i >> 6; quad[q++] = (i >> 3) & 7; quad[q++] = vox[i] / 3; }
      log(q, quad.slice(0, 8), quad[q - 1]);
    }
    run();`,
  'numeric: light flood fill with typed queues': `
    function run() {
      const W = 12, N = W * W, light = new Uint8Array(N), solid = new Uint8Array(N), queue = new Int32Array(N * 4);
      for (let i = 0; i < N; i++) solid[i] = ((i * 2654435761) >>> 29) === 0 ? 1 : 0;
      let head = 0, tail = 0;
      const seed = (W >> 1) * W + (W >> 1);
      solid[seed] = 0; light[seed] = 15; queue[tail++] = seed;
      while (head < tail) {
        const i = queue[head++], l = light[i];
        if (l <= 1) continue;
        const x = i % W, y = (i / W) | 0;
        const ns = [x > 0 ? i - 1 : -1, x < W - 1 ? i + 1 : -1, y > 0 ? i - W : -1, y < W - 1 ? i + W : -1];
        for (let k = 0; k < 4; k++) {
          const j = ns[k];
          if (j < 0 || solid[j]) continue;
          if (light[j] + 2 <= l) { light[j] = l - 1; queue[tail++] = j; }
        }
      }
      let total = 0, lit = 0, maxRow = '';
      for (let i = 0; i < N; i++) { total += light[i]; if (light[i]) lit++; }
      for (let x = 0; x < W; x++) maxRow += light[(W >> 1) * W + x].toString(16);
      log(total, lit, tail, maxRow, solid.reduce((a, b) => a + b, 0));
    }
    run();`,
  'numeric: counters that overflow 2^31 in loops': `
    function run() {
      let c = 2147483600, d = -2147483600, e = 1073741800;
      for (let i = 0; i < 100; i++) { c++; d--; e += 1; }
      log(c, d, e, c | 0, d | 0, c > 2147483647, d < -2147483648, typeof c);
      let big = 0;
      for (let i = 0; i < 30; i++) big += 123456789;
      log(big, big | 0, big >>> 0, big - Math.floor(big / 1000) * 1000, (big / 1000) | 0);
      let w = 2147483000, wraps = 0;
      for (let i = 0; i < 2000; i++) { const nw = (w + 1) | 0; if (nw < w) wraps++; w = nw; }
      log(w, wraps);
      let u = 4294967000;
      for (let i = 0; i < 500; i++) u = (u + 1) >>> 0;
      log(u);
      let m = 1;
      for (let i = 0; i < 40; i++) m *= 3;
      log(m, m | 0, m - Math.floor(m / 7) * 7, Number.isSafeInteger(m));
      let x = 0, steps = 0;
      while (x <= 2147483647) { x += 268435456; steps++; }
      log(x, steps, x | 0);
    }
    run();`,
  'numeric: induction variables modified inside loop bodies': `
    function run(n) {
      const seen = [];
      for (let i = 0; i < n; i++) { if (i % 3 === 0) i += 2; seen.push(i); }
      log(seen.join());
      const s2 = [];
      for (let i = 0; i < n; i++) { s2.push(i); i = i * 2; }
      log(s2.join());
      const s3 = [];
      for (let i = n; i > 0; i--) { s3.push(i); if (i === 5) i -= 0.5; }
      log(s3.join());
      let last = 0;
      for (let i = 2147483640; i < 2147483647; i++) last = i;
      log(last, last + 1, (last + 1) | 0);
      let low = 0, cnt = 0;
      for (let i = -2147483640; i > -2147483648; i--) { low = i; cnt++; }
      log(low, cnt, low - 1);
      const s4 = [];
      for (let i = 0; i < n; i++) { const fn = () => i; s4.push(fn()); i++; }
      log(s4.join());
      let j = 0;
      for (; j < n; j++) if (j === 4) break;
      log(j);
      const s5 = [];
      for (let i = 0; i < 2 * n; i++) { if (i & 1) continue; s5.push(i / 2); }
      log(s5.join());
      let k = 0.5, s6 = [];
      for (k = 0.5; k < n; k++) s6.push(k);
      log(s6.join(), k);
      let big = 0;
      for (let i = 2147483645; i < 2147483647; i++) big = i + i;
      log(big);
    }
    run(10);`,
  'numeric: functions with int params receiving floats and specials': `
    function add(a, b) { return a + b; }
    function mul(a, b) { return a * b; }
    function bits(a) { return a | 0; }
    function half(a) { return a / 2; }
    function cmp(a, b) { return a < b ? -1 : a > b ? 1 : 0; }
    function run() {
      log(add(1, 2), add(2147483647, 1), add(-2147483648, -1), add(0.5, 0.25), add(-0, -0), add(NaN, 1));
      log(mul(65536, 65536), mul(-1, 0), mul(46341, 46341), mul(3, 0.1), mul(1e308, 10));
      log(bits(2147483648), bits(-0.9), bits(4294967297), bits(NaN), bits(1e21), bits(-2147483649));
      log(half(3), half(-1), half(1), half(-0), half(2147483647));
      log(cmp(1, 2), cmp(NaN, 1), cmp(-0, 0), cmp(Infinity, 1e308), cmp(2147483648, 2147483647));
      let acc = 0;
      for (let i = 0; i < 10; i++) acc = add(acc, half(i));
      log(acc);
    }
    run();`,
  'numeric: object and class methods with numeric bodies': `
    const o = {
      wrap(x) { let h = x | 0; h = (h << 5) - h + 7 | 0; return h; },
      frac(x, y) { return x % y; },
      neg(x) { return -x; },
      inc(x) { let v = x; v++; ++v; return v; },
      shr(x) { return x >>> 1; },
      not(x) { return ~x; },
      cmp(x) { return x !== x ? 'nan' : x < 0 ? 'neg' : 'pos'; },
    };
    class M {
      static mix(a, b) { let h = a ^ b; h = Math.imul(h, 0x5bd1e995); h ^= h >>> 15; return h >>> 0; }
      scale(x, k) { return x * k / 3; }
      clamp(x) { return x < 0 ? 0 : x > 255 ? 255 : x; }
    }
    function run() {
      log(o.wrap(2147483647), o.wrap(-1), o.wrap(1e10), o.frac(-4, 2), o.frac(5.5, 2), o.frac(1e10, 7), o.frac(NaN, 2), o.frac(-0, 3));
      log(o.neg(0), o.neg(-0), o.inc(0.5), o.inc(2147483647), o.shr(-1), o.shr(-0.5), o.not(2 ** 32), o.cmp(NaN), o.cmp(-0), o.cmp(-1e-300));
      const m = new M();
      log(M.mix(1, 2), M.mix(-1, 2147483647), m.scale(1, 1), m.scale(-0, 5), m.clamp(-0.5), m.clamp(255.5), m.clamp(NaN));
      const vals = [1.5, -2, 300];
      log(vals.map(m.clamp), vals.map(v => o.frac(v, 1)), vals.map(o.neg));
    }
    run();`,
  // ---------- soundness of the inferred types ----------
  'numeric: var declared under an unbraced if or while stays undefined': `
    function f(c) { if (c) var t = 1.5; return t; }
    function g(c) { while (c) var u = c--; return u; }
    function h(c) { if (c) {} else var w = 2 | 0; log(w, typeof w, w === undefined); }
    function k(c) { lbl: if (c) var z = c * 0.5; return [z, z + 1, String(z)]; }
    function run() {
      log(f(true), f(false), typeof f(0), f(0) === undefined);
      log(g(3), g(0));
      h(true); h(false);
      log(k(4), k(0));
      let x = 0;
      if (x) var y = x | 0;
      log(y, y === undefined, y + 1, y | 0);
    }
    run();`,
  'numeric: top-level functions called from host-created handlers': `
    function add1(x) { return x + 1; }
    function half(x) { return x / 2; }
    function scale(v) { return v * 10; }
    log(add1(2), half(4), scale(3));
    log(this.add1('2'), this.half(-0), this.scale(0.25));
    const app = document.getElementById('app');
    app.innerHTML = "<button id=b2 onclick=\\"log('inline', add1('5'), add1(0.5), scale(0.5))\\">x</button>";
    document.getElementById('b2').click();
    setTimeout("log('timer', add1('7'), half(3))", 0);
    window.__done = new Promise(r => setTimeout(r, 20));`,
  'numeric: slice and subarray results of another element kind': `
    function run() {
      const a = new Uint8Array(4);
      a.constructor = Float32Array;
      const b = a.slice(0, 2), s = a.subarray(0, 1);
      b[0] = 0.5; s[0] = 0.25;
      log(b, s, b[0] ? 'truthy' : 'falsy', s[0] ? 'truthy' : 'falsy', b[0] | 0, b[0] + 1, s instanceof Float32Array);
      const c = new Int16Array(3);
      c.slice = function () { return ['x', 0.25]; };
      const d = c.slice();
      log(d[0] + 1, d[1] ? 'T' : 'F', d.length, d[1] * 4);
    }
    run();`,
  'numeric: Math and parseFloat replaced without a plain member write': `
    Object.assign(Math, { imul: (a, b) => 'imul:' + a + ',' + b, round: x => 'round:' + x });
    Object.defineProperty(Math, 'max', { value: () => 'max!', writable: true, configurable: true });
    function run(x) { log(Math.imul(x, 3), Math.round(x + 0.5), Math.max(x, 1)); }
    run(2);
    Object.assign(window, { parseFloat: s => 'pf:' + s });
    function run2(s) { const v = parseFloat(s); log(v, typeof v); }
    run2('1.5');`,
};

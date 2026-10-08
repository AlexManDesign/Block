// Differential cases aimed at the whole-program type inference (35-types.js)
// and the typed code built on it (34-bindings.js): bindings whose type changes
// over time, vars that may still be undefined, closures and direct calls that
// see a binding in another state than the analysis assumed, functions reached
// from places the closed-world analysis does not see, and coercions between
// numbers, booleans and strings.
export const cases = {
  // ---------- one binding, several types over time ----------
  'inference: number variable later becomes string, undefined, object': `
    function run() {
      let v = 1;
      log(v + 1, typeof v);
      v = v / 4; log(v, typeof v);
      v = 'str'; log(v + 1, typeof v, v.length);
      v = undefined; log(v + 1, typeof v);
      v = { valueOf() { return 41; } }; log(v + 1, typeof v);
      v = null; log(v + 1, v * 2);
      let late = 0;
      for (let i = 0; i < 3; i++) late = i === 2 ? 'last' : late + 0.5;
      log(late, typeof late);
      let flip = 0;
      const step = () => { flip = flip === 0 ? false : flip === false ? '' : 0; return flip; };
      log(step(), step(), step(), step(), typeof flip);
    }
    run();`,

  'inference: var read before its declaration and outside its block': `
    function before() { const seen = [typeof x, x]; var x = 5; seen.push(x); return seen; }
    function loopVar(n) { for (var i = 0; i < n; i++) { var last = i * 1.5; } return [i, last, typeof last]; }
    function blockVar(c) { { var inner = 7; } if (c) { var other = 'o'; } return [inner, other]; }
    function redeclared() { var r = 1; var r = 'two'; return r; }
    function doWhile(n) { do { var d = n; } while (d > 100); return d; }
    log(before(), loopVar(3), loopVar(0), blockVar(true), blockVar(false), redeclared(), doWhile(2));
    function catchVar() {
      try { throw 1; } catch (e) { var e = 5; log('in catch', e); }
      return e;
    }
    log(catchVar());`,

  // ---------- closures and captured counters ----------
  'inference: let in loops captured by closures and modified': `
    const fs = [];
    for (let i = 0; i < 3; i++) fs.push(() => (i += 10));
    log(fs.map(f => f()), fs.map(f => f()));
    const ts = [];
    for (let i = 0; ts.push(() => i), i < 2; i++) {}
    log(ts.map(f => f()));
    for (let i = 0; i < 10; i++) { const bump = () => { i += 3; }; if (i % 2 === 0) bump(); log('i', i); }
    const gs = [];
    for (let i = 0; i < 4; i++) { if (i === 1) continue; gs.push(() => i * 0.5); }
    log(gs.map(g => g()));
    const hs = [];
    for (let k = 0; k < 3; k++) { let local = k; hs.push(() => local++); }
    log(hs.map(h => h()), hs.map(h => h()));`,

  'inference: closure from a for-let initializer called directly': `
    const out = [];
    for (let i = 0, g = () => i; i < 3; i++) out.push(g());
    log(out);
    const out2 = [], keep = [];
    for (let i = 0, g = () => i * 10; i < 3; i++) { keep.push(g); out2.push(g()); }
    log(out2, keep.map(f => f()));`,

  'inference: captured counter changes type inside closures': `
    let counter = 0;
    const inc = () => ++counter, incBy = d => (counter += d), reset = () => { counter = 'reset'; };
    log(inc(), inc(), incBy(0.5), counter, typeof counter);
    reset(); log(counter, typeof counter);
    log(inc(), typeof counter);
    function makeAcc() {
      let total = 0, label = 'acc';
      return { add(x) { total += x; return total; }, name() { label += total; return label; }, get: () => total };
    }
    const acc = makeAcc();
    log(acc.add(1), acc.add('2'), acc.get(), typeof acc.get(), acc.name(), acc.add(3));`,

  'inference: compound assignment whose right side changes the variable': `
    let x = 1;
    function bumpX() { x = 10; return 1; }
    x += bumpX(); log(x);
    let y = 2;
    const setY = () => { y = 'changed'; return 3; };
    y = y * setY(); log(y, typeof y);
    let z = 5;
    const o = { valueOf() { z = 100.5; return 1; } };
    log(z + (o - 0), z, o * z, z);`,

  // ---------- calls: arguments, defaults, escapes ----------
  'inference: one function called with numbers, strings, undefined, objects': `
    function twice(x) { return x * 2; }
    log(twice(2), twice(2.5), twice('3'), twice(true), twice(undefined), twice(null), twice({ valueOf: () => 4 }), twice([5]));
    function add(a, b) { return a + b; }
    log(add(1, 2), add('a', 1), add(1), add(1, 2, 3), add(true, true), add(1.5, -1.5), add([1], [2]), add({}, 1));
    function id(v) { return v; }
    const r1 = id(1), r2 = id('s'), r3 = id(false);
    log(r1 + 1, r2 + 1, r3 + 1, typeof r1, typeof r2, typeof r3);
    function flagOrNum(f) { return f ? 1 : 0.5; }
    log(flagOrNum(true), flagOrNum(0), flagOrNum(''), flagOrNum('x'), flagOrNum(NaN), flagOrNum(-0));`,

  'inference: fewer and more arguments and default parameters': `
    function def(a, b = a + 1, c = b * 2) { return [a, b, c]; }
    log(def(1), def(1, 5), def(1, undefined, 3), def('x'), def(1, null), def());
    function fewer(a, b, c) { return [typeof a, typeof b, typeof c, a + b]; }
    log(fewer(1), fewer(1, 'b'), fewer(1, 2, 3));
    let side = 0;
    function extra(a) { return a; }
    log(extra(1, side++, side += 0.5), side);
    function dflt(n = 10) { return n * 2; }
    log(dflt(), dflt(undefined), dflt(null), dflt(0), dflt(2.5), dflt('4'));
    const ta = new Float64Array(1);
    log(dflt(ta[5]), dflt(ta[0]));`,

  'inference: functions stored in variables, objects, arrays and passed to host': `
    function add(a, b) { return a + b; }
    function twice(x) { return x * 2; }
    log(add(1, 2), twice(4));
    const ops = { add, mul: (a, b) => a * b };
    log(ops.add(2, 3), ops.mul(2, '4'), ops['add']('x', 'y'));
    const arr = [add, twice]; log(arr[0](1, 1), arr[1]('5'));
    let fnv = add; log(fnv(10, 20)); fnv = twice; log(fnv(10, 20));
    const pick = c => c ? add : twice;
    log(pick(true)('a', 'b'), pick(false)(true));
    log(add.call(null, 'c', 1), twice.apply(null, [2.5]), add.bind(null, 1)(1));
    function sorter(a, b) { return a - b; }
    log([3, 1, 2].sort(sorter), sorter(5, 2), sorter('5', '2'));
    function onlyCb(x) { return x + 1; }
    document.getElementById('btn').addEventListener('click', e => log('click', onlyCb(e.type)));
    log(onlyCb(1));
    document.getElementById('btn').click();
    function each(v, i) { return typeof v + i; }
    log(['a', 2].map(each), each(1, 1), [1.5].forEach(each));
    function reviver(k, v) { return typeof v === 'number' ? v * 10 : v; }
    log(JSON.parse('{"a":1,"b":[2,"x"]}', reviver), reviver('k', 3));`,

  // ---------- recursion and returns ----------
  'inference: recursion and mutual recursion returning numbers or undefined': `
    function fact(n) { return n <= 1 ? 1 : n * fact(n - 1); }
    log(fact(5), fact(20), fact(0), fact(25));
    function isEven(n) { if (n === 0) return true; return isOdd(n - 1); }
    function isOdd(n) { if (n === 0) return false; return isEven(n - 1); }
    log(isEven(10), isOdd(7), isEven(3));
    function find(a, i, x) { if (i >= a.length) return; if (a[i] === x) return i; return find(a, i + 1, x); }
    log(find([4, 5, 6], 0, 6), find([4], 0, 9));
    function depth(n) { if (n > 0) return depth(n - 1) + 1; return 0; }
    log(depth(10));
    function ping(n) { return n <= 0 ? 'done' : pong(n - 1); }
    function pong(n) { return n <= 0 ? 0 : ping(n - 1); }
    log(ping(3), ping(4), pong(3), typeof pong(1));
    function fib(n) { return n < 2 ? n : fib(n - 1) + fib(n - 2); }
    log(fib(20), fib(1.5), fib(-1));`,

  'inference: stack overflow in deep recursion is catchable': `
    function deep(n) { return n === 0 ? 0 : 1 + deep(n - 1); }
    function wrap() { try { return deep(1e6); } catch (e) { return 'caught ' + e.name; } }
    log(deep(1000), wrap());
    const o = { deep(n) { return n === 0 ? 'bottom' : o.deep(n - 1); } };
    try { log(o.deep(1e6)); } catch (e) { logError(e); }
    log('after', o.deep(3));`,

  'inference: functions that sometimes fall off the end': `
    function fallOff(x) { if (x > 0) return x * 2; }
    log(fallOff(2), fallOff(-1), fallOff(0.5));
    function maybe(x) { if (x === 1) return 1; else if (x === 2) return 2; }
    log(maybe(1), maybe(2), maybe(3));
    function sw(x) { switch (x) { case 1: return 'one'; case 2: return 2; } }
    log(sw(1), sw(2), sw(3));
    function loopy(n) { while (true) { if (n > 3) break; if (n === 3) return n; n++; } }
    log(loopy(0), loopy(5));
    function lbl(x) { a: { if (x) break a; return 1; } }
    log(lbl(true), lbl(false));
    function thrower(x) { if (x) return 7; throw new RangeError('no'); }
    try { log(thrower(1)); log(thrower(0)); } catch (e) { logError(e); }
    const arrow = x => { if (x) return 1.5; };
    log(arrow(true), arrow(false), arrow(true) + 1, arrow(false) + 1);
    function bare(x) { if (x) return; return x * 3; }
    log(bare(true), bare(0), bare(false));`,

  'inference: return inside try/finally': `
    function tf(x) {
      try { if (x > 0) return x * 2; } finally { log('finally', x); }
      return -1;
    }
    log(tf(3), tf(-3), tf(1.5));
    function tf2(x) {
      try { if (x) return 1; throw new Error('e'); } catch (e) { return 2; } finally { if (x === 5) return 5.5; }
    }
    log(tf2(1), tf2(0), tf2(5));
    function tf3(x) {
      let r = 0;
      try { r = 1; return r; } finally { r = 'changed'; log('r', r); }
    }
    log(tf3());
    function tf4(n) {
      for (let i = 0; i < n; i++) { try { if (i === 2) return i; continue; } finally { log('f', i); } }
      return 'none';
    }
    log(tf4(5), tf4(1));`,

  'inference: return inside for-of': `
    function firstBig(arr) { for (const v of arr) { if (v > 2) return v * 1; } return 0; }
    log(firstBig([1, 2, 3, 4]), firstBig([1]), firstBig(new Set([5])), firstBig('7'));
    function idx(arr, x) { let i = 0; for (const v of arr) { if (v === x) return i; i++; } }
    log(idx([5, 6, 7], 7), idx([5], 9));
    function nested(n) {
      outer: for (const a of [1, 2, 3]) {
        for (const b of [10, 20]) { if (a * b === n) return a + b; if (b === 20 && a === 2) continue outer; }
      }
      return false;
    }
    log(nested(20), nested(30), nested(7));
    let closed = 0;
    const iterable = { [Symbol.iterator]() { let k = 0; return { next: () => ({ value: k++, done: k > 5 }), return() { closed++; return {}; } }; } };
    function early(limit) { for (const v of iterable) if (v === limit) return v / 2; return -1; }
    log(early(2), early(9), closed);`,

  // ---------- booleans, numbers, strings mixed ----------
  'inference: booleans used as numbers and numbers as booleans': `
    let t = true, f = false;
    log(t + 1, t + t, f - 1, -f, Object.is(-f, -0), +t, ~t, t * 3, t / 0, f / 0, t % 2, t ** 2, t << 4, t | f, t & 1, t ^ t);
    log(t + 'x', f + '', \`\${t}\${f}\`, t + null, f + undefined);
    let e = 0; e = !e; log(e, typeof e);
    let n = 1; n = n > 0; log(n, typeof n);
    let k = 5; if (k) log('k truthy'); k = 0; if (!k) log('k falsy'); k = NaN; if (!k) log('NaN falsy'); k = -0; if (!k) log('-0 falsy');
    let cnt = 0; for (let i = 0; i < 5; i++) cnt += (i & 1) === 1; log(cnt, typeof cnt);
    let sum = 0; for (const x of [true, false, true]) sum += x; log(sum);
    const flags = [1 > 0, 0 > 1]; log(flags[0] + flags[1], flags[0] * 2.5);
    function asNum(b) { return b * 1; } log(asNum(true), asNum(false), asNum(3 < 4));`,

  'inference: comparisons mixing numbers, booleans and strings': `
    function cmpAll(a, b) { return [a < b, a > b, a <= b, a >= b, a == b, a != b, a === b].map(Number).join(''); }
    log(cmpAll(1, '2'), cmpAll('10', 9), cmpAll(true, '1'), cmpAll('a', true), cmpAll(null, 0), cmpAll(undefined, 0), cmpAll([2], 1), cmpAll(NaN, NaN));
    let n = 1, t = true, s = '1', f = 1.5, z = -0;
    log(n < s, n == s, n === s, t == s, t < f, z == 0, z === 0, s > z, t >= n, f > t, s < 'a', n + t + s, s + n + t);
    log(n < t, t < n, t <= n, n == t, n != t, f != t, !n == !t, (n > 0) === t, (n > 0) == n);
    let big = '9', small = '10';
    log(big > small, +big > +small, big > 10, small == 10, small === 10);`,

  'inference: ++ and compound assignment on booleans and strings': `
    let b = true; b++; log(b, typeof b);
    let c = false; c += 1; log(c, typeof c);
    let d = true; d = d && 5; log(d);
    let s = '5'; s++; log(s, typeof s);
    let s2 = '5'; s2 += 1; log(s2, typeof s2); s2 -= 1; log(s2, typeof s2);
    let s3 = 'x'; s3 += 1; s3 += true; s3 += null; s3 += undefined; s3 += -0; s3 += 1.5; log(s3);
    let w = 'ab'; w *= 2; log(w); let w2 = '6'; w2 /= 2; log(w2, typeof w2);
    let x = '10'; x = x - 1; log(x, typeof x); x = x + '1'; log(x, typeof x);
    let m = 7; m += ''; log(m, typeof m, m.length);
    let q = 0; q ||= 'fallback'; log(q); let r = 1; r &&= 'yes'; log(r); let u; u ??= 2.5; log(u + 1);`,

  // ---------- globals and functions replaced ----------
  'inference: functions redeclared': `
    function g() { return 'g1'; }
    function g() { return 'g2'; }
    log(g());
    function outer() {
      function h(a) { return a * 2; }
      const r1 = h(2);
      function h(a) { return a + '!'; }
      return [r1, h(3)];
    }
    log(outer());
    function mixed() { var q = 1; function q() {} return typeof q; }
    log(mixed());
    function mixed2() { var q = 1.5; return [typeof q, q]; function q() {} }
    log(mixed2());
    function f(x) { return x + 1; }
    log(f(1), f(2.5));
    </script>
    <script>
    log(f(3));
    function f(x) { return 'second ' + x; }
    log(f(4));`,

  'inference: parameter shadowed by an inner function declaration': `
    function shadow(p) { function p() { return 'fn'; } return typeof p; }
    try { log(shadow(5)); } catch (e) { logError(e); }
    function shadowCall(n) { function n() { return 'inner'; } return n(); }
    try { log(shadowCall(1.5)); } catch (e) { logError(e); }`,

  'inference: global function reassigned via window and another script': `
    function f(x) { return x + 1; }
    log(f(1));
    var later = function () { return 'var-fn'; };
    log(later());
    window.f = function (x) { return 'window ' + x; };
    log(f(2));
    </script>
    <script>
    log(f(3));
    later = () => 'reassigned';
    log(later());
    var f = x => 'var ' + x;
    log(f(4));`,

  'inference: global function replaced through this and document.defaultView': `
    function f(x) { return x + 1; }
    log(f(1));
    this.f = function (x) { return 'this ' + x; };
    log(f(2));
    function g(x) { return x * 2; }
    log(g(1));
    document.defaultView.g = function (x) { return 'view ' + x; };
    log(g(2));`,

  'inference: global function replaced by uncompiled handler code': `
    function g(x) { return x * 2; }
    log(g(1));
    const app = document.getElementById('app');
    app.setAttribute('onclick', 'g = function (x) { return "handler " + x; }');
    app.click();
    log(g(3));`,

  'inference: lexical binding declared in a later script': `
    function readLater() { return later * 2; }
    function readName() { return name2 + '!'; }
    function makeLater() { return new Later(); }
    try { log(readLater()); } catch (e) { logError(e); }
    </script>
    <script>
    let later = 21;
    const name2 = 'n';
    class Later {}
    log(later, name2);
    try { log(readLater()); } catch (e) { logError(e); }
    try { log(readName()); } catch (e) { logError(e); }
    try { log(makeLater() instanceof Later); } catch (e) { logError(e); }`,

  'inference: function declared in a later script called with other types': `
    function callLater(v) { return later(v); }
    function grab() { return later; }
    </script>
    <script>
    function later(x) { return x + 1; }
    log(later(1), later(2));
    log(callLater('s'), callLater(true), callLater(undefined), grab()('g'), [5, 'x'].map(grab()));`,

  'inference: top-level let and const used by uncompiled handler code': `
    let score = 5;
    const LIMIT = 3;
    const app = document.getElementById('app');
    app.setAttribute('onclick', 'try { log("handler sees", score, LIMIT); score = "str"; } catch (e) { logError(e); }');
    app.click();
    log(score, typeof score, score + 1);`,

  'inference: direct call of a let/const function in its TDZ': `
    function outer() {
      try { log('early', f(1)); } catch (e) { logError(e); }
      const f = x => x + 1;
      return f(2);
    }
    log(outer());
    function run() { return helper(3); }
    try { log(run()); } catch (e) { logError(e); }
    const helper = x => x * 10;
    log(run());
    function sw(k) {
      switch (k) {
        case 0: try { log('case0', g(1)); } catch (e) { logError(e); }
        case 1: const g = x => x * 2; log('case1', g(2)); break;
        case 2: try { log('case2', g(3)); } catch (e) { logError(e); }
      }
    }
    sw(0); sw(1); sw(2);`,

  // ---------- typeof and global constants ----------
  'inference: typeof on typed variables': `
    function run() {
      const ta = new Int32Array(2);
      let i = 1, f = 1.5, b = true, s = 's', u = ta[5], e = ta[0], fn = () => 1, nul = null, o = {};
      log(typeof i, typeof f, typeof b, typeof s, typeof u, typeof e, typeof fn, typeof nul, typeof o, typeof ta);
      log(typeof i === 'number', typeof b === 'boolean', typeof s === 'string', typeof u === 'undefined', typeof e === 'number',
        typeof fn === 'function', typeof nul === 'object', typeof o !== 'object', 'number' == typeof f, typeof ta[9] === 'undefined');
      log(typeof document.all, typeof document.all === 'undefined', typeof undeclaredName === 'undefined', typeof Math.max === 'function');
      i = 'now string'; log(typeof i === 'string', typeof (i + 1), typeof (b + 1), typeof (s * 1), typeof -b, typeof !i);
      class K {} log(typeof K === 'function', typeof new K(), typeof Symbol() === 'symbol', typeof 1n === 'bigint');
    }
    run();`,

  'inference: NaN, Infinity and undefined globals and shadowing': `
    log(NaN | 0, Infinity | 0, -Infinity, Infinity - Infinity, undefined + 1, undefined * 2, NaN === NaN, Infinity > 1e308);
    function shadowNaN(NaN) { return NaN + 1; }
    function shadowUndef(undefined) { return undefined; }
    log(shadowNaN(1), shadowNaN('x'), shadowUndef(3), shadowUndef());
    function sloppySet() { undefined = 5; NaN = 1; Infinity = 0; return [undefined, NaN, Infinity]; }
    log(sloppySet());
    function strictSet() { 'use strict'; try { NaN = 1; } catch (e) { logError(e); } }
    strictSet();
    { let Infinity = 'local'; log(Infinity); }
    let inf = 1 / 0, nan = 0 / 0;
    log(inf === Infinity, nan !== nan, isNaN(nan), isFinite(inf), Math.max(nan, 1), Math.min(inf, 2), [nan].includes(NaN), [nan].indexOf(NaN));`,

  // ---------- generators and async ----------
  'inference: generator shares variables with normal functions': `
    function outer() {
      let n = 0, s = 'a', flag = true;
      function* gen() { while (n < 3) { n++; s += n; flag = !flag; yield n; } return s; }
      function bump() { n += 0.5; s = s + '|'; return n; }
      function sq(x) { return x * x; }
      function* gen2(k) { yield sq(k); yield sq(n); yield sq('3'); }
      const it = gen();
      log(it.next().value, bump(), it.next().value, n, s, flag);
      log(it.next().value, it.next().value, n, s, typeof n, typeof s, typeof flag);
      log([...gen2(2)], sq(4));
    }
    outer();`,

  'inference: async functions share variables with normal ones': `
    let shared = 0;
    async function incAsync() { await null; shared += 2; return shared; }
    function incSync() { shared += 1; return shared; }
    async function af() { let k = 1; await null; k += 1.5; return k; }
    function plain(x) { return x * 3; }
    window.__done = (async () => {
      const p = incAsync(); log(incSync(), shared);
      log(await p, shared, await af());
      let mix = 1; const g2 = async () => { mix = mix + 'x'; }; await g2(); log(mix, typeof mix);
      log(plain(await Promise.resolve(2)), plain(1.5));
      let st = 'p'; const waiters = [1, 2].map(async v => { await null; st += v; return st; });
      log(st, await Promise.all(waiters), st);
    })();`,

  // ---------- loops ----------
  'inference: numeric loop variables used after the loop': `
    var i;
    for (i = 0; i < 3; i++) {}
    log(i);
    function f() { for (var j = 0; j < 4; j++) {} return j; }
    function g() { let k = 0; for (; k < 5; k++) { if (k === 3) break; } return k; }
    function h(n) { let k; for (k = 0; k < n; k++); return [k, typeof k]; }
    function w() { let a = 0; while (a < 2.5) a += 1; return a; }
    function d() { var x = 1; for (var x = 0.5; x < 3; x++); return x; }
    function e() { for (let i = 10; i > 0; i--) { if (i === 7) return i; } }
    function last() { let i = 0; const fs = []; for (; i < 3; i++) fs.push(() => i); return [i, fs.map(f => f())]; }
    function big() { let c = 0, i = 2147483640; for (; i < 2147483647; i++) c++; return [c, i, i + 1]; }
    function down() { let c = 0, i = -2147483640; for (; i > -2147483648; i--) c++; return [c, i, i - 1]; }
    log(f(), g(), h(3), h(-1), w(), d(), e(), last(), big(), down());`,

  // ---------- strings ----------
  'inference: string methods on values that are sometimes not strings': `
    function up(x) { return x.toUpperCase(); }
    log(up('a'));
    try { up(5); } catch (e) { logError(e); }
    function len(x) { return x.length; }
    log(len('abc'), len([1, 2]), len({ length: 'L' }), len(function (a, b) {}));
    function first(x) { return x.charAt ? x.charAt(0) : x[0]; }
    log(first('xyz'), first(['q']), first({ 0: 'z' }));
    let s = 'abc';
    log(s.indexOf('c'), s.slice(1));
    s = 12345; log(String(s).slice(1), s.toString().length);
    s = ['a', 'b', 'c']; log(s.indexOf('c'), s.slice(1), s.includes('b'), s.concat('d'));
    s = null; try { s.slice(1); } catch (e) { logError(e); }`,

  'inference: string methods with non-string arguments': `
    const s = 'a5bnullundefinedtrue1.5';
    log(s.indexOf(5), s.indexOf(null), s.indexOf(undefined), s.indexOf(), s.includes(true), s.includes(1.5), s.lastIndexOf(5, '3'), s.startsWith(5, 1), s.endsWith(true));
    log(s.charCodeAt('1'), s.charAt('2'), s.charAt(true), s.slice('1', '3'), s.slice(true), s.substring(null, 2));
    log('ab'.repeat('2'), 'ab'.repeat(true), 'ab'.concat(1, null, true, undefined), 'x'.padStart(4, 0), 'x'.padEnd(3, 12));
    log(s.slice(-3.7), s.slice(2.9, 4.1), s.substring(-5, 2), s.substring(4, 1), s.charAt(-1), s.charCodeAt(99), s.charAt(1e10), s.slice(NaN, Infinity));
    log(s.indexOf('b', -5), s.indexOf('b', NaN), s.lastIndexOf('n', NaN), s.lastIndexOf('n', -Infinity), s.includes('a', 1.9), s.startsWith('5', 1.2));
    const obj = { valueOf() { log('valueOf'); return 2; }, toString() { log('toString'); return 'b'; } };
    log(s.slice(obj), s.indexOf(obj), s.charAt(obj), s.includes(obj), s.concat(obj));`,

  'inference: string slice, substring and endsWith with an undefined end': `
    const s = 'hello';
    log(s.slice(1, undefined), s.substring(1, undefined), s.endsWith('o', undefined), s.slice(undefined, 2), s.charAt(undefined));
    const ta = new Int32Array(2);
    log(s.slice(1, ta[5]), s.substring(2, ta[7]));
    function end(x) { return x; }
    log(s.slice(2, end(undefined)), s.slice(2, end(3)), s.slice(1, NaN), s.substring(3, NaN));`,

  'inference: String.prototype patched through an alias': `
    const proto = Object.getPrototypeOf('');
    const orig = proto.indexOf;
    proto.indexOf = function () { return 42; };
    const s = 'abc';
    log(s.indexOf('b'));
    proto.indexOf = orig;
    log(s.indexOf('b'));
    const viaCtor = 'x'.constructor.prototype, origSlice = viaCtor.slice;
    viaCtor.slice = function () { return 'sliced!'; };
    log(s.slice(1));
    viaCtor.slice = origSlice;
    log(s.slice(1));`,

  // ---------- typed arrays, keys, number formatting ----------
  'inference: typed array elements that may be undefined flow into variables': `
    const ta = new Int32Array([7, 0, -3]);
    const tf = new Float64Array([0.5, NaN, -0]);
    let a = ta[5], b = tf[1], c = tf[2];
    log(a, b, c, typeof a, a + 'x', b + 'x', c + 'x', \`\${a}|\${c}\`);
    log(ta[9] ?? 'dflt', ta[1] ?? 'dflt', ta[9] || 'or', ta[1] || 'or', tf[0] && 'and', tf[1] && 'and');
    log(ta[9] === undefined, ta[9] == null, ta[9] !== ta[9], tf[1] !== tf[1], ta[9] == 0, ta[1] == false, ta[9] == false, ta[0] === 7);
    function pick(i) { return ta[i]; }
    log(pick(0), pick(10), pick(10) === undefined, String(pick(10)));
    let acc = ta[8]; acc++; log(acc);
    let v = 1; v = ta[9]; log(v, v + 1, v === undefined);
    const cond = ta[9] ? 'yes' : 'no', cond2 = tf[1] ? 'yes' : 'no', cond3 = tf[0] ? 'yes' : 'no';
    log(cond, cond2, cond3);`,

  'inference: typed numbers used as keys and in collections': `
    let x = 0.5; x = x * 2;
    switch (x) { case 1: log('one'); break; default: log('other'); }
    const m = new Map([[1, 'a'], [NaN, 'nan'], [0, 'zero']]);
    let z = -0, nn = 0 / 0, inf = 1 / 0, big = 2 ** 32, frac = 1.5, huge = 1e21;
    log(m.get(x), m.get(nn), m.get(z), [1, 2].indexOf(x), [1].includes(x), [NaN].includes(nn), [NaN].indexOf(nn), x === 1, Object.is(x, 1));
    const s = new Set([x, 1, z, 0, nn, NaN]); log(s.size);
    const o = {}; o[z] = 'negzero'; o[nn] = 'nan'; o[inf] = 'inf'; o[big] = 'big'; o[frac] = 'frac'; o[huge] = 'huge'; o[x] = 'one';
    log(Object.keys(o), o[0], o['NaN'], o.Infinity, o['4294967296'], o['1.5'], o['1e+21'], o[1]);
    const a = [10, 20, 30]; log(a[z], a[x], a[frac], a[nn], a[-1 + x]);
    const t = true; log(a[t], o[t], ({ true: 'T' })[t]);`,

  'inference: -0 and number formatting through typed paths': `
    let z = -0;
    log('' + z, \`\${z}\`, z + '', String(z), [z] + '', z.toString(), Object.is(z, -0), 1 / z);
    const vals = [1e21, 1.7976931348623157e308, 5e-324, 123e-20, 0.000001, 1e-7, -1e-7, 2 ** 53 + 2, 4294967295, -2147483648, 1 / 3, 1e300 * 1e10, -1 / 0, 0 / 0, 0.1 * 3];
    let out = '';
    for (let i = 0; i < vals.length; i++) { let v = 0.5; v = vals[i] * 1; out += v + ';'; }
    log(out);
    let i = 2147483647; i = i + 1; log(i, i + '', (i | 0) + '');
    let acc = ''; for (let k = -3; k < 3; k++) { const q = k / 4; acc += \`\${q}|\` + q + ','; } log(acc);`,

  // ---------- call sites the analysis must see ----------
  'inference: call sites in class fields, static blocks, defaults and keys': `
    function k(x) { return typeof x + '=' + x; }
    log(k(1));
    class A {
      static s = k('field');
      f = k(true);
      static { log('static', k(null)); }
      [k(2.5)]() { return 1; }
      get g() { return k([1]); }
    }
    log(A.s, new A().f, Object.getOwnPropertyNames(A.prototype), new A().g);
    function d(a = k({})) { return a; }
    log(d(), \`\${k(undefined)}\`);
    const o = { [k(-0)]: 1, get h() { return k(1n); } };
    log(Object.keys(o), o.h);
    function* gen() { yield k('gen'); }
    log([...gen()]);
    </script>
    <div id="h1" onclick="log('handler', k('str'), k(event.type))"></div>
    <script>
    document.getElementById('h1').click();
    log(k(3));`,

  'inference: TDZ of typed lets read by closures and hoisted functions': `
    function getX() { return x + 1; }
    try { log(getX()); } catch (e) { logError(e); }
    let x = 5;
    log(getX());
    function outer() {
      const g = () => y + 1;
      try { log(g()); } catch (e) { logError(e); }
      let y = 1.5;
      return g();
    }
    log(outer());
    function inner() {
      function h() { return z * 2; }
      try { log(h()); } catch (e) { logError(e); }
      const z = 4;
      return h();
    }
    log(inner());
    function typeofTdz() { try { log(typeof w); } catch (e) { logError(e); } let w = 1; return typeof w; }
    log(typeofTdz());`,
};

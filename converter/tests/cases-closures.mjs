// Functions created by compiled code are WASM closures until they reach the host.
export const cases = {
  'closures: identity and typeof': `
const fs = [];
for (let i = 0; i < 3; i++) fs.push(x => x + i);
log(typeof fs[0], fs[0] === fs[0], fs[0] === fs[1], fs[0] == fs[0], fs[0] !== fs[1]);
log(fs.map(f => f(10)));
const m = new Map(); m.set(fs[1], 'one'); log(m.get(fs[1]), m.has(fs[0]), m.size);
const s = new Set(fs); s.add(fs[0]); log(s.size);
log(fs.indexOf(fs[2]), fs.includes(fs[1]));
function same(a) { return a; }
log(same(fs[0]) === fs[0], [fs[0]][0] === fs[0], ({ f: fs[0] }).f === fs[0]);
if (fs[0]) log('truthy'); log(!fs[0], fs[0] ? 1 : 2, fs[0] && 'and', fs[0] || 'or', fs[0] ?? 'nn');`,

  'closures: counters and shared scope': `
function makeCounter() { let n = 0; return { inc() { n++; return n; }, get() { return n; }, add: d => (n += d) }; }
const a = makeCounter(), b = makeCounter();
a.inc(); a.inc(); b.inc(); a.add(10);
log(a.get(), b.get(), Object.keys(a), typeof a.inc, a.inc.name, a.add.name, a.inc.length, a.add.length);
const fs = []; for (var i = 0; i < 3; i++) fs.push(() => i); log(fs.map(f => f()));
const gs = []; for (let j = 0; j < 3; j++) gs.push(() => j++); log(gs.map(g => g()), gs.map(g => g()));
let total = 0; const step = v => { total += v; return step; }; step(1)(2)(3); log(total);`,

  'closures: this binding': `
'use strict';
const o = { v: 1, get() { return this && this.v; }, arrow: () => typeof this, nested() { return [1, 2].map(x => this.v + x); } };
log(o.get(), o.arrow(), o.nested());
const g = o.get; log(g());
const p = { v: 2, get: o.get }; log(p.get());
function strictThis() { return this; }
log(strictThis(), strictThis.call(5), typeof strictThis.call('s'));
class C { constructor() { this.v = 7; this.f = () => this.v; } m() { return this.v; } }
const c = new C(); const f = c.f; log(f(), c.m(), C.prototype.m.call({ v: 9 }));`,

  'closures: sloppy this': `
var self = this;
function sloppy() { return this === self ? 'global' : typeof this; }
const holder = { sloppy };
log(sloppy(), holder.sloppy() === 'object', sloppy.call(null), sloppy.call(undefined), sloppy.call(1), sloppy.call('x'), sloppy.call(true));
const make = () => function () { return typeof this; };
const h = make(); log(h(), h.call(4), ({ h }).h());
function outer() { return (function () { return this === self; })(); } log(outer());
function wrapper() { const inner = () => this; return inner(); }
log(wrapper() === self, wrapper.call(holder) === holder, typeof wrapper.call(3));`,

  'closures: passed to host': `
const nums = [5, 1, 4, 2, 3];
log(nums.slice().sort((a, b) => a - b), nums.map((x, i) => x * i), nums.filter(x => x & 1), nums.reduce((s, x) => s + x, 0));
log(nums.findIndex(x => x === 4), nums.some(x => x > 4), nums.every(x => x > 0), JSON.stringify({ a: 1, b: [2] }, (k, v) => typeof v === 'number' ? v * 10 : v));
let calls = 0; const cb = () => calls++;
document.getElementById('btn').addEventListener('click', cb);
document.getElementById('btn').click();
document.getElementById('btn').removeEventListener('click', cb);
document.getElementById('btn').click();
log(calls);
const fns = { a: () => 1 }; log(JSON.stringify(fns), Object.values(fns).length, typeof Object.values(fns)[0]);
log(Array.from({ length: 3 }, (_, i) => i * i), [1, 2, 3].forEach(x => calls += x), calls);
window.__done = new Promise(r => setTimeout(() => { log('timeout', calls); r(); }, 0));`,

  'closures: properties name length new': `
const add = function (a, b) { return a + b; };
const arrow = (a, b, ...rest) => rest.length;
log(add.name, add.length, arrow.name, arrow.length, arrow(1, 2, 3, 4, 5));
add.cache = { hits: 0 }; add.cache.hits++; log(add.cache.hits, Object.keys(add), 'cache' in add);
function Point(x, y) { this.x = x; this.y = y; }
const P = function (x) { this.x = x; };
const p = new P(3); log(p.x, p instanceof P, Object.getPrototypeOf(p) === P.prototype, typeof P.prototype);
P.prototype.twice = function () { return this.x * 2; }; log(p.twice(), new P(5).twice());
try { new arrow(); } catch (e) { logError(e); }
const meth = { m() { return 1; } }.m; try { new meth(); } catch (e) { logError(e); }
log(Object.getOwnPropertyNames(arrow).sort(), Object.getOwnPropertyNames(meth).sort(), Object.getOwnPropertyNames(P).sort());`,

  'closures: call apply bind': `
function sum(a, b, c) { return (this ? this.base : 0) + a + b + (c || 0); }
const inner = function (x) { return x * 2; };
const o = { base: 100 };
log(sum.call(o, 1, 2), sum.apply(o, [1, 2, 3]), inner.call(null, 4), inner.apply(undefined, [5]));
const bound = inner.bind(null, 21); log(bound(), bound.name, bound.length);
const arrowB = (x => x + 1).bind(null); log(arrowB(1));
const partial = sum.bind(o, 1); log(partial(2), partial(2, 3));
log(Function.prototype.call.call(inner, null, 7), Reflect.apply(inner, null, [8]));`,

  'closures: rest, defaults and arguments counts': `
const r = (...xs) => xs;
log(r(), r(1), r(1, 2, 3), r(undefined), r.length);
const d = (a, b = a + 1, ...c) => [a, b, c];
log(d(1), d(1, 5), d(1, 5, 6, 7), d.length);
const few = (a, b, c) => [a, b, c]; log(few(1), few(1, 2, 3, 4));
const spreadCall = (...xs) => xs.length; const args = [1, 2, 3]; log(spreadCall(...args), spreadCall(0, ...args, 4));
function classic() { return arguments.length; } const viaVar = classic; log(viaVar(1, 2), viaVar());
const many = (a, b, c, d, e, f, g, h, i, j) => [a, j]; log(many(1, 2, 3, 4, 5, 6, 7, 8, 9, 10), many(1, 2, 3, 4, 5, 6, 7, 8));`,

  'closures: recursion and higher order': `
const fib = n => n < 2 ? n : fib(n - 1) + fib(n - 2);
log(fib(15));
const compose = (...fns) => x => fns.reduceRight((v, f) => f(v), x);
const inc = x => x + 1, dbl = x => x * 2;
log(compose(inc, dbl)(5), compose(dbl, inc)(5), compose()(3));
const memo = f => { const cache = new Map(); return n => { if (!cache.has(n)) cache.set(n, f(n)); return cache.get(n); }; };
const slow = memo(n => n * n); log(slow(4), slow(4), slow(5));
const curry = a => b => c => a + b + c; log(curry(1)(2)(3), curry('a')('b')('c'));
let depth = 0; const walk = (node) => { depth++; (node.children || []).forEach(walk); };
walk({ children: [{ children: [{}, {}] }, {}] }); log(depth);
const ops = { '+': (a, b) => a + b, '*': (a, b) => a * b };
log(['+', '*'].map(k => ops[k](3, 4)), ops['+'].name);`,

  'closures: exceptions through closures': `
const thrower = () => { throw new RangeError('x'); };
try { thrower(); } catch (e) { logError(e); }
const safe = f => { try { return f(); } catch (e) { return 'caught ' + e.message; } finally { log('finally'); } };
log(safe(() => { throw new Error('boom'); }), safe(() => 5));
try { [1].map(() => { throw new TypeError('in map'); }); } catch (e) { logError(e); }
const o = { f: null }; try { o.f(); } catch (e) { logError(e); }
const notFn = 5; try { notFn(); } catch (e) { logError(e); }`,

  'closures: methods and objects': `
const v = { x: 1, y: 2, len() { return Math.hypot(this.x, this.y); }, scale(k) { return { ...this, x: this.x * k, y: this.y * k }; } };
const w = v.scale(3); log(w.x, w.y, typeof w.len, w.len === v.len, Math.round(w.len() * 100));
log(JSON.stringify(v), Object.keys(w), Object.entries({ f() {} }).length);
const proto = { hello() { return 'hi ' + this.name; } };
const obj = Object.create(proto); obj.name = 'o'; log(obj.hello());
const withSuper = { __proto__: proto, hello() { return super.hello() + '!'; }, name: 's' }; log(withSuper.hello());
const getset = { _v: 1, get v() { return this._v; }, set v(x) { this._v = x * 2; } }; getset.v = 5; log(getset.v);
const named = function fact(n) { return n <= 1 ? 1 : n * fact(n - 1); }; log(named(5), named.name);
const holder = { list: [], add(x) { this.list.push(x); return this; } }; holder.add(1).add(2); log(holder.list);`,

  'closures: generators and async stay correct': `
const gen = function* (n) { for (let i = 0; i < n; i++) yield i; };
log([...gen(3)], typeof gen, Object.prototype.toString.call(gen(1)));
const af = async x => x * 2;
const results = [];
window.__done = (async () => { results.push(await af(4)); const arrow = async () => 'a'; results.push(await arrow()); log(results, typeof af); })();`,

  'closures: Function.prototype.toString and instanceof': `
const f = x => x;
function g() {}
log(f instanceof Function, g instanceof Function, Object.getPrototypeOf(f) === Function.prototype, typeof f.call, String(f).includes('=>'), String(g).startsWith('function g'));`,
};

// Self-hosted built-ins (37-prelude.js) against the natives they replace.
export const cases = {
  'builtins: identity, typeof, name and length': `
const a = [1, 2, 3];
log(a.map === Array.prototype.map, typeof a.map, a.map.name, a.map.length, a.indexOf.length, a.includes.length, a.slice.length, Array.isArray.name);
const m = a.map; log(m === [].map, String(a.forEach).includes('native code'), Object.getOwnPropertyNames(a.filter).sort());
const saved = Array.prototype.map; log(saved.call(a, x => x + 1), Reflect.apply(saved, a, [x => -x]));
log([].concat.name, Function.prototype.call.call(a.slice, a, 1));`,

  'builtins: map filter forEach basics': `
const a = [3, 1, 4, 1, 5, 9, 2, 6];
log(a.map((x, i, arr) => x * i + arr.length), a.filter(x => x & 1), a.map(String), a.filter(Boolean).length);
const seen = []; a.forEach(function (x, i) { seen.push(this.k + x + i); }, { k: 100 }); log(seen);
log(a.forEach(x => x), a.some(x => x > 8), a.every(x => x > 0), a.some(x => x > 9), a.every(x => x > 1));
log(a.find(x => x > 4), a.findIndex(x => x > 4), a.findLast(x => x < 3), a.findLastIndex(x => x < 3), a.find(x => x > 99), a.findIndex(x => x > 99));
log([].map(x => x), [].some(x => true), [].every(x => false), [].find(x => true), [].findLastIndex(x => true));`,

  'builtins: holes': `
const a = [1, , 3, , 5]; a[9] = 10;
const visits = []; a.forEach((x, i) => visits.push(i)); log(visits);
const m = a.map(x => x * 2); log(m.length, 1 in m, 2 in m, m, Object.keys(m));
log(a.filter(x => true), a.some(x => x === undefined), a.every(x => x !== undefined));
log(a.find(x => x === undefined), a.findIndex(x => x === undefined), a.findLast(x => x === undefined));
log(a.indexOf(undefined), a.includes(undefined), a.lastIndexOf(undefined), a.slice(0, 4), 1 in a.slice(0, 4), a.join('-'));
const b = []; b.length = 4; log(b.map(x => 1), b.join(), b.includes(undefined), b.indexOf(undefined));`,

  'builtins: callbacks that mutate the array': `
const a = [1, 2, 3, 4];
log(a.map((x, i, arr) => { if (i === 0) { arr.push(99); arr[2] = 30; } return x; }), a);
const b = [1, 2, 3, 4];
log(b.filter((x, i, arr) => { if (i === 1) arr.length = 2; return true; }), b);
const c = [1, 2, 3, 4], seen = [];
c.forEach((x, i, arr) => { seen.push(x); if (i === 0) arr.shift(); }); log(seen, c);
const d = [1, 2, 3]; log(d.some((x, i, arr) => { delete arr[1]; return x === 2; }), d, 1 in d);
const e = [5, 6, 7]; log(e.find((x, i, arr) => { arr[i + 1] = 0; return x === 0; }), e);
const f = [1, 2, 3]; log(f.findIndex((x, i, arr) => { arr.pop(); return x === 3; }), f);`,

  'builtins: callbacks that escape the array to the host': `
const a = [{ v: 1 }, { v: 2 }, { v: 3 }];
const out = a.map((o, i, arr) => { if (i === 0) JSON.stringify(arr); o.v *= 10; return o.v + arr.length; });
log(out, JSON.stringify(a), a[0].v);
const b = [1, 2, 3];
log(b.filter((x, i, arr) => { if (i === 1) { const h = new Map([[arr, 1]]); arr.push(h.get(arr) + 3); } return x > 1; }), b);
const c = [3, 2, 1]; log(c.map((x, i, arr) => { if (i === 0) arr.sort(); return arr[i] * x; }), c);`,

  'builtins: thisArg and callback kinds': `
'use strict';
const a = [1, 2];
log(a.map(function () { return this; }, 7), a.map(function () { return this; }), a.map(function () { return typeof this; }, 'str'));
log(a.map(Math.sqrt), a.map(Number.prototype.toFixed.call.bind(Number.prototype.toFixed)), ['1', '2', '3'].map(parseInt), ['a', 'b'].map(String.prototype.toUpperCase.call, String.prototype.toUpperCase));
const obj = { k: 3, f(x) { return x * this.k; } }; log(a.map(obj.f, obj), a.map(obj.f, { k: -1 }));
class Mul { constructor(k) { this.k = k; } apply(x) { return x * this.k; } }
const m = new Mul(5); log(a.map(m.apply, m), a.map(x => m.apply(x)));
const bound = function (x) { return x + this.d; }.bind({ d: 0.5 }); log(a.map(bound));`,

  'builtins: non-callable callbacks and bad receivers': `
const a = [1, 2];
for (const cb of [undefined, null, 1, 'f', {}, [], Symbol.iterator]) { try { a.map(cb); log('no error'); } catch (e) { logError(e); } }
try { a.forEach(); } catch (e) { logError(e); }
try { [].filter(5); } catch (e) { logError(e); }
try { Array.prototype.map.call(null, x => x); } catch (e) { logError(e); }
try { Array.prototype.forEach.call(undefined, x => x); } catch (e) { logError(e); }
log(Array.prototype.map.call('abc', c => c + c), Array.prototype.filter.call({ length: 3, 0: 'a', 2: 'c' }, x => true), Array.prototype.indexOf.call({ length: 2, 1: 7 }, 7));
log(Array.prototype.slice.call({ length: 2, 0: 1, 1: 2 }), Array.prototype.join.call({ length: 3, 0: 'x', 2: 'z' }, '+'), Array.prototype.includes.call('xyz', 'y'));
function args() { return Array.prototype.slice.call(arguments, 1); } log(args(1, 2, 3));`,

  'builtins: indexOf lastIndexOf includes': `
const a = [1, 2, NaN, -0, 0, '1', 2, undefined, null];
log(a.indexOf(2), a.lastIndexOf(2), a.indexOf(NaN), a.includes(NaN), a.indexOf(0), a.indexOf(-0), a.lastIndexOf(0), a.includes(-0), a.indexOf('1'), a.indexOf(null), a.includes(undefined));
log(a.indexOf(2, 2), a.indexOf(2, -3), a.indexOf(2, -100), a.indexOf(2, 100), a.indexOf(2, Infinity), a.indexOf(2, -Infinity), a.indexOf(1, NaN), a.indexOf(2, 1.9), a.indexOf(2, '3'));
log(a.lastIndexOf(2, 5), a.lastIndexOf(2, -4), a.lastIndexOf(2, -100), a.lastIndexOf(2, 100), a.lastIndexOf(1, -Infinity), a.lastIndexOf(2, undefined), a.lastIndexOf(2));
log(a.includes(2, 7), a.includes(2, -3), a.includes(NaN, 3), a.includes(1, Infinity), [].includes(undefined), [].indexOf(undefined), [, ,].includes(undefined), [, ,].indexOf(undefined));
const o = { v: 1 }, b = [o, { v: 1 }]; log(b.indexOf(o), b.includes({ v: 1 }), b.lastIndexOf(o));
const order = []; const from = { valueOf() { order.push('from'); return 1; } };
const c = [5, 6, 7]; log(c.indexOf(6, from), c.includes(7, from), c.lastIndexOf(5, from), order);
try { c.indexOf(1, Symbol()); } catch (e) { logError(e); } try { c.includes(1, 1n); } catch (e) { logError(e); }
log(c.indexOf(6, null), c.includes(6, true), c.indexOf(5, ''), c.lastIndexOf(7, '2'));`,

  'builtins: slice': `
const a = [0, 1, 2, 3, 4, 5];
log(a.slice(), a.slice(2), a.slice(-2), a.slice(1, -1), a.slice(4, 2), a.slice(-100, 100), a.slice(NaN), a.slice(2, NaN), a.slice(Infinity), a.slice(-Infinity, 2), a.slice(1.7, 3.2), a.slice('1', '3'), a.slice(undefined, 2), a.slice(null, 2));
const s = a.slice(); s.push(6); log(a.length, s.length, s === a);
const h = [1, , 3]; const hs = h.slice(); log(hs.length, 1 in hs, hs);
const order = [];
const b = [1, 2, 3, 4];
log(b.slice({ valueOf() { order.push('start'); b.length = 2; return 0; } }, { valueOf() { order.push('end'); return 4; } }), order, b);
const c = [1, 2, 3];
log(c.slice({ valueOf() { JSON.stringify(c); c.push(4); return 1; } }), c);
try { a.slice(Symbol()); } catch (e) { logError(e); }`,

  'builtins: join': `
log([1, 2, 3].join(), [1, 2, 3].join(''), [1, 2, 3].join(' - '), ['a', null, undefined, 'b'].join('|'), [true, false, 1.5, -0, NaN, 1e21].join(), [].join(), [, ,].join());
log([1, 2].join(undefined), [1, 2].join(null), [1, 2].join(0), [1, 2].join({ toString() { return '*'; } }), [[1, 2], [3]].join(';'), [{}, { toString() { return 'T'; } }].join());
const cyc = [1, 2]; cyc.push(cyc); log(cyc.join('/'), String(cyc));
try { [1].join(Symbol()); } catch (e) { logError(e); } try { [Symbol()].join(); } catch (e) { logError(e); }
log(['x', 1, 2n].join(), [new Date(0).getTime(), 'z'].join(':'));
const key = (x, y, z) => [x, y, z].join(','); log(key(1, -2, 3.5), key('a', undefined, null));`,

  'builtins: Array.isArray': `
log(Array.isArray([]), Array.isArray([1, 2]), Array.isArray({}), Array.isArray(null), Array.isArray(undefined), Array.isArray('a'), Array.isArray(x => x), Array.isArray(new Uint8Array(2)), Array.isArray({ length: 0 }));
const a = [1]; JSON.stringify(a); log(Array.isArray(a), Array.isArray(new Array(3)), Array.isArray(Array.from('ab')), Array.isArray(new Proxy([], {})), Array.isArray(Object.create(Array.prototype)));
class Sub extends Array {} log(Array.isArray(new Sub()), Array.isArray(Array.prototype));`,

  'builtins: prototype and species changes': `
const a = [1, 2, 3];
Array.prototype.extra = function () { return this.length; };
log(a.extra(), a.map(x => x).extra());
delete Array.prototype.extra;
Object.defineProperty(Array.prototype, '1', { get() { return 'proto'; }, configurable: true });
const h = [0, , 2];
log(h.map(x => x), h.indexOf('proto'), h.includes('proto'), h.slice(), h.join(), h.filter(x => true), h.find(x => x === 'proto'));
delete Array.prototype[1];
class MyArray extends Array {}
const m = MyArray.from([1, 2, 3]);
log(m.map(x => x * 2) instanceof MyArray, m.filter(x => x > 1) instanceof MyArray, m.slice(1) instanceof MyArray, m.map(x => x).constructor.name);
const c = [1, 2, 3]; c.constructor = MyArray; log(c.map(x => x) instanceof MyArray, c.slice(0, 1) instanceof MyArray, c.filter(x => x) instanceof MyArray);
const d = [1, 2]; d.constructor = { [Symbol.species]: function (n) { return { length: 0, n }; } }; log(JSON.stringify(d.map(x => x)), JSON.stringify(d.slice(1)));`,

  'builtins: species of the Array constructor': `
const saved = Object.getOwnPropertyDescriptor(Array, Symbol.species);
class Tracked extends Array {}
Object.defineProperty(Array, Symbol.species, { get() { return Tracked; }, configurable: true });
const a = [1, 2, 3];
log(a.map(x => x) instanceof Tracked, a.filter(x => x) instanceof Tracked, a.slice() instanceof Tracked);
Object.defineProperty(Array, Symbol.species, saved);
log(a.map(x => x) instanceof Tracked, a.slice() instanceof Array);`,

  'builtins: large arrays and loops': `
const big = Array.from({ length: 5000 }, (_, i) => i);
let s = 0; big.forEach(x => { s += x; });
log(s, big.map(x => x * 2).filter(x => x % 3 === 0).length, big.indexOf(4999), big.includes(5000), big.slice(4990).join(), big.some(x => x === 2500), big.every(x => x < 5000), big.findLast(x => x % 1000 === 0));
const words = 'the quick brown fox jumps over the lazy dog'.split(' ');
log(words.map(w => w.length).join('+'), words.filter(w => w.length > 3).join(' '), words.indexOf('the', 1), words.lastIndexOf('the'), words.findIndex(w => w.startsWith('j')));
const objs = []; for (let i = 0; i < 100; i++) objs.push({ id: i, tag: i % 7 === 0 ? 'seven' : 'other' });
const sevens = objs.filter(o => o.tag === 'seven'); sevens[0].id = -1;
log(sevens.length, objs[0].id, objs.find(o => o.id === 49).tag, objs.map(o => o.id).slice(0, 5));`,
};

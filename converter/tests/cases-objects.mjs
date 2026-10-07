// Differential cases for plain objects and arrays as compiled code uses them:
// the WASM object heap (property lists + dense array part), materialization
// when objects escape to host code, and the paths that fall back to the host.
export const cases = {
  // ---------- keys and insertion order ----------
  'objects: integer-like keys come first': `
    const o = {};
    o.b = 1; o[2] = 'two'; o.a = 2; o['1'] = 'one'; o[0] = 'zero';
    o['01'] = 'lead'; o['-1'] = 'neg'; o[1.5] = 'frac'; o['4294967294'] = 'max'; o['4294967295'] = 'big';
    log(o[1], o['2'], o[0], o['01'], o[-1], o['1.5'], o[4294967294], o[4294967295]);
    log(Object.keys(o).join('|'));
    const keys = []; for (const k in o) keys.push(k); log(keys.join('|'));
    log(JSON.stringify(o));
    const lit = {z: 1, 10: 'a', y: 2, 9: 'b', [3 + 4]: 'c', '8': 'd'};
    log(lit[7], lit[8], lit['9'], lit[10]);
    log(Object.keys(lit).join('|'), Object.values(lit).join('|'));`,
  'objects: delete then re-add': `
    function run(){
      const o = {a: 1, b: 2, c: 3};
      log(delete o.b, 'b' in o, o.b, o.hasOwnProperty('b'));
      o.b = 20;
      log(o.b, 'b' in o);
      log(delete o.a, delete o.a, delete o.nothing, o.a);
      o.a = undefined;
      log('a' in o, o.a);
      for (let i = 0; i < 5; i++) { delete o.c; o.c = i; }
      log(o.c);
      return o;
    }
    const o = run();
    log(Object.keys(o).join(','), JSON.stringify(o));
    const n = {1: 'a', 2: 'b', x: 1}; delete n[1]; n.y = 2; n[1] = 'A'; n[0] = 'Z';
    log(Object.keys(n).join(','));`,
  'objects: computed and coerced keys': `
    const o = {};
    o[1] = 'num'; log(o['1'], o[1.0], o[1]);
    o[-0] = 'negzero'; log(o[0], o['0'], o['-0']);
    o[1e21] = 'exp'; log(o['1e+21']);
    o[NaN] = 'nan'; log(o.NaN, o['NaN']);
    o[Infinity] = 'inf'; o[-Infinity] = 'ninf'; log(o.Infinity, o['-Infinity']);
    o[true] = 't'; o[null] = 'n'; o[undefined] = 'u'; log(o.true, o['null'], o.undefined);
    o[{}] = 'obj'; o[[1, 2]] = 'arr'; o[[]] = 'empty'; log(o['[object Object]'], o['1,2'], o['']);
    o[0.1 + 0.2] = 'float'; log(o['0.30000000000000004']);
    const key = {toString(){ log('toString called'); return 'custom'; }};
    o[key] = 'k'; log(o.custom);
    log(Object.keys(o).join('|'));`,
  'objects: typed number keys in functions': `
    function fill(){
      const o = {}, a = [];
      for (let i = 0; i < 3; i += 0.5) { o[i] = i * 2; a[i] = i; }
      for (let i = -2; i < 0; i++) { o[i] = 'n' + i; a[i] = 'n' + i; }
      const z = 0, nz = -z; o[nz] = 'zero'; a[nz] = 'zero';
      let s = 0; for (let i = 0; i < 3; i += 0.5) s += o[i];
      log(s, o[0], o[0.5], o['2.5'], o[-1], a[0], a[1], a[0.5], a[-2], a.length);
      return [o, a];
    }
    const [o, a] = fill();
    log(Object.keys(o).join('|'));
    log(Object.keys(a).join('|'), a.length);`,
  // ---------- spread, rest, Object.* ----------
  'objects: object spread order and overrides': `
    const a = {x: 1, y: 2, 1: 'one'};
    const b = {y: 3, z: 4, 0: 'zero'};
    const c = {...a, ...b, x: 9};
    log(c.x, c.y, c.z, c[0], c[1]);
    log(Object.keys(c).join(','));
    const d = {w: 0, ...a, x: 'late'};
    log(Object.keys(d).join(','), d.x);
    c.y = 100; log(a.y, b.y, c.y);
    log(JSON.stringify({...[7, 8]}), JSON.stringify({...'hi'}), JSON.stringify({...null, ...undefined, ...5, ...true}));
    let calls = 0;
    const g = {get v(){ calls++; return calls * 10; }};
    const e = {...g}; log(calls, e.v, e.v, calls, Object.getOwnPropertyDescriptor(e, 'v').writable);
    const {x, ...rest} = c; log(x, Object.keys(rest).join(','));
    rest.extra = 1; log(c.extra, Object.keys(rest).length);`,
  'objects: array spread sources': `
    function build(){
      const a = [1, 2];
      const holes = [1, , 3];
      const b = [...a, ...'ab', ...new Set([5, 5, 6]), ...holes, ...a];
      log(b.length, b.join('|'), 1 in [...holes]);
      a.push(3);
      log(b.length, a.length);
      const self = [1, 2]; const twice = [...self, ...self]; self.push(9);
      log(twice.join(), self.join());
      return b;
    }
    const b = build();
    log(b);
    try { [...{}]; } catch (e) { logError(e); }
    try { [...null]; } catch (e) { logError(e); }`,
  'objects: keys values entries assign then keep mutating': `
    function run(){
      const o = {b: 1, a: 2};
      o.c = 3;
      log(Object.keys(o).join(','), Object.values(o).join(','), JSON.stringify(Object.entries(o)));
      o.d = 4; delete o.a; o.b += 10;
      log(Object.keys(o).join(','), o.b, o.d);
      const t = {x: 0};
      const r = Object.assign(t, {y: 1}, null, {x: 2, z: 3}, 'ab');
      log(r === t, Object.keys(t).join(','), t[0], t[1], t.x);
      t.x++; t.w = 'w';
      log(t.x, JSON.stringify(t));
      const f = Object.fromEntries(Object.entries(o).map(([k, v]) => [k + k, v * 2]));
      f.extra = true;
      log(JSON.stringify(f), Object.keys(f).length);
    }
    run();`,
  'objects: freeze seal preventExtensions': `
    function sloppy(){
      const f = Object.freeze({a: 1, nested: {b: 2}});
      f.a = 2; f.c = 3; delete f.a; f.nested.b = 3;
      log(f.a, f.c, f.nested.b, Object.isFrozen(f), Object.isFrozen(f.nested));
      const s = Object.seal({a: 1});
      s.a = 5; s.b = 6; log(delete s.a, s.a, s.b, Object.isSealed(s), Object.isFrozen(s));
      const p = Object.preventExtensions({a: 1});
      p.b = 2; p.a = 9; log(p.a, p.b, delete p.a, 'a' in p, Object.isExtensible(p));
      const arr = Object.freeze([1, 2]);
      arr[0] = 9; arr[5] = 1; log(arr[0], arr.length);
      try { arr.push(3); } catch (e) { logError(e); }
      try { arr.pop(); } catch (e) { logError(e); }
      log(arr.join(), Object.isFrozen({}), Object.isFrozen(Object.preventExtensions({})));
    }
    function strict(){
      'use strict';
      const f = Object.freeze({a: 1});
      try { f.a = 2; } catch (e) { logError(e); }
      try { f.b = 2; } catch (e) { logError(e); }
      try { delete f.a; } catch (e) { logError(e); }
      const arr = Object.freeze([1]);
      try { arr[0] = 2; } catch (e) { logError(e); }
      try { arr.length = 0; } catch (e) { logError(e); }
      log(f.a, arr.length);
    }
    sloppy(); strict();`,
  'objects: defineProperty on compiled objects': `
    function run(){
      const o = {a: 1};
      Object.defineProperty(o, 'hidden', {value: 'h', enumerable: false});
      Object.defineProperty(o, 'ro', {value: 1, enumerable: true, writable: false});
      let backing = 5;
      Object.defineProperty(o, 'acc', {get(){ return backing * 2; }, set(v){ backing = v; }, enumerable: true, configurable: true});
      o.ro = 2; o.acc = 7; o.a++;
      log(o.hidden, o.ro, o.acc, backing, o.a);
      log(Object.keys(o).join(','), JSON.stringify(o));
      const d = Object.getOwnPropertyDescriptor(o, 'ro');
      log(d.value, d.writable, d.enumerable, d.configurable);
      log(JSON.stringify(Object.getOwnPropertyNames(o)));
      try { Object.defineProperty(o, 'ro', {value: 3}); } catch (e) { logError(e); }
      delete o.acc; log(o.acc, 'acc' in o);
      const arr = [1, 2, 3];
      Object.defineProperty(arr, 1, {value: 'x', writable: false});
      arr[1] = 'y'; arr.push(4);
      log(arr.join(), arr.length);
      Object.defineProperty(arr, 'length', {writable: false});
      try { 'use strict'; arr.push(5); } catch (e) { logError(e); }
      log(arr.length);
    }
    run();`,
  'objects: getters and setters on literals': `
    function run(){
      let log2 = [];
      const k = 'dyn';
      const o = {
        _v: 1,
        get v(){ log2.push('get'); return this._v; },
        set v(x){ log2.push('set ' + x); this._v = x * 2; },
        get [k + 'Name'](){ return 'computed'; },
        set only(x){ this._only = x; },
        get ro(){ return 'ro'; },
      };
      o.v = 3; log(o.v, o._v);
      o.v += 1; log(o._v);
      o.v++; log(o._v);
      log(o.dynName, o.only, o.ro);
      o.only = 'set'; o.ro = 'changed';
      log(o._only, o.ro, log2.join(','));
      log(Object.keys(o).join(','), JSON.stringify(o));
      let total = 0; for (let i = 0; i < 4; i++) { o.v = i; total += o.v; } log(total);
      const d = Object.getOwnPropertyDescriptor(o, 'v');
      log(typeof d.get, typeof d.set, d.get.name, d.set.name, Object.getOwnPropertyDescriptor(o, 'dynName').get.name);
    }
    run();
    (function(){ 'use strict'; const o = {get ro(){ return 1; }}; try { o.ro = 2; } catch (e) { logError(e); } log(o.ro); })();`,
  'objects: __proto__ in literals': `
    const base = {greet(){ return 'hi ' + this.name; }, shared: 'base'};
    const a = {__proto__: base, name: 'a'};
    const b = {'__proto__': base, name: 'b'};
    const c = {['__proto__']: base, name: 'c'};
    const __proto__ = 'short'; const d = {__proto__};
    const e = {__proto__: 5, name: 'e'};
    const n = {__proto__: null, x: 1};
    log(a.greet(), b.greet(), a.shared, Object.getPrototypeOf(a) === base, Object.getPrototypeOf(b) === base);
    log(typeof c.greet, c.__proto__ === base, Object.getPrototypeOf(c) === Object.prototype, Object.keys(c).join(','), c.hasOwnProperty('__proto__'));
    log(d.__proto__, Object.keys(d).join(','), Object.getPrototypeOf(d) === Object.prototype);
    log(Object.getPrototypeOf(e) === Object.prototype, Object.keys(e).join(','));
    log(n.x, n.toString, 'toString' in n, Object.getPrototypeOf(n), Object.keys(n).join(','));
    n.__proto__ = 'own'; log(n.__proto__, Object.keys(n).join(','), Object.getPrototypeOf(n));
    try { String(n); } catch (err) { logError(err); }
    log(JSON.stringify(n), JSON.stringify(c.__proto__ === base));
    base.shared = 'changed'; log(a.shared, b.shared);
    a.shared = 'own'; log(a.shared, base.shared, Object.keys(a).join(','));
    delete a.shared; log(a.shared);`,
  'objects: missing properties and the prototype chain': `
    function run(){
      const o = {x: 1};
      const a = [1, 2];
      log(o.missing, o.missing === undefined, typeof o.missing, o.missing?.deep);
      log(o.toString === Object.prototype.toString, o.hasOwnProperty('x'), o.hasOwnProperty('toString'));
      log(o.valueOf() === o, o.constructor === Object, o.constructor.name, a.constructor === Array, a.constructor.name);
      log(String(o), o + '', '' + a, a.toString(), String([]), String([null, undefined, 1]));
      log(Object.prototype.hasOwnProperty.call(o, 'x'), Object.prototype.isPrototypeOf(o), o.propertyIsEnumerable('x'));
      log('toString' in o, 'valueOf' in a, 'push' in a, 'missing' in o, 'constructor' in o);
      log(o.__proto__ === Object.prototype, a.__proto__ === Array.prototype, o.isPrototypeOf, typeof o.__lookupGetter__);
      log(typeof a.map, typeof a.missing, a.length, o.length);
    }
    run();`,
  'objects: own properties shadowing builtin names': `
    function run(){
      const o = {toString(){ return 'T'; }, valueOf(){ return 42; }, constructor: 'ctor', hasOwnProperty: 1, length: 3};
      log(String(o), o + 1, o * 2, '' + o, o.constructor, o.hasOwnProperty, o.length);
      log(Object.prototype.hasOwnProperty.call(o, 'toString'), Object.keys(o).join(','));
      try { o.hasOwnProperty('x'); } catch (e) { logError(e); }
      const p = {};
      p.toString = () => 'P'; p.valueOf = () => 7;
      log(p + 1, '' + p, String(p), p > 6, [p, p].join('-'));
      delete p.valueOf; log(p + 1);
      delete p.toString; log(p + '');
      const q = {}; q.constructor = 5; q.isPrototypeOf = 'x'; log(q.constructor, q.isPrototypeOf, Object.keys(q).join(','));
    }
    run();`,
  // ---------- escaping to host ----------
  'objects: JSON.stringify escape then mutate': `
    function run(){
      const o = {n: 1, list: [1, 2], inner: {v: 'a'}};
      const alias = o, list = o.list, inner = o.inner;
      log(JSON.stringify(o));
      o.n = 2; alias.m = 'new'; list.push(3); inner.v = 'b'; delete o.inner.missing;
      const later = o;
      later.list[0] = 'first'; delete alias.n;
      log(JSON.stringify(o), o === alias, later.list === list, list.length, inner === o.inner);
      list.length = 1; inner.w = [inner.v];
      log(JSON.stringify(alias), o.list.length, o.inner.w[0]);
      const parsed = JSON.parse(JSON.stringify(o)); parsed.list.push('p'); parsed.k = 1;
      log(o.list.length, parsed.list.length, Object.keys(parsed).join(','));
    }
    run();`,
  'objects: Map Set and WeakMap keys': `
    function run(){
      const m = new Map(), s = new Set(), w = new WeakMap();
      const keys = [];
      for (let i = 0; i < 4; i++) { const k = {id: i}; keys.push(k); m.set(k, i * 10); s.add(k); w.set(k, 'w' + i); }
      keys[1].id = 'changed'; keys.push({id: 0});
      log(m.size, s.size, m.get(keys[1]), m.get(keys[4]), s.has(keys[2]), s.has({id: 2}), w.get(keys[3]), w.has(keys[4]));
      const arr = [1, 2]; m.set(arr, 'arr'); arr.push(3);
      log(m.get(arr), [...m.keys()].indexOf(arr), [...m.keys()][4] === arr);
      let total = 0; for (const [k, v] of m) if (typeof v === 'number') total += v + (typeof k.id === 'number' ? k.id : 100);
      log(total);
      const first = [...s][0]; first.extra = 'x'; log(keys[0].extra, first === keys[0]);
      m.delete(keys[0]); log(m.size, m.has(keys[0]), JSON.stringify([...m.values()]));
    }
    run();`,
  'objects: passed to host callbacks and mutated': `
    function run(){
      const items = [{n: 3}, {n: 1}, {n: 2}];
      const first = items[0];
      items.forEach((it, i) => { it.n *= 10; it.i = i; });
      log(first.n, first.i, items[1].n);
      const doubled = items.map(it => ({n: it.n * 2, src: it}));
      doubled[0].src.n = -1; log(first.n, doubled[0].n);
      items.sort((a, b) => a.n - b.n);
      log(items.map(it => it.n).join(), items[0] === first, items.indexOf(first), items.includes(first));
      const sum = items.reduce((acc, it) => { acc.total += it.n; acc.count++; return acc; }, {total: 0, count: 0});
      log(sum.total, sum.count, JSON.stringify(sum));
      const found = items.find(it => it.n === 20); found.mark = true;
      log(items.filter(it => it.mark).length, items.findIndex(it => it === found));
      const outer = {hits: 0};
      [1, 2, 3].forEach(function(x){ outer.hits += x; outer['k' + x] = x; });
      log(outer.hits, Object.keys(outer).join(','));
    }
    run();`,
  'objects: mutated through host and compiled sides': `
    function run(){
      const o = {count: 0, list: []};
      const holder = [o];
      window.sharedObj = o;
      Object.assign(window.sharedObj, {count: 5, added: 'host'});
      log(o.count, o.added, holder[0].count);
      o.count++; o.list.push('c');
      log(window.sharedObj.count, window.sharedObj.list.length);
      Reflect.set(window.sharedObj, 'r', 1); Reflect.deleteProperty(o, 'added');
      log(o.r, 'added' in o, holder[0].added);
      Object.defineProperty(window.sharedObj, 'g', {get(){ return this.count * 2; }, enumerable: true});
      log(o.g, holder[0].g);
      o.count = 100; log(o.g, JSON.stringify(window.sharedObj));
      window.sharedArr = o.list; window.sharedArr.unshift('h'); o.list.push('c2');
      log(o.list.join(), window.sharedArr === o.list, holder[0].list.length);
    }
    run();`,
  'objects: same object through many references': `
    function run(){
      const a = {v: 0};
      const arr = [a, a, {v: 100}];
      const obj = {p: a, q: a, r: arr};
      arr[0].v++; obj.q.v += 10; obj.r[1].v *= 2;
      log(a.v, arr[1] === obj.p, obj.r[2].v);
      const copy = arr.slice(); copy[0].v = 'via copy';
      log(a.v, copy === arr, copy[0] === a);
      log(JSON.stringify(obj));
      obj.p.v = 'after'; arr.push(a);
      log(obj.q.v, arr[3].v, arr.length, obj.r.length);
      a.self = a;
      try { JSON.stringify(a); } catch (e) { logError(e); }
      log(a.self.self.self === a, arr.filter(x => x === a).length);
    }
    run();`,
  // ---------- shapes ----------
  'objects: many properties': `
    function run(){
      const o = {};
      for (let i = 0; i < 300; i++) o['p' + i] = i;
      let s = 0; for (let i = 0; i < 300; i++) s += o['p' + i];
      log(s, o.p0, o.p299, o.p300);
      for (let i = 0; i < 300; i += 3) delete o['p' + i];
      let t = 0; for (let i = 0; i < 300; i++) t += o['p' + i] || 0;
      log(t, 'p3' in o, 'p4' in o);
      for (let i = 0; i < 300; i += 6) o['p' + i] = -i;
      const keys = Object.keys(o);
      log(keys.length, keys.slice(0, 5).join(), keys.slice(-5).join());
      const nums = {};
      for (let i = 99; i >= 0; i--) nums[i * 3] = i;
      const nk = Object.keys(nums); log(nk.length, nk.slice(0, 4).join(), nk.slice(-2).join());
    }
    run();`,
  'objects: loop-built same and different shapes': `
    function make(i){
      if (i % 3 === 0) return {x: i, y: i * 2};
      if (i % 3 === 1) return {y: i * 2, x: i};
      const o = {x: i}; if (i % 2) o.y = i * 2; o['z' + i] = true; return o;
    }
    function sum(list){ let s = 0; for (let i = 0; i < list.length; i++) s += list[i].x + list[i].y; return s; }
    const list = [];
    for (let i = 0; i < 12; i++) list.push(make(i));
    log(sum(list), list.map(o => Object.keys(o).join('')).join(' '));
    for (const o of list) if (o.y === undefined) o.y = 0;
    log(sum(list));
    list[5].x = 'str'; log(sum(list.slice(0, 6)));`,
  'objects: optional properties and defaults': `
    function run(){
      const values = [0, '', null, undefined, false, NaN, 'v', 3];
      for (const v of values) {
        const o = {x: v};
        log(o.x || 'or', o.x ?? 'nn', 'x' in o, o.x === undefined, o.hasOwnProperty('x'), o.y ?? 'missing', o.y || 0);
      }
      const o = {a: 0, b: null, c: 'c'};
      o.a ||= 5; o.b ??= []; o.c &&= 'cc'; o.d ??= 'd'; o.e ||= 0; o.f &&= 'never';
      o.b.push(1);
      log(JSON.stringify(o), 'f' in o);
      const counts = {};
      for (const w of 'a b a c b a'.split(' ')) counts[w] = (counts[w] || 0) + 1;
      log(JSON.stringify(counts));
      const opts = {}; const width = opts.width ?? 100, height = opts.height || 50;
      log(width + height, opts.depth?.value, opts.fn?.(), opts.list?.[0]);
    }
    run();`,
  // ---------- arrays ----------
  'objects: array holes': `
    function run(){
      const a = [1, , 3, , ];
      log(a.length, 1 in a, 2 in a, a[1], a.indexOf(undefined), a.includes(undefined));
      const b = []; b[5] = 'x'; b[2] = 'y';
      log(b.length, Object.keys(b).join(), 0 in b, b.findIndex(v => v === undefined));
      let visits = 0; b.forEach(() => visits++); log(visits);
      log(JSON.stringify(b.map(v => v + '!')), b.join('-'), JSON.stringify(b));
      const c = [1, 2, 3]; delete c[1];
      log(c.length, 1 in c, c[1], JSON.stringify(c));
      const d = new Array(3); log(d.length, 0 in d); d.push('p'); log(d.length, d[3]);
      log([...a].length, 1 in [...a]);
      for (const v of [, 'x']) log('of', v);
      const e = [1]; e[3] = 4; e.length = 5; log(e, e.length);
      return [a, b, c];
    }
    log(run());`,
  'objects: array length writes': `
    function run(){
      const a = [1, 2, 3, 4, 5];
      a.length = 3; log(a, a[3], a[4]);
      a.length = 6; log(a.length, 3 in a, a[5]);
      a[3] = 'back'; log(a);
      a.length = 0; log(a.length, a[0]);
      a.push('new'); log(a, a.length);
      for (const bad of [-1, 3.5, NaN, 2 ** 32]) {
        try { a.length = bad; log('ok', bad); } catch (e) { logError(e); }
      }
      log(a.length);
      a.length = '2'; log(a.length, a);
      a.length = -0; log(a.length);
      const b = []; b[b.length] = 'i0'; b[b.length] = 'i1'; b.length += 2; log(b, b.length);
      b.length--; b.length--; log(b);
    }
    run();`,
  'objects: array length near 2^32': `
    function run(){
      const a = [1, 2];
      a.length = 2 ** 31; log(a.length, a[0], a[2 ** 31 - 1]);
      a.length = 4294967295; log(a.length, a[1]);
      try { a.push('x'); } catch (e) { logError(e); }
      log(a.length);
      const b = [7];
      b.length = 3e9; log(b.length, b[0]);
      try { log(b.pop(), b.length); } catch (e) { logError(e); }
      b.length = 1; log(b, b.length);
    }
    run();`,
  'objects: push pop shift unshift splice interleaved': `
    function run(){
      const a = [];
      log(a.push(1), a.push(2, 3), a.push(), a.length);
      log(a.pop(), a.length, a.pop(), a.pop(), a.pop(), a.length);
      a.push('a', 'b', 'c');
      log(a.shift(), a.length, a.join());
      log(a.push('d'), a.unshift('z', 'y'), a.join());
      log(a.pop(), a.splice(1, 2).join(), a.join(), a.length);
      log(a.splice(1, 0, 'i1', 'i2'), a.join());
      log(a.splice(-1), a.join(), a.splice(10, 1).length);
      const b = [1, 2, 3];
      const alias = b;
      b.shift(); b.push(4); alias.pop(); b.unshift(0);
      log(b.join(), alias === b, alias.length);
      const c = [5]; for (let i = 0; i < 20; i++) { c.push(i); if (i % 3 === 0) c.shift(); if (i % 5 === 0) c.pop(); }
      log(c.join(), c.length);
    }
    run();`,
  'objects: sort and reverse': `
    function run(){
      const nums = [10, 9, 1, 100, 25, -3];
      log(nums.slice().sort().join(), nums.slice().sort((a, b) => a - b).join(), nums.slice().sort((a, b) => b - a).join());
      const r = nums.sort(); log(r === nums, nums.join());
      const mixed = [3, undefined, 1, , 2, undefined];
      mixed.sort(); log(mixed.length, JSON.stringify(mixed), 5 in mixed, 3 in mixed);
      const h = [1, , 3]; h.reverse(); log(1 in h, JSON.stringify(h), h[0]);
      const fl = [0.5, -0, 0, 2, 1.5, NaN, -1]; fl.sort((a, b) => a - b); log(fl);
      const objs = []; for (let i = 0; i < 10; i++) objs.push({k: i % 3, id: i});
      objs.sort((a, b) => a.k - b.k); log(objs.map(o => o.id).join());
      objs.reverse(); log(objs.map(o => o.k + ':' + o.id).join());
      const strs = ['b', 'a', 'B', 'c', 'A']; strs.sort(); log(strs.join());
      log([3, 2, 1].reverse().join(), [].reverse().length);
    }
    run();`,
  'objects: negative and fractional indices': `
    function run(){
      const a = [10, 20, 30];
      a[-1] = 'neg'; a[1.5] = 'frac'; a['2'] = 'two'; a['02'] = 'zero-two'; a['1e1'] = 'exp';
      log(a.length, a[-1], a['-1'], a[1.5], a['1.5'], a[2], a['02'], a[10], a.at(-1));
      log(Object.keys(a).join('|'));
      log(a.indexOf('two'), a.join(), JSON.stringify(a));
      let i = -1; log(a[i], a[i + 1], a[i * -0], a[0.5 + 0.5]);
      const idx = [0, -0, 0.0, 1 / 3]; for (const k of idx) log(k, a[k]);
      delete a[-1]; log(Object.keys(a).length, a[-1]);
      const b = []; b[-0] = 'z'; b[1.0] = 'one'; log(b.length, b);
    }
    run();`,
  'objects: string keys on arrays': `
    function run(){
      const a = [1, 2, 3];
      a.name = 'arr'; a['extra'] = {deep: true};
      log(a.length, a.name, a.extra.deep, Object.keys(a).join(), JSON.stringify(a));
      const keys = []; for (const k in a) keys.push(k); log(keys.join());
      log([...a].length, a.hasOwnProperty('name'), 'name' in a, 'push' in a, typeof a['push'], a['length']);
      a.push(4); log(a.length, a.name, a.slice(1).name, Object.keys(a).length);
      const b = [0]; b.length2 = 5; b['length'] = 3; log(b.length, b.length2, b);
      const c = []; c['0'] = 'zero'; c['1'] = 'one'; c['x'] = 'ex'; log(c.length, c.join('+'));
    }
    run();`,
  'objects: array-likes': `
    function run(){
      const al = {length: 3, 0: 'a', 1: 'b', 2: 'c'};
      log(Array.from(al).join(), Array.prototype.join.call(al, '-'), Array.prototype.map.call(al, x => x.toUpperCase()).join());
      log(Array.prototype.push.call(al, 'd'), al.length, al[3]);
      log(Array.prototype.pop.call(al), al.length, 3 in al);
      const sparse = {length: '3', 1: 'only'};
      log(JSON.stringify(Array.from(sparse)), Array.prototype.indexOf.call(sparse, 'only'));
      const str = 'héllo'; log(Array.from(str).length, Array.prototype.slice.call(str, 1, 3).join());
      function args(){ return Array.prototype.slice.call(arguments, 1); }
      log(args(1, 2, 3).join());
      const o = {length: 0, push: Array.prototype.push, pop: Array.prototype.pop};
      o.push('x'); o.push('y', 'z'); log(o.length, o[1], o.pop(), o.length, Object.keys(o).join());
    }
    run();`,
  'objects: nested arrays of objects': `
    function run(){
      const grid = [];
      for (let y = 0; y < 4; y++) { const row = []; for (let x = 0; x < 4; x++) row.push({x, y, alive: (x * 3 + y) % 4 === 0}); grid.push(row); }
      function neighbors(gx, gy){ let n = 0; for (let dy = -1; dy <= 1; dy++) for (let dx = -1; dx <= 1; dx++) { if (!dx && !dy) continue; const row = grid[gy + dy]; const c = row && row[gx + dx]; if (c && c.alive) n++; } return n; }
      const counts = grid.map(row => row.map(c => neighbors(c.x, c.y)));
      log(counts.map(r => r.join('')).join('/'));
      for (const row of grid) for (const c of row) c.alive = !c.alive;
      grid[1][2].tag = 'tagged'; grid[1].push({x: 4, y: 1, alive: true});
      log(grid.flat().filter(c => c.alive).length, grid[1].length, grid[1][2].tag);
      const byY = {}; for (const c of grid.flat()) (byY[c.y] ??= []).push(c.x);
      log(JSON.stringify(byY));
      log(JSON.stringify(grid[0].slice(0, 2)));
    }
    run();`,
  // ---------- numbers inside properties ----------
  'objects: particles with int and float values': `
    function step(ps, dt){ for (let i = 0; i < ps.length; i++) { const p = ps[i]; p.x += p.vx * dt; p.y += p.vy; p.vy -= 0.5; if (p.y < 0) { p.y = -p.y; p.vy = -p.vy * 0.5; } p.age++; } }
    const ps = [];
    for (let i = 0; i < 4; i++) ps.push({x: i, y: 10, vx: i % 2 ? 1 : 0.25, vy: 0, age: 0});
    for (let f = 0; f < 8; f++) { step(ps, f % 2 ? 1 : 0.5); log(ps.map(p => p.x + ',' + p.y + ',' + p.vy).join(' ')); }
    log(ps.map(p => p.age).join(), JSON.stringify(ps[0]));
    const q = {x: 0, v: -1};
    q.x *= q.v; log(q.x, Object.is(q.x, -0), 1 / q.x);
    q.x = 0 / 0; q.x += 1; log(q.x, q.x === q.x);
    q.x = 1e308; q.x *= 10; log(q.x); q.x = -q.x; log(q.x);
    q.x = 2; q.x /= 4; q.x *= 4; log(q.x, Number.isInteger(q.x));`,
  'objects: -0 NaN and infinities in properties': `
    function run(){
      const o = {z: -0, n: NaN, i: -Infinity};
      log(o.z, Object.is(o.z, -0), o.z === 0, 1 / o.z, o.n, o.n === o.n, Object.is(o.n, NaN), o.i);
      const a = [-0, NaN, 0];
      log(a, a.indexOf(0), a.indexOf(-0), a.includes(-0), a.indexOf(NaN), a.includes(NaN), a.lastIndexOf(0));
      log(JSON.stringify(o), JSON.stringify(a));
      const m = new Map([[-0, 'zero']]); log(m.get(0), [...m.keys()][0], Object.is([...m.keys()][0], -0));
      o.z = 0 * -5; a[2] = -a[2]; log(Object.is(o.z, -0), Object.is(a[2], -0), a);
      o.m = Math.min(0, -0); o.r = Math.round(-0.4); o.mod = -4 % 2; log(Object.is(o.m, -0), Object.is(o.r, -0), Object.is(o.mod, -0));
      const counts = {}; counts[-0] = 1; counts[0]++; log(JSON.stringify(counts));
      const s = new Set([NaN, NaN, -0, 0]); log(s.size);
    }
    run();`,
  'objects: i31 boundary values in properties': `
    function run(){
      const o = {a: 1073741822, b: -1073741823, c: 2 ** 31 - 1};
      for (let i = 0; i < 3; i++) { o.a++; o.b--; log(o.a, o.b); }
      o.a -= 2; o.b += 2; log(o.a, o.b, o.a + o.b);
      o.c++; log(o.c, o.c | 0, o.c >>> 0);
      const arr = [1073741823, 1073741824, -1073741824, -1073741825];
      for (let i = 0; i < arr.length; i++) arr[i] += 1;
      log(arr);
      const m = new Map(); m.set(2 ** 30, 'x'); log(m.get(1073741824), m.get(1073741823 + 1), m.has(2 ** 30 - 1));
      const k = {}; k[2 ** 30] = 'k'; k[-(2 ** 30)] = 'neg'; log(k[1073741824], k['1073741824'], k[-1073741824], Object.keys(k).join());
      o.f = 0.5; o.f += 0.5; log(o.f, Number.isInteger(o.f), o.f === 1);
      o.f = 1; o.f += 0.25; o.f -= 0.25; log(o.f, o.f === 1, [o.f].indexOf(1), new Set([1, o.f]).size);
    }
    run();`,
  'objects: equality and identity after escape': `
    function run(){
      const a = {id: 1};
      const list = [a, {id: 2}];
      const before = list[0];
      log(list[0] === a, before === a, list.indexOf(a));
      JSON.stringify(list);
      log(list[0] === a, before === a, list.indexOf(a), list.includes(before));
      const vals = Object.values({k: a}); log(vals[0] === a);
      const found = list.find(x => x.id === 1); log(found === a, found === before);
      const wrapped = [[a]]; const flat = wrapped.flat(); log(flat[0] === a);
      const m = new Map([[a, a]]); log(m.get(a) === a, [...m.keys()][0] === before);
      log(a == list[0], a != list[1], a === {id: 1}, [a].concat([a])[1] === a);
    }
    run();`,
  'objects: delete on arrays': `
    function run(){
      const a = [1, 2, 3, 4];
      log(delete a[1], a.length, 1 in a, JSON.stringify(a));
      log(delete a[3], a.length, a);
      log(delete a['0'], delete a[10], delete a[-1], delete a.nothing, a.length, a);
      log(delete a.length, a.length);
      (function(){ 'use strict'; try { delete a.length; } catch (e) { logError(e); } })();
      a.push('p'); log(a, a.length);
      const b = [1, 2]; delete b[1]; log(b.pop(), b.length, b.pop(), b.length);
    }
    run();`,
  'objects: Object.create and inherited writes': `
    function run(){
      const proto = {kind: 'proto', counter: 0, list: []};
      const child = Object.create(proto);
      child.own = 1;
      log(child.kind, child.own, 'kind' in child, child.hasOwnProperty('kind'), Object.keys(child).join());
      child.counter++; child.list.push('shared');
      log(child.counter, proto.counter, proto.list.length, child.hasOwnProperty('counter'));
      const lit = {__proto__: child, deep: true};
      log(lit.kind, lit.own, lit.counter, lit.list === proto.list);
      proto.kind = 'changed'; log(lit.kind, child.kind);
      delete child.counter; log(child.counter, lit.counter);
      const ro = Object.create(Object.defineProperty({}, 'fixed', {value: 1, writable: false}));
      ro.fixed = 2; log(ro.fixed, ro.hasOwnProperty('fixed'));
      const bare = Object.create(null); bare.k = 'v'; log(bare.k, 'toString' in bare, Object.keys(bare).join());
    }
    run();`,
  'objects: methods with this on literals': `
    function run(){
      const counter = {n: 0, inc(){ this.n++; return this; }, add(k){ this.n += k; return this; }, get double(){ return this.n * 2; }};
      counter.inc().inc().add(0.5).inc();
      log(counter.n, counter.double);
      const other = {n: 100, inc: counter.inc};
      other.inc(); log(other.n, counter.n);
      const bound = counter.add.bind(other); bound(1); log(other.n);
      const calc = {vals: [1, 2, 3], total(){ return this.vals.reduce((s, v) => s + v * this.factor, 0); }, factor: 2};
      log(calc.total()); calc.factor = 0.5; calc.vals.push(4); log(calc.total());
      const key = 'dyn'; const m = {[key](){ return key; }, ['n' + 1]: 1}; log(m.dyn(), m.dyn.name, m.n1);
    }
    run();`,
  'objects: arrays of numbers with mixed representations': `
    function run(){
      const a = [1, 2, 3, 4];
      a[0] += 0.5; a[1] = -0; a[2] = NaN; a[3] *= 1e10;
      log(a, a.indexOf(NaN), a.includes(NaN), a.indexOf(0));
      const b = a.slice().sort((x, y) => x - y); log(b);
      for (let i = 0; i < a.length; i++) a[i] = a[i] | 0;
      log(a);
      const acc = []; let v = 0.1;
      for (let i = 0; i < 6; i++) { acc.push(v); v = v * 10; }
      log(acc, acc.map(x => Math.floor(x)));
      const ints = [5, 3]; ints[1] -= 3; ints[0] /= 2; log(ints, Object.is(ints[1], 0));
    }
    run();`,
  // ---------- typed values (int32 / f64 / str) flowing through objects ----------
  'objects: int32 values and keys': `
    function run(){
      const o = {};
      for (let i = -3; i < 3; i++) o[i] = i * 2;
      log(JSON.stringify(o), Object.keys(o).join());
      const big = 1 << 30, min = 1 << 31, m1 = ~0;
      o.big = big; o.min = min; o.m1 = m1; o.u = -1 >>> 0; o.sh = 5 >> 1; o.x = 7 ^ 2;
      log(o.big, o.min, o.m1, o.u, o.sh, o.x, typeof o.big, o.big === 1073741824, o.min === -2147483648);
      const a = [];
      a[m1] = 'neg'; a[big] = 'big'; a[min] = 'min';
      log(a.length, a[-1], a['-1'], a[-2147483648], Object.keys(a).join());
      let k = 0;
      for (let i = 0; i < 40; i++) k = (k * 31 + i) | 0;
      o.hash = k; log(o.hash, k);
      const arr = [10, 20, 30, 40];
      log(arr[k & 3], arr[k >>> 30], arr[k >> 30], arr[1 << 1], arr[~-2]);
      const ints = {a: 1 | 0, b: 2 & 3, c: Math.imul(3, 4), d: Math.clz32(1)};
      log(JSON.stringify(ints), ints.a + ints.b, ints.c / ints.d);
    }
    run();`,
  'objects: -0 and integral floats through typed locals': `
    function run(){
      const z = -0;
      function mk(v){ return {v, w: v * 1, s: '' + v, t: \`\${v}\`}; }
      for (const o of [mk(z), mk(0 * -1), mk(0.5 + 0.5), mk(1e21), mk(NaN), mk(2 ** 30), mk(-(2 ** 30)), mk(-1073741825)])
        log(o.v, Object.is(o.v, -0), Object.is(o.w, -0), o.s, o.t);
      const n = {}; n.x = z; n.y = -z; n.q = z + 0; n.r = z - 0; n.p = z * -1;
      log(Object.is(n.x, -0), Object.is(n.y, -0), Object.is(n.q, -0), Object.is(n.r, -0), Object.is(n.p, -0));
      const arr = [z, -z, z * 1]; arr.push(z);
      log(Object.is(arr[0], -0), Object.is(arr[1], -0), Object.is(arr[2], -0), Object.is(arr[3], -0), 1 / arr[3]);
      const f = {v: 0.5}; f.v += 0.5;
      log(f.v === 1, [1, 2].indexOf(f.v), new Set([1]).has(f.v), new Map([[1, 'one']]).get(f.v), ['zero', 'one'][f.v], ({1: 'x'})[f.v]);
      switch (f.v) { case 1: log('case 1'); break; default: log('default'); }
      const keyed = {}; keyed[f.v] = 'k'; log(Object.keys(keyed).join(), keyed['1']);
      let s = 0; for (let i = 0; i < 10; i++) s += 0.1; f.s = s; log(f.s, f.s === 1, f.s < 1);
      const m = {x: 2 ** 31}; m.x -= 2 ** 31; log(m.x === 0, Object.is(m.x, 0));
    }
    run();`,
  'objects: keys built from strings and numbers': `
    function run(){
      const s = 'abcdef';
      const o = {};
      for (let i = 0; i < s.length; i++) o[s.charAt(i)] = s.charCodeAt(i);
      log(JSON.stringify(o));
      const a = [];
      a[s.indexOf('c')] = 'c'; a['' + s.indexOf('e')] = 'e'; a[s.slice(1, 2)] = 'b';
      log(a.length, JSON.stringify(a), a.b, Object.keys(a).join());
      const k = {};
      k[s.slice(0, 0)] = 'empty'; k[s.charAt(99)] = 'oob'; k[String(-0)] = 'negzero'; k['' + 1 / 3] = 'third';
      k[s.repeat(2)] = 'rep'; k[s.substring(4, 1)] = 'sub';
      log(JSON.stringify(k), k[''], k[0], k[1 / 3]);
      const arr = [];
      for (const p of ['1', '0', '01', '4294967295', '4294967294', '-0', '1.0']) arr['' + p] = p;
      log(arr.length, Object.keys(arr).join());
      const em = '\\uD83D' + '\\uDE00'; const lit = {'\\uD83D\\uDE00': 'smile'}; lit[em + ''] = 'set';
      log(lit['\\uD83D\\uDE00'], Object.keys(lit).length, em.length);
      const x = 1 << 31; const ik = {}; ik['n' + x] = 1; ik[x] = 2; ik[x + ''] = 3; log(JSON.stringify(ik));
      const nk = {};
      for (const v of [0.1, 1e21, -1e-7, -0, 2 ** 53, NaN, -Infinity, 4294967295, 2 ** 31, 5e-324, 100, -5]) { const y = v * 1; nk['' + y] = 1; nk[\`k\${y}\`] = 1; }
      log(Object.keys(nk).join('|'));
      const L = 'len' + 'gth', P = '__pro' + 'to__';
      const b = [1, 2, 3, 4]; b[L] = 2; log(b.length, b.join(), b[L]);
      const base = {hello(){ return 'hi'; }}; const c = {}; c[P] = base;
      log(typeof c.hello, Object.keys(c).join(), Object.getPrototypeOf(c) === base);
      const nlit = {0x10: 'hex', 1e3: 'exp', .5: 'half', 1_000_0: 'sep', 0b11: 'bin', 1e21: 'big', 0.1: 'tenth', 5(){ return 'm5'; }, '007': 'bond'};
      log(Object.keys(nlit).join('|'), nlit[16], nlit[0.5], nlit['1e+21'], nlit[5](), nlit[5].name);
    }
    run();`,
  'objects: loop-built objects with induction variables': `
    function run(){
      const n = 5 | 0;
      const items = [];
      for (let i = 0; i < n; i++) items.push({i, get: () => i, sq: i * i, half: i / 2, key: 'k' + i});
      log(items.map(o => o.i + ':' + o.get() + ':' + o.sq + ':' + o.half + ':' + o.key).join(' '));
      const byKey = {};
      for (let i = n; i > -3; i--) byKey[i] = i;
      log(Object.keys(byKey).join(), JSON.stringify(byKey));
      const grid = [];
      for (let y = 0; y < 3; y++) { grid.push([]); for (let x = 0; x < 3; x++) grid[y].push(x * 3 + y); }
      log(JSON.stringify(grid));
      const near = {total: 0};
      for (let i = 2147483640; i < 2147483647; i++) near.total += i - 2147483640;
      log(near.total);
      const lim = {v: 3};
      for (let i = 0; i < (lim.v | 0); i++) lim.v = i < 5 ? lim.v + 1 : lim.v;
      log(lim.v);
    }
    run();`,
  // ---------- push in WASM ----------
  'objects: push argument evaluation order': `
    function run(){
      const a = [1, 2];
      log(a.push(a.length, a.pop(), a.push(9)), a.join());
      const b = [1];
      log(b.push((b.length = 0, 'x'), b.length), b.join());
      const c = [1, 2, 3];
      try { log(c.push(Object.freeze(c).length)); } catch (e) { logError(e); }
      log(c.length, c.join());
      const d = [1];
      try { log(d.push((Object.preventExtensions(d), 2))); } catch (e) { logError(e); }
      log(d.length);
      const s = [1];
      try { s.push((Object.seal(s), 2)); } catch (e) { logError(e); }
      log(s.length);
      const e = [0];
      log(e.push((e.push = function(...v){ log('own push', v.length); return -1; }, 'arg')), e.length, e[1]);
      log(e.push('again'), e.length);
      delete e.push; log(e.push('restored'), e.length, e.join());
      let n = null, evaluated = false;
      try { n.push((evaluated = true)); } catch (err) { logError(err); }
      log(evaluated);
      let ev2 = false;
      try { ({}).push((ev2 = true)); } catch (err) { logError(err); }
      log(ev2);
      try { 'abc'.push(1); } catch (err) { logError(err); }
    }
    run();`,
  'objects: push onto aliased escaped and holey arrays': `
    function run(){
      const a = [];
      for (let i = 0; i < 20; i++) a.push(i, i * 0.5, 'x' + i);
      log(a.length, a[59], a[58], a[57], a.slice(0, 6).join());
      const holes = [1, , 3]; holes.length = 5; holes.push('p');
      log(holes.length, 3 in holes, holes[5], JSON.stringify(holes));
      const s = [1, 2, 3]; s.length = 1; s.push('q'); log(s.length, s.join(), 2 in s);
      const nested = []; nested.push(nested); log(nested[0] === nested, nested.length);
      const holder = {list: []}; const alias = holder.list;
      holder.list.push(1); alias.push(2); holder['list'].push(3);
      log(holder.list.join(), alias.length);
      const esc = [1]; JSON.stringify(esc); esc.push(2); window.esc = esc; esc.push(3); window.esc.push(4);
      log(esc.join(), window.esc.length, esc === window.esc);
      const iter = [1, 2, 3];
      iter.forEach(v => { if (v < 3) iter.push(v + 10); });
      log(iter.join());
      const notArr = {length: 1, 0: 'a', push: Array.prototype.push};
      log(notArr.push('b', 'c'), notArr.length, notArr[2]);
      const pr = Array.prototype.push; const own = [7];
      log(pr.call(own, 8), pr.apply(own, [9, 10]), own.push.call(own, 11), own.join());
    }
    run();`,
  'objects: push across the dense limit': `
    function run(){
      const big = [];
      big.length = 1048560;
      for (let i = 0; i < 20; i++) big.push(i);
      log(big.length, big[1048560], big[1048579], 1048570 in big, 5 in big);
      const b2 = [];
      b2[1048574] = 'a';
      log(b2.push('b', 'c', 'd'), b2.length, b2[1048577], b2[1048574]);
      const b3 = []; b3[2000000] = 1; log(b3.length, b3.push(2), b3[2000001]);
      const b4 = [0]; b4.length = 1048566; log(b4.push(1), b4.push(2, 3, 4), b4.length, b4[1048569]);
    }
    run();`,
  'objects: push arguments that change the array or its prototype': `
    function run(){
      const a = [1, 2];
      let r = 'unset';
      try { r = a.push((a.length = 4294967295, 'x')); } catch (e) { logError(e); }
      log(r, a.length, a[4294967295]);
      const b = [1, 2, 3];
      const calls = [];
      try {
        r = b.push((Object.defineProperty(Array.prototype, '3', {set(v){ calls.push('setter ' + v); }, configurable: true}), 4));
      } finally { delete Array.prototype[3]; }
      log(r, b.length, 3 in b, b[3], calls.join());
    }
    run();`,
  // ---------- prototype chain ----------
  'objects: prototype changes through Reflect.apply': `
    const o = {own: 1}; const a = [1, , 3];
    log(o.extra, a[1]);
    Reflect.apply(Object.defineProperty, null, [Object.prototype, 'extra', {value: 'e', configurable: true, writable: true}]);
    log(o.extra, ({}).extra);
    delete Object.prototype.extra;
    log(o.extra, a[1]);
    Object.assign.apply(null, [Array.prototype, {1: 'p1'}]);
    log(a[1], [7, , 9][1]);
    delete Array.prototype[1];
    log(a[1]);
    const args = [Object.prototype, {extra: 'spread'}];
    Object.assign(...args);
    log(o.extra);
    delete Object.prototype.extra;
    log(o.extra);`,
  'objects: reading through null and undefined with coercible keys': `
    function run(){
      const seq = [];
      const key = name => ({toString(){ seq.push('key ' + name); return name; }});
      const val = v => { seq.push('val ' + v); return v; };
      const o = {};
      o[key('a')] = val(1); log(seq.splice(0).join(', '));
      o[key('a')] += val(2); log(seq.splice(0).join(', '), o.a);
      const lit = {[key('b')]: val(3), [key('c')]: val(4)}; log(seq.splice(0).join(', '), Object.keys(lit).join());
      log(key('a') in o, delete o[key('a')], seq.splice(0).join(', '));
      const n = null;
      try { n[key('x')] = val(5); } catch (e) { logError(e); }
      log(seq.splice(0).join(', '));
      try { n[key('y')]; } catch (e) { logError(e); }
      log(seq.splice(0).join(', '));
      const bad = {toString(){ throw new RangeError('no'); }};
      let u;
      try { u[bad]; } catch (e) { logError(e); }
      try { n[bad] = 1; } catch (e) { logError(e); }
      const {[key('d')]: d = val('def')} = {}; log(seq.splice(0).join(', '), d);
    }
    run();`,
  // ---------- destructuring and iteration ----------
  'objects: destructuring defaults holes and rest': `
    function run(){
      const arr = [1, , 3, undefined, null];
      const [a = 'da', b = 'db', c = 'dc', d = 'dd', e = 'de', f = 'df'] = arr;
      log(a, b, c, d, e, f);
      const [x, ...rest] = arr; rest.push('r'); log(x, rest.length, 0 in rest, JSON.stringify(rest), arr.length);
      const o = {p: 1, q: {r: [10, 20]}, 'a-b': 'dash'};
      const {p, q: {r: [, second]}, 'a-b': dash, missing = p + 1, ...others} = o;
      log(p, second, dash, missing, JSON.stringify(others));
      const k = 'q'; const {[k]: qq, ['a' + '-b']: dd, ...noQ} = o; log(qq === o.q, dd, Object.keys(noQ).join());
      let s = 0; for (const {p: pv = 5, v = 0} of [{p: 1}, {v: 2}, {p: undefined, v: 3}]) s += pv * 10 + v; log(s);
      let m, n; ({m, n = m} = {m: 'mm'}); log(m, n);
      [o.p, o.q] = [o.q, o.p]; log(typeof o.p, o.q);
      function f2({a, b: [c, d] = [7, 8]} = {}, ...more){ return [a, c, d, more.length].join(); }
      log(f2(), f2({a: 1}), f2({a: 1, b: [2]}, 3, 4));
    }
    run();`,
  'objects: object rest after numeric keys': `
    function run(){
      const {0: z, ...others} = {0: 'zero', x: 1};
      log(z, JSON.stringify(others));
      const {1.5: h, ...o2} = {'1.5': 'h', y: 2};
      log(h, JSON.stringify(o2));
      const i = 0; const {[i]: z2, ...o3} = {0: 'zero', w: 3};
      log(z2, JSON.stringify(o3));
      const {0: first, ...tail} = ['p', 'q'];
      log(first, JSON.stringify(tail));
      const {'2': two, ...o4} = {2: 'two', v: 4};
      log(two, JSON.stringify(o4));
    }
    run();`,
  'objects: for-in while deleting': `
    function run(){
      const o = {a: 1, b: 2, c: 3, 1: 'one'};
      const seen = [];
      for (const k in o) { seen.push(k); if (k === 'a') delete o.b; }
      log(seen.join(), Object.keys(o).join());
      const p = {a: 1, b: 2}; const s2 = [];
      for (const k in p) { s2.push(k); delete p.b; }
      log(s2.join());
      const a = [1, 2, 3, 4]; const s3 = [];
      for (const k in a) { s3.push(k); if (k === '0') a.length = 2; }
      log(s3.join());
      const host = JSON.parse('{"a":1,"b":2}'); const s4 = [];
      for (const k in host) { s4.push(k); delete host.b; }
      log(s4.join());
      const r = {a: 1, b: 2, c: 3}; const s5 = [];
      for (const k in r) { s5.push(k); if (k === 'a') { delete r.c; r.c = 'readded'; } }
      log(s5.join());
      const proto = {inherited: 1}; const child = Object.create(proto); child.own = 2;
      const s6 = []; for (const k in child) s6.push(k); log(s6.join());
    }
    run();`,
  'objects: optional chaining on nested objects': `
    function run(){
      const o = {a: {b: [{c: 1}]}, f(){ return this.a; }, n: null};
      log(o?.a?.b?.[0]?.c, o.x?.y.z, o.n?.x, o.f?.()?.b.length, o.g?.(), o.a?.['b']?.[5]?.c);
      const i = 0; log(o.a.b?.[i].c, o.a.b?.[i + 1]?.c);
      let u; log(u?.x, u?.[0], u?.(), delete u?.x, delete o?.n, 'n' in o);
      const list = [null, {v: 1}, undefined, {v: 2}];
      log(list.map(e => e?.v ?? '-').join());
      let count = 0; const side = () => { count++; return 'x'; };
      u?.[side()]; o.n?.[side()]; o.a?.[side()]; log(count);
      log((o?.a).b.length, (o.n)?.x);
    }
    run();`,
  // ---------- host interplay ----------
  'objects: JSON hooks and structuredClone': `
    function run(){
      const o = {n: 1, d: new Date(0), nested: {toJSON(key){ return 'nested@' + key; }}, u: undefined, f(){}, s: Symbol('x'), arr: [undefined, function(){}, NaN, -0, Infinity]};
      log(JSON.stringify(o));
      log(JSON.stringify(o, ['n', 'arr']), JSON.stringify({a: [1, {b: 2}]}, null, 2).split('\\n').length);
      log(JSON.stringify(o, (k, v) => typeof v === 'number' ? v * 2 : v));
      const lit = {toJSON(){ return {replaced: true}; }};
      log(JSON.stringify([lit, {lit}]));
      const parsed = JSON.parse('{"a":[1,2,{"b":null}],"1":"one","__proto__":{"x":1}}', function(k, v){ return Array.isArray(v) ? v.concat('r') : v; });
      parsed.a.push('p'); parsed.c = parsed.a.length;
      log(JSON.stringify(parsed), parsed.x, Object.keys(parsed).join(), parsed.__proto__.x);
      const clone = structuredClone({m: new Map([[1, {deep: [1]}]]), list: [1, , 3]});
      clone.list.push(4); log(clone.m.get(1).deep.length, clone.list.length, 1 in clone.list);
      const original = {v: [1, 2]}; const sc = structuredClone(original); sc.v.push(3); original.v.push('o');
      log(original.v.join(), sc.v.join());
    }
    run();`,
  'objects: DOM objects and event callbacks': `
    function run(){
      const state = {clicks: 0, history: [], opts: {once: false}};
      const btn = document.getElementById('btn'), app = document.getElementById('app');
      const handler = e => { state.clicks++; state.history.push(e.type + state.clicks); state.last = e.target.id; };
      btn.addEventListener('click', handler, state.opts);
      btn.click(); btn.click();
      log(state.clicks, state.history.join(), state.last);
      btn.removeEventListener('click', handler);
      btn.click(); log(state.clicks);
      Object.assign(app.dataset, {count: state.clicks, name: 'n' + state.clicks});
      app.dataset.extra = state.history.length;
      log(app.getAttribute('data-count'), app.dataset.name, app.dataset.extra, typeof app.dataset.extra);
      const rec = {tag: 'span', text: 'hi'};
      const el = document.createElement(rec.tag); el.textContent = rec.text; app.appendChild(el);
      rec.text = 'changed';
      log(app.innerHTML, el.textContent);
      const custom = new CustomEvent('ping', {detail: state});
      app.addEventListener('ping', e => { e.detail.pinged = (e.detail.pinged || 0) + 1; });
      app.dispatchEvent(custom); app.dispatchEvent(custom);
      log(state.pinged, custom.detail === state);
    }
    run();`,
  'objects: copying and generic array methods': `
    function run(){
      const a = [5, 1, 4];
      log(a.at(-1), a.at(1.7), a.at(-4), a.indexOf(4, -1), a.lastIndexOf(5, -3), a.includes(5, 1));
      log(a.fill(0, 1, 2).join(), a.copyWithin(0, 1).join(), [1, [2, [3, [4]]]].flat(Infinity).join());
      log(Array.from({length: 3}, (_, i) => ({i})).map(o => o.i * 2).join(), Array.of(7).length, Array(3).fill().map((_, i) => i).join());
      const objs = [{k: 'b', v: 1}, {k: 'a', v: 2}, {k: 'b', v: 3}, {k: 'a', v: 4}];
      const grouped = {}; for (const o of objs) (grouped[o.k] ||= []).push(o.v);
      log(JSON.stringify(grouped));
      log(objs.toSorted((x, y) => x.k < y.k ? -1 : x.k > y.k ? 1 : 0).map(o => o.v).join(), objs.map(o => o.v).join());
      const w = objs.with(1, {k: 'z', v: 0}); log(w[1].k, objs[1].k, objs.toReversed()[0].v, objs.toSpliced(0, 2).length);
      log(objs.findLast(o => o.k === 'b').v, objs.findLastIndex(o => o.k === 'a'));
      log(Object.entries(['x', , 'z']).join('|'), Object.values('hi').join(), Object.keys(5).length);
      log(Object.entries({b: 1, a: 2, 1: 3}).map(([k, v]) => k + v).join());
      log(Array.isArray(a), a instanceof Array, Object.prototype.toString.call(a), typeof a);
    }
    run();`,
  // ---------- coercion ----------
  'objects: valueOf BigInt and loose equality': `
    function run(){
      const seq = [];
      const a = {valueOf(){ seq.push('a'); return 6; }}, b = {valueOf(){ seq.push('b'); return 4; }};
      log(a * b, a - b, a / b, a % b, a | b, a & 2, a >>> 0, a << 1, +a, -a, ~a, a ** 2, seq.join(''));
      seq.length = 0; log(a < b, a >= b, a == 6, a + b, seq.join(''));
      const o = {n: 10n};
      o.n += 2n; o.n++; log(String(o.n), typeof o.n);
      try { o.n *= 2; } catch (e) { logError(e); }
      try { log(o.n | 0); } catch (e) { logError(e); }
      try { log(o.n >>> 0n); } catch (e) { logError(e); }
      log(String(o.n * 2n), String(-o.n), o.n > 5, o.n == 13);
      const counted = {count: 0, valueOf(){ return ++this.count; }};
      log(counted | 0, counted * 1, counted + 0, \`\${counted}\`, counted.count);
      const one = [1], pair = [1, 2], empty = [], obj = {}, zero = [0];
      log(one == 1, pair == '1,2', obj == '[object Object]', empty == false, zero == false, [null] == '', one == true);
      log(obj == {}, empty == null, [[]] == 0, [[2]] == 2, [NaN] == 'NaN');
      const v = {valueOf(){ return 3; }, toString(){ return 'str'; }};
      log(v == 3, v == 'str', v + '', \`\${v}\`, String(v), [v] + '', v === 3);
    }
    run();`,
  'objects: accessors with typed backing values': `
    function run(){
      let backing = 0;
      const o = {x: 1};
      Object.defineProperty(o, 'v', {get(){ return backing * 2; }, set(n){ backing = n | 0; }, enumerable: true, configurable: true});
      for (let i = 0; i < 4; i++) { o.v = i + 0.75; o.x += o.v; }
      log(o.x, backing, o.v, JSON.stringify(o));
      o.v++; o.v += 0.5; log(backing, o.v);
      const counts = {get size(){ return Object.keys(this).length; }};
      counts.a = 1; counts.b = 2; log(counts.size, JSON.stringify(counts));
      const temps = {_c: 25, get f(){ return this._c * 9 / 5 + 32; }, set f(v){ this._c = (v - 32) * 5 / 9; }};
      temps.f = 212; log(temps._c, temps.f); temps.f -= 180; log(temps._c, temps.f);
      const sq = Object.defineProperties({}, {side: {value: 3, writable: true, enumerable: true}, area: {get(){ return this.side ** 2; }, enumerable: false}});
      sq.side += 1; log(sq.area, Object.keys(sq).join(), JSON.stringify(sq));
      const sym = Symbol('tag');
      const s = {a: 1, [sym]: 'sym', 2: 'two', [Symbol.toStringTag]: 'Custom'};
      log(s[sym], Object.keys(s).join(), Object.getOwnPropertySymbols(s).length, String(s), JSON.stringify(s), ({...s})[sym]);
      const names = {[1.5]: () => 0, [sym]: function(){}, ['x' + 1]: class {}, 2: function(){}, [-0]: () => 0, get [3](){ return 3; }};
      log(names[1.5].name, names[sym].name, names.x1.name, names[2].name, names[0].name, Object.getOwnPropertyDescriptor(names, 3).get.name);
    }
    run();`,
  'objects: particles with typed step factors': `
    function run(){
      const ps = [];
      for (let i = 0; i < 6; i++) ps.push({x: i, y: 0, vx: 1, vy: i % 2 ? 0.5 : -1});
      let vx = 1;
      for (let t = 0; t < 5; t++) {
        for (const p of ps) { p.x += p.vx * vx; p.y += p.vy; if (p.y < -2) { p.y = -2; p.vy = -p.vy; } p.vx *= -1; }
        vx = vx * 0.5;
        log(ps.map(p => p.x + '/' + p.y).join(' '));
      }
      log(ps.map(p => p.x | 0).join(), ps.filter(p => Number.isInteger(p.x)).length);
      const acc = {sum: 0, sq: 0, min: Infinity, max: -Infinity};
      for (const p of ps) { acc.sum += p.x; acc.sq += p.x * p.x; acc.min = Math.min(acc.min, p.y); acc.max = Math.max(acc.max, p.y); }
      log(JSON.stringify(acc));
      const q = {v: 1073741823}; q.v++; q.v++; q.v -= 2; q.v--; log(q.v, q.v + 1);
      const r = {v: 0}; r.v -= 0; log(Object.is(r.v, 0)); r.v = -r.v; log(Object.is(r.v, -0));
      r.v += 0; log(Object.is(r.v, 0)); r.v *= -1; log(Object.is(r.v, -0)); r.v++; r.v--; log(Object.is(r.v, 0));
    }
    run();`,
  'objects: typed array elements beside plain array elements': `
    function run(){
      const plain = [0.5, -5], f64 = new Float64Array(2), i8 = new Int8Array(2);
      f64[0] = 0.5; i8[1] = -5;
      log(plain[0] > 0, f64[0] > 0, plain[0] === 0, f64[0] === 0);
      log(plain[5] >= 0, f64[5] >= 0, i8[5] >= 0, plain[5] == 0, i8[5] == 0, i8[5] !== 0);
      log(plain[1] < 0, i8[1] < 0, i8[1] === -5, i8[0] === i8[5]);
      const f32 = new Float32Array(2); f32[1] = 2.5;
      log(f32[1] < 3, f32[1] > 2, f32[1] === 2, f32[9] <= 0);
      log(plain[3] += 'x', f64[3] += 'x', f64[0] += 'y', f64[0]);
      log((f64[9] ?? 5) + 1, f64[9] && 1, (f64[9] || 7) * 2, typeof f64[9]);
      const idx = 1.5; log(i8[idx], i8[idx] | 0, i8[idx] + 1, i8[-0], plain[idx]);
    }
    run();`,
  'objects: delete and re-add in a long loop': `
    function run(){
      const flags = {};
      let seen = 0;
      for (let i = 0; i < 100000; i++) { flags.busy = i; if (flags.busy === i) seen++; delete flags.busy; }
      log(seen, 'busy' in flags, Object.keys(flags).length);
    }
    run();
    log('after');`,
};

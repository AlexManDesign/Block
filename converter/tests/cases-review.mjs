// Adversarial review of commit 36ae267: closures as WASM CLOSURE structs called
// through a shared table, dense a[i] fast paths, delete unlinking property
// nodes, int32 remainder, for-in key re-checks and push re-checks.
export const cases = {
  // ---------- closure identity across WASM and host ----------
  'review: closure identity in host collections': `
    function run(){
      const f = x => x + 1, g = function (y) { return y * 2; };
      const m = { meth() { return 1; } }.meth;
      const map = new Map([[f, 'f'], [g, 'g']]);
      const wm = new WeakMap(); wm.set(g, 'weak-g'); wm.set(m, 'weak-m');
      const set = new Set([f, g, f, m]);
      log(map.get(f), map.get(g), map.has(m), wm.get(g), wm.get(m), wm.has(f), set.size);
      const keys = [...map.keys()];
      log(keys[0] === f, keys[1] === g, Object.is(keys[0], f), keys.indexOf(g), keys.lastIndexOf(f), keys.includes(m));
      const back = Array.from(set);
      log(back[0] === f, back[2] === m, back.findIndex(x => x === g), [f, g].concat([m]).indexOf(m));
      const viaJSON = JSON.parse('[1,2]', function (k, v) { return k === '0' ? f : v; });
      log(viaJSON[0] === f, typeof viaJSON[0], viaJSON[0](41));
      const holder = { list: [f] }; const copy = Object.assign({}, holder);
      log(copy.list[0] === f, structuredClone([1])[0], Object.values({ a: g })[0] === g);
      let n = 0; const h = () => n++;
      const btn = document.getElementById('btn');
      btn.addEventListener('click', h); btn.addEventListener('click', h);
      btn.click(); btn.removeEventListener('click', h); btn.click();
      log(n);
      const once = [];
      const k = e => once.push(e.type);
      btn.addEventListener('click', k, { once: true }); btn.click(); btn.click(); btn.removeEventListener('click', k);
      log(once.join());
    }
    run();`,

  'review: typeof instanceof and Function.prototype on closures': `
    function run(){
      const arrow = (a, b) => a;
      const fn = function (a, b, c) { return this; };
      const meth = { m(a) { return a; } }.m;
      const all = [arrow, fn, meth];
      log(all.map(f => typeof f).join(), typeof all[0], typeof { x: arrow }.x);
      log(all.map(f => f instanceof Function).join(), all.map(f => f instanceof Object).join());
      log(all.map(f => Object.prototype.toString.call(f)).join());
      log(all.map(f => Object.getPrototypeOf(f) === Function.prototype).join(), all.map(f => f.constructor === Function).join());
      log(all.map(f => Object.hasOwn(f, 'prototype')).join(), all.map(f => 'prototype' in f).join(), fn.prototype.constructor === fn);
      log(all.map(f => f.length).join(), all.map(f => f.name).join());
      log(Function.prototype.call.call(arrow, null, 7, 8), Function.prototype.apply.call(meth, null, [9]), Reflect.apply(arrow, 1, [5]));
      const b = fn.bind('ctx', 1);
      log(typeof b, b.name, b.length, typeof b(), b() instanceof String);
      log(typeof arrow.call, arrow.call === Function.prototype.call, arrow.hasOwnProperty('name'), Object.getOwnPropertyNames(meth).sort().join());
      log(Function.prototype.toString.call(arrow).length > 0, typeof String(fn));
    }
    run();`,

  'review: properties on closures written and read from both sides': `
    function run(){
      const f = () => 1;
      f.x = 1; f['y'] = 2; f[0] = 'zero'; f[1.5] = 'frac';
      log(f.x, f.y, f[0], f['0'], f[1.5], Object.keys(f).join(), JSON.stringify(Object.entries(f)));
      Object.assign(f, { z: 3 }); Object.defineProperty(f, 'hidden', { value: 'h', enumerable: false });
      log(f.z, f.hidden, 'z' in f, 'hidden' in f, f.hasOwnProperty('x'), Object.keys(f).length);
      f.x++; f.y += 10; f.count ??= 5; f.count ||= 9; f.z **= 2;
      log(f.x, f.y, f.count, f.z, --f.x, f.x--, f.x);
      log(delete f.y, 'y' in f, f.y, delete f[0], f[0]);
      const ks = []; for (const k in f) ks.push(k); log(ks.join());
      const spread = { ...f }; log(JSON.stringify(spread));
      const g = function () {}; g.prototype.shared = 's'; g.x = 'gx';
      log(new g().shared, g.x, Object.keys(g).join());
      try { 'use strict'; f.name = 'renamed'; log('assigned', f.name); } catch (e) { logError(e); }
      log(f.name, f.length);
    }
    run();`,

  'review: name and length of closures': `
    function run(){
      const a = () => {}, b = function () {}, c = (x, y = 1, z) => {}, d = (...r) => {}, e = ({ p }, [q]) => {};
      let g; g = () => {}; let h; h ||= function () {};
      const { k = () => {} } = {}; const [l = x => x] = [];
      const o = { m() {}, n: () => {}, ['comp' + 'uted']: function () {}, 5: () => {}, get acc() { return 1; }, set acc(v) {} };
      const s = Symbol('sym'), t = Symbol();
      const p = { [s]: () => {}, [t]: function () {} };
      log([a, b, c, d, e, g, h, k, l].map(f => f.name + ':' + f.length).join(' '));
      log([o.m, o.n, o.computed, o[5]].map(f => f.name + ':' + f.length).join(' '));
      const desc = Object.getOwnPropertyDescriptor(o, 'acc'); log(desc.get.name, desc.set.name, desc.set.length);
      log(JSON.stringify(p[s].name), JSON.stringify(p[t].name));
      function outer(cb = () => 0) { return cb.name; } log(outer(), outer(function named() {}));
      const arr = [() => {}, function () {}]; log(JSON.stringify(arr[0].name), JSON.stringify(arr[1].name));
      const obj = {}; obj.prop = () => {}; log(JSON.stringify(obj.prop.name));
    }
    run();`,

  'review: new on closures and new.target': `
    function run(){
      const F = function (x) { this.x = x; this.t = new.target === F; this.arrowTarget = (() => new.target)(); };
      const a = new F(1), b = new F;
      log(a.x, a.t, a.arrowTarget === F, b.x, a instanceof F, Object.getPrototypeOf(a) === F.prototype);
      log(F(2) === undefined, typeof globalThis.x);
      const R = function () { return { replaced: true }; }, P = function () { this.kept = 1; return 5; };
      log(JSON.stringify(new R()), JSON.stringify(new P()));
      const G = function () {};
      const viaReflect = Reflect.construct(F, [3], G);
      log(viaReflect.x, viaReflect.t, Object.getPrototypeOf(viaReflect) === G.prototype, viaReflect.arrowTarget === G);
      const fs = [F]; const c = new fs[0](4); log(c.x, c instanceof fs[0]);
      const ns = { F }; const d = new ns.F(5); log(d.x);
      try { new (() => {})(); } catch (e) { logError(e); }
      try { new ({ m() {} }).m(); } catch (e) { logError(e); }
      const args9 = new F(1, 2, 3, 4, 5, 6, 7, 8, 9); log(args9.x);
      function Outer() { return () => new.target; }
      const plain = Outer(), built = new Outer();
      log(plain(), built() === Outer, typeof built);
    }
    run();`,

  'review: closures as methods of wasm and host objects': `
    function run(){
      const o = { v: 'wasm', get() { return this && this.v; }, arrow: () => typeof this };
      log(o.get(), (o.get)(), o['get'](), o?.get(), o.get?.(), (0, o.get)() === undefined ? 'undef-this' : 'global-this');
      const host = document.getElementById('app');
      host.method = function () { return this.id; };
      host.method2 = o.get; host.v = 'host';
      log(host.method(), host.method2(), host['method'](), host.method === host.method);
      const parsed = JSON.parse('{"v":"parsed"}'); parsed.get = o.get;
      log(parsed.get(), parsed.get.call(o), o.get.call(parsed));
      const bare = Object.create(null); bare.v = 'bare'; bare.get = o.get; log(bare.get());
      const arr = [1, 2, 3]; arr.sum = function () { let s = 0; for (const x of this) s += x; return s; };
      log(arr.sum(), arr.map.call([4], x => x).length);
      const nested = { inner: { v: 'inner', get: o.get } }; log(nested.inner.get());
      const list = [o.get]; log(list[0]() === undefined, typeof list[0].call({ v: 1 }));
    }
    run();`,

  'review: sloppy this with primitive receivers': `
    var self = this;
    function kind() { return typeof this; }
    function isGlobal() { return this === self && this === globalThis && this === window; }
    function strictKind() { 'use strict'; return this === undefined ? 'undefined' : this === null ? 'null' : typeof this; }
    Number.prototype.k = kind; String.prototype.k = kind; Boolean.prototype.k = kind; Symbol.prototype.k = kind; BigInt.prototype.k = kind;
    Number.prototype.sk = strictKind; String.prototype.sk = strictKind; Boolean.prototype.sk = strictKind;
    function run(){
      const s = Symbol('q'), big = 10n;
      log((5).k(), (5.5).k(), 'str'.k(), true.k(), s.k(), big.k());
      log((5).sk(), 'str'.sk(), false.sk());
      log(kind(), isGlobal(), kind.call(null), kind.call(undefined), isGlobal.call(null), isGlobal.call(undefined));
      log(strictKind(), strictKind.call(null), strictKind.call(7), strictKind.call('x'));
      const fs = [kind, isGlobal, strictKind];
      log(fs[0](), fs[1](), fs[2](), fs[0].call(1), fs[2].call(1));
      const wrapped = (3).k === kind; log(wrapped);
      function ret() { return this; }
      const r1 = ret.call(4), r2 = ret.call('w');
      log(typeof r1, r1 instanceof Number, r1 + 1, typeof r2, r2.length, r1 === ret.call(4));
      const nine = (a, b, c, d, e, f, g, h, i) => i;
      Number.prototype.many = function (a, b, c, d, e, f, g, h, i) { return typeof this + i; };
      log((1).many(1, 2, 3, 4, 5, 6, 7, 8, 9), nine(1, 2, 3, 4, 5, 6, 7, 8, 9));
      delete Number.prototype.k; delete Number.prototype.sk; delete Number.prototype.many;
    }
    run();`,

  'review: arrows capture this and new.target': `
    function Maker(tag) {
      this.tag = tag;
      this.getTag = () => this.tag;
      this.getTarget = () => new.target;
      this.nested = () => () => this.tag + '!';
    }
    function run(){
      const m = new Maker('m');
      const getTag = m.getTag, other = { tag: 'other', getTag };
      log(getTag(), other.getTag(), getTag.call({ tag: 'call' }), getTag.apply(null), getTag.bind({ tag: 'b' })());
      log(m.getTarget() === Maker, m.nested()(), [1, 2].map(m.getTag).join());
      const plain = {}; Maker.call(plain, 'plain');
      log(plain.getTag(), plain.getTarget(), typeof plain.nested()());
      const obj = { v: 1, make() { return [() => this.v, () => () => this.v * 10]; } };
      const [a, b] = obj.make(); obj.v = 2;
      log(a(), b()(), a.call({ v: 9 }));
      class Base { constructor() { this.b = 'base'; } }
      class Derived extends Base {
        constructor() {
          const early = () => this.b;
          let first; try { first = early(); } catch (e) { first = e.name; }
          super();
          this.late = early();
          this.first = first;
        }
      }
      const d = new Derived(); log(d.first, d.late);
    }
    run();`,

  'review: closures escaping into JSON and host': `
    function run(){
      const f = () => 1;
      const data = { f, n: 1, list: [f, 2, () => 3], nested: { g: function () {} } };
      log(JSON.stringify(data), JSON.stringify([f]), JSON.stringify(f), String(JSON.stringify(f)));
      log(Object.entries(data).map(([k, v]) => k + ':' + typeof v).join());
      log(Object.values(data)[0] === f, data.list.filter(x => typeof x === 'function').length);
      try { structuredClone({ f }); } catch (e) { log(e.name); }
      const arr = [f, f]; log(arr.indexOf(f), new Set(arr).size, arr.join('|').length > 0);
      const replacer = (k, v) => typeof v === 'function' ? 'fn:' + (v.name || 'anon') : v;
      log(JSON.stringify(data, replacer));
      log(JSON.stringify({ toJSON: () => ({ via: 'toJSON' }) }));
      const sorted = [3, 1, 2].sort(function (a, b) { return a - b; }); log(sorted.join());
      log(typeof Object.fromEntries([['k', f]]).k, Array.of(f)[0] === f);
    }
    run();`,

  'review: host callbacks re-entering compiled closures': `
    function run(){
      const trace = [];
      const inner = x => { trace.push('inner' + x); return x * 2; };
      const outer = x => [x, x + 1].map(inner).reduce((s, v) => s + v, 0);
      log([1, 10].map(outer).join(), trace.join());
      const counts = { n: 0 };
      const cmp = function (a, b) { this === undefined || counts.n++; return a - b; };
      log([5, 3, 9, 1].sort(cmp).join(), counts.n > 0);
      log('a-b-c'.replace(/-/g, (m, off) => '[' + off + ']'), 'x1y2'.replace(/\\d/g, d => inner(+d)));
      const thisArgs = [];
      [1, 2].forEach(function () { thisArgs.push(typeof this); }, 'str');
      [1].forEach(function () { 'use strict'; thisArgs.push(typeof this); }, 'str');
      [1].map(function () { thisArgs.push(this === undefined || this === globalThis ? 'g' : 'o'); });
      log(thisArgs.join());
      const reviverThis = [];
      JSON.parse('{"a":{"b":1}}', function (k, v) { reviverThis.push(k + ':' + Object.keys(this).join('+')); return v; });
      log(reviverThis.join(' '));
      log(Array.from({ length: 4 }, (_, i) => i * i).join(), Array.from('ab', function (c) { return c + this.s; }, { s: '!' }).join());
      const depth = n => n === 0 ? 'bottom' : [n - 1].map(depth)[0];
      log(depth(50));
      let resolved = null;
      new Promise(res => res('sync-executor')).then(v => { resolved = v; });
      window.__done = Promise.resolve().then(() => log('then', resolved));
    }
    run();`,

  'review: deep recursion through closures': `
    function run(){
      const o = { down(n) { return n === 0 ? 0 : 1 + this.down(n - 1); } };
      log(o.down(3000));
      const fs = [];
      fs.push(n => n === 0 ? 'even' : fs[1](n - 1));
      fs.push(n => n === 0 ? 'odd' : fs[0](n - 1));
      log(fs[0](2001), fs[0](2000));
      const sum = (n, acc) => n === 0 ? acc : sum(n - 1, acc + n);
      const store = { sum }; log(store.sum(2500, 0));
    }
    run();`,

  'review: stack overflow through closures is catchable': `
    function run(){
      const o = { inf() { return this.inf() + 1; } };
      try { o.inf(); log('no throw'); } catch (e) { log('caught', e instanceof RangeError); }
      const fs = [() => fs[0]()];
      try { fs[0](); log('no throw'); } catch (e) { log('caught2', e instanceof RangeError); }
      log('after');
    }
    run();`,

  'review: exceptions thrown through call_indirect frames': `
    function run(){
      const thrower = v => { throw v; };
      const mid = (f, v) => { try { return f(v); } finally { log('finally', typeof v); } };
      const values = ['str', 42, 1.5, null, undefined, { k: 1 }, thrower, [1, 2]];
      for (const v of values) {
        try { mid(thrower, v); } catch (e) { log('caught', typeof e, e === v, e === null ? 'null' : typeof e === 'object' ? JSON.stringify(e) : typeof e === 'function' ? 'fn' : e); }
      }
      try { [1, 2].map(x => { if (x === 2) throw new TypeError('from map'); return x; }); } catch (e) { log(e.name, e.message); }
      const rethrow = f => { try { f(); } catch (e) { e.wrapped = true; throw e; } };
      try { rethrow(() => { throw new Error('inner'); }); } catch (e) { log(e.message, e.wrapped); }
      const obj = { m() { return this.missing(); } };
      try { obj.m(); } catch (e) { logError(e); }
      let finallyCount = 0;
      const deep = n => { try { if (n === 0) throw new RangeError('deep'); return deep(n - 1); } finally { finallyCount++; } };
      try { deep(20); } catch (e) { log(e.name, finallyCount); }
      try { JSON.parse('[1]', () => { throw new SyntaxError('reviver'); }); } catch (e) { log(e.name, e.message); }
    }
    run();`,

  'review: argument counts 0 to 12 through closures': `
    function run(){
      const count = (...r) => r.length;
      const first = (a, ...r) => [a, r.length];
      const plain = function (a, b, c, d, e, f, g, h, i, j, k, l) { return [a, h, i, l].map(x => x === undefined ? 'u' : x).join('/'); };
      const fs = [count, first, plain];
      log(fs[0](), fs[0](1), fs[0](1, 2), fs[0](1, 2, 3), fs[0](1, 2, 3, 4), fs[0](1, 2, 3, 4, 5), fs[0](1, 2, 3, 4, 5, 6));
      log(fs[0](1, 2, 3, 4, 5, 6, 7), fs[0](1, 2, 3, 4, 5, 6, 7, 8), fs[0](1, 2, 3, 4, 5, 6, 7, 8, 9), fs[0](1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12));
      log(JSON.stringify(fs[1]()), JSON.stringify(fs[1](1, 2, 3, 4, 5, 6, 7, 8)), JSON.stringify(fs[1](1, 2, 3, 4, 5, 6, 7, 8, 9, 10)));
      log(fs[2](), fs[2](1, 2, 3, 4, 5, 6, 7, 8), fs[2](1, 2, 3, 4, 5, 6, 7, 8, 9), fs[2](1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12));
      const nine = [1, 2, 3, 4, 5, 6, 7, 8, 9], empty = [];
      log(fs[0](...empty), fs[0](...nine), fs[0](0, ...nine, 10), fs[2](...nine), fs[2](...[1, , 3]));
      log(fs[0].apply(null, nine), fs[0].call(null, ...nine), Reflect.apply(fs[0], null, Array(12).fill(0)), fs[0].apply(null, { length: 3 }));
      const o = { count }; log(o.count(1, 2, 3, 4, 5, 6, 7, 8, 9, 10), o['count'](...nine, ...nine));
    }
    run();`,

  'review: functions using arguments called from compiled code': `
    function run(){
      function args() { return arguments; }
      function len() { return arguments.length; }
      function mapped(a, b) { arguments[0] = 'changed'; b = 'b2'; return [a, arguments[1], arguments.length].join(); }
      function strictArgs(a) { 'use strict'; arguments[0] = 'changed'; return a; }
      const fs = [args, len, mapped, strictArgs];
      log(fs[1](), fs[1](1, 2, 3), fs[1](1, 2, 3, 4, 5, 6, 7, 8, 9), fs[1](...[1, 2, 3, 4, 5, 6, 7, 8, 9, 10]));
      log(fs[2]('a', 'b'), fs[2]('a'), fs[3]('orig'));
      const got = fs[0]('s', 1.5, { k: 1 }, null, true, 2 ** 40);
      log(got.length, typeof got[0], got[0], got[1], JSON.stringify(got[2]), got[3], got[4], got[5]);
      log(JSON.stringify(Array.from(got)), JSON.stringify([...got]), Array.prototype.slice.call(got, 1, 3).join('|'));
      log(JSON.stringify(got), Object.keys(got).join());
      const arrowOuter = function () { return (() => arguments.length + ':' + arguments[0])(); };
      log(arrowOuter('x', 'y'), [arrowOuter][0](7));
      function callee() { return arguments.callee === callee; } log([callee][0]());
    }
    run();`,

  'review: getters and setters on object literals': `
    function run(){
      let backing = 1;
      const o = {
        _v: 10,
        get v() { return this._v; }, set v(x) { this._v = x * 2; },
        get closed() { return backing; }, set closed(x) { backing = x; },
        get self() { return this; },
        ['get' + 'Computed']: 1,
        get ['dyn' + 'amic']() { return 'dyn'; },
        method() { return this.v + 1; },
      };
      log(o.v, (o.v = 5), o.v, o._v, o.closed, (o.closed = 7), backing, o.self === o, o.dynamic, o.method());
      const d = Object.getOwnPropertyDescriptor(o, 'v');
      log(typeof d.get, d.get.name, d.set.name, d.get.call({ _v: 'other' }), d.enumerable, d.configurable);
      const child = Object.create(o); child._v = 3; log(child.v, child.method(), child.self === child);
      child.v = 4; log(child._v, o._v, Object.keys(child).join());
      log(JSON.stringify(o), Object.keys(o).join());
      const counter = { n: 0, get next() { return ++this.n; } };
      log(counter.next, counter.next, [counter].map(c => c.next)[0]);
    }
    run();`,

  'review: object literal methods using super': `
    function run(){
      const base = { hello() { return 'base hello ' + this.name; }, x: 'bx', get g() { return 'base getter ' + this.name; } };
      const o = {
        __proto__: base, name: 'o',
        hello() { return 'o>' + super.hello(); },
        viaArrow() { return (() => super.hello())(); },
        prop() { return super.x + '|' + super['x']; },
        get g() { return 'o getter>' + super.g; },
        assign() { super.y = 'set-via-super'; return this.y; },
        plain() { return this.name; },
      };
      log(o.hello(), o.viaArrow(), o.prop(), o.g, o.assign(), Object.hasOwn(o, 'y'), o.plain());
      const moved = { name: 'moved', hello: o.hello }; log(moved.hello());
      const mapped = [o].map(x => x.hello()); log(mapped[0]);
      const other = { hello() { return 'other ' + this.name; } };
      Object.setPrototypeOf(o, other); log(o.hello());
      log(typeof o.hello, o.hello.name, Object.keys(o).join());
    }
    run();`,

  'review: named function expressions': `
    function run(){
      const fact = function f(n) { return n <= 1 ? 1 : n * f(n - 1); };
      const self = function me() { return me; };
      const sloppy = function re() { re = 5; return typeof re; };
      const strict = function re() { 'use strict'; try { re = 5; } catch (e) { return e.name; } return 'no error'; };
      const shadow = function sh() { const sh = 'local'; return sh; };
      log(fact(6), fact.name, self() === self, sloppy(), strict(), shadow(), self.name);
      const fs = [fact, self]; log(fs[0](5), fs[1]() === fs[1], [3, 4].map(fact).join());
      const capture = function cap() { return () => cap; }; log(capture()() === capture);
      const o = { m: function inner() { return typeof inner; } }; log(o.m(), o.m.name);
    }
    run();`,

  // ---------- dense array fast paths ----------
  'review: dense index reads with odd indices': `
    function run(){
      const a = [10, 20, , 40];
      const idx = [0, -0, 1, 1.5, 2, 3, 4, -1, NaN, Infinity, -Infinity, 2 ** 31, 2 ** 32, 2 ** 32 - 1, 0.5 - 0.5, 1e-9];
      log(idx.map(i => String(a[i])).join());
      a[-1] = 'neg'; a[1.5] = 'frac'; a[NaN] = 'nan'; a[2 ** 32] = 'big';
      log(idx.map(i => String(a[i])).join(), a.length, Object.keys(a).join());
      const b = [1, 2, 3]; let z = -0;
      b[z] = 'negzero'; log(b[0], b.length, Object.keys(b).join());
      const c = []; for (let i = 0; i < 5; i++) c[i * 0.5] = i; log(JSON.stringify(c), c.length, Object.keys(c).join());
      const d = [1, 2, 3]; let f = 1; f = f / 2; d[f * 2] = 'two'; log(d.join());
    }
    run();`,

  'review: dense index writes to holes and past length': `
    function run(){
      const a = [1, , 3];
      a[1] = 'filled'; log(a.join(), 1 in a);
      a[5] = 'far'; log(a.length, 4 in a, JSON.stringify(a));
      a.length = 2; log(a[2], 2 in a, a.length);
      a[2] = 'after-shrink'; log(a.join(), a.length);
      a.length = 6; a[4] = 'grown'; log(JSON.stringify(a), 3 in a);
      const b = []; b[0] = 0; b[2] = 2; b[1] = 1; log(b.join(), b.length);
      for (let i = 0; i < 4; i++) b[i] = (b[i] || 0) + 10; log(b.join());
      delete b[1]; log(b[1], 1 in b, b.length); b[1] = 'back'; log(b.join());
    }
    run();`,

  'review: prototype index accessors versus dense fast paths': `
    function run(){
      const a = [1, , 3], b = [7, 8];
      const calls = [];
      Object.defineProperty(Array.prototype, '1', { get() { calls.push('get'); return 'proto1'; }, set(v) { calls.push('set ' + v); }, configurable: true });
      try {
        log(a[1], b[1], a.length);
        a[1] = 'written'; b[1] = 'own';
        log(Object.hasOwn(a, 1), a[1], b[1], calls.join());
        Object.defineProperty(Object.prototype, '0', { get() { return 'objproto0'; }, configurable: true });
        const c = [, 'c1']; log(c[0], [][0], ({})[0]);
        delete Object.prototype[0];
      } finally { delete Array.prototype[1]; }
      log([1, , 3][1], calls.length);
    }
    run();`,

  'review: dense fast paths on frozen and materialized arrays': `
    'use strict';
    function run(){
      const f = Object.freeze([1, 2, 3]);
      try { f[0] = 9; } catch (e) { logError(e); }
      log(f[0], f.length);
      const s = Object.seal([1, 2]); s[0] = 'ok'; try { s[2] = 'no'; } catch (e) { logError(e); } log(s.join(), s.length);
      const ro = [1, 2, 3]; Object.defineProperty(ro, 1, { value: 'ro', writable: false });
      try { ro[1] = 'x'; } catch (e) { logError(e); } log(ro[1], ro[0]);
      const g = [1, 2]; Object.defineProperty(g, 0, { get() { return 'getter'; } }); log(g[0], g[1]);
      const m = [1, 2, 3]; JSON.stringify(m); m[1] = 'after-escape'; log(m.join(), m[1]);
      const host = [1, 2, 3]; document.getElementById('app').data = host; host[0] = 'h'; log(document.getElementById('app').data[0], host[0]);
      const ne = [1, 2]; Object.preventExtensions(ne); ne[0] = 'kept'; try { ne[2] = 'new'; } catch (e) { logError(e); } log(ne.join());
      const lenRO = [1, 2]; Object.defineProperty(lenRO, 'length', { writable: false }); lenRO[0] = 'z'; try { lenRO[2] = 'w'; } catch (e) { logError(e); } log(lenRO.join(), lenRO.length);
    }
    run();`,

  'review: typed loops over arrays with changing length': `
    function run(){
      const a = [1, 2, 3, 4, 5, 6];
      let sum = 0;
      for (let i = 0; i < a.length; i++) { sum += a[i]; if (i === 1) a.length = 4; }
      log(sum, a.length);
      const b = [1, 2, 3];
      for (let i = 0; i < 6; i++) b[i] = (b[i] === undefined ? 'u' : b[i]) + '/' + i;
      log(b.join());
      const c = [0, 1, 2, 3, 4, 5, 6, 7];
      for (let i = 0; i < c.length; i++) c[i] = c[c.length - 1 - i];
      log(c.join());
      const d = [1, 2, 3, 4];
      for (let i = 0; i < 4; i++) { d[i / 2] = 'h' + i; }
      log(JSON.stringify(d), Object.keys(d).join());
      const e = [5, 6, 7]; let k = 0; for (let i = 0; i < 3; i++) k += e[i | 0] * e[i >> 1] - e[i % 2]; log(k);
    }
    run();`,

  // ---------- delete unlinking ----------
  'review: delete first middle last and re-add': `
    function run(){
      const mk = () => ({ a: 1, b: 2, c: 3, d: 4 });
      const o1 = mk(); delete o1.a; log(Object.keys(o1).join(), JSON.stringify(o1));
      const o2 = mk(); delete o2.b; delete o2.c; log(Object.keys(o2).join(), o2.d);
      const o3 = mk(); delete o3.d; o3.e = 5; log(Object.keys(o3).join());
      const o4 = mk(); delete o4.d; delete o4.c; delete o4.b; delete o4.a; log(Object.keys(o4).length, JSON.stringify(o4));
      o4.z = 26; o4.a = 1; log(Object.keys(o4).join(), o4.z, o4.a);
      const o5 = mk(); delete o5.a; o5.a = 'again'; delete o5.d; o5.d = 'dd'; log(Object.keys(o5).join(), JSON.stringify(o5));
      const o6 = mk(); log(delete o6.missing, delete o6.a, delete o6.a, Object.keys(o6).join());
      const o7 = { only: 1 }; delete o7.only; o7.next = 2; o7.more = 3; delete o7.more; o7.last = 4; log(Object.keys(o7).join());
      const o8 = { x: 1, 1: 'one', y: 2 }; delete o8[1]; delete o8['y']; o8[0] = 'zero'; log(Object.keys(o8).join(), JSON.stringify(o8));
      const key = 'b', o9 = mk(); delete o9[key]; const k2 = 2; const o10 = { 2: 'two', a: 1 }; delete o10[k2]; log(Object.keys(o9).join(), Object.keys(o10).join());
    }
    run();`,

  'review: delete many keys and spread': `
    function run(){
      const o = {};
      for (let i = 0; i < 60; i++) o['k' + i] = i;
      for (let i = 0; i < 60; i += 2) delete o['k' + i];
      log(Object.keys(o).length, Object.keys(o).slice(0, 4).join(), o.k59, o.k58, 'k0' in o);
      for (let i = 0; i < 60; i += 3) o['k' + i] = 'r' + i;
      const ks = Object.keys(o); log(ks.length, ks.slice(-3).join(), ks.indexOf('k0'), ks.indexOf('k1'));
      const copy = { ...o }; log(Object.keys(copy).length, copy.k3, copy.k2);
      const { k1, k3, ...rest } = o; log(k1, k3, Object.keys(rest).length);
      let total = 0; for (const k in o) total += typeof o[k] === 'number' ? o[k] : 0; log(total);
      const q = { a: 1, b: 2, c: 3 }; delete q.b; delete q.a; q.a = 'A'; delete q.c; q.b = 'B';
      log(JSON.stringify(q), Object.entries(q).join(';'));
    }
    run();`,

  'review: delete during for-in': `
    function run(){
      const o = { a: 1, b: 2, c: 3, d: 4, e: 5 };
      const seen = [];
      for (const k in o) { seen.push(k); if (k === 'b') { delete o.c; delete o.a; } if (k === 'd') delete o.e; }
      log(seen.join(), Object.keys(o).join());
      const p = { a: 1, b: 2, c: 3 }; const s2 = [];
      for (const k in p) { s2.push(k); delete p[k]; p[k + k] = 1; }
      log(s2.join(), Object.keys(p).join());
      const proto = { shared: 'p', other: 'q' }; const child = Object.create(proto); child.own = 1; child.shared = 'own';
      const s3 = []; for (const k in child) { s3.push(k); if (k === 'own') delete child.shared; } log(s3.join());
      const child2 = Object.create(proto); child2.x = 1; const s4 = [];
      for (const k in child2) { s4.push(k); if (k === 'x') delete proto.other; } log(s4.join());
      const arr = ['a', 'b', 'c', 'd']; const s5 = [];
      for (const k in arr) { s5.push(k); if (k === '0') { delete arr[2]; arr.pop(); } } log(s5.join());
      const t = { a: 1, b: 2 }; const s6 = [];
      for (const k in t) { s6.push(k); if (k === 'a') { delete t.b; t.b = 'back'; } } log(s6.join());
    }
    run();`,

  'review: for-in over a proxy calls traps like the engine': `
    function run(){
      const trace = [];
      const target = { a: 1, b: 2, c: 3 };
      const p = new Proxy(target, {
        ownKeys(t) { trace.push('ownKeys'); return Reflect.ownKeys(t); },
        getOwnPropertyDescriptor(t, k) { trace.push('gopd ' + k); return Reflect.getOwnPropertyDescriptor(t, k); },
        has(t, k) { trace.push('has ' + k); return Reflect.has(t, k); },
        getPrototypeOf(t) { trace.push('proto'); return Reflect.getPrototypeOf(t); },
      });
      for (const k in p) { trace.push('body ' + k); if (k === 'a') delete target.b; }
      log(trace.join(', '));
    }
    run();`,

  // ---------- int32 remainder ----------
  'review: int32 remainder results reused': `
    function run(){
      const out = [];
      for (let i = -4; i <= 4; i++) {
        for (let j = -3; j <= 3; j++) {
          const r = i % j;
          out.push(Object.is(r, -0) ? '-0' : String(r));
          if (r === 0 && 1 / r < 0) out.push('neg');
        }
      }
      log(out.join(' '));
      let minInt = -2147483648 | 0, m1 = -1 | 0, z = 0 | 0;
      const r1 = minInt % m1, r2 = minInt % z, r3 = z % m1, r4 = (minInt + 1) % 2;
      log(Object.is(r1, -0), r2, Object.is(r3, -0), 1 / r3, r4, (r1 | 0) + 1, r1 === 0);
      const arr = ['zero', 'one', 'two'];
      let acc = '';
      for (let i = -6; i < 6; i++) acc += arr[i % 3] === undefined ? 'u' : arr[i % 3][0];
      log(acc);
      let s = 0; for (let i = 0; i < 100; i++) s += (i * 7919) % 13 - (i % -5); log(s);
      let q = -7 | 0; q %= 7; log(Object.is(q, -0), q + 0, String(q), q.toFixed(1), JSON.stringify([q]));
      const k = 5 | 0, nk = -5 | 0; log(k % nk, nk % k, Object.is(nk % k, -0), Math.sign(nk % k), Object.is(Math.max(nk % k, -1), 0));
    }
    run();`,

  // ---------- push re-checks after arguments ----------
  'review: push arguments that change the receiver': `
    function run(){
      let a = [1, 2];
      const r1 = a.push((a = ['replaced'], 3)); log(r1, a.join());
      const b = [1, 2]; const r2 = b.push((b.length = 0, 'x'), 'y'); log(r2, b.join(), b.length);
      const c = [1, 2, 3]; const r3 = c.push(c.pop(), c.pop()); log(r3, c.join());
      const d = [1]; const r4 = d.push(d.push(2), d.push(3)); log(r4, d.join());
      const e = [1]; const r5 = e.push((JSON.stringify(e), 'after-host')); e.push('again'); log(r5, e.join());
      const f = [1, 2]; try { f.push((Object.freeze(f), 'frozen')); } catch (err) { logError(err); } log(f.join(), Object.isFrozen(f));
      const g = [1]; try { g.push((Object.defineProperty(g, 'length', { writable: false }), 'w')); } catch (err) { logError(err); } log(g.join(), g.length);
      const h = [1]; h.push((h.push = function () { return 'own push'; }, 'orig')); log(h.length, h.join(), h.push('x'));
      const i = [1]; const saved = Array.prototype.push;
      try { const r = i.push((Array.prototype.push = function () { return 'patched'; }, 2)); log(r, i.join(), [9].push(1)); } finally { Array.prototype.push = saved; }
      const j = [1, 2, 3]; j.push((j[10] = 'far', 'p')); log(j.length, j[11], 10 in j);
    }
    run();`,

  'review: push arguments that add index accessors': `
    function run(){
      const calls = [];
      const a = [1, 2];
      try {
        const r = a.push((Object.defineProperty(Object.prototype, '2', { set(v) { calls.push('objproto set ' + v); }, get() { return 'objproto'; }, configurable: true }), 'v'));
        log(r, a.length, Object.hasOwn(a, 2), a[2], calls.join());
      } finally { delete Object.prototype[2]; }
      const b = [1];
      try {
        const r = b.push(1, (Reflect.defineProperty(Array.prototype, '2', { set(v) { calls.push('arrproto set ' + v); }, configurable: true }), 'late'));
        log(r, b.length, Object.hasOwn(b, 2), calls.join());
      } finally { delete Array.prototype[2]; }
    }
    run();`,

  'review: prototype changes through bound and nested apply': `
    function run(){
      const a = [0, , 2];
      log(a[1]);
      const define = Object.defineProperty.bind(Object, Array.prototype);
      define('1', { get() { return 'bound-getter'; }, configurable: true });
      log(a[1], [5, , 6][1]);
      delete Array.prototype[1];
      log(a[1]);
      Reflect.apply(Reflect.apply, null, [Object.defineProperty, null, [Array.prototype, '1', { get() { return 'nested'; }, configurable: true }]]);
      log(a[1]);
      delete Array.prototype[1];
      const o = { own: 1 };
      Reflect.apply(Reflect.apply, null, [Object.defineProperty, null, [Object.prototype, 'extra', { value: 'nested-extra', configurable: true, writable: true }]]);
      log(o.extra, ({}).extra);
      delete Object.prototype.extra;
      log(o.extra);
    }
    run();`,
};

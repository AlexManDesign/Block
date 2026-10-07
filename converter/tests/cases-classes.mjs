// Differential cases for classes: fields, accessors, statics, privates,
// inheritance and super, method extraction, coercion hooks, built-in
// subclasses, prototype mutation and numeric hot loops over instances.
export const cases = {
  'classes: field initializer order vs constructor': `
class A {
  a = 1;
  b = this.a + 1;
  ['c' + 1] = this.b * 10;
  constructor(x) { log('ctor sees', this.a, this.b, this.c1); this.z = x; this.a = 'over'; }
  d = 'last';
}
const o = new A(5);
log(Object.keys(o), o.a, o.b, o.c1, o.d, o.z);
log(JSON.stringify(o));`,

  'classes: derived field order around super': `
class B { constructor() { this.fromBase = 1; log('base', Object.keys(this)); } }
class D extends B {
  f1 = 'one';
  constructor() { log('before super'); super(); log('after super', Object.keys(this)); this.own = 2; }
  f2 = this.f1 + '+';
}
const d = new D();
log(Object.keys(d), d.f2);`,

  'classes: field define semantics, arrow fields and virtual calls from initializers': `
const calls = [];
class A { set x(v) { calls.push('setter ' + v); } get x() { return 'getter'; } }
class B extends A { x = 1; }
class C extends A { constructor() { super(); this.x = 2; } }
const b = new B(), c = new C();
log(calls, b.x, c.x, Object.keys(b), Object.keys(c));
class P { __proto__ = 5; constructor() { this.k = 1; } }
const p = new P();
log(Object.keys(p), Object.getPrototypeOf(p) === P.prototype, p.__proto__ === 5);
class H { x = 1; constructor() { Object.preventExtensions(this); } }
try { new (class extends H { y = 2; })(); } catch (e) { logError(e); }
class Btn {
  label = 'b'; count = 0;
  onClick = () => { this.count++; return this.label + this.count; };
  static make(label) { const b = new Btn(); b.label = label; return b; }
}
const x = Btn.make('x'), y = Btn.make('y');
log([x.onClick, y.onClick, x.onClick].map(h => h()), x.count, y.count, x.onClick === y.onClick, Object.keys(x));
class V1 { v = this.compute(); compute() { return 'V1'; } }
class V2 extends V1 { w = 2; compute() { return 'V2:' + this.w; } }
class V3 extends V2 { compute() { return 'V3:' + super.compute(); } }
log(new V1().v, new V2().v, new V3().v, new V3().w);`,

  'classes: getters and setters': `
class Temp {
  #c = 0;
  get c() { return this.#c; }
  set c(v) { if (typeof v !== 'number') throw new TypeError('num'); this.#c = v; }
  get f() { return this.#c * 9 / 5 + 32; }
  set f(v) { this.#c = (v - 32) * 5 / 9; }
}
const t = new Temp();
t.c = 100; log(t.c, t.f);
t.f = -40; log(t.c, t.f);
try { t.c = '5'; } catch (e) { logError(e); }
log(t.c, Object.keys(t), 'c' in t, Object.hasOwn(t, 'c'));
const d = Object.getOwnPropertyDescriptor(Temp.prototype, 'f');
log(typeof d.get, typeof d.set, d.enumerable, d.get.name, d.set.name);
class GetOnly { get x() { return 1; } strictSet() { try { this.x = 5; return 'no throw'; } catch (e) { return e.name; } } }
const g = new GetOnly();
g.x = 5;
log(g.x, g.strictSet());
class SetOnly { set y(v) { this._y = v; } }
const s = new SetOnly(); s.y = 3; log(s.y, s._y);`,

  'classes: static members and static blocks order': `
const order = [];
class S {
  static a = (order.push('a'), 1);
  static { order.push('block1:' + S.a + ':' + typeof S.b); }
  static b = S.a + 1;
  static helper() { return 'h' + S.b; }
  static c = this.helper();
  static { order.push('block2:' + this.c); this.d = this.name + '!'; }
}
log(order.join(' '), S.a, S.b, S.c, S.d);
log(Object.keys(S), typeof S.helper);
class L { static { var leaked = 1; this.l = typeof leaked; } }
log(L.l, typeof leaked);`,

  'classes: static inheritance, super in statics and private statics': `
class A {
  static count = 0;
  static create(...args) { this.count++; return new this(...args); }
  static describe() { return 'I am ' + this.name; }
  static get kind() { return 'A-kind'; }
  static set kind(v) { this._k = v; }
}
class B extends A {
  constructor(x) { super(); this.x = x; }
  static describe() { return super.describe() + ' (B)'; }
  static get kind() { return 'B<' + super.kind + '>'; }
  static set kind(v) { super.kind = v + '!'; }
  static { this.fromBlock = super.describe(); }
  static fld = super.kind;
}
const b = B.create(7);
log(b instanceof B, b.x, A.count, B.count, Object.hasOwn(B, 'count'));
log(A.describe(), B.describe(), B.fromBlock, B.fld);
B.kind = 'x';
log(B.kind, A.kind, B._k, Object.hasOwn(B, '_k'));
class Cfg {
  static #store = { a: 1 };
  static get(k) { return this.#store[k]; }
  static set(k, v) { this.#store[k] = v; return this; }
  static get size() { return Object.keys(Cfg.#store).length; }
}
Cfg.set('b', 2).set('c', 3);
log(Cfg.get('a'), Cfg.get('c'), Cfg.size);
class Sub extends Cfg {}
try { Sub.get('a'); } catch (e) { logError(e); }
log(Sub.size);`,

  'classes: private fields methods and brand check': `
class Acc {
  #balance = 0;
  static #instances = 0;
  #log = [];
  constructor(start) { this.#deposit(start); Acc.#instances++; }
  #deposit(v) { this.#balance += v; this.#log.push(v); }
  get #total() { return this.#balance; }
  deposit(v) { this.#deposit(v); return this.#total; }
  static isAcc(o) { return #balance in o; }
  static get instances() { return Acc.#instances; }
  history() { return this.#log.slice(); }
  equals(o) { return #balance in o && o.#balance === this.#balance; }
}
const a = new Acc(10), b = new Acc(5);
log(a.deposit(5), b.deposit(10), a.equals(b), a.history());
log(Acc.isAcc(a), Acc.isAcc({}), Acc.instances, Object.keys(a), JSON.stringify(a));
try { Acc.prototype.deposit.call({}, 1); } catch (e) { logError(e); }
class Early { x = this.#m(); #m() { return 'pm'; } #y = 'late'; get y() { return this.#y; } }
log(new Early().x, new Early().y);
class Base2 { constructor() { try { this.r = this.callPm(); } catch (e) { this.r = e.name; } } }
class D2 extends Base2 { #pm() { return 'ok'; } callPm() { return this.#pm(); } }
log(new D2().r);`,

  'classes: private names per evaluation, nesting and siblings': `
function make() { return class { #x = 1; static has(o) { return #x in o; } get x() { return this.#x; } }; }
const A = make(), B = make();
const a = new A(), b = new B();
log(A.has(a), A.has(b), B.has(b), B.has(a), a.x, b.x);
try { Object.getOwnPropertyDescriptor(A.prototype, 'x').get.call(b); } catch (e) { logError(e); }
const classes = [];
for (let i = 0; i < 3; i++) classes.push(class { #x = i; static has(o) { return #x in o; } get x() { return this.#x; } });
const objs = classes.map(C => new C());
log(objs.map(o => o.x), classes.map(C => objs.filter(o => C.has(o)).length));
class Outer {
  #x = 'outer';
  static Inner = class { #x = 'inner'; read(o) { return o.#x; } readOuter(o) { return Outer.peek(o); } };
  static peek(o) { return o.#x; }
  reader() { const self = this; return new (class { #y = 1; get() { return self.#x + this.#y; } })(); }
}
const o = new Outer(), i = new Outer.Inner();
log(i.read(i), i.readOuter(o), o.reader().get());
try { i.read(o); } catch (e) { logError(e); }
try { Outer.peek(i); } catch (e) { logError(e); }
class S1 { #x = 'S1'; static r(o) { return o.#x; } }
class S2 { #x = 'S2'; static r(o) { return o.#x; } }
log(S1.r(new S1()), S2.r(new S2()));
try { S1.r(new S2()); } catch (e) { logError(e); }`,

  'classes: private compound ops, destructuring and accessor errors': `
class P {
  #a = 1; #b = 2; #n = null;
  static #s = 10;
  #m() { return 'm'; }
  get #g() { return 'g'; }
  set #st(v) { this.log = v; }
  run() {
    this.#a += 5; this.#b++; ++this.#b; this.#n ??= 'filled'; this.#a **= 2;
    [this.#a, this.#b] = [this.#b, this.#a];
    ({ q: this.#n } = { q: 'dq' });
    this.#st = 'via setter';
    const f = this.#m;
    let r1, r2, r3;
    try { this.#m = 1; } catch (e) { r1 = e.name; }
    try { this.#g = 1; } catch (e) { r2 = e.name; }
    try { this.#st; r3 = 'no'; } catch (e) { r3 = e.name; }
    P.#s--;
    return [this.#a, this.#b, this.#n, f.call(this), this.#g, this.log, r1, r2, r3, P.#s, this?.#a];
  }
  static check(o) { try { return #a in o; } catch (e) { return e.name; } }
}
log(new P().run(), P.check(1), P.check(null), P.check([]));
class Holder {
  #obj; #arr;
  constructor(o, a) { this.#obj = o; this.#arr = a; }
  same(o, a) { return [this.#obj === o, this.#arr === a]; }
  bump() { this.#obj.n++; this.#arr.push(this.#obj.n); return this; }
  get view() { return [this.#obj.n, this.#arr.length]; }
}
const obj = { n: 1 }, arr = [];
const h = new Holder(obj, arr);
h.bump().bump();
log(h.same(obj, arr), h.view, obj.n, arr, new Map([[obj, 'o']]).get(obj));
obj.n = 100; log(h.view);`,

  'classes: inheritance chain with super method calls': `
class A { who() { return 'A'; } chain() { return [this.who()]; } get tag() { return 'tA'; } }
class B extends A { who() { return 'B'; } chain() { return super.chain().concat('B:' + super.who()); } get tag() { return super.tag + '>tB'; } }
class C extends B { who() { return 'C'; } chain() { return super.chain().concat('C:' + super.who()); } get tag() { return super.tag + '>tC'; } }
const c = new C();
log(c.chain(), c.tag, new B().chain(), new A().tag);
log(c instanceof A, c instanceof B, c instanceof C, new A() instanceof C);
log(Object.getPrototypeOf(C.prototype) === B.prototype, C.__proto__ === B);
class X { constructor() { this.from = 'X'; } }
class Y { constructor() { this.from = 'Y'; } }
class Z1 extends X { constructor() { super(); } }
class Z2 extends X {}
Object.setPrototypeOf(Z1, Y); Object.setPrototypeOf(Z2, Y);
log(new Z1().from, new Z2().from, new Z1() instanceof X, new Z1() instanceof Y);`,

  'classes: super property assignment and compound ops': `
class A { m() { return 'A.m'; } get count() { return this._c ?? 0; } set count(v) { this._c = v; } }
class B extends A {
  setViaSuper() { super.x = 5; return [this.x, Object.hasOwn(this, 'x'), A.prototype.x]; }
  m() { const f = () => super.m(); return f() + '+B'; }
  inc() { super.count++; super.count += 10; super['count'] *= 2; super.count ??= 99; return [this._c, super.count, Object.keys(this)]; }
  plain() { super.fresh = 1; super.fresh += 1; return [this.fresh, Object.hasOwn(this, 'fresh'), Object.hasOwn(A.prototype, 'fresh')]; }
  del() { try { delete super.count; return 'no'; } catch (e) { return e.name; } }
  opt() { return [super.missing?.(), super.inc?.name]; }
}
const b = new B();
log(b.setViaSuper(), b.m(), b.inc(), b.plain(), b.del(), b.opt());
const base = { greet() { return 'base ' + this.n; } };
const obj = { __proto__: base, n: 1, greet() { return super.greet() + '!'; } };
log(obj.greet());`,

  'classes: overriding and polymorphic dispatch in loop': `
class Shape { area() { return 0; } toString() { return this.constructor.name + '(' + this.area() + ')'; } }
class Sq extends Shape { constructor(s) { super(); this.s = s; } area() { return this.s * this.s; } }
class Circ extends Shape { constructor(r) { super(); this.r = r; } area() { return Math.round(Math.PI * this.r * this.r * 100) / 100; } }
class Unit extends Sq { constructor() { super(1); } }
const shapes = [new Sq(2), new Circ(1), new Unit(), new Shape(), new Sq(0.5)];
let total = 0;
for (const s of shapes) total += s.area();
log(total, shapes.map(String).join(' '), shapes.map(s => s instanceof Sq));`,

  'classes: method extraction, binding and callbacks': `
class A {
  constructor() { this.v = 42; this.bound = this.get.bind(this); this.arrow = () => this.v; }
  get_() { return this; }
  get() { return this.v; }
}
const a = new A();
const f = a.get, g = a.get_;
log(g() === undefined);
try { f(); } catch (e) { logError(e); }
log(a.bound(), a.arrow(), f.call({ v: 'other' }), f.apply(a, []));
const { bound, arrow } = a;
log(bound(), arrow());
class Multiplier {
  constructor(k) { this.k = k; }
  apply(x) { return x * this.k; }
  applyAll(xs) { return xs.map(this.apply, this); }
  applyArrow(xs) { return xs.map(x => this.apply(x)); }
  applyBroken(xs) { try { return xs.map(this.apply); } catch (e) { return 'err:' + e.name; } }
}
const m = new Multiplier(3);
log(m.applyAll([1, 2, 3]), m.applyArrow([4, 5]), m.applyBroken([1]));
log([1, 2].map(m.apply.bind(m)), [5, 6].reduce((acc, x) => acc + m.apply(x), 0));
log([3, 1, 2].sort(new (class { cmp(a, b) { return b - a; } })().cmp));
class T {
  constructor() { this.v = 'T'; }
  tag(strs, ...vals) { return this.v + ':' + strs.raw.join('|') + ':' + vals.join(','); }
  getFn() { return () => this.v + '!'; }
}
const t = new T();
log(t.tag\`a\${1}b\${2}c\`, t.getFn()(), t.missing?.(), (t.tag)\`p\`, (t.getFn)()());
try { (0, t.getFn)()(); } catch (e) { logError(e); }`,

  'classes: valueOf, toString and toPrimitive coercions': `
class Money {
  constructor(c) { this.cents = c; }
  valueOf() { return this.cents / 100; }
  toString() { return '$' + (this.cents / 100).toFixed(2); }
}
const a = new Money(150), b = new Money(275);
log(a + b, a * 2, a - b, a < b, a > b, +a, -a, a + '', \`\${a}\`, String(b), [a, b].join(';'));
let s = 'x'; s += a;
log('lit' + a, a + 'lit', s, 'q'.concat(a), JSON.stringify({ k: a }));
class Hint { [Symbol.toPrimitive](hint) { return hint === 'number' ? 7 : hint === 'string' ? 'str' : 'def'; } }
const h = new Hint();
log(+h, \`\${h}\`, h + '', h * 2, String(h), h + 1, h == 'def', [h] + '');
class Base { toString() { return 'B<' + this.id + '>'; } }
class Child extends Base { constructor(id) { super(); this.id = id; } }
class Child2 extends Child { toString() { return 'C2/' + super.toString(); } }
const xs = [new Child(1), new Child2(2)];
log(\`\${xs[0]}|\${xs[1]}\`, xs[0] + xs[1], xs.join(), 'x' + xs[1]);
const o = {}; o[xs[0]] = 'key'; log(Object.keys(o));
let total = 0; total -= a; total *= b; log(total, Number(a), parseFloat(b), isNaN(a));`,

  'classes: instances in arrays maps sets and JSON': `
class Pt {
  constructor(x, y) { this.x = x; this.y = y; }
  toJSON() { return [this.x, this.y]; }
}
class Plain { constructor(n) { this.n = n; this.nested = { list: [n, n * 2] }; } }
const pts = [new Pt(1, 2), new Pt(3, 4)];
const m = new Map(pts.map(p => [p, p.x + p.y]));
const s = new Set(pts); s.add(pts[0]);
log(m.get(pts[1]), m.size, s.size, m.has(new Pt(1, 2)));
log(JSON.stringify(pts), JSON.stringify({ p: pts[0], q: new Plain(5) }));
log(pts, new Plain(2), pts.indexOf(pts[1]), pts.includes(pts[0]));
log([new Plain(3), new Plain(1), new Plain(2)].sort((a, b) => a.n - b.n).map(p => p.n));
class Outer { #hidden = 1; constructor() { this.a = new Plain(1); this.b = [new Plain(2), null, undefined]; this.u = undefined; this.f = () => 1; this.d = new Date(0); } }
log(JSON.stringify(new Outer()));
log(JSON.stringify([new Plain(3)], (k, v) => v instanceof Plain ? 'P' + v.n : v));`,

  'classes: instances escaping to host and aliasing': `
class Box { constructor(v) { this.v = v; } get() { return this.v; } }
const b = new Box({ deep: [1, 2] });
const copy = Object.assign({}, b), spread = { ...b };
log(copy, spread, copy instanceof Box, spread.v === b.v);
b.v.deep.push(3); log([b][0].get().deep, copy.v.deep.length);
const frozen = Object.freeze(new Box(1));
try { frozen.v = 2; } catch (e) { logError(e); }
log(frozen.v, Object.isFrozen(frozen));
const sealed = Object.seal(new Box(1)); sealed.v = 9;
try { sealed.w = 1; } catch (e) { logError(e); }
log(sealed.v, sealed.w);
class Holder { constructor(arr, obj) { this.arr = arr; this.obj = obj; } add(x) { this.arr.push(x); this.obj.n++; return this; } }
const arr = [1, 2], obj = { n: 0 };
const h = new Holder(arr, obj);
arr.push(3); obj.n += 10;
h.add(4).add(5);
log(arr, obj, h.arr === arr, h.obj === obj, arr.length, h.arr[4]);
arr.length = 1; log(h.arr, h.arr.length);
const h2 = new Holder([], {});
h2.arr[5] = 'x'; log(h2.arr.length, 4 in h2.arr, h2.arr);
const p = new Box(1);
const lit = { __proto__: p, own: 2 };
lit.v = 5; log(lit.get(), p.v, lit instanceof Box, Object.keys(lit));`,

  'classes: class expressions and name inference': `
const A = class {};
const B = class Inner { static self() { return Inner; } name2() { return Inner.name; } };
const factory = (base) => class extends base { ext() { return 'ext'; } };
const C = factory(B);
log(A.name, B.name, B.self() === B, new B().name2(), C.name, new C().ext(), new C().name2());
log(typeof Inner, (class {}).name, (class Z {}).name);
const obj = { K: class {} }; log(obj.K.name);
let D; D = class {}; log(D.name);
const { C1 = class {} } = {};
let C2; C2 ??= class {};
const [C3 = class {}] = [];
function g(C4 = class {}) { return C4.name; }
const o = {}; o.C5 = class {};
log(C1.name, C2.name, C3.name, g(), JSON.stringify(o.C5.name));
const withStaticName = class { static name = 'S'; };
log(withStaticName.name);
const inst = new class { constructor() { this.x = 1; } get y() { return this.x + 1; } }();
log(inst.x, inst.y, JSON.stringify(inst.constructor.name), new (class { z = 3; })().z);`,

  'classes: class bindings, inner name and TDZ': `
class A { static tag = 'orig'; static who() { return A.tag; } m() { return A === Orig; } static rename() { try { A = 1; } catch (e) { return e.name; } } }
const Orig = A;
log(A.rename());
A = { tag: 'replaced' };
log(Orig.who(), new Orig().m(), A.tag);
let B = class Self { reassign() { try { Self = 2; return 'no'; } catch (e) { return e.name; } } };
log(new B().reassign());
try { new Later(); } catch (e) { logError(e); }
class Later {}
try { class Selfish extends Selfish {} } catch (e) { logError(e); }
try { class K { [K.name]() {} } } catch (e) { logError(e); }
function early() { return new Dd().v; }
try { early(); } catch (e) { logError(e); }
class Dd { v = 'd'; }
log(early());
for (let i = 0; i < 2; i++) {
  const get = () => Loop;
  try { get(); } catch (e) { logError(e); }
  class Loop { static i = i; }
  log(get().i);
}
const y = 'outer';
class F { x = y; z = typeof q; constructor(y, q) { this.p = y; } }
const f = new F('param', 1);
log(f.x, f.z, f.p);`,

  'classes: new.target and bound classes': `
class A { constructor() { this.t = new.target.name; this.isA = new.target === A; } }
class B extends A {}
log(new A().t, new B().t, new A().isA, new B().isA);
function F() { return new.target === undefined ? 'call' : 'new:' + new.target.name; }
log(F(), new F() instanceof F);
class Abstract { constructor() { if (new.target === Abstract) throw new TypeError('abstract'); this.ok = true; } }
class Concrete extends Abstract {}
try { new Abstract(); } catch (e) { logError(e); }
log(new Concrete().ok);
const r = Reflect.construct(A, [], B); log(r.t, r instanceof B);
class P { constructor(x, y) { this.v = [x, y]; this.t = new.target === P; } }
const BP = P.bind(null, 1);
const bp = new BP(2);
log(bp.v, bp.t, bp instanceof P, bp instanceof BP, BP.name, typeof BP.prototype);
try { BP(3); } catch (e) { logError(e); }
try { class C extends BP {} } catch (e) { logError(e); }`,

  'classes: constructors returning objects and return override': `
class A { constructor() { this.a = 1; return { replaced: true }; } }
class B { constructor() { this.b = 1; return 42; } }
class C extends A { constructor() { super(); this.c = 2; } }
const a = new A(), b = new B(), c = new C();
log(a, a instanceof A, b, b instanceof B, c, c instanceof C);
class D extends Object { constructor() { return; } }
try { new D(); } catch (e) { logError(e); }
class E extends Object { constructor() { super(); return undefined; } }
log(new E() instanceof E);
class F extends Object { constructor() { super(); return 1; } }
try { new F(); } catch (e) { logError(e); }
class Stamper { constructor(o) { return o; } }
class Tag extends Stamper { #tag = 'tagged'; pub = 'p'; static has(o) { return #tag in o; } static get(o) { return o.#tag; } }
const lit = { a: 1 };
const r = new Tag(lit);
log(r === lit, Tag.has(lit), Tag.get(lit), Tag.has({}), lit.pub, Object.keys(lit), r instanceof Tag);
const arr = [1, 2];
new Tag(arr); log(Tag.has(arr), arr.pub, arr.length, Object.keys(arr));
const frozen = Object.freeze({ f: 1 });
try { new Tag(frozen); } catch (e) { logError(e); }
log(Tag.has(frozen));
class PrivOnly extends Stamper { #p = 1; static has(o) { return #p in o; } }
const fr2 = Object.freeze({}); new PrivOnly(fr2); log(PrivOnly.has(fr2));
try { new PrivOnly(fr2); } catch (e) { logError(e); }`,

  'classes: derived constructor this before and after super': `
class B { constructor() { this.b = 1; } m() { return 'B.m'; } }
class D1 extends B { constructor() { try { this.x = 1; } catch (e) { logError(e); } super(); log('ok', this.b); } }
new D1();
class D2 extends B { constructor() { super(); try { super(); } catch (e) { logError(e); } } }
new D2();
class D3 extends B { constructor() { const init = () => super(); init(); this.y = 2; } }
log(new D3());
class D4 extends B { constructor() { } }
try { new D4(); } catch (e) { logError(e); }
const reg = [];
class D5 extends B {
  #p = 'priv';
  f = () => this;
  g = super.m();
  constructor() {
    const early = () => this;
    try { early(); } catch (e) { logError(e); }
    super();
    log(early() === this, this.f() === this, this.#p, #p in this, this.g, typeof this, this instanceof D5);
    this.#p = 'changed';
    const arrowSuper = () => super.m();
    log(this.#p, arrowSuper(), Object.keys(this));
    reg.push(this);
  }
}
const d5 = new D5();
log(reg[0] === d5, d5.f() === d5);
class Args { constructor(...args) { this.args = args; } }
class Dflt extends Args {}
class Spread extends Args { constructor(...xs) { super(...xs, 'extra'); } }
log(new Dflt(1, 2).args, new Spread(3).args, Dflt.length, Spread.length);
function Old(x) { this.x = x; }
Old.prototype.hi = function () { return 'hi ' + this.x; };
class New extends Old { constructor() { super(5); } }
log(new New().hi(), new New() instanceof Old);
function Bad() { Args.call(this); }
try { new Bad(); } catch (e) { logError(e); }`,

  'classes: extending Array': `
class Stack extends Array {
  peek() { return this[this.length - 1]; }
  sum() { let s = 0; for (const x of this) s += x; return s; }
}
const s = new Stack();
s.push(1, 2, 3);
log(s.length, s.peek(), s.sum(), s instanceof Stack, Array.isArray(s));
const m = s.map(x => x * 2);
log(m instanceof Stack, m.sum(), m.length, s.filter(x => x > 1).peek());
s.length = 1; log(s.length, s[1], Stack.of(7, 8).peek(), Stack.from('ab').peek());
log(s.constructor.name, JSON.stringify(s.concat([9])));
class Plain extends Array { static get [Symbol.species]() { return Array; } extra() { return 'x'; } }
const p = Plain.from([1, 2, 3]), mapped = p.map(x => x);
log(p instanceof Plain, mapped instanceof Plain, Array.isArray(mapped), typeof mapped.extra, p.extra());`,

  'classes: extending Map, Set and Error': `
class DefaultMap extends Map {
  constructor(def, entries) { super(entries); this.def = def; }
  get(k) { return this.has(k) ? super.get(k) : this.def(k); }
}
const dm = new DefaultMap(k => k + '?', [['a', 1]]);
log(dm.get('a'), dm.get('b'), dm.size, dm instanceof Map, [...dm.keys()]);
class CountingSet extends Set { add(v) { this.adds = (this.adds || 0) + 1; return super.add(v); } }
const cs = new CountingSet([1, 2, 2]);
cs.add(3);
log(cs.size, cs.adds, [...cs]);
class AppError extends Error {
  constructor(msg, code) { super(msg); this.name = 'AppError'; this.code = code; }
  describe() { return this.name + '#' + this.code + ': ' + this.message; }
}
class NotFound extends AppError { constructor(what) { super(what + ' not found', 404); this.name = 'NotFound'; } }
try { throw new NotFound('page'); } catch (e) {
  log(e instanceof NotFound, e instanceof AppError, e instanceof Error, e.describe(), String(e), e.code);
  log(Object.keys(e), typeof e.stack);
}
class PlainErr extends Error {}
const p = new PlainErr('x'); log(p.name, p.message, String(p), p instanceof PlainErr);
class WithCause extends Error { constructor(m, c) { super(m, { cause: c }); } }
log(new WithCause('m', 'why').cause);`,

  'classes: extending Promise, typed arrays, EventTarget and HTMLElement': `
class Bytes extends Uint8Array { sum() { let s = 0; for (let i = 0; i < this.length; i++) s += this[i]; return s; } }
const b = new Bytes(4);
b[0] = 255; b[1] = 256; b[2] = -1; b[3] = 1.9;
log(b.sum(), b instanceof Uint8Array, b.subarray(1) instanceof Bytes, Array.from(b));
class D extends Date { year() { return this.getUTCFullYear(); } }
log(new D(0).year(), new D(86400000 * 366).toISOString());
class Emitter extends EventTarget {
  #count = 0;
  fire(type) { this.dispatchEvent(new CustomEvent(type, { detail: ++this.#count })); return this; }
}
const em = new Emitter(), got = [];
em.addEventListener('x', e => got.push(e.type + e.detail + (e.target === em)));
em.addEventListener('y', function (e) { got.push(this === em); });
em.fire('x').fire('y').fire('x');
log(got);
class MyEl extends HTMLElement {
  static observedAttributes = ['label'];
  #renders = 0;
  connectedCallback() { this.render(); }
  attributeChangedCallback(name, oldV, newV) { this.last = name + ':' + oldV + '->' + newV; if (this.isConnected) this.render(); }
  render() { this.textContent = 'el ' + (this.getAttribute('label') || '') + ' #' + (++this.#renders); }
}
customElements.define('my-el', MyEl);
const el = document.createElement('my-el');
document.getElementById('app').appendChild(el);
el.setAttribute('label', 'L');
log(el.textContent, el.last, el instanceof MyEl, document.getElementById('app').innerHTML);
class MyP extends Promise { tag() { return 'myp'; } }
const q = MyP.resolve(5).then(x => x * 2);
log(q instanceof MyP, q.tag());
window.__done = Promise.all([q, new MyP(res => res('ctor')), MyP.all([1, MyP.resolve(2)])]).then(v => log(v));`,

  'classes: prototype mutation after instances exist': `
class P { constructor(x) { this.x = x; } }
const a = new P(2), b = new P(3);
P.prototype.double = function () { return this.x * 2; };
log(a.double(), b.double());
Object.defineProperty(P.prototype, 'sq', { get() { return this.x * this.x; }, configurable: true });
log(a.sq, b.sq);
P.prototype.double = function () { return 'replaced ' + this.x; };
log(a.double());
delete P.prototype.double;
log(typeof a.double, 'double' in a);
a.double = () => 'own'; log(a.double(), typeof b.double);
Object.setPrototypeOf(b, { sq: 'swapped' }); log(b.sq, b instanceof P);
class Acc { constructor() { this.t = 0; } step(i) { this.t += i; } }
const acc = new Acc();
for (let i = 0; i < 1000; i++) {
  if (i === 500) Acc.prototype.step = function (i) { this.t -= i; };
  acc.step(i);
}
log(acc.t);
class W {}
const w = new W(); w.v = 1;
Object.defineProperty(W.prototype, 'w', { set(x) { this._w = x * 10; }, get() { return this._w; }, configurable: true });
w.w = 2;
const wl = Object.create(W.prototype); wl.w = 3;
log(w.w, Object.keys(w), wl.w, Object.keys(wl));
class G { constructor() { this._v = 1; } }
Object.defineProperty(G.prototype, 'v', { get() { return this._v * 10; }, set(x) { this._v = x; }, configurable: true });
class H extends G { get v() { return 'H:' + super.v; } set v(x) { super.v = x + 1; } }
const hh = new H(); log(hh.v); hh.v = 4; log(hh.v, hh._v, Object.keys(hh));`,

  'classes: Vec3 hot loop with field type changes': `
class Vec3 {
  constructor(x, y, z) { this.x = x; this.y = y; this.z = z; }
  add(o) { return new Vec3(this.x + o.x, this.y + o.y, this.z + o.z); }
  scale(k) { this.x *= k; this.y *= k; this.z *= k; return this; }
  dot(o) { return this.x * o.x + this.y * o.y + this.z * o.z; }
  get len() { return Math.sqrt(this.dot(this)); }
}
let acc = new Vec3(0, 0, 0);
const step = new Vec3(1, 0.5, -0.25);
for (let i = 0; i < 2000; i++) { acc = acc.add(step); if (i % 500 === 0) acc.scale(0.5); }
log(acc.x, acc.y, acc.z, acc.len);
const big = new Vec3(2 ** 30 - 1, -(2 ** 30), 1e300);
const b2 = big.add(new Vec3(1, -1, 1e300));
log(b2.x, b2.y, b2.z, big.dot(new Vec3(0, 0, 0)));
const z = new Vec3(0, -0, 0).scale(-1);
log(z.x, z.y, z.z, Object.is(z.x, -0));
const s = new Vec3('1', 2, 3);
log(s.add(new Vec3(1, 1, 1)).x, s.dot(new Vec3(2, 2, 2)), new Vec3(NaN, 0, 0).len);
class Rect {
  constructor(w, h) { this.w = w; this.h = h; }
  get area() { return this.w * this.h; }
  set area(a) { const r = Math.sqrt(a / this.area); this.w *= r; this.h *= r; }
}
const r = new Rect(3, 4);
let sum = 0;
for (let i = 0; i < 5000; i++) { sum += r.area; r.w += 0.001; }
log(Math.round(sum), r.w.toFixed(3));
r.area = 100; log(Math.round(r.area), r.w.toFixed(4), r.h.toFixed(4));`,

  'classes: field changes type across iterations': `
class Cell { constructor() { this.v = 0; } }
const c = new Cell();
const seen = [];
for (let i = 0; i < 8; i++) {
  if (i === 2) c.v = c.v + 0.5;
  else if (i === 4) c.v = 'S' + c.v;
  else if (i === 6) c.v = null;
  else c.v = c.v + 1;
  seen.push(typeof c.v + ':' + c.v);
}
log(seen.join(' '));
c.v = 1073741823; c.v++; log(c.v, c.v + 1);
c.v = -1073741824; c.v--; log(c.v);
c.v = 2 ** 31; c.v |= 0; log(c.v);
class P { constructor() { this.x = 0; this.y = 0.5; this.n = 1073741822; } }
function run(p, steps) { let acc = 0; for (let i = 0; i < steps; i++) { p.x += 1; p.y *= -1; p.n++; acc += i * 0.25; } return acc; }
const p = new P();
log(run(p, 5), p.x, p.y, p.n, run(p, 0));
const q = new P(); q.y = -0; for (let i = 0; i < 3; i++) q.y *= -1; log(Object.is(q.y, 0), Object.is(q.y, -0));`,

  'classes: typed numbers flowing into instances and closures': `
function negZero(k) { return -k * 0; }
function edge(i) { const vals = [0, 1073741823, 1073741824, -1073741824, -1073741825, 2147483647, 2147483648, -2147483649, 0.5, 1e21, 2 ** 53 + 2]; let v = 0; for (let j = 0; j < vals.length; j++) if (j === i) v = vals[j] * 1; return v; }
class Box { constructor(v) { this.v = v; this.t = typeof v; } inc() { this.v++; return this; } }
const b = new Box(negZero(1));
log(Object.is(b.v, -0), 1 / b.v, b.t);
const out = [];
for (let i = 0; i < 11; i++) { const x = new Box(edge(i)); out.push(x.v, x.inc().v); }
log(out.join(' '));
function build(k) {
  let n = k * 1;
  const C = class {
    get() { return n; }
    static bump() { n += 0.5; return n; }
    f = n * 2;
    static s = n + 1;
  };
  n = n + 10;
  const c = new C();
  return [c.get(), C.bump(), c.f, C.s, c.get()];
}
log(build(1), build(2.25));
let total = 0;
class Adder { add(v) { total += v * 1; } neg() { total = -total; } }
const ad = new Adder();
for (let i = 0; i < 10; i++) ad.add(0.1);
log(total);
total = 0; ad.neg(); log(Object.is(total, -0));
for (let i = 0; i < 3; i++) { class Q { static { total += i * 1.5; } } }
log(total);`,

  'classes: Object.keys and for-in order on instances': `
const sym = Symbol('s');
class K {
  b = 1;
  2 = 'two';
  [sym] = 'symbol';
  a = 0;
  constructor() { this[1] = 'one'; this.c = 3; this.b = 'b2'; delete this.a; this.a = 'again'; }
}
const k = new K();
log(Object.keys(k), Object.getOwnPropertyNames(k), Object.getOwnPropertySymbols(k).length, k[sym]);
log(Object.entries(k), JSON.stringify(k));
for (const key in k) log('in', key);
class A { m() {} constructor() { this.own = 1; } }
A.prototype.extra = 'proto';
const a = new A();
const keys = []; for (const key in a) keys.push(key); log(keys);
log(Object.keys(a), a.propertyIsEnumerable('own'), A.prototype.propertyIsEnumerable('m'));
class S { static s() {} static f = 1; m() {} get g() { return 1; } static { this.dyn = 2; } }
log(Object.getOwnPropertyNames(S), Object.getOwnPropertyNames(S.prototype));`,

  'classes: computed keys, symbols and evaluation order': `
const name = 'dyn';
let i = 0;
class C {
  [name + (++i)]() { return 'd1'; }
  [name + (++i)]() { return 'd2'; }
  static [name]() { return 'static'; }
  get [Symbol.toStringTag]() { return 'Cee'; }
  *[Symbol.iterator]() { yield 1; yield 2; }
  ['a' + 'b'] = i;
}
const c = new C();
log(c.dyn1(), c.dyn2(), C.dyn(), Object.prototype.toString.call(c), [...c], c.ab, i);
log(Object.getOwnPropertyNames(C.prototype));
const order = [];
const k = (n) => (order.push('key ' + n), n);
const v = (n) => (order.push('init ' + n), n);
class A {
  [k('a')] = v('a');
  static [k('s')] = v('s');
  [k('m')]() {}
  static { order.push('block'); }
  [k('b')] = v('b');
}
order.push('defined');
new A();
log(order.join(', '));
let key = 'first';
class B { [key] = 1; }
key = 'second';
log(Object.keys(new B()));
let coerced = 0;
const keyObj = { toString() { coerced++; return 'kk'; } };
class KO { [keyObj] = 1; static [keyObj]() { return 's'; } }
new KO(); new KO();
log(coerced, Object.keys(new KO()), KO.kk());`,

  'classes: generator and async methods with this and super': `
class Q {
  constructor(items) { this.items = items; }
  *[Symbol.iterator]() { for (const x of this.items) yield x * 10; }
  *pairs() { let i = 0; for (const x of this.items) yield [i++, x]; }
  async total() { let t = 0; for (const x of this.items) t += await Promise.resolve(x); return t; }
  static async make(n) { await null; return new Q(Array.from({ length: n }, (_, i) => i)); }
}
class A { base() { return 'A'; } *[Symbol.iterator]() { yield 'a1'; yield 'a2'; } }
class B extends A {
  constructor() { super(); this.v = 'B'; }
  async am(x) { const before = this.v; await null; const s = super.base(); await null; return [before, this.v, s, x, arguments.length]; }
  *gm() { const x = yield this.v; yield super.base() + x; yield* [this.v + '!']; }
  *[Symbol.iterator]() { yield* super[Symbol.iterator](); yield super['base']() + '/' + this.v; }
  async *agm() { yield this.v; await null; yield super.base(); }
  arrow() { return (async () => { await null; return super.base() + '+' + this.v; })(); }
}
const q = new Q([1, 2, 3]);
log([...q], [...q.pairs()]);
const b = new B();
const g = b.gm();
log(g.next().value, g.next('X').value, g.next().value, g.next().done, [...b]);
window.__done = (async () => {
  log(await q.total());
  const q2 = await Q.make(4);
  log(q2.items, await q2.total());
  log(await b.am(1, 2), await b.arrow());
  const out = []; for await (const v of b.agm()) out.push(v); log(out);
})();`,

  'classes: yield and await inside class definitions': `
function* gen() {
  const C = class { [yield 'first']() { return 'm1'; } static [yield 'second'] = 5; get [yield 'third']() { return 'g'; } };
  return C;
}
const it = gen();
log(it.next().value, it.next('alpha').value, it.next('beta').value);
const r = it.next('gam');
log(r.done, new r.value().alpha(), r.value.beta, new r.value().gam, Object.getOwnPropertyNames(r.value.prototype));
class Base { hi() { return 'base hi'; } }
function* heritage() { class X extends (yield 'need base') { hi() { return 'X>' + super.hi(); } } yield new X().hi(); }
const h = heritage();
log(h.next().value, h.next(Base).value, h.next().done);
function* privGen() {
  class P { #x = 'p'; static read(o) { return o.#x; } }
  const p = new P();
  yield P.read(p);
  class Q { #x = 'q'; static read(o) { return o.#x; } }
  yield Q.read(new Q()) + P.read(p);
  try { Q.read(p); } catch (e) { yield e.name; }
}
log([...privGen()]);
async function make() {
  let n = 1;
  const C = class extends (await Promise.resolve(Base)) { [await Promise.resolve('dyn')]() { return 'D' + super.hi() + n; } static [await Promise.resolve('s')] = 7; };
  n = 2;
  await null;
  return C;
}
window.__done = make().then(C => log(new C().dyn(), C.s, Object.getPrototypeOf(C).name));`,

  'classes: instanceof, calling and typeof checks': `
class Even { static [Symbol.hasInstance](n) { return n % 2 === 0; } }
log(2 instanceof Even, 3 instanceof Even);
class A { constructor(x) { this.x = x; } }
const a = new A();
log(a instanceof A);
A.prototype.constructor = null;
log(a instanceof A, a.constructor);
const b = Object.create(A.prototype);
log(b instanceof A, Object.getPrototypeOf(b) === A.prototype);
try { log(a instanceof {}); } catch (e) { logError(e); }
try { A(1); } catch (e) { logError(e); }
try { A.call({}, 1); } catch (e) { logError(e); }
log(typeof A, typeof A.prototype, A.length);
const desc = Object.getOwnPropertyDescriptor(A, 'prototype');
log(desc.writable, desc.enumerable, desc.configurable);
try { new (class extends 5 {})(); } catch (e) { logError(e); }
class N extends null { static s() { return 's'; } }
log(N.s(), Object.getPrototypeOf(N.prototype));
try { new N(); } catch (e) { logError(e); }
log(typeof window.A, Object.hasOwn(globalThis, 'A'));`,

  'classes: class instances as iterators': `
const order = [];
class Res { constructor(v, d) { this._v = v; this._d = d; } get value() { order.push('value'); return this._v; } get done() { order.push('done'); return this._d; } }
class Range {
  constructor(n) { this.n = n; this.i = 0; this.closed = 0; }
  [Symbol.iterator]() { order.push('iter'); return this; }
  get next() { order.push('get next'); return () => { order.push('next'); return new Res(this.i, this.i++ >= this.n); }; }
  return() { this.closed++; order.push('return'); return {}; }
}
for (const x of new Range(2)) order.push('body ' + x);
log(order.join(',')); order.length = 0;
const r = new Range(5); for (const x of r) { if (x === 1) break; } log(r.closed, r.i, order.join(',')); order.length = 0;
const r2 = new Range(5); const [a, b] = r2; log(a, b, r2.closed, order.join(',')); order.length = 0;
const r3 = new Range(3); log([...r3], Math.max(...new Range(4)), r3.closed); order.length = 0;
const r4 = new Range(5); try { for (const x of r4) throw new Error('x'); } catch (e) { log(e.message, r4.closed); }
const r5 = new Range(5); function* d() { yield* r5; } for (const x of d()) { if (x === 0) break; } log(r5.closed);
class Gen { constructor(vals) { this.vals = vals; } *[Symbol.iterator]() { for (const v of this.vals) yield v; } }
const [p = 'dp', , q = 'dq', ...rest] = new Gen([undefined, 1, null, 3, 4]);
log(p, q, rest);`,

  'classes: member assignment targets on this': `
class S {
  #p = 0; a = 1; b = 2;
  swap() { [this.a, this.b] = [this.b, this.a]; return [this.a, this.b]; }
  take(o) { ({ x: this.x, y: this.#p = 'dflt', ...this.rest } = o); return [this.x, this.#p, this.rest]; }
  loop(arr) { const seen = []; for (this.cur of arr) seen.push(this.cur * 2); for (this.#p of arr) seen.push(this.#p); for (this.key in { k1: 1, k2: 2 }) seen.push(this.key); return [seen, this.cur, this.#p, this.key]; }
  destructThis() { const { a, b, missing = 'm' } = this; return [a, b, missing]; }
}
const s = new S();
log(s.swap(), s.take({ x: 'X', z: 1, w: 2 }), s.take({ x: 1, y: 'Y' }), s.loop([3, 4]), s.destructThis());
class K {
  get() { return 'get'; } set(v) { return 'set' + v; } static() { return 'static'; } async() { return 'async'; }
  static static() { return 'S.static'; } 'quoted key'() { return 'q'; } 123() { return 'num'; }
  of() { return 'of'; } let() { return 'let'; } yield() { return 'yield'; } new() { return 'new'; }
}
const k = new K();
log(k.get(), k.set(1), k.static(), k.async(), K.static(), k['quoted key'](), k[123](), k.of(), k.let(), k.yield(), k.new());`,

  'classes: tree of instances with shared static state': `
let created = 0;
class Node2 {
  static all = [];
  constructor(val, parent = null) {
    this.val = val; this.parent = parent; this.children = [];
    if (parent) parent.children.push(this);
    Node2.all.push(this); created++;
  }
  path() { return this.parent ? this.parent.path() + '/' + this.val : String(this.val); }
  depth() { let d = 0, n = this; while (n.parent) { d++; n = n.parent; } return d; }
  sum() { return this.val + this.children.reduce((s, c) => s + c.sum(), 0); }
}
const root = new Node2(1); const c1 = new Node2(2, root); const c2 = new Node2(3, c1); new Node2(4, root);
log(c2.path(), c2.depth(), root.sum(), created, Node2.all.length, root.children.map(c => c.val));
log(Node2.all.indexOf(c2), Node2.all[1] === c1, c2.parent.parent === root);`,

  // ---------- findings ----------
  'classes: symbol-keyed method names': `
const anon = Symbol(), empty = Symbol(''), desc = Symbol('d');
class A { [anon]() {} [empty]() {} [desc]() {} get [anon]() { return 1; } static [desc]() {} }
log(JSON.stringify(Object.getOwnPropertyDescriptor(A.prototype, anon).get.name));
log(JSON.stringify(A.prototype[empty].name), JSON.stringify(A.prototype[desc].name), A[desc].name);
class B { [anon]() {} }
log(JSON.stringify(B.prototype[anon].name));
const o = { [anon]() {}, [empty]() {}, [desc]: function () {} };
log(JSON.stringify(o[anon].name), JSON.stringify(o[empty].name), JSON.stringify(o[desc].name));`,

  'classes: anonymous functions in field initializers get the field name': `
const sym = Symbol('sy');
class A {
  x = () => {};
  y = function () {};
  static z = () => {};
  static K = class {};
  ['c' + 1] = () => {};
  [sym] = function () {};
  #p = () => {};
  pn() { return this.#p.name; }
  w = function named() {};
}
const a = new A();
log(a.x.name, a.y.name, A.z.name, A.K.name, a.c1.name, a[sym].name, a.pn(), a.w.name);`,

  'classes: Math intrinsics do not coerce extra arguments': `
const order = [];
class V { constructor(n) { this.n = n; } valueOf() { order.push('valueOf ' + this.n); return this.n; } }
const mk = n => (order.push('make ' + n), new V(n));
log(Math.abs(mk(-1), mk(2)), order.join()); order.length = 0;
log(Math.max(mk(1), mk(3), mk(2)), order.join()); order.length = 0;
log(Math.round(mk(1.5), mk(9)), Math.sqrt(mk(4), mk(8)), order.join()); order.length = 0;
log(Math.pow(mk(2), mk(3), mk(4)), Math.imul(mk(3), mk(4), mk(5)), order.join()); order.length = 0;
log(Math.floor(mk(2.5), mk(6)), Math.sign(mk(-3), mk(7)), order.join());`,

  'classes: Function.prototype.toString of classes and methods': `
class Widget { render() { return 1; } static make() { return new Widget(); } }
const isClass = f => /^class\\b/.test(String(f));
function plain() {}
log(isClass(Widget), isClass(plain), isClass(class {}));
log(String(Widget.prototype.render), String(Widget.make));`,
};

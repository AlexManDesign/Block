// Own WASM-GC runtime for lexical environments. No C/C++ toolchain or engine.
// Structures contain links and externref values, including our own ordinary
// objects and arrays. The browser collector traces all these references.
const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const F64=require("./wasm.mjs")["F64"];
const u32=require("./wasm.mjs")["u32"];
const i32=require("./wasm.mjs")["i32"];
const utf8=require("./wasm.mjs")["utf8"];
const section=require("./wasm.mjs")["section"];
const STRING=require("./string-wasm.mjs")["STRING"];
const STRING_REF=require("./string-wasm.mjs")["STRING_REF"];
const stringTypes=require("./string-wasm.mjs")["stringTypes"];
const addStringRuntime=require("./string-wasm.mjs")["addStringRuntime"];
const OBJECT=require("./object-wasm.mjs")["OBJECT"];
const OBJECT_REF=require("./object-wasm.mjs")["OBJECT_REF"];
const objectTypes=require("./object-wasm.mjs")["objectTypes"];
const addObjectRuntime=require("./object-wasm.mjs")["addObjectRuntime"];

const NUMBER=3,ATOM=4,POOL=5,TOKEN=6,SCOPE=14;
let cached;

class Emitter {
 constructor(module,params){this.module=module;this.params=params;this.code=[];this.locals=[];this.labels=[];}
 out(...b){this.code.push(...b);}
 local(type=REF){const id=this.params.length+this.locals.length;this.locals.push(type);return id;}
 get(id){this.out(0x20,...u32(id));} set(id){this.out(0x21,...u32(id));} tee(id){this.out(0x22,...u32(id));}
 int(n){this.out(0x41,...i32(n));} nil(){this.out(0xd0,REF);} undef(){this.call('undefinedValue');}
 float(n){const a=new Uint8Array(8);new DataView(a.buffer).setFloat64(0,n,true);this.out(0x44,...a);}
 call(name){this.out(0x10,...u32(this.module.ids.get(name)));}
 ret(){this.out(0x0f);} drop(){this.out(0x1a);}
 internal(type){this.out(0xfb,0x1a,0xfb,0x16,...u32(type));}
 field(id,type,index){this.get(id);this.internal(type);this.out(0xfb,2,...u32(type),...u32(index));}
 put(id,type,index,value){this.get(id);this.internal(type);value();this.out(0xfb,5,...u32(type),...u32(index));}
 create(type){this.out(0xfb,0,...u32(type),0xfb,0x1b);}
 test(id,type){this.get(id);this.out(0xfb,0x1a,0xfb,0x14,...u32(type));}
 nonnull(id){this.get(id);this.out(0xd1,0x45);}
 if(yes,no=null,result=0x40){this.out(0x04,result);this.labels.push({});yes();if(no){this.out(0x05);no();}this.labels.pop();this.out(0x0b);}
 block(kind,body){this.out(kind==='loop'?3:2,0x40);const label={};this.labels.push(label);body(label);this.labels.pop();this.out(0x0b);}
 branch(label,conditional=false){this.out(conditional?0x0d:0x0c,...u32(this.labels.length-1-this.labels.indexOf(label)));}
 fail(code,name){this.int(code);if(name===undefined)this.int(-1);else this.get(name);this.call('fail');this.out(0);}
}

function environmentWasm(){
 if(cached)return cached;
 const types=[
  // 0..2 are retired environment structs, kept so type indices stay stable.
  [0x5f,5,REF,1,REF,1,I32,1,I32,1,REF,1],
  [0x5f,6,REF,1,I32,1,I32,1,REF,1,REF,1,I32,1],
  [0x5f,2,REF,1,I32,1],
  [0x5f,2,F64,0,0x6d,0],
  [0x5f,2,I32,0,0x6d,0],
  [0x5e,REF,1],
  [0x5f,0],
  ...stringTypes,
  ...objectTypes,
  [0x5e,REF,1],                                   // scope record
 ],typeKeys=new Map(),imports=[],functions=[],ids=new Map();
 const type=(p,r)=>{const key=JSON.stringify([p,r]);if(!typeKeys.has(key)){typeKeys.set(key,types.length);types.push([0x60,...u32(p.length),...p,...u32(r.length),...r]);}return typeKeys.get(key);};
 const host=(name,p,r)=>{ids.set(name,imports.length);imports.push({name,type:type(p,r)});};
 host('hostRead',[REF],[REF]);host('hostWrite',[REF,REF],[REF]);host('fail',[I32,I32],[]);
 host('hostTruth',[REF],[I32]);host('hostUpdate',[REF,REF,I32,I32],[REF]);
 host('hostProperty',[REF,REF,I32],[REF]);host('hostStringRead',[REF,REF],[REF]);host('hostStringWrite',[REF,REF,REF,I32],[REF]);
 host('hostEqual',[REF,REF],[I32]);host('hostTemplateString',[REF],[REF]);
 host('hostPrototypeFlags',[],[I32]);host('hostKey',[REF],[REF]);
 host('hostObjectGet',[REF,REF],[REF]);host('hostObjectSet',[REF,REF,REF,I32],[REF]);
 host('hostObjectDefine',[REF,REF,REF,I32],[REF]);host('hostObjectDelete',[REF,REF,I32],[I32]);
 host('hostArrayPush',[REF,REF],[REF]);host('hostArrayHole',[REF],[REF]);host('hostArg',[REF,I32],[REF]);
 host('hostHas',[REF,REF],[I32]);host('hostRemove',[REF],[REF]);
 host('hostToNumeric',[REF],[REF]);host('hostIncrement',[REF,I32],[REF]);
 const def=(name,p,r,body)=>{ids.set(name,imports.length+functions.length);functions.push({name,params:p,type:type(p,r),body});};

 // GC globals: imported host undefined at 0 (ABI only), pool at 1,
 // internal undefined/null/false/true singletons at 2..5, private brand at 6.
 def('initValues',[I32],[],f=>{
  f.out(0xfb,0,...u32(TOKEN),0x24,6);
  for(let kind=0;kind<4;kind++){f.int(kind);f.out(0x23,6);f.create(ATOM);f.out(0x24,...u32(kind+2));}
  f.get(0);f.out(0xfb,7,...u32(POOL),0xfb,0x1b,0x24,1);
 });
 def('setConstant',[I32,REF],[],f=>{f.out(0x23,1);f.internal(POOL);f.get(0);f.get(1);f.out(0xfb,0x0e,...u32(POOL));});
 def('lit',[I32],[REF],f=>{f.out(0x23,1);f.internal(POOL);f.get(0);f.out(0xfb,0x0b,...u32(POOL));});
 def('number',[F64],[REF],f=>{f.get(0);f.out(0x23,6);f.create(NUMBER);});
 def('isNumber',[REF],[I32],f=>{f.test(0,NUMBER);f.if(()=>{f.field(0,NUMBER,1);f.out(0x23,6,0xd3);},()=>f.int(0),I32);});
 def('toNumber',[REF],[F64],f=>f.field(0,NUMBER,0));
 // ECMAScript ToInt32 for doubles. Powers of two keep the remainder exact;
 // doubles with magnitude >= 2^84 are already multiples of 2^32.
 def('int32',[F64],[I32],f=>{
  const n=f.local(F64);f.get(0);f.get(0);f.out(0x61,0x45);f.if(()=>{f.int(0);f.ret();});
  f.get(0);f.out(0x99);f.float(2**84);f.out(0x66);f.if(()=>{f.int(0);f.ret();});
  f.get(0);f.float(-2147483648);f.out(0x66);f.get(0);f.float(2147483648);f.out(0x63,0x71);f.if(()=>{f.get(0);f.out(0xaa);f.ret();});
  f.get(0);f.out(0x9d);f.set(n);f.get(n);f.get(n);f.float(4294967296);f.out(0xa3,0x9c);f.float(4294967296);f.out(0xa2,0xa1,0xab);
 });
 def('atomKind',[REF],[I32],f=>{f.test(0,ATOM);f.if(()=>{f.field(0,ATOM,1);f.out(0x23,6,0xd3);f.if(()=>{f.field(0,ATOM,0);f.ret();});});f.int(-1);});
 def('undefinedValue',[],[REF],f=>f.out(0x23,2));
 def('nullValue',[],[REF],f=>f.out(0x23,3));
 def('boolean',[I32],[REF],f=>{f.get(0);f.if(()=>f.out(0x23,5),()=>f.out(0x23,4),REF);});
 def('truth',[REF],[I32],f=>{
  const value=f.local(F64),kind=f.local(I32);f.get(0);f.call('isNumber');f.if(()=>{
  f.field(0,NUMBER,0);f.tee(value);f.float(0);f.out(0x62);f.get(value);f.get(value);f.out(0x61,0x71);f.ret();
  });f.get(0);f.call('atomKind');f.tee(kind);f.int(0);f.out(0x4e);f.if(()=>{f.get(kind);f.int(3);f.out(0x46);f.ret();});
  f.get(0);f.call('isString');f.if(()=>{f.get(0);f.call('stringLength');f.out(0x45,0x45);f.ret();});f.get(0);f.call('isObject');f.if(()=>{f.int(1);f.ret();});f.get(0);f.call('hostTruth');
 });
 def('nullish',[REF],[I32],f=>{f.get(0);f.call('atomKind');f.int(2);f.out(0x49);});
 def('isUndefined',[REF],[I32],f=>{f.get(0);f.call('atomKind');f.out(0x45);});
 addStringRuntime(def);
 addObjectRuntime(def);
 def('equal',[REF,REF],[I32],f=>{
  f.get(0);f.call('isString');f.if(()=>{f.get(1);f.call('isString');f.if(()=>{f.get(0);f.get(1);f.call('stringCompare');f.out(0x45);},()=>f.int(0),I32);f.ret();});
  f.get(1);f.call('isString');f.if(()=>{f.int(0);f.ret();});
  f.get(0);f.call('isNumber');f.get(1);f.call('isNumber');f.out(0x71);f.if(()=>{f.get(0);f.call('toNumber');f.get(1);f.call('toNumber');f.out(0x61);f.ret();});
  f.get(0);f.call('isObject');f.if(()=>{f.get(1);f.call('isObject');f.if(()=>{f.get(0);f.internal(OBJECT);f.get(1);f.internal(OBJECT);f.out(0xd3);},()=>f.int(0),I32);f.ret();});
  f.get(1);f.call('isObject');f.if(()=>{f.int(0);f.ret();});
  f.get(0);f.get(1);f.call('hostEqual');
 });
 def('templateString',[REF],[REF],f=>{f.get(0);f.call('isString');f.if(()=>f.get(0),()=>{f.get(0);f.call('hostTemplateString');},REF);});
 def('property',[REF,REF,I32],[REF],f=>{f.get(0);f.call('isObject');f.if(()=>{f.get(0);f.get(1);f.get(2);f.int(1);f.out(0x23,6);f.create(OBJECT_REF);f.ret();});f.get(0);f.call('isString');f.if(()=>{f.get(0);f.get(1);f.get(2);f.out(0x23,6);f.create(STRING_REF);},()=>{f.get(0);f.get(1);f.get(2);f.call('hostProperty');},REF);});

 // Scope records: (array (mut externref)); element 0 is the parent record,
 // elements 1..n are the captured bindings. null marks an uninitialized
 // (TDZ) binding. The compiler resolves (hops, slot) statically.
 def('scopeNew',[REF,I32],[REF],f=>{
  const record=f.local();f.get(1);f.int(1);f.out(0x6a,0xfb,7,...u32(SCOPE),0xfb,0x1b);f.tee(record);
  f.internal(SCOPE);f.int(0);f.get(0);f.out(0xfb,0x0e,...u32(SCOPE));f.get(record);
 });
 def('scopeParent',[REF],[REF],f=>{f.get(0);f.internal(SCOPE);f.int(0);f.out(0xfb,0x0b,...u32(SCOPE));});
 def('scopeGet',[REF,I32],[REF],f=>{f.get(0);f.internal(SCOPE);f.get(1);f.out(0xfb,0x0b,...u32(SCOPE));});
 def('scopeSet',[REF,I32,REF],[],f=>{f.get(0);f.internal(SCOPE);f.get(1);f.get(2);f.out(0xfb,0x0e,...u32(SCOPE));});
 def('scopeClone',[REF],[REF],f=>{
  const n=f.local(I32),copy=f.local();f.get(0);f.internal(SCOPE);f.out(0xfb,0x0f);f.tee(n);f.out(0xfb,7,...u32(SCOPE),0xfb,0x1b);f.set(copy);
  f.get(copy);f.internal(SCOPE);f.int(0);f.get(0);f.internal(SCOPE);f.int(0);f.get(n);f.out(0xfb,0x11,...u32(SCOPE),...u32(SCOPE));f.get(copy);
 });
 // Module records by module index (global 8 holds the container array).
 def('initModuleScopes',[I32],[],f=>{f.get(0);f.out(0xfb,7,...u32(SCOPE),0xfb,0x1b,0x24,8);});
 def('setModuleScope',[I32,REF],[],f=>{f.out(0x23,8);f.internal(SCOPE);f.get(0);f.get(1);f.out(0xfb,0x0e,...u32(SCOPE));});
 def('moduleScope',[I32],[REF],f=>{f.out(0x23,8);f.internal(SCOPE);f.get(0);f.out(0xfb,0x0b,...u32(SCOPE));});
 // References are only created for properties now; bindings never are.
 def('read',[REF],[REF],f=>{
  f.get(0);f.call('isStringReference');f.if(()=>{f.field(0,STRING_REF,0);f.field(0,STRING_REF,1);f.call('stringRead');f.ret();});
  f.get(0);f.call('isObjectReference');f.if(()=>{f.field(0,OBJECT_REF,0);f.field(0,OBJECT_REF,1);f.call('objectGet');f.ret();});
  f.get(0);f.call('hostRead');
 });
 def('write',[REF,REF],[REF],f=>{
  f.get(0);f.call('isStringReference');f.if(()=>{f.field(0,STRING_REF,0);f.field(0,STRING_REF,1);f.get(1);f.field(0,STRING_REF,2);f.call('hostStringWrite');f.ret();});
  f.get(0);f.call('isObjectReference');f.if(()=>{f.field(0,OBJECT_REF,0);f.field(0,OBJECT_REF,1);f.get(1);f.field(0,OBJECT_REF,2);f.call('objectSet');f.ret();});
  f.get(0);f.get(1);f.call('hostWrite');
 });
 def('toNumeric',[REF],[REF],f=>{f.get(0);f.call('isNumber');f.if(()=>{f.get(0);f.ret();});f.get(0);f.call('hostToNumeric');});
 def('increment',[REF,I32],[REF],f=>{f.get(0);f.call('isNumber');f.if(()=>{f.get(0);f.call('toNumber');f.get(1);f.out(0xb7,0xa0);f.call('number');f.ret();});f.get(0);f.get(1);f.call('hostIncrement');});
 def('update',[REF,I32,I32],[REF],f=>{
  const old=f.local(),value=f.local();f.get(0);f.call('read');f.set(old);f.get(old);f.call('isNumber');f.if(()=>{
   f.field(old,NUMBER,0);f.get(1);f.out(0xb7,0xa0);f.call('number');f.set(value);
   f.get(0);f.get(value);f.call('write');f.drop();f.get(2);f.if(()=>f.get(old),()=>f.get(value),REF);f.ret();
  });f.get(0);f.get(old);f.get(1);f.get(2);f.call('hostUpdate');
 });

 const module={ids},bodies=functions.map(fn=>{
  const f=new Emitter(module,fn.params);fn.body(f);const groups=[];
  for(const t of f.locals){if(groups.at(-1)?.[1]===t)groups.at(-1)[0]++;else groups.push([1,t]);}
  const bytes=[...u32(groups.length),...groups.flatMap(([n,t])=>[...u32(n),t]),...f.code,0x0b];return [...u32(bytes.length),...bytes];
 });
 const entries=imports.map(fn=>[...utf8('h'),...utf8(fn.name),0,...u32(fn.type)]);
 entries.push([...utf8('h'),...utf8('undefined'),3,REF,0]);
 const exports=functions.map((fn,i)=>[...utf8(fn.name),0,...u32(imports.length+i)]);
 const bytes=[0,97,115,109,1,0,0,0];
 const globals=[...u32(8),...Array.from({length:5},()=>[REF,1,0xd0,REF,0x0b]).flat(),0x6d,1,0xd0,0x6d,0x0b,I32,1,0x41,0,0x0b,REF,1,0xd0,REF,0x0b];
 for(const [id,data]of [[1,[...u32(types.length),...types.flat()]],[2,[...u32(entries.length),...entries.flat()]],[3,[...u32(functions.length),...functions.flatMap(fn=>u32(fn.type))]],[6,globals],[7,[...u32(exports.length),...exports.flat()]],[10,[...u32(bodies.length),...bodies.flat()]]])bytes.push(...section(id,data));
 const binary=new Uint8Array(bytes);
 // Module() provides an offset/function diagnostic if the own encoder is wrong.
 new WebAssembly.Module(binary);cached=binary;return binary;
}

exports["environmentWasm"]=environmentWasm;

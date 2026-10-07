// Own ordinary-object / dense-array heap and property algorithms, emitted as
// WASM-GC instructions. Complex descriptors and host interop use one-way
// promotion: all aliases keep following the same host object after escape.
const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const F64=require("./wasm.mjs")["F64"];
const u32=require("./wasm.mjs")["u32"];
const STRING=require("./string-wasm.mjs")["STRING"];
const OBJECT=11,PROPERTY=12,OBJECT_REF=13;
const objectTypes=[
 [0x5f,7,I32,1,REF,1,REF,1,REF,1,I32,1,REF,1,0x6d,0], // kind, head, tail, dense, length, host, brand
 [0x5f,3,REF,0,REF,1,REF,1],                           // key, value (null = deleted), next
 [0x5f,5,REF,0,REF,0,I32,0,I32,0,0x6d,0],                  // receiver, key, strict, brand
];
const POOL=5,KIND=0,HEAD=1,TAIL=2,DENSE=3,LENGTH=4,HOST=5;
const MAX_DENSE=1048576;
function addObjectRuntime(def){
 const dense=(f,o)=>{f.field(o,OBJECT,DENSE);f.internal(POOL);};
 const array=(f,o)=>{f.field(o,OBJECT,KIND);f.int(1);f.out(0x46);};
 const forwarded=(f,o)=>{f.field(o,OBJECT,HOST);f.out(0xd1,0x45);};
 const textIs=(f,k,text)=>{f.get(k);f.call('isString');f.if(()=>{f.get(k);f.call('stringLength');f.int(text.length);f.out(0x46);for(let i=0;i<text.length;i++){f.get(k);f.int(i);f.call('stringCharCodeAtSafe');f.int(text.charCodeAt(i));f.out(0x46,0x71);}},()=>f.int(0),I32);};
 const host=(f,name,n)=>{for(let i=0;i<n;i++)f.get(i);f.call(name);f.ret();};
 def('isObject',[REF],[I32],f=>{f.test(0,OBJECT);f.if(()=>{f.field(0,OBJECT,6);f.out(0x23,6,0xd3);},()=>f.int(0),I32);});
 def('isObjectReference',[REF],[I32],f=>{f.test(0,OBJECT_REF);f.if(()=>{f.field(0,OBJECT_REF,4);f.out(0x23,6,0xd3);},()=>f.int(0),I32);});
 for(const [name,index,type]of [['objectKind',KIND,I32],['objectHead',HEAD,REF],['objectHost',HOST,REF],['arrayLength',LENGTH,I32]])def(name,[REF],[type],f=>f.field(0,OBJECT,index));
 def('objectSetHost',[REF,REF],[],f=>f.put(0,OBJECT,HOST,()=>f.get(1)));
 for(const [name,index]of [['propertyKey',0],['propertyValue',1],['propertyNext',2]])def(name,[REF],[REF],f=>f.field(0,PROPERTY,index));
 for(const [name,index,type]of [['objectReceiver',0,REF],['objectKey',1,REF],['objectStrict',2,I32]])def(name,[REF],[type],f=>f.field(0,OBJECT_REF,index));
 def('objectNew',[I32],[REF],f=>{f.get(0);f.nil();f.nil();f.int(0);f.out(0xfb,7,...u32(POOL),0xfb,0x1b);f.int(0);f.nil();f.out(0x23,6);f.create(OBJECT);});
 def('object',[],[REF],f=>{f.int(0);f.call('objectNew');});
 def('array',[],[REF],f=>{f.int(1);f.call('objectNew');});
 def('invalidatePrototypes',[],[],f=>{f.int(0);f.out(0x24,7);});
 def('prototypeFlags',[],[I32],f=>{f.out(0x23,7,0x45);f.if(()=>{f.call('hostPrototypeFlags');f.out(0x24,7);});f.out(0x23,7);});
 def('key',[REF],[REF],f=>{f.get(0);f.call('isString');f.get(0);f.call('isNumber');f.out(0x72);f.if(()=>f.get(0),()=>{f.get(0);f.call('hostKey');},REF);});
 def('stringCharCodeAtSafe',[REF,I32],[I32],f=>{f.get(1);f.get(0);f.call('stringLength');f.out(0x49);f.if(()=>{f.get(0);f.get(1);f.call('stringUnit');},()=>f.int(-1),I32);});
 def('isLengthKey',[REF],[I32],f=>textIs(f,0,'length'));
 def('isProtoKey',[REF],[I32],f=>textIs(f,0,'__proto__'));
 // Canonical array indices are 0 .. 2^32-2. -1 is the non-index sentinel.
 def('arrayIndex',[REF],[I32],f=>{
  const n=f.local(F64),i=f.local(I32),len=f.local(I32),digit=f.local(I32);
  f.get(0);f.call('isNumber');f.if(()=>{f.get(0);f.call('toNumber');f.set(n);},()=>{
   f.get(0);f.call('isString');f.out(0x45);f.if(()=>{f.int(-1);f.ret();});
   f.get(0);f.call('stringLength');f.tee(len);f.out(0x45);f.get(len);f.int(10);f.out(0x4b,0x72);f.if(()=>{f.int(-1);f.ret();});
   f.get(len);f.int(1);f.out(0x4b);f.get(0);f.int(0);f.call('stringUnit');f.int(48);f.out(0x46,0x71);f.if(()=>{f.int(-1);f.ret();});
   f.block('end',end=>f.block('loop',loop=>{
    f.get(i);f.get(len);f.out(0x4f);f.branch(end,true);f.get(0);f.get(i);f.call('stringUnit');f.int(48);f.out(0x6b);f.tee(digit);f.int(9);f.out(0x4b);f.if(()=>{f.int(-1);f.ret();});
    f.get(n);f.float(10);f.out(0xa2);f.get(digit);f.out(0xb8,0xa0);f.set(n);f.get(i);f.int(1);f.out(0x6a);f.set(i);f.branch(loop);
   }));
  });
  f.get(n);f.float(0);f.out(0x66);f.get(n);f.float(4294967295);f.out(0x63,0x71);f.get(n);f.get(n);f.out(0x9d,0x61,0x71);f.if(()=>{f.get(n);f.out(0xab);},()=>f.int(-1),I32);
 });
 def('objectFind',[REF,REF],[REF],f=>{
  const node=f.local();f.field(0,OBJECT,HEAD);f.set(node);
  f.block('end',end=>f.block('loop',loop=>{f.get(node);f.out(0xd1);f.branch(end,true);f.field(node,PROPERTY,1);f.out(0xd1,0x45);f.if(()=>{f.field(node,PROPERTY,0);f.internal(STRING);f.get(1);f.internal(STRING);f.out(0xd3);f.if(()=>{f.get(node);f.ret();});f.field(node,PROPERTY,0);f.get(1);f.call('stringCompare');f.out(0x45);f.if(()=>{f.get(node);f.ret();});});f.field(node,PROPERTY,2);f.set(node);f.branch(loop);}));f.nil();
 });
 def('objectStore',[REF,REF,REF],[REF],f=>{
  const node=f.local(),tail=f.local();f.get(0);f.get(1);f.call('objectFind');f.set(node);f.nonnull(node);
  f.if(()=>f.put(node,PROPERTY,1,()=>f.get(2)),()=>{
   f.get(1);f.get(2);f.nil();f.create(PROPERTY);f.set(node);f.field(0,OBJECT,TAIL);f.set(tail);f.nonnull(tail);f.if(()=>f.put(tail,PROPERTY,2,()=>f.get(node)),()=>f.put(0,OBJECT,HEAD,()=>f.get(node)));f.put(0,OBJECT,TAIL,()=>f.get(node));
  });f.get(2);
 });
 def('arrayCapacity',[REF],[I32],f=>{dense(f,0);f.out(0xfb,0x0f);});
 def('arrayOwn',[REF,I32],[REF],f=>{f.get(1);f.get(0);f.call('arrayCapacity');f.out(0x49);f.if(()=>{dense(f,0);f.get(1);f.out(0xfb,0x0b,...u32(POOL));},()=>f.nil(),REF);});
 def('arrayStore',[REF,I32,REF],[REF],f=>{
  const old=f.local(),size=f.local(I32),n=f.local(I32);f.get(0);f.call('arrayCapacity');f.set(size);
  f.get(1);f.get(size);f.out(0x4f);f.if(()=>{
   f.get(1);f.int(1);f.out(0x6a);f.get(size);f.int(2);f.out(0x6c);f.out(0x4b);f.if(()=>{f.get(1);f.int(1);f.out(0x6a);},()=>{f.get(size);f.int(2);f.out(0x6c);},I32);f.set(n);
   f.get(n);f.int(8);f.out(0x49);f.if(()=>{f.int(8);f.set(n);});f.get(n);f.int(MAX_DENSE);f.out(0x4b);f.if(()=>{f.int(MAX_DENSE);f.set(n);});
   f.field(0,OBJECT,DENSE);f.set(old);f.put(0,OBJECT,DENSE,()=>{f.get(n);f.out(0xfb,7,...u32(POOL),0xfb,0x1b);});
   dense(f,0);f.int(0);f.get(old);f.internal(POOL);f.int(0);f.get(size);f.out(0xfb,0x11,...u32(POOL),...u32(POOL));
  });dense(f,0);f.get(1);f.get(2);f.out(0xfb,0x0e,...u32(POOL));
  f.get(1);f.field(0,OBJECT,LENGTH);f.out(0x4f);f.if(()=>f.put(0,OBJECT,LENGTH,()=>{f.get(1);f.int(1);f.out(0x6a);}));f.get(2);
 });
 def('arrayResize',[REF,I32],[],f=>{
  const cap=f.local(I32);f.get(0);f.call('arrayCapacity');f.set(cap);f.get(1);f.get(cap);f.out(0x49);f.if(()=>{dense(f,0);f.get(1);f.nil();f.get(cap);f.get(1);f.out(0x6b,0xfb,0x10,...u32(POOL));});f.put(0,OBJECT,LENGTH,()=>f.get(1));
 });
 // a[i] with an integer index inside a dense, not forwarded array: the element,
 // or null when the generic path must decide (holes, out of range, other keys).
 const denseIndex=(f,i)=>{
  array(f,0);f.out(0x45);f.if(()=>{f.int(-1);f.ret();});forwarded(f,0);f.if(()=>{f.int(-1);f.ret();});
  f.get(1);f.out(0xfc,0x02);f.tee(i);f.out(0xb7);f.get(1);f.out(0x62);f.if(()=>{f.int(-1);f.ret();});
  f.get(i);f.field(0,OBJECT,LENGTH);f.out(0x4f);f.if(()=>{f.int(-1);f.ret();});f.get(i);
 };
 def('denseSlot',[REF,F64],[I32],f=>denseIndex(f,f.local(I32)));
 def('denseGet',[REF,F64],[REF],f=>{const i=f.local(I32);f.get(0);f.get(1);f.call('denseSlot');f.tee(i);f.int(0);f.out(0x48);f.if(()=>{f.nil();f.ret();});f.get(0);f.get(i);f.call('arrayOwn');});
 def('denseSet',[REF,F64,REF],[I32],f=>{
  const i=f.local(I32);f.get(0);f.get(1);f.call('denseSlot');f.tee(i);f.int(0);f.out(0x48);f.if(()=>{f.int(0);f.ret();});
  f.get(0);f.get(i);f.call('arrayOwn');f.out(0xd1);f.if(()=>{f.int(0);f.ret();});
  dense(f,0);f.get(i);f.get(2);f.out(0xfb,0x0e,...u32(POOL));f.int(1);
 });
 def('objectGet',[REF,REF],[REF],f=>{
  const index=f.local(I32),node=f.local();f.get(1);f.call('key');f.set(1);forwarded(f,0);f.if(()=>host(f,'hostObjectGet',2));
  array(f,0);f.if(()=>{
   f.get(1);f.call('isLengthKey');f.if(()=>{f.field(0,OBJECT,LENGTH);f.out(0xb8);f.call('number');f.ret();});
   f.get(1);f.call('arrayIndex');f.tee(index);f.int(-1);f.out(0x47);f.if(()=>{
    f.get(0);f.get(index);f.call('arrayOwn');f.set(node);f.nonnull(node);f.if(()=>{f.get(node);f.ret();});
    f.call('prototypeFlags');f.int(2);f.out(0x71);f.if(()=>{f.undef();f.ret();});host(f,'hostObjectGet',2);
   });
  });
  f.get(1);f.call('isNumber');f.if(()=>{f.get(1);f.call('hostKey');f.set(1);});
  f.get(1);f.call('isString');f.if(()=>{f.get(0);f.get(1);f.call('objectFind');f.set(node);f.nonnull(node);f.if(()=>{f.field(node,PROPERTY,1);f.ret();});});
  f.field(0,OBJECT,KIND);f.int(2);f.out(0x46);f.if(()=>{f.undef();f.ret();});
  f.field(0,OBJECT,KIND);f.out(0x45);f.if(()=>{f.call('prototypeFlags');f.int(8);f.out(0x71);f.if(()=>{f.get(1);f.call('isString');f.if(()=>{f.get(1);f.call('isBaseKey');f.out(0x45);f.if(()=>{f.undef();f.ret();});});});});
  f.get(0);f.get(1);f.field(0,OBJECT,KIND);f.call('hostProtoGet');
 });
 def('objectSet',[REF,REF,REF,I32],[REF],f=>{
  const index=f.local(I32),node=f.local(),n=f.local(F64);f.get(1);f.call('key');f.set(1);forwarded(f,0);f.if(()=>host(f,'hostObjectSet',4));
  array(f,0);f.if(()=>{
   f.get(1);f.call('isLengthKey');f.if(()=>{
    f.get(2);f.call('isNumber');f.out(0x45);f.if(()=>host(f,'hostObjectSet',4));f.get(2);f.call('toNumber');f.set(n);
    f.get(n);f.float(0);f.out(0x66);f.get(n);f.float(4294967296);f.out(0x63,0x71);f.get(n);f.get(n);f.out(0x9d,0x61,0x71,0x45);f.if(()=>f.fail(6));
    f.get(0);f.get(n);f.out(0xab);f.call('arrayResize');f.get(2);f.ret();
   });
   f.get(1);f.call('arrayIndex');f.tee(index);f.int(-1);f.out(0x47);f.if(()=>{
    f.get(index);f.int(MAX_DENSE);f.out(0x4f);f.if(()=>host(f,'hostObjectSet',4));
    f.get(0);f.get(index);f.call('arrayOwn');f.out(0xd1);f.if(()=>{f.call('prototypeFlags');f.int(2);f.out(0x71,0x45);f.if(()=>host(f,'hostObjectSet',4));});
    f.get(0);f.get(index);f.get(2);f.call('arrayStore');f.ret();
   });
  });
  f.get(1);f.call('isNumber');f.if(()=>{f.get(1);f.call('hostKey');f.set(1);});
  f.get(1);f.call('isString');f.out(0x45);f.if(()=>host(f,'hostObjectSet',4));
  f.get(0);f.get(1);f.call('objectFind');f.set(node);f.nonnull(node);f.if(()=>{f.put(node,PROPERTY,1,()=>f.get(2));f.get(2);f.ret();});
  f.field(0,OBJECT,KIND);f.int(2);f.out(0x47);f.if(()=>{
   array(f,0);f.get(1);f.call('isProtoKey');f.out(0x72);f.if(()=>host(f,'hostObjectSet',4));
   f.call('prototypeFlags');f.int(1);f.out(0x71,0x45);f.if(()=>host(f,'hostObjectSet',4));
  });f.get(0);f.get(1);f.get(2);f.call('objectStore');
 });
 def('objectDefine',[REF,REF,REF,I32],[REF],f=>{
  forwarded(f,0);f.if(()=>host(f,'hostObjectDefine',4));
  f.get(3);f.int(3);f.out(0x46);f.if(()=>{f.get(2);f.call('atomKind');f.int(1);f.out(0x46);f.if(()=>{f.put(0,OBJECT,KIND,()=>f.int(2));f.get(0);f.ret();});host(f,'hostObjectDefine',4);});
  // A method that is a closure needs no home object: a plain data property.
  f.get(3);f.int(4);f.out(0x46);f.if(()=>{f.get(2);f.call('isClosure');f.if(()=>{f.int(0);f.set(3);});});
  f.get(3);f.if(()=>host(f,'hostObjectDefine',4));
  f.get(1);f.call('isString');f.out(0x45);f.if(()=>{f.get(1);f.call('hostKey');f.set(1);});
  f.get(1);f.call('isString');f.out(0x45);f.if(()=>host(f,'hostObjectDefine',4));
  f.get(0);f.get(1);f.get(2);f.call('objectStore');f.drop();f.get(0);
 });
 // 1 when o.push is the original Array.prototype.push on a plain WASM array.
 def('pushIntrinsic',[REF,REF],[I32],f=>{
  f.get(0);f.call('isObject');f.out(0x45);f.if(()=>{f.int(0);f.ret();});
  array(f,0);f.out(0x45);f.if(()=>{f.int(0);f.ret();});forwarded(f,0);f.if(()=>{f.int(0);f.ret();});
  // Near the length limit the real push decides (it throws RangeError).
  f.field(0,OBJECT,LENGTH);f.int(MAX_DENSE-8);f.out(0x4f);f.if(()=>{f.int(0);f.ret();});
  f.call('prototypeFlags');f.int(16);f.out(0x71,0x45);f.if(()=>{f.int(0);f.ret();});
  f.get(0);f.get(1);f.call('objectFind');f.out(0xd1);
 });
 // After the arguments of o.push(...) ran: may the elements still be stored here?
 def('pushStill',[REF],[I32],f=>{
  forwarded(f,0);f.if(()=>{f.int(0);f.ret();});f.field(0,OBJECT,LENGTH);f.int(MAX_DENSE-8);f.out(0x4f);f.if(()=>{f.int(0);f.ret();});
  f.call('prototypeFlags');f.int(2);f.out(0x71,0x45,0x45);
 });
 def('push',[REF,REF],[REF],f=>{
  forwarded(f,0);f.if(()=>{f.get(0);f.get(1);f.call('hostArrayPush');f.ret();});
  f.field(0,OBJECT,LENGTH);f.int(MAX_DENSE);f.out(0x4f);f.if(()=>{f.get(0);f.get(1);f.call('hostArrayPush');f.ret();});
  f.get(0);f.field(0,OBJECT,LENGTH);f.get(1);f.call('arrayStore');f.drop();f.get(0);
 });
 def('hole',[REF],[REF],f=>{
  forwarded(f,0);f.if(()=>host(f,'hostArrayHole',1));f.field(0,OBJECT,LENGTH);f.int(-1);f.out(0x46);f.if(()=>f.fail(6));f.put(0,OBJECT,LENGTH,()=>{f.field(0,OBJECT,LENGTH);f.int(1);f.out(0x6a);});f.get(0);
 });
 def('objectArg',[REF,I32],[REF],f=>{f.get(0);f.call('isObject');f.if(()=>{f.get(1);f.field(0,OBJECT,LENGTH);f.out(0x4f);f.if(()=>{f.undef();f.ret();});f.get(0);f.get(1);f.out(0xb8);f.call('number');f.call('objectGet');},()=>{f.get(0);f.get(1);f.call('hostArg');},REF);});
 def('objectDelete',[REF,REF,I32],[I32],f=>{
  const index=f.local(I32),node=f.local();f.get(1);f.call('key');f.set(1);forwarded(f,0);f.if(()=>host(f,'hostObjectDelete',3));
  array(f,0);f.if(()=>{
   f.get(1);f.call('isLengthKey');f.if(()=>{f.get(2);f.if(()=>f.fail(7));f.int(0);f.ret();});
   f.get(1);f.call('arrayIndex');f.tee(index);f.int(-1);f.out(0x47);f.if(()=>{f.get(index);f.get(0);f.call('arrayCapacity');f.out(0x49);f.if(()=>{dense(f,0);f.get(index);f.nil();f.out(0xfb,0x0e,...u32(POOL));});f.int(1);f.ret();});
  });f.get(1);f.call('isNumber');f.if(()=>{f.get(1);f.call('hostKey');f.set(1);});
  f.get(1);f.call('isString');f.if(()=>{f.get(0);f.get(1);f.call('objectUnlink');});f.int(1);
 });
 // Removes a key's node from the list, so delete/re-add cycles do not grow it.
 def('objectUnlink',[REF,REF],[],f=>{
  const node=f.local(),prev=f.local(),next=f.local();f.field(0,OBJECT,HEAD);f.set(node);
  f.block('end',end=>f.block('loop',loop=>{
   f.get(node);f.out(0xd1);f.branch(end,true);
   f.field(node,PROPERTY,0);f.internal(STRING);f.get(1);f.internal(STRING);f.out(0xd3);f.if(()=>f.int(1),()=>{f.field(node,PROPERTY,0);f.get(1);f.call('stringCompare');f.out(0x45);},I32);
   f.if(()=>{
    f.field(node,PROPERTY,2);f.set(next);
    f.nonnull(prev);f.if(()=>f.put(prev,PROPERTY,2,()=>f.get(next)),()=>f.put(0,OBJECT,HEAD,()=>f.get(next)));
    f.nonnull(next);f.out(0x45);f.if(()=>f.put(0,OBJECT,TAIL,()=>f.get(prev)));f.ret();
   });
   f.get(node);f.set(prev);f.field(node,PROPERTY,2);f.set(node);f.branch(loop);
  }));
 });
 def('has',[REF,REF],[I32],f=>{
  const index=f.local(I32),node=f.local();f.get(0);f.call('isObject');f.out(0x45);f.if(()=>host(f,'hostHas',2));
  f.get(1);f.call('key');f.set(1);forwarded(f,0);f.if(()=>host(f,'hostHas',2));
  array(f,0);f.if(()=>{
   f.get(1);f.call('isLengthKey');f.if(()=>{f.int(1);f.ret();});
   f.get(1);f.call('arrayIndex');f.tee(index);f.int(-1);f.out(0x47);f.if(()=>{
    f.get(0);f.get(index);f.call('arrayOwn');f.out(0xd1,0x45);f.if(()=>{f.int(1);f.ret();});
    f.call('prototypeFlags');f.int(2);f.out(0x71);f.if(()=>{f.int(0);f.ret();});host(f,'hostHas',2);
   });
  });f.get(1);f.call('isNumber');f.if(()=>{f.get(1);f.call('hostKey');f.set(1);});
  f.get(1);f.call('isString');f.if(()=>{f.get(0);f.get(1);f.call('objectFind');f.set(node);f.nonnull(node);f.if(()=>{f.int(1);f.ret();});});
  f.field(0,OBJECT,KIND);f.int(2);f.out(0x46);f.if(()=>{f.int(0);f.ret();});host(f,'hostHas',2);
 });
 def('remove',[REF],[REF],f=>{
  f.get(0);f.call('isObjectReference');f.if(()=>{f.field(0,OBJECT_REF,0);f.field(0,OBJECT_REF,1);f.field(0,OBJECT_REF,2);f.call('objectDelete');f.call('boolean');f.ret();});f.get(0);f.call('hostRemove');
 });
 // Method lookup/identity checks live at the host boundary; the recognized
 // intrinsic's element loop and length update are native WASM operations.
 def('arrayPushValues',[REF,REF,REF],[REF],f=>{
  const length=f.local(F64),i=f.local(I32),n=f.local(I32),result=f.local();
  f.get(0);f.get(2);f.call('objectGet');f.call('toNumber');f.set(length);f.field(1,OBJECT,LENGTH);f.set(n);
  f.block('end',end=>f.block('loop',loop=>{f.get(i);f.get(n);f.out(0x4f);f.branch(end,true);f.get(0);f.get(length);f.get(i);f.out(0xb8,0xa0);f.call('number');f.get(1);f.get(i);f.call('arg');f.int(1);f.call('objectSet');f.drop();f.get(i);f.int(1);f.out(0x6a);f.set(i);f.branch(loop);}));
  f.get(length);f.get(n);f.out(0xb8,0xa0);f.call('number');f.set(result);f.get(0);f.get(2);f.get(result);f.int(1);f.call('objectSet');
 });
 def('arrayPopValue',[REF,REF],[REF],f=>{
  const n=f.local(F64),index=f.local(),value=f.local();f.get(0);f.get(1);f.call('objectGet');f.call('toNumber');f.tee(n);f.float(0);f.out(0x61);f.if(()=>{f.undef();f.ret();});
  f.get(n);f.float(1);f.out(0xa1);f.call('number');f.set(index);f.get(0);f.get(index);f.call('objectGet');f.set(value);
  f.get(0);f.get(index);f.int(1);f.call('objectDelete');f.drop();f.get(0);f.get(1);f.get(index);f.int(1);f.call('objectSet');f.drop();f.get(value);
 });
}

exports["OBJECT"]=OBJECT;
exports["PROPERTY"]=PROPERTY;
exports["OBJECT_REF"]=OBJECT_REF;
exports["objectTypes"]=objectTypes;
exports["addObjectRuntime"]=addObjectRuntime;

// Own UTF-16 algorithms emitted directly as WASM instructions. No JS string
// operations are imported here. The bridge copies strings only at JS boundaries.
const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const F64=require("./wasm.mjs")["F64"];
const u32=require("./wasm.mjs")["u32"];

const STRING=7,UTF16=8,STRING_REF=9,BUFFER=10;
const stringTypes=[
 [0x5f,4,REF,0,I32,0,I32,0,0x6d,0], // buffer, offset, length, instance brand
 [0x5e,0x77,1],                    // packed unsigned UTF-16 code units
 [0x5f,4,REF,0,REF,0,I32,0,0x6d,0],// receiver, property key, strict, brand
 [0x5f,2,REF,0,I32,1],             // packed data, append-only high-water mark
];
const STRING_METHODS=['charCodeAt','charAt','slice','substring','indexOf','lastIndexOf','includes','startsWith','endsWith','repeat','concat'];

function addStringRuntime(def){
 const buffer=(f,s)=>{f.field(s,STRING,0);f.internal(BUFFER);f.out(0xfb,2,...u32(BUFFER),0);f.internal(UTF16);};
 const unit=(f,s,i)=>{buffer(f,s);f.field(s,STRING,1);f.get(i);f.out(0x6a,0xfb,0x0d,...u32(UTF16));};
 const length=(f,s)=>f.field(s,STRING,2);
 const next=(f,i)=>{f.get(i);f.int(1);f.out(0x6a);f.set(i);};
 const copy=(f,target,destination,source,sourceOffset,count)=>{
  buffer(f,target);destination();buffer(f,source);f.field(source,STRING,1);sourceOffset();f.out(0x6a);count();f.out(0xfb,0x11,...u32(UTF16),...u32(UTF16));
 };
 def('isString',[REF],[I32],f=>{f.test(0,STRING);f.if(()=>{f.field(0,STRING,3);f.out(0x23,6,0xd3);},()=>f.int(0),I32);});
 def('isStringReference',[REF],[I32],f=>{f.test(0,STRING_REF);f.if(()=>{f.field(0,STRING_REF,3);f.out(0x23,6,0xd3);},()=>f.int(0),I32);});
 for(const [name,index]of [['stringReceiver',0],['stringKey',1]])def(name,[REF],[REF],f=>f.field(0,STRING_REF,index));
 def('stringStrict',[REF],[I32],f=>f.field(0,STRING_REF,2));
 def('stringAllocate',[I32,I32],[REF],f=>{
  f.get(1);f.int(0x0ffffff0);f.out(0x4b);f.if(()=>f.fail(5));
  f.get(1);f.out(0xfb,7,...u32(UTF16),0xfb,0x1b);f.get(0);f.create(BUFFER);f.int(0);f.get(0);f.out(0x23,6);f.create(STRING);
 });
 def('stringAlloc',[I32],[REF],f=>{f.get(0);f.get(0);f.call('stringAllocate');});
 def('stringLength',[REF],[I32],f=>length(f,0));
 // Decimal text of an int32 (no host call).
 def('intToString',[I32],[REF],f=>{
  const u=f.local(I32),n=f.local(I32),t=f.local(I32),out=f.local(),at=f.local(I32),neg=f.local(I32);
  f.get(0);f.int(0);f.out(0x48);f.tee(neg);f.if(()=>{f.int(0);f.get(0);f.out(0x6b);},()=>f.get(0),I32);f.tee(u);f.set(t);
  f.int(1);f.set(n);
  f.block('end',end=>f.block('loop',loop=>{f.get(t);f.int(10);f.out(0x49);f.branch(end,true);f.get(t);f.int(10);f.out(0x6e);f.set(t);f.get(n);f.int(1);f.out(0x6a);f.set(n);f.branch(loop);}));
  f.get(n);f.get(neg);f.out(0x6a);f.call('stringAlloc');f.set(out);
  f.get(neg);f.if(()=>{f.get(out);f.int(0);f.int(45);f.call('stringSet');});
  f.get(n);f.get(neg);f.out(0x6a);f.set(at);
  f.block('end',end=>f.block('loop',loop=>{
   f.get(at);f.int(1);f.out(0x6b);f.set(at);f.get(out);f.get(at);f.get(u);f.int(10);f.out(0x70);f.int(48);f.out(0x6a);f.call('stringSet');
   f.get(u);f.int(10);f.out(0x6e);f.tee(u);f.out(0x45);f.branch(end,true);f.branch(loop);
  }));
  f.get(out);
 });
 // String(x) for numbers: int32 values in WASM, everything else via the host.
 def('numberToString',[F64],[REF],f=>{
  const i=f.local(I32);f.get(0);f.out(0xfc,0x02);f.tee(i);f.out(0xb7);f.get(0);f.out(0x61);
  f.if(()=>{f.get(i);f.call('intToString');},()=>{f.get(0);f.call('hostNumberToString');},REF);
 });
 def('stringFromCharCode',[I32],[REF],f=>{const s=f.local();f.int(1);f.call('stringAlloc');f.tee(s);f.int(0);f.get(0);f.int(0xffff);f.out(0x71);f.call('stringSet');f.get(s);});
 def('stringSet',[REF,I32,I32],[],f=>{buffer(f,0);f.field(0,STRING,1);f.get(1);f.out(0x6a);f.get(2);f.out(0xfb,0x0e,...u32(UTF16));});
 def('stringUnit',[REF,I32],[I32],f=>unit(f,0,1));
 def('stringRange',[REF,I32,I32],[REF],f=>{
  f.field(0,STRING,0);f.field(0,STRING,1);f.get(1);f.out(0x6a);f.get(2);f.get(1);f.out(0x6b,0x23,6);f.create(STRING);
 });
 def('stringConcat',[REF,REF],[REF],f=>{
  const target=f.local(),store=f.local(),n=f.local(I32),m=f.local(I32),total=f.local(I32),capacity=f.local(I32);
  length(f,0);f.tee(n);f.out(0x45);f.if(()=>{f.get(1);f.ret();});
  length(f,1);f.tee(m);f.out(0x45);f.if(()=>{f.get(0);f.ret();});
  f.get(n);f.get(m);f.out(0x6a);f.tee(total);f.int(0x0ffffff0);f.out(0x4b);f.if(()=>f.fail(5));
  f.field(0,STRING,0);f.set(store);f.field(0,STRING,1);f.out(0x45);f.field(store,BUFFER,1);f.get(n);f.out(0x46,0x71);buffer(f,0);f.out(0xfb,0x0f);f.get(total);f.out(0x4f,0x71);
  f.if(()=>{
   f.get(store);f.int(0);f.get(total);f.out(0x23,6);f.create(STRING);f.set(target);
   copy(f,target,()=>f.get(n),1,()=>f.int(0),()=>f.get(m));f.put(store,BUFFER,1,()=>f.get(total));f.get(target);f.ret();
  });
  f.get(total);f.int(2);f.out(0x6c);f.tee(capacity);f.int(32);f.out(0x49);f.if(()=>{f.int(32);f.set(capacity);});
  f.get(capacity);f.int(0x0ffffff0);f.out(0x4b);f.if(()=>{f.get(total);f.set(capacity);});f.get(total);f.get(capacity);f.call('stringAllocate');f.set(target);
  copy(f,target,()=>f.int(0),0,()=>f.int(0),()=>f.get(n));copy(f,target,()=>f.get(n),1,()=>f.int(0),()=>f.get(m));f.get(target);
 });
 def('stringCompare',[REF,REF],[I32],f=>{
  const i=f.local(I32),n=f.local(I32),m=f.local(I32),a=f.local(I32),b=f.local(I32);
  length(f,0);f.set(n);length(f,1);f.set(m);
  f.block('end',end=>f.block('loop',loop=>{
   f.get(i);f.get(n);f.out(0x4f);f.get(i);f.get(m);f.out(0x4f,0x72);f.branch(end,true);
   unit(f,0,i);f.set(a);unit(f,1,i);f.set(b);f.get(a);f.get(b);f.out(0x47);f.if(()=>{f.get(a);f.get(b);f.out(0x49);f.if(()=>f.int(-1),()=>f.int(1),I32);f.ret();});next(f,i);f.branch(loop);
  }));f.get(n);f.get(m);f.out(0x49);f.if(()=>f.int(-1),()=>{f.get(n);f.get(m);f.out(0x4b);},I32);
 });
 // ToIntegerOrInfinity and clamping for substring/search positions. Conversion
 // to i32 is reached only after ruling out NaN, infinities and large doubles.
 def('stringClamp',[F64,I32],[I32],f=>{
  f.get(0);f.get(0);f.out(0x62);f.get(0);f.float(0);f.out(0x65,0x72);f.if(()=>{f.int(0);f.ret();});
  f.get(0);f.get(1);f.out(0xb7,0x66);f.if(()=>{f.get(1);f.ret();});f.get(0);f.out(0xaa);
 });
 def('stringRelative',[F64,I32],[I32],f=>{
  f.get(0);f.out(0x9d);f.set(0);f.get(0);f.float(0);f.out(0x63);f.if(()=>{f.get(0);f.get(1);f.out(0xb7,0xa0);f.set(0);});f.get(0);f.get(1);f.call('stringClamp');
 });
 def('stringSlice',[REF,F64,F64],[REF],f=>{
  const start=f.local(I32),end=f.local(I32);f.get(1);length(f,0);f.call('stringRelative');f.set(start);f.get(2);length(f,0);f.call('stringRelative');f.set(end);
  f.get(end);f.get(start);f.out(0x49);f.if(()=>{f.get(start);f.set(end);});f.get(0);f.get(start);f.get(end);f.call('stringRange');
 });
 def('stringSubstring',[REF,F64,F64],[REF],f=>{
  const start=f.local(I32),end=f.local(I32),swap=f.local(I32);f.get(1);length(f,0);f.call('stringClamp');f.set(start);f.get(2);length(f,0);f.call('stringClamp');f.set(end);
  f.get(start);f.get(end);f.out(0x4b);f.if(()=>{f.get(start);f.set(swap);f.get(end);f.set(start);f.get(swap);f.set(end);});f.get(0);f.get(start);f.get(end);f.call('stringRange');
 });
 def('stringPosition',[REF,F64],[I32],f=>{
  f.get(1);f.get(1);f.out(0x62);f.if(()=>{f.float(0);f.set(1);});f.get(1);f.out(0x9d);f.set(1);
  f.get(1);f.float(0);f.out(0x63);f.get(1);length(f,0);f.out(0xb7,0x66,0x72);f.if(()=>{f.int(-1);f.ret();});f.get(1);f.out(0xaa);
 });
 def('stringCharCodeAt',[REF,F64],[F64],f=>{
  const i=f.local(I32);f.get(0);f.get(1);f.call('stringPosition');f.tee(i);f.int(0);f.out(0x48);f.if(()=>f.float(NaN),()=>{unit(f,0,i);f.out(0xb7);},F64);
 });
 def('stringCharAt',[REF,F64],[REF],f=>{
  const i=f.local(I32);f.get(0);f.get(1);f.call('stringPosition');f.tee(i);f.int(0);f.out(0x48);f.if(()=>{f.int(0);f.call('stringAlloc');},()=>{f.get(0);f.get(i);f.get(i);f.int(1);f.out(0x6a);f.call('stringRange');},REF);
 });
 def('stringMatch',[REF,REF,I32],[I32],f=>{
  const i=f.local(I32),at=f.local(I32),n=f.local(I32);length(f,1);f.set(n);
  f.get(2);length(f,0);f.out(0x4b);f.get(n);length(f,0);f.get(2);f.out(0x6b,0x4b,0x72);f.if(()=>{f.int(0);f.ret();});
  f.block('end',end=>f.block('loop',loop=>{f.get(i);f.get(n);f.out(0x4f);f.branch(end,true);f.get(2);f.get(i);f.out(0x6a);f.set(at);unit(f,0,at);unit(f,1,i);f.out(0x47);f.if(()=>{f.int(0);f.ret();});next(f,i);f.branch(loop);}));f.int(1);
 });
 def('stringIndexOf',[REF,REF,F64],[I32],f=>{
  const i=f.local(I32),last=f.local(I32);f.get(2);length(f,0);f.call('stringClamp');f.set(i);
  length(f,0);length(f,1);f.out(0x6b);f.set(last);
  f.block('end',end=>f.block('loop',loop=>{f.get(i);f.get(last);f.out(0x4a);f.branch(end,true);f.get(0);f.get(1);f.get(i);f.call('stringMatch');f.if(()=>{f.get(i);f.ret();});next(f,i);f.branch(loop);}));f.int(-1);
 });
 def('stringLastIndexOf',[REF,REF,F64],[I32],f=>{
  const i=f.local(I32),last=f.local(I32);f.get(2);f.get(2);f.out(0x62);f.if(()=>{f.float(Infinity);f.set(2);});
  f.get(2);length(f,0);f.call('stringClamp');f.set(i);length(f,0);length(f,1);f.out(0x6b);f.set(last);f.get(i);f.get(last);f.out(0x4a);f.if(()=>{f.get(last);f.set(i);});
  f.block('end',end=>f.block('loop',loop=>{f.get(i);f.int(0);f.out(0x48);f.branch(end,true);f.get(0);f.get(1);f.get(i);f.call('stringMatch');f.if(()=>{f.get(i);f.ret();});f.get(i);f.int(1);f.out(0x6b);f.set(i);f.branch(loop);}));f.int(-1);
 });
 def('stringIncludes',[REF,REF,F64],[I32],f=>{f.get(0);f.get(1);f.get(2);f.call('stringIndexOf');f.int(0);f.out(0x4e);});
 def('stringStartsWith',[REF,REF,F64],[I32],f=>{f.get(0);f.get(1);f.get(2);length(f,0);f.call('stringClamp');f.call('stringMatch');});
 def('stringEndsWith',[REF,REF,F64],[I32],f=>{f.get(0);f.get(1);f.get(2);length(f,0);f.call('stringClamp');length(f,1);f.out(0x6b);f.call('stringMatch');});
 def('stringRepeat',[REF,F64],[REF],f=>{
  const n=f.local(I32),total=f.local(F64),target=f.local(),filled=f.local(I32),take=f.local(I32);
  f.get(1);f.get(1);f.out(0x62);f.if(()=>{f.float(0);f.set(1);});f.get(1);f.out(0x9d);f.set(1);
  f.get(1);f.float(0);f.out(0x63);f.get(1);f.float(Infinity);f.out(0x61,0x72);f.if(()=>f.fail(5));
  length(f,0);f.out(0x45);f.get(1);f.float(0);f.out(0x61,0x72);f.if(()=>{f.int(0);f.call('stringAlloc');f.ret();});
  f.get(1);length(f,0);f.out(0xb7,0xa2);f.tee(total);f.float(0x0ffffff0);f.out(0x64);f.if(()=>f.fail(5));
  f.get(total);f.out(0xaa);f.tee(n);f.call('stringAlloc');f.set(target);length(f,0);f.set(filled);copy(f,target,()=>f.int(0),0,()=>f.int(0),()=>f.get(filled));
  f.block('end',end=>f.block('loop',loop=>{
   f.get(filled);f.get(n);f.out(0x4f);f.branch(end,true);f.get(n);f.get(filled);f.out(0x6b);f.set(take);f.get(take);f.get(filled);f.out(0x4b);f.if(()=>{f.get(filled);f.set(take);});
   copy(f,target,()=>f.get(filled),target,()=>f.int(0),()=>f.get(take));f.get(filled);f.get(take);f.out(0x6a);f.set(filled);f.branch(loop);
  }));f.get(target);
 });
 // Own indexed properties. A missing/noncanonical index must consult the JS
 // prototype, which can contain custom getters even for numeric-looking keys.
 def('stringOwnIndex',[REF],[I32],f=>{
  const n=f.local(I32),i=f.local(I32),digit=f.local(I32),value=f.local(I32),x=f.local(F64);
  f.get(0);f.call('isNumber');f.if(()=>{f.get(0);f.call('toNumber');f.tee(x);f.float(0);f.out(0x66);f.get(x);f.float(0x0ffffff0);f.out(0x63,0x71);f.if(()=>{f.get(x);f.out(0x9d);f.get(x);f.out(0x61);f.if(()=>{f.get(x);f.out(0xaa);f.ret();});});f.int(-1);f.ret();});
  f.get(0);f.call('isString');f.out(0x45);f.if(()=>{f.int(-1);f.ret();});length(f,0);f.tee(n);f.out(0x45);f.get(n);f.int(9);f.out(0x4b,0x72);f.if(()=>{f.int(-1);f.ret();});
  f.int(0);f.set(i);unit(f,0,i);f.int(48);f.out(0x46);f.get(n);f.int(1);f.out(0x4b,0x71);f.if(()=>{f.int(-1);f.ret();});
  f.block('end',end=>f.block('loop',loop=>{f.get(i);f.get(n);f.out(0x4f);f.branch(end,true);unit(f,0,i);f.int(48);f.out(0x6b);f.tee(digit);f.int(9);f.out(0x4b);f.if(()=>{f.int(-1);f.ret();});f.get(value);f.int(10);f.out(0x6c);f.get(digit);f.out(0x6a);f.set(value);next(f,i);f.branch(loop);}));f.get(value);
 });
 def('stringRead',[REF,REF],[REF],f=>{
  const index=f.local(I32),i=f.local(I32);f.get(1);f.call('isString');f.if(()=>{
   length(f,1);f.int(6);f.out(0x46);f.if(()=>{
    f.int(1);for(const [at,c]of [...'length'].entries()){f.int(at);f.set(i);unit(f,1,i);f.int(c.charCodeAt(0));f.out(0x46,0x71);}f.if(()=>{length(f,0);f.out(0xb7);f.call('number');f.ret();});
   });
  });
  f.get(1);f.call('stringOwnIndex');f.tee(index);length(f,0);f.out(0x49);f.if(()=>{f.get(0);f.get(index);f.get(index);f.int(1);f.out(0x6a);f.call('stringRange');f.ret();});f.get(0);f.get(1);f.call('hostStringRead');
 });
}

exports["STRING"]=STRING;
exports["UTF16"]=UTF16;
exports["STRING_REF"]=STRING_REF;
exports["BUFFER"]=BUFFER;
exports["stringTypes"]=stringTypes;
exports["STRING_METHODS"]=STRING_METHODS;
exports["addStringRuntime"]=addStringRuntime;

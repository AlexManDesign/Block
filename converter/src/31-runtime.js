// Browser ABI plus the own WASM-GC environment runtime embedded in metadata.
// No parser, AST evaluator, eval, Function constructor or third-party VM.
function boot(binary, constants, metadata, globalObject, moduleSpecs) {
 'use strict';
 globalObject ??= globalThis;
 moduleSpecs ??= [];
 const {Object,Reflect,Map,WeakMap,WeakSet,Proxy,Promise,Set,Array,ArrayBuffer,Number,String,BigInt,Symbol,RegExp,Error,TypeError,ReferenceError,RangeError,SyntaxError,WebAssembly,Uint8Array,Uint16Array,URL,atob}=globalThis;
 const UNINIT=Symbol('uninitialized');
 const errorTag=new WebAssembly.Tag({parameters:['externref']});
 const pool=constants.map(x=>x.t==='u'?undefined:x.t==='big'?BigInt(x.v):x.t==='num'?Number(x.v):x.v);
 let instance,environment,UNDEFINED,NULL,TRUE,FALSE;
 const hostObjects=new WeakMap();
 const ref=(object,key,strict=false)=>({object,key,strict});
 const toKey=k=>typeof k==='symbol'?k:Reflect.ownKeys({[k]:null})[0];
 // SetFunctionName: a symbol without a description gives the empty name.
 const keyName=key=>typeof key==='symbol'?(key.description===undefined?'':'['+key.description+']'):String(key);
 // Function.prototype.toString of compiled code: the source is not shipped, so
 // compiled functions read like built-ins, keeping their syntactic kind
 // (class detection by /^class/ still works).
 const compiledKinds=new WeakMap(),originalToString=Function.prototype.toString;
 const compiledToString={toString(){
  const kind=compiledKinds.get(this);if(kind===undefined)return Reflect.apply(originalToString,this,[]);
  const name=typeof this.name==='string'?this.name:'';
  switch(kind){
   case'class':return (name?'class '+name+' ':'class ')+'{ [native code] }';
   case'method':return name+'() { [native code] }';
   case'arrow':return '() => { [native code] }';
   case'generator':return 'function* '+name+'() { [native code] }';
   case'async':return 'async function '+name+'() { [native code] }';
   case'async-generator':return 'async function* '+name+'() { [native code] }';
  }
  return 'function '+name+'() { [native code] }';
 }}.toString;
 compiledKinds.set(compiledToString,'function');
 if(Function.prototype.toString!==compiledToString)Object.defineProperty(Function.prototype,'toString',{value:compiledToString,writable:true,configurable:true,enumerable:false});
 const functions=new WeakMap(),classes=new WeakMap(),thisCells=new WeakMap(),privateKeys=new WeakSet(),templateCache=new Map();
 const resolvePromise=Promise.resolve,thenPromise=Promise.prototype.then;
 const resolved=v=>Reflect.apply(resolvePromise,Promise,[v]),upon=(p,yes,no)=>Reflect.apply(thenPromise,p,[yes,no]);
 function thisValue(v){if(thisCells.has(v)){v=thisCells.get(v);if(v===UNINIT)throw new ReferenceError('Must call super before accessing this');}return v;}
 const environmentInfo=new WeakMap();
 function info(e){let data=environmentInfo.get(e);if(!data){data={};environmentInfo.set(e,data);}return data;}
 function context(e,key){for(let p=e;p;p=environment.scopeParent(p)){const data=environmentInfo.get(p);if(data&&Object.hasOwn(data,key))return data[key];}throw new ReferenceError('Missing '+key+' context');}
 function privateDeclare(e,index,kind,isStatic){const data=info(e);if(!data.privates)data.privates=new Map();const name=pool[index];if(!data.privates.has(name)){const d={name,kind,isStatic,values:new WeakMap()};data.privates.set(name,d);privateKeys.add(d);}}
 function privateKey(e,index){const name=pool[index];for(let p=e;p;p=environment.scopeParent(p)){const privates=environmentInfo.get(p)?.privates;if(privates?.has(name))return privates.get(name);}throw new SyntaxError('Unknown private name: '+name);}
 function privateCheck(d,o){if(!d.values.has(o))throw new TypeError('Object does not have private member #'+d.name);}
 const names=new Map();pool.forEach((name,i)=>{if(typeof name==='string')names.set(name,i);});
 function nameIndex(name){if(!names.has(name)){names.set(name,pool.length);pool.push(name);}return names.get(name);}
 const objectPrototype=Object.prototype,arrayPrototype=Array.prototype,getPrototype=Object.getPrototypeOf,getNames=Object.getOwnPropertyNames,defineProperty=Object.defineProperty;
 const baseKeys=Object.getOwnPropertyNames(Object.prototype);
 const intrinsic=(name)=>{const f=getDescriptor(arrayPrototype,name)?.value;return typeof f==='function'&&Reflect.apply(globalThis.Function.prototype.toString,f,[])===`function ${name}() { [native code] }`?f:null;};
 const stringCache=new Map(),hostStrings=new WeakMap(),characterCode=String.prototype.charCodeAt,fromCharCode=String.fromCharCode,getDescriptor=Object.getOwnPropertyDescriptor;
 const arrayPush=intrinsic('push'),arrayPop=intrinsic('pop');
 const speciesGetter=getDescriptor(Array,Symbol.species)?.get;
 // Natives that self-hosted built-ins replace inside WASM, resolved at boot.
 const nativeAt=path=>{let v=globalObject;for(const k of path.split('.')){v=v==null?undefined:v[k];}return typeof v==='function'?v:null;};
 let pendingObjects=null;
 const stringMethods=['charCodeAt','charAt','slice','substring','indexOf','lastIndexOf','includes','startsWith','endsWith','repeat','concat'];
 const stringIntrinsics=stringMethods.map(name=>{
  const f=getDescriptor(String.prototype,name)?.value;
  if(typeof f!=='function')return null;
  return Reflect.apply(globalThis.Function.prototype.toString,f,[])===`function ${name}() { [native code] }`?f:null;
 });
 const stringMethodIds=new Map(stringIntrinsics.map((f,i)=>[f,i]));stringMethodIds.delete(null);
 function rememberString(s,v){if(!stringCache.has(s)&&stringCache.size>=128)stringCache.delete(stringCache.keys().next().value);stringCache.set(s,v);hostStrings.set(v,s);return v;}
 // Small integers cross as plain numbers (i31 inside WASM); other values
 // are converted to the environment's own representation.
 function box(v){
  switch(typeof v){
   case'number':return (v|0)===v&&v>=-1073741824&&v<=1073741823&&(v!==0||1/v>0)?v:environment.number(v);
   case'object':if(v===null)return NULL;{const o=hostObjects.get(v);return o===undefined?v:o;}
   case'function':{const o=hostObjects.get(v);return o===undefined?v:o;}
   case'boolean':return v?TRUE:FALSE;
   case'undefined':return UNDEFINED;
   case'string':{
    if(stringCache.has(v))return stringCache.get(v);
    const s=environment.stringAlloc(v.length);for(let i=0;i<v.length;i++)environment.stringSet(s,i,Reflect.apply(characterCode,v,[i]));return rememberString(v,s);
   }
  }
  return v;
 }
 function unbox(v){
  if(v===null||typeof v!=='object')return v;
  switch(environment.valueKind(v)){
   case 1:return environment.toNumber(v);
   case 2:return undefined;case 3:return null;case 4:return false;case 5:return true;
   case 6:{
    if(hostStrings.has(v))return hostStrings.get(v);
    let s='';const n=environment.stringLength(v);
    for(let start=0;start<n;start+=4096){const end=n<start+4096?n:start+4096,codes=new Uint16Array(end-start);for(let i=start;i<end;i++)codes[i-start]=environment.stringUnit(v,i);s+=Reflect.apply(fromCharCode,String,codes);}
    rememberString(s,v);return s;
   }
   case 7:return materialize(v);
   case 8:return closureFunction(v);
  }
  return v;
 }
 // A WASM closure gets its JS function when it first reaches the host; the
 // function maps back to the same struct, so identity holds on both sides.
 function closureFunction(c){
  const existing=environment.closureHost(c);if(existing!==null)return existing;
  const f=makeFunction(environment.closureEnv(c),environment.closureId(c),unbox(environment.closureName(c)),environment.closureFlags(c),environment.closureSelf(c),environment.closureNewTarget(c));
  environment.closureSetHost(c,f);hostObjects.set(f,c);return f;
 }
 const isClosure=v=>typeof v==='object'&&v!==null&&environment.isClosure(v)===1;
 // Unboxes a value whose kind (see valueKind) WASM already computed.
 function fromKind(v,kind){
  switch(kind){
   case 0:return v;
   case 1:return typeof v==='number'?v:environment.toNumber(v);
   case 2:return undefined;case 3:return null;case 4:return false;case 5:return true;
   case 6:{const s=hostStrings.get(v);return s===undefined?unbox(v):s;}
  }
  return unbox(v);
 }
 // A native call or write can only change the prototypes the WASM object
 // runtime relies on when it is handed one of them.
 // Short argument arrays are searched too (Reflect.apply, Function.prototype.apply);
 // a long or sparse array is data, not an argument list.
 const isPrototype=v=>v===objectPrototype||v===arrayPrototype||v===Array;
 const touchesPrototypes=(...values)=>values.some(v=>isPrototype(v)||Array.isArray(v)&&v.length<=64&&v.some(isPrototype));
 // ToNumeric: one ToPrimitive (hint number), then BigInt or Number.
 const toNumeric=v=>typeof v==='number'||typeof v==='bigint'?v:-(-v);
 // Error text for a key: never runs the key's own toString.
 const keyText=k=>k!==null&&(typeof k==='object'||typeof k==='function')?'[object]':String(k);
 function failure(e){return e instanceof WebAssembly.Exception&&e.is(errorTag)?e:new WebAssembly.Exception(errorTag,[box(e)]);}
 // Host calls from WASM: JS exceptions become WASM exceptions. Fixed arities
 // avoid allocating rest arrays on every crossing.
 function boxedBridge(fn,invalidate=false,arity=fn.length){
  if(invalidate)return (...a)=>{try{return fn(...a);}catch(e){throw failure(e);}finally{environment.invalidatePrototypes();}};
  switch(arity){
   case 0:return ()=>{try{return fn();}catch(e){throw failure(e);}};
   case 1:return a=>{try{return fn(a);}catch(e){throw failure(e);}};
   case 2:return (a,b)=>{try{return fn(a,b);}catch(e){throw failure(e);}};
   case 3:return (a,b,c)=>{try{return fn(a,b,c);}catch(e){throw failure(e);}};
   case 4:return (a,b,c,d)=>{try{return fn(a,b,c,d);}catch(e){throw failure(e);}};
   case 5:return (a,b,c,d,e5)=>{try{return fn(a,b,c,d,e5);}catch(e){throw failure(e);}};
  }
  return (...a)=>{try{return fn(...a);}catch(e){throw failure(e);}};
 }
 function bridge(fn,scalar=false,invalidate=false){
  const out=scalar?v=>v:box;
  switch(fn.length){
   case 0:return boxedBridge(()=>out(fn()),invalidate);
   case 1:return boxedBridge(a=>out(fn(unbox(a))),invalidate);
   case 2:return boxedBridge((a,b)=>out(fn(unbox(a),unbox(b))),invalidate);
   case 3:return boxedBridge((a,b,c)=>out(fn(unbox(a),unbox(b),unbox(c))),invalidate);
   case 4:return boxedBridge((a,b,c,d)=>out(fn(unbox(a),unbox(b),unbox(c),unbox(d))),invalidate);
  }
  return boxedBridge((...a)=>out(fn(...a.map(unbox))),invalidate,-1);
 }
 function materialize(v){
  const existing=environment.objectHost(v);if(existing!==null)return existing;
  const kind=environment.objectKind(v),o=kind===1?[]:kind===2?Object.create(null):{};
  environment.objectSetHost(v,o);hostObjects.set(o,v);
  const active=pendingObjects!==null,queue=pendingObjects??(pendingObjects=[]);
  defineProperty(queue,queue.length,{value:[v,o,kind],writable:true,configurable:true});if(active)return o;
  try{for(let cursor=0;cursor<queue.length;cursor++){
   const [record,target,type]=queue[cursor];
   if(type===1){const n=environment.arrayLength(record)>>>0;target.length=n;const cap=environment.arrayCapacity(record);for(let i=0;i<cap&&i<n;i++){const value=environment.arrayOwn(record,i);if(value!==null)defineProperty(target,i,{value:unbox(value),writable:true,enumerable:true,configurable:true});}}
   for(let node=environment.objectHead(record);node!==null;node=environment.propertyNext(node)){const value=environment.propertyValue(node);if(value!==null)defineProperty(target,unbox(environment.propertyKey(node)),{value:unbox(value),writable:true,enumerable:true,configurable:true});}
  }}finally{pendingObjects=null;}
  return o;
 }
 function prototypeFlags(){
  let flags=4;const objectChain=getPrototype(objectPrototype)===null,arrayChain=getPrototype(arrayPrototype)===objectPrototype;
  const index=k=>String(Number(k))===k&&Number(k)>=0&&Number(k)<4294967295&&Number.isInteger(Number(k));
  const keys=getNames(objectPrototype);
  if(objectChain&&keys.every(k=>k==='__proto__'||getDescriptor(objectPrototype,k)?.writable===true))flags|=1;
  if(objectChain&&arrayChain&&!keys.some(index)&&!getNames(arrayPrototype).some(index))flags|=2;
  if(objectChain&&keys.length===baseKeys.length&&keys.every((k,i)=>k===baseKeys[i]))flags|=8;
  if(arrayChain&&getDescriptor(arrayPrototype,'push')?.value===arrayPush&&(flags&2))flags|=16;
  // ArraySpeciesCreate of a plain array is a plain array.
  if(arrayChain&&getDescriptor(arrayPrototype,'constructor')?.value===Array&&speciesGetter&&getDescriptor(Array,Symbol.species)?.get===speciesGetter)flags|=32;
  return flags;
 }
 // Prototype lookup for a key an ordinary WASM object does not have.
 function protoGet(o,key,kind){
  const k=typeof key==='object'&&key!==null?fromKind(key,6):key;
  if(getPrototype(objectPrototype)===null&&(kind!==1||getPrototype(arrayPrototype)===objectPrototype)){
   const d=(kind===1?getDescriptor(arrayPrototype,k):undefined)||getDescriptor(objectPrototype,k);
   if(!d)return UNDEFINED;if(Object.hasOwn(d,'value'))return box(d.value);
  }
  return box(materialize(o)[k]);
 }
 function objectGet(o,key){
  const k=unbox(key),forward=environment.objectHost(o);if(forward!==null)return box(forward[k]);
  const kind=environment.objectKind(o);if(kind===2)return box(undefined);
  // Data properties on the two unmodified prototype chains need no receiver.
  // Getters and unusual chains receive the stable, materialized JS identity.
  if(getPrototype(objectPrototype)===null&&(kind!==1||getPrototype(arrayPrototype)===objectPrototype)){
   const d=(kind===1?getDescriptor(arrayPrototype,k):undefined)||getDescriptor(objectPrototype,k);
   if(!d)return box(undefined);if(Object.hasOwn(d,'value'))return box(d.value);
  }
  return box(materialize(o)[k]);
 }
 const host={
  hostRead:readProperty,hostWrite:writeProperty,
  hostGet:(o,k)=>{if(o==null)throw new TypeError("Cannot read properties of "+o+" (reading '"+keyText(k)+"')");return o[k];},
  hostGetIndex:(o,i)=>{if(o==null)throw new TypeError("Cannot read properties of "+o+" (reading '"+i+"')");return o[i];},
  hostSet:(o,k,v,s)=>setProperty(o,k,v,!!s),hostSetIndex:(o,i,v,s)=>setProperty(o,i,v,!!s),
  hostProperty:(o,k,s)=>{if(o==null)throw new TypeError('Cannot access '+keyText(k)+' of '+o);return ref(o,k,!!s);},
  hostStringRead:(o,k)=>o[k],hostStringWrite:(o,k,v,s)=>setProperty(o,k,v,!!s),
  hostEqual:(a,b)=>+(a===b),hostTemplateString:v=>{if(typeof v==='symbol')throw new TypeError('Cannot convert Symbol to string');return String(v);},
  hostNumberToString:v=>String(v),hostToNumeric:toNumeric,hostToNumber:v=>+v,hostIncrement:(v,d)=>typeof v==='bigint'?v+BigInt(d):v+d,
  hostTruth:v=>+!!v,hostUpdate:(r,value,d,post)=>{let n=toNumeric(value);const previous=n;n=typeof n==='bigint'?n+BigInt(d):n+d;write(r,n);return post?previous:n;},
  fail:(code,k)=>{if(code===1)throw new SyntaxError('Duplicate binding: '+pool[k]);if(code===2)throw new ReferenceError('Binding is not initialized');if(code===3)throw new TypeError('Assignment to import binding');if(code===4)throw new TypeError('Assignment to constant variable');if(code===5)throw new RangeError('Invalid string length or repeat count');if(code===6)throw new RangeError('Invalid array length');if(code===7)throw new TypeError('Cannot delete array length');throw new Error('Environment runtime error '+code);},
 };
 // f<id> by plan id, for calls of WASM closures from WASM.
 const table=new WebAssembly.Table({element:'anyfunc',initial:Math.max(1,metadata.length)});
 const nativeImports={undefined:new WebAssembly.Global({value:'externref'},undefined),table};
 // Writes and updates on host objects may run setters that change prototypes.
 const mutating=new Set(['hostWrite','hostSet','hostSetIndex','hostUpdate']);
 for(const [name,fn]of Object.entries(host))nativeImports[name]=bridge(fn,name==='hostTruth'||name==='hostEqual'||name==='hostToNumber',mutating.has(name));
 const key=k=>typeof k==='object'&&k!==null?fromKind(k,6):k;
 const typeNames=['number','string','undefined','boolean','function','object','symbol','bigint'];
 let stash;
 Object.assign(nativeImports,{
  hostGetRaw:boxedBridge((o,k)=>box(o[key(k)])),
  hostGetF:boxedBridge((o,k)=>{const v=o[key(k)];if(typeof v==='number'&&v===v)return v;stash=v;return NaN;}),
  hostGetIndexF:boxedBridge((o,i)=>{const v=o[i];if(typeof v==='number'&&v===v)return v;stash=v;return NaN;}),
  hostStash:()=>{const v=stash;stash=undefined;return box(v);},
  hostDefineIndex:boxedBridge((o,i,v)=>{defineProperty(unbox(o),i,{value:unbox(v),writable:true,enumerable:true,configurable:true});return o;}),
  hostTypeof:v=>typeNames.indexOf(typeof v),
  hostGetIndexRaw:boxedBridge((o,i)=>box(o[i])),
  hostSetRaw:boxedBridge((o,k,v,kind,strict)=>{const value=fromKind(v,kind);setProperty(o,key(k),value,!!strict);if(isPrototype(o))environment.invalidatePrototypes();return v;},false,5),
  hostSetIndexRaw:boxedBridge((o,i,v,kind,strict)=>{const value=fromKind(v,kind);if(ArrayBuffer.isView(o))o[i]=value;else{setProperty(o,i,value,!!strict);if(isPrototype(o))environment.invalidatePrototypes();}return v;},false,5),
  hostCall0:boxedBridge((fn,self,k)=>callKinds(fn,self,k,[])),
  hostCall1:boxedBridge((fn,self,k,a)=>callKinds(fn,self,k,[a])),
  hostCall2:boxedBridge((fn,self,k,a,b)=>callKinds(fn,self,k,[a,b]),false,5),
  ...Object.fromEntries(Array.from({length:6},(_,i)=>['hostCall'+(i+3),boxedBridge((fn,self,k,...a)=>callKinds(fn,self,k,a),false,-1)])),
  hostPrototypeFlags:boxedBridge(prototypeFlags),hostKey:bridge(toKey),
  hostHas:boxedBridge((o,k)=>{const key=unbox(k);if(environment.isObject(o)){const forward=environment.objectHost(o);if(forward!==null)return +(key in forward);return +(key in (environment.objectKind(o)===1?arrayPrototype:objectPrototype));}return +(key in unbox(o));},true),
  hostRemove:bridge(r=>raw.remove(r)),
  hostObjectGet:boxedBridge(objectGet),hostProtoGet:boxedBridge(protoGet),
  hostObjectSet:boxedBridge((o,k,v,strict)=>{setProperty(materialize(o),unbox(k),unbox(v),!!strict);return v;}),
  hostObjectDefine:boxedBridge((o,k,v,kind)=>{raw.define(materialize(o),unbox(k),unbox(v),kind);return o;}),
  hostObjectDelete:boxedBridge((o,k,strict)=>{const ok=Reflect.deleteProperty(materialize(o),unbox(k));if(!ok&&strict)throw new TypeError('Cannot delete property');return +ok;},true),
  hostArrayPush:boxedBridge((o,v)=>{const a=materialize(o),i=a.length;defineProperty(a,i,{value:unbox(v),writable:true,enumerable:true,configurable:true});return o;},true),
  hostArrayHole:boxedBridge(o=>{materialize(o).length++;return o;}),
  hostArg:boxedBridge((a,i)=>box(i<a.length?a[i]:undefined)),hostArgCount:a=>a.length,
  hostMakeFunction:boxedBridge((env,id,k,flags,self,newTarget)=>makeFunction(env,id,pool[k],flags,self,newTarget)),
  hostToObject:boxedBridge(v=>box(Object(unbox(v)))),
 });
 if(!metadata[0]?.environmentWasm)throw new Error('Missing WASM environment runtime; rebuild with compiler 0.9+.');
 const environmentBytes=Uint8Array.from(atob(metadata[0].environmentWasm),c=>c.charCodeAt(0));
 environment=new WebAssembly.Instance(new WebAssembly.Module(environmentBytes),{h:nativeImports}).exports;
 environment.initValues(pool.length);
 environment.setGlobalObject(globalObject);
 UNDEFINED=environment.undefinedValue();NULL=environment.nullValue();TRUE=environment.boolean(1);FALSE=environment.boolean(0);
 pool.forEach((v,i)=>environment.setConstant(i,box(v)));
 environment.setBaseKeys(baseKeys.length);baseKeys.forEach((k,i)=>environment.setBaseKey(i,box(k)));
 const root=environment.scopeNew(null,metadata[0]?.rootSlots|0);
 function read(r){try{return unbox(environment.read(r));}catch(e){throw unwrap(e);}}
 function write(r,v){try{return unbox(environment.write(r,box(v)));}catch(e){throw unwrap(e);}}
 function propertyReference(r){if(environment.isObjectReference(r))return ref(unbox(environment.objectReceiver(r)),unbox(environment.objectKey(r)),!!environment.objectStrict(r));return environment.isStringReference(r)?ref(unbox(environment.stringReceiver(r)),unbox(environment.stringKey(r)),!!environment.stringStrict(r)):r;}
 function prepare(r){
  const fn=environment.read(r);
  if(environment.isObjectReference(r))return ({fn,self:environment.objectReceiver(r)});
  if(environment.isStringReference(r))return ({fn,self:environment.stringReceiver(r),nativeString:true});
  return ({fn,self:box(r.super?r.receiver:r.object)});
 }
 function nativeStringCall(fn,s,args){
  const method=stringMethods[stringMethodIds.get(fn)],a=args[0],b=args[1];
  const text=v=>{if(typeof v==='symbol')throw new TypeError('Cannot convert Symbol to string');return box(String(v));};
  switch(method){
   case'charCodeAt':return environment.number(environment.stringCharCodeAt(s,+a));
   case'charAt':return environment.stringCharAt(s,+a);
   case'slice':return environment.stringSlice(s,+a,b===undefined?Infinity:+b);
   case'substring':return environment.stringSubstring(s,+a,b===undefined?Infinity:+b);
   case'repeat':return environment.stringRepeat(s,+a);
   case'concat':{for(const value of args)s=environment.stringConcat(s,text(value));return s;}
   case'indexOf':case'lastIndexOf':{const search=text(a),position=+b;return environment.number(environment[method==='indexOf'?'stringIndexOf':'stringLastIndexOf'](s,search,position));}
   case'includes':case'startsWith':case'endsWith':{
    if(a!==null&&(typeof a==='object'||typeof a==='function'))return Reflect.apply(fn,unbox(s),args);
    const search=text(a),position=method==='endsWith'&&b===undefined?Infinity:+b;
    return environment.boolean(environment['string'+method[0].toUpperCase()+method.slice(1)](s,search,position));
   }
  }
  return Reflect.apply(checkCallable(fn),unbox(s),args);
 }
 function argumentList(a){const n=Array.isArray(a)?a.length:environment.argCount(a)>>>0,values=[];for(let i=0;i<n;i++)defineProperty(values,i,{value:unbox(environment.arg(a,i)),writable:true,enumerable:true,configurable:true});return values;}
 // Calls a boxed function value with boxed `self` and a JS array of boxed
 // arguments. Compiled functions are entered directly.
 // A WASM array whose push/pop can run in WASM: not materialized, and no
 // index accessors on the prototypes (those must see the element writes).
 const denseArray=self=>environment.isObject(self)&&environment.objectKind(self)===1&&environment.objectHost(self)===null&&(environment.prototypeFlags()&2)!==0;
 function callValue(fn,self,args){
  if(isClosure(fn))return environment.closureInvoke(fn,self,args);
  const f=functions.get(fn);if(f?.invokeBoxed)return f.invokeBoxed(self,args,environment.undefinedValue());
  if(stringMethodIds.has(fn)&&environment.isString(self))return box(nativeStringCall(fn,self,args.map(unbox)));
  if((fn===arrayPush||fn===arrayPop)&&denseArray(self)&&(environment.arrayLength(self)>>>0)+args.length<1048576){
   if(fn===arrayPop)return environment.arrayPopValue(self,box('length'));
   for(const v of args)environment.push(self,v);return environment.getProp(self,box('length'));
  }
  const values=new Array(args.length);for(let i=0;i<args.length;i++)values[i]=unbox(args[i]);
  return applyNative(fn,unbox(self),values);
 }
 function applyNative(fn,self,values){
  const result=Reflect.apply(checkCallable(typeof fn==='function'?fn:unbox(fn)),self,values);
  if(touchesPrototypes(self,...values))environment.invalidatePrototypes();
  return box(result);
 }
 function callKinds(fn,self,kinds,args){
  if(isClosure(fn))return environment.closureInvoke(fn,self,args);
  const f=functions.get(fn);if(f?.invokeBoxed)return f.invokeBoxed(self,args,environment.undefinedValue());
  const selfKind=kinds&7;
  if(selfKind===6&&stringMethodIds.has(fn)||selfKind===7&&(fn===arrayPush||fn===arrayPop))return callValue(fn,self,args);
  const values=new Array(args.length);for(let i=0;i<args.length;i++)values[i]=fromKind(args[i],(kinds>>>3*(i+1))&7);
  return applyNative(fn,fromKind(self,selfKind),values);
 }
 function boxedArguments(a){if(Array.isArray(a))return a;const n=environment.argCount(a)>>>0,values=new Array(n);for(let i=0;i<n;i++)values[i]=environment.arg(a,i);return values;}
 function invoke(r,a){
  if(isClosure(r.fn))return environment.closureInvoke(r.fn,r.self,a);
  const f=functions.get(r.fn);if(f?.invokeBoxed)return f.invokeBoxed(r.self,a,environment.undefinedValue());
  if(r.nativeString&&stringMethodIds.has(r.fn))return box(nativeStringCall(r.fn,r.self,argumentList(a)));
  if(environment.isObject(r.self)&&environment.objectKind(r.self)===1&&environment.objectHost(r.self)===null){
   if(r.fn===arrayPush)return environment.arrayPushValues(r.self,a,box('length'));
   if(r.fn===arrayPop)return environment.arrayPopValue(r.self,box('length'));
  }
  return applyNative(r.fn,unbox(r.self),argumentList(a));
 }
 function readProperty(r){if(r.private){const d=r.private;privateCheck(d,r.object);if(d.kind===0)return d.values.get(r.object);if(d.kind===1)return d.fn;if(!d.get)throw new TypeError('Private getter missing');return Reflect.apply(d.get,r.object,[]);}if(r.unresolved)throw new ReferenceError(r.key+' is not defined');if(r.super)return Reflect.get(r.object,r.key,r.receiver);return r.object[r.key];}
 function setProperty(object,key,value,strict){if(object==null)throw new TypeError('Cannot set property of null or undefined');if(typeof object!=='object'&&typeof object!=='function'){if(strict)throw new TypeError('Cannot assign property of primitive');return value;}if(!Reflect.set(object,key,value,object)&&strict)throw new TypeError('Cannot assign property '+String(key));return value;}
 function writeProperty(r,v){if(r.private){const d=r.private;privateCheck(d,r.object);if(d.kind===0)d.values.set(r.object,v);else if(d.kind===2&&d.set)Reflect.apply(d.set,r.object,[v]);else throw new TypeError('Private member is not writable');return v;}if(r.unresolved&&r.strict)throw new ReferenceError(r.key+' is not defined');if(r.super){if(!Reflect.set(r.object,r.key,v,r.receiver))throw new TypeError('Cannot assign super property');return v;}return setProperty(r.object,r.key,v,r.strict);}
 function unwrap(e){if(e instanceof WebAssembly.Exception&&e.is(errorTag))return unbox(e.getArg(errorTag,0));return e;}
 function callCompiled(id,env,self,args,newTarget){try{return unbox(instance.exports['f'+id](env,box(self),args,box(newTarget)));}catch(e){throw unwrap(e);}}
 function makeFunction(env,id,name,flags,self,newTarget){
  const strict=!!(flags&1),arrow=!!(flags&2),info={home:null,method:!!(flags&8)};let closure=env,f;
  const call=(receiver,a,target)=>flags&48?startResumable(id,closure,receiver,a,target,flags,f):callCompiled(id,closure,receiver,a,target);
  info.invokeBoxed=(receiver,a,target)=>{
   if(arrow){receiver=box(self);target=box(newTarget);}else if(!strict){if(environment.nullish(receiver))receiver=box(globalObject);else if(!environment.isObject(receiver))receiver=box(Object(unbox(receiver)));}
   return flags&48?box(startResumable(id,closure,receiver,a,target,flags,f)):instance.exports['f'+id](closure,receiver,a,target);
  };
  if(arrow)f=(...a)=>call(self,a,newTarget);
  else if(flags&56)f=({method(...a){return call(strict?this:(this==null?globalObject:Object(this)),a,undefined);}}).method;
  else f=function(...a){return call(strict?this:(this==null?globalObject:Object(this)),a,new.target);};
  Object.defineProperty(f,'name',{value:name||'',configurable:true});Object.defineProperty(f,'length',{value:metadata[id].arity,configurable:true});
  // Record [parent, f] per function object: self name of a named function
  // expression, home object for super, callee for arguments.
  if(flags&64){closure=environment.scopeNew(env,1);environment.scopeSet(closure,1,f);info.record=closure;environmentInfo.set(closure,{callee:f});}
  functions.set(f,info);compiledKinds.set(f,arrow?'arrow':flags&8?'method':(flags&48)===48?'async-generator':flags&16?'generator':flags&32?'async':'function');
  if(flags&16)Object.defineProperty(f,'prototype',{value:Object.create(flags&32?asyncGeneratorPrototype:generatorPrototype),writable:true});
  return f;
 }
 function setHome(fn,home){const i=functions.get(fn);if(!i)return;i.home=home;if(i.record)environmentInfo.get(i.record).home=home;}
 function addPrivate(d,self,value){if(d.values.has(self))throw new TypeError('Private member already initialized');d.values.set(self,value);}
 function initializeField(field,self){const value=Reflect.apply(field.fn,self,[field.key]);if(field.isBlock)return;
  // An anonymous function in a computed-key field is named after the key.
  if(field.named&&typeof value==='function')Object.defineProperty(value,'name',{value:keyName(field.key),configurable:true});if(privateKeys.has(field.key))addPrivate(field.key,self,value);else Object.defineProperty(self,field.key,{value,writable:true,enumerable:true,configurable:true});}
 function initializeFields(c,self){const info=classes.get(c);for(const d of info.privateMethods)if(!d.isStatic)addPrivate(d,self,undefined);for(const field of info.fields)initializeField(field,self);}
 function makeClass(env,base,ctorId,nameIndex,derived){
  let prototype=Object.prototype;if(derived){if(base!==null){if(typeof base!=='function')throw new TypeError('Class extends a non-constructor');Reflect.construct(Object,[],base);}prototype=base===null?null:base.prototype;if(prototype!==null&&typeof prototype!=='object'&&typeof prototype!=='function')throw new TypeError('Invalid superclass prototype');}
  const e=environment.scopeNew(env,1);
  const c=function(...args){if(!new.target)throw new TypeError('Class constructor cannot be called without new');
   if(derived){const cell={};thisCells.set(cell,UNINIT);if(ctorId<0){const value=Reflect.construct(Object.getPrototypeOf(c),args,new.target);thisCells.set(cell,value);initializeFields(c,value);return value;}const result=callCompiled(ctorId,e,cell,args,new.target);if(result!==null&&(typeof result==='object'||typeof result==='function'))return result;if(result!==undefined)throw new TypeError('Derived constructor returned a primitive');return thisValue(cell);}
   initializeFields(c,this);if(ctorId<0)return this;const result=callCompiled(ctorId,e,this,args,new.target);return result!==null&&(typeof result==='object'||typeof result==='function')?result:this;
  };
  c.prototype=Object.create(prototype,{constructor:{value:c,writable:true,configurable:true}});Object.defineProperty(c,'prototype',{writable:false});Object.defineProperty(c,'name',{value:pool[nameIndex],configurable:true});Object.defineProperty(c,'length',{value:ctorId<0?0:metadata[ctorId].arity,configurable:true});if(derived&&base!==null)Object.setPrototypeOf(c,base);classes.set(c,{fields:[],statics:[],privateMethods:[]});
  environment.scopeSet(e,1,c);environmentInfo.set(e,{home:c.prototype,classOwner:c});compiledKinds.set(c,'class');return c;
 }
 function classMethod(c,key,fn,kind,isStatic){const home=isStatic?c:c.prototype;setHome(fn,home);const name=privateKeys.has(key)?'#'+key.name:keyName(key);Object.defineProperty(fn,'name',{value:(kind===1?'get ':kind===2?'set ':'')+name,configurable:true});if(privateKeys.has(key)){key[kind===1?'get':kind===2?'set':'fn']=fn;const methods=classes.get(c).privateMethods;if(!methods.includes(key))methods.push(key);return;}Object.defineProperty(home,key,{...(kind===1?{get:fn}:kind===2?{set:fn}:{value:fn,writable:true}),enumerable:false,configurable:true});}
 function classField(c,key,fn,isStatic,flags){setHome(fn,isStatic?c:c.prototype);classes.get(c)[isStatic?'statics':'fields'].push({key,fn,isBlock:flags&1,named:flags&2});}
 function finishClass(c){const info=classes.get(c);for(const d of info.privateMethods)if(d.isStatic)addPrivate(d,c,undefined);for(const field of info.statics)initializeField(field,c);return c;}
 function superCall(env,cell,args,newTarget){const c=context(env,'classOwner'),value=Reflect.construct(Object.getPrototypeOf(c),args,newTarget);if(thisCells.get(cell)!==UNINIT)throw new ReferenceError('Super constructor may only be called once');thisCells.set(cell,value);initializeFields(c,value);return value;}
 function checkCallable(f){if(f===globalThis.eval||f===globalThis.Function)throw new TypeError('Runtime JS generation is not supported by this AOT compiler');return f;}
 function argumentsObject(args,env,paramIndex,mapped){
  const object={...args};Object.defineProperty(object,'length',{value:args.length,writable:true,configurable:true});Object.defineProperty(object,Symbol.iterator,{value:Array.prototype[Symbol.iterator],writable:true,configurable:true});
  if(!mapped){const poison=()=>{throw new TypeError('Restricted arguments.callee');};Object.defineProperty(object,'callee',{get:poison,set:poison});return object;}
  Object.defineProperty(object,'callee',{value:context(env,'callee'),writable:true,configurable:true});const slots=pool[paramIndex],map=new Map();for(let i=0;i<slots.length&&i<args.length;i++)if(slots[i])map.set(String(i),slots[i]);
  const read=k=>unbox(environment.scopeGet(env,map.get(k))),write=(k,v)=>environment.scopeSet(env,map.get(k),box(v));
  return new Proxy(object,{get:(t,k,r)=>map.has(k)?read(k):Reflect.get(t,k,r),getOwnPropertyDescriptor:(t,k)=>{const d=Reflect.getOwnPropertyDescriptor(t,k);if(d&&map.has(k))d.value=read(k);return d;},defineProperty:(t,k,descriptor)=>{const linked=map.has(k),d={...descriptor};if(linked&&d.writable===false&&!Object.hasOwn(d,'value')&&!Object.hasOwn(d,'get')&&!Object.hasOwn(d,'set'))d.value=read(k);if(!Reflect.defineProperty(t,k,d))return false;if(linked){if(Object.hasOwn(d,'get')||Object.hasOwn(d,'set'))map.delete(k);else{if(Object.hasOwn(d,'value'))write(k,d.value);if(d.writable===false)map.delete(k);}}return true;},deleteProperty:(t,k)=>{const ok=Reflect.deleteProperty(t,k);if(ok)map.delete(k);return ok;}});
 }
 function requireObject(v){if(v===null||(typeof v!=='object'&&typeof v!=='function'))throw new TypeError('Iterator result must be an object');return v;}
 function iterator(v){const it=requireObject(Reflect.apply(checkCallable(v[Symbol.iterator]),v,[]));return {iterator:it,next:it.next,done:false};}
 function step(r,readValue=true){if(r.done)return {done:true,value:undefined};try{const item=requireObject(Reflect.apply(checkCallable(r.next),r.iterator,[]));if(item.done){r.done=true;return {done:true,value:undefined};}return {done:false,value:readValue?item.value:undefined};}catch(e){r.done=true;throw e;}}
 function closeIterator(r,pendingThrow){if(r.done)return;r.done=true;try{const method=r.iterator.return;if(method!=null)requireObject(Reflect.apply(checkCallable(method),r.iterator,[]));}catch(e){if(!pendingThrow)throw e;}}
 function asyncIterator(v){const method=v[Symbol.asyncIterator];if(method!=null){const it=requireObject(Reflect.apply(checkCallable(method),v,[]));return {iterator:it,next:it.next,done:false,sync:false};}return {...iterator(v),sync:true};}
 function syncContinuation(r,item,closeOnRejection){
  requireObject(item);const done=!!item.done,value=item.value;if(done)r.done=true;
  const fail=e=>{if(!done&&closeOnRejection)closeIterator(r,true);throw e;};
  let p;try{p=resolved(value);}catch(e){return fail(e);}return upon(p,v=>({done,value:v}),fail);
 }
 function asyncNext(r){
  if(r.done)return resolved({done:true,value:undefined});
  try{const result=Reflect.apply(checkCallable(r.next),r.iterator,[]);
   const read=item=>{requireObject(item);const done=!!item.done;if(done){r.done=true;return {done:true,value:undefined};}return {done:false,value:item.value};};
   const promise=r.sync?syncContinuation(r,result,true):upon(resolved(result),read);
   return upon(promise,v=>v,e=>{r.done=true;throw e;});
  }catch(e){r.done=true;return Promise.reject(e);}
 }
 function asyncClose(r,pendingThrow){
  if(r.done)return resolved(undefined);r.done=true;
  try{const method=r.iterator.return;if(method==null)return resolved(undefined);const result=Reflect.apply(checkCallable(method),r.iterator,[]);
   const promise=r.sync?syncContinuation(r,result,false):upon(resolved(result),requireObject);
   return upon(promise,()=>undefined,e=>{if(!pendingThrow)throw e;});
  }catch(e){return pendingThrow?resolved(undefined):Promise.reject(e);}
 }
 function delegate(v,async){return {...(async?asyncIterator(v):iterator(v)),async};}
 function delegateStep(r,request){
  const advance=()=>{
   const kind=request.kind,name=kind===1?'throw':kind===2?'return':'next';let method=kind===0?r.next:r.iterator[name];
   if(method==null){if(kind===2)return {done:true,value:request.value};if(kind===1){const close=r.iterator.return;if(close!=null){const answer=Reflect.apply(checkCallable(close),r.iterator,[]);if(r.async&&!r.sync)return upon(resolved(answer),v=>{requireObject(v);throw new TypeError('Delegated iterator has no throw method');});requireObject(answer);}throw new TypeError('Delegated iterator has no throw method');}}
   const result=Reflect.apply(checkCallable(method),r.iterator,[request.value]);
   if(!r.async)return requireObject(result);
   const finish=item=>{requireObject(item);return {done:!!item.done,value:item.value};};
   return r.sync?syncContinuation(r,result,kind!==2):upon(resolved(result),finish);
  };
  if(!r.async)return advance();try{return resolved(advance());}catch(e){return Promise.reject(e);}
 }
 const generatorFrames=new WeakMap();
 const generatorPrototype={next(value){return resumeGenerator(this,0,value);},throw(value){return resumeGenerator(this,1,value);},return(value){return resumeGenerator(this,2,value);},[Symbol.iterator](){return this;}};
 const asyncGeneratorPrototype={next(value){return enqueueGenerator(this,0,value);},throw(value){return enqueueGenerator(this,1,value);},return(value){return enqueueGenerator(this,2,value);},[Symbol.asyncIterator](){return this;}};
 Object.defineProperty(generatorPrototype,Symbol.toStringTag,{value:'Generator',configurable:true});
 Object.defineProperty(asyncGeneratorPrototype,Symbol.toStringTag,{value:'AsyncGenerator',configurable:true});
 function frame(id,e,self,args,target){return {id,pc:0,slots:[e,self,args,target,...metadata[id].frameTypes.slice(4).map(t=>t===0x6f?null:0)],input:{kind:0,value:undefined},running:false,closed:false,started:false};}
 function execute(f,kind,value){if(f.running)throw new TypeError('Generator is already running');f.running=true;f.input={kind,value};try{return callCompiled(f.id,f,undefined,undefined,undefined);}finally{f.running=false;}}
 function resumeGenerator(object,kind,value){
  const f=generatorFrames.get(object);if(!f)throw new TypeError('Invalid generator receiver');if(f.running)throw new TypeError('Generator is already running');
  if(f.closed){if(kind===1)throw value;return {value:kind===2?value:undefined,done:true};}
  if(!f.started){if(kind!==0){f.closed=true;if(kind===1)throw value;return {value,done:true};}f.started=true;value=undefined;}
  try{const r=execute(f,kind,value);if(r.kind===3)f.closed=true;if(r.kind===4)return r.value;return {value:r.value,done:r.kind===3};}catch(e){f.closed=true;throw e;}
 }
 function driveAsync(f){return new Promise((resolve,reject)=>{
  const advance=(kind,value)=>{let r;try{r=execute(f,kind,value);}catch(e){reject(e);return;}if(r.kind===3){resolve(r.value);return;}upon(resolved(r.value),v=>advance(0,v),e=>advance(1,e));};advance(0,undefined);
 });}
 function enqueueGenerator(object,kind,value){return new Promise((resolve,reject)=>{
  const f=generatorFrames.get(object);if(!f||!f.queue){reject(new TypeError('Invalid async generator receiver'));return;}
  f.queue.push({kind,value,resolve,reject});drainGenerator(f);
 });}
 function drainGenerator(f){
  if(f.processing||!f.queue.length)return;f.processing=true;const request=f.queue[0];
  const settle=(ok,value,done=false)=>{if(ok)request.resolve({value,done});else request.reject(value);f.queue.shift();f.processing=false;drainGenerator(f);};
  const advance=(kind,value)=>{
   let r;try{r=execute(f,kind,value);}catch(e){f.closed=true;settle(false,e);return;}
   if(r.kind===2){upon(resolved(r.value),v=>advance(0,v),e=>advance(1,e));return;}
   if(r.kind===3){f.closed=true;settle(true,r.value,true);return;}
   upon(resolved(r.value),v=>settle(true,v),e=>advance(1,e));
  };
  if(f.closed||(!f.started&&request.kind!==0)){f.closed=true;if(request.kind===1)settle(false,request.value);else if(request.kind===2)upon(resolved(request.value),v=>settle(true,v,true),e=>settle(false,e));else settle(true,undefined,true);return;}
  if(!f.started){f.started=true;advance(0,undefined);}else if(request.kind===2)upon(resolved(request.value),v=>advance(2,v),e=>advance(1,e));else advance(request.kind,request.value);
 }
 function startResumable(id,e,self,args,target,flags,fn){
  const f=frame(id,e,self,args,target);
  if(flags&16){const first=execute(f,0,undefined);if(first.kind!==0)throw new Error('Missing generator entry suspension');const object=Object.create(fn.prototype);if(flags&32)f.queue=[];generatorFrames.set(object,f);return object;}
  return driveAsync(f);
 }
 // Linked binding records contain names and function IDs, never executable JS.
 environment.initModuleScopes(moduleSpecs.length);
 const modules=moduleSpecs.map((spec,i)=>{const env=environment.scopeNew(root,spec.slots|0);environment.setModuleScope(i,env);return {spec,env,state:0,promise:null,error:null,namespace:null,meta:null};});
 const moduleValue=target=>{if(target.namespace)return namespace(modules[target.module]);const v=environment.scopeGet(modules[target.module].env,target.slot);if(v===null)throw new ReferenceError('Binding is not initialized');return unbox(v);};
 function namespace(record){
  if(record.namespace)return record.namespace;
  const target=Object.create(null),exports=new Map(record.spec.exports.map(e=>[e.name,e.target]));
  for(const name of exports.keys())Object.defineProperty(target,name,{value:undefined,writable:true,enumerable:true,configurable:false});
  Object.defineProperty(target,Symbol.toStringTag,{value:'Module'});Object.preventExtensions(target);
  record.namespace=new Proxy(target,{
   get:(t,k)=>exports.has(k)?moduleValue(exports.get(k)):Reflect.get(t,k),
   getOwnPropertyDescriptor:(t,k)=>{const d=Reflect.getOwnPropertyDescriptor(t,k);if(exports.has(k))d.value=moduleValue(exports.get(k));return d;},
   ownKeys:()=>[...exports.keys(),Symbol.toStringTag],set:()=>false,
   deleteProperty:(t,k)=>!Reflect.has(t,k),
   defineProperty:(t,k,d)=>{if(!exports.has(k))return Reflect.defineProperty(t,k,d);const value=moduleValue(exports.get(k));if(d.configurable===true||d.enumerable===false||d.writable===false||Object.hasOwn(d,'get')||Object.hasOwn(d,'set'))return false;return !Object.hasOwn(d,'value')||Object.is(d.value,value);},
  });return record.namespace;
 }
 function moduleMeta(env){const r=context(env,'module');if(!r.meta){r.meta=Object.create(null);r.meta.url=new URL(r.spec.url??'',globalObject.document?.baseURI||globalObject.location?.href||'file:///project/index.html').href;}return r.meta;}
 function failModule(record,error){for(const r of modules)if(r.spec.group===record.spec.group){r.state=4;r.error=error;}throw error;}
 function evaluateModule(record){
  if(record.state===4)throw record.error;if(record.state===3||record.state===1)return null;if(record.state===2)return record.promise;
  record.state=1;
  try{
   const waits=[];for(const id of record.spec.deps){const p=evaluateModule(modules[id]);if(p){upon(p,undefined,()=>{});waits.push(p);}}
   const run=()=>record.spec.async?driveAsync(frame(record.spec.execute,record.env,undefined,[],undefined)):callCompiled(record.spec.execute,record.env,undefined,[],undefined);
   if(waits.length||record.spec.async){record.state=2;const p=waits.length?upon(Promise.all(waits),run):run();record.promise=upon(resolved(p),()=>{record.state=3;},e=>failModule(record,e));return record.promise;}
   run();record.state=3;return null;
  }catch(e){return failModule(record,e);}
 }
 function runModule(id){try{return resolved(evaluateModule(modules[id]));}catch(e){return Promise.reject(e);}}
 function importModule(id){return upon(resolved(),()=>upon(runModule(id),()=>namespace(modules[id])));}
 function initializeModules(){
  for(const r of modules)info(r.env).module=r;
  for(const r of modules)for(const entry of r.spec.imports)if(entry.target.namespace)environment.scopeSet(r.env,entry.slot,box(namespace(modules[entry.target.module])));
  for(const r of modules)callCompiled(r.spec.init,r.env,undefined,[],undefined);
 }
 const raw={
  lit:i=>pool[i],number:n=>n,boolean:b=>!!b,isNumber:x=>+(typeof x==='number'),toNumber:x=>x,
  truth:v=>+!!v,nullish:v=>+(v==null),isUndefined:v=>+(v===undefined),equal:(a,b)=>+(a===b),
  intToString:null,numberToString:null,stringFromCharCode:null,pushIntrinsic:null,pushStill:null,plainArray:null,isWasmArray:null,defineIndex:null,typeofCode:null,arrayPushFunction:()=>arrayPush,fromInt32:null,scopeNew:null,scopeClone:null,moduleScope:null,toNumeric:null,increment:null,toNumberValue:null,read,write,
  globalRead:k=>{const name=pool[k];if(!(name in globalObject))throw new ReferenceError(name+' is not defined');return globalObject[name];},
  globalTypeof:k=>{const name=pool[k];return name in globalObject?typeof globalObject[name]:'undefined';},
  globalWrite:(k,v,s)=>{const name=pool[k];if(s&&!(name in globalObject))throw new ReferenceError(name+' is not defined');return setProperty(globalObject,name,v,!!s);},
  globalVar:k=>{const name=pool[k];if(!(name in globalObject))Object.defineProperty(globalObject,name,{value:undefined,writable:true,enumerable:true,configurable:false});},
  globalFunction:(k,f)=>{const name=pool[k],d=Object.getOwnPropertyDescriptor(globalObject,name);if(!d||d.configurable)Object.defineProperty(globalObject,name,{value:f,writable:true,enumerable:true,configurable:false});else globalObject[name]=f;},
  globalDelete:k=>Reflect.deleteProperty(globalObject,pool[k]),
  tdz:k=>{throw new ReferenceError("Cannot access '"+pool[k]+"' before initialization");},
  constAssign:()=>{throw new TypeError('Assignment to constant variable.');},
  scriptError:k=>{throw new SyntaxError("Identifier '"+pool[k]+"' has already been declared");},
  key:toKey,property:(o,k,s)=>{if(o==null)throw new TypeError('Cannot access '+keyText(k)+' of '+o);return ref(o,k,!!s);},
  remove:r=>{r=propertyReference(r);if(r.super)throw new ReferenceError('Cannot delete super property');const ok=Reflect.deleteProperty(Object(r.object),r.key);if(!ok&&r.strict)throw new TypeError('Cannot delete property');return ok;},
  typeOf:r=>typeof read(r),
  update:(r,d,post)=>{const v=read(r);let n=toNumeric(v);const previous=n;n=typeof n==='bigint'?n+BigInt(d):n+d;write(r,n);return post?previous:n;},
  unary:(op,v)=>{switch(pool[op]){case'+':return +v;case'-':return -v;case'!':return !v;case'~':return ~v;case'void':return undefined;case'typeof':return typeof v;}throw new Error('Unknown unary operator');},
  templateString:v=>{if(typeof v==='symbol')throw new TypeError('Cannot convert Symbol to string');return String(v);},
  binary:(op,a,b)=>{switch(pool[op]){case'+':return a+b;case'-':return a-b;case'*':return a*b;case'/':return a/b;case'%':return a%b;case'**':return a**b;case'===':return a===b;case'!==':return a!==b;case'==':return a==b;case'!=':return a!=b;case'<':return a<b;case'>':return a>b;case'<=':return a<=b;case'>=':return a>=b;case'&':return a&b;case'|':return a|b;case'^':return a^b;case'<<':return a<<b;case'>>':return a>>b;case'>>>':return a>>>b;case'in':return a in b;case'instanceof':return a instanceof b;}throw new Error('Unknown binary operator');},
  array:()=>[],push:(a,v)=>{a.push(v);return a;},hole:a=>{a.length++;return a;},spread:(a,v)=>{a.push(...v);return a;},
  object:()=>({}),assign:(o,v)=>{if(v==null)return o;for(const k of Reflect.ownKeys(Object(v)))if(Object.getOwnPropertyDescriptor(Object(v),k).enumerable)Object.defineProperty(o,k,{value:v[k],writable:true,enumerable:true,configurable:true});return o;},
  define:(o,k,v,kind)=>{if(kind===3){if(v===null||typeof v==='object'||typeof v==='function')Object.setPrototypeOf(o,v);return o;}if(functions.get(v)?.method)setHome(v,o);const d=kind===1?{get:v}:kind===2?{set:v}:{value:v,writable:true};Object.defineProperty(o,k,{...d,enumerable:true,configurable:true});return o;},
  regex:(p,f)=>new RegExp(pool[p],pool[f]),func:null,
  prepare,invoke,
  stringBuiltins:mask=>+stringMethods.every((name,i)=>!(mask&(1<<i))||stringIntrinsics[i]!==null&&getDescriptor(String.prototype,name)?.value===stringIntrinsics[i]),
  call:(f,self,a)=>Reflect.apply(checkCallable(f),self,a),construct:(f,a)=>Reflect.construct(checkCallable(f),a),
  arg:(a,i)=>a[i],rest:(a,i)=>a.slice(i),
  arguments:argumentsObject,iterator,next:r=>step(r),done:r=>+r.done,value:r=>r.value,
  // for-in: keys deleted before they are reached are skipped (as V8 does).
  keys:v=>{const a=[];for(const k in v)a.push(k);const o=Object(v);let i=0;const it={next(){while(i<a.length){const k=a[i++];if(k in o)return {value:k,done:false};}return {value:undefined,done:true};}};return {iterator:it,next:it.next,done:false};},
  throw:v=>{throw v;},
 };
 Object.assign(raw,{
  moduleMeta,importModule,
  prepareValue:fn=>({fn,self:undefined}),
  setFunctionName:(f,key,kind)=>{const name=keyName(key);Object.defineProperty(f,'name',{value:(kind===1?'get ':kind===2?'set ':'')+name,configurable:true});return f;},callable:r=>r.fn,thisValue,makeClass,classMethod,classField,finishClass,superCall,privateDeclare,privateKey,
  privateProperty:(e,o,i)=>({private:privateKey(e,i),object:o}),privateHas:(e,i,o)=>{requireObject(o);return privateKey(e,i).values.has(o);},
  superProperty:(e,self,key)=>({object:Object.getPrototypeOf(context(e,'home')),key,receiver:thisValue(self),super:true,strict:true}),
  coercible:v=>{if(v==null)throw new TypeError('Cannot destructure null or undefined');return v;},
  objectRest:(v,excluded)=>{const o={};excluded=excluded.map(k=>typeof k==='symbol'?k:String(k));for(const k of Reflect.ownKeys(Object(v)))if(!excluded.includes(k)&&Object.getOwnPropertyDescriptor(Object(v),k)?.enumerable)Object.defineProperty(o,k,{value:v[k],enumerable:true,configurable:true,writable:true});return o;},
  take:r=>step(r).value,skip:r=>{step(r,false);},iteratorRest:r=>{const a=[];for(let item=step(r);!item.done;item=step(r))a.push(item.value);return a;},closeIterator,
  template:(site,index)=>{if(!templateCache.has(site)){const spec=pool[index],cooked=spec.cooked.map(s=>s===null?undefined:s),r=Object.freeze([...spec.raw]);Object.defineProperty(cooked,'raw',{value:r});templateCache.set(site,Object.freeze(cooked));}return templateCache.get(site);},
  pause:()=>{throw new Error('Unlowered suspension');},resumeKind:r=>r.kind,resumeValue:r=>r.value,
  request:(kind,value)=>({kind,value}),delegate,delegateStep,asyncIterator,asyncNext,asyncClose,
  frameRef:(f,i)=>f.slots[i],frameInt:(f,i)=>f.slots[i]||0,frameFloat:(f,i)=>f.slots[i]??0,
  putRef:(f,i,v)=>{f.slots[i]=v;},putInt:(f,i,v)=>{f.slots[i]=v;},putFloat:(f,i,v)=>{f.slots[i]=v;},
  framePC:f=>f.pc,frameInput:f=>f.input,framePause:(f,value,kind,pc)=>{f.pc=pc;return {kind,value};},frameDone:(f,value)=>({kind:3,value}),
 });
 const imports={error:errorTag};
 const nativeNames=new Set(['func','rest','intToString','numberToString','stringFromCharCode','pushIntrinsic','pushStill','plainArray','isWasmArray','defineIndex','typeofCode','fromInt32','scopeNew','scopeClone','moduleScope','toNumeric','increment','toNumberValue','read','write','lit','number','boolean','isNumber','toNumber','truth','nullish','isUndefined','update','int32','equal','templateString','property','key','object','array','push','hole','arg','remove']);
 const scalarResults=new Set(['truth','nullish','isUndefined','equal','isNumber','toNumber','done','resumeKind','frameInt','frameFloat','framePC','stringBuiltins']);
 for(const[name,fn]of Object.entries(raw))imports[name]=nativeNames.has(name)?environment[name]:bridge(fn,scalarResults.has(name));
 imports.define=environment.objectDefine;
 // A self-hosted built-in answers for its native inside WASM; the native stays
 // the function's identity for the host (closureFunction returns it).
 imports.isWasmObject=environment.isObject;
 imports.registerIntrinsic=boxedBridge((name,c)=>{const native=nativeAt(unbox(name));if(native&&!hostObjects.has(native)){environment.closureSetHost(c,native);hostObjects.set(native,c);}});
 imports.callNative=boxedBridge((c,self,a)=>applyNative(environment.closureHost(c),unbox(self),argumentList(a)));
 imports.thisValue=boxedBridge(v=>thisCells.has(v)?box(thisValue(v)):v);
 imports.coercible=boxedBridge(v=>{if(environment.nullish(v))throw new TypeError('Cannot destructure null or undefined');return v;});
 imports.prepare=boxedBridge(prepare);
 imports.callArray=boxedBridge((fn,self,a)=>{if(isClosure(fn))return environment.closureInvoke(fn,self,a);const f=functions.get(fn);if(f?.invokeBoxed)return f.invokeBoxed(self,a,environment.undefinedValue());return callValue(fn,self,boxedArguments(a));});
 for(let n=0;n<=8;n++)imports['call'+n]=environment['call'+n];
 for(const name of ['getProp','setProp','getIndex','setIndex'])imports[name]=environment[name];imports.prepareValue=boxedBridge(fn=>({fn,self:environment.undefinedValue()}));imports.callable=boxedBridge(r=>r.fn);imports.invoke=boxedBridge(invoke);
 imports.spread=boxedBridge((a,v)=>{for(const value of unbox(v))environment.push(a,box(value));return a;},true);
 imports.typeOf=boxedBridge(r=>{const v=environment.read(r);return box(environment.isObject(v)?'object':isClosure(v)?'function':typeof unbox(v));});
 imports.unary=boxedBridge((op,v)=>pool[op]==='typeof'&&(environment.isObject(v)||isClosure(v))?box(environment.isObject(v)?'object':'function'):box(raw.unary(op,unbox(v))),true);

 imports.frameRef=boxedBridge((f,i)=>box(f.slots[i]));imports.putRef=boxedBridge((f,i,v)=>{f.slots[i]=v;});
 imports.int32=environment.int32;imports.has=environment.has;
 // Numeric helpers of the typed tier take and return f64: no boxing at all.
 imports.fmod=(a,b)=>a%b;imports.pow=(a,b)=>a**b;
 // Typed array elements for the typed tier: plain JS numbers both ways.
 imports.taGetF=(a,i)=>a[i];imports.taGetI=(a,i)=>a[i]|0;imports.taSetF=(a,i,v)=>{a[i]=v;};imports.taSetI=(a,i,v)=>{a[i]=v;};imports.taLength=a=>a.length;
 for(const name of ['random','atan2','pow','acos','acosh','asin','asinh','atan','atanh','cbrt','cos','cosh','exp','expm1','log','log10','log1p','log2','sin','sinh','tan','tanh'])imports['math_'+name]=Math[name];
 for(const name of ['isString','stringLength','stringConcat','stringCompare','stringRead','stringCharCodeAt','stringCharAt','stringSlice','stringSubstring','stringIndexOf','stringLastIndexOf','stringIncludes','stringStartsWith','stringEndsWith','stringRepeat'])imports[name]=environment[name];
 const bytes=typeof binary==='string'?Uint8Array.from(atob(binary),c=>c.charCodeAt(0)):binary;
 instance=new WebAssembly.Instance(new WebAssembly.Module(bytes),{r:imports});
 for(let id=0;id<metadata.length;id++)table.set(id,instance.exports['f'+id]);
 metadata.forEach((m,id)=>{if(m.prelude)callCompiled(id,root,globalObject,[],undefined);});
 initializeModules();
 const handlers=new Map();
 return {run:id=>callCompiled(id,root,globalObject,[],undefined),module:runModule,importModule,event:(id,self,event)=>{if(!handlers.has(id)){let e=root;const fn=function(event){return callCompiled(id,e,this,[event],undefined);};if(metadata[id].usesArguments){e=environment.scopeNew(root,1);environment.scopeSet(e,1,fn);info(e).callee=fn;}handlers.set(id,fn);}return Reflect.apply(handlers.get(id),self,[event]);},exports:instance.exports};
}

exports["boot"]=boot;

// Self-hosted built-ins: library functions written in JS and compiled to WASM
// together with the program, the way IL2CPP compiles the base class library
// for Unity. Without them, arr.map(f) on a WASM array calls the native method:
// the array and every object reachable from it become host objects, and each
// later access to them crosses into JS.
//
// Each function is registered for a native (`$$register`): inside WASM the
// native's value is this closure, for the host the closure is the native.
// The fast path runs only for a plain WASM array ($$plainArray: not
// materialized, prototypes without index properties, original species); any
// other receiver gets the native ($$native), with the arguments as given.
// The code follows the specification steps, so callbacks that change the
// array, its prototype or materialize it see exactly what they would in JS.
//
// Intrinsics (only here): $$register(path, fn), $$native(fn, self, args),
// $$call(f, self, ...args), $$plainArray(o), $$isWasmArray(o), $$isWasmObject(o),
// $$defineIndex(o, index, value) (CreateDataPropertyOrThrow).
//
// The prelude is assembled from parts: each part is a function whose body is
// pasted into one strict IIFE, so parts share the helpers defined in `core`.
// A new part lives in its own module (38-prelude-<name>.js exporting PART) and
// is added to PARTS below.
function core(){
 const toInteger=v=>{const n=+v;return n!==n||n===0?0:Math.trunc(n);};
 const notCallable=()=>{throw new TypeError('callback is not a function');};

 function forEach(callbackfn,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(forEach,O,[callbackfn,thisArg]);
  const len=O.length;
  if(typeof callbackfn!=='function')notCallable();
  for(let k=0;k<len;k++)if(k in O)$$call(callbackfn,thisArg,O[k],k,O);
  return undefined;
 }
 function map(callbackfn,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(map,O,[callbackfn,thisArg]);
  const len=O.length;
  if(typeof callbackfn!=='function')notCallable();
  const A=[];A.length=len;
  for(let k=0;k<len;k++)if(k in O)$$defineIndex(A,k,$$call(callbackfn,thisArg,O[k],k,O));
  return A;
 }
 function filter(callbackfn,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(filter,O,[callbackfn,thisArg]);
  const len=O.length;
  if(typeof callbackfn!=='function')notCallable();
  const A=[];let to=0;
  for(let k=0;k<len;k++)if(k in O){const kValue=O[k];if($$call(callbackfn,thisArg,kValue,k,O)){$$defineIndex(A,to,kValue);to++;}}
  return A;
 }
 function some(callbackfn,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(some,O,[callbackfn,thisArg]);
  const len=O.length;
  if(typeof callbackfn!=='function')notCallable();
  for(let k=0;k<len;k++)if(k in O&&$$call(callbackfn,thisArg,O[k],k,O))return true;
  return false;
 }
 function every(callbackfn,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(every,O,[callbackfn,thisArg]);
  const len=O.length;
  if(typeof callbackfn!=='function')notCallable();
  for(let k=0;k<len;k++)if(k in O&&!$$call(callbackfn,thisArg,O[k],k,O))return false;
  return true;
 }
 // find/findIndex/findLast/findLastIndex visit holes as undefined.
 function find(predicate,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(find,O,[predicate,thisArg]);
  const len=O.length;
  if(typeof predicate!=='function')notCallable();
  for(let k=0;k<len;k++){const kValue=O[k];if($$call(predicate,thisArg,kValue,k,O))return kValue;}
  return undefined;
 }
 function findIndex(predicate,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(findIndex,O,[predicate,thisArg]);
  const len=O.length;
  if(typeof predicate!=='function')notCallable();
  for(let k=0;k<len;k++)if($$call(predicate,thisArg,O[k],k,O))return k;
  return -1;
 }
 function findLast(predicate,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(findLast,O,[predicate,thisArg]);
  const len=O.length;
  if(typeof predicate!=='function')notCallable();
  for(let k=len-1;k>=0;k--){const kValue=O[k];if($$call(predicate,thisArg,kValue,k,O))return kValue;}
  return undefined;
 }
 function findLastIndex(predicate,thisArg){
  const O=this;
  if(!$$plainArray(O))return $$native(findLastIndex,O,[predicate,thisArg]);
  const len=O.length;
  if(typeof predicate!=='function')notCallable();
  for(let k=len-1;k>=0;k--)if($$call(predicate,thisArg,O[k],k,O))return k;
  return -1;
 }
 // The number of arguments matters for fromIndex: rest parameters keep it.
 function indexOf(...args){
  const O=this;
  if(!$$plainArray(O))return $$native(indexOf,O,args);
  const searchElement=args[0],len=O.length;
  if(len===0)return -1;
  let n=args.length>1?toInteger(args[1]):0;
  if(n===Infinity)return -1;
  if(n===-Infinity)n=0;
  for(let k=n>=0?n:Math.max(len+n,0);k<len;k++)if(k in O&&O[k]===searchElement)return k;
  return -1;
 }
 function lastIndexOf(...args){
  const O=this;
  if(!$$plainArray(O))return $$native(lastIndexOf,O,args);
  const searchElement=args[0],len=O.length;
  if(len===0)return -1;
  const n=args.length>1?toInteger(args[1]):len-1;
  if(n===-Infinity)return -1;
  for(let k=n>=0?Math.min(n,len-1):len+n;k>=0;k--)if(k in O&&O[k]===searchElement)return k;
  return -1;
 }
 // SameValueZero, and holes read as undefined.
 function includes(...args){
  const O=this;
  if(!$$plainArray(O))return $$native(includes,O,args);
  const searchElement=args[0],len=O.length;
  if(len===0)return false;
  let n=args.length>1?toInteger(args[1]):0;
  if(n===Infinity)return false;
  if(n===-Infinity)n=0;
  const nan=searchElement!==searchElement;
  for(let k=n>=0?n:Math.max(len+n,0);k<len;k++){const e=O[k];if(e===searchElement||nan&&e!==e)return true;}
  return false;
 }
 function slice(start,end){
  const O=this;
  if(!$$plainArray(O))return $$native(slice,O,[start,end]);
  const len=O.length,relativeStart=toInteger(start);
  let k=relativeStart<0?Math.max(len+relativeStart,0):Math.min(relativeStart,len);
  const relativeEnd=end===undefined?len:toInteger(end);
  const final=relativeEnd<0?Math.max(len+relativeEnd,0):Math.min(relativeEnd,len);
  // valueOf of start/end may have changed the array or its species: the
  // native then continues with the converted (side-effect free) numbers.
  if(!$$plainArray(O))return $$native(slice,O,[relativeStart,relativeEnd]);
  const A=[];let n=0;
  for(;k<final;k++,n++)if(k in O)$$defineIndex(A,n,O[k]);
  A.length=n;
  return A;
 }
 // Only arrays of primitives join here: element toString calls (and V8's
 // cycle detection for nested arrays) stay with the native.
 function join(separator){
  const O=this;
  if(!$$plainArray(O)||separator!==undefined&&typeof separator!=='string')return $$native(join,O,[separator]);
  const len=O.length;
  for(let k=0;k<len;k++){const e=O[k];if(e!==null&&typeof e!=='undefined'&&typeof e!=='string'&&typeof e!=='number'&&typeof e!=='boolean')return $$native(join,O,[separator]);}
  const sep=separator===undefined?',':separator;
  let R='';
  for(let k=0;k<len;k++){if(k>0)R+=sep;const e=O[k];if(e!==undefined&&e!==null)R+=e;}
  return R;
 }
 function isArray(arg){
  if($$isWasmObject(arg))return $$isWasmArray(arg);
  return $$native(isArray,undefined,[arg]);
 }

 $$register('Array.prototype.forEach',forEach);
 $$register('Array.prototype.map',map);
 $$register('Array.prototype.filter',filter);
 $$register('Array.prototype.some',some);
 $$register('Array.prototype.every',every);
 $$register('Array.prototype.find',find);
 $$register('Array.prototype.findIndex',findIndex);
 $$register('Array.prototype.findLast',findLast);
 $$register('Array.prototype.findLastIndex',findLastIndex);
 $$register('Array.prototype.indexOf',indexOf);
 $$register('Array.prototype.lastIndexOf',lastIndexOf);
 $$register('Array.prototype.includes',includes);
 $$register('Array.prototype.slice',slice);
 $$register('Array.prototype.join',join);
 $$register('Array.isArray',isArray);
}

const PARTS=[core];
const body=f=>{const s=f.toString();return s.slice(s.indexOf('{')+1,s.lastIndexOf('}'));};
exports["PRELUDE"]="(function(){'use strict';\n"+PARTS.map(body).join('\n')+'\n})();';

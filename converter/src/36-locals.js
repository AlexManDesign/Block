// Local slot compaction: temporaries whose live ranges do not overlap share
// one WASM local. Engines compile a function in time that grows with
// locals x control-flow merges, so a top-level script with thousands of
// temporaries took over a second to compile before this pass.
//
// A local qualifies when every read is dominated by a write. In structured
// control flow, an instruction p dominates a later instruction q when p's
// enclosing frame (block, loop, if/else arm, try/catch arm) is q's frame or
// one of its ancestors: branches only leave frames forward to an enclosing
// end, or go back to the start of an enclosing loop. A qualifying local is
// live from its first write to its last access; a range that starts before a
// loop and reaches into it is extended to the loop's end, because the back
// edge reads it again. Every other local keeps a slot of its own.
const u32=require("./wasm.mjs")["u32"];

const NO_IMMEDIATE=new Set([0x00,0x01,0x05,0x0b,0x0f,0x19,0x1a,0x1b,0xd1,0xd3,0xd4]);
const SINGLE_BYTE_TYPES=new Set([0x40,0x7f,0x7e,0x7d,0x7c,0x7b,0x70,0x6f,0x6e,0x6d,0x6c,0x6b,0x6a,0x69,0x71,0x72,0x73,0x74]);

// Splits a function body (without its final end) into instructions:
// {start, end, op, local, kind (1 read, 2 write), frame}.
function decode(code){
 let i=0;
 const leb=()=>{let v=0,s=0,b;do{b=code[i++];if(b===undefined)throw new Error('truncated');v+=(b&127)*2**s;s+=7;}while(b&128);return v;};
 const sleb=()=>{let b;do{b=code[i++];if(b===undefined)throw new Error('truncated');}while(b&128);};
 const blockType=()=>{const t=code[i];if(SINGLE_BYTE_TYPES.has(t)){i++;return;}if(t===0x63||t===0x64){i++;sleb();return;}sleb();};
 const heapType=()=>sleb();
 const frames=[{parent:-1,depth:0}],stack=[0],loops=[],loopOf=new Map(),out=[];
 const open=kind=>{const f=frames.length;frames.push({parent:stack.at(-1),depth:stack.length,kind});stack.push(f);return f;};
 const sibling=()=>{const old=stack.pop();const f=frames.length;frames.push({parent:frames[old].parent,depth:frames[old].depth,kind:frames[old].kind});stack.push(f);};
 while(i<code.length){
  const start=i,op=code[i++];let local=-1,kind=0,frame=stack.at(-1);
  if(op===0x02||op===0x03||op===0x04||op===0x06){blockType();const f=open(op);if(op===0x03){const loop={start:out.length,end:-1};loops.push(loop);loopOf.set(f,loop);}}
  else if(op===0x05||op===0x07||op===0x19){if(op===0x07)leb();if(stack.length<2)throw new Error('else outside a block');sibling();}
  else if(op===0x0b||op===0x18){if(op===0x18)leb();if(stack.length<2)throw new Error('end outside a block');const f=stack.pop();const loop=loopOf.get(f);if(loop)loop.end=out.length;}
  else if(op>=0x20&&op<=0x22){local=leb();kind=op===0x20?1:2;}
  else if(NO_IMMEDIATE.has(op)||op>=0x45&&op<=0xc4){}
  else if(op===0x0c||op===0x0d||op===0x10||op===0x12||op===0x14||op===0x15||op===0x08||op===0x09||op===0x23||op===0x24||op===0x25||op===0x26||op===0xd2||op===0xd5||op===0xd6||op===0x3f||op===0x40)leb();
  else if(op===0x0e){const n=leb();for(let k=0;k<=n;k++)leb();}
  else if(op===0x11||op===0x13)(leb(),leb());
  else if(op===0x1c){const n=leb();i+=n;}
  else if(op>=0x28&&op<=0x3e)(leb(),leb());
  else if(op===0x41||op===0x42)sleb();
  else if(op===0x43)i+=4;
  else if(op===0x44)i+=8;
  else if(op===0xd0)heapType();
  else if(op===0xfb){
   const sub=leb();
   if([0,1,6,7,11,12,13,14,16].includes(sub))leb();
   else if([2,3,4,5,8,9,10,17,18,19].includes(sub))(leb(),leb());
   else if(sub>=20&&sub<=23)heapType();
   else if(sub===24||sub===25){i++;leb();heapType();heapType();}
   else if(sub===15||sub>=26&&sub<=30){}
   else throw new Error('unknown GC opcode '+sub);
  }
  else if(op===0xfc){
   const sub=leb();
   if(sub<=7){}else if(sub===9||sub===11||sub===13||sub>=15&&sub<=17)leb();else if(sub===8||sub===10||sub===12||sub===14)(leb(),leb());else throw new Error('unknown 0xfc opcode '+sub);
  }
  else throw new Error('unknown opcode 0x'+op.toString(16));
  out.push({start,end:i,op,local,kind,frame});
 }
 if(stack.length!==1)throw new Error('unterminated block');
 return {instructions:out,frames,loops};
}

function compactLocals(fn){
 const params=fn.params.length,count=fn.locals.length;
 if(count<2)return fn;
 let decoded;try{decoded=decode(fn.code);}catch(e){return fn;}
 const {instructions,frames,loops}=decoded;
 const ancestor=(a,b)=>{const depth=frames[a].depth;while(frames[b].depth>depth)b=frames[b].parent;return a===b;};
 const info=Array.from({length:count},()=>({defs:[],uses:[]}));
 instructions.forEach((x,index)=>{if(x.local>=params){const l=info[x.local-params];(x.kind===1?l.uses:l.defs).push(index);}});
 const ranges=[],exclusive=[];
 info.forEach((l,k)=>{
  const type=fn.locals[k];
  if(!l.defs.length&&!l.uses.length)return; // never used: dropped
  let temp=l.defs.length>0&&l.defs.length*l.uses.length<=20000;
  if(temp)for(const u of l.uses){if(!l.defs.some(d=>d<u&&ancestor(instructions[d].frame,instructions[u].frame))){temp=false;break;}}
  if(!temp){exclusive.push(k);return;}
  let start=Math.min(l.defs[0],l.uses[0]??Infinity),end=Math.max(l.defs.at(-1),l.uses.at(-1)??-1);
  for(let changed=true;changed;){changed=false;for(const loop of loops)if(start<loop.start&&end>=loop.start&&end<loop.end){end=loop.end;changed=true;}}
  ranges.push({k,type,start,end});
 });
 // Linear scan: a slot is free for a range that starts after its last end.
 const mapping=new Int32Array(count).fill(-1),locals=[];
 for(const k of exclusive){mapping[k]=params+locals.length;locals.push(fn.locals[k]);}
 const slots=[];
 ranges.sort((a,b)=>a.start-b.start);
 for(const r of ranges){
  let slot=slots.find(s=>s.type===r.type&&s.end<r.start);
  if(!slot){slot={type:r.type,index:params+locals.length,end:-1};locals.push(r.type);slots.push(slot);}
  slot.end=r.end;mapping[r.k]=slot.index;
 }
 if(locals.length>=count)return fn;
 const code=[];let at=0;
 for(const x of instructions){
  if(x.local<params)continue;
  for(;at<x.start;at++)code.push(fn.code[at]);
  code.push(fn.code[x.start],...u32(mapping[x.local-params]));at=x.end;
 }
 for(;at<fn.code.length;at++)code.push(fn.code[at]);
 return {...fn,code,locals};
}

// Stack guard: V8 does not let WASM catch its own stack overflow, so compiled
// functions count their frames in global 0 (in 8-byte units: a Liftoff frame
// is about 7 units plus one per parameter and local) and throw a catchable
// RangeError at the limit in global 1, which the runtime calibrates at boot.
// Returns become branches to one exit that subtracts the frame again; catch
// handlers restore the count of their own frame. Functions that call nothing
// but pure helpers (no way back into compiled code) are not counted.
function guardStack(fn,importCount,pure,overflow){
 if(fn.code.length<=1)return fn;
 let decoded;try{decoded=decode(fn.code);}catch(e){return fn;}
 const {instructions,frames}=decoded;
 const callsOut=instructions.some(x=>x.op===0x11||x.op===0x13||x.op===0x12||x.op===0x10&&!pure.has(calleeOf(fn.code,x.start)));
 if(!callsOut)return fn;
 const weight=7+fn.params.length+fn.locals.length,catches=instructions.some(x=>x.op===0x07||x.op===0x19);
 const saved=fn.params.length+fn.locals.length,locals=catches?[...fn.locals,0x7f]:fn.locals;
 const leb=n=>{const a=[];do{let b=n&127;n>>>=7;if(n)b|=128;a.push(b);}while(n);return a;},sleb=n=>{const a=[];for(;;){const b=n&127;n>>=7;if(n===0&&!(b&64)||n===-1&&(b&64)){a.push(b);return a;}a.push(b|128);}};
 const code=[0x23,0,0x41,...sleb(weight),0x6a,0x24,0,0x23,0,0x23,1,0x4b,0x04,0x40,0x10,...leb(overflow),0x00,0x0b];
 if(catches)code.push(0x23,0,0x21,...leb(saved));
 code.push(0x02,fn.results[0]);
 for(const x of instructions){
  if(x.op===0x0f){code.push(0x0c,...leb(frames[x.frame].depth));continue;}
  for(let i=x.start;i<x.end;i++)code.push(fn.code[i]);
  if(x.op===0x07||x.op===0x19)code.push(0x20,...leb(saved),0x24,0);
 }
 code.push(0x0b,0x23,0,0x41,...sleb(weight),0x6b,0x24,0);
 return {...fn,code,locals};
}
function calleeOf(code,start){let i=start+1,v=0,s=0,b;do{b=code[i++];v+=(b&127)*2**s;s+=7;}while(b&128);return v;}

exports["compactLocals"]=compactLocals;
exports["guardStack"]=guardStack;

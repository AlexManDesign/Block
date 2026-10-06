// Compile structured WASM into a resumable WASM control-flow graph.
// Neither the graph nor the source AST is interpreted by the browser runtime.
const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const F64=require("./wasm.mjs")["F64"];
const u32=require("./wasm.mjs")["u32"];
const i32=require("./wasm.mjs")["i32"];

function decode(bytes){
 let cursor=0;
 function leb(signed=false){let value=0,shift=0,b;do{b=bytes[cursor++];value|=(b&127)<<shift;shift+=7;}while(b&128);if(signed&&shift<32&&(b&64))value|=(-1<<shift);return signed?value:value>>>0;}
 function list(){const nodes=[];while(cursor<bytes.length){const start=cursor,op=bytes[cursor++];if([0x0b,0x05,0x07].includes(op)){if(op===0x07)leb();return {nodes,stop:op};}
  if([0x02,0x03,0x04,0x06].includes(op)){const type=bytes[cursor++],first=list();let other=[];if(first.stop===0x05||first.stop===0x07)other=list().nodes;nodes.push({op,type,body:first.nodes,other});continue;}
  let arg,sub;if([0x10,0x20,0x21,0x22,0x0c].includes(op))arg=leb();else if(op===0x41)arg=leb(true);
  else if(op===0x44)cursor+=8;else if(op===0xd0)leb();
  else if(op===0xfb){sub=leb();if([0x0b,0x0c,0x0d,0x0e,0x16,0x17,0x14,0x15].includes(sub))arg=leb();else if(![0x0f,0x1a,0x1b].includes(sub))throw new Error('Unknown lowering GC opcode: 0x'+sub.toString(16));}
  else if(![0xd1,0x00,0x0f,0x1a,0x45,0x46,0x47,0x48,0x4a,0x4c,0x4e,0x71,0x72,0x73,0x74,0x75,0x76,0x61,0x62,0x63,0x64,0x65,0x66,0x9a,0xa0,0xa1,0xa2,0xa3,0xb7,0xb8].includes(op))throw new Error('Unknown lowering opcode: 0x'+op.toString(16));
  nodes.push({op,arg,sub,bytes:bytes.slice(start,cursor)});
 }return {nodes};}return list().nodes;
}

function lowerResumable(body,compiler){
 const original=[REF,REF,REF,REF,...body.locals],blocks=[];
 const block=(stack,handler)=>{const b={id:blocks.length,entry:[...stack],stack:[...stack],code:[],handler,term:null,reachable:false};blocks.push(b);return b;};
 const edge=(from,to,height=to.entry.length,result=0)=>{if(!from)return;from.term={kind:'goto',to,height,result};if(from.reachable)to.reachable=true;};
 const pop=(b,t)=>{const actual=b.stack.pop();if(actual!==t)throw new Error(`Lowering type mismatch in block ${b.id}: ${actual} / ${t}`);};
 function compile(nodes,current,labels,handler){
  for(const n of nodes){if(!current)break;
   if([0x02,0x03,0x04,0x06].includes(n.op)){
    if(n.op===0x04)pop(current,I32);
    const prefix=[...current.stack],result=n.type===0x40?[]:[n.type],end=block([...prefix,...result],handler);
    if(n.op===0x04){const yes=block(prefix,handler),no=block(prefix,handler);yes.reachable=no.reachable=current.reachable;current.term={kind:'if',yes,no};const nested=[...labels,{to:end,height:prefix.length,result:result.length}];edge(compile(n.body,yes,nested,handler),end);edge(compile(n.other,no,nested,handler),end);}
    else if(n.op===0x06){const caught=block([...prefix,REF],handler);caught.reachable=current.reachable;const h={to:caught,height:prefix.length},start=block(prefix,h);edge(current,start);const nested=[...labels,{to:end,height:prefix.length,result:result.length}];edge(compile(n.body,start,nested,h),end);edge(compile(n.other,caught,nested,handler),end);}
    else if(n.op===0x03){const start=block(prefix,handler);edge(current,start);edge(compile(n.body,start,[...labels,{to:start,height:prefix.length,result:0}],handler),end);}
    else edge(compile(n.body,current,[...labels,{to:end,height:prefix.length,result:result.length}],handler),end);
    current=end.reachable?end:null;continue;
   }
   if(n.op===0x0c){const label=labels[labels.length-1-n.arg];if(!label)throw new Error('Invalid lowering branch');edge(current,label.to,label.height,label.result);current=null;continue;}
   if(n.op===0x0f){pop(current,REF);current.term={kind:'return'};current=null;continue;}
   if(n.op===0x00){current.term={kind:'trap'};current=null;continue;}
   if(n.op===0x10){const imp=compiler.imports[n.arg];for(const t of [...imp.params].reverse())pop(current,t);if(imp.name==='pause'){const next=block([...current.stack,REF],handler);next.resume=true;next.reachable=current.reachable;current.term={kind:'pause',next};current=next;continue;}current.stack.push(...imp.results);}
   else if(n.op===0x20)current.stack.push(original[n.arg]);
   else if(n.op===0x21)pop(current,original[n.arg]);
   else if(n.op===0x22){pop(current,original[n.arg]);current.stack.push(original[n.arg]);}
   else if(n.op===0x41)current.stack.push(I32);
   else if(n.op===0x44)current.stack.push(F64);
   else if(n.op===0xd0)current.stack.push(REF);
   else if(n.op===0xd1){pop(current,REF);current.stack.push(I32);}
   else if(n.op===0xfb){
    if(n.sub===0x1a){pop(current,REF);current.stack.push('any');}
    else if(n.sub===0x1b){current.stack.pop();current.stack.push(REF);}
    else if(n.sub===0x16||n.sub===0x17){pop(current,'any');current.stack.push('ref'+n.arg);}
    else if(n.sub===0x14||n.sub===0x15){pop(current,'any');current.stack.push(I32);}
    else if(n.sub>=0x0b&&n.sub<=0x0d){pop(current,I32);current.stack.pop();current.stack.push(REF);}
    else if(n.sub===0x0e){pop(current,REF);pop(current,I32);current.stack.pop();}
    else if(n.sub===0x0f){current.stack.pop();current.stack.push(I32);}
   }
   else if(n.op===0x1a){if(!current.stack.length)throw new Error('Lowering stack underflow');current.stack.pop();}
   else if(n.op===0x45){pop(current,I32);current.stack.push(I32);}
   else if([0x46,0x47,0x48,0x4a,0x4c,0x4e,0x71,0x72,0x73,0x74,0x75,0x76].includes(n.op)){pop(current,I32);pop(current,I32);current.stack.push(I32);}
   else if(n.op===0x9a){pop(current,F64);current.stack.push(F64);}
   else if(n.op===0xb7||n.op===0xb8){pop(current,I32);current.stack.push(F64);}
   else if(n.op>=0x61&&n.op<=0x66){pop(current,F64);pop(current,F64);current.stack.push(I32);}
   else if(n.op>=0xa0&&n.op<=0xa3){pop(current,F64);pop(current,F64);current.stack.push(F64);}
   current.code.push(n);
  }return current;
 }
 const entry=block([],null);entry.reachable=true;const last=compile(decode(body.code),entry,[],null);if(last){pop(last,REF);last.term={kind:'return'};}
 const active=blocks.filter(b=>b.reachable),spillKeys=new Map(),spillTypes=[];
 function slot(position,type){if(typeof type==='string')throw new Error('Lowering: GC value crosses a block boundary');const key=position+':'+type;if(!spillKeys.has(key)){spillKeys.set(key,original.length+spillTypes.length);spillTypes.push(type);}return spillKeys.get(key);}
 for(const b of active){b.entry.forEach((t,i)=>slot(i,t));b.stack.forEach((t,i)=>slot(i,t));if(b.handler)slot(b.handler.height,REF);}
 const savedTypes=[...original,...spillTypes],locals=[...savedTypes],local=t=>{const n=4+locals.length;locals.push(t);return n;};
 const pc=local(I32),condition=local(I32),yieldKind=local(I32),yieldValue=local(REF),caught=local(REF),code=[];
 const out=(...b)=>code.push(...b),get=i=>out(0x20,...u32(i)),set=i=>out(0x21,...u32(i)),integer=n=>out(0x41,...i32(n));
 const call=name=>out(0x10,...u32(compiler.import(name)));
 const spill=stack=>{for(let i=stack.length-1;i>=0;i--)set(4+slot(i,stack[i]));};
 const restore=b=>b.entry.forEach((t,i)=>{if(b.resume&&i===b.entry.length-1){get(0);call('frameInput');}else get(4+slot(i,t));});
 function saveFrame(){savedTypes.forEach((t,i)=>{get(0);integer(i);get(4+i);call(t===REF?'putRef':t===I32?'putInt':'putFloat');});}
 savedTypes.forEach((t,i)=>{get(0);integer(i);call(t===REF?'frameRef':t===I32?'frameInt':'frameFloat');set(4+i);});
 get(0);call('framePC');set(pc);out(0x03,0x40);
 for(const b of active){
  get(pc);integer(b.id);out(0x46,0x04,0x40);if(b.handler)out(0x06,0x40);
  restore(b);for(const n of b.code){if([0x20,0x21,0x22].includes(n.op))out(n.op,...u32(4+n.arg));else out(...n.bytes);}
  const t=b.term,depth=b.handler?2:1;
  if(t.kind==='goto'){
   spill(b.stack);if(t.result)for(let i=0;i<t.result;i++){const from=b.stack.length-t.result+i,type=b.stack[from];get(4+slot(from,type));set(4+slot(t.height+i,type));}
   integer(t.to.id);set(pc);out(0x0c,...u32(depth));
  }else if(t.kind==='if'){set(condition);spill(b.stack);get(condition);out(0x04,0x40);integer(t.yes.id);set(pc);out(0x05);integer(t.no.id);set(pc);out(0x0b,0x0c,...u32(depth));}
  else if(t.kind==='return'){set(yieldValue);get(0);get(yieldValue);call('frameDone');out(0x0f);}
  else if(t.kind==='pause'){set(yieldKind);set(yieldValue);spill(b.stack);saveFrame();get(0);get(yieldValue);get(yieldKind);integer(t.next.id);call('framePause');out(0x0f);}
  else out(0x00);
  if(b.handler){out(0x07,0);set(caught);get(caught);set(4+slot(b.handler.height,REF));integer(b.handler.to.id);set(pc);out(0x0c,2,0x0b);}
  out(0x0b);
 }
 out(0x00,0x0b,0x00);
 return {code,locals};
}

exports["lowerResumable"]=lowerResumable;

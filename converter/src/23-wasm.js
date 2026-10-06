// Binary WebAssembly encoder, written for this project.
const REF=0x6f,I32=0x7f,F64=0x7c;
function u32(n){const a=[];do{const b=n&127;n>>>=7;a.push(b|(n?128:0));}while(n);return a;}
function i32(n){const a=[];let more=true;while(more){let b=n&127;n>>=7;more=!((n===0&&!(b&64))||(n===-1&&(b&64)));if(more)b|=128;a.push(b);}return a;}
function utf8(s){const a=[...new TextEncoder().encode(s)];return [...u32(a.length),...a];}
function section(id,b){return [id,...u32(b.length),...b];}
// functions: [{params, results, code, locals}]; exports: [{name, index}]
// (index counts imports first). Locals are single-byte value types.
function makeModule(imports,functions,gcTypes=[],exported=[]){
 const types=[...gcTypes],keys=new Map();
 const type=(params,results)=>{const k=JSON.stringify([params,results]);if(!keys.has(k)){keys.set(k,types.length);types.push([0x60,...u32(params.length),...params,...u32(results.length),...results]);}return keys.get(k);};
 const entries=imports.map(x=>[...utf8('r'),...utf8(x.name),0,...u32(type(x.params,x.results))]);
 entries.push([...utf8('r'),...utf8('error'),4,0,...u32(type([REF],[]))]);
 const signatures=functions.map(f=>u32(type(f.params,f.results)));
 const exports=exported.map(e=>[...utf8(e.name),0,...u32(e.index)]);
 const bodies=functions.map(f=>{const groups=[];for(const t of f.locals){if(groups.at(-1)?.[1]===t)groups.at(-1)[0]++;else groups.push([1,t]);}const b=[...u32(groups.length),...groups.flatMap(([n,t])=>[...u32(n),t]),...f.code,0x0b];return [...u32(b.length),...b];});
 const result=[0,97,115,109,1,0,0,0];
 for(const[id,data]of [[1,[...u32(types.length),...types.flat()]],[2,[...u32(entries.length),...entries.flat()]],[3,[...u32(functions.length),...signatures.flat()]],[7,[...u32(exports.length),...exports.flat()]],[10,[...u32(bodies.length),...bodies.flat()]]])for(const b of section(id,data))result.push(b);
 const binary=new Uint8Array(result);
 if(!WebAssembly.validate(binary)){let detail='';try{new WebAssembly.Module(binary);}catch(e){detail=': '+e.message;}throw new Error('Внутренняя ошибка: получен некорректный WASM'+detail);}
 return binary;
}

exports["REF"]=REF;
exports["I32"]=I32;
exports["F64"]=F64;
exports["u32"]=u32;
exports["i32"]=i32;
exports["utf8"]=utf8;
exports["section"]=section;
exports["makeModule"]=makeModule;

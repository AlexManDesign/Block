const parse=require("acorn")["parse"];
const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const F64=require("./wasm.mjs")["F64"];
const u32=require("./wasm.mjs")["u32"];
const i32=require("./wasm.mjs")["i32"];
const makeModule=require("./wasm.mjs")["makeModule"];
const features=require("./compiler-features.mjs")["features"];
const featureSignatures=require("./compiler-features.mjs")["featureSignatures"];
const lowerResumable=require("./resumable.mjs")["lowerResumable"];
const specializeNumeric=require("./numeric.mjs")["specializeNumeric"];
const environmentWasm=require("./environment-wasm.mjs")["environmentWasm"];
const stringTypes=require("./string-wasm.mjs")["stringTypes"];
const base64=require("./encoding.mjs")["base64"];
const ScopeAnalysis=require("./scope.mjs")["ScopeAnalysis"];
const bindings=require("./bindings.mjs")["bindings"];
const reprOf=require("./bindings.mjs")["reprOf"];
const VALUE_TYPE=require("./bindings.mjs")["VALUE_TYPE"];
const TypeInference=require("./types.mjs")["TypeInference"];

const signatures={
 ...featureSignatures,
 lit:['i','r'],number:['f','r'],boolean:['i','r'],isNumber:['r','i'],toNumber:['r','f'],int32:['f','i'],
 truth:['r','i'],nullish:['r','i'],isUndefined:['r','i'],equal:['rr','i'],has:['rr','i'],
 isString:['r','i'],stringLength:['r','i'],stringConcat:['rr','r'],stringCompare:['rr','i'],stringRead:['rr','r'],stringBuiltins:['i','i'],
 stringCharCodeAt:['rf','f'],stringCharAt:['rf','r'],stringSlice:['rff','r'],stringSubstring:['rff','r'],stringIndexOf:['rrf','i'],stringLastIndexOf:['rrf','i'],stringIncludes:['rrf','i'],stringStartsWith:['rrf','i'],stringEndsWith:['rrf','i'],stringRepeat:['rf','r'],
 scopeNew:['ri','r'],scopeClone:['r','r'],moduleScope:['i','r'],
 globalRead:['i','r'],globalTypeof:['i','r'],globalWrite:['iri','r'],globalVar:['i',''],globalFunction:['ir',''],globalDelete:['i','r'],
 tdz:['i',''],constAssign:['i',''],scriptError:['i',''],toNumeric:['r','r'],increment:['ri','r'],
 read:['r','r'],write:['rr','r'],key:['r','r'],property:['rri','r'],remove:['r','r'],typeOf:['r','r'],update:['rii','r'],
 unary:['ir','r'],templateString:['r','r'],binary:['irr','r'],array:['','r'],push:['rr','r'],hole:['r','r'],spread:['rr','r'],
 object:['','r'],assign:['rr','r'],define:['rrri','r'],regex:['ii','r'],func:['riiirr','r'],
 prepare:['r','r'],invoke:['rr','r'],call:['rrr','r'],construct:['rr','r'],arg:['ri','r'],rest:['ri','r'],arguments:['rrii','r'],
 iterator:['r','r'],keys:['r','r'],next:['r','r'],done:['r','i'],value:['r','r'],throw:['r',''],
 setFunctionName:['rri','r'],fromInt32:['i','r'],
 pushIntrinsic:['rr','i'],pushStill:['r','i'],arrayPushFunction:['','r'],intToString:['i','r'],numberToString:['f','r'],stringFromCharCode:['i','r'],taGetF:['rf','f'],taGetI:['rf','i'],taSetF:['rff',''],taSetI:['rfi',''],taLength:['r','f'],getProp:['rr','r'],setProp:['rrri','r'],getIndex:['rf','r'],setIndex:['rfri','r'],callArray:['rrr','r'],
 ...Object.fromEntries(Array.from({length:9},(_,n)=>['call'+n,['rr'+'r'.repeat(n),'r']])),
 toNumberValue:['r','f'],fmod:['ff','f'],pow:['ff','f'],math_random:['','f'],math_atan2:['ff','f'],math_pow:['ff','f'],
 ...Object.fromEntries(['acos','acosh','asin','asinh','atan','atanh','cbrt','cos','cosh','exp','expm1','log','log10','log1p','log2','sin','sinh','tan','tanh'].map(n=>['math_'+n,['f','f']])),
};
const types={r:REF,i:I32,f:F64};
const strictBody=(body,inherited)=>!!(inherited||body?.some?.(n=>n.directive==='use strict'));
function error(n,message){throw new SyntaxError(message+(n?.loc?` (строка ${n.loc.start.line}, столбец ${n.loc.start.column+1})`:''));}

class Compiler {
 constructor({numeric=true,environments=true}={}){
  this.numeric=numeric;this.environments=environments;this.constants=[];this.constantKeys=new Map();this.imports=[];this.importNames=new Map();this.plans=[];this.bodies=[];this.templateCount=0;
  // All imports are declared up front, so function indices are known while emitting.
  for(const name of Object.keys(signatures))this.import(name);
 }
 // Each plan owns two functions: f<id> (generic entry, exported) and t<id>
 // (typed body called directly by compiled code, or a stub).
 genericIndex(id){return this.imports.length+2*id;}
 typedIndex(fn){return this.imports.length+2*this.planOf(fn)+1;}
 planOf(fn){const n=fn.node;if(n._planId===undefined)this.add(n,fn.strict,'function',n.id?.name||'');return n._planId;}
 paramRepr(fn,i){const p=fn.node.params[i];return p.type==='AssignmentPattern'?'r':reprOf(p._ref.binding.type);}
 constant(v){const d=typeof v==='undefined'?{t:'u'}:typeof v==='bigint'?{t:'big',v:String(v)}:typeof v==='number'?{t:'num',v:Object.is(v,-0)?'-0':String(v)}:{t:'v',v};const key=JSON.stringify(d);if(!this.constantKeys.has(key)){this.constantKeys.set(key,this.constants.length);this.constants.push(d);}return this.constantKeys.get(key);}
 import(name){if(!signatures[name])throw new Error('Unknown ABI: '+name);if(!this.importNames.has(name)){const[p,r]=signatures[name];this.importNames.set(name,this.imports.length);this.imports.push({name,params:[...p].map(t=>types[t]),results:[...r].map(t=>types[t])});}return this.importNames.get(name);}
 add(node,strict=false,kind='function',name=''){if(kind==='function'&&node._planId!==undefined)return node._planId;const id=this.plans.length;if(kind==='function')node._planId=id;let arity=node.params?.findIndex(p=>p.type==='AssignmentPattern'||p.type==='RestElement')??0;if(arity<0)arity=node.params.length;this.plans.push({node,kind,name,sourceName:kind==='script'||kind==='handler'?name:this.currentSource||name,strict:strictBody(node.body?.body||node.body,strict),arity});return id;}
 script(source,filename='script'){let ast;try{ast=parse(source,{ecmaVersion:'latest',sourceType:'script',locations:true});}catch(e){throw new SyntaxError(filename+': '+e.message);}return this.add(ast,false,'script',filename);}
 handler(source,name){let ast;try{ast=parse(`function handler(event){\n${source}\n}`,{ecmaVersion:'latest',locations:true});}catch(e){throw new SyntaxError(name+': '+e.message);}return this.add(ast.body[0],false,'handler',name);}
 build(){
  // Resolve every name of the program before emitting any function.
  const analysis=this.analysis=new ScopeAnalysis();
  for(const plan of [...this.plans])analysis.program(plan);
  analysis.finish();
  new TypeInference(analysis).run();
  this.moduleSpecs??=[];
  for(const [id,spec]of this.moduleSpecs.entries()){
   const scope=analysis.modules.get(id),slotOf=(module,local)=>analysis.modules.get(module).bindings.get(local).slot;
   spec.slots=scope.slots;
   for(const entry of spec.imports)entry.slot=scope.bindings.get(entry.local).slot;
   for(const entry of spec.exports)if(!entry.target.namespace)entry.target.slot=slotOf(entry.target.module,entry.target.local);
  }
  const generic=[REF,REF,REF,REF],functions=[];
  for(let id=0;id<this.plans.length;id++){
   const plan=this.plans[id];this.currentSource=plan.sourceName;
   try{
    const fn=analysis.functions.get(plan.node);
    if(plan.kind==='function'&&fn?.typedBody){
     const params=plan.node.params.map((p,i)=>VALUE_TYPE[this.paramRepr(fn,i)]),ret=VALUE_TYPE[reprOf(fn.ret)];
     const typed=new FunctionEmitter(this,id,{typed:true,base:4+params.length,retRepr:reprOf(fn.ret)}).compile();
     functions[2*id]={params:generic,results:[REF],...this.adapter(id,fn)};
     functions[2*id+1]={params:[...generic,...params],results:[ret],...typed};
     plan.typed=true;
     continue;
    }
    const resumable=!!(plan.node.async||plan.node.generator);
    let body=new FunctionEmitter(this,id,{dynamicOnly:resumable}).compile();
    if(resumable){plan.frameTypes=[REF,REF,REF,REF,...body.locals];body=lowerResumable(body,this);}
    else if(this.numeric){const fast=specializeNumeric(this,plan,body);if(fast){body={code:[...fast.code,...body.code],locals:[...body.locals,...fast.locals]};plan.numeric=true;plan.stringSpecialization=fast.stringSpecialization;}}
    functions[2*id]={params:generic,results:[REF],...body};
    functions[2*id+1]={params:generic,results:[REF],code:[0x00],locals:[]};
   }catch(e){e.message=this.currentSource+': '+e.message;throw e;}
  }
  const metadata=this.plans.map(p=>({arity:p.arity,name:p.name,frameTypes:p.frameTypes,numeric:!!p.numeric,stringSpecialization:!!p.stringSpecialization,usesArguments:!!p.usesArguments}));
  if(metadata.length)metadata[0].rootSlots=analysis.root.slots;
  const environmentBinary=this.environments?environmentWasm():null;
  if(environmentBinary&&metadata.length)metadata[0].environmentWasm=base64(environmentBinary);
  // Types 0..2: strings (numeric.mjs relies on these indices), 3: scope record.
  this.functions=functions;
  const exported=this.plans.map((_,id)=>({name:'f'+id,index:this.genericIndex(id)}));
  // Readable names in profiles: f<id>/t<id> plus the source name.
  const names=this.plans.flatMap((p,id)=>{const label=(p.name||p.kind).replace(/[^\w$.-]/g,'_').slice(0,40);return [[this.genericIndex(id),'f'+id+'_'+label],[this.genericIndex(id)+1,'t'+id+'_'+label]];});
  return {binary:makeModule(this.imports,functions,[stringTypes[0],stringTypes[1],stringTypes[3],[0x5e,REF,1]],exported,names),constants:this.constants,metadata,environmentBytes:environmentBinary?.length||0};
 }
 // Generic entry of a typed function: unpack the argument array, call t<id>.
 adapter(id,fn){
  const code=[],out=(...b)=>code.push(...b),call=name=>out(0x10,...u32(this.import(name)));
  out(0x20,0,0x20,1,0x20,2,0x20,3);
  fn.node.params.forEach((p,i)=>{
   out(0x20,2,0x41,...i32(i));call('arg');
   const repr=this.paramRepr(fn,i);if(repr==='f')call('toNumberValue');else if(repr==='n'){call('toNumberValue');call('int32');}else if(repr==='i')call('truth');
  });
  out(0x10,...u32(this.imports.length+2*id+1));
  const ret=reprOf(fn.ret);if(ret==='f')call('number');else if(ret==='n')call('fromInt32');else if(ret==='i')call('boolean');
  return {code,locals:[]};
 }
}

class FunctionEmitter {
 constructor(compiler,id,{typed=false,base=4,retRepr=null,dynamicOnly=false}={}){this.c=compiler;this.id=id;this.plan=compiler.plans[id];this.code=[];this.locals=[];this.labels=[];this.finalizers=[];this.env=0;this.strict=this.plan.strict;this.typed=typed;this.base=base;this.retRepr=retRepr;this.dynamicOnly=dynamicOnly;}
 out(...bytes){this.code.push(...bytes);}
 local(type=REF){const id=this.base+this.locals.length;this.locals.push(type);return id;}
 get(i){this.out(0x20,...u32(i));} set(i){this.out(0x21,...u32(i));} tee(i){this.out(0x22,...u32(i));}
 integer(n){this.out(0x41,...i32(n));} lit(v){this.integer(this.c.constant(v));this.rt('lit');}
 rt(name){this.out(0x10,...u32(this.c.import(name)));}
 label(kind,body,result=0x40,tag=null){this.out(kind==='loop'?0x03:0x02,result);const label={kind,tag};this.labels.push(label);body(label);this.labels.pop();this.out(0x0b);}
 branch(label){const depth=this.labels.length-1-this.labels.lastIndexOf(label);if(depth<0)throw new Error('Invalid compiler branch');this.out(0x0c,...u32(depth));}
 ifElse(yes,no,result=0x40){this.out(0x04,result);this.labels.push({kind:'if'});yes();if(no){this.out(0x05);no();}this.labels.pop();this.out(0x0b);}
 args(nodes){this.rt('array');for(const a of nodes){if(a?.type==='SpreadElement'){this.expression(a.argument);this.rt('spread');}else if(a){this.expression(a);this.rt('push');}else this.rt('hole');}}
 binary(op,left,right){
  const a=this.local(),b=this.local();left();this.set(a);right();this.set(b);
  if(this.c.environments&&(op==='==='||op==='!==')){this.get(a);this.get(b);this.rt('equal');if(op==='!==')this.out(0x45);this.rt('boolean');return;}
  if(this.c.environments&&op==='in'){this.get(b);this.get(a);this.rt('has');this.rt('boolean');return;}
  const comparisons=this.c.environments?{'==':0x61,'===':0x61,'!=':0x62,'!==':0x62,'<':0x63,'>':0x64,'<=':0x65,'>=':0x66}:{};
  const bitwise=this.c.environments?{'&':0x71,'|':0x72,'^':0x73,'<<':0x74,'>>':0x75,'>>>':0x76}:{};
  const numeric={'+':0xa0,'-':0xa1,'*':0xa2,'/':0xa3,...comparisons,...bitwise};
  const dynamic=()=>{this.integer(this.c.constant(op));this.get(a);this.get(b);this.rt('binary');};
  const stringComparisons={'==':0x46,'===':0x46,'!=':0x47,'!==':0x47,'<':0x48,'>':0x4a,'<=':0x4c,'>=':0x4e};
  const fallback=()=>{if(this.c.environments&&(op==='+'||stringComparisons[op])){this.get(a);this.rt('isString');this.get(b);this.rt('isString');this.out(0x71);this.ifElse(()=>{this.get(a);this.get(b);if(op==='+')this.rt('stringConcat');else{this.rt('stringCompare');this.integer(0);this.out(stringComparisons[op]);this.rt('boolean');}},dynamic,REF);}else dynamic();};
  if(numeric[op]){this.get(a);this.rt('isNumber');this.get(b);this.rt('isNumber');this.out(0x71);this.ifElse(()=>{this.get(a);this.rt('toNumber');if(bitwise[op])this.rt('int32');this.get(b);this.rt('toNumber');if(bitwise[op])this.rt('int32');this.out(numeric[op]);if(bitwise[op])this.out(op==='>>>'?0xb8:0xb7);this.rt(comparisons[op]?'boolean':'number');},fallback,REF);}else fallback();
 }
 expression(n){
  switch(n.type){
   case'Literal':if(n.regex){this.integer(this.c.constant(n.regex.pattern));this.integer(this.c.constant(n.regex.flags));this.rt('regex');}else this.lit(n.value);return;
   case'ThisExpression':this.get(1);return;
   case'MetaProperty':if(n.meta.name==='new'&&n.property.name==='target'){this.get(3);return;}break;
   case'MemberExpression':this.reference(n);this.rt('read');return;
   case'FunctionExpression':case'ArrowFunctionExpression':this.function(n);return;
   case'ArrayExpression':this.args(n.elements);return;
   case'ObjectExpression':this.rt('object');for(const p of n.properties){if(p.type==='SpreadElement'){this.expression(p.argument);this.rt('assign');continue;}if(p.computed){this.expression(p.key);this.rt('key');}else this.lit(p.key.name??p.key.value);if(p.method||p.kind==='get'||p.kind==='set')this.function(p.value,p.key.name||'',true);else this.expression(p.value);this.integer(p.kind==='get'?1:p.kind==='set'?2:!p.computed&&!p.shorthand&&!p.method&&(p.key.name??p.key.value)==='__proto__'?3:p.method?4:0);this.rt('define');}return;
   case'TemplateLiteral':this.lit(n.quasis[0].value.cooked);for(let i=0;i<n.expressions.length;i++){const prev=this.local();this.set(prev);this.binary('+',()=>this.get(prev),()=>{this.expression(n.expressions[i]);this.rt('templateString');});const next=this.local();this.set(next);this.binary('+',()=>this.get(next),()=>this.lit(n.quasis[i+1].value.cooked));}return;
   case'BinaryExpression':this.binary(n.operator,()=>this.expression(n.left),()=>this.expression(n.right));return;
   case'LogicalExpression':{const v=this.local();this.expression(n.left);this.tee(v);this.rt(n.operator==='??'?'nullish':'truth');if(n.operator==='||')this.ifElse(()=>this.get(v),()=>this.expression(n.right),REF);else this.ifElse(()=>this.expression(n.right),()=>this.get(v),REF);return;}
   case'ConditionalExpression':this.expression(n.test);this.rt('truth');this.ifElse(()=>this.expression(n.consequent),()=>this.expression(n.alternate),REF);return;
   case'SequenceExpression':n.expressions.forEach((e,i)=>{this.expression(e);if(i<n.expressions.length-1)this.out(0x1a);});return;
   case'UnaryExpression':
    if(n.operator==='delete'){if(['Identifier','MemberExpression'].includes(n.argument.type)){this.reference(n.argument);this.rt('remove');}else{this.expression(n.argument);this.out(0x1a);this.lit(true);}return;}
    if(n.operator==='typeof'&&n.argument.type==='Identifier'){this.reference(n.argument);this.rt('typeOf');return;}
    if(this.c.environments&&n.operator==='!'){this.expression(n.argument);this.rt('truth');this.out(0x45);this.rt('boolean');return;}
    if(this.c.environments&&n.operator==='void'){this.expression(n.argument);this.out(0x1a);this.lit(undefined);return;}
    if(this.c.environments&&['+','-','~'].includes(n.operator)){const v=this.local();this.expression(n.argument);this.tee(v);this.rt('isNumber');this.ifElse(()=>{this.get(v);this.rt('toNumber');if(n.operator==='-')this.out(0x9a);else if(n.operator==='~'){this.rt('int32');this.integer(-1);this.out(0x73,0xb7);}this.rt('number');},()=>{this.integer(this.c.constant(n.operator));this.get(v);this.rt('unary');},REF);return;}
    this.integer(this.c.constant(n.operator));this.expression(n.argument);this.rt('unary');return;
   case'UpdateExpression':this.reference(n.argument);this.integer(n.operator==='++'?1:-1);this.integer(n.prefix?0:1);this.rt('update');return;
   case'AssignmentExpression':{
    const r=this.local();this.reference(n.left);this.set(r);
    if(['&&=','||=','??='].includes(n.operator)){const old=this.local();this.get(r);this.rt('read');this.tee(old);this.rt(n.operator==='??='?'nullish':'truth');const put=()=>{this.get(r);this.expression(n.right);this.rt('write');};if(n.operator==='||=')this.ifElse(()=>this.get(old),put,REF);else this.ifElse(put,()=>this.get(old),REF);return;}
    this.get(r);if(n.operator==='=')this.expression(n.right);else this.binary(n.operator.slice(0,-1),()=>{this.get(r);this.rt('read');},()=>this.expression(n.right));this.rt('write');return;
   }
   case'CallExpression':if(n.optional)break;if(n.callee.type==='Identifier'||n.callee.type==='MemberExpression'){this.reference(n.callee);this.rt('prepare');this.args(n.arguments);this.rt('invoke');}else{this.expression(n.callee);this.lit(undefined);this.args(n.arguments);this.rt('call');}return;
   case'NewExpression':this.expression(n.callee);this.args(n.arguments);this.rt('construct');return;
   case'ChainExpression':error(n,'Optional chaining (?.) ещё не реализован');
   case'AwaitExpression':error(n,'await ещё не реализован');
  }error(n,'Конструкция ещё не реализована: '+n.type);
 }
 statement(n,tag=null){
  switch(n.type){
   case'EmptyStatement':case'DebuggerStatement':case'FunctionDeclaration':return;
   case'ExpressionStatement':if(n.directive)return;this.expression(n.expression);this.out(0x1a);return;
   case'ReturnStatement':if(n.argument)this.expression(n.argument);else this.lit(undefined);this.out(0x0f);return;
   case'ThrowStatement':this.expression(n.argument);this.rt('throw');this.out(0x00);return;
   case'IfStatement':this.expression(n.test);this.rt('truth');this.ifElse(()=>this.statement(n.consequent),n.alternate?()=>this.statement(n.alternate):null);return;
   case'BreakStatement':case'ContinueStatement':{const target=[...this.labels].reverse().find(l=>(n.type==='BreakStatement'?l.kind==='break'||(n.label&&l.kind==='label'):l.kind==='continue')&&(!n.label||l.tag===n.label.name));if(!target)error(n,'Не найдена цель break/continue');this.branch(target);return;}
   case'LabeledStatement':if(['ForStatement','WhileStatement','DoWhileStatement','ForInStatement','ForOfStatement'].includes(n.body.type))this.statement(n.body,n.label.name);else this.label('label',()=>this.statement(n.body),0x40,n.label.name);return;
  }error(n,'Конструкция ещё не реализована: '+n.type);
 }
}

// Layers: base syntax, then advanced syntax, then static bindings on top.
const emitter=FunctionEmitter.prototype;
emitter.baseExpression=emitter.expression;
emitter.baseStatement=emitter.statement;
Object.assign(emitter,features);
Object.assign(emitter,{featureAbrupt:emitter.abrupt,featureExpression:emitter.expression,featureStatement:emitter.statement,featurePattern:emitter.pattern,featureReference:emitter.reference,featureCallTarget:emitter.callTarget,featureChainPart:emitter.chainPart});
Object.assign(emitter,bindings);

exports["Compiler"]=Compiler;

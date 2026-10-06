// A conservative, guarded specialization written for this compiler.
// Pure number/string bodies use WASM locals and own runtime exports without JS
// calls inside their loops. Unsupported functions keep the generic backend.
const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const F64=require("./wasm.mjs")["F64"];
const u32=require("./wasm.mjs")["u32"];
const i32=require("./wasm.mjs")["i32"];
const STRING_METHODS=require("./string-wasm.mjs")["STRING_METHODS"];

class NotNumeric extends Error {}
const arithmetic={'+':0xa0,'-':0xa1,'*':0xa2,'/':0xa3};
const comparisons={'==':0x61,'===':0x61,'!=':0x62,'!==':0x62,'<':0x63,'>':0x64,'<=':0x65,'>=':0x66};
const booleanComparisons={'==':0x46,'===':0x46,'!=':0x47,'!==':0x47,'<':0x48,'>':0x4a,'<=':0x4c,'>=':0x4e};
const RECORD=-1,STRING=-2;
const wasmType=t=>t===STRING?REF:t;
const stringComparisons={'==':0x46,'===':0x46,'!=':0x47,'!==':0x47,'<':0x48,'>':0x4a,'<=':0x4c,'>=':0x4e};
const memberName=n=>!n.computed?n.property.name:n.property.type==='Literal'?String(n.property.value):null;
function stringParameters(node){
 const names=new Set(node.params.filter(p=>p.type==='Identifier').map(p=>p.name)),strings=new Set();
 const mark=n=>{if(n?.type==='Identifier'&&names.has(n.name))strings.add(n.name);};
 const walk=n=>{if(!n||typeof n!=='object')return;
  if(n.type==='MemberExpression'&&(memberName(n)==='length'||STRING_METHODS.includes(memberName(n))))mark(n.object);
  if(n.type==='CallExpression'&&n.callee.type==='MemberExpression'){const method=memberName(n.callee);if(['indexOf','lastIndexOf','includes','startsWith','endsWith'].includes(method))mark(n.arguments[0]);if(method==='concat')n.arguments.forEach(mark);}
  if(n.type==='BinaryExpression'&&(n.operator==='+'||stringComparisons[n.operator])){if(n.left.type==='Literal'&&typeof n.left.value==='string')mark(n.right);if(n.right.type==='Literal'&&typeof n.right.value==='string')mark(n.left);}
  for(const [k,v]of Object.entries(n))if(!['loc','start','end'].includes(k)&&k[0]!=='_'){if(Array.isArray(v))v.forEach(walk);else if(v&&typeof v==='object')walk(v);}
 };walk(node.body);return strings;
}
const bitwise={'&':0x71,'|':0x72,'^':0x73,'<<':0x74,'>>':0x75,'>>>':0x76};
function integerBindings(node){
 const declarations=new Map(),counts=new Map(),writes=[];
 const signed=n=>n?.type==='Literal'&&typeof n.value==='number'&&Number.isInteger(n.value)&&!Object.is(n.value,-0)&&n.value>=-2147483648&&n.value<=2147483647||n?.type==='BinaryExpression'&&['&','|','^','<<','>>'].includes(n.operator)||n?.type==='UnaryExpression'&&n.operator==='~';
 const walk=n=>{if(!n||typeof n!=='object')return;if(n.type==='VariableDeclarator'&&n.id.type==='Identifier'){counts.set(n.id.name,(counts.get(n.id.name)||0)+1);if(signed(n.init))declarations.set(n.id.name,n);}
  if(n.type==='AssignmentExpression'&&n.left.type==='Identifier')writes.push([n.left.name,n.operator==='='?signed(n.right):['&=','|=','^=','<<=','>>='].includes(n.operator)]);
  if(n.type==='UpdateExpression'&&n.argument.type==='Identifier')writes.push([n.argument.name,false]);
  for(const [k,v]of Object.entries(n))if(!['loc','start','end'].includes(k)&&k[0]!=='_'){if(Array.isArray(v))v.forEach(walk);else if(v&&typeof v==='object')walk(v);}
 };
 node.params.forEach(p=>{if(p.type==='Identifier')counts.set(p.name,1);});walk(node.body);
 for(const [name,count]of counts)if(count!==1)declarations.delete(name);
 for(const [name,safe]of writes)if(!safe)declarations.delete(name);
 return new Set(declarations.values());
}

class NumericEmitter {
 constructor(c,plan,offset){this.c=c;this.plan=plan;this.offset=offset;this.code=[];this.locals=[];this.labels=[];this.scopes=[];this.integers=c.environments?integerBindings(plan.node):new Set();this.stringParams=c.environments?stringParameters(plan.node):new Set();this.stringMask=0;this.usesStrings=false;}
 reject(reason){throw new NotNumeric(reason);}
 out(...bytes){this.code.push(...bytes);}
 local(type){const index=this.offset+this.locals.length;this.locals.push(wasmType(type));return index;}
 get(i){this.out(0x20,...u32(i));} set(i){this.out(0x21,...u32(i));} tee(i){this.out(0x22,...u32(i));}
 integer(n){this.out(0x41,...i32(n));}
 number(n){const data=new Uint8Array(8);new DataView(data.buffer).setFloat64(0,n,true);this.out(0x44,...data);}
 rt(name){this.out(0x10,...u32(this.c.import(name)));}
 undefined(){this.integer(this.c.constant(undefined));this.rt('lit');}
 box(type){if(type!==STRING)this.rt(type===F64?'number':'boolean');}
 binding(name,write=false){
  let binding;for(let i=this.scopes.length-1;i>=0;i--)if(this.scopes[i].has(name)){binding=this.scopes[i].get(name);break;}
  if(!binding)this.reject('Внешняя переменная: '+name);
  if(binding.type===null)this.reject('Привязка до инициализации: '+name);
  if(write&&binding.kind==='const')this.reject('Изменение const: '+name);
  return binding;
 }
 reference(n,write=false){
  if(n.type==='Identifier'){const b=this.binding(n.name,write);if(b.type===RECORD)this.reject('Локальный объект покидает доступ по полю');return b;}
  if(n.type==='MemberExpression'&&!n.optional&&n.object.type==='Identifier'){
   const record=this.binding(n.object.name);
   const key=!n.computed?n.property.name:n.property.type==='Literal'&&['string','number'].includes(typeof n.property.value)?String(n.property.value):null;
   if(record.type===RECORD&&key!==null&&record.fields.has(key))return record.fields.get(key);
  }
  this.reject('Динамический доступ к свойству');
 }
 read(b){this.get(b.local);if(b.storage===I32)this.out(0xb7);}
 stringField(index){this.c.stringGC=true;this.out(0xfb,0x1a,0xfb,0x16,0,0xfb,2,0,index);}
 charCodeAt(receiver,position){
  const index=this.local(F64);position();this.set(index);this.get(index);this.get(index);this.out(0x62);this.ifElse(()=>{this.number(0);this.set(index);});this.get(index);this.out(0x9d);this.set(index);
  this.get(index);this.number(0);this.out(0x63);this.get(index);this.get(receiver);this.stringField(2);this.out(0xb7,0x66,0x72);
  this.ifElse(()=>this.number(NaN),()=>{this.get(receiver);this.stringField(0);this.out(0xfb,0x1a,0xfb,0x16,2,0xfb,2,2,0,0xfb,0x1a,0xfb,0x16,1);this.get(receiver);this.stringField(1);this.get(index);this.out(0xaa,0x6a,0xfb,0x0d,1,0xb7);},F64);
 }
 integerExpression(n){
  if(n.type==='Literal'&&typeof n.value==='number'){this.integer(n.value|0);return;}
  if(n.type==='Identifier'){const b=this.binding(n.name);if(b.storage===I32){this.get(b.local);return;}}
  if(n.type==='BinaryExpression'&&bitwise[n.operator]){this.integerExpression(n.left);this.integerExpression(n.right);this.out(bitwise[n.operator]);return;}
  if(n.type==='UnaryExpression'&&n.operator==='~'){this.integerExpression(n.argument);this.integer(-1);this.out(0x73);return;}
  if(this.expression(n)!==F64)this.reject('Нечисловой битовый операнд');this.rt('int32');
 }
 record(n){
  if(!this.c.environments)this.reject('Локальные объекты требуют backend 0.7');
  const fields=new Map();
  for(const p of n.properties){
   if(p.type!=='Property'||p.kind!=='init'||p.method||p.computed)this.reject('Сложное свойство локального объекта');
   const key=p.key.type==='Identifier'?p.key.name:String(p.key.value);
   if(key==='__proto__'&&!p.shorthand)this.reject('Прототип локального объекта');
   const type=this.expression(p.value),local=this.local(type);this.set(local);fields.set(key,{type,local,kind:'field'});
  }
  return fields;
 }
 scope(statements,body){
  const scope=new Map();
  for(const n of statements)if(n.type==='VariableDeclaration'){
   if(n.kind==='var')this.reject('var требует общего окружения');
   for(const d of n.declarations){if(d.id.type!=='Identifier'||!d.init)this.reject('Нужна простая привязка с инициализатором');if(scope.has(d.id.name))this.reject('Повторная привязка');scope.set(d.id.name,{kind:n.kind,type:null,local:null});}
  }
  this.scopes.push(scope);body();this.scopes.pop();
 }
 block(kind,body){const label={kind};this.out(kind==='loop'?0x03:0x02,0x40);this.labels.push(label);body(label);this.labels.pop();this.out(0x0b);}
 branch(label,conditional=false){const depth=this.labels.length-1-this.labels.lastIndexOf(label);if(depth<0)throw new Error('Invalid numeric branch');this.out(conditional?0x0d:0x0c,...u32(depth));}
 condition(type){
  if(type===I32)return;
  if(type===STRING){this.stringField(2);this.out(0x45,0x45);return;}
  // NaN is falsy in JS; comparing only against zero would be incorrect.
  const tmp=this.local(F64);this.tee(tmp);this.number(0);this.out(0x62);this.get(tmp);this.get(tmp);this.out(0x61,0x71);
 }
 ifElse(yes,no,type=0x40){this.out(0x04,wasmType(type));this.labels.push({kind:'if'});yes();if(no){this.out(0x05);no();}this.labels.pop();this.out(0x0b);}
 stringMethod(n){
  const callee=n.callee,method=callee.type==='MemberExpression'&&!callee.optional?memberName(callee):null;
  if(!this.c.environments||n.optional||!STRING_METHODS.includes(method)||n.arguments.some(a=>a.type==='SpreadElement'))this.reject('Вызов требует общего backend');
  if(this.expression(callee.object)!==STRING)this.reject('Не строковый получатель');
  const receiver=this.local(STRING);this.set(receiver);const args=[];
  for(const node of n.arguments){const type=this.expression(node),local=this.local(type);this.set(local);args.push({type,local});}
  this.stringMask|=1<<STRING_METHODS.indexOf(method);this.usesStrings=true;
  const arg=(i,type,defaultValue)=>{if(i>=args.length){this.number(defaultValue);return;}if(args[i].type!==type)this.reject('Строковый метод требует преобразования аргумента');this.get(args[i].local);};
  if(method==='concat'){this.get(receiver);for(const a of args){if(a.type!==STRING)this.reject('concat требует строковые аргументы');this.get(a.local);this.rt('stringConcat');}return STRING;}
  if(method==='charCodeAt'){this.charCodeAt(receiver,()=>arg(0,F64,NaN));return F64;}
  this.get(receiver);
  if(['indexOf','lastIndexOf','includes','startsWith','endsWith'].includes(method)){
   if(!args.length)this.reject('Поиск без строкового аргумента');arg(0,STRING);arg(1,F64,method==='endsWith'?Infinity:NaN);this.rt('string'+method[0].toUpperCase()+method.slice(1));
   if(method==='indexOf'||method==='lastIndexOf'){this.out(0xb7);return F64;}return I32;
  }
  arg(0,F64,NaN);if(method==='slice'||method==='substring')arg(1,F64,Infinity);
  this.rt('string'+method[0].toUpperCase()+method.slice(1));return method==='charCodeAt'?F64:STRING;
 }
 expression(n){
  switch(n.type){
   case'Literal':if(typeof n.value==='number'){this.number(n.value);return F64;}if(typeof n.value==='boolean'){this.integer(+n.value);return I32;}if(this.c.environments&&typeof n.value==='string'){this.integer(this.c.constant(n.value));this.rt('lit');this.usesStrings=true;return STRING;}break;
   case'Identifier':{const b=this.reference(n);this.read(b);return b.type;}
   case'MemberExpression':{
    if(n.object.type==='Identifier'&&this.binding(n.object.name).type===RECORD){const b=this.reference(n);this.read(b);return b.type;}
    if(!n.optional&&memberName(n)==='length'&&this.expression(n.object)===STRING){this.stringField(2);this.out(0xb7);return F64;}break;
   }
   case'CallExpression':return this.stringMethod(n);
   case'UnaryExpression':{
    const t=this.expression(n.argument);
    if(n.operator==='!'){this.condition(t);this.out(0x45);return I32;}
    if(this.c.environments&&n.operator==='typeof'){this.out(0x1a);this.integer(this.c.constant(t===STRING?'string':t===F64?'number':'boolean'));this.rt('lit');this.usesStrings=true;return STRING;}
    if(t===F64&&n.operator==='+')return F64;
    if(t===F64&&n.operator==='-'){this.out(0x9a);return F64;}
    if(t===F64&&n.operator==='~'&&this.c.environments){this.rt('int32');this.integer(-1);this.out(0x73,0xb7);return F64;}break;
   }
   case'BinaryExpression':{
    if(this.c.environments&&bitwise[n.operator]){this.integerExpression(n);this.out(n.operator==='>>>'?0xb8:0xb7);return F64;}
    const a=this.expression(n.left),b=this.expression(n.right);
    if(a===F64&&b===F64&&arithmetic[n.operator]){this.out(arithmetic[n.operator]);return F64;}
    if(a===STRING&&b===STRING){if(n.operator==='+'){this.rt('stringConcat');return STRING;}if(stringComparisons[n.operator]){this.rt('stringCompare');this.integer(0);this.out(stringComparisons[n.operator]);return I32;}break;}
    if(a===b&&comparisons[n.operator]){this.out(a===F64?comparisons[n.operator]:booleanComparisons[n.operator]);return I32;}break;
   }
   case'AssignmentExpression':{
    const b=this.reference(n.left,true);
    const op=n.operator.slice(0,-1),bit=this.c.environments&&bitwise[op];
    if(b.type===STRING){if(n.operator!== '='&&n.operator!=='+=')break;if(n.operator==='+=' )this.get(b.local);if(this.expression(n.right)!==STRING)this.reject('Присваивание меняет тип');if(n.operator==='+=' )this.rt('stringConcat');this.tee(b.local);return STRING;}
    if(b.storage===I32){if(n.operator==='=')this.integerExpression(n.right);else{this.get(b.local);this.integerExpression(n.right);this.out(bit);}this.tee(b.local);this.out(0xb7);return F64;}
    if(n.operator!=='='){if(b.type!==F64||!(arithmetic[op]||bit))break;this.get(b.local);if(bit)this.rt('int32');}
    const t=this.expression(n.right);if(t!==b.type)this.reject('Присваивание меняет тип');
    if(n.operator!=='='){if(bit){this.rt('int32');this.out(bit,op==='>>>'?0xb8:0xb7);}else this.out(arithmetic[op]);}
    this.tee(b.local);return t;
   }
   case'UpdateExpression':{
    const b=this.reference(n.argument,true);if(b.type!==F64)break;
    this.get(b.local);let old;if(!n.prefix){old=this.local(F64);this.tee(old);}
    this.number(1);this.out(n.operator==='++'?0xa0:0xa1);
    if(n.prefix)this.tee(b.local);else{this.set(b.local);this.get(old);}return F64;
   }
   case'ConditionalExpression':{
    this.condition(this.expression(n.test));
    // The result type is known after emitting the branches; patch blocktype.
    this.out(0x04,0x40);const typeOffset=this.code.length-1;this.labels.push({kind:'if'});
    const a=this.expression(n.consequent);this.out(0x05);const b=this.expression(n.alternate);
    if(a!==b)this.reject('Разные типы ветвей');this.labels.pop();this.out(0x0b);this.code[typeOffset]=wasmType(a);return a;
   }
   case'LogicalExpression':{
    const a=this.expression(n.left),tmp=this.local(a);this.tee(tmp);
    if(n.operator==='??'){
     // Neither accepted type can be nullish. Still check the other branch so
     // the specialization has a consistent, explicit supported syntax set.
     this.out(0x1a);this.integer(1);
    }else this.condition(a);
    this.ifElse(()=>{if(n.operator==='&&'){if(this.expression(n.right)!==a)this.reject('Разные типы логических ветвей');}else this.get(tmp);},()=>{if(n.operator==='&&')this.get(tmp);else if(this.expression(n.right)!==a)this.reject('Разные типы логических ветвей');},a);return a;
   }
   case'SequenceExpression':{let type;for(let i=0;i<n.expressions.length;i++){type=this.expression(n.expressions[i]);if(i<n.expressions.length-1)this.out(0x1a);}return type;}
  }
  this.reject('Динамическая операция: '+n.type+(n.operator?' '+n.operator:''));
 }
 statement(n){
  switch(n.type){
   case'EmptyStatement':case'DebuggerStatement':return;
   case'ExpressionStatement':if(!n.directive){this.expression(n.expression);this.out(0x1a);}return;
   case'BlockStatement':this.scope(n.body,()=>n.body.forEach(s=>this.statement(s)));return;
   case'VariableDeclaration':
    if(n.kind==='var')this.reject('var требует общего окружения');
    for(const d of n.declarations){const b=this.scopes.at(-1).get(d.id.name);if(!b||b.type!==null)this.reject('Повторная или сложная привязка');if(d.init.type==='ObjectExpression'){b.fields=this.record(d.init);b.type=RECORD;}else if(this.integers.has(d)){this.integerExpression(d.init);b.type=F64;b.storage=I32;b.local=this.local(I32);this.set(b.local);}else{const t=this.expression(d.init);b.type=t;b.local=this.local(t);this.set(b.local);}}return;
   case'ReturnStatement':if(n.argument)this.box(this.expression(n.argument));else this.undefined();this.out(0x0f);return;
   case'IfStatement':this.condition(this.expression(n.test));this.ifElse(()=>this.statement(n.consequent),n.alternate?()=>this.statement(n.alternate):null);return;
   case'ForStatement':case'WhileStatement':case'DoWhileStatement':
    this.scope(n.init?.type==='VariableDeclaration'?[n.init]:[],()=>{
     if(n.init){if(n.init.type==='VariableDeclaration')this.statement(n.init);else{this.expression(n.init);this.out(0x1a);}}
     this.block('break',end=>this.block('loop',again=>{
      if(n.type!=='DoWhileStatement'&&n.test){this.condition(this.expression(n.test));this.out(0x45);this.branch(end,true);}
      this.block('continue',()=>this.statement(n.body));
      if(n.update){this.expression(n.update);this.out(0x1a);}
      if(n.type==='DoWhileStatement'){this.condition(this.expression(n.test));this.branch(again,true);}else this.branch(again);
     }));
    });return;
   case'BreakStatement':case'ContinueStatement':{
    if(n.label)this.reject('Метки используют общий backend');
    const kind=n.type==='BreakStatement'?'break':'continue',target=[...this.labels].reverse().find(l=>l.kind===kind);if(!target)this.reject('Нет цели перехода');this.branch(target);return;
   }
  }
  this.reject('Общий backend: '+n.type);
 }
 compile(){
  const {node}=this.plan,params=new Map();
  for(const p of node.params){if(p.type!=='Identifier'||p.name==='arguments'||params.has(p.name))this.reject('Сложные или повторные параметры');const type=this.stringParams.has(p.name)?STRING:F64;params.set(p.name,{type,local:this.local(type),kind:'param'});if(type===STRING)this.usesStrings=true;}
  this.scopes.push(params);
  let maskOffset;
  this.block('guard',fallback=>{
   // All guards precede all user operations. A failed guard jumps to the
   // generic WASM body with the original environment and argument array.
   const ref=this.local(REF);
   node.params.forEach((p,i)=>{const b=params.get(p.name);this.get(2);this.integer(i);this.rt('arg');this.tee(ref);this.rt(b.type===STRING?'isString':'isNumber');this.out(0x45);this.branch(fallback,true);this.get(ref);if(b.type===F64)this.rt('toNumber');this.set(b.local);});
   // Patched after analysis. A single prototype guard precedes the complete
   // pure body; a failed guard executes the generic code with original inputs.
   maskOffset=this.code.length;
   if(node.body.type==='BlockStatement')this.statement(node.body);else{this.box(this.expression(node.body));this.out(0x0f);}
   this.undefined();this.out(0x0f);
  });
  const suffix=this.code.slice(maskOffset);this.code.length=maskOffset;
  if(this.stringMask){this.integer(this.stringMask);this.rt('stringBuiltins');this.out(0x45,0x0d,0);}
  this.code.push(...suffix);
  return {code:this.code,locals:this.locals,stringSpecialization:this.usesStrings};
 }
}

function specializeNumeric(c,plan,generic){
 if(plan.kind!=='function'||plan.node.async||plan.node.generator)return null;
 try{return new NumericEmitter(c,plan,4+generic.locals.length).compile();}
 catch(e){if(!(e instanceof NotNumeric))throw e;plan.numericReason=e.message;return null;}
}

exports["specializeNumeric"]=specializeNumeric;

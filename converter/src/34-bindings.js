// Emitter methods for statically resolved bindings (scope.mjs) and statically
// typed values (types.mjs). A local binding is a WASM local; a captured
// binding is element `slot` of a scope record reached from a known record by
// a fixed number of parent links. Values have one of three representations:
// 'r' boxed (externref), 'f' a JS number as f64, 'n' an int32 number as i32,
// 'i' a boolean as i32.
const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const F64=require("./wasm.mjs")["F64"];
const u32=require("./wasm.mjs")["u32"];
const hops=require("./scope.mjs")["hops"];
const NUM=require("./types.mjs")["NUM"];
const INT=require("./types.mjs")["INT"];
const NU=require("./types.mjs")["NU"];
const STR=require("./types.mjs")["STR"];
const TAI=require("./types.mjs")["TAI"];
const BOOL=require("./types.mjs")["BOOL"];

const SCOPE=3; // scope record type index in the program module
const GLOBAL_CONSTANTS=new Map([['undefined',undefined],['NaN',NaN],['Infinity',Infinity]]);
const VALUE_TYPE={r:REF,f:F64,i:I32,n:I32};
const reprOf=t=>t===NUM?'f':t===INT?'n':t===BOOL?'i':'r';
const numeric=t=>t===NUM||t===INT||t===BOOL||t===NU;
const number=t=>t===NUM||t===INT;
const I32_COMPARE={'<':0x48,'>':0x4a,'<=':0x4c,'>=':0x4e,'==':0x46,'===':0x46,'!=':0x47,'!==':0x47};
const F64_OPS={'+':0xa0,'-':0xa1,'*':0xa2,'/':0xa3};
const F64_COMPARE={'<':0x63,'>':0x64,'<=':0x65,'>=':0x66,'==':0x61,'===':0x61,'!=':0x62,'!==':0x62};
const I32_OPS={'&':0x71,'|':0x72,'^':0x73,'<<':0x74,'>>':0x75,'>>>':0x76};
const MATH_UNARY={abs:0x99,ceil:0x9b,floor:0x9c,trunc:0x9d,sqrt:0x9f};
const MATH_IMPORTED1=new Set(['acos','acosh','asin','asinh','atan','atanh','cbrt','cos','cosh','exp','expm1','log','log10','log1p','log2','sin','sinh','tan','tanh']);
const MATH_IMPORTED2=new Set(['atan2','pow']);
function fail(n,message){throw new SyntaxError(message+(n?.loc?` (строка ${n.loc.start.line}, столбец ${n.loc.start.column+1})`:''));}

const bindings={
 ref(n){if(!n._ref)fail(n,'Внутренняя ошибка: имя '+n.name+' не разрешено');return n._ref;},
 f64(n){const a=new Uint8Array(8);new DataView(a.buffer).setFloat64(0,n,true);this.out(0x44,...a);},
 // ---- scope records ----
 castRecord(){this.out(0xfb,0x1a,0xfb,0x16,SCOPE);},
 slotGet(slot){this.castRecord();this.integer(slot);this.out(0xfb,0x0b,SCOPE);},
 record(target){
  const own=this.records.get(target);if(own!==undefined){this.get(own);return;}
  const n=hops(this.closureScope,target);this.get(0);for(let i=0;i<n;i++)this.slotGet(0);
 },
 // Record of the nearest materialized scope at or above `scope`.
 recordAt(scope){let s=scope;while(s&&!s.materialized)s=s.parent;this.record(s);},
 openRecord(scope){
  this.get(this.env);this.integer(scope.slots);this.rt('scopeNew');
  const local=this.local();this.set(local);this.env=local;this.records.set(scope,local);
 },
 enterScope(scope,body,statements=null){
  if(!scope){body();return;}
  const previous=this.env;
  if(scope.materialized&&!this.records.has(scope))this.openRecord(scope);
  // A block re-entered by a loop starts in TDZ again.
  for(const b of scope.bindings.values())if(b.storage==='local'&&b.tdzChecks){this.out(0xd0,REF);this.set(this.bindingLocal(b));}
  if(statements)this.hoistFunctions(statements);
  body();
  this.env=previous;
 },
 hoistFunctions(statements){
  for(const s of statements)if(s.type==='FunctionDeclaration')this.initIdentifier(s.id,()=>this.function(s,s._displayName||s.id.name));
 },
 // ---- representations ----
 // Converts the value on the stack from representation `from` to `to`.
 // `t` is its static type; a boxed value of static type num/bool is unboxed
 // without checks.
 convert(from,to,t){
  if(from===to)return;
  if(to==='r'){this.rt(from==='f'?'number':from==='n'?'fromInt32':'boolean');return;}
  if(to==='f'){
   if(from==='i'||from==='n'){this.out(0xb7);return;}
   if(t===BOOL){this.rt('truth');this.out(0xb7);return;}
   this.rt(number(t)?'toNumber':'toNumberValue');return;
  }
  if(to==='n'){
   // An int-typed value converts exactly; anything else gets ToInt32.
   if(from==='i')return;
   if(from==='r'){if(t===BOOL){this.rt('truth');return;}this.rt(number(t)?'toNumber':'toNumberValue');}
   if(t===INT)this.out(0xaa);else this.toInt32();
   return;
  }
  // to 'i': JavaScript truthiness.
  if(from==='n'){this.out(0x45,0x45);return;}
  if(from==='f'){const v=this.local(F64);this.tee(v);this.f64(0);this.out(0x62);this.get(v);this.get(v);this.out(0x61,0x71);return;}
  this.rt('truth');
 },
 emitAs(n,to,hint=''){
  if(n._elem&&to!=='r'&&!this.dynamicOnly){this.elementAs(n,to);return;}
  if(to==='n'&&!this.dynamicOnly&&n._t!==INT&&this.wrapsToInt32(n,0)){this.int32Wrap(n);return;}
  this.convert(this.natural(n,hint),to,n._t);
 },
 // ---- strings ----
 // Pushes ToString(n) when n's static type allows it without user code.
 stringOf(n){
  switch(n._t){
   case STR:this.natural(n);return true;
   case INT:this.emitAs(n,'n');this.rt('intToString');return true;
   case NUM:this.emitAs(n,'f');this.rt('numberToString');return true;
   case BOOL:this.emitAs(n,'i');this.ifElse(()=>this.lit('true'),()=>this.lit('false'),REF);return true;
  }
  return false;
 },
 concat(left,right){
  const direct=t=>t===STR||t===INT||t===NUM||t===BOOL;
  if(!direct(left._t)||!direct(right._t))return null;
  this.stringOf(left);this.stringOf(right);this.rt('stringConcat');return 'r';
 },
 template(n){
  this.lit(n.quasis[0].value.cooked);
  n.expressions.forEach((e,i)=>{
   if(!this.stringOf(e)){this.expression(e);this.rt('templateString');}
   this.rt('stringConcat');
   const next=n.quasis[i+1].value.cooked;if(next){this.lit(next);this.rt('stringConcat');}
  });
 },
 // String.prototype methods on a string receiver, in WASM. Arguments are
 // evaluated first and converted afterwards, as the methods do.
 stringMethod(n){
  const {name}=n._strMethod,args=n.arguments,search=['indexOf','lastIndexOf','includes','startsWith','endsWith'].includes(name);
  if(!['charCodeAt','charAt','slice','substring','repeat','concat'].includes(name)&&!search)return null;
  if(search&&(!args.length||args[0]._t!==STR)||name==='concat'&&args.some(a=>a._t!==STR))return null;
  const s=this.local();this.natural(n.callee.object);this.set(s);
  const values=args.map(a=>{const r=this.natural(a),x=this.local(VALUE_TYPE[r]);this.set(x);return {x,r,t:a._t};});
  const number=(i,missing)=>{if(i>=values.length){this.f64(missing);return;}const v=values[i];this.get(v.x);this.convert(v.r,'f',v.t);};
  this.get(s);
  switch(name){
   case'charCodeAt':number(0,NaN);this.rt('stringCharCodeAt');return 'f';
   case'charAt':number(0,NaN);this.rt('stringCharAt');return 'r';
   case'slice':case'substring':number(0,NaN);number(1,Infinity);this.rt(name==='slice'?'stringSlice':'stringSubstring');return 'r';
   case'repeat':number(0,NaN);this.rt('stringRepeat');return 'r';
   case'concat':for(const v of values){this.get(v.x);this.rt('stringConcat');}return 'r';
  }
  this.get(values[0].x);number(1,name==='endsWith'?Infinity:NaN);
  this.rt('string'+name[0].toUpperCase()+name.slice(1));
  return name==='indexOf'||name==='lastIndexOf'?'n':'i';
 },
 // A typed array element in a numeric context: one host call, no boxing.
 // Out of bounds it is NaN (or 0 after ToInt32), exactly what undefined
 // becomes there.
 elementAs(n,repr){
  this.expression(n.object);this.emitAs(n.property,'f');
  if(repr==='n'||repr==='i'&&n._elem===TAI){this.rt('taGetI');if(repr==='i')this.out(0x45,0x45);return;}
  this.rt('taGetF');this.convert('f',repr,NU);
 },
 // a[i] = v / a[i] op= v / a[i]++ on a typed array receiver.
 assignElement(n){
  const m=n.left,o=this.local(),i=this.local(F64);
  this.expression(m.object);this.set(o);this.emitAs(m.property,'f');this.set(i);
  let r;
  if(n.operator==='=')r=this.natural(n.right);
  else if(['&&=','||=','??='].includes(n.operator))return null;
  else r=this.binaryAny(n.operator.slice(0,-1),{type:'Emit',_t:NUM,emit:()=>{this.get(o);this.get(i);this.rt('taGetF');return 'f';}},n.right,n._t);
  return this.storeElement(o,i,r);
 },
 storeElement(o,i,r){
  const v=this.local(VALUE_TYPE[r]);this.set(v);this.get(o);this.get(i);this.get(v);
  if(r==='f')this.rt('taSetF');else if(r==='r'){this.integer(+this.strict);this.rt('setIndex');this.out(0x1a);}else this.rt('taSetI');
  this.get(v);return r;
 },
 updateElement(n){
  const m=n.argument,o=this.local(),i=this.local(F64),old=this.local(F64),value=this.local(F64);
  this.expression(m.object);this.set(o);this.emitAs(m.property,'f');this.set(i);
  this.get(o);this.get(i);this.rt('taGetF');this.tee(old);this.f64(n.operator==='++'?1:-1);this.out(0xa0);this.set(value);
  this.get(o);this.get(i);this.get(value);this.rt('taSetF');this.get(n.prefix?value:old);return 'f';
 },
 // ToInt32 of sums/differences of int32 values (exact in f64 up to 2^53) and
 // of x >>> y equals wrapping i32 arithmetic on the same bits.
 wrapsToInt32(n,depth){
  if(depth>20)return false;
  if(n._t===INT)return true;
  if(n.type!=='BinaryExpression')return false;
  if(n.operator==='>>>')return true;
  return (n.operator==='+'||n.operator==='-')&&n._t===NUM&&this.wrapsToInt32(n.left,depth+1)&&this.wrapsToInt32(n.right,depth+1);
 },
 int32Wrap(n){
  if(n._t===INT){this.convert(this.natural(n),'n',INT);return;}
  if(n.operator==='>>>'){this.operandsAs(n.left,n.right,'n');this.out(0x76);return;}
  this.int32Wrap(n.left);this.int32Wrap(n.right);this.out(n.operator==='+'?0x6a:0x6b);
 },
 condition(n){this.emitAs(n,'i');},
 // ToInt32 of the f64 on the stack, inline for the common in-range case.
 toInt32(){
  const v=this.local(F64);this.tee(v);this.f64(-2147483648);this.out(0x66);this.get(v);this.f64(2147483648);this.out(0x63,0x71);
  this.ifElse(()=>{this.get(v);this.out(0xaa);},()=>{this.get(v);this.rt('int32');},I32);
 },
 // ---- bindings ----
 bindingLocal(b){let local=this.bindingLocals.get(b);if(local===undefined){local=this.local(VALUE_TYPE[this.storageRepr(b)]);this.bindingLocals.set(b,local);}return local;},
 storageRepr(b){return b.storage==='local'&&!this.dynamicOnly?reprOf(b.type):'r';},
 importTarget(b){
  const id=b.scope.node._moduleId,spec=this.c.moduleSpecs[id],entry=spec.imports.find(e=>e.local===b.name);
  if(!entry||entry.target.namespace)return null;
  const target=this.c.analysis.modules.get(entry.target.module).bindings.get(entry.target.local);
  if(!target)throw new Error('Внутренняя ошибка: нет привязки '+entry.target.local);
  return {module:entry.target.module,slot:target.slot,initialized:target.kind==='function'};
 },
 // Pushes the stored value; returns [representation, may be uninitialized].
 loadBinding(b){
  if(b.storage==='local'){this.get(this.bindingLocal(b));return [this.storageRepr(b),true];}
  if(b.kind==='import'){
   const t=this.importTarget(b);
   if(t){this.integer(t.module);this.rt('moduleScope');this.slotGet(t.slot);return ['r',!t.initialized];}
  }
  this.record(b.scope);this.slotGet(b.slot);return ['r',true];
 },
 // Stores the value on the stack (representation `from`) into binding b.
 storeBinding(b,keep,from='r'){
  const repr=this.storageRepr(b);
  if(b.storage==='local'){this.convert(from,repr,b.type);this.out(keep?0x22:0x21,...u32(this.bindingLocal(b)));return repr;}
  this.convert(from,'r',b.type);
  const v=this.local();this.set(v);this.record(b.scope);this.castRecord();this.integer(b.slot);this.get(v);this.out(0xfb,0x0e,SCOPE);if(keep)this.get(v);
  return 'r';
 },
 tdz(name){this.integer(this.c.constant(name));this.rt('tdz');this.out(0x00);},
 checkInitialized(name){const t=this.local();this.tee(t);this.out(0xd1);this.ifElse(()=>this.tdz(name));this.get(t);},
 isGlobal(r){return !r.binding||r.binding.storage==='global';},
 // Pushes an identifier's value in its natural representation.
 readNatural(n){
  const r=this.ref(n),b=r.binding;
  if(this.isGlobal(r)){
   if(!b&&GLOBAL_CONSTANTS.has(n.name)){if(n.name!=='undefined'&&!this.dynamicOnly){this.f64(GLOBAL_CONSTANTS.get(n.name));return 'f';}this.lit(GLOBAL_CONSTANTS.get(n.name));return 'r';}
   this.integer(this.c.constant(n.name));this.rt('globalRead');return 'r';
  }
  const [repr,mayBeEmpty]=this.loadBinding(b);
  if(repr==='r'&&r.check&&mayBeEmpty)this.checkInitialized(n.name);
  if(repr==='r'&&b.storage!=='local'&&!this.dynamicOnly&&numeric(b.type)){this.convert('r',reprOf(b.type),b.type);return reprOf(b.type);}
  return repr;
 },
 readIdentifier(n){this.convert(this.readNatural(n),'r',n._t);},
 // Assignment semantics: TDZ and constant checks after the value is computed.
 // emitValue pushes the value and may return its representation (default 'r').
 writeIdentifier(n,emitValue,keep=true){
  const r=this.ref(n),b=r.binding;
  if(this.isGlobal(r)){this.integer(this.c.constant(n.name));this.convert(emitValue()||'r','r');this.integer(+this.strict);this.rt('globalWrite');if(!keep)this.out(0x1a);return 'r';}
  let from=emitValue()||'r';
  if(r.check&&r.mode==='write'){
   const v=this.local(VALUE_TYPE[from]);this.set(v);
   const [repr,mayBeEmpty]=this.loadBinding(b);
   if(repr==='r'&&mayBeEmpty){this.out(0xd1);this.ifElse(()=>this.tdz(n.name));}else this.out(0x1a);
   this.get(v);
  }
  if(!b.mutable){
   if(b.kind==='callee'&&!this.strict){if(!keep)this.out(0x1a);return from;}
   this.out(0x1a);this.integer(this.c.constant(n.name));this.rt('constAssign');this.out(0x00);return 'r';
  }
  return this.storeBinding(b,keep,from);
 },
 // Declaration semantics: initializes the binding, never checks.
 initIdentifier(n,emitValue){
  const r=this.ref(n),b=r.binding;
  if(this.isGlobal(r)){this.integer(this.c.constant(n.name));this.convert(emitValue()||'r','r');this.integer(0);this.rt('globalWrite');this.out(0x1a);return;}
  this.storeBinding(b,false,emitValue()||'r');
 },
 initBinding(b,emitValue){this.storeBinding(b,false,emitValue()||'r');},
 typeofIdentifier(n){
  if(this.isGlobal(this.ref(n))){this.integer(this.c.constant(n.name));this.rt('globalTypeof');return;}
  this.integer(this.c.constant('typeof'));this.readIdentifier(n);this.rt('unary');
 },
 deleteIdentifier(n){
  if(this.isGlobal(this.ref(n))){this.integer(this.c.constant(n.name));this.rt('globalDelete');return;}
  this.lit(false);
 },
 updateNatural(n,delta,prefix,induction=false){
  const b=this.ref(n).binding;
  if(induction&&!this.dynamicOnly&&b.type===INT){
   // A proven loop induction variable cannot overflow int32.
   const old=this.local(I32),value=this.local(I32);
   this.convert(this.readNatural(n),'n',INT);this.tee(old);this.integer(delta);this.out(0x6a);this.set(value);
   this.writeIdentifier(n,()=>{this.get(value);return 'n';},false);this.get(prefix?value:old);return 'n';
  }
  if(!this.dynamicOnly&&b&&b.storage!=='global'&&b.type===NUM){
   const old=this.local(F64),value=this.local(F64);
   this.convert(this.readNatural(n),'f',NUM);this.tee(old);this.f64(delta);this.out(0xa0);this.set(value);
   this.writeIdentifier(n,()=>{this.get(value);return 'f';},false);this.get(prefix?value:old);return 'f';
  }
  const old=this.local(),value=this.local();
  this.readIdentifier(n);this.rt('toNumeric');this.tee(old);this.integer(delta);this.rt('increment');this.set(value);
  this.writeIdentifier(n,()=>this.get(value),false);this.get(prefix?value:old);return 'r';
 },
 assignNatural(n){
  const left=n.left,t=n._t;
  if(n.operator==='=')return this.writeIdentifier(left,()=>this.natural(n.right,left.name));
  if(['&&=','||=','??='].includes(n.operator)){
   const old=this.local();this.readIdentifier(left);this.tee(old);this.rt(n.operator==='??='?'nullish':'truth');
   const put=()=>this.convert(this.writeIdentifier(left,()=>this.natural(n.right,left.name)),'r',t);
   if(n.operator==='||=')this.ifElse(()=>this.get(old),put,REF);else this.ifElse(put,()=>this.get(old),REF);return 'r';
  }
  return this.writeIdentifier(left,()=>this.binaryAny(n.operator.slice(0,-1),left,n.right,t));
 },
 binaryAny(op,left,right,t){
  const r=this.binaryNatural(op,left,right,t);if(r)return r;
  this.binary(op,()=>this.expression(left),()=>this.expression(right));return 'r';
 },
 // ---- typed expressions ----
 expression(n,hint=''){this.convert(this.natural(n,hint),'r',n._t);},
 natural(n,hint=''){
  if((n.type==='FunctionExpression'||n.type==='ArrowFunctionExpression')&&n._displayName&&!hint)hint=n._displayName;
  if(!this.dynamicOnly){
   const t=n._t;
   switch(n.type){
    case'Literal':
     if(typeof n.value==='number'){if(t===INT){this.integer(n.value);return 'n';}this.f64(n.value);return 'f';}
     if(typeof n.value==='boolean'){this.integer(+n.value);return 'i';}
     break;
    case'Identifier':return this.readNatural(n);
    case'UnaryExpression':
     if(n.operator==='!'){this.condition(n.argument);this.out(0x45);return 'i';}
     if(n.operator==='-'&&t===INT&&n.argument.type==='Literal'){this.integer(-n.argument.value);return 'n';}
     if(n.operator==='+'&&t===INT){this.emitAs(n.argument,'n');return 'n';}
     if(n.operator==='+'&&t===NUM){this.emitAs(n.argument,'f');return 'f';}
     if(n.operator==='-'&&t===NUM){this.emitAs(n.argument,'f');this.out(0x9a);return 'f';}
     if(n.operator==='~'&&t===INT){this.emitAs(n.argument,'n');this.integer(-1);this.out(0x73);return 'n';}
     if(n.operator==='void'){const r=this.natural(n.argument);this.out(0x1a);this.lit(undefined);return r&&'r';}
     break;
    case'UpdateExpression':if(n.argument.type==='Identifier')return this.updateNatural(n.argument,n.operator==='++'?1:-1,n.prefix,!!n._induction);if(n.argument._elem)return this.updateElement(n);if(this.plainMember(n.argument))return this.updateMember(n);break;
    case'AssignmentExpression':{
     if(n.left.type==='Identifier')return this.assignNatural(n);
     if(n.left._elem){const r=this.assignElement(n);if(r)return r;}
     if(this.plainMember(n.left))return this.assignMember(n);break;
    }
    case'BinaryExpression':{const r=this.binaryNatural(n.operator,n.left,n.right,t);if(r)return r;break;}
    case'LogicalExpression':
     if(number(t)&&number(n.left._t)&&number(n.right._t)||t===BOOL&&n.left._t===t&&n.right._t===t){
      const repr=reprOf(t),v=this.local(VALUE_TYPE[repr]);this.emitAs(n.left,repr);this.tee(v);
      if(n.operator==='??'){this.out(0x1a);this.get(v);return repr;}
      this.convert(repr,'i',t);
      if(n.operator==='||')this.ifElse(()=>this.get(v),()=>this.emitAs(n.right,repr),VALUE_TYPE[repr]);else this.ifElse(()=>this.emitAs(n.right,repr),()=>this.get(v),VALUE_TYPE[repr]);
      return repr;
     }
     break;
    case'ConditionalExpression':
     if(numeric(t)){const repr=reprOf(t);this.condition(n.test);this.ifElse(()=>this.emitAs(n.consequent,repr),()=>this.emitAs(n.alternate,repr),VALUE_TYPE[repr]);return repr;}
     this.condition(n.test);this.ifElse(()=>this.expression(n.consequent),()=>this.expression(n.alternate),REF);return 'r';
    case'SequenceExpression':{let r='r';n.expressions.forEach((e,i)=>{r=this.natural(e);if(i<n.expressions.length-1)this.out(0x1a);});return r;}
    case'CallExpression':{
     if(n._direct&&!n.optional)return this.directCall(n,n._direct);
     if(n._math&&!n.optional){const r=this.math(n);if(r)return r;}
     if(n._strMethod){const r=this.stringMethod(n);if(r)return r;}
     if(n._fromCharCode){this.emitAs(n.arguments[0],'n');this.rt('stringFromCharCode');return 'r';}
     if(this.callValue(n))return 'r';
     break;
    }
    case'TemplateLiteral':this.template(n);return 'r';
    case'MemberExpression':
     if(n._taLength){this.expression(n.object);this.rt('taLength');return 'f';}
     if(n._strLength){this.expression(n.object);this.rt('stringLength');return 'n';}
     if(this.plainMember(n)){this.memberGet(n);return 'r';}break;
    case'ObjectExpression':this.objectLiteral(n);return 'r';
    case'Emit':return n.emit();
   }
  }else if(n.type==='Emit')return n.emit();
  this.dynamicExpression(n,hint);return 'r';
 },
 // Object literal; anonymous functions take their names from the keys.
 objectLiteral(n){
  this.rt('object');
  for(const p of n.properties){
   if(p.type==='SpreadElement'){this.expression(p.argument);this.rt('assign');continue;}
   const accessor=p.kind==='get'||p.kind==='set',anonymous=!p.method&&!accessor&&(p.value.type==='ArrowFunctionExpression'||(p.value.type==='FunctionExpression'||p.value.type==='ClassExpression')&&!p.value.id);
   const kind=p.kind==='get'?1:p.kind==='set'?2:!p.computed&&!p.shorthand&&!p.method&&(p.key.name??p.key.value)==='__proto__'?3:p.method?4:0;
   let key=null;
   if(p.computed){this.expression(p.key);this.rt('key');key=this.local();this.tee(key);}else this.lit(p.key.name??p.key.value);
   const name=p.computed?'':String(p.key.name??p.key.value);
   if(p.method||accessor)this.function(p.value,name,true);else this.expression(p.value,kind===3?'':name);
   if(key!==null&&(p.method||accessor||anonymous)||accessor){if(key!==null)this.get(key);else this.lit(name);this.integer(p.kind==='get'?1:p.kind==='set'?2:0);this.rt('setFunctionName');}
   this.integer(kind);this.rt('define');
  }
 },
 // ---- properties and calls without reference objects ----
 plainMember(m){return m.type==='MemberExpression'&&!m.optional&&m.object.type!=='Super'&&m.property.type!=='PrivateIdentifier';},
 // Evaluates object and key once; returns get/set emitters for that place.
 place(m){
  const o=this.local();this.expression(m.object);this.set(o);
  let key=null,kind='c';
  if(m.computed){const r=this.natural(m.property);if(r==='f'){key=this.local(F64);kind='f';}else{this.convert(r,'r',m.property._t);key=this.local();kind='r';}this.set(key);}
  const pushKey=()=>{if(kind==='c')this.lit(m.property.name);else this.get(key);};
  return {
   get:()=>{this.get(o);pushKey();this.rt(kind==='f'?'getIndex':'getProp');return 'r';},
   set:emitValue=>{this.get(o);pushKey();this.convert(emitValue()||'r','r');this.integer(+this.strict);this.rt(kind==='f'?'setIndex':'setProp');return 'r';},
  };
 },
 memberGet(m){
  this.expression(m.object);
  if(!m.computed){this.lit(m.property.name);this.rt('getProp');return;}
  const r=this.natural(m.property);
  if(r==='f'){this.rt('getIndex');return;}
  this.convert(r,'r',m.property._t);this.rt('getProp');
 },
 assignMember(n){
  const p=this.place(n.left),t=n._t;
  if(n.operator==='=')return p.set(()=>this.natural(n.right));
  if(['&&=','||=','??='].includes(n.operator)){
   const old=this.local();p.get();this.tee(old);this.rt(n.operator==='??='?'nullish':'truth');
   const put=()=>p.set(()=>this.natural(n.right));
   if(n.operator==='||=')this.ifElse(()=>this.get(old),put,REF);else this.ifElse(put,()=>this.get(old),REF);return 'r';
  }
  return p.set(()=>this.binaryAny(n.operator.slice(0,-1),{type:'Emit',_t:'any',emit:p.get},n.right,t));
 },
 updateMember(n){
  const p=this.place(n.argument),old=this.local(),value=this.local();
  p.get();this.rt('toNumeric');this.tee(old);this.integer(n.operator==='++'?1:-1);this.rt('increment');this.set(value);
  p.set(()=>this.get(value));this.out(0x1a);this.get(n.prefix?value:old);return 'r';
 },
 // f(a, b) and o.m(a, b): one host crossing, arguments passed directly.
 callValue(n){
  const callee=n.callee;
  if(n.optional||n.arguments.length>8||n.arguments.some(a=>a.type==='SpreadElement'))return false;
  if(callee.type==='Super'||callee.type==='ChainExpression'||callee.type==='MemberExpression'&&!this.plainMember(callee))return false;
  if(callee.type==='MemberExpression'&&!callee.computed&&callee.property.name==='push'&&n.arguments.length)return this.pushCall(n);
  if(callee.type==='MemberExpression'){const self=this.local();this.expression(callee.object);this.tee(self);this.set(self);
   this.get(self);if(!callee.computed)this.lit(callee.property.name);else{const r=this.natural(callee.property);if(r==='f'){this.rt('getIndex');this.get(self);this.args8(n);return true;}this.convert(r,'r',callee.property._t);}
   this.rt('getProp');this.get(self);
  }else{this.expression(callee);this.lit(undefined);}
  this.args8(n);return true;
 },
 args8(n){for(const a of n.arguments)this.expression(a);this.rt('call'+n.arguments.length);},
 // o.push(...): a WASM array with the original Array.prototype.push grows in
 // WASM. Whether that applies is decided before the arguments are evaluated,
 // where the method lookup happens in JS.
 pushCall(n){
  const self=this.local(),fast=this.local(I32),fn=this.local();
  this.expression(n.callee.object);this.set(self);
  this.get(self);this.lit('push');this.rt('pushIntrinsic');this.tee(fast);
  this.ifElse(()=>this.out(0xd0,REF),()=>{this.get(self);this.lit('push');this.rt('getProp');},REF);this.set(fn);
  const args=n.arguments.map(a=>{const x=this.local();this.expression(a);this.set(x);return x;});
  this.get(fast);
  this.ifElse(()=>{for(const x of args){this.get(self);this.get(x);this.rt('push');this.out(0x1a);}this.get(self);this.lit('length');this.rt('getProp');},
   ()=>{this.get(fn);this.get(self);for(const x of args)this.get(x);this.rt('call'+args.length);},REF);
  return true;
 },
 // Numeric binary operators on statically numeric operands; null otherwise.
 binaryNatural(op,left,right,t){
  if(this.dynamicOnly)return null;
  const a=left._t,b=right._t;
  if(a===NU&&b===NU&&['==','===','!=','!=='].includes(op))return null;
  if(a===STR&&b===STR&&I32_COMPARE[op]!==undefined){this.natural(left);this.natural(right);this.rt('stringCompare');this.integer(0);this.out(I32_COMPARE[op]);return 'i';}
  if(op==='+'&&t===STR)return this.concat(left,right);
  if(F64_COMPARE[op]!==undefined&&numeric(a)&&numeric(b)){
   // A number is never strictly equal to a boolean.
   if((op==='==='||op==='!==')&&(a===BOOL)!==(b===BOOL)){this.natural(left);this.out(0x1a);this.natural(right);this.out(0x1a);this.integer(op==='!=='?1:0);return 'i';}
   if(a!==NUM&&b!==NUM){this.operandsAs(left,right,a===BOOL&&b===BOOL?'i':'n');this.out(I32_COMPARE[op]);return 'i';}
   this.operands(left,right);this.out(F64_COMPARE[op]);return 'i';
  }
  if(t===INT&&I32_OPS[op]!==undefined){this.operandsAs(left,right,'n');this.out(I32_OPS[op]);return 'n';}
  if(op==='>>>'&&t===NUM){this.operandsAs(left,right,'n');this.out(0x76,0xb8);return 'f';}
  if(t!==NUM)return null;
  if(F64_OPS[op]!==undefined){this.operands(left,right);this.out(F64_OPS[op]);return 'f';}
  if(op==='%'){this.operands(left,right);this.remainder();return 'f';}
  if(op==='**'){this.operands(left,right);this.rt('pow');return 'f';}
  return null;
 },
 // Evaluates both operands, then converts them to f64 (ToNumber happens
 // after both operands are evaluated, as the language requires).
 operands(left,right){this.operandsAs(left,right,'f');},
 operandsAs(left,right,repr){
  if(repr==='n'&&left._t!==INT&&this.wrapsToInt32(left,0)){this.int32Wrap(left);this.emitAs(right,'n');return;}
  // Converting a value of a numeric type cannot run user code: no need to wait.
  if(numeric(left._t)){this.emitAs(left,repr);this.emitAs(right,repr);return;}
  const a=this.natural(left);
  if(a==='r'&&!numeric(left._t)){const x=this.local();this.set(x);const b=this.natural(right),y=this.local(VALUE_TYPE[b]);this.set(y);this.get(x);this.convert('r',repr,left._t);this.get(y);this.convert(b,repr,right._t);return;}
  this.convert(a,repr,left._t);this.emitAs(right,repr);
 },
 // JS remainder: exact for int32 operands inline, otherwise the host's %.
 remainder(){
  const a=this.local(F64),b=this.local(F64),r=this.local(I32);this.set(b);this.set(a);
  this.get(a);this.get(a);this.out(0xaa,0xb7,0x61);this.get(b);this.get(b);this.out(0xaa,0xb7,0x61,0x71);
  this.get(b);this.f64(0);this.out(0x62,0x71);this.get(b);this.f64(-1);this.out(0x62,0x71);
  this.get(a);this.f64(-2147483648);this.out(0x66,0x71);this.get(a);this.f64(2147483648);this.out(0x63,0x71);
  this.get(b);this.f64(-2147483648);this.out(0x66,0x71);this.get(b);this.f64(2147483648);this.out(0x63,0x71);
  this.ifElse(()=>{
   // A zero result takes the dividend's sign (-4 % 2 is -0).
   this.get(a);this.out(0xaa);this.get(b);this.out(0xaa,0x6f);this.tee(r);this.out(0x45);this.get(a);this.f64(0);this.out(0x63,0x71);
   this.ifElse(()=>this.f64(-0),()=>{this.get(r);this.out(0xb7);},F64);
  },()=>{this.get(a);this.get(b);this.rt('fmod');},F64);
 },
 math(n){
  const name=n._math,args=n.arguments;
  if(args.some(a=>a.type==='SpreadElement'))return null;
  // Arguments are evaluated first, then converted with ToNumber in order.
  // Every argument is evaluated; only the ones the function reads are
  // converted with ToNumber (which may run valueOf).
  const all=(repr='f',count=args.length)=>{
   const values=args.map((a,i)=>{if(i<count&&numeric(a._t)){const x=this.local(VALUE_TYPE[repr]);this.emitAs(a,repr);this.set(x);return [x,repr,a._t];}const r=this.natural(a),x=this.local(VALUE_TYPE[r]);this.set(x);return [x,r,a._t];});
   return values.slice(0,count).map(([x,r,t])=>{if(r===repr)return x;const y=this.local(VALUE_TYPE[repr]);this.get(x);this.convert(r,repr,t);this.set(y);return y;});
  };
  if(MATH_UNARY[name]!==undefined&&args.length>=1){const [x]=all('f',1);this.get(x);this.out(MATH_UNARY[name]);return 'f';}
  if((name==='min'||name==='max')&&args.length>=1){const xs=all();this.get(xs[0]);for(const x of xs.slice(1)){this.get(x);this.out(name==='min'?0xa4:0xa5);}return 'f';}
  if((name==='min'||name==='max')&&!args.length){this.f64(name==='min'?Infinity:-Infinity);return 'f';}
  if(name==='round'&&args.length>=1){
   const [x]=all('f',1),r=this.local(F64);this.get(x);this.out(0x9b);this.tee(r);this.f64(0.5);this.out(0xa1);this.get(x);this.out(0x64);
   this.ifElse(()=>{this.get(r);this.f64(1);this.out(0xa1);},()=>this.get(r),F64);return 'f';
  }
  if(name==='sign'&&args.length>=1){const [x]=all('f',1);this.get(x);this.f64(0);this.out(0x64);this.ifElse(()=>this.f64(1),()=>{this.get(x);this.f64(0);this.out(0x63);this.ifElse(()=>this.f64(-1),()=>this.get(x),F64);},F64);return 'f';}
  if(name==='fround'&&args.length>=1){const [x]=all('f',1);this.get(x);this.out(0xb6,0xbb);return 'f';}
  if(name==='imul'&&args.length>=2){const [x,y]=all('n',2);this.get(x);this.get(y);this.out(0x6c);return 'n';}
  if(name==='clz32'&&args.length>=1){const [x]=all('n',1);this.get(x);this.out(0x67);return 'n';}
  if(MATH_IMPORTED1.has(name)&&args.length>=1){const [x]=all('f',1);this.get(x);this.rt('math_'+name);return 'f';}
  if(MATH_IMPORTED2.has(name)&&args.length>=2){const [x,y]=all('f',2);this.get(x);this.get(y);this.rt('math_'+name);return 'f';}
  if(name==='random'){all('f',0);this.rt('math_random');return 'f';}
  return null;
 },
 // Direct call of a statically known function: no JS, no argument array.
 directCall(n,fn){
  const params=fn.node.params;
  // `this` and new.target are undefined for a plain call; a callee that never
  // reads them gets null and saves two constant loads.
  this.recordAt(fn.scope.parent);
  if(fn.usesThis)this.lit(undefined);else this.out(0xd0,REF);
  this.out(0xd0,REF);
  if(fn.usesThis)this.lit(undefined);else this.out(0xd0,REF);
  params.forEach((p,i)=>{
   const repr=this.c.paramRepr(fn,i);
   if(i<n.arguments.length)this.emitAs(n.arguments[i],repr);
   else if(repr==='r')this.lit(undefined);
   else throw new Error('Внутренняя ошибка: пропущен типизированный аргумент');
  });
  for(const extra of n.arguments.slice(params.length)){this.natural(extra);this.out(0x1a);}
  this.out(0x10,...u32(this.c.typedIndex(fn)));
  return reprOf(fn.ret);
 },
 dynamicExpression(n,hint){
  switch(n.type){
   case'Identifier':this.readIdentifier(n);return;
   case'UnaryExpression':if(n.argument.type==='Identifier'){if(n.operator==='typeof'){this.typeofIdentifier(n.argument);return;}if(n.operator==='delete'){this.deleteIdentifier(n.argument);return;}}break;
   case'UpdateExpression':if(n.argument.type==='Identifier'){this.convert(this.updateNatural(n.argument,n.operator==='++'?1:-1,n.prefix),'r',NUM);return;}break;
   case'AssignmentExpression':if(n.left.type==='Identifier'){this.convert(this.assignNatural(n),'r',n._t);return;}break;
  }
  this.featureExpression(n,hint);
 },
 // ---- functions ----
 info(node){const fn=this.c.analysis.functions.get(node);if(!fn)throw new Error('Внутренняя ошибка: функция без анализа');return fn;},
 function(n,name='',method=false){
  const fn=this.info(n),label=name||n.id?.name||'',id=this.c.add(n,fn.strict,'function',label);
  this.get(this.env);this.integer(id);this.integer(this.c.constant(label));
  this.integer((fn.strict?1:0)|(fn.arrow?2:0)|(n.type==='FunctionExpression'&&n.id?4:0)|(method?8:0)|(n.generator?16:0)|(n.async?32:0)|(fn.fscope?64:0));
  this.get(1);this.get(3);this.rt('func');
 },
 hoistVars(scope,skip=null){
  for(const b of scope.bindings.values()){
   if(b.kind!=='var'||b.storage==='global'||skip?.has(b.name))continue;
   // A typed var is only read after its initializer, so it needs no undefined.
   if(this.storageRepr(b)!=='r')continue;
   this.lit(undefined);this.storeBinding(b,false);
  }
 },
 compile(){
  const {node,kind}=this.plan,fn=this.info(node),analysis=this.c.analysis;
  this.fnInfo=fn;this.records=new Map();this.bindingLocals=new Map();this.env=0;
  if(kind==='module-init'||kind==='module-body'){
   this.records.set(fn.scope,0);this.closureScope=fn.scope;
   if(kind==='module-init'){this.hoistVars(fn.scope);this.hoistFunctions(node.body);}
   else node.body.forEach(n=>this.statement(n));
   this.lit(undefined);return this.result();
  }
  if(kind==='script'){
   this.records.set(analysis.root,0);this.closureScope=analysis.root;
   const error=analysis.scriptErrors.get(node);
   if(error!==undefined){this.integer(this.c.constant(error));this.rt('scriptError');this.out(0x00);return this.result();}
   for(const d of node._globalDecls||[])if(d.kind==='var'){this.integer(this.c.constant(d.name));this.rt('globalVar');}
   for(const s of node.body)if(s.type==='FunctionDeclaration'){this.integer(this.c.constant(s.id.name));this.function(s,s.id.name);this.rt('globalFunction');}
   node.body.forEach(n=>this.statement(n));
   this.lit(undefined);return this.result();
  }
  this.closureScope=fn.scope.parent;
  if(fn.scope.materialized)this.openRecord(fn.scope);
  this.plan.usesArguments=fn.usesArguments;
  if(fn.usesArguments){
   const slots=fn.mapped?node.params.map((p,i)=>node.params.findLastIndex(q=>q.name===p.name)===i?fn.scope.bindings.get(p.name).slot:0):[];
   this.initBinding(fn.argumentsBinding,()=>{this.get(2);this.get(this.env);this.integer(this.c.constant(slots));this.integer(+fn.mapped);this.rt('arguments');});
  }
  if(this.typed)this.typedParameters(node,fn);
  else node.params.forEach((p,i)=>this.pattern(p,()=>{this.get(2);this.integer(i);this.rt(p.type==='RestElement'?'rest':'arg');},'let'));
  if(node.type==='ArrowFunctionExpression'&&node.body.type!=='BlockStatement'){this.returnValue(node.body);return this.result();}
  const body=node.body.body,prologue=()=>{
   if(fn.bodyScope!==fn.scope){
    // Non-simple parameters: body vars start as copies of same-named parameters.
    const copied=new Set();
    for(const b of fn.bodyScope.bindings.values()){const p=fn.scope.bindings.get(b.name);if(b.kind==='var'&&p){copied.add(b.name);this.initBinding(b,()=>{const [repr]=this.loadBinding(p);return repr;});}}
    this.hoistVars(fn.bodyScope,copied);
   }else this.hoistVars(fn.scope);
   this.hoistFunctions(body);
   if(node.generator){this.lit(undefined);this.integer(0);this.rt('pause');this.out(0x1a);}
   body.forEach(n=>this.statement(n));
  };
  if(fn.bodyScope!==fn.scope)this.enterScope(fn.bodyScope,prologue);else prologue();
  if(this.retRepr&&this.retRepr!=='r')this.out(0x00);else this.lit(undefined);
  return this.result();
 },
 result(){return {code:this.code,locals:this.locals};},
 // Typed entry: parameters arrive as WASM params 4.. in their representation.
 typedParameters(node,fn){
  node.params.forEach((p,i)=>{
   const id=p.type==='AssignmentPattern'?p.left:p,b=id._ref.binding,incoming=4+i,repr=this.c.paramRepr(fn,i);
   if(p.type==='AssignmentPattern'){
    const target=this.storageRepr(b);
    this.get(incoming);this.rt('isUndefined');
    this.ifElse(()=>this.emitAs(p.right,target,id.name),()=>{this.get(incoming);this.convert('r',target,b.type);},VALUE_TYPE[target]);
    this.storeBinding(b,false,target);return;
   }
   if(b.storage==='local'&&this.storageRepr(b)===repr){this.bindingLocals.set(b,incoming);return;}
   this.get(incoming);this.storeBinding(b,false,repr);
  });
 },
 returnValue(n){
  if(this.retRepr&&!this.finalizers.length){if(n)this.emitAs(n,this.retRepr);else this.lit(undefined);this.out(0x0f);return;}
  const v=this.local();if(n)this.expression(n);else this.lit(undefined);this.set(v);this.abrupt('return',null,v);
 },
 abrupt(kind,target=null,value=null){
  if(kind==='return'&&this.retRepr&&!this.finalizers.some(c=>true)){this.get(value);this.convert('r',this.retRepr,this.fnInfo.ret);this.out(0x0f);return;}
  this.featureAbrupt(kind,target,value);
 },
 pattern(n,emit,kind='assign'){
  if(n.type==='Identifier'){if(kind==='assign')this.writeIdentifier(n,emit,false);else this.initIdentifier(n,emit);return;}
  this.featurePattern(n,emit,kind);
 },
 reference(n){
  if(n.type==='Identifier')throw new Error('Внутренняя ошибка: ссылка на привязку '+n.name);
  this.featureReference(n);
 },
 callTarget(n){if(n.type==='Identifier'){this.readIdentifier(n);this.rt('prepareValue');return;}this.featureCallTarget(n);},
 chainPart(n,c,mode='value'){if(mode==='call'&&n.type==='Identifier'){this.readIdentifier(n);this.rt('prepareValue');return;}this.featureChainPart(n,c,mode);},
 classExpression(n,inferredName=''){
  const strict=this.strict;this.strict=true;
  this.enterScope(n._scope,()=>{
   const bindingName=n.id?.name||'',name=n._displayName||bindingName||inferredName,c=this.local();
   for(const m of n.body.body)if(m.key?.type==='PrivateIdentifier'){this.get(this.env);this.integer(this.c.constant(m.key.name));this.integer(m.type==='PropertyDefinition'?0:m.kind==='get'||m.kind==='set'?2:1);this.integer(+m.static);this.rt('privateDeclare');}
   const ctor=n.body.body.find(m=>m.type==='MethodDefinition'&&m.kind==='constructor'),ctorId=ctor?this.c.add(ctor.value,true,'function',name):-1;
   this.get(this.env);if(n.superClass)this.expression(n.superClass);else this.lit(undefined);this.integer(ctorId);this.integer(this.c.constant(name));this.integer(+!!n.superClass);this.rt('makeClass');this.set(c);
   for(const m of n.body.body){
    if(m===ctor)continue;
    if(m.type==='StaticBlock'){this.get(c);this.lit(undefined);this.function(m._initializer,'',true);this.integer(1);this.integer(1);this.rt('classField');continue;}
    this.get(c);
    if(m.key.type==='PrivateIdentifier'){this.get(this.env);this.integer(this.c.constant(m.key.name));this.rt('privateKey');}else if(m.computed){this.expression(m.key);this.rt('key');}else this.lit(m.key.name??m.key.value);
    if(m.type==='MethodDefinition'){this.function(m.value,m.key.name||'',true);this.integer(m.kind==='get'?1:m.kind==='set'?2:0);this.integer(+m.static);this.rt('classMethod');}
    else{this.function(m._initializer,'',true);this.integer(+m.static);this.integer(m._nameFromKey?2:0);this.rt('classField');}
   }
   if(bindingName)this.initBinding(n._scope.bindings.get(bindingName),()=>this.get(c));
   this.get(c);this.rt('finishClass');
  });
  this.strict=strict;
 },
 statement(n,tag=null){
  switch(n.type){
   case'FunctionDeclaration':return;
   case'ExpressionStatement':if(n.directive)return;this.natural(n.expression);this.out(0x1a);return;
   case'ReturnStatement':
    if(this.retRepr||!(this.plan.node.async&&this.plan.node.generator)){this.returnValue(n.argument);return;}
    break;
   case'IfStatement':this.condition(n.test);this.ifElse(()=>this.statement(n.consequent),n.alternate?()=>this.statement(n.alternate):null);return;
   case'VariableDeclaration':
    if(!['var','let','const'].includes(n.kind))break;
    for(const d of n.declarations){
     if(d.id.type!=='Identifier'){if(d.init||n.kind!=='var')this.pattern(d.id,()=>d.init?this.expression(d.init,d.id._displayName||''):this.lit(undefined),n.kind);continue;}
     if(!d.init&&n.kind==='var')continue;
     this.initIdentifier(d.id,()=>d.init?this.natural(d.init,d.id._displayName||d.id.name):this.lit(undefined));
    }
    return;
   case'BlockStatement':this.enterScope(n._scope,()=>n.body.forEach(x=>this.statement(x)),n.body);return;
   case'ClassDeclaration':this.initIdentifier(n.id,()=>this.classExpression(n));return;
   case'ForStatement':case'WhileStatement':case'DoWhileStatement':{
    const scope=n.type==='ForStatement'?n._scope:null;
    this.enterScope(scope,()=>{
     if(n.init){if(n.init.type==='VariableDeclaration')this.statement(n.init);else{this.natural(n.init);this.out(0x1a);}}
     // Closures capture a fresh copy of the loop bindings per iteration.
     const copy=!!scope?.materialized,next=()=>{if(copy){this.get(this.env);this.rt('scopeClone');this.set(this.env);}};
     next();
     this.label('break',end=>this.label('loop',again=>{
      if(n.type!=='DoWhileStatement'&&n.test){this.condition(n.test);this.out(0x45);this.ifElse(()=>this.branch(end));}
      this.label('continue',()=>this.statement(n.body),0x40,tag);
      next();
      if(n.update){this.natural(n.update);this.out(0x1a);}
      if(n.type==='DoWhileStatement'){this.condition(n.test);this.ifElse(()=>this.branch(again));}else this.branch(again);
     }),0x40,tag);
    });return;
   }
   case'ForOfStatement':case'ForInStatement':{
    const iterator=this.local(),next=this.local(),lexical=n.left.type==='VariableDeclaration'&&n.left.kind!=='var';
    const head=()=>{this.expression(n.right);this.rt(n.await?'asyncIterator':n.type==='ForOfStatement'?'iterator':'keys');this.set(iterator);};
    if(lexical)this.enterScope(n._headScope,head);else head();
    this.label('break',end=>this.withFinally(()=>this.label('loop',again=>{
     const advance=()=>{this.get(iterator);this.rt(n.await?'asyncNext':'next');};
     if(n.await)this.suspend(advance,2);else advance();
     this.tee(next);this.rt('done');this.ifElse(()=>this.branch(end));
     const bind=()=>{
      const value=()=>{this.get(next);this.rt('value');};
      if(n.left.type==='VariableDeclaration')this.pattern(n.left.declarations[0].id,value,n.left.kind);else this.pattern(n.left,value);
      this.label('continue',()=>this.statement(n.body),0x40,tag);
     };
     if(lexical)this.enterScope(n._scope,bind);else bind();
     this.branch(again);
    }),c=>{if(n.await){this.suspend(()=>{this.get(iterator);this.get(c.state);this.integer(1);this.out(0x46);this.rt('asyncClose');},2);this.out(0x1a);}else this.closeIterator(iterator,c);}),0x40,tag);
    return;
   }
   case'SwitchStatement':{
    const value=this.local(),start=this.local(I32);this.expression(n.discriminant);this.set(value);this.integer(-1);this.set(start);
    const statements=n.cases.flatMap(c=>c.consequent);
    this.enterScope(n._scope,()=>{
     let def=-1;
     n.cases.forEach((c,i)=>{if(c.test===null){def=i;return;}this.get(start);this.integer(-1);this.out(0x46);this.ifElse(()=>{this.get(value);this.expression(c.test);this.rt('equal');this.ifElse(()=>{this.integer(i);this.set(start);});});});
     if(def>=0){this.get(start);this.integer(-1);this.out(0x46);this.ifElse(()=>{this.integer(def);this.set(start);});}
     this.label('break',()=>n.cases.forEach((c,i)=>{this.get(start);this.integer(0);this.out(0x4e);this.get(start);this.integer(i);this.out(0x4c,0x71);this.ifElse(()=>c.consequent.forEach(s=>this.statement(s)));}),0x40,tag);
    },statements);return;
   }
   case'TryStatement':{
    if(n.finalizer){this.withFinally(()=>this.statement({...n,finalizer:null}),()=>this.statement(n.finalizer));return;}
    if(!n.handler){this.statement(n.block);return;}
    this.out(0x06,0x40);this.labels.push({kind:'try'});this.statement(n.block);this.out(0x07,0);const caught=this.local();this.set(caught);
    this.enterScope(n.handler._scope,()=>{if(n.handler.param)this.pattern(n.handler.param,()=>this.get(caught),'let');this.statement(n.handler.body);});
    this.labels.pop();this.out(0x0b);return;
   }
  }
  this.featureStatement(n,tag);
 },
};

exports["bindings"]=bindings;
exports["SCOPE"]=SCOPE;
exports["reprOf"]=reprOf;
exports["VALUE_TYPE"]=VALUE_TYPE;

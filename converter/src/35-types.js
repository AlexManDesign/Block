// Whole-program type inference over the resolved bindings (scope.mjs).
// Lattice: bottom < int < num < any, bottom < bool < any. num is a JS number
// kept as f64, int a number known to be an int32 (kept as i32, never -0), bool
// an i32; any is the boxed dynamic value. Types are sound for the closed world
// of the compiled page: every call of a non-escaping function is a known
// direct call, so its parameter types are the join of the argument types.
// Typed arrays: tai has int32 elements (Int8/16/32, Uint8/16, Uint8Clamped),
// taf other numbers (Uint32, Float32/64). An element read is nu: a number, or
// undefined out of bounds; in numeric contexts both act like NaN.
// str is a string (the runtime's own UTF-16 string).
const NUM='num',INT='int',BOOL='bool',ANY='any',NU='nu',TAI='tai',TAF='taf',STR='str';
// String.prototype methods compiled to WASM, and their result types.
const STRING_RESULTS={charCodeAt:'num',charAt:'str',slice:'str',substring:'str',repeat:'str',concat:'str',indexOf:'int',lastIndexOf:'int',includes:'bool',startsWith:'bool',endsWith:'bool',toUpperCase:'str',toLowerCase:'str',trim:'str',trimStart:'str',trimEnd:'str',padStart:'str',padEnd:'str'};
const NUMBERS=new Set([NUM,INT,NU]);
const join=(a,b)=>{
 if(a===b)return a;if(a==null)return b;if(b==null)return a;
 if(NUMBERS.has(a)&&NUMBERS.has(b))return a===NU||b===NU?NU:NUM;
 if((a===TAI||a===TAF)&&(b===TAI||b===TAF))return TAF;
 return ANY;
};
const numeric=t=>t===NUM||t===INT||t===BOOL||t===NU;
const TYPED_ARRAYS={Int8Array:TAI,Uint8Array:TAI,Uint8ClampedArray:TAI,Int16Array:TAI,Uint16Array:TAI,Int32Array:TAI,Uint32Array:TAF,Float32Array:TAF,Float64Array:TAF};
const typedArray=t=>t===TAI||t===TAF;
const isInt32=v=>typeof v==='number'&&(v|0)===v&&!Object.is(v,-0);
const ARITHMETIC=new Set(['-','*','/','%','**']),BITWISE=new Set(['&','|','^','<<','>>']);
const RELATIONAL=new Set(['<','>','<=','>=','==','!=','===','!==','in','instanceof']);
const MATH=new Set(['abs','acos','acosh','asin','asinh','atan','atan2','atanh','cbrt','ceil','clz32','cos','cosh','exp','expm1','floor','fround','hypot','imul','log','log10','log1p','log2','max','min','pow','random','round','sign','sin','sinh','sqrt','tan','tanh','trunc']);
const NUMBER_GLOBALS=new Set(['Number','parseInt','parseFloat']),BOOL_GLOBALS=new Set(['isNaN','isFinite','Boolean']);
const FUNCTIONS=new Set(['FunctionDeclaration','FunctionExpression','ArrowFunctionExpression']);

function children(n,visit){
 for(const key in n){
  if(key==='loc'||key[0]==='_')continue;
  const v=n[key];
  if(Array.isArray(v)){for(const x of v)if(x&&typeof x==='object')visit(x);}
  else if(v&&typeof v==='object'&&typeof v.type==='string')visit(v);
 }
}

class TypeInference {
 constructor(analysis){
  this.a=analysis;this.types=new Map();this.changed=false;
  this.fns=[...new Set(analysis.functions.values())];
  this.globalNames=new Set();
 }
 // A global builtin we can rely on: not declared by the program and never
 // written through window/globalThis/self.
 builtin(n,name){
  if(name==='Math'&&this.a.mathWrites)return false;
  return n?.type==='Identifier'&&n.name===name&&n._ref&&!n._ref.binding&&!this.a.globalWrites.has(name)&&!this.a.dynamicGlobalWrites;
 }
 type(b){return this.types.get(b);}
 widen(b,t){if(!b)return;const old=this.types.get(b),next=join(old,t);if(next!==old){this.types.set(b,next);this.changed=true;}}
 // ---- structure: known callees, escapes, parameter flow ----
 knownFunction(b){
  if(!b||b.assigns||b.inits!==1)return null;
  if(b.storage==='global'&&(this.a.globalEscapes||this.a.dynamicGlobalWrites||this.a.globalWrites.has(b.name)||this.a.globalReads?.has(b.name)))return null;
  const node=b.fnNode;if(!node)return null;
  return this.a.functions.get(node)||null;
 }
 structure(){
  // Bindings that hold one specific function for their whole lifetime.
  for(const fn of this.fns){
   const n=fn.node;if(!n||!FUNCTIONS.has(n.type))continue;
   fn.escapes=false;fn.calls=[];fn.usesThis=false;fn.binding=null;
   if(n.type==='FunctionDeclaration'&&n.id?._ref?.binding){n.id._ref.binding.fnNode=n;fn.binding=n.id._ref.binding;}
  }
  const visitDeclarator=d=>{if(d.id.type==='Identifier'&&d.init&&(d.init.type==='FunctionExpression'||d.init.type==='ArrowFunctionExpression')&&d.id._ref?.binding){const b=d.id._ref.binding;if(b.kind==='const'||b.kind==='let'){b.fnNode=d.init;this.a.functions.get(d.init).binding=b;}}};
  for(const fn of this.fns){
   const n=fn.node;if(!n)continue;
   const walk=(x,parent)=>{
    if(x.type==='VariableDeclarator')visitDeclarator(x);
    if(x!==n&&FUNCTIONS.has(x.type)){if(x.type==='ArrowFunctionExpression')this.arrows(x,fn);return;}
    if(x.type==='ThisExpression')fn.usesThis=true;
    if(x.type==='MetaProperty'||x.type==='Super')fn.usesThis=true;
    if(x.type==='PropertyDefinition'){if(x.computed)walk(x.key,x);return;}
    if(x.type==='StaticBlock')return;
    children(x,c=>walk(c,x));
   };
   walk(n,null);
  }
  for(const fn of this.fns){
   const n=fn.node;if(!n)continue;
   const walk=(x,parent,key)=>{
    if(x!==n&&FUNCTIONS.has(x.type))return;
    if(x.type==='PropertyDefinition'){if(x.computed)walk(x.key,x,'key');return;}
    if(x.type==='StaticBlock')return;
    if(x.type==='Identifier'&&x._ref){
     const b=x._ref.binding,callee=b?.fnNode;
     if(callee&&x._ref.mode!=='declare'&&x._ref.mode!=='update'&&x._ref.mode!=='write'){
      const target=this.a.functions.get(callee);
      if(parent?.type==='CallExpression'&&key==='callee'&&!parent.optional)target.calls.push({node:parent,caller:fn});
      else if(x._ref.mode!=='typeof')target.escapes=true;
     }
    }
    for(const k in x){
     if(k==='loc'||k[0]==='_')continue;
     const v=x[k];
     if(Array.isArray(v)){for(const c of v)if(c&&typeof c==='object')walk(c,x,k);}
     else if(v&&typeof v==='object'&&typeof v.type==='string')walk(v,x,k);
    }
   };
   walk(n,null,null);
  }
  // A function may be called directly when its binding never changes and it
  // needs no per-object record (self name, home object, arguments.callee).
  for(const fn of this.fns){
   const n=fn.node;if(!n||!FUNCTIONS.has(n.type))continue;
   const simple=n.params.every(p=>p.type==='Identifier'||p.type==='AssignmentPattern'&&p.left.type==='Identifier');
   const tdz=n.params.some(p=>(p.type==='AssignmentPattern'?p.left:p)._ref?.binding?.tdzChecks);
   fn.typedBody=simple&&!tdz&&!n.generator&&!n.async&&!fn.fscope&&!fn.usesArguments&&!fn.method&&!fn.classConstructor;
   // Direct calls pass `this` as undefined, which only strict code or code
   // that never reads `this` can observe correctly.
   fn.direct=fn.typedBody&&!!fn.binding&&!!this.knownFunction(fn.binding)&&!(fn.usesThis&&(fn.arrow||!fn.strict));
   if(!fn.direct||!fn.binding)fn.escapes=true;
   for(const c of fn.calls)if(c.node.arguments.some(a=>a.type==='SpreadElement'))fn.escapes=true;
  }
 }
 arrows(x,fn){
  // `this` inside an arrow is the enclosing function's `this`.
  const walk=y=>{if(y!==x&&FUNCTIONS.has(y.type)&&y.type!=='ArrowFunctionExpression')return;if(y.type==='ThisExpression'||y.type==='MetaProperty'||y.type==='Super')fn.usesThis=true;children(y,walk);};
  walk(x);
 }
 // ---- initial types ----
 initial(){
  const visit=scope=>{
   const owner=scope.fn,resumable=!!(owner?.node?.generator||owner?.node?.async);
   for(const b of scope.bindings.values()){
    let t;
    if(b.storage==='global'||resumable)t=ANY;
    else if(['arguments','catch','callee','class','class-inner','import','namespace','function','function-block'].includes(b.kind))t=ANY;
    else if(b.storage==='local'&&b.tdzChecks)t=ANY;
    else if(b.kind==='param'){
     const fn=b.scope.fn;
     if(fn.escapes||fn.mapped||fn.usesArguments)t=ANY;
    }
    else if(b.kind==='var'&&(b.captured||b.unsafeVar))t=ANY;
    if(t)this.types.set(b,t);
   }
   scope.children.forEach(visit);
  };
  visit(this.a.root);
 }
 varSafety(){
  // A var is typed only when it behaves like a let: one initialized
  // declaration and every read inside the same block, after it.
  for(const fn of this.fns){
   const n=fn.node;if(!n)continue;
   const blocks=[];
   const walk=x=>{
    if(x!==n&&FUNCTIONS.has(x.type))return;
    if(x.type==='PropertyDefinition'||x.type==='StaticBlock')return;
    const block=['BlockStatement','Program','ForStatement','ForInStatement','ForOfStatement','SwitchStatement'].includes(x.type);
    if(block)blocks.push(x);
    if(x.type==='VariableDeclaration'&&x.kind==='var')for(const d of x.declarations){
     if(d.id.type!=='Identifier'){for(const id of collect(d.id))if(id._ref?.binding)id._ref.binding.unsafeVar=true;continue;}
     const b=d.id._ref?.binding;if(!b)continue;
     const owner=blocks.at(-1);
     if(!d.init||b.varBlock||owner?.type==='SwitchStatement'||blocks.some(k=>k.type==='SwitchStatement'))b.unsafeVar=true;
     b.varBlock=owner;b.firstInit=d.end;
    }
    children(x,walk);
    if(block)blocks.pop();
   };
   walk(n);
  }
  for(const fn of this.fns){
   const n=fn.node;if(!n)continue;
   const walk=x=>{
    if(x!==n&&FUNCTIONS.has(x.type))return;
    if(x.type==='PropertyDefinition'||x.type==='StaticBlock')return;
    if(x.type==='Identifier'&&x._ref?.binding&&x._ref.mode!=='declare'){
     const b=x._ref.binding;
     if(b.kind==='var'&&!(b.varBlock&&x.start>=b.firstInit&&x.end<=b.varBlock.end&&x.start>=b.varBlock.start))b.unsafeVar=true;
    }
    children(x,walk);
   };
   walk(n);
  }
 }
 // ---- fixed point ----
 run(){
  this.structure();this.varSafety();this.initial();
  const settle=()=>{for(let round=0;round<100;round++){this.changed=false;for(const fn of this.fns)this.visitFunction(fn);if(!this.changed)return;}throw new Error('Внутренняя ошибка: вывод типов не сошёлся');};
  settle();
  // Whatever stayed unknown (dead code, functions that never return) is
  // dynamic; propagate that, then freeze and annotate every expression.
  const bottoms=scope=>{for(const b of scope.bindings.values())if(!this.types.has(b))this.types.set(b,ANY);scope.children.forEach(bottoms);};
  bottoms(this.a.root);
  for(const fn of this.fns)if(fn.ret==null)fn.ret=ANY;
  settle();
  const freeze=scope=>{for(const b of scope.bindings.values())b.type=this.types.get(b);scope.children.forEach(freeze);};
  freeze(this.a.root);
  this.annotate=true;for(const fn of this.fns)this.visitFunction(fn);
 }
 visitFunction(fn){
  const n=fn.node;if(!n)return;
  this.fn=fn;
  if(fn.kind==='script'||fn.kind==='module'){n.body.forEach(s=>this.statement(s));return;}
  for(const p of n.params){
   if(p.type==='Identifier')continue;
   if(p.type==='AssignmentPattern'&&p.left.type==='Identifier'){const t=this.expr(p.right);this.widen(p.left._ref.binding,t);}
   else this.pattern(p);
  }
  if(n.body.type==='BlockStatement'){
   n.body.body.forEach(s=>this.statement(s));
   if(this.completes(n.body))fn.ret=join(fn.ret,ANY);
  }else fn.ret=join(fn.ret,this.expr(n.body));
 }
 // Conservative "may reach the end of the block" test.
 completes(n){
  if(!n)return true;
  switch(n.type){
   case'ReturnStatement':case'ThrowStatement':return false;
   case'BlockStatement':return n.body.length===0||this.completes(n.body[n.body.length-1])&&!n.body.some(s=>s.type==='ReturnStatement'||s.type==='ThrowStatement');
   case'IfStatement':return !n.alternate||this.completes(n.consequent)||this.completes(n.alternate);
   default:return true;
  }
 }
 pattern(p){
  // Destructuring and other non-trivial writes produce dynamic values.
  for(const id of collect(p))this.widen(id._ref?.binding,ANY);
  const walkDefaults=x=>{if(!x)return;if(x.type==='AssignmentPattern'){this.expr(x.right);walkDefaults(x.left);}else if(x.type==='ArrayPattern')x.elements.forEach(walkDefaults);else if(x.type==='ObjectPattern')x.properties.forEach(q=>{if(q.computed)this.expr(q.key);walkDefaults(q.type==='RestElement'?q.argument:q.value);});else if(x.type==='RestElement')walkDefaults(x.argument);else if(x.type==='MemberExpression')this.expr(x);};
  walkDefaults(p);
 }
 statement(n){
  if(!n)return;
  switch(n.type){
   case'VariableDeclaration':
    for(const d of n.declarations){
     if(d.id.type==='Identifier'){const b=d.id._ref?.binding;const t=d.init?this.expr(d.init):ANY;if(d.init||n.kind!=='var')this.widen(b,t);}
     else{if(d.init)this.expr(d.init);this.pattern(d.id);}
    }return;
   case'FunctionDeclaration':case'EmptyStatement':case'DebuggerStatement':case'BreakStatement':case'ContinueStatement':return;
   case'ClassDeclaration':this.expr(n);return;
   case'ReturnStatement':this.fn.ret=join(this.fn.ret,n.argument?this.expr(n.argument):ANY);return;
   case'ForInStatement':case'ForOfStatement':
    this.expr(n.right);
    if(n.left.type==='VariableDeclaration')this.pattern(n.left.declarations[0].id);else this.pattern(n.left);
    this.statement(n.body);return;
   case'TryStatement':this.statement(n.block);if(n.handler){if(n.handler.param)this.pattern(n.handler.param);this.statement(n.handler.body);}this.statement(n.finalizer);return;
  }
  if(n.type==='ForStatement'&&this.induction(n))return;
  // Generic: expressions and nested statements.
  children(n,c=>{if(c.type.endsWith('Statement')||c.type==='VariableDeclaration'||c.type==='FunctionDeclaration'||c.type==='ClassDeclaration'||c.type==='SwitchCase')this.statement(c);else this.expr(c);});
 }
 // for (...; i < e; i++) with i and e int32 and i changed nowhere else: the
 // test bounds i below 2^31-1 before every increment, so i stays int32
 // (likewise i > e with i--).
 induction(n){
  const u=n.update,t=n.test;
  if(!u||u.type!=='UpdateExpression'||u.argument.type!=='Identifier'||!t||t.type!=='BinaryExpression')return false;
  const b=u.argument._ref?.binding;
  if(!b||b.storage==='global'||b.assigns!==1)return false;
  const up=u.operator==='++',ref=x=>x.type==='Identifier'&&x._ref?.binding===b;
  let bound,ok=false;
  if(ref(t.left)&&(up?t.operator==='<':t.operator==='>'))bound=t.right;
  else if(ref(t.right)&&(up?t.operator==='>':t.operator==='<'))bound=t.left;
  else return false;
  if(n.init){if(n.init.type==='VariableDeclaration')this.statement(n.init);else this.expr(n.init);}
  const l=this.expr(t.left),r=this.expr(t.right);if(this.annotate)t._t=BOOL;
  // Optimistic while types are still settling: unknown counts as int until
  // the fixed point proves otherwise (then i widens and stays num).
  const bt=bound===t.left?l:r;
  ok=(bt==null||bt===INT)&&(this.types.get(b)??INT)===INT;
  if(ok){this.expr(u.argument);if(this.annotate){u._t=INT;u._induction=true;}}else this.expr(u);
  this.statement(n.body);
  return true;
 }
 // Static type of an expression; also propagates assignments and calls.
 expr(n){
  const t=this.exprType(n);
  if(this.annotate)n._t=t??ANY;
  return t;
 }
 exprType(n){
  switch(n.type){
   case'Literal':return typeof n.value==='number'?(isInt32(n.value)?INT:NUM):typeof n.value==='boolean'?BOOL:typeof n.value==='string'?STR:ANY;
   case'TemplateLiteral':n.expressions.forEach(e=>this.expr(e));return STR;
   case'Identifier':{
    const b=n._ref?.binding;
    if(!b)return n.name==='NaN'||n.name==='Infinity'?NUM:ANY;
    return this.types.get(b)??null;
   }
   case'UnaryExpression':{
    const t=this.expr(n.argument);
    if(n.operator==='!'||n.operator==='delete')return BOOL;
    if(n.operator==='+')return t===INT?INT:NUM;
    if(n.operator==='-'&&n.argument.type==='Literal'&&typeof n.argument.value==='number')return isInt32(-n.argument.value)?INT:NUM;
    if(n.operator==='~')return numeric(t)?INT:t==null?null:ANY;
    if(n.operator==='-')return numeric(t)?NUM:t==null?null:ANY;
    return ANY;
   }
   case'UpdateExpression':{
    const t=this.expr(n.argument);
    const r=numeric(t)?NUM:t==null?null:ANY;
    if(n.argument.type==='Identifier')this.widen(n.argument._ref?.binding,r===null?null:r);
    return r;
   }
   case'BinaryExpression':{
    const a=this.expr(n.left),b=this.expr(n.right);
    return this.binaryType(n.operator,a,b);
   }
   case'LogicalExpression':return join(this.expr(n.left),this.expr(n.right));
   case'ConditionalExpression':this.expr(n.test);return join(this.expr(n.consequent),this.expr(n.alternate));
   case'SequenceExpression':{let t;for(const e of n.expressions)t=this.expr(e);return t;}
   case'AssignmentExpression':{
    if(n.left.type==='Identifier'){
     const b=n.left._ref?.binding;let t;
     if(n.operator==='=')t=this.expr(n.right);
     else if(['&&=','||=','??='].includes(n.operator))t=join(this.expr(n.left),this.expr(n.right));
     else t=this.binaryType(n.operator.slice(0,-1),this.expr(n.left),this.expr(n.right));
     if(b&&b.storage!=='global')this.widen(b,t);
     return t;
    }
    if(n.left.type==='ArrayPattern'||n.left.type==='ObjectPattern'){const t=this.expr(n.right);this.pattern(n.left);return t;}
    const l=this.expr(n.left),r=this.expr(n.right);
    if(n.operator==='=')return r;
    if(['&&=','||=','??='].includes(n.operator))return ANY;
    return this.binaryType(n.operator.slice(0,-1),l===NU?NU:ANY,r);
   }
   case'CallExpression':return this.call(n);
   case'ChainExpression':this.expr(n.expression);return ANY;
   case'NewExpression':{
    n.arguments.forEach(a=>this.expr(a.type==='SpreadElement'?a.argument:a));
    if(n.callee.type==='Identifier'&&TYPED_ARRAYS[n.callee.name]&&this.builtin(n.callee,n.callee.name))return TYPED_ARRAYS[n.callee.name];
    this.expr(n.callee);return ANY;
   }
   case'MemberExpression':{
    if(n.optional||n.object.type==='Super'){children(n,c=>{if(c.type!=='PrivateIdentifier')this.expr(c);});return ANY;}
    const o=this.expr(n.object);
    if(n.computed){
     const k=this.expr(n.property);
     if(typedArray(o)&&numeric(k)){if(this.annotate)n._elem=o;return NU;}
     return ANY;
    }
    if(typedArray(o)&&n.property.name==='length'){if(this.annotate)n._taLength=true;return NUM;}
    if(o===STR&&n.property.name==='length'){if(this.annotate)n._strLength=true;return INT;}
    return ANY;
   }
   case'FunctionExpression':case'ArrowFunctionExpression':return ANY;
   case'ClassExpression':case'ClassDeclaration':
    if(n.superClass)this.expr(n.superClass);
    for(const m of n.body.body)if(m.computed)this.expr(m.key);
    return ANY;
   case'ObjectExpression':for(const p of n.properties){if(p.type==='SpreadElement'){this.expr(p.argument);continue;}if(p.computed)this.expr(p.key);if(!(p.method||p.kind==='get'||p.kind==='set'))this.expr(p.value);}return ANY;
  }
  children(n,c=>{if(c.type!=='TemplateElement'&&c.type!=='PrivateIdentifier'&&c.type!=='Super')this.expr(c);});
  return ANY;
 }
 binaryType(op,a,b){
  if(RELATIONAL.has(op))return BOOL;
  // A string operand makes + a concatenation.
  if(op==='+'&&(a===STR||b===STR))return STR;
  const bits=BITWISE.has(op);
  if(a==null||b==null)return a==null&&b==null?null:numeric(a)||numeric(b)?(op==='+'?null:bits?INT:NUM):null;
  if(op==='>>>')return NUM;
  if(op==='+')return numeric(a)&&numeric(b)?NUM:ANY;
  // Mixing a Number with a BigInt throws, so one numeric operand fixes the result.
  if(bits)return numeric(a)||numeric(b)?INT:ANY;
  if(ARITHMETIC.has(op))return numeric(a)||numeric(b)?NUM:ANY;
  return ANY;
 }
 call(n){
  const callee=n.callee;
  if(callee.type==='Identifier'){
   const b=callee._ref?.binding,fn=b?.fnNode?this.a.functions.get(b.fnNode):null;
   const args=n.arguments.map(a=>this.expr(a.type==='SpreadElement'?a.argument:a));
   if(fn&&fn.direct&&!fn.escapes&&!n.arguments.some(x=>x.type==='SpreadElement')){
    n._direct=fn;
    fn.node.params.forEach((p,i)=>{
     const id=p.type==='AssignmentPattern'?p.left:p,param=id._ref.binding;
     if(i<args.length)this.widen(param,n.arguments[i]&&n.arguments[i].type!=='SpreadElement'?args[i]:ANY);
     else if(p.type!=='AssignmentPattern')this.widen(param,ANY);
    });
    return fn.ret??null;
   }
   if(fn&&fn.direct&&!n.arguments.some(x=>x.type==='SpreadElement')){n._direct=fn;return fn.ret??null;}
   if(!b&&(NUMBER_GLOBALS.has(callee.name)||BOOL_GLOBALS.has(callee.name))&&this.builtin(callee,callee.name))return NUMBER_GLOBALS.has(callee.name)?NUM:BOOL;
   if(!b&&callee.name==='String'&&this.builtin(callee,'String'))return STR;
   return ANY;
  }
  if(callee.type==='MemberExpression'&&!callee.computed&&!callee.optional&&this.builtin(callee.object,'Math')&&MATH.has(callee.property.name)){
   n.arguments.forEach(a=>this.expr(a.type==='SpreadElement'?a.argument:a));
   if(n.arguments.some(a=>a.type==='SpreadElement'))return NUM;
   n._math=callee.property.name;return callee.property.name==='imul'||callee.property.name==='clz32'?INT:NUM;
  }
  const receiver=callee.type==='MemberExpression'&&!callee.computed&&!callee.optional?this.expr(callee.object):this.expr(callee);
  const argTypes=n.arguments.map(a=>this.expr(a.type==='SpreadElement'?a.argument:a));
  if(callee.type==='MemberExpression'&&!callee.computed&&!callee.optional&&!n.optional&&!this.a.stringPrototype&&!n.arguments.some(a=>a.type==='SpreadElement')){
   const name=callee.property.name;
   if(receiver===STR&&Object.hasOwn(STRING_RESULTS,name)){if(this.annotate)n._strMethod={name,args:argTypes};return STRING_RESULTS[name];}
   if(name==='fromCharCode'&&this.builtin(callee.object,'String')){if(this.annotate&&argTypes.length===1)n._fromCharCode=true;return STR;}
  }
  // slice/subarray of a typed array keep its element kind.
  if(callee.type==='MemberExpression'&&!callee.computed&&typedArray(receiver)&&['slice','subarray'].includes(callee.property.name))return receiver;
  return ANY;
 }
}
function collect(p,out=[]){
 if(!p)return out;
 switch(p.type){
  case'Identifier':out.push(p);break;
  case'AssignmentPattern':collect(p.left,out);break;
  case'RestElement':collect(p.argument,out);break;
  case'ArrayPattern':p.elements.forEach(e=>collect(e,out));break;
  case'ObjectPattern':p.properties.forEach(q=>collect(q.type==='RestElement'?q.argument:q.value,out));break;
 }
 return out;
}

exports["TypeInference"]=TypeInference;
exports["NUM"]=NUM;
exports["INT"]=INT;
exports["NU"]=NU;
exports["STR"]=STR;
exports["TAI"]=TAI;
exports["TAF"]=TAF;
exports["BOOL"]=BOOL;
exports["ANY"]=ANY;

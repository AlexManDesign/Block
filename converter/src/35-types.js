// Whole-program type inference over the resolved bindings (scope.mjs).
// Lattice: bottom < num | bool < any. num is a JS number kept as f64, bool an
// i32; any is the boxed dynamic value. Types are sound for the closed world
// of the compiled page: every call of a non-escaping function is a known
// direct call, so its parameter types are the join of the argument types.
const NUM='num',BOOL='bool',ANY='any';
const join=(a,b)=>a===b?a:a==null?b:b==null?a:ANY;
const numeric=t=>t===NUM||t===BOOL;
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
  // Generic: expressions and nested statements.
  children(n,c=>{if(c.type.endsWith('Statement')||c.type==='VariableDeclaration'||c.type==='FunctionDeclaration'||c.type==='ClassDeclaration'||c.type==='SwitchCase')this.statement(c);else this.expr(c);});
 }
 // Static type of an expression; also propagates assignments and calls.
 expr(n){
  const t=this.exprType(n);
  if(this.annotate)n._t=t??ANY;
  return t;
 }
 exprType(n){
  switch(n.type){
   case'Literal':return typeof n.value==='number'?NUM:typeof n.value==='boolean'?BOOL:ANY;
   case'Identifier':{
    const b=n._ref?.binding;
    if(!b)return n.name==='NaN'||n.name==='Infinity'?NUM:ANY;
    return this.types.get(b)??null;
   }
   case'UnaryExpression':{
    const t=this.expr(n.argument);
    if(n.operator==='!'||n.operator==='delete')return BOOL;
    if(n.operator==='+')return NUM;
    if(n.operator==='-'||n.operator==='~')return numeric(t)?NUM:t==null?null:ANY;
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
    this.expr(n.left);const r=this.expr(n.right);
    if(n.operator==='=')return r;
    if(['&&=','||=','??='].includes(n.operator))return ANY;
    return this.binaryType(n.operator.slice(0,-1),ANY,r);
   }
   case'CallExpression':return this.call(n);
   case'ChainExpression':this.expr(n.expression);return ANY;
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
  if(a==null||b==null)return a==null&&b==null?null:numeric(a)||numeric(b)?(op==='+'?null:NUM):null;
  if(op==='>>>')return NUM;
  if(op==='+')return numeric(a)&&numeric(b)?NUM:ANY;
  // Mixing a Number with a BigInt throws, so one numeric operand fixes the result.
  if(ARITHMETIC.has(op)||BITWISE.has(op))return numeric(a)||numeric(b)?NUM:ANY;
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
   return ANY;
  }
  if(callee.type==='MemberExpression'&&!callee.computed&&!callee.optional&&this.builtin(callee.object,'Math')&&MATH.has(callee.property.name)){
   n.arguments.forEach(a=>this.expr(a.type==='SpreadElement'?a.argument:a));
   if(n.arguments.some(a=>a.type==='SpreadElement'))return NUM;
   n._math=callee.property.name;return NUM;
  }
  this.expr(callee);n.arguments.forEach(a=>this.expr(a.type==='SpreadElement'?a.argument:a));
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
exports["BOOL"]=BOOL;
exports["ANY"]=ANY;

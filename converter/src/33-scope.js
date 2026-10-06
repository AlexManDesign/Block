// Static scope analysis for the whole program. Every identifier is resolved
// at compile time; nothing is looked up by name while the program runs.
//  - a binding no closure captures becomes a WASM local of its function;
//  - a captured binding gets a slot in a runtime scope record (WASM-GC array,
//    element 0 = parent record), addressed by (hops, slot);
//  - top-level var/function of classic scripts and unresolved names live on
//    the JS global object, as the language requires.
const FUNCTIONS=new Set(['FunctionDeclaration','FunctionExpression','ArrowFunctionExpression']);
const LEXICAL=new Set(['let','const','class']);
function fail(n,message){throw new SyntaxError(message+(n?.loc?` (строка ${n.loc.start.line}, столбец ${n.loc.start.column+1})`:''));}
function patternNames(n,out=[]){
 if(!n)return out;
 switch(n.type){
  case'Identifier':out.push(n);break;
  case'AssignmentPattern':patternNames(n.left,out);break;
  case'RestElement':patternNames(n.argument,out);break;
  case'ArrayPattern':n.elements.forEach(e=>patternNames(e,out));break;
  case'ObjectPattern':n.properties.forEach(p=>patternNames(p.type==='RestElement'?p.argument:p.value,out));break;
  case'MemberExpression':break;
  default:fail(n,'Неподдерживаемая привязка '+n.type);
 }
 return out;
}

class Binding {
 constructor(name,kind,scope,node){
  this.name=name;this.kind=kind;this.scope=scope;this.node=node;
  this.captured=false;this.storage=null;this.slot=0;this.inits=0;this.assigns=0;this.reads=0;
  // Position after which the binding is initialized; reads that provably come
  // later in the same function need no TDZ check.
  this.ready=Infinity;this.tdzChecks=false;
 }
 get lexical(){return LEXICAL.has(this.kind);}
 get constant(){return this.kind==='const'||this.kind==='class-inner'||this.kind==='import'||this.kind==='namespace'||this.kind==='callee';}
 get mutable(){return !this.constant;}
}

class Scope {
 constructor(kind,parent,fn,node){
  this.kind=kind;this.parent=parent;this.fn=fn;this.node=node;this.bindings=new Map();
  this.materialized=false;this.forced=false;this.slots=0;this.children=[];this.strict=!!parent?.strict;
  if(parent)parent.children.push(this);
 }
 lookupLocal(name){return this.bindings.get(name);}
}

class FunctionInfo {
 constructor(node,outer,kind){
  this.node=node;this.outer=outer;this.kind=kind;this.scope=null;this.paramScope=null;this.bodyScope=null;
  this.fscopeNode=null;this.fscope=false;this.usesArguments=false;this.mapped=false;this.argumentsBinding=null;
  this.strict=false;this.arrow=node?.type==='ArrowFunctionExpression';this.method=false;this.classConstructor=false;
 }
}

class ScopeAnalysis {
 constructor(){
  this.root=new Scope('root',null,null,null);this.root.forced=true;
  this.globals=new Map();this.scriptErrors=new Map();this.modules=new Map();this.functions=new Map();
  this.globalWrites=new Set();this.dynamicGlobalWrites=false;
 }
 // ---- pass 1: scopes and declarations ----
 declare(scope,name,kind,node){
  const existing=scope.bindings.get(name);
  if(existing){
   if(existing.lexical||LEXICAL.has(kind))fail(node,'Повторное объявление '+name);
   if(kind==='function'&&existing.kind!=='param')existing.kind='function';
   return existing;
  }
  const b=new Binding(name,kind,scope,node);scope.bindings.set(name,b);return b;
 }
 hoistVars(nodes,scope,fn){
  const visit=n=>{
   if(!n||typeof n!=='object')return;
   if(Array.isArray(n)){n.forEach(visit);return;}
   if(FUNCTIONS.has(n.type)||n.type==='ClassDeclaration'||n.type==='ClassExpression')return;
   if(n.type==='VariableDeclaration'&&n.kind==='var')for(const d of n.declarations)for(const id of patternNames(d.id))this.declareVar(scope,id.name,id,fn);
   for(const key in n){if(key==='loc'||key[0]==='_')continue;const v=n[key];if(v&&typeof v==='object')visit(v);}
  };
  visit(nodes);
 }
 declareVar(scope,name,node,fn){
  if(scope.kind==='script'){this.declareGlobal(scope,name,'var',node);return;}
  const b=this.declare(scope,name,'var',node);b.ready=-Infinity;
 }
 declareGlobal(scope,name,kind,node){
  const script=scope.node,existing=this.root.bindings.get(name);
  if(existing&&existing.lexical){this.scriptError(script,name);return;}
  let b=existing;if(!b){b=new Binding(name,kind,this.root,node);b.storage='global';b.ready=-Infinity;this.root.bindings.set(name,b);}
  else if(kind==='function')b.kind='function';
  (script._globalDecls??=[]).push({name,kind,node});
 }
 scriptError(script,name){if(!this.scriptErrors.has(script))this.scriptErrors.set(script,name);}
 lexicalDeclarations(statements,scope,fn,top){
  for(const n of statements){
   const s=n.type==='ExportNamedDeclaration'||n.type==='ExportDefaultDeclaration'?n.declaration:n;
   if(!s)continue;
   if(s.type==='VariableDeclaration'&&s.kind!=='var'){
    if(!['let','const'].includes(s.kind))fail(s,'Декларация '+s.kind+' ещё не реализована');
    for(const d of s.declarations)for(const id of patternNames(d.id))this.declareLexical(scope,id.name,s.kind,id,d.end);
   }
   else if(s.type==='ClassDeclaration')this.declareLexical(scope,s.id.name,'class',s.id,s.end);
   else if(s.type==='FunctionDeclaration'){
    if(top){
     if(scope.kind==='script')this.declareGlobal(scope,s.id.name,'function',s.id);
     else{const b=this.declare(scope,s.id.name,'function',s.id);b.ready=-Infinity;}
    }else{
     if(!scope.strict)fail(s,'Функция в нестрогом блоке пока не поддержана');
     const b=this.declareLexical(scope,s.id.name,'let',s.id,-Infinity);b.kind='function-block';
    }
   }
  }
 }
 declareLexical(scope,name,kind,node,ready){
  if(scope.kind==='script'){
   const existing=this.root.bindings.get(name);
   if(existing){this.scriptError(scope.node,name);return existing;}
   const b=new Binding(name,kind,this.root,node);b.ready=Infinity;this.root.bindings.set(name,b);b.script=scope.node;return b;
  }
  const b=this.declare(scope,name,kind,node);b.ready=ready;return b;
 }
 // Creates the scope tree of a top-level plan (script, handler, module).
 program(plan){
  const node=plan.node;
  if(plan.kind==='script'){
   const fn=new FunctionInfo(node,this.root,'script');fn.strict=plan.strict;this.functions.set(node,fn);
   const scope=new Scope('script',this.root,fn,node);scope.strict=fn.strict;fn.scope=scope;node._scope=scope;
   this.hoistVars(node.body,scope,fn);this.lexicalDeclarations(node.body,scope,fn,true);
   node.body.forEach(s=>this.statement(s,scope,fn));
   return fn;
  }
  if(plan.kind==='module-init'||plan.kind==='module-body'){
   const known=this.modules.get(node._moduleId);
   if(known){this.functions.set(node,known.fn);node._scope=known;return known.fn;}
   const fn=new FunctionInfo(node,this.root,'module');fn.strict=true;this.functions.set(node,fn);
   const scope=new Scope('module',this.root,fn,node);scope.forced=true;scope.strict=true;fn.scope=scope;node._scope=scope;
   for(const [local,target]of node._imports||[]){const b=this.declare(scope,local,target.namespace?'namespace':'import',null);b.importTarget=target;b.ready=target.namespace?-Infinity:Infinity;}
   this.hoistVars(node.body,scope,fn);this.lexicalDeclarations(node.body,scope,fn,true);
   node.body.forEach(s=>this.statement(s,scope,fn));
   this.modules.set(node._moduleId,scope);
   return fn;
  }
  // handler
  return this.functionNode(node,this.root,{handler:true});
 }
 functionNode(n,outer,{method=false,classConstructor=false,handler=false}={}){
  const fn=new FunctionInfo(n,outer,handler?'handler':'function');this.functions.set(n,fn);
  fn.method=method;fn.classConstructor=classConstructor;
  fn.strict=!!(classConstructor||outer.strict||n.body?.body?.some?.(s=>s.directive==='use strict'));
  const simple=n.params.every(p=>p.type==='Identifier');
  if(!fn.arrow){
   let uses=false;
   const visit=x=>{if(!x||typeof x!=='object')return;if(Array.isArray(x)){x.forEach(visit);return;}if(x.type==='FunctionDeclaration'||x.type==='FunctionExpression')return;if(x.type==='Identifier'&&x.name==='arguments')uses=true;for(const k in x){if(k==='loc'||k[0]==='_')continue;const v=x[k];if(v&&typeof v==='object')visit(v);}};
   visit(n.params);visit(n.body);
   fn.usesArguments=uses&&!n.params.some(p=>patternNames(p).some(id=>id.name==='arguments'));
  }
  const named=n.type==='FunctionExpression'&&!!n.id;
  fn.fscope=!!(named||method||fn.usesArguments||classConstructor);
  // Per-function-object record created by the runtime: [parent, function].
  // Holds the self binding of a named function expression and the
  // callee/home data used by arguments.callee and super.
  if(fn.fscope){
   const f=new Scope('fscope',outer,fn,n);f.forced=true;f.strict=fn.strict;fn.fscopeNode=f;
   if(named){const b=this.declare(f,n.id.name,'callee',n.id);b.ready=-Infinity;}
   outer=f;
  }
  const scope=new Scope('function',outer,fn,n);scope.strict=fn.strict;fn.scope=scope;fn.paramScope=scope;n._scope=scope;
  const params=[];
  for(const p of n.params)for(const id of patternNames(p)){
   if(scope.bindings.has(id.name)){if(fn.strict||!simple||fn.arrow)fail(p,'Повторные параметры в строгой или непростой функции');continue;}
   const b=this.declare(scope,id.name,'param',id);b.ready=simple?-Infinity:p.end;params.push(b);
  }
  if(fn.usesArguments){
   fn.mapped=!fn.strict&&simple;
   const b=this.declare(scope,'arguments','arguments',null);b.ready=-Infinity;fn.argumentsBinding=b;
   // A mapped arguments object aliases the parameters: keep them in the record.
   if(fn.mapped)for(const p of params)p.captured=true;
  }
  // Parameter expressions see parameters only; with non-simple parameters
  // the body gets its own var scope (copies of same-named parameters).
  let bodyScope=scope;
  if(!simple&&n.body.type==='BlockStatement'){
   bodyScope=new Scope('params-body',scope,fn,n.body);bodyScope.strict=fn.strict;fn.bodyScope=bodyScope;
   for(const p of n.params)this.pattern(p,scope,fn);
  }else{fn.bodyScope=scope;for(const p of n.params)this.pattern(p,scope,fn);}
  if(n.body.type==='BlockStatement'){
   this.hoistVars(n.body.body,bodyScope,fn);this.lexicalDeclarations(n.body.body,bodyScope,fn,true);
   n.body.body.forEach(s=>this.statement(s,bodyScope,fn));
  }else this.expression(n.body,scope,fn);
  return fn;
 }
 block(statements,scope,fn,node,kind='block'){
  const inner=new Scope(kind,scope,fn,node);node._scope=inner;
  this.lexicalDeclarations(statements,inner,fn,false);
  return inner;
 }
 statement(n,scope,fn){
  if(!n)return;
  switch(n.type){
   case'EmptyStatement':case'DebuggerStatement':return;
   case'ExpressionStatement':this.expression(n.expression,scope,fn);return;
   case'BlockStatement':{const inner=this.block(n.body,scope,fn,n);n.body.forEach(s=>this.statement(s,inner,fn));return;}
   case'StaticBlock':{const inner=this.block(n.body,scope,fn,n);n.body.forEach(s=>this.statement(s,inner,fn));return;}
   case'VariableDeclaration':for(const d of n.declarations){this.pattern(d.id,scope,fn);if(d.init)this.expression(d.init,scope,fn);}return;
   case'FunctionDeclaration':this.reference(n.id,scope,fn,'declare');n._outerScope=scope;this.functionNode(n,scope);return;
   case'ClassDeclaration':this.reference(n.id,scope,fn,'declare');this.classNode(n,scope,fn);return;
   case'ReturnStatement':case'ThrowStatement':if(n.argument)this.expression(n.argument,scope,fn);return;
   case'IfStatement':this.expression(n.test,scope,fn);this.statement(n.consequent,scope,fn);this.statement(n.alternate,scope,fn);return;
   case'LabeledStatement':this.statement(n.body,scope,fn);return;
   case'BreakStatement':case'ContinueStatement':return;
   case'WhileStatement':case'DoWhileStatement':this.expression(n.test,scope,fn);this.statement(n.body,scope,fn);return;
   case'ForStatement':{
    let inner=scope;
    if(n.init?.type==='VariableDeclaration'&&n.init.kind!=='var'){inner=new Scope('for',scope,fn,n);n._scope=inner;this.lexicalDeclarations([n.init],inner,fn,false);}
    if(n.init){if(n.init.type==='VariableDeclaration')this.statement(n.init,inner,fn);else this.expression(n.init,inner,fn);}
    if(n.test)this.expression(n.test,inner,fn);if(n.update)this.expression(n.update,inner,fn);this.statement(n.body,inner,fn);return;
   }
   case'ForInStatement':case'ForOfStatement':{
    if(n.left.type==='VariableDeclaration'&&n.left.kind!=='var'){
     // TDZ scope for the head expression, then a fresh scope per iteration.
     const head=new Scope('for-head',scope,fn,n.right);n._headScope=head;this.lexicalDeclarations([n.left],head,fn,false);
     for(const b of head.bindings.values())b.ready=Infinity;
     this.expression(n.right,head,fn);
     const iter=new Scope('for-iteration',scope,fn,n);n._scope=iter;this.lexicalDeclarations([n.left],iter,fn,false);
     for(const b of iter.bindings.values())b.ready=n.left.end;
     this.statement(n.left,iter,fn);this.statement(n.body,iter,fn);return;
    }
    this.expression(n.right,scope,fn);
    if(n.left.type==='VariableDeclaration')this.statement(n.left,scope,fn);else this.pattern(n.left,scope,fn,true);
    this.statement(n.body,scope,fn);return;
   }
   case'SwitchStatement':{
    this.expression(n.discriminant,scope,fn);
    const inner=this.block(n.cases.flatMap(c=>c.consequent),scope,fn,n,'switch');
    for(const b of inner.bindings.values())if(b.ready!==-Infinity)b.ready=Infinity;
    for(const c of n.cases){if(c.test)this.expression(c.test,inner,fn);c.consequent.forEach(s=>this.statement(s,inner,fn));}return;
   }
   case'TryStatement':
    this.statement(n.block,scope,fn);
    if(n.handler){
     const catchScope=new Scope('catch',scope,fn,n.handler);n.handler._scope=catchScope;
     if(n.handler.param){for(const id of patternNames(n.handler.param)){const b=this.declare(catchScope,id.name,'catch',id);b.ready=n.handler.param.type==='Identifier'?-Infinity:n.handler.param.end;}this.pattern(n.handler.param,catchScope,fn);}
     this.statement(n.handler.body,catchScope,fn);
    }
    this.statement(n.finalizer,scope,fn);return;
   case'WithStatement':fail(n,'with не поддерживается AOT-компилятором');
  }
  fail(n,'Конструкция ещё не реализована: '+n.type);
 }
 pattern(n,scope,fn,assign=false){
  if(!n)return;
  switch(n.type){
   case'Identifier':this.reference(n,scope,fn,assign?'write':'declare');return;
   case'MemberExpression':this.expression(n,scope,fn);return;
   case'AssignmentPattern':this.pattern(n.left,scope,fn,assign);this.expression(n.right,scope,fn);return;
   case'RestElement':this.pattern(n.argument,scope,fn,assign);return;
   case'ArrayPattern':n.elements.forEach(e=>this.pattern(e,scope,fn,assign));return;
   case'ObjectPattern':for(const p of n.properties){if(p.type==='RestElement'){this.pattern(p.argument,scope,fn,assign);continue;}if(p.computed)this.expression(p.key,scope,fn);this.pattern(p.value,scope,fn,assign);}return;
  }
  fail(n,'Шаблон ещё не реализован: '+n.type);
 }
 classNode(n,scope,fn){
  const inner=new Scope('class',scope,fn,n);inner.strict=true;n._scope=inner;
  if(n.id){const b=this.declare(inner,n.id.name,'class-inner',n.id);b.ready=n.end;}
  if(n.superClass)this.expression(n.superClass,inner,fn);
  if(n.body.body.some(m=>m.key?.type==='PrivateIdentifier'))inner.forced=true;
  const ctor=n.body.body.find(m=>m.type==='MethodDefinition'&&m.kind==='constructor');
  for(const m of n.body.body){
   if(m.computed)this.expression(m.key,inner,fn);
   if(m.type==='MethodDefinition'){m.value._outerScope=inner;this.functionNode(m.value,inner,{method:m!==ctor,classConstructor:m===ctor});}
   else if(m.type==='PropertyDefinition'){
    // Field initializers run as hidden methods.
    const wrapper={type:'FunctionExpression',params:[],body:{type:'BlockStatement',body:[{type:'ReturnStatement',argument:m.value||null}]},start:m.start,end:m.end,loc:m.loc,_synthetic:true};
    m._initializer=wrapper;wrapper._outerScope=inner;this.functionNode(wrapper,inner,{method:true});
   }
   else if(m.type==='StaticBlock'){
    const wrapper={type:'FunctionExpression',params:[],body:{type:'BlockStatement',body:m.body},start:m.start,end:m.end,loc:m.loc,_synthetic:true};
    m._initializer=wrapper;wrapper._outerScope=inner;this.functionNode(wrapper,inner,{method:true});
   }
   else fail(m,'Элемент класса ещё не реализован: '+m.type);
  }
 }
 expression(n,scope,fn){
  if(!n)return;
  switch(n.type){
   case'Identifier':this.reference(n,scope,fn,'read');return;
   case'Literal':case'ThisExpression':case'Super':case'MetaProperty':case'TemplateElement':case'PrivateIdentifier':return;
   case'ArrayExpression':n.elements.forEach(e=>this.expression(e,scope,fn));return;
   case'ObjectExpression':
    for(const p of n.properties){
     if(p.type==='SpreadElement'){this.expression(p.argument,scope,fn);continue;}
     if(p.computed)this.expression(p.key,scope,fn);
     if(p.method||p.kind==='get'||p.kind==='set'){p.value._outerScope=scope;this.functionNode(p.value,scope,{method:true});}
     else this.expression(p.value,scope,fn);
    }return;
   case'FunctionExpression':case'ArrowFunctionExpression':n._outerScope=scope;this.functionNode(n,scope);return;
   case'ClassExpression':this.classNode(n,scope,fn);return;
   case'SpreadElement':this.expression(n.argument,scope,fn);return;
   case'UnaryExpression':
    if(n.operator==='typeof'&&n.argument.type==='Identifier'){this.reference(n.argument,scope,fn,'typeof');return;}
    if(n.operator==='delete'&&n.argument.type==='Identifier'){this.reference(n.argument,scope,fn,'delete');return;}
    this.expression(n.argument,scope,fn);return;
   case'UpdateExpression':if(n.argument.type==='Identifier')this.reference(n.argument,scope,fn,'update');else{this.globalTarget(n.argument);this.expression(n.argument,scope,fn);}return;
   case'AssignmentExpression':
    if(n.left.type==='Identifier')this.reference(n.left,scope,fn,n.operator==='='?'write':'update');
    else if(n.left.type==='ArrayPattern'||n.left.type==='ObjectPattern')this.pattern(n.left,scope,fn,true);
    else{this.globalTarget(n.left);this.expression(n.left,scope,fn);}
    this.expression(n.right,scope,fn);return;
   case'BinaryExpression':case'LogicalExpression':this.expression(n.left,scope,fn);this.expression(n.right,scope,fn);return;
   case'MemberExpression':if(n.object.type==='Identifier')n.object._memberObject=true;this.expression(n.object,scope,fn);if(n.computed)this.expression(n.property,scope,fn);return;
   case'ConditionalExpression':this.expression(n.test,scope,fn);this.expression(n.consequent,scope,fn);this.expression(n.alternate,scope,fn);return;
   case'CallExpression':case'NewExpression':this.expression(n.callee,scope,fn);n.arguments.forEach(a=>this.expression(a,scope,fn));return;
   case'SequenceExpression':n.expressions.forEach(e=>this.expression(e,scope,fn));return;
   case'YieldExpression':case'AwaitExpression':this.expression(n.argument,scope,fn);return;
   case'TemplateLiteral':n.expressions.forEach(e=>this.expression(e,scope,fn));return;
   case'TaggedTemplateExpression':this.expression(n.tag,scope,fn);this.expression(n.quasi,scope,fn);return;
   case'ChainExpression':this.expression(n.expression,scope,fn);return;
   case'ImportExpression':this.expression(n.source,scope,fn);return;
  }
  fail(n,'Конструкция ещё не реализована: '+n.type);
 }
 // Writes to properties of the global object through window/globalThis/self.
 globalTarget(n){
  if(n.type!=='MemberExpression'||n.object.type!=='Identifier'||!['window','globalThis','self'].includes(n.object.name))return;
  if(n.computed)this.dynamicGlobalWrites=true;else this.globalWrites.add(n.property.name);
 }
 reference(n,scope,fn,mode){
  if(mode==='declare'){
   // Declaration sites resolve in their own scope (already declared in pass 1).
   let s=scope;while(s&&!s.bindings.has(n.name))s=s.parent;
   const binding=s?s.bindings.get(n.name):null;
   n._ref={binding,scope,fn,mode,check:false};
   if(binding){binding.inits++;this.capture(binding,fn);}
   return;
  }
  if(['eval','Function'].includes(n.name)&&mode==='read')fail(n,'Динамическое создание JS не поддерживается AOT-компилятором');
  let s=scope,binding=null;
  while(s){const b=s.bindings.get(n.name);if(b){binding=b;break;}s=s.parent;}
  n._ref={binding,scope,fn,mode,check:false};
  if(!binding){
   if(mode==='write'||mode==='update')this.globalWrites.add(n.name);
   if(mode==='read'&&['window','globalThis','self','top','parent','frames'].includes(n.name)&&!n._memberObject)this.globalEscapes=true;
   return;
  }
  if(mode==='write'||mode==='update')binding.assigns++;
  if(mode!=='write')binding.reads++;
  this.capture(binding,fn);
  if(this.needsCheck(n,binding,fn)){n._ref.check=true;binding.tdzChecks=true;}
 }
 needsCheck(n,binding,fn){
  if(binding.storage==='global'||binding.ready===-Infinity)return false;
  if(binding.kind==='import')return true;
  if(binding.scope===this.root)return !(fn.kind==='script'&&fn.node===binding.script&&n.start>=binding.ready);
  if(binding.scope.fn===fn)return !(n.start>=binding.ready);
  return !this.closureAfter(fn,binding);
 }
 // A closure created after a binding is initialized can never observe its TDZ.
 closureAfter(fn,binding){
  if(binding.ready===-Infinity)return true;
  let f=fn;while(f&&f.outer&&f.outer.fn!==binding.scope.fn)f=f.outer.fn;
  if(!f||!f.node)return false;
  const created=f.node.type==='FunctionDeclaration'?-Infinity:f.node.start;
  return created>=binding.ready&&binding.scope.kind!=='switch';
 }
 capture(binding,fn){if(binding.storage!=='global'&&binding.scope.fn!==fn)binding.captured=true;}
 // ---- pass 2: storage and slots ----
 finish(){
  const visit=scope=>{
   let slot=0;
   for(const b of scope.bindings.values()){
    if(b.storage==='global')continue;
    if(scope.kind==='root'||scope.kind==='module'||scope.kind==='fscope'||b.captured){b.storage='env';b.slot=++slot;}
    else b.storage='local';
   }
   scope.slots=slot;scope.materialized=scope.forced||slot>0;
   scope.children.forEach(visit);
  };
  visit(this.root);
 }
}

// Number of parent links from the record of scope `from` (or the nearest
// materialized scope above it) to the record of scope `target`.
function hops(from,target){
 let n=0;
 for(let s=from;s;s=s.parent){if(!s.materialized)continue;if(s===target)return n;n++;}
 throw new Error('Внутренняя ошибка: область '+target.kind+' недостижима');
}

exports["ScopeAnalysis"]=ScopeAnalysis;
exports["hops"]=hops;
exports["patternNames"]=patternNames;

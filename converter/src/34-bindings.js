// Emitter methods for statically resolved bindings (see scope.mjs).
// A local binding is a WASM local; a captured binding is element `slot` of a
// scope record reached from a known record by a fixed number of parent links.
const REF=require("./wasm.mjs")["REF"];
const u32=require("./wasm.mjs")["u32"];
const hops=require("./scope.mjs")["hops"];

const SCOPE=3; // scope record type index in the program module
const GLOBAL_CONSTANTS=new Map([['undefined',undefined],['NaN',NaN],['Infinity',Infinity]]);
function fail(n,message){throw new SyntaxError(message+(n?.loc?` (строка ${n.loc.start.line}, столбец ${n.loc.start.column+1})`:''));}

const bindings={
 ref(n){if(!n._ref)fail(n,'Внутренняя ошибка: имя '+n.name+' не разрешено');return n._ref;},
 // ---- scope records ----
 castRecord(){this.out(0xfb,0x1a,0xfb,0x16,SCOPE);},
 slotGet(slot){this.castRecord();this.integer(slot);this.out(0xfb,0x0b,SCOPE);},
 record(target){
  const own=this.records.get(target);if(own!==undefined){this.get(own);return;}
  const n=hops(this.closureScope,target);this.get(0);for(let i=0;i<n;i++)this.slotGet(0);
 },
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
 // ---- bindings ----
 bindingLocal(b){let local=this.bindingLocals.get(b);if(local===undefined){local=this.local();this.bindingLocals.set(b,local);}return local;},
 importTarget(b){
  const id=b.scope.node._moduleId,spec=this.c.moduleSpecs[id],entry=spec.imports.find(e=>e.local===b.name);
  if(!entry||entry.target.namespace)return null;
  const target=this.c.analysis.modules.get(entry.target.module).bindings.get(entry.target.local);
  if(!target)throw new Error('Внутренняя ошибка: нет привязки '+entry.target.local);
  return {module:entry.target.module,slot:target.slot,initialized:target.kind==='function'};
 },
 loadBinding(b){
  if(b.storage==='local'){this.get(this.bindingLocal(b));return true;}
  if(b.kind==='import'){
   const t=this.importTarget(b);
   if(t){this.integer(t.module);this.rt('moduleScope');this.slotGet(t.slot);return !t.initialized;}
  }
  this.record(b.scope);this.slotGet(b.slot);return true;
 },
 storeBinding(b,keep){
  if(b.storage==='local'){this.out(keep?0x22:0x21,...u32(this.bindingLocal(b)));return;}
  const v=this.local();this.set(v);this.record(b.scope);this.castRecord();this.integer(b.slot);this.get(v);this.out(0xfb,0x0e,SCOPE);if(keep)this.get(v);
 },
 tdz(name){this.integer(this.c.constant(name));this.rt('tdz');this.out(0x00);},
 checkInitialized(name){const t=this.local();this.tee(t);this.out(0xd1);this.ifElse(()=>this.tdz(name));this.get(t);},
 isGlobal(r){return !r.binding||r.binding.storage==='global';},
 readIdentifier(n){
  const r=this.ref(n),b=r.binding;
  if(this.isGlobal(r)){
   if(!b&&GLOBAL_CONSTANTS.has(n.name)){this.lit(GLOBAL_CONSTANTS.get(n.name));return;}
   this.integer(this.c.constant(n.name));this.rt('globalRead');return;
  }
  const mayBeEmpty=this.loadBinding(b);
  if(r.check&&mayBeEmpty)this.checkInitialized(n.name);
 },
 // Assignment semantics: TDZ and constant checks after the value is computed.
 writeIdentifier(n,emitValue,keep=true){
  const r=this.ref(n),b=r.binding;
  if(this.isGlobal(r)){this.integer(this.c.constant(n.name));emitValue();this.integer(+this.strict);this.rt('globalWrite');if(!keep)this.out(0x1a);return;}
  emitValue();
  if(r.check&&r.mode==='write'){const v=this.local();this.set(v);if(this.loadBinding(b)){this.out(0xd1);this.ifElse(()=>this.tdz(n.name));}else this.out(0x1a);this.get(v);}
  if(!b.mutable){
   if(b.kind==='callee'&&!this.strict){if(!keep)this.out(0x1a);return;}
   this.out(0x1a);this.integer(this.c.constant(n.name));this.rt('constAssign');this.out(0x00);return;
  }
  this.storeBinding(b,keep);
 },
 // Declaration semantics: initializes the binding, never checks.
 initIdentifier(n,emitValue){
  const r=this.ref(n),b=r.binding;
  if(this.isGlobal(r)){this.integer(this.c.constant(n.name));emitValue();this.integer(0);this.rt('globalWrite');this.out(0x1a);return;}
  emitValue();this.storeBinding(b,false);
 },
 initBinding(b,emitValue){emitValue();this.storeBinding(b,false);},
 typeofIdentifier(n){
  if(this.isGlobal(this.ref(n))){this.integer(this.c.constant(n.name));this.rt('globalTypeof');return;}
  this.integer(this.c.constant('typeof'));this.readIdentifier(n);this.rt('unary');
 },
 deleteIdentifier(n){
  if(this.isGlobal(this.ref(n))){this.integer(this.c.constant(n.name));this.rt('globalDelete');return;}
  this.lit(false);
 },
 updateIdentifier(n,delta,prefix){
  const old=this.local(),value=this.local();
  this.readIdentifier(n);this.rt('toNumeric');this.tee(old);this.integer(delta);this.rt('increment');this.set(value);
  this.writeIdentifier(n,()=>this.get(value),false);this.get(prefix?value:old);
 },
 assignIdentifier(n){
  const left=n.left;
  if(n.operator==='='){this.writeIdentifier(left,()=>this.expression(n.right,left.name));return;}
  if(['&&=','||=','??='].includes(n.operator)){
   const old=this.local();this.readIdentifier(left);this.tee(old);this.rt(n.operator==='??='?'nullish':'truth');
   const put=()=>this.writeIdentifier(left,()=>this.expression(n.right,left.name));
   if(n.operator==='||=')this.ifElse(()=>this.get(old),put,REF);else this.ifElse(put,()=>this.get(old),REF);return;
  }
  this.writeIdentifier(left,()=>this.binary(n.operator.slice(0,-1),()=>this.readIdentifier(left),()=>this.expression(n.right)));
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
   this.lit(undefined);return {code:this.code,locals:this.locals};
  }
  if(kind==='script'){
   this.records.set(analysis.root,0);this.closureScope=analysis.root;
   const error=analysis.scriptErrors.get(node);
   if(error!==undefined){this.integer(this.c.constant(error));this.rt('scriptError');this.out(0x00);return {code:this.code,locals:this.locals};}
   for(const d of node._globalDecls||[])if(d.kind==='var'){this.integer(this.c.constant(d.name));this.rt('globalVar');}
   for(const s of node.body)if(s.type==='FunctionDeclaration'){this.integer(this.c.constant(s.id.name));this.function(s,s.id.name);this.rt('globalFunction');}
   node.body.forEach(n=>this.statement(n));
   this.lit(undefined);return {code:this.code,locals:this.locals};
  }
  this.closureScope=fn.scope.parent;
  if(fn.scope.materialized)this.openRecord(fn.scope);
  this.plan.usesArguments=fn.usesArguments;
  if(fn.usesArguments){
   const slots=fn.mapped?node.params.map((p,i)=>node.params.findLastIndex(q=>q.name===p.name)===i?fn.scope.bindings.get(p.name).slot:0):[];
   this.initBinding(fn.argumentsBinding,()=>{this.get(2);this.get(this.env);this.integer(this.c.constant(slots));this.integer(+fn.mapped);this.rt('arguments');});
  }
  node.params.forEach((p,i)=>this.pattern(p,()=>{this.get(2);this.integer(i);this.rt(p.type==='RestElement'?'rest':'arg');},'let'));
  if(node.type==='ArrowFunctionExpression'&&node.body.type!=='BlockStatement'){this.expression(node.body);this.out(0x0f);this.lit(undefined);return {code:this.code,locals:this.locals};}
  const body=node.body.body,prologue=()=>{
   if(fn.bodyScope!==fn.scope){
    // Non-simple parameters: body vars start as copies of same-named parameters.
    const copied=new Set();
    for(const b of fn.bodyScope.bindings.values()){const p=fn.scope.bindings.get(b.name);if(b.kind==='var'&&p){copied.add(b.name);this.initBinding(b,()=>this.loadBinding(p));}}
    this.hoistVars(fn.bodyScope,copied);
   }else this.hoistVars(fn.scope);
   this.hoistFunctions(body);
   if(node.generator){this.lit(undefined);this.integer(0);this.rt('pause');this.out(0x1a);}
   body.forEach(n=>this.statement(n));
  };
  if(fn.bodyScope!==fn.scope)this.enterScope(fn.bodyScope,prologue);else prologue();
  this.lit(undefined);return {code:this.code,locals:this.locals};
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
    else{this.function(m._initializer,'',true);this.integer(+m.static);this.integer(0);this.rt('classField');}
   }
   if(bindingName)this.initBinding(n._scope.bindings.get(bindingName),()=>this.get(c));
   this.get(c);this.rt('finishClass');
  });
  this.strict=strict;
 },
 expression(n,hint=''){
  switch(n.type){
   case'Identifier':this.readIdentifier(n);return;
   case'UnaryExpression':if(n.argument.type==='Identifier'){if(n.operator==='typeof'){this.typeofIdentifier(n.argument);return;}if(n.operator==='delete'){this.deleteIdentifier(n.argument);return;}}break;
   case'UpdateExpression':if(n.argument.type==='Identifier'){this.updateIdentifier(n.argument,n.operator==='++'?1:-1,n.prefix);return;}break;
   case'AssignmentExpression':if(n.left.type==='Identifier'){this.assignIdentifier(n);return;}break;
  }
  this.featureExpression(n,hint);
 },
 statement(n,tag=null){
  switch(n.type){
   case'FunctionDeclaration':return;
   case'BlockStatement':this.enterScope(n._scope,()=>n.body.forEach(x=>this.statement(x)),n.body);return;
   case'ClassDeclaration':this.initIdentifier(n.id,()=>this.classExpression(n));return;
   case'ForStatement':case'WhileStatement':case'DoWhileStatement':{
    const scope=n.type==='ForStatement'?n._scope:null;
    this.enterScope(scope,()=>{
     if(n.init){if(n.init.type==='VariableDeclaration')this.statement(n.init);else{this.expression(n.init);this.out(0x1a);}}
     // Closures capture a fresh copy of the loop bindings per iteration.
     const copy=!!scope?.materialized,next=()=>{if(copy){this.get(this.env);this.rt('scopeClone');this.set(this.env);}};
     next();
     this.label('break',end=>this.label('loop',again=>{
      if(n.type!=='DoWhileStatement'&&n.test){this.expression(n.test);this.rt('truth');this.out(0x45);this.ifElse(()=>this.branch(end));}
      this.label('continue',()=>this.statement(n.body),0x40,tag);
      next();
      if(n.update){this.expression(n.update);this.out(0x1a);}
      if(n.type==='DoWhileStatement'){this.expression(n.test);this.rt('truth');this.ifElse(()=>this.branch(again));}else this.branch(again);
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
    const value=this.local(),start=this.local(0x7f);this.expression(n.discriminant);this.set(value);this.integer(-1);this.set(start);
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

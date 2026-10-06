const REF=require("./wasm.mjs")["REF"];
const I32=require("./wasm.mjs")["I32"];
const featureSignatures={
 moduleMeta:['r','r'],importModule:['i','r'],
 prepareValue:['r','r'],callable:['r','r'],coercible:['r','r'],objectRest:['rr','r'],
 take:['r','r'],skip:['r',''],iteratorRest:['r','r'],closeIterator:['ri',''],
 thisValue:['r','r'],superProperty:['rrr','r'],superCall:['rrrr','r'],
 makeClass:['rriii','r'],classMethod:['rrrii',''],classField:['rrrii',''],finishClass:['r','r'],
 privateDeclare:['riii',''],privateKey:['ri','r'],privateProperty:['rri','r'],privateHas:['rir','r'],template:['ii','r'],
 pause:['ri','r'],resumeKind:['r','i'],resumeValue:['r','r'],
 frameRef:['ri','r'],frameInt:['ri','i'],frameFloat:['ri','f'],putRef:['rir',''],putInt:['rii',''],putFloat:['rif',''],
 framePC:['r','i'],frameInput:['r','r'],framePause:['rrii','r'],frameDone:['rr','r'],
 delegate:['ri','r'],delegateStep:['rr','r'],request:['ir','r'],
 asyncIterator:['r','r'],asyncNext:['r','r'],asyncClose:['ri','r'],
};
function error(n,message){throw new SyntaxError(message+(n?.loc?` (строка ${n.loc.start.line}, столбец ${n.loc.start.column+1})`:''));}

const features={
 abrupt(kind,target=null,value=null){
  const c=[...this.finalizers].reverse().find(c=>kind==='return'||this.labels.indexOf(target)<this.labels.indexOf(c.exit));
  if(c){let r=c.routes.find(r=>r.kind===kind&&r.target===target);if(!r){r={kind,target,id:c.routes.length+2};c.routes.push(r);}if(value!==null){this.get(value);this.set(c.value);}this.integer(r.id);this.set(c.state);this.branch(c.exit);}
  else if(kind==='return'){this.get(value);this.out(0x0f);}else this.branch(target);
 },
 withFinally(body,cleanup){
  const c={state:this.local(I32),value:this.local(),routes:[]};this.integer(0);this.set(c.state);
  this.label('cleanup',exit=>{c.exit=exit;this.finalizers.push(c);this.out(0x06,0x40);this.labels.push({kind:'try'});body();this.out(0x07,0);this.set(c.value);this.integer(1);this.set(c.state);this.labels.pop();this.out(0x0b);this.finalizers.pop();});
  cleanup(c);this.get(c.state);this.integer(1);this.out(0x46);this.ifElse(()=>{this.get(c.value);this.rt('throw');this.out(0x00);});
  for(const r of c.routes){this.get(c.state);this.integer(r.id);this.out(0x46);this.ifElse(()=>this.abrupt(r.kind,r.target,c.value));}
 },
 closeIterator(iterator,c){this.get(iterator);this.get(c.state);this.integer(1);this.out(0x46);this.rt('closeIterator');},
 pattern(n,emit,kind='assign'){
  switch(n.type){
   case'MemberExpression':if(kind!=='assign')error(n,'Ожидалось имя привязки');this.reference(n);emit();this.rt('write');this.out(0x1a);return;
   case'AssignmentPattern':this.pattern(n.left,()=>{const v=this.local();emit();this.tee(v);this.rt('isUndefined');this.ifElse(()=>this.expression(n.right,n.left.type==='Identifier'?n.left.name:''),()=>this.get(v),REF);},kind);return;
   case'RestElement':this.pattern(n.argument,emit,kind);return;
   case'ObjectPattern':{const v=this.local(),excluded=this.local();emit();this.rt('coercible');this.set(v);this.rt('array');this.set(excluded);for(const p of n.properties){if(p.type==='RestElement'){this.pattern(p.argument,()=>{this.get(v);this.get(excluded);this.rt('objectRest');},kind);continue;}const key=this.local();if(p.computed){this.expression(p.key);this.rt('key');}else this.lit(p.key.name??p.key.value);this.set(key);this.get(excluded);this.get(key);this.rt('push');this.out(0x1a);this.pattern(p.value,()=>{this.get(v);this.get(key);this.integer(+this.strict);this.rt('property');this.rt('read');},kind);}return;}
   case'ArrayPattern':{const iterator=this.local();emit();this.rt('iterator');this.set(iterator);this.withFinally(()=>{for(const item of n.elements){if(!item){this.get(iterator);this.rt('skip');}else this.pattern(item.type==='RestElement'?item.argument:item,()=>{this.get(iterator);this.rt(item.type==='RestElement'?'iteratorRest':'take');},kind);}},c=>this.closeIterator(iterator,c));return;}
  }error(n,'Шаблон ещё не реализован: '+n.type);
 },
 reference(n){
  if(n.type==='MemberExpression'&&!n.optional){if(n.property.type==='PrivateIdentifier'){this.get(this.env);this.expression(n.object);this.integer(this.c.constant(n.property.name));this.rt('privateProperty');return;}if(n.object.type==='Super'){this.get(this.env);this.get(1);if(n.computed)this.expression(n.property);else this.lit(n.property.name);this.rt('superProperty');return;}this.expression(n.object);if(n.computed)this.expression(n.property);else this.lit(n.property.name);this.integer(+this.strict);this.rt('property');return;}
  error(n,'Неподдерживаемая ссылка: '+n.type);
 },
 chain(n,mode='value'){const result=this.local();this.label('chain',exit=>{this.chainPart(n.expression,{exit,result,mode},mode);this.set(result);});this.get(result);},
 shortChain(v,c,prepared=false){this.get(v);if(prepared)this.rt('callable');this.rt('nullish');this.ifElse(()=>{this.lit(c.mode==='delete'?true:undefined);if(c.mode==='call')this.rt('prepareValue');this.set(c.result);this.branch(c.exit);});},
 chainPart(n,c,mode='value'){
  if(n.type==='MemberExpression'){if(n.object.type==='Super'){this.reference(n);this.rt(mode==='call'?'prepare':mode==='delete'?'remove':'read');return;}const base=this.local();this.chainPart(n.object,c);this.set(base);if(n.optional)this.shortChain(base,c);if(n.property.type==='PrivateIdentifier'){this.get(this.env);this.get(base);this.integer(this.c.constant(n.property.name));this.rt('privateProperty');}else{this.get(base);if(n.computed)this.expression(n.property);else this.lit(n.property.name);this.integer(+this.strict);this.rt('property');}this.rt(mode==='call'?'prepare':mode==='delete'?'remove':'read');return;}
  if(n.type==='CallExpression'){const f=this.local();this.chainPart(n.callee,c,'call');this.set(f);if(n.optional)this.shortChain(f,c,true);this.get(f);this.args(n.arguments);this.rt('invoke');}
  else if(n.type==='ChainExpression'){this.chain(n,mode);return;}else this.expression(n);
  if(mode==='call')this.rt('prepareValue');if(mode==='delete'){this.out(0x1a);this.lit(true);}
 },
 callTarget(n){if(n.type==='ChainExpression')this.chain(n,'call');else if(n.type==='MemberExpression'){this.reference(n);this.rt('prepare');}else{this.expression(n);this.rt('prepareValue');}},
 suspend(emit,kind){
  const request=this.local();emit();this.integer(kind);this.rt('pause');this.set(request);
  this.get(request);this.rt('resumeKind');this.integer(2);this.out(0x46);this.ifElse(()=>{const v=this.local();this.get(request);this.rt('resumeValue');this.set(v);this.abrupt('return',null,v);});
  this.get(request);this.rt('resumeKind');this.integer(1);this.out(0x46);this.ifElse(()=>{this.get(request);this.rt('resumeValue');this.rt('throw');this.out(0x00);});this.get(request);this.rt('resumeValue');
 },
 yieldDelegate(n){
  const iterator=this.local(),request=this.local(),step=this.local(),result=this.local(),async=!!this.plan.node.async;
  this.expression(n.argument);this.integer(+async);this.rt('delegate');this.set(iterator);this.integer(0);this.lit(undefined);this.rt('request');this.set(request);
  this.label('delegate-end',end=>this.label('loop',again=>{
   const advance=()=>{this.get(iterator);this.get(request);this.rt('delegateStep');};if(async)this.suspend(advance,2);else advance();this.set(step);
   this.get(step);this.rt('done');this.ifElse(()=>{
    this.get(step);this.rt('value');this.set(result);this.get(request);this.rt('resumeKind');this.integer(2);this.out(0x46);this.ifElse(()=>this.abrupt('return',null,result));this.branch(end);
   });
   this.get(step);if(async)this.rt('value');this.integer(async?1:4);this.rt('pause');this.set(request);this.branch(again);
  }));this.get(result);
 },
 expression(n,hint=''){
  switch(n.type){
   case'MetaProperty':if(n.meta.name==='import'&&n.property.name==='meta'){this.get(this.env);this.rt('moduleMeta');return;}break;
   case'ImportExpression':if(!Number.isInteger(n._moduleTarget))error(n,'ImportExpression: динамический import() требует этапа связывания модулей');this.integer(n._moduleTarget);this.rt('importModule');return;
   case'ThisExpression':this.get(1);this.rt('thisValue');return;
   case'FunctionExpression':case'ArrowFunctionExpression':this.function(n,n.id?.name||hint);return;
   case'ClassExpression':this.classExpression(n,hint);return;
   case'ChainExpression':this.chain(n);return;
   case'AwaitExpression':this.suspend(()=>this.expression(n.argument),2);return;
   case'YieldExpression':if(n.delegate){this.yieldDelegate(n);return;}this.suspend(()=>n.argument?this.expression(n.argument):this.lit(undefined),1);return;
   case'UnaryExpression':if(n.operator==='delete'&&n.argument.type==='ChainExpression'){this.chain(n.argument,'delete');return;}break;
   case'BinaryExpression':if(n.left.type==='PrivateIdentifier'&&n.operator==='in'){this.get(this.env);this.integer(this.c.constant(n.left.name));this.expression(n.right);this.rt('privateHas');return;}break;
   case'CallExpression':if(n.callee.type==='Super'){this.get(this.env);this.get(1);this.args(n.arguments);this.get(3);this.rt('superCall');}else{this.callTarget(n.callee);this.args(n.arguments);this.rt('invoke');}return;
   case'TaggedTemplateExpression':this.callTarget(n.tag);this.rt('array');this.integer(this.c.templateCount++);this.integer(this.c.constant({cooked:n.quasi.quasis.map(q=>q.value.cooked),raw:n.quasi.quasis.map(q=>q.value.raw)}));this.rt('template');this.rt('push');for(const x of n.quasi.expressions){this.expression(x);this.rt('push');}this.rt('invoke');return;
   case'AssignmentExpression':{
    if(n.operator==='='&&['ArrayPattern','ObjectPattern'].includes(n.left.type)){const v=this.local();this.expression(n.right);this.set(v);this.pattern(n.left,()=>this.get(v));this.get(v);return;}
    const r=this.local();this.reference(n.left);this.set(r);if(['&&=','||=','??='].includes(n.operator)){const old=this.local();this.get(r);this.rt('read');this.tee(old);this.rt(n.operator==='??='?'nullish':'truth');const put=()=>{this.get(r);this.expression(n.right);this.rt('write');};if(n.operator==='||=')this.ifElse(()=>this.get(old),put,REF);else this.ifElse(put,()=>this.get(old),REF);return;}
    this.get(r);if(n.operator==='=')this.expression(n.right,n.left.type==='Identifier'?n.left.name:'');else this.binary(n.operator.slice(0,-1),()=>{this.get(r);this.rt('read');},()=>this.expression(n.right));this.rt('write');return;
   }
  }this.baseExpression(n);
 },
 statement(n,tag=null){
  switch(n.type){
   case'VariableDeclaration':if(!['var','let','const'].includes(n.kind))error(n,'Декларация '+n.kind+' ещё не реализована');for(const d of n.declarations)if(d.init||n.kind!=='var')this.pattern(d.id,()=>d.init?this.expression(d.init,d.id._displayName||(d.id.type==='Identifier'?d.id.name:'')):this.lit(undefined),n.kind);return;
   case'ReturnStatement':{const v=this.local();const value=()=>n.argument?this.expression(n.argument):this.lit(undefined);if(this.plan.node.async&&this.plan.node.generator)this.suspend(value,2);else value();this.set(v);this.abrupt('return',null,v);return;}
   case'BreakStatement':case'ContinueStatement':{const target=[...this.labels].reverse().find(l=>(n.type==='BreakStatement'?l.kind==='break'||(n.label&&l.kind==='label'):l.kind==='continue')&&(!n.label||l.tag===n.label.name));if(!target)error(n,'Не найдена цель break/continue');this.abrupt('branch',target);return;}
  }this.baseStatement(n,tag);
 },
};

exports["featureSignatures"]=featureSignatures;
exports["features"]=features;

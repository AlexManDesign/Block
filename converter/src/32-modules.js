// AOT module discovery and linking. No source text is evaluated here.
const parse=require("acorn")["parse"];
const utf8Size=require("./encoding.mjs")["utf8Size"];

const label=n=>n.name??n.value;
const functionNode=n=>['FunctionDeclaration','FunctionExpression','ArrowFunctionExpression'].includes(n.type);
function visit(node,fn){if(!node||typeof node!=='object'||fn(node)===false)return;for(const[k,v]of Object.entries(node)){if(['loc','start','end'].includes(k)||k.startsWith('_'))continue;if(Array.isArray(v))v.forEach(x=>visit(x,fn));else if(v&&typeof v==='object')visit(v,fn);}}
function bindingNames(n){if(!n)return [];switch(n.type){case'Identifier':return[n.name];case'AssignmentPattern':return bindingNames(n.left);case'RestElement':return bindingNames(n.argument);case'ArrayPattern':return n.elements.flatMap(bindingNames);case'ObjectPattern':return n.properties.flatMap(p=>bindingNames(p.type==='RestElement'?p.argument:p.value));default:return [];}}
const same=(a,b)=>a&&b&&a.module===b.module&&a.local===b.local&&!!a.namespace===!!b.namespace;
const AMBIGUOUS=Symbol('ambiguous export');
function relativeURL(url,documentURL){const target=new URL(url),base=new URL('.',documentURL);if(target.origin!==base.origin||target.protocol!==base.protocol||target.host!==base.host)return url;const from=base.pathname.split('/').slice(0,-1),to=target.pathname.split('/');let i=0;while(i<from.length&&i<to.length&&from[i]===to[i])i++;return ('../'.repeat(from.length-i)||'./')+to.slice(i).join('/')+target.search+target.hash;}

class ModuleLinker {
 constructor(compiler,{loadModule=null,documentURL='file:///project/index.html'}={}){this.compiler=compiler;this.loadModule=loadModule;this.documentURL=documentURL;this.records=[];this.byURL=new Map();this.inlineCount=0;this.bytes=0;}
 fail(name,node,message){throw new SyntaxError(name+': '+message+(node?.loc?` (строка ${node.loc.start.line}, столбец ${node.loc.start.column+1})`:''));}
 resolve(specifier,referrer){if(typeof specifier!=='string'||!(/^(\.\.?\/|\/)/.test(specifier)||/^[a-z][a-z\d+.-]*:/i.test(specifier)))throw new SyntaxError('Путь модуля должен быть относительным: '+specifier+' (карты импортов и имена пакетов пока не поддержаны).');const url=new URL(specifier,referrer);if(url.protocol!=='file:')throw new SyntaxError('Сетевые модули не загружаются: '+specifier);return url.href;}
 external(specifier,referrer=this.documentURL){const url=this.resolve(specifier,referrer);if(this.byURL.has(url))return this.byURL.get(url);if(!this.loadModule)throw new Error('Для импорта '+specifier+' откройте HTML-файл на диске.');const source=this.loadModule(url,referrer);if(typeof source!=='string')throw new TypeError('Загрузчик модуля должен вернуть текст UTF-8.');return this.add(source,url,relativeURL(url,this.documentURL),false);}
 inline(source,name='module'){return this.add(source,this.documentURL,name,true);}
 add(source,url,name,inline){
  if(this.records.length>=256)throw new Error('Превышено ограничение в 256 модулей.');
  const size=utf8Size(source);if(size>8*1024*1024)throw new Error(name+': модуль больше 8 МБ.');this.bytes+=size;if(this.bytes>32*1024*1024)throw new Error('Суммарный размер модулей больше 32 МБ.');
  let ast;try{ast=parse(source,{ecmaVersion:'latest',sourceType:'module',locations:true});}catch(e){this.fail(name,null,e.message);}
  const id=this.records.length,r={id,url,name,inline,source,sourceBytes:size,imports:new Map(),explicit:new Map(),stars:[],deps:[],ast,async:false};this.records.push(r);if(!inline)this.byURL.set(url,id);
  const request=(node)=>{if(node.attributes?.length||node.assertions?.length)this.fail(name,node,'Атрибуты импорта ещё не реализованы.');let target;try{target=this.external(node.source.value,url);}catch(e){this.fail(name,node,e.message);}if(!r.deps.includes(target))r.deps.push(target);return target;};
  const body=[];
  for(const n of ast.body){
   if(n.type==='ImportDeclaration'){const target=request(n);for(const s of n.specifiers)r.imports.set(s.local.name,{module:target,...(s.type==='ImportNamespaceSpecifier'?{namespace:true}:{imported:s.type==='ImportDefaultSpecifier'?'default':label(s.imported)})});continue;}
   if(n.type==='ExportAllDeclaration'){const target=request(n);if(n.exported)r.explicit.set(label(n.exported),{module:target,namespace:true});else r.stars.push(target);continue;}
   if(n.type==='ExportNamedDeclaration'){
    if(n.source){const target=request(n);for(const s of n.specifiers)r.explicit.set(label(s.exported),{module:target,imported:label(s.local)});}
    else if(n.declaration){const d=n.declaration;body.push(d);for(const local of d.type==='VariableDeclaration'?d.declarations.flatMap(d=>bindingNames(d.id)):[d.id.name])r.explicit.set(local,{local});}
    else for(const s of n.specifiers)r.explicit.set(label(s.exported),{local:label(s.local)});continue;
   }
   if(n.type==='ExportDefaultDeclaration'){
    const d=n.declaration,local=d.id?.name||'*default*';r.explicit.set('default',{local});
    if(d.type==='FunctionDeclaration'||d.type==='ClassDeclaration'){if(!d.id){d.id={type:'Identifier',name:local};d._displayName='default';}body.push(d);}
    else body.push({type:'VariableDeclaration',kind:'const',declarations:[{type:'VariableDeclarator',id:{type:'Identifier',name:local,_displayName:'default'},init:d}],loc:n.loc});continue;
   }
   body.push(n);
  }
  ast.body=body;
  visit(ast,n=>{if(functionNode(n))return false;if(n.type==='AwaitExpression'||n.type==='ForOfStatement'&&n.await)r.async=true;});
  this.discoverDynamic(ast,url,name);
  ast._moduleId=id;ast._imports=r.imports;
  r.init=this.compiler.add(ast,true,'module-init',name);this.compiler.plans[r.init].sourceName=name;
  r.execute=this.compiler.add({...ast,async:r.async},true,'module-body',name);this.compiler.plans[r.execute].sourceName=name;
  return id;
 }
 discoverDynamic(ast,referrer=this.documentURL,name='script'){
  visit(ast,n=>{if(n.type!=='ImportExpression')return;if(n.options)this.fail(name,n,'Параметры динамического import() пока не поддержаны.');if(n.source.type!=='Literal'||typeof n.source.value!=='string')this.fail(name,n,'Для AOT нужен строковый литерал в import("./module.js").');try{n._moduleTarget=this.external(n.source.value,referrer);}catch(e){this.fail(name,n,e.message);}});
 }
 resolveExport(id,name,seen=new Set()){
  const key=JSON.stringify([id,name]);if(seen.has(key))return null;seen=new Set(seen).add(key);const r=this.records[id],entry=r.explicit.get(name);
  if(entry){if(entry.namespace)return {module:entry.module,namespace:true};if(entry.imported!==undefined)return this.resolveExport(entry.module,entry.imported,seen);const imp=r.imports.get(entry.local);if(imp)return imp.namespace?{module:imp.module,namespace:true}:this.resolveExport(imp.module,imp.imported,seen);return {module:id,local:entry.local};}
  if(name==='default')return null;let answer=null;
  for(const target of r.stars){const value=this.resolveExport(target,name,seen);if(value===AMBIGUOUS)return value;if(value){if(answer&&!same(answer,value))return AMBIGUOUS;answer=value;}}
  return answer;
 }
 exportedNames(id,seen=new Set()){if(seen.has(id))return [];seen=new Set(seen).add(id);const r=this.records[id],names=new Set(r.explicit.keys());for(const dep of r.stars)for(const name of this.exportedNames(dep,seen))if(name!=='default')names.add(name);return [...names].sort();}
 finish(){
  // Assign static strongly connected components. Synchronous cycles are supported;
  // reject async cycles instead of silently changing their evaluation schedule.
  let next=0,group=0;const stack=[],active=new Set(),index=new Map(),low=new Map(),cycles=[];
  const dfs=id=>{index.set(id,next);low.set(id,next++);stack.push(id);active.add(id);for(const dep of this.records[id].deps){if(!index.has(dep)){dfs(dep);low.set(id,Math.min(low.get(id),low.get(dep)));}else if(active.has(dep))low.set(id,Math.min(low.get(id),index.get(dep)));}if(low.get(id)===index.get(id)){const members=[];let value;do{value=stack.pop();active.delete(value);members.push(value);this.records[value].group=group;}while(value!==id);group++;if(members.length>1||this.records[id].deps.includes(id))cycles.push(members);}};
  for(const r of this.records)if(!index.has(r.id))dfs(r.id);
  const reachesAwait=(id,seen=new Set())=>{if(seen.has(id))return false;seen.add(id);return this.records[id].async||this.records[id].deps.some(dep=>reachesAwait(dep,seen));};
  for(const members of cycles)if(members.some(i=>reachesAwait(i)))throw new SyntaxError('Цикл модулей с зависимостью от top-level await ещё не поддержан: '+members.map(i=>this.records[i].name).join(' → '));
  const required=(id,name,owner)=>{const value=this.resolveExport(id,name);if(!value||value===AMBIGUOUS)throw new SyntaxError(owner+': '+(value===AMBIGUOUS?'Неоднозначный':'Отсутствует')+' экспорт '+JSON.stringify(name)+' из '+this.records[id].name);return value;};
  return this.records.map(r=>({name:r.name,init:r.init,execute:r.execute,async:r.async,group:r.group,deps:r.deps,url:r.inline?null:relativeURL(r.url,this.documentURL),
   imports:[...r.imports].map(([local,v])=>({local,target:v.namespace?{module:v.module,namespace:true}:required(v.module,v.imported,r.name)})),
   exports:this.exportedNames(r.id).flatMap(name=>{const target=r.explicit.has(name)?required(r.id,name,r.name):this.resolveExport(r.id,name);return target&&target!==AMBIGUOUS?[{name,target}]:[];})}));
 }
}

exports["ModuleLinker"]=ModuleLinker;

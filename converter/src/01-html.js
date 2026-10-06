const parseHTML=require("parse5")["parse"];
const Compiler=require("./compiler.mjs")["Compiler"];
const boot=require("./runtime.mjs")["boot"];
const ModuleLinker=require("./modules.mjs")["ModuleLinker"];
const utf8Size=require("./encoding.mjs")["utf8Size"];
const base64=require("./encoding.mjs")["base64"];
const safeJSON=v=>JSON.stringify(v).replace(/</g,'\\u003c').replace(/\u2028/g,'\\u2028').replace(/\u2029/g,'\\u2029');
const jsTypes=new Set(['','text/javascript','application/javascript','application/ecmascript','text/ecmascript','application/x-javascript','application/x-ecmascript','text/x-javascript','text/x-ecmascript','text/jscript','text/livescript',...Array.from({length:6},(_,i)=>'text/javascript1.'+i)]);
const attr=s=>String(s).replace(/&/g,'&amp;').replace(/"/g,'&quot;').replace(/</g,'&lt;');
function convertHTML(html,{loadExternal=null,loadModule=null,documentURL='file:///project/index.html'}={}){
 if(utf8Size(html)>8*1024*1024)throw new Error('Максимальный размер HTML — 8 МБ.');
 const tree=parseHTML(html,{sourceCodeLocationInfo:true}),compiler=new Compiler(),entries=[],edits=[],linker=new ModuleLinker(compiler,{loadModule,documentURL});
 let key='__own_wasm_app';while(html.includes(key))key+='_';
 let scriptCount=0,eventCount=0,firstScript=null,nonce='';
 const liveScripts=new Set();
 function walk(n,inert=false){
  if(n.tagName==='iframe'&&(n.attrs||[]).some(a=>['src','srcdoc'].includes(a.name)&&a.value.trim()))throw new Error('Вложенные HTML-документы iframe ещё не компилируются.');
  if(n.tagName==='script'){
   const attrs=Object.fromEntries(n.attrs.map(a=>[a.name,a.value]));const type=(attrs.type||'').trim().toLowerCase();
   if(type==='importmap')throw new Error('Карты импортов ещё не поддержаны. Используйте относительные пути модулей.');
   const module=type==='module';if(!module&&!jsTypes.has(type))return;
   if(['async','defer','nomodule'].some(a=>a in attrs))throw new Error('Атрибуты async/defer/nomodule ещё не реализованы.');
   const loc=n.sourceCodeLocation;if(!loc?.endTag)throw new Error('У исполняемого <script> отсутствует закрывающий тег.');
   let source=html.slice(loc.startTag.endOffset,loc.endTag.startOffset),name='script '+(++scriptCount);
   if('src'in attrs&&!module){if(!loadExternal)throw new Error('Для внешнего src выберите HTML-файл на диске.');source=loadExternal(attrs.src);name+=' · '+attrs.src;}
   if(utf8Size(source)>8*1024*1024)throw new Error('Скрипт больше 8 МБ.');
   if(source.includes(key)){while(source.includes(key)||html.includes(key))key+='_';}
   let id;
   if(module){id='src'in attrs?linker.external(/^(\.\.?\/|\/|[a-z][a-z\d+.-]*:)/i.test(attrs.src)?attrs.src:'./'+attrs.src):linker.inline(source,name);entries.push({moduleId:id,name:name+' · module',sourceBytes:linker.records[id].sourceBytes});}
   else{id=compiler.script(source,name);linker.discoverDynamic(compiler.plans[id].node,'src'in attrs?new URL(attrs.src,documentURL).href:documentURL,name);entries.push({id,name,sourceBytes:utf8Size(source)});}
   if(!inert){liveScripts.add(n);if(firstScript===null){firstScript=loc.startOffset;nonce=attrs.nonce||'';}}
   if('src'in attrs){let tag=html.slice(loc.startTag.startOffset,loc.startTag.endOffset);const a=loc.attrs.src;tag=tag.slice(0,a.startOffset-loc.startTag.startOffset)+tag.slice(a.endOffset-loc.startTag.startOffset);edits.push({start:loc.startOffset,end:loc.endOffset,scriptId:id,module,tag});}
   else edits.push({start:loc.startTag.endOffset,end:loc.endTag.startOffset,scriptId:id,module});
  }
  for(const a of n.attrs||[]){
   if(/^on[a-z]+$/i.test(a.name)&&a.value.trim()){
    const loc=n.sourceCodeLocation?.attrs?.[a.name];if(!loc)throw new Error('Не найдено положение обработчика '+a.name);
    const name=`${n.tagName}.${a.name} #${++eventCount}`,id=compiler.handler(a.value,name);linker.discoverDynamic(compiler.plans[id].node,documentURL,name);entries.push({id,name,sourceBytes:utf8Size(a.value)});
    edits.push({start:loc.startOffset,end:loc.endOffset,eventId:id,attribute:a.name});
   }
   if(/^\s*javascript:/i.test(a.value.replace(/[\t\r\n]/g,'')))throw new Error('URL javascript: не поддержан. Перенесите код в обработчик события.');
  }
  for(const child of n.childNodes||[])walk(child,inert);
  // Inert template contents may be cloned later, so compile their handlers too.
  if(n.content)walk(n.content,true);
 }
 walk(tree);if(!entries.length)throw new Error('В HTML не найден исполняемый JavaScript.');
 const modules=linker.finish();
 const moduleRoots=new Set(entries.map(e=>e.moduleId));for(const r of linker.records){if(!moduleRoots.has(r.id))entries.push({moduleId:r.id,name:'module · '+r.name,sourceBytes:r.sourceBytes});while(r.source.includes(key))key+='_';}
 compiler.moduleSpecs=modules;
 const {binary,constants,metadata,environmentBytes}=compiler.build();
 for(const e of edits){if('eventId'in e)e.text=e.attribute+'="'+attr(`return ${key}.event(${e.eventId},this,event)`)+'"';else{const call=e.module?`await ${key}.module(${e.scriptId});`:`${key}.run(${e.scriptId});`;e.text=e.tag?e.tag+call+'<\/script>':call;}}
 const loader=`<script${nonce?' nonce="'+attr(nonce)+'"':''}>/* Own AOT compiler: WASM program + fixed browser ABI */\nvar ${key}=(${boot.toString()})(${safeJSON(base64(binary))},${safeJSON(constants)},${safeJSON(metadata)},undefined,${safeJSON(modules)});\n<\/script>`;
 const docType=tree.childNodes.find(n=>n.nodeName==='#documentType');
 const htmlNode=tree.childNodes.find(n=>n.tagName==='html'),head=htmlNode?.childNodes.find(n=>n.tagName==='head');
 const headScript=head?.childNodes.find(n=>liveScripts.has(n));
 const position=headScript?.sourceCodeLocation?.startOffset??head?.sourceCodeLocation?.endTag?.startOffset??
  Math.max(htmlNode?.sourceCodeLocation?.startTag?.endOffset??docType?.sourceCodeLocation?.endOffset??0,...(head?.childNodes||[]).map(n=>n.sourceCodeLocation?.endOffset??0));
 edits.push({start:position,end:position,text:'\n'+loader+'\n'});
 edits.sort((a,b)=>b.start-a.start||(b.end-b.start)-(a.end-a.start));
 let output=html;for(const e of edits)output=output.slice(0,e.start)+e.text+output.slice(e.end);
 if(!output.startsWith('\ufeff'))output='\ufeff'+output;
 const numericFunctions=metadata.filter(m=>m.numeric&&!m.stringSpecialization).length,stringFunctions=metadata.filter(m=>m.stringSpecialization).length;
 return {output,compiled:entries.length,functions:metadata.length,numericFunctions,stringFunctions,specializedFunctions:numericFunctions+stringFunctions,resumable:metadata.filter(m=>m.frameTypes).length,modules:modules.length,wasmBytes:binary.length+environmentBytes,programWasmBytes:binary.length,runtimeWasmBytes:environmentBytes,scripts:scriptCount,events:eventCount,report:entries.map(e=>({name:e.name,status:'compiled',reason:`WASM + WASM-runtime + браузерный JS · ${e.sourceBytes} Б исходника`})),optimizations:compiler.plans.filter(p=>p.kind==='function').map(p=>({name:p.name||'(без имени)',source:p.sourceName,numeric:!!p.numeric,string:!!p.stringSpecialization,reason:p.numeric?(p.stringSpecialization?'Строки и числа в WASM; проверки перед входом в тело':'Числовое тело и локальные записи без JS-вызовов внутри'):p.numericReason||(p.node.async||p.node.generator?'Приостанавливаемая функция':'Общий backend с WASM GC')})),engine:'own-aot-0.9.0'};
}

exports["convertHTML"]=convertHTML;

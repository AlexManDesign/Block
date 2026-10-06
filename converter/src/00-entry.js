// Browser entry: only explicitly selected files are visible to the compiler.
const convertHTML=require("./html.mjs")["convertHTML"];
const utf8Size=require("./encoding.mjs")["utf8Size"];
const ROOT='file:///project/';
function fileURL(path){if(typeof path!=='string'||!path||path.startsWith('/')||path.includes('\\')||path.split('/').some(s=>!s||s==='.'||s==='..'))throw new Error('Некорректный путь файла: '+path);return ROOT+path.split('/').map(encodeURIComponent).join('/');}
function compileProject({html,entry='index.html',files=[]}){
 if(typeof html!=='string')throw new TypeError('Нужен текст HTML.');
 const documentURL=fileURL(entry),selected=new Map(),used=new Set();
 for(const item of files){const url=fileURL(item.path);if(selected.has(url))throw new Error('Два файла с одним путём: '+item.path);selected.set(url,item);}
 const read=url=>{
  const u=new URL(url);if(u.protocol!=='file:'||u.host||!u.href.startsWith(ROOT))throw new Error('Разрешены только файлы выбранного проекта: '+url);
  u.search='';u.hash='';const file=selected.get(u.href);if(!file)throw new Error('Не выбран файл '+decodeURIComponent(u.pathname.slice('/project/'.length))+'. Откройте папку проекта или добавьте HTML и JS вместе.');
  if(file.blob?.size>8*1024*1024)throw new Error(file.path+': файл больше 8 МБ.');
  if(file.text===undefined){if(typeof FileReaderSync==='undefined')throw new Error('В этой среде недоступно чтение файлов проекта.');file.text=new TextDecoder('utf-8',{fatal:true}).decode(new FileReaderSync().readAsArrayBuffer(file.blob));}
  if(typeof file.text!=='string'||utf8Size(file.text)>8*1024*1024)throw new Error(file.path+': нужен текст UTF-8 до 8 МБ.');used.add(file.path);return file.text;
 };
 const result=convertHTML(html,{documentURL,loadModule:read,loadExternal:src=>{if(/^(?:[a-z][a-z\d+.-]*:|\/\/)/i.test(src))throw new Error('Сетевые и абсолютные src не загружаются: '+src);return read(new URL(src,documentURL).href);}});
 return {...result,usedFiles:[...used]};
}

exports["compileProject"]=compileProject;

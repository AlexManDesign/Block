// Dumps the reference block-id table (name -> numeric id) from genWorker.js / meshWorker.js.
import fs from 'node:fs'; import path from 'node:path'; import { pathToFileURL } from 'node:url';
const which = process.argv[2] || 'genWorker';
const src = fs.readFileSync(path.resolve(import.meta.dirname, `../../reference/source/${which}.js`), 'utf8');
const cache = path.resolve(import.meta.dirname, '../.cache'); fs.mkdirSync(cache, { recursive: true });
// the enum object is the one containing AIR:0,GRASS:1 ; find its minified variable name
const m = src.match(/([A-Za-z_$][\w$]*)=\{AIR:0,GRASS:1,/);
const tmp = path.join(cache, `${which}.ids.mjs`);
fs.writeFileSync(tmp, src + `\n;globalThis.__BLOCKS=${m[1]};`);
globalThis.self = { postMessage() {} };
await import(pathToFileURL(tmp).href);
const out = Object.entries(globalThis.__BLOCKS).sort((a, b) => a[1] - b[1]);
console.log(JSON.stringify(Object.fromEntries(out)));

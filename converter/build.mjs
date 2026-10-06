// Rebuilds js-wasm-converter.html from src/*.js.
// Each src file "NN-name.js" is the body of bundle factory NN (CommonJS style:
// require("./x.mjs") / exports[...]). Third-party factories stay untouched.
// Usage: node build.mjs            -> writes js-wasm-converter.html
//        node build.mjs --out f.html -> writes another file (template unchanged)
//        node build.mjs --extract  -> (once) splits own factories into src/
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

const DIR = path.dirname(fileURLToPath(import.meta.url));
const HTML = path.join(DIR, 'js-wasm-converter.html');
const SRC = path.join(DIR, 'src');
const OWN = { 0: 'entry', 1: 'html', 21: 'compiler', 23: 'wasm', 24: 'compiler-features', 25: 'resumable', 26: 'numeric', 27: 'string-wasm', 28: 'environment-wasm', 29: 'object-wasm', 30: 'encoding', 31: 'runtime', 32: 'modules' };

function split(html) {
  const start = html.indexOf('const factories={\n') + 'const factories={\n'.length;
  const depsAt = html.indexOf('\nconst dependencies=', start);
  const end = html.lastIndexOf('\n};', depsAt);
  const body = html.slice(start, end + 1);
  const re = /^(\d+):function\(module,exports,require\)\{\n/gm;
  const marks = [...body.matchAll(re)];
  const factories = new Map();
  marks.forEach((m, i) => {
    const from = m.index + m[0].length, to = i + 1 < marks.length ? marks[i + 1].index : body.length;
    let text = body.slice(from, to);
    text = text.replace(/\n\},\n?$/, '\n').replace(/\n\}\n?$/, '\n');
    factories.set(+m[1], text);
  });
  const depsLineEnd = html.indexOf('\n', depsAt + 1);
  const depsLine = html.slice(depsAt + 1, depsLineEnd);
  const deps = JSON.parse(depsLine.slice('const dependencies='.length, depsLine.indexOf('],cache=') + 1));
  return { start, end: end + 1, factories, depsAt: depsAt + 1, depsLineEnd, depsLine, deps };
}

const html = fs.readFileSync(HTML, 'utf8');
const parts = split(html);

if (process.argv.includes('--extract')) {
  fs.mkdirSync(SRC, { recursive: true });
  for (const [id, name] of Object.entries(OWN)) fs.writeFileSync(path.join(SRC, `${String(id).padStart(2, '0')}-${name}.js`), parts.factories.get(+id));
  console.log('extracted', Object.keys(OWN).length, 'modules');
  process.exit(0);
}

// Map module specifiers to factory ids: own sources by file name, the rest from the old table.
const files = fs.readdirSync(SRC).filter(f => /^\d+-.+\.js$/.test(f)).sort();
const byName = new Map();
for (const f of files) { const [, id, name] = f.match(/^(\d+)-(.+)\.js$/); byName.set('./' + name + '.mjs', +id); }
const factories = new Map(parts.factories);
const deps = [...parts.deps];
for (const f of files) {
  const [, idText] = f.match(/^(\d+)-/); const id = +idText;
  const text = fs.readFileSync(path.join(SRC, f), 'utf8');
  factories.set(id, text.endsWith('\n') ? text : text + '\n');
  const map = {};
  for (const [, spec] of text.matchAll(/require\("([^"]+)"\)/g)) {
    const target = byName.get(spec) ?? parts.deps[id]?.[spec];
    if (target === undefined) throw new Error(`${f}: unknown module ${spec}`);
    map[spec] = target;
  }
  deps[id] = map;
}
const ids = [...factories.keys()].sort((a, b) => a - b);
ids.forEach((id, i) => { if (id !== i) throw new Error('factory ids must be contiguous, missing ' + i); });
for (let i = 0; i < ids.length; i++) deps[i] ??= {};
const body = ids.map(id => `${id}:function(module,exports,require){\n${factories.get(id)}}`).join(',\n') + '\n';
const depsLine = 'const dependencies=' + JSON.stringify(deps.slice(0, ids.length)) + parts.depsLine.slice(parts.depsLine.indexOf(',cache='));
const out = html.slice(0, parts.start) + body + html.slice(parts.end, parts.depsAt) + depsLine + html.slice(parts.depsLineEnd);
const outAt = process.argv.indexOf('--out'), target = outAt >= 0 ? path.resolve(process.argv[outAt + 1]) : HTML;
fs.writeFileSync(target, out);
console.log('built', path.basename(target), (out.length / 1024).toFixed(0) + ' KB,', ids.length, 'modules');

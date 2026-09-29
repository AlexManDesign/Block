// Builds site/main.js = reference main.js + a hook exposing every top-level variable as window.__bc.
import * as acorn from 'acorn'; import fs from 'node:fs'; import path from 'node:path';
const src = fs.readFileSync(path.resolve(import.meta.dirname, '../../reference/source/main.js'), 'utf8');
const ast = acorn.parse(src, { ecmaVersion: 'latest', sourceType: 'module' });
const names = new Set();
for (const n of ast.body) {
  if (n.type === 'VariableDeclaration') for (const d of n.declarations) { if (d.id.type === 'Identifier') names.add(d.id.name); }
  if (n.type === 'FunctionDeclaration' && n.id) names.add(n.id.name);
}
const getters = [...names].map(n => `get ${JSON.stringify(n)}(){return ${n}}`).join(',');
fs.writeFileSync(path.resolve(import.meta.dirname, 'site/main.js'), src + `\n;window.__bc={${getters}};\n`);
console.log('top-level names:', names.size);

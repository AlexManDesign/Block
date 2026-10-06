// Lists runtime calls per compiled function: node inspect.mjs -e "code" [functionName]
import fs from 'node:fs';
import { CONVERTER } from './harness.mjs';
const html = fs.readFileSync(CONVERTER, 'utf8');
const start = html.indexOf('<script>function createOwnCompiler(){') + 8, end = html.indexOf('</script>', start);
const source = html.slice(start, end);
// Expose the compiler instance by patching Compiler.build.
const patched = source.replace('build(){', 'build(){globalThis.__compiler=this;');
const createOwnCompiler = new Function(patched + '\nreturn createOwnCompiler;')();
const code = process.argv[2] === '-e' ? process.argv[3] : fs.readFileSync(process.argv[2], 'utf8');
createOwnCompiler()({ html: `<script>${code}</script>`, entry: 'index.html', files: [] });
const c = globalThis.__compiler, want = process.argv[4];
const names = c.imports.map(i => i.name);
c.plans.forEach((p, id) => {
  if (want && p.name !== want) return;
  console.log(`#${id} ${p.kind} ${p.name} typed=${!!p.typed} numeric=${!!p.numeric}`);
});
// Re-decode bodies from the binary: count call targets per function.
const r = createOwnCompiler()({ html: `<script>${code}</script>`, entry: 'index.html', files: [] });
const fns = globalThis.__compiler.functions;
globalThis.__compiler.plans.forEach((p, id) => {
  if (want && p.name !== want) return;
  for (const [k, f] of [['f', fns[2 * id]], ['t', fns[2 * id + 1]]]) {
    const calls = new Map(); const b = f.code;
    for (let i = 0; i < b.length - 1; i++) if (b[i] === 0x10) { let v = 0, s = 0, j = i + 1, x; do { x = b[j++]; v |= (x & 127) << s; s += 7; } while (x & 128); if (v < names.length) calls.set(names[v], (calls.get(names[v]) || 0) + 1); }
    console.log(` ${k}${id}: ${b.length} bytes, locals ${JSON.stringify(f.locals.map(t => ({0x6f: 'r', 0x7c: 'f', 0x7f: 'i'})[t] || t).join(''))}, calls: ${[...calls].map(([n, c]) => n + '×' + c).join(' ')}`);
  }
});
if (process.argv[5] === '--types') {
  const plan = globalThis.__compiler.plans.find(p => p.name === want);
  const src = code;
  const walk = (n, d = 0) => { if (!n || typeof n !== 'object') return; if (n.type && n._t) console.log('  '.repeat(d) + n.type + ' ' + (n.operator || n.name || '') + ' :: ' + n._t + '  `' + src.slice(n.start, n.end).slice(0, 40) + '`'); for (const k in n) { if (k[0] === '_' || k === 'loc') continue; const v = n[k]; if (Array.isArray(v)) v.forEach(x => walk(x, d + 1)); else if (v && typeof v === 'object') walk(v, d + 1); } };
  walk(plan.node.body);
  for (const p of plan.node.params) console.log('param', p.name, p._ref.binding.type);
}

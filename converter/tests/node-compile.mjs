// Runs the converter's compiler in Node for debugging: node node-compile.mjs file.html|-e "code"
import fs from 'node:fs';
import { CONVERTER } from './harness.mjs';
const html = fs.readFileSync(CONVERTER, 'utf8');
const start = html.indexOf('<script>function createOwnCompiler(){') + 8, end = html.indexOf('</script>', start);
const createOwnCompiler = new Function(html.slice(start, end) + '\nreturn createOwnCompiler;')();
export const compile = (source) => createOwnCompiler()({ html: source, entry: 'index.html', files: [] });
if (process.argv[1].endsWith('node-compile.mjs')) {
  const arg = process.argv[2] === '-e' ? `<script>${process.argv[3]}</script>` : fs.readFileSync(process.argv[2], 'utf8');
  try { const r = compile(arg); console.log('ok', r.functions, 'functions', r.wasmBytes, 'bytes'); }
  catch (e) { console.log(e.stack.split('\n').slice(0, 25).join('\n')); }
}

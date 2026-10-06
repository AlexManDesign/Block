// Prints the WASM the compiler emits for a function, as readable text.
// Usage: node disasm.mjs -e "code" functionName   (or a file instead of -e "code")
// Only the opcodes this compiler emits are decoded.
import fs from 'node:fs';
import { CONVERTER } from './harness.mjs';

const html = fs.readFileSync(CONVERTER, 'utf8');
const start = html.indexOf('<script>function createOwnCompiler(){') + 8, end = html.indexOf('</script>', start);
const create = new Function(html.slice(start, end).replace('build(){', 'build(){globalThis.__compiler=this;') + '\nreturn createOwnCompiler;')();
const code = process.argv[2] === '-e' ? process.argv[3] : fs.readFileSync(process.argv[2], 'utf8');
const want = process.argv[process.argv[2] === '-e' ? 4 : 3];
create()({ html: `<script>${code}</script>`, entry: 'index.html', files: [] });
const c = globalThis.__compiler;
const importNames = c.imports.map(i => i.name);

const OPS = {
  0x00: 'unreachable', 0x01: 'nop', 0x05: 'else', 0x07: 'catch', 0x0b: 'end', 0x0f: 'return', 0x1a: 'drop', 0x1b: 'select',
  0x45: 'i32.eqz', 0x46: 'i32.eq', 0x47: 'i32.ne', 0x48: 'i32.lt_s', 0x49: 'i32.lt_u', 0x4a: 'i32.gt_s', 0x4b: 'i32.gt_u', 0x4c: 'i32.le_s', 0x4d: 'i32.le_u', 0x4e: 'i32.ge_s', 0x4f: 'i32.ge_u',
  0x50: 'i64.eqz', 0x61: 'f64.eq', 0x62: 'f64.ne', 0x63: 'f64.lt', 0x64: 'f64.gt', 0x65: 'f64.le', 0x66: 'f64.ge',
  0x67: 'i32.clz', 0x68: 'i32.ctz', 0x69: 'i32.popcnt', 0x6a: 'i32.add', 0x6b: 'i32.sub', 0x6c: 'i32.mul', 0x6d: 'i32.div_s', 0x6e: 'i32.div_u', 0x6f: 'i32.rem_s', 0x70: 'i32.rem_u',
  0x71: 'i32.and', 0x72: 'i32.or', 0x73: 'i32.xor', 0x74: 'i32.shl', 0x75: 'i32.shr_s', 0x76: 'i32.shr_u', 0x77: 'i32.rotl', 0x78: 'i32.rotr',
  0x99: 'f64.abs', 0x9a: 'f64.neg', 0x9b: 'f64.ceil', 0x9c: 'f64.floor', 0x9d: 'f64.trunc', 0x9e: 'f64.nearest', 0x9f: 'f64.sqrt',
  0xa0: 'f64.add', 0xa1: 'f64.sub', 0xa2: 'f64.mul', 0xa3: 'f64.div', 0xa4: 'f64.min', 0xa5: 'f64.max', 0xa6: 'f64.copysign',
  0xaa: 'i32.trunc_f64_s', 0xab: 'i32.trunc_f64_u', 0xb6: 'f32.demote_f64', 0xb7: 'f64.convert_i32_s', 0xb8: 'f64.convert_i32_u', 0xbb: 'f64.promote_f32', 0xbd: 'i64.reinterpret_f64',
  0xd1: 'ref.is_null', 0xd3: 'ref.eq',
};
const GC = { 0x00: ['struct.new', 1], 0x02: ['struct.get', 2], 0x05: ['struct.set', 2], 0x07: ['array.new_default', 1], 0x0b: ['array.get', 1], 0x0e: ['array.set', 1], 0x0f: ['array.len', 0], 0x11: ['array.copy', 2], 0x14: ['ref.test', 1], 0x16: ['ref.cast', 1], 0x1a: ['any.convert_extern', 0], 0x1b: ['extern.convert_any', 0], 0x1c: ['ref.i31', 0], 0x1d: ['i31.get_s', 0] };

function disassemble(f, params) {
  const b = f.code, lines = []; let i = 0, depth = 1;
  const leb = (signed = false) => { let v = 0, s = 0, x; do { x = b[i++]; v |= (x & 127) << s; s += 7; } while (x & 128); if (signed && s < 32 && (x & 64)) v |= -1 << s; return v; };
  const pad = () => '  '.repeat(depth);
  while (i < b.length) {
    const op = b[i++];
    if (op === 0x02 || op === 0x03 || op === 0x04 || op === 0x06) { const t = b[i++]; lines.push(pad() + ({ 2: 'block', 3: 'loop', 4: 'if', 6: 'try' })[op] + (t === 0x40 ? '' : ' ' + ({ 0x6f: 'externref', 0x7c: 'f64', 0x7f: 'i32' })[t])); depth++; continue; }
    if (op === 0x05 || op === 0x07) { depth--; lines.push(pad() + OPS[op] + (op === 0x07 ? ' ' + leb() : '')); depth++; continue; }
    if (op === 0x0b) { depth--; if (depth > 0) lines.push(pad() + 'end'); continue; }
    if (op === 0x0c || op === 0x0d) { lines.push(pad() + (op === 0x0c ? 'br ' : 'br_if ') + leb()); continue; }
    if (op === 0x10) { const n = leb(); lines.push(pad() + 'call ' + (n < importNames.length ? '$' + importNames[n] : (n - importNames.length) % 2 ? `$t${(n - importNames.length - 1) / 2}` : `$f${(n - importNames.length) / 2}`)); continue; }
    if (op >= 0x20 && op <= 0x22) { lines.push(pad() + ['local.get', 'local.set', 'local.tee'][op - 0x20] + ' ' + leb()); continue; }
    if (op === 0x41) { lines.push(pad() + 'i32.const ' + leb(true)); continue; }
    if (op === 0x44) { const v = new DataView(Uint8Array.from(b.slice(i, i + 8)).buffer).getFloat64(0, true); i += 8; lines.push(pad() + 'f64.const ' + v); continue; }
    if (op === 0xd0) { lines.push(pad() + 'ref.null ' + b[i++].toString(16)); continue; }
    if (op === 0xfb) { const sub = leb(), [name, n] = GC[sub] || ['gc.0x' + sub.toString(16), 0]; const imm = []; for (let k = 0; k < n; k++) imm.push(leb()); lines.push(pad() + name + (imm.length ? ' ' + imm.join(' ') : '')); continue; }
    if (op === 0xfc) { const sub = leb(); lines.push(pad() + ({ 2: 'i32.trunc_sat_f64_s' })[sub] || 'misc.' + sub); continue; }
    lines.push(pad() + (OPS[op] || '??0x' + op.toString(16)));
  }
  const names = { 0x6f: 'r', 0x7c: 'f', 0x7f: 'i' };
  return `params ${params.map(t => names[t] || t).join('')}  locals ${f.locals.map(t => names[t] || t).join('')}\n` + lines.join('\n');
}

c.plans.forEach((p, id) => {
  if (want && p.name !== want) return;
  for (const [kind, f] of [['f', c.functions[2 * id]], ['t', c.functions[2 * id + 1]]]) {
    if (f.code.length <= 1) continue;
    console.log(`;; ${kind}${id} ${p.kind} ${p.name}`);
    console.log(disassemble(f, f.params));
  }
});

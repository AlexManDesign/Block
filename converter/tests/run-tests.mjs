// Differential tests: plain JS vs compiled WASM must log the same lines.
// Usage: node run-tests.mjs [name filter] [--file cases-x.mjs]
// Cases come from every cases*.mjs file next to this script.
import fs from 'node:fs';
import path from 'node:path';
import { launch, compilerPage, runPage, wrap } from './harness.mjs';

const args = process.argv.slice(2), fileAt = args.indexOf('--file');
const onlyFile = fileAt >= 0 ? args.splice(fileAt, 2)[1] : null;
const filter = args[0] ? new RegExp(args[0], 'i') : null;
const dir = path.dirname(new URL(import.meta.url).pathname);
const cases = {};
for (const file of fs.readdirSync(dir).filter(f => /^cases.*\.mjs$/.test(f)).sort()) {
  if (onlyFile && file !== onlyFile) continue;
  const module = await import(path.join(dir, file));
  for (const [name, code] of Object.entries(module.cases)) {
    if (cases[name] !== undefined) throw new Error(`duplicate case name "${name}" in ${file}`);
    cases[name] = code;
  }
}
const browser = await launch();
const { page: cpage, compile } = await compilerPage(browser);
let pass = 0, fail = 0, compileFail = 0;
const failures = [];
const entries = Object.entries(cases).filter(([name]) => !filter || filter.test(name));
const concurrency = +process.env.TEST_CONCURRENCY || 6;
async function one([name, code]) {
  const html = wrap(code);
  const expected = await runPage(browser, html);
  await expected.page.close();
  const c = await compile(html);
  if (!c.ok) { compileFail++; failures.push(`✗ ${name}: COMPILE ERROR ${c.error}`); return; }
  const actual = await runPage(browser, c.output);
  await actual.page.close();
  const a = JSON.stringify(expected.out), b = JSON.stringify(actual.out);
  if (a === b) pass++;
  else {
    fail++;
    const lines = [];
    for (let i = 0; i < Math.max(expected.out.length, actual.out.length); i++)
      if (expected.out[i] !== actual.out[i]) lines.push(`    #${i} js:   ${expected.out[i]}\n    #${i} wasm: ${actual.out[i]}`);
    failures.push(`✗ ${name}\n${lines.slice(0, 4).join('\n')}`);
  }
}
const queue = [...entries];
await Promise.all(Array.from({ length: concurrency }, async () => { while (queue.length) await one(queue.shift()); }));

// Built-in converter examples must compile and behave like the source page.
if (!onlyFile && (!filter || filter.test('examples'))) {
  const examples = await cpage.evaluate(() => examples);
  for (const [name, demo] of Object.entries(examples)) {
    const c = await compile(demo.html, demo.files, demo.entry);
    if (!c.ok) { compileFail++; failures.push(`✗ example ${name}: COMPILE ERROR ${c.error}`); continue; }
    if (demo.files.length) { pass++; continue; } // module examples need file:// project; compile-only
    const text = async html => { const r = await runPage(browser, html, { waitMs: 300 }); const t = await r.page.evaluate(() => document.body.innerText.replace(/\ufeff/g, '').trim().replace(/\d+\.\d+ мс/g, 'T мс')); await r.page.close(); return [t, ...r.out]; };
    const a = JSON.stringify(await text(demo.html)), b = JSON.stringify(await text(c.output));
    if (a === b) pass++; else { fail++; failures.push(`✗ example ${name}\n    js:   ${a.slice(0, 300)}\n    wasm: ${b.slice(0, 300)}`); }
  }
}
console.log(failures.join('\n'));
console.log(`\n${pass} passed, ${fail} failed, ${compileFail} compile errors`);
await browser.close();
process.exit(fail + compileFail ? 1 : 0);

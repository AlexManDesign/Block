// Compile the voxel game from the repo, compare load time and FPS with plain JS.
// Usage: node game.mjs [game.html]
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { launch, compilerPage, ROOT } from './harness.mjs';

const GAME = process.argv[2] || path.join(ROOT, '..', 'voxel_forge_v0_93_59_water67_complete_mesh_guards.html');
const OUT = fs.mkdtempSync(path.join(os.tmpdir(), 'game-'));
const browser = await launch();
const { compile } = await compilerPage(browser);
const t0 = Date.now();
const c = await compile(fs.readFileSync(GAME, 'utf8'));
if (!c.ok) { console.log('COMPILE ERROR', c.error); process.exit(1); }
console.log(`compile ${Date.now() - t0} ms, functions ${c.functions}, wasm ${(c.wasmBytes / 1024).toFixed(0)} KB`);
fs.copyFileSync(GAME, path.join(OUT, 'game.js.html'));
fs.writeFileSync(path.join(OUT, 'game.wasm.html'), c.output);
async function measure(file) {
  const page = await browser.newPage({ viewport: { width: 960, height: 540 } });
  const errors = [];
  page.on('pageerror', e => errors.push(String(e).slice(0, 160)));
  const start = Date.now();
  await page.goto('file://' + file, { waitUntil: 'load', timeout: 300000 });
  const load = Date.now() - start;
  await page.waitForTimeout(3000);
  const fps = await page.evaluate(() => new Promise(done => { let n = 0; const t = performance.now(); const tick = () => { n++; if (performance.now() - t < 4000) requestAnimationFrame(tick); else done(n / ((performance.now() - t) / 1000)); }; requestAnimationFrame(tick); }));
  const menu = await page.evaluate(() => document.body.innerText.slice(0, 80).replace(/\s+/g, ' '));
  await page.screenshot({ path: file.replace(/\.html$/, '.png') });
  await page.close();
  return { load, fps: +fps.toFixed(1), menu, errors };
}
console.log('js  ', JSON.stringify(await measure(path.join(OUT, 'game.js.html'))));
console.log('wasm', JSON.stringify(await measure(path.join(OUT, 'game.wasm.html'))));
console.log('screenshots in', OUT);
await browser.close();

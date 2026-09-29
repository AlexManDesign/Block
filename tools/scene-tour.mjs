#!/usr/bin/env node
// Scene tour: biomes, waterfalls, caves.
// Usage: node tools/scene-tour.mjs  (env: PAGE, SEED, OUT, ONLY=biomes,waterfall,caves,natfall, BIOMES=PLAINS,JUNGLE,...) Per scene: screenshot, speed metrics, culling on/off pixel diff.
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');
import http from 'node:http'; import fs from 'node:fs';
const PAGE = process.env.PAGE || 'voxel_forge.html', SEED = +(process.env.SEED || 424242), OUT = process.env.OUT || 'tour';
fs.mkdirSync(OUT, { recursive: true });
const html = fs.readFileSync(PAGE);
const server = http.createServer((q, r) => { r.writeHead(200, { 'content-type': 'text/html' }); r.end(html); }).listen(0);
const b = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const p = await b.newPage({ viewport: { width: 800, height: 450 } });
const errs = []; p.on('pageerror', e => errs.push(e.message)); p.on('console', m => { if (m.type() === 'error') errs.push(m.text().slice(0, 200)); });
const dec = await b.newPage();
await p.goto(`http://127.0.0.1:${server.address().port}/`); await p.waitForTimeout(800);
await p.evaluate((s) => { window.randomWorldSeed = () => s; }, SEED);
await p.click('#playCreative');
await p.waitForFunction(() => typeof worldReady !== 'undefined' && worldReady, null, { timeout: 240000 });
await p.waitForTimeout(5000);
await p.evaluate(() => {
  for (const id of ['tip', 'resources', 'stats', 'mode', 'hotbar', 'blockName', 'vitals', 'minimapWrap', 'cross', 'xrayBadge']) { const e = document.getElementById(id); if (e) e.style.visibility = 'hidden'; }
  window.drawMainFirstPersonHeld = () => {}; window.drawMainSelectionOutline = () => {};
  mainLightFlicker = 0; mainLightFlickerTarget = 0; mainLightFlickerAt = 1e15; pauseOpen = true; day = .25;
});
const setCam = (v, cull = true) => p.evaluate(([v, cull]) => { if (typeof sectionCulling !== 'undefined') { sectionCulling = cull; drawHiddenFaces = !cull; } player.flying = true; player.x = v[0]; player.y = v[1]; player.z = v[2]; player.yaw = v[3]; player.pitch = v[4]; player.vx = player.vy = player.vz = 0; day = .25; }, [v, cull]);
async function waitLoaded(v, maxMs = 45000) {
  const t0 = Date.now();
  while (Date.now() - t0 < maxMs) {
    await setCam(v); await p.waitForTimeout(700);
    const m = await p.evaluate(() => { const pcx = Math.floor(player.x / 16), pcz = Math.floor(player.z / 16); let miss = 0; for (let dz = -renderDistance; dz <= renderDistance; dz++) for (let dx = -renderDistance; dx <= renderDistance; dx++) { const c = chunkFastGet(pcx + dx, pcz + dz); if (!c || !c.meshBuilt || !c.meshComplete || c.dirty) miss++; } return miss + meshDoneCount() + meshJobs.length + genJobs.length; });
    if (m === 0) break;
  }
  for (let i = 0; i < 4; i++) { await setCam(v); await p.waitForTimeout(400); }
  await p.evaluate(() => processLightDirty(Infinity));
  for (let i = 0; i < 6; i++) { await setCam(v); await p.waitForTimeout(400); }
}
async function diff(a, bb) {
  return dec.evaluate(async ([x, y]) => { const load = async s => { const im = new Image(); im.src = 'data:image/png;base64,' + s; await im.decode(); const c = document.createElement('canvas'); c.width = im.width; c.height = im.height; const g = c.getContext('2d'); g.drawImage(im, 0, 0); return g.getImageData(0, 0, c.width, c.height).data; }; const A = await load(x), B2 = await load(y); let n = 0; for (let i = 0; i < A.length; i += 4) if (A[i] !== B2[i] || A[i + 1] !== B2[i + 1] || A[i + 2] !== B2[i + 2]) n++; return n; }, [a.toString('base64'), bb.toString('base64')]);
}
const rows = [];
async function scene(name, v) {
  await waitLoaded(v);
  const ms = [];
  for (let i = 0; i < 6; i++) { await setCam(v); await p.waitForTimeout(500); ms.push(await p.evaluate(() => ({ gpu: avgGpuFrameMs, cpu: avgRenderCpuMs, tris: triangles, draws: drawCalls, sect: typeof svVisibleSections !== 'undefined' ? svVisibleSections : 0, O: opaqueTrisFrame, C: cutoutTrisFrame, W: waterTrisFrame, T: transTrisFrame }))); }
  const m = ms[ms.length - 1];
  const info = await p.evaluate(() => { const cb = caveBiomeAt(player.x, Math.floor(player.y), player.z); return { biome: biomeAt(player.x, player.z) + (cb ? '/' + ['', 'LUSH', 'DRIPSTONE', 'DEEP_DARK'][cb] : ''), sky: getPackedLightWorld(player.x, player.y + 1.62, player.z) >> 4, uw: renderUnderwater }; });
  const on = await p.screenshot(); fs.writeFileSync(`${OUT}/${name}.png`, on);
  let d = -1, offTris = 0;
  if (await p.evaluate(() => typeof sectionCulling !== 'undefined')) { for (let i = 0; i < 4; i++) { await setCam(v, false); await p.waitForTimeout(300); } offTris = await p.evaluate(() => triangles); d = await diff(on, await p.screenshot()); await setCam(v, true); }
  const row = `${name.padEnd(16)} ${info.biome.padEnd(22)} sky=${String(info.sky).padStart(2)} uw=${info.uw ? 1 : 0} tris ${String(Math.round(m.tris / 1000)).padStart(4)}k (O${Math.round(m.O / 1000)} C${Math.round(m.C / 1000)} W${Math.round(m.W / 1000)} T${Math.round(m.T / 1000)}) noCull ${Math.round(offTris / 1000)}k draws ${m.draws} sect ${m.sect} cpu ${m.cpu.toFixed(2)}ms gpu ${m.gpu.toFixed(0)}ms cullDiffPx ${d}`;
  rows.push(row); console.log(row);
}
// ---- find targets
const spawn = await p.evaluate(() => [player.x, player.z]);
const ONLY = (process.env.ONLY || 'biomes,waterfall,caves').split(',');
const want = ['PLAINS', 'FOREST', 'BIRCH_FOREST', 'DARK_FOREST', 'TAIGA', 'SNOWY_TAIGA', 'ICE_SPIKES', 'DESERT', 'BADLANDS', 'SAVANNA', 'JUNGLE', 'SWAMP', 'MANGROVE_SWAMP', 'CHERRY_GROVE', 'MUSHROOM_FIELDS', 'JAGGED_PEAKS', 'MEADOW', 'OCEAN', 'WARM_OCEAN', 'FROZEN_OCEAN', 'RIVER'];
const found = await p.evaluate(([sx, sz, want]) => { const best = {}; for (let r = 0; r <= 3000; r += 48) for (let a = 0; a < 64; a++) { const x = sx + Math.cos(a / 64 * Math.PI * 2) * r, z = sz + Math.sin(a / 64 * Math.PI * 2) * r; const bi = biomeAt(x, z); if (want.includes(bi) && !best[bi]) { let ok = true; for (const [dx, dz] of [[40, 0], [-40, 0], [0, 40], [0, -40]]) if (biomeAt(x + dx, z + dz) !== bi) ok = false; if (ok) best[bi] = [Math.round(x), Math.round(z)]; } } return best; }, [spawn[0], spawn[1], want]);
console.log('biomes found:', Object.keys(found).join(', '));
const surfY = (x, z) => p.evaluate(([x, z]) => heightAt(Math.floor(x), Math.floor(z)), [x, z]);
const BIOMES = process.env.BIOMES ? process.env.BIOMES.split(',') : want;
for (const bi of want) {
  if (!found[bi] || !ONLY.includes('biomes') || !BIOMES.includes(bi)) continue; const [x, z] = found[bi];
  await setCam([x + .5, 130, z + .5, 0, 0.3]); await waitLoaded([x + .5, 130, z + .5, 0, 0.3], 30000);
  const h = await p.evaluate(([x, z]) => { let y = 250; while (y > -60 && !blocks[getBlock(Math.floor(x), y, Math.floor(z))]?.solid && !isFluidWater(getBlock(Math.floor(x), y, Math.floor(z)))) y--; return y; }, [x, z]);
  await scene('biome_' + bi, [x + .5, h + 9, z + .5, 0.8, 0.28]);
}
// ---- built waterfall: stone cliff with water sources on top, simulated until it falls
if (ONLY.includes('waterfall')) {
  const base = found.PLAINS || spawn;
  await setCam([base[0] + .5, 120, base[1] + .5, 0, .3]); await waitLoaded([base[0] + .5, 120, base[1] + .5, 0, .3], 30000);
  const w = await p.evaluate(([bx, bz]) => { const X = Math.floor(bx), Z = Math.floor(bz); let g = 200; while (g > 0 && !blocks[getBlock(X, g, Z)]?.solid) g--; const top = g + 24;
    for (let x = X - 3; x <= X + 3; x++) for (let z = Z - 4; z <= Z; z++) for (let y = g + 1; y <= top; y++) setBlock(x, y, z, B.STONE);
    for (let x = X - 2; x <= X + 2; x++) { setBlock(x, top, Z, B.WATER); setBlock(x, top, Z - 1, B.WATER); }
    for (let x = X - 3; x <= X + 3; x++) setBlock(x, top + 1, Z - 2, B.STONE);
    return [X, g, Z, top]; }, base);
  await p.evaluate(() => { pauseOpen = false; });
  for (let i = 0; i < 40; i++) { await setCam([w[0] + .5, w[3] - 8, w[2] + 14, 0, 0.05]); await p.waitForTimeout(500); }
  await p.evaluate(() => { pauseOpen = true; });
  const cnt = await p.evaluate(([X, g, Z, top]) => { let n = 0; for (let y = g; y <= top; y++) for (let x = X - 3; x <= X + 3; x++) for (let z = Z; z <= Z + 3; z++) if (getBlock(x, y, z) === B.WATER_FALLING) n++; return n; }, w);
  console.log('built waterfall, falling cells:', cnt);
  await scene('waterfall_front', [w[0] + .5, w[3] - 10, w[2] + 14.5, 0, 0.05]);
  await scene('waterfall_side', [w[0] + 12.5, w[3] - 12, w[2] + 3.5, -Math.PI / 2 - 0.35, 0.05]);
  await scene('waterfall_pool', [w[0] + .5, w[1] + 6, w[2] + 9.5, 0, 0.5]);
}
// ---- waterfalls: find falling water with open air on a side, near loaded chunks; search several biome spots
let wf = null;
if (ONLY.includes('natfall')) for (const bi of ['JAGGED_PEAKS', 'MEADOW', 'TAIGA', 'FOREST', 'PLAINS', 'JUNGLE', 'SNOWY_TAIGA', 'SAVANNA', 'BADLANDS']) {
  if (!found[bi]) continue;
  await setCam([found[bi][0] + .5, 150, found[bi][1] + .5, 0, .3]); await waitLoaded([found[bi][0] + .5, 150, found[bi][1] + .5, 0, .3], 30000);
  wf = await p.evaluate(() => { let best = null, bn = 0; for (const c of chunks.values()) { const sec = c.sections; for (let si = 0; si < sec.length; si++) { const s = sec[si]; if (!s) continue; for (let i = 0; i < 4096; i++) { if (s.blocks[i] !== B.WATER_FALLING) continue; const lx = i & 15, lz = (i >> 4) & 15, ly = i >> 8, x = c.cx * 16 + lx, y = -64 + si * 16 + ly, z = c.cz * 16 + lz; if (y < 50) continue; let n = 0; for (let k = 0; k < 20; k++) if (getBlock(x, y - k, z) === B.WATER_FALLING) n++; else break; let open = null; for (const [dx, dz] of [[1, 0], [-1, 0], [0, 1], [0, -1]]) if (getBlock(x + dx * 3, y, z + dz * 3) === B.AIR && getBlock(x + dx, y, z + dz) === B.AIR) open = [dx, dz]; if (open && n > bn) { bn = n; best = [x, y, z, open[0], open[1], n] } } } } return best; });
  if (wf) { console.log('waterfall in', bi, wf); break; }
}
if (wf) {
  const [x, y, z, dx, dz, n] = wf, dist = 9, cx = x + .5 + dx * dist, cz = z + .5 + dz * dist, cy = y - n / 2;
  const yaw = Math.atan2(-dx, dz) + Math.PI; // look back toward the waterfall
  await scene('waterfall', [cx, cy, cz, Math.atan2(x + .5 - cx, -(z + .5 - cz)), 0.15]);
  await scene('waterfall_top', [cx, y + 4, cz, Math.atan2(x + .5 - cx, -(z + .5 - cz)), 0.6]);
}
// ---- caves by cave biome near spawn and a few other spots
for (const [k, name] of (ONLY.includes('caves') ? [[1, 'LUSH'], [2, 'DRIPSTONE'], [3, 'DEEP_DARK'], [0, 'PLAIN_CAVE']] : [])) {
  let pos = null;
  for (const bi of [null, 'JUNGLE', 'FOREST', 'PLAINS', 'JAGGED_PEAKS', 'DESERT', 'TAIGA']) {
    const base = bi ? found[bi] : spawn; if (!base) continue;
    if (bi) { await setCam([base[0] + .5, 120, base[1] + .5, 0, .3]); await waitLoaded([base[0] + .5, 120, base[1] + .5, 0, .3], 30000); }
    pos = await p.evaluate(([k, bx, bz]) => { let rs = 7; const rnd = () => (rs = (rs * 1103515245 + 12345) & 0x7fffffff) / 0x7fffffff; for (let t = 0; t < 60000; t++) { const x = Math.floor(bx + (rnd() - .5) * 150), z = Math.floor(bz + (rnd() - .5) * 150), y = Math.floor(-58 + rnd() * 100); if (!chunkFastGet(Math.floor(x / 16), Math.floor(z / 16))) continue; if (getBlock(x, y, z) !== B.AIR || getBlock(x, y + 1, z) !== B.AIR || getBlock(x, y + 2, z) !== B.AIR) continue; if ((getPackedLightWorld(x, y + 1, z) >> 4) !== 0) continue; if ((caveBiomeAt(x, y, z) || 0) !== k) continue; let open = 0; for (const [dx, dz] of [[3, 0], [-3, 0], [0, 3], [0, -3], [6, 0], [-6, 0], [0, 6], [0, -6]]) if (getBlock(x + dx, y + 1, z + dz) === B.AIR) open++; if (open < 5) continue; return [x + .5, y + .05, z + .5]; } return null; }, [k, base[0], base[1]]);
    if (pos) break;
  }
  if (!pos) { console.log('cave not found', name); continue; }
  await scene('cave_' + name, [pos[0], pos[1], pos[2], 0.7, 0.05]);
  await scene('cave_' + name + '_b', [pos[0], pos[1], pos[2], 3.8, -0.2]);
}
console.log('\nERRORS:', errs.length ? errs.slice(0, 8).join(' | ') : 'none');
fs.writeFileSync(`${OUT}/summary.txt`, rows.join('\n') + '\nERRORS: ' + errs.join(' | '));
await b.close(); server.close();

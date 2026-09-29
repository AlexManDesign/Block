#!/usr/bin/env node
// Headless render regression checks for a Voxel Forge build.
//
//   npm install            (installs playwright; Chromium must be available to it)
//   node tools/render-check.mjs [voxel_forge.html] [--shots=dir]
//
// Uses Chromium's software GPU (SwiftShader) so it runs without a graphics card. GPU timings from
// SwiftShader are not representative; the checks below are about correctness and scheduling.
// Set PLAYWRIGHT_MODULE to an absolute path of playwright's index.mjs if it is not resolvable.
import http from 'node:http';
import fs from 'node:fs';
import path from 'node:path';

const args = process.argv.slice(2);
const page_ = args.find(a => !a.startsWith('--')) || 'voxel_forge.html';
const shotsArg = args.find(a => a.startsWith('--shots='));
const shots = shotsArg ? shotsArg.slice(8) : null;
if (shots) fs.mkdirSync(shots, { recursive: true });
const { chromium } = await import(process.env.PLAYWRIGHT_MODULE || 'playwright');

const html = fs.readFileSync(page_);
const server = http.createServer((req, res) => { res.writeHead(200, { 'content-type': 'text/html; charset=utf-8' }); res.end(html); }).listen(0);
const browser = await chromium.launch({ args: ['--use-angle=swiftshader', '--enable-unsafe-swiftshader', '--ignore-gpu-blocklist'] });
const page = await browser.newPage({ viewport: { width: 800, height: 450 } });
const errors = [];
page.on('pageerror', e => errors.push(`pageerror: ${e.message}`));
page.on('console', m => { if (m.type() === 'error') errors.push(`console.error: ${m.text().slice(0, 300)}`); });

const results = [];
const check = (name, ok, detail) => { results.push({ name, ok, detail }); console.log(`${ok ? 'PASS' : 'FAIL'}  ${name}  ${detail}`); };
const ev = (f, a) => page.evaluate(f, a);

// Decode a screenshot and average a rectangle, using a throwaway page (no image deps needed).
const decoder = await browser.newPage();
async function meanRGB(png, [x, y, w, h]) {
  return decoder.evaluate(async ([b64, r]) => {
    const im = new Image(); im.src = 'data:image/png;base64,' + b64; await im.decode();
    const c = document.createElement('canvas'); c.width = im.width; c.height = im.height; const g = c.getContext('2d'); g.drawImage(im, 0, 0);
    const d = g.getImageData(r[0], r[1], r[2], r[3]).data; let R = 0, G = 0, B = 0, n = 0;
    for (let i = 0; i < d.length; i += 4) { R += d[i]; G += d[i + 1]; B += d[i + 2]; n++; }
    return [R / n, G / n, B / n];
  }, [png.toString('base64'), [x, y, w, h]]);
}
async function hold(pos, frames = 8) {
  for (let i = 0; i < frames; i++) {
    await ev(p => { player.flying = true; player.x = p[0]; player.y = p[1]; player.z = p[2]; player.yaw = p[3]; player.pitch = p[4]; player.vx = player.vy = player.vz = 0; day = .25; }, pos);
    await page.waitForTimeout(600);
    if (i === 3) await ev(() => processLightDirty(Infinity));
  }
}
async function shot(name) { const b = await page.screenshot({ timeout: 120000 }); if (shots) fs.writeFileSync(path.join(shots, name + '.png'), b); return b; }

try {
  await page.goto(`http://127.0.0.1:${server.address().port}/`);
  await page.waitForTimeout(800);
  await ev(() => { window.randomWorldSeed = () => 424242; });
  await page.click('#playCreative');
  await page.waitForFunction(() => typeof worldReady !== 'undefined' && worldReady, null, { timeout: 240000 });
  await page.waitForTimeout(6000);
  await ev(() => {
    for (const id of ['tip', 'resources', 'stats', 'mode', 'hotbar', 'blockName', 'vitals', 'minimapWrap', 'cross']) { const e = document.getElementById(id); if (e) e.style.visibility = 'hidden'; }
    window.drawMainFirstPersonHeld = () => {}; window.drawMainSelectionOutline = () => {};
  });

  // 1. Lightmap on the GPU must have the same orientation as the CPU table the shaders index into.
  const lm = await ev(() => {
    const fb = gl.createFramebuffer(); gl.bindFramebuffer(gl.FRAMEBUFFER, fb);
    gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, mainLightmapTex, 0);
    const px = new Uint8Array(1024); gl.readPixels(0, 0, 16, 16, gl.RGBA, gl.UNSIGNED_BYTE, px);
    gl.bindFramebuffer(gl.FRAMEBUFFER, null); gl.deleteFramebuffer(fb);
    let maxDiff = 0; for (let i = 0; i < 1024; i++) maxDiff = Math.max(maxDiff, Math.abs(px[i] - mainLightmapPixels[i]));
    return { maxDiff, gpuSky15: [...px.slice(960, 963)], cpuSky15: [...mainLightmapPixels.slice(960, 963)] };
  });
  check('lightmap-orientation', lm.maxDiff <= 2, `max |GPU-CPU| = ${lm.maxDiff} (sky15 GPU ${lm.gpuSky15} / CPU ${lm.cpuSky15})`);

  // 1b. Grey foliage tiles (grass, ferns, vines, acacia/dark oak/jungle leaves) must be tinted (not grey).
  const fol = await ev(() => { const c = document.createElement('canvas'); c.width = atlasImg.naturalWidth; c.height = atlasImg.naturalHeight; const g = c.getContext('2d'); g.drawImage(atlasImg, 0, 0); const per = c.width / 16, out = {}; for (const nm of ['short_grass', 'fern', 'acacia_leaves', 'dark_oak_leaves', 'vine']) { const ti = TILE_NAMES.indexOf(nm), d = g.getImageData((ti % per) * 16, Math.floor(ti / per) * 16, 16, 16).data; let sat = 0, n = 0; for (let i = 0; i < d.length; i += 4) if (d[i + 3] > 48) { sat += Math.max(d[i], d[i + 1], d[i + 2]) - Math.min(d[i], d[i + 1], d[i + 2]); n++; } out[nm] = +(sat / n).toFixed(1); } return out; });
  check('foliage-tinted', Object.values(fol).every(v => v > 8), `mean saturation per tile ${JSON.stringify(fol)}`);

  // 2. A torch-lit room below sea level must not be tinted as if it were under water.
  const room = await ev(() => {
    const X = Math.floor(player.x), Z = Math.floor(player.z), Y = 18;
    for (let x = X - 6; x <= X + 6; x++) for (let z = Z - 6; z <= Z + 6; z++) for (let y = Y - 1; y <= Y + 5; y++) {
      const wall = x === X - 6 || x === X + 6 || z === Z - 6 || z === Z + 6 || y === Y - 1 || y === Y + 5;
      setBlock(x, y, z, wall ? B.STONE : B.AIR);
    }
    for (const [dx, dz] of [[-4, -4], [4, -4], [-4, 4], [4, 4]]) setBlock(X + dx, Y, Z + dz, B.TORCH, { mount: 'floor' });
    processLightDirty(Infinity); return [X, Y, Z];
  });
  await hold([room[0] + .5, room[1] + .2, room[2] + 4.5, 0, 0.12]);
  const cave = await meanRGB(await shot('cave'), [100, 60, 600, 330]);
  check('cave-not-tinted', cave[2] - cave[0] < 20, `mean RGB ${cave.map(v => v.toFixed(0)).join(',')} (blue-red = ${(cave[2] - cave[0]).toFixed(0)})`);

  // 3. Water behind clear and stained glass must be visible (compare with the tank drained).
  const tank = await ev(() => {
    const X = Math.floor(player.x) - 40, Z = Math.floor(player.z), Y = 150;
    for (let x = X - 1; x <= X + 6; x++) for (let y = Y - 1; y <= Y + 4; y++) for (let z = Z - 4; z <= Z; z++) {
      const shell = x === X - 1 || x === X + 6 || y === Y - 1 || z === Z - 4;
      if (shell) setBlock(x, y, z, B.STONE);
      else if (z === Z) { if (y <= Y + 2) setBlock(x, y, z, x < X + 3 ? B.GLASS : B.VIRTUAL_TRANSPARENT, x < X + 3 ? null : { v: 'LIGHT_BLUE_STAINED_GLASS' }); }
      else if (y <= Y + 2) setBlock(x, y, z, B.WATER);
    }
    processLightDirty(Infinity); return [X, Y, Z];
  });
  const tankView = [tank[0] + 2.5, tank[1] + 1.4, tank[2] + 4.2, 0, 0.05];
  // Regions inside one glass pane on the clear side and on the stained side.
  const clearR = [230, 330, 60, 50], stainedR = [520, 330, 60, 50];
  await hold(tankView);
  const withWater = await shot('tank_water');
  await ev(t => { const [X, Y, Z] = t; for (let x = X; x <= X + 5; x++) for (let y = Y; y <= Y + 2; y++) for (let z = Z - 3; z <= Z - 1; z++) setBlock(x, y, z, B.AIR); }, tank);
  await hold(tankView);
  const drained = await shot('tank_drained');
  const dist = (a, b) => Math.abs(a[0] - b[0]) + Math.abs(a[1] - b[1]) + Math.abs(a[2] - b[2]);
  const dClear = dist(await meanRGB(withWater, clearR), await meanRGB(drained, clearR));
  const dStained = dist(await meanRGB(withWater, stainedR), await meanRGB(drained, stainedR));
  check('water-behind-clear-glass', dClear > 12, `colour change when water is removed: ${dClear.toFixed(1)}`);
  check('water-behind-stained-glass', dStained > 12, `colour change when water is removed: ${dStained.toFixed(1)}`);

  // 4. X-Ray: glass/water are drawn with terrainProg, whose camera matrix must be current.
  await ev(() => { xrayActive = true; });
  await hold([tankView[0] + 3, tankView[1], tankView[2] + 2, 0.4, 0.1], 4);
  const xr = await ev(() => { const u = gl.getUniform(terrainProg, U.vp); let d = 0; for (let i = 0; i < 16; i++) d = Math.max(d, Math.abs(u[i] - vp[i])); return d; });
  await ev(() => { xrayActive = false; });
  check('xray-camera-uniforms', xr < 1e-3, `max |terrainProg.uVP - vp| = ${xr.toExponential(2)}`);

  // 5. Cave culling + buried-face removal must not change the image (only isolated crack pixels may differ).
  if (await ev(() => typeof sectionCulling !== 'undefined')) {
    const cullViews = [[room[0] + .5, room[1] + .2, room[2] + 4.5, 0, 0.12], [room[0] + .5, 90, room[2] + .5, 0.6, 1.1], [room[0] + .5, 75, room[2] + .5, 2.2, 0.15]];
    let worst = 0, cut = [];
    await ev(() => { pauseOpen = true; mainLightFlicker = 0; mainLightFlickerTarget = 0; mainLightFlickerAt = 1e15; });
    for (const v of cullViews) {
      await ev(() => { sectionCulling = true; drawHiddenFaces = false; }); await hold(v, 6);
      const on = await shot('cull_on'); const tOn = await ev(() => triangles);
      await ev(() => { sectionCulling = false; drawHiddenFaces = true; }); await hold(v, 4);
      const off = await shot('cull_off'); const tOff = await ev(() => triangles);
      const n = await decoder.evaluate(async ([a, b]) => { const ld = async s => { const im = new Image(); im.src = 'data:image/png;base64,' + s; await im.decode(); const c = document.createElement('canvas'); c.width = im.width; c.height = im.height; const g = c.getContext('2d'); g.drawImage(im, 0, 0); return g.getImageData(0, 0, c.width, c.height).data; }; const A = await ld(a), B2 = await ld(b); let n = 0; for (let i = 0; i < A.length; i += 4) if (A[i] !== B2[i] || A[i + 1] !== B2[i + 1] || A[i + 2] !== B2[i + 2]) n++; return n; }, [on.toString('base64'), off.toString('base64')]);
      worst = Math.max(worst, n); cut.push(`${Math.round(tOff / 1000)}k->${Math.round(tOn / 1000)}k`);
    }
    await ev(() => { sectionCulling = true; drawHiddenFaces = false; pauseOpen = false; });
    check('culling-image-identical', worst <= 800 * 450 * 0.0005, `max differing pixels ${worst} (triangles ${cut.join(', ')})`);
  }

  // 6. Streaming on a 144 Hz display (short frame budget) while flying: the world must keep loading.
  await ev(() => {
    const ob = window.frameBackgroundBudget;
    window.frameBackgroundBudget = function (hot) { streamFrameMs = 6.94; return ob(hot); };
    player.flying = true; player.yaw = 0; player.pitch = 0.2; player.y = 110; touchActive = true; keys.KeyW = true; keys.ControlLeft = true;
  });
  await page.waitForTimeout(20000);
  const st = await ev(() => {
    keys.KeyW = false;
    const pcx = Math.floor(player.x / 16), pcz = Math.floor(player.z / 16); let missing = 0, total = 0;
    for (let dz = -renderDistance; dz <= renderDistance; dz++) for (let dx = -renderDistance; dx <= renderDistance; dx++) { total++; const c = chunkFastGet(pcx + dx, pcz + dz); if (!c || !c.meshBuilt || !c.meshComplete) missing++; }
    return { missing, total, travelled: Math.round(-player.z), genDone: genDoneCount(), meshDone: meshDoneCount(), lightQ: lightDirtyCount() };
  });
  await shot('streaming_144hz');
  check('streaming-144hz', st.missing < st.total * 0.5 && st.genDone < 64 && st.meshDone < 64,
    `missing ${st.missing}/${st.total} chunks after ${st.travelled} blocks; queued gen ${st.genDone}, mesh ${st.meshDone}`);
  check('light-queue-bounded', st.lightQ < 4000, `pending light repairs ${st.lightQ}`);

  check('no-page-errors', errors.length === 0, errors.length ? errors.slice(0, 5).join(' | ') : 'none');
} catch (e) {
  check('run', false, e.stack || String(e));
} finally {
  await browser.close(); server.close();
}
const failed = results.filter(r => !r.ok).length;
console.log(`\n${results.length - failed}/${results.length} checks passed`);
process.exit(failed ? 1 : 0);

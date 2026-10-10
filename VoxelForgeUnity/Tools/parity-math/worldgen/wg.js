// JS side of the worldgen parity check: runs the reference createWorldGenKernel from pretty.js.
//   node wg.js <pretty.js> <resDir> <B.json> find   <seed> <outList>             structure / mineshaft / ravine / well chunks
//   node wg.js <pretty.js> <resDir> <B.json> chunks <seed> <chunkList> <outDir>   blocks (.bin), meta (.meta), biome/height (.bio)
//   node wg.js <pretty.js> <resDir> <B.json> an     <seed> <ptsFile> <outFile>    per-point analysis (incl. dryAt, terrainDensity)
//   node wg.js <pretty.js> <resDir> <B.json> rav    <seed> <cells> <outFile>      ravine plans (node x/z/r bits) for cells in [-cells,cells)^2
// B.json (block name -> id) is written by the C# side, so both kernels see the same ids.
'use strict';
const fs = require('fs'), path = require('path');
const [, , PRETTY, RES, BJSON, MODE, SEEDS, A1, A2] = process.argv;
const R = RES.endsWith('/') ? RES : RES + '/';
const lines = fs.readFileSync(PRETTY, 'utf8').split('\n');
const a = lines.findIndex(l => l.startsWith('function createWorldGenKernel(')); if (a < 0) throw new Error('createWorldGenKernel not found');
let b = a; while (lines[b] !== '}') b++;
let src = lines.slice(a, b + 1).join('\n');
const sub = (t, v) => { if (!src.includes(t)) throw new Error('anchor not found: ' + t); src = src.replace(t, v); };
sub('/*EXTRACTED MAIN_CLIMATE_B64.bin*/ ""', JSON.stringify(fs.readFileSync(R + 'main_climate.bytes').toString('base64')));
sub('/*EXTRACTED MAIN_SHIPS.json*/ null', fs.readFileSync(R + 'main_ships.json', 'utf8'));
sub('/*EXTRACTED MAIN_PYRAMID_BLOCKS.json*/ null', fs.readFileSync(R + 'main_pyramid_blocks.json', 'utf8'));
sub('/*EXTRACTED MAIN_OUTPOST_BLOCKS.json*/ null', fs.readFileSync(R + 'main_outpost_blocks.json', 'utf8'));
sub('  return {\n    setSeed,', '  return {planRavine,structureCandidate,planMineshaft,desertWellAt,MAIN_VALLEY_OFFSETS,\n    setSeed,'); // test-only exports
const B = JSON.parse(fs.readFileSync(BJSON, 'utf8'));
const seed = parseInt(SEEDS);
const K = new Function('B', 'initialSeed', src + '\nreturn createWorldGenKernel(B,initialSeed);')(B, seed);
const F = new Float64Array(1), U = new Uint32Array(F.buffer);
const hx = v => { F[0] = v; return U[1].toString(16).padStart(8, '0') + U[0].toString(16).padStart(8, '0'); };

if (MODE === 'find') {
  const out = [];
  for (const t of ['pyramid', 'outpost', 'portal', 'ship']) { let n = 0; for (let rx = -12; rx < 12 && n < 3; rx++) for (let rz = -12; rz < 12 && n < 3; rz++) { const s = K.structureCandidate(t, rx, rz); if (s) { out.push([s.acx, s.acz]); n++; } } }
  let n = 0; for (let x = -200; x < 200 && n < 3; x++) for (let z = -60; z < 60 && n < 3; z++) if (K.planMineshaft(x, z)) { out.push([x, z], [x + 1, z]); n++; }
  n = 0; for (let x = -200; x < 200 && n < 4; x++) for (let z = -20; z < 20 && n < 4; z++) { const p = K.planRavine(x, z); if (p) { const m = p.nodes[p.nodes.length >> 1]; out.push([x, z], [Math.floor(m[0] / 16), Math.floor(m[1] / 16)]); n++; } }
  n = 0; for (let x = -500; x < 500 && n < 3; x++) for (let z = -200; z < 200 && n < 3; z++) if (K.desertWellAt(x, z)) { out.push([x, z]); n++; }
  fs.writeFileSync(A1, out.map(p => p.join(' ')).join('\n') + '\n');
} else if (MODE === 'chunks') {
  const outDir = A2;
  for (const line of fs.readFileSync(A1, 'utf8').trim().split('\n')) {
    const [cx, cz] = line.split(' ').map(Number);
    K.setSeed(seed);
    const d = K.generateDense(cx, cz);
    fs.writeFileSync(`${outDir}/js_${seed}_${cx}_${cz}.bin`, Buffer.from(d.buffer, d.byteOffset, d.length));
    const meta = K.structureMetaForChunk(cx, cz).concat(K.seaPickleMetaForDense(d, cx, cz), K.caveVineMetaForDense(d, cx, cz));
    let ms = ''; for (const m of meta) { ms += `${m[0]},${m[1]},${m[2]}:`; for (const k of Object.keys(m[3]).sort()) ms += `${k}=${String(m[3][k])};`; ms += '\n'; }
    fs.writeFileSync(`${outDir}/js_${seed}_${cx}_${cz}.meta`, ms);
    let bi = '';
    for (let z = 0; z < 16; z++) for (let x = 0; x < 16; x++) { const X = cx * 16 + x, Z = cz * 16 + z, c = K.columnInfo(X, Z); bi += `${c.biome} ${c.origH} ${K.heightAt(X, Z)} ${K.generatedSurfaceAt(X, Z)} ${hx(c.origH)} ${hx(c.factor)} ${hx(c.mAmp)} ${hx(c.peak)}\n`; }
    fs.writeFileSync(`${outDir}/js_${seed}_${cx}_${cz}.bio`, bi);
  }
} else if (MODE === 'an') {
  let o = '';
  for (const line of fs.readFileSync(A1, 'utf8').trim().split('\n')) {
    const [x, y, z] = line.split(' ').map(Number);
    const f = K.featureInfoAt(x, z), lp = K.landPlantInfoAt(x, z), c = K.columnInfo(x, z);
    o += [K.baseBlock(x, y, z), K.terrainBlock(x, y, z), K.landPlantAt(x, z), K.treeAt(x, z) ? 1 : 0, K.aquaPlantAt(x, y, z), K.structureChestKindAt(x, y, z) ?? 'null',
      K.caveBiomeAt(x, y, z), f ? f.kind + '/' + f.height : 'null', lp ? lp.id + '/' + lp.height : 'null', K.iceSpikeInfoAt(x, z), K.bambooInfoAt(x, z), K.cactusAt(x, z) ? 1 : 0,
      String(K.dryAt(x, z)), String(K.terrainDensity(x, y, z)), K.mineshaftsForChunk(x >> 4, z >> 4).length,
      hx(K.dryAt(x, z)), hx(K.terrainDensity(x, y, z)), c.biome, hx(c.origH), hx(c.temp), hx(c.moist), hx(c.continental), hx(c.erosion), hx(c.weird), hx(c.peak), hx(c.factor), hx(c.mAmp),
      K.heightAt(x, z), K.heightAtRaw(x, z)].join(' ') + '\n';
  }
  fs.writeFileSync(A2, o);
} else if (MODE === 'rav') {
  const C = parseInt(A1); let o = 'valley ' + K.MAIN_VALLEY_OFFSETS.map(q => q.map(hx).join(',')).join(' ') + '\n';
  for (let x = -C; x < C; x++) for (let z = -C; z < C; z++) {
    const p = K.planRavine(x, z); if (!p) continue;
    o += `${x} ${z} ${p.depth} ${hx(p.waterFrac)} ${hx(p.minx)} ${hx(p.maxx)} ${hx(p.minz)} ${hx(p.maxz)} ` + p.nodes.map(n => hx(n[0]) + ',' + hx(n[1]) + ',' + hx(n[2])).join(' ') + '\n';
  }
  fs.writeFileSync(A2, o);
} else throw new Error('unknown mode ' + MODE);

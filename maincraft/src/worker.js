'use strict';
// Worker entry: world generation and section meshing.
let GEN = null, MESHER = null;
self.onmessage = function (e) {
  const d = e.data;
  if (d.t === 'init') {
    resolveBlockTextures(d.layers);
    initMesherTextures(d.layers);
    GEN = new WorldGen(d.seed);
    MESHER = new Mesher();
    self.postMessage({ t: 'ready' });
    return;
  }
  if (d.t === 'gen') {
    const r = GEN.generate(d.cx, d.cz);
    const tr = [r.biomes.buffer, r.caveBiomes.buffer];
    for (const s of r.sections) if (s) { tr.push(s.ids.buffer, s.meta.buffer); }
    self.postMessage({ t: 'gen', cx: d.cx, cz: d.cz, sections: r.sections, biomes: r.biomes, caveBiomes: r.caveBiomes, springs: r.springs, spawners: r.spawners }, tr);
    return;
  }
  if (d.t === 'mesh') {
    MESHER.sway = d.sway !== false;
    const r = MESHER.mesh(d);
    self.postMessage({ t: 'mesh', cx: d.cx, cz: d.cz, sy: d.sy, ver: d.ver, data: r.data, counts: r.counts, gc: r.gc, vis: r.vis,
      ids: d.ids, meta: d.meta, light: d.light }, [r.data.buffer, d.ids.buffer, d.meta.buffer, d.light.buffer]);
    return;
  }
  if (d.t === 'surface') {
    self.postMessage({ t: 'surface', id: d.id, y: GEN.surfaceAt(d.x, d.z), biome: GEN.colInfo(d.x, d.z).biome });
  }
};

'use strict';
// Bootstrap: decode embedded assets, start the game.

function loadImage(b64) {
  return new Promise((res, rej) => { const i = new Image(); i.onload = () => res(i); i.onerror = rej; i.src = 'data:image/png;base64,' + b64; });
}
async function boot() {
  Settings.load();
  if (Settings.renderDist > 24) Settings.renderDist = 24;
  const A = ASSETS;
  const [atlas, sun, moon] = await Promise.all([loadImage(A.atlas), loadImage(A.sun), loadImage(A.moon)]);
  const ents = {};
  await Promise.all(Object.keys(A.entities).map(async k => { ents[k] = await loadImage(A.entities[k]); }));
  const assets = { atlasImage: atlas, sunImage: sun, moonImage: moon, entityImages: ents, layers: A.layers, anim: A.anim, count: A.count, cols: A.cols };
  resolveBlockTextures(A.layers);
  const workerSrc = document.getElementById('worker-src').textContent;
  let game;
  try { game = new Game(assets, workerSrc); }
  catch (e) { document.getElementById('fatal').textContent = 'WebGL2: ' + e.message; show('fatal', true); throw e; }
  assets.atlasPixels = game.r.atlasPixels;
  Icons.init(assets);
  window.game = game;
  UI.init(game);
  UI.showTitle();
  show('boot', false);
}
boot();

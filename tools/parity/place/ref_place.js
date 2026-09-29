// Evaluated inside the headless reference game (tools/refgame/run.mjs). Expects a global
// __SCENARIO__ (prepended by run_place.sh). Runs every test through main.js kT()/gT()/nA() and
// returns {testName: [[x,y,z,KEY,meta],...]} for the cell region after the test.
(() => {
  const b = window.__bc, o = b.o, K = b.K, SC = __SCENARIO__;
  const inv = {}; for (const k in o) inv[o[k]] = k;
  const O = [Math.floor(b.Q.pos[0]) + 40, 200, Math.floor(b.Q.pos[2]) + 40];
  const savePos = b.Q.pos.slice(), saveYaw = b.Q.yaw, saveMode = b.p.mode, saveInv = b.p.survInv[b.p.hotbarSel];
  const id = k => { if (!(k in o)) throw new Error('unknown key ' + k); return o[k]; };
  function chunk(x, z) {
    let c = b.X1(x, z);
    if (!c) { c = b.Qi(Math.floor(x / 16), Math.floor(z / 16), new Array(b.Zr).fill(null)); b.wt.chunks.set(b.DA(c.cx, c.cz), c); }
    return c;
  }
  function raw(x, y, z, bid, meta) {
    const c = chunk(x, z), n = y - K, s = b.q0(c, n >> 4), i = b.Pn(x & 15, z & 15, n & 15);
    s.blocks[i] = bid; s.meta[i] = meta || 0;
  }
  const LO = -3, HI = 10;
  const out = {};
  b.p.mode = 'creative';
  for (const t of SC.tests) {
    for (let x = LO; x < HI; x++) for (let y = LO; y < HI; y++) for (let z = LO; z < HI; z++) raw(O[0] + x, O[1] + y, O[2] + z, 0, 0);
    if (!t.noFloor) for (let x = 0; x < 4; x++) for (let z = 0; z < 4; z++) raw(O[0] + x, O[1], O[2] + z, id('STONE'), 0);
    for (const f of t.fixture) raw(O[0] + f.x, O[1] + f.y, O[2] + f.z, id(f.id), f.meta);
    const pl = t.player || [-30, 0, -30];
    b.Q.pos[0] = O[0] + pl[0]; b.Q.pos[1] = O[1] + pl[1]; b.Q.pos[2] = O[2] + pl[2];
    let err = null;
    try {
      for (const a of t.actions) {
        if (a.op === 'raw') { raw(O[0] + a.x, O[1] + a.y, O[2] + a.z, id(a.id), a.meta); continue; }
        if (a.op === 'edit') { b.nA(O[0] + a.x, O[1] + a.y, O[2] + a.z, id(a.id), !0, a.meta || 0); continue; }
        const [x, y, z] = [O[0] + a.hit[0], O[1] + a.hit[1], O[2] + a.hit[2]];
        const A = { x, y, z, id: b.S(x, y, z) };
        if (a.op === 'break') { b.gT(A); continue; }
        const k = a.k;
        A.face = [-k[0], -k[1], -k[2]]; A.px = x + k[0]; A.py = y + k[1]; A.pz = z + k[2];
        A.hx = O[0] + a.h[0]; A.hy = O[1] + a.h[1]; A.hz = O[2] + a.h[2];
        b.Q.yaw = a.yaw;
        b.p.survInv[b.p.hotbarSel] = a.held ? { id: id(a.held), count: 64 } : null;
        b.kT(A);
      }
    } catch (e) { err = String(e && e.stack || e).split('\n').slice(0, 3).join(' | '); }
    const cells = [];
    for (let x = LO; x < HI; x++) for (let y = LO; y < HI; y++) for (let z = LO; z < HI; z++) {
      const v = b.S(O[0] + x, O[1] + y, O[2] + z);
      if (v !== 0) cells.push([x, y, z, inv[v], b.RA(O[0] + x, O[1] + y, O[2] + z)]);
    }
    out[t.name] = err ? { err, cells } : cells;
  }
  for (let x = LO; x < HI; x++) for (let y = LO; y < HI; y++) for (let z = LO; z < HI; z++) raw(O[0] + x, O[1] + y, O[2] + z, 0, 0);
  b.Q.pos[0] = savePos[0]; b.Q.pos[1] = savePos[1]; b.Q.pos[2] = savePos[2]; b.Q.yaw = saveYaw; b.p.mode = saveMode;
  b.p.survInv[b.p.hotbarSel] = saveInv;
  return JSON.stringify(out);
})()

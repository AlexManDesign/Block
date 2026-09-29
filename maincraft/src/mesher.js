'use strict';
// Section mesher (worker side).
// Input: padded 18^3 arrays (ids, meta, light) around one 16^3 section.
// Output: packed quads (3 x uint32 per vertex, 4 vertices per quad) for three passes
// (solid, cutout, translucent) + face-to-face visibility graph for occlusion culling.
//
// vertex layout
//   w0: x | y<<10 | z<<20 | flags<<30      (positions in 1/32 block, section-local)
//   w1: layer | u<<11 | v<<16 | shade<<21   (u,v in texels 0..16, shade 0..255)
//   w2: sky | blk<<8                         (light*16, 0..240)

const P = 18, SX = 1, SZ = 18, SY = 324;
const pidx = (x, y, z) => ((y + 1) * P + (z + 1)) * P + (x + 1);

// face order: 0 +x, 1 -x, 2 +y, 3 -y, 4 +z, 5 -z
const FN = [[1, 0, 0], [-1, 0, 0], [0, 1, 0], [0, -1, 0], [0, 0, 1], [0, 0, -1]];
const FACE_SHADE = [0.72, 0.72, 1.0, 0.55, 0.82, 0.82];
// corners of each face (unit cube), CCW from outside: BL, BR, TR, TL
const FC = [
  [[1, 0, 1], [1, 0, 0], [1, 1, 0], [1, 1, 1]],
  [[0, 0, 0], [0, 0, 1], [0, 1, 1], [0, 1, 0]],
  [[0, 1, 1], [1, 1, 1], [1, 1, 0], [0, 1, 0]],
  [[1, 0, 1], [0, 0, 1], [0, 0, 0], [1, 0, 0]],
  [[0, 0, 1], [1, 0, 1], [1, 1, 1], [0, 1, 1]],
  [[1, 0, 0], [0, 0, 0], [0, 1, 0], [1, 1, 0]],
];
const FACE_OF_DIR = [4, 1, 5, 0];        // horizontal dir (0 S,1 W,2 N,3 E) -> face index
const DIRX = [0, -1, 0, 1], DIRZ = [1, 0, -1, 0];
const OPP_FACE = [1, 0, 3, 2, 5, 4];

// AO sample offsets per face/vertex: [side1, side2, corner] as padded index deltas (relative to face neighbour)
const AO_OFF = [];
const FACE_DELTA = FN.map(n => n[0] * SX + n[1] * SY + n[2] * SZ);
(function () {
  for (let f = 0; f < 6; f++) {
    const n = FN[f];
    const ax = n[0] !== 0 ? [1, 2] : n[1] !== 0 ? [0, 2] : [0, 1];
    const arr = [];
    for (let v = 0; v < 4; v++) {
      const c = FC[f][v];
      const o1 = [0, 0, 0], o2 = [0, 0, 0];
      o1[ax[0]] = c[ax[0]] ? 1 : -1;
      o2[ax[1]] = c[ax[1]] ? 1 : -1;
      const d = o => o[0] * SX + o[1] * SY + o[2] * SZ;
      arr.push([d(o1), d(o2), d([o1[0] + o2[0], o1[1] + o2[1], o1[2] + o2[2]])]);
    }
    AO_OFF.push(arr);
  }
})();
const AO_CURVE = [0.62, 0.7467, 0.8734, 1.0];

class QBuf {
  constructor(q) { this.d = new Uint32Array(q * 12); this.n = 0; }
  ensure() {
    if ((this.n + 2) * 12 > this.d.length) { const nd = new Uint32Array(this.d.length * 2); nd.set(this.d); this.d = nd; }
  }
}

class Mesher {
  constructor() {
    this.bufs = [new QBuf(8192), new QBuf(4096), new QBuf(2048)];
    this.vis = new Uint8Array(4096);
    this.stack = new Int32Array(4096);
    this.fancyLeaves = true;
    this.sway = true;
  }

  // push one quad: positions in 1/16 units (converted to 1/32), uv in texels
  quad(buf, px, py, pz, us, vs, layer, shades, skys, blks, flags, flip) {
    buf.ensure();
    const d = buf.d;
    let o = buf.n * 12;
    for (let k = 0; k < 4; k++) {
      const v = flip ? (k + 1) & 3 : k;
      let x = Math.round(px[v] * 2), y = Math.round(py[v] * 2), z = Math.round(pz[v] * 2);
      if (x < 0) x = 0; if (y < 0) y = 0; if (z < 0) z = 0;
      const fl = typeof flags === 'number' ? flags : flags[v];
      d[o++] = x | (y << 10) | (z << 20) | (fl << 30);
      let u = us[v], vv = vs[v];
      u = u < 0 ? 0 : u > 16 ? 16 : u; vv = vv < 0 ? 0 : vv > 16 ? 16 : vv;
      d[o++] = layer | (Math.round(u) << 11) | (Math.round(vv) << 16) | (shades[v] << 21);
      d[o++] = skys[v] | (blks[v] << 8);
    }
    buf.n++;
  }

  mesh(job) {
    const ids = job.ids, meta = job.meta, light = job.light;
    this.ids = ids; this.metaArr = meta; this.light = light;
    this.fancyLeaves = job.fancyLeaves !== false;
    for (const b of this.bufs) b.n = 0;
    const tpx = new Float32Array(4), tpy = new Float32Array(4), tpz = new Float32Array(4);
    const tu = new Float32Array(4), tv = new Float32Array(4);
    const sh = new Int32Array(4), sk = new Int32Array(4), bl = new Int32Array(4), bright = new Float32Array(4);
    this.t = { tpx, tpy, tpz, tu, tv, sh, sk, bl };
    for (let y = 0; y < 16; y++) for (let z = 0; z < 16; z++) {
      let p = pidx(0, y, z);
      for (let x = 0; x < 16; x++, p++) {
        const id = ids[p];
        if (id === 0) continue;
        const shape = SHAPE[id];
        if (shape === SH.CUBE) { this.cube(ids, meta, light, p, id, x, y, z, bright); continue; }
        this.special(ids, meta, light, p, id, shape, x, y, z);
      }
    }
    const vis = this.visibility(ids);
    const counts = this.bufs.map(b => b.n);
    const total = counts[0] + counts[1] + counts[2];
    const out = new Uint32Array(total * 12);
    let off = 0;
    for (const b of this.bufs) { out.set(b.d.subarray(0, b.n * 12), off); off += b.n * 12; }
    return { data: out, counts, vis };
  }

  // ---------------------------------------------------------------- full cubes
  cube(ids, meta, light, p, id, x, y, z, bright) {
    const fl = FLAGS[id];
    const layerBuf = this.bufs[RLAYER[id]];
    const leaves = (fl & BF_LEAVES) !== 0;
    const clear = (fl & (BF_GLASS | BF_TRANS)) !== 0;
    const emissive = (fl & BF_EMISSIVE) !== 0;
    const m = meta[p];
    const { tpx, tpy, tpz, tu, tv, sh, sk, bl } = this.t;
    const flagV = leaves && this.sway ? 2 : 0;
    for (let f = 0; f < 6; f++) {
      const np = p + FACE_DELTA[f];
      const n = ids[np];
      if (OPAQUE[n]) continue;
      if (n === id && (clear || (leaves && !this.fancyLeaves))) continue;
      if (clear && n !== 0 && (FLAGS[n] & (BF_GLASS | BF_TRANS)) && SHAPE[n] === SH.CUBE && RLAYER[n] === RLAYER[id] && (fl & BF_GLASS)) continue;
      // texture + uv rotation
      let layer = FTEX[id * 6 + f], rot = 0;
      if (fl & BF_AXIS) {
        const ax = m & 3;
        if (ax === 1) { // x axis
          if (f === 0 || f === 1) layer = FTEX[id * 6 + 2]; else { layer = FTEX[id * 6]; rot = f === 2 || f === 3 ? 1 : 1; }
        } else if (ax === 2) { // z axis
          if (f === 4 || f === 5) layer = FTEX[id * 6 + 2]; else { layer = FTEX[id * 6]; rot = f === 0 || f === 1 ? 1 : 0; }
        }
      }
      if ((fl & BF_FACING) && f === FACE_OF_DIR[m & 3]) layer = FRONT[id];
      const nl = light[np];
      const baseSky = nl >> 4, baseBlk = nl & 15;
      const corners = FC[f], aoo = AO_OFF[f];
      const fs = FACE_SHADE[f];
      for (let v = 0; v < 4; v++) {
        const c = corners[v];
        tpx[v] = (x + c[0]) * 16; tpy[v] = (y + c[1]) * 16; tpz[v] = (z + c[2]) * 16;
        const a = aoo[v];
        const i1 = np + a[0], i2 = np + a[1], i3 = np + a[2];
        const s1 = OPAQUE[ids[i1]], s2 = OPAQUE[ids[i2]], s3 = OPAQUE[ids[i3]];
        const ao = s1 && s2 ? 0 : 3 - (s1 + s2 + s3);
        let ssum = baseSky, bsum = baseBlk, cnt = 1;
        if (!s1) { const l = light[i1]; ssum += l >> 4; bsum += l & 15; cnt++; }
        if (!s2) { const l = light[i2]; ssum += l >> 4; bsum += l & 15; cnt++; }
        if (!s3 && !(s1 && s2)) { const l = light[i3]; ssum += l >> 4; bsum += l & 15; cnt++; }
        const skyv = Math.round(ssum * 16 / cnt), blkv = emissive ? 240 : Math.round(bsum * 16 / cnt);
        sk[v] = skyv; bl[v] = blkv;
        const s = fs * AO_CURVE[ao];
        sh[v] = Math.round(s * 255);
        bright[v] = ao * 4 + (Math.max(skyv, blkv) / 16);
      }
      this.faceUV(f, tpx, tpy, tpz, rot, tu, tv, x, y, z);
      const flip = bright[0] + bright[2] < bright[1] + bright[3];
      this.quad(layerBuf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, flagV, flip);
    }
  }

  // UV by projecting the (1/16 unit) positions onto the face plane (like MC uv-lock)
  faceUV(f, px, py, pz, rot, tu, tv, bx, by, bz) {
    const ox = bx * 16, oy = by * 16, oz = bz * 16;
    for (let v = 0; v < 4; v++) {
      const x = px[v] - ox, y = py[v] - oy, z = pz[v] - oz;
      let u, w;
      switch (f) {
        case 0: u = 16 - z; w = 16 - y; break;
        case 1: u = z; w = 16 - y; break;
        case 2: u = x; w = z; break;
        case 3: u = x; w = 16 - z; break;
        case 4: u = x; w = 16 - y; break;
        default: u = 16 - x; w = 16 - y; break;
      }
      for (let r = 0; r < rot; r++) { const t = u; u = 16 - w; w = t; }
      tu[v] = u; tv[v] = w;
    }
  }

  // ---------------------------------------------------------------- partial shapes
  // emit an axis aligned box (coords in 1/16 inside the block). tex: layer or array[6].
  // opts bits: cull faces on the block border against opaque neighbours (always), mask of faces to skip
  box(p, id, bx, by, bz, x0, y0, z0, x1, y1, z1, tex, skipMask, buf, extra) {
    const ids = this.ids, light = this.light;
    const { tpx, tpy, tpz, tu, tv, sh, sk, bl } = this.t;
    const own = light[p], ownS = own >> 4, ownB = own & 15;
    const emissive = (FLAGS[id] & BF_EMISSIVE) !== 0;
    const lo = [x0, y0, z0], hi = [x1, y1, z1];
    for (let f = 0; f < 6; f++) {
      if (skipMask & (1 << f)) continue;
      const n = FN[f];
      const onEdge = (n[0] === 1 && x1 >= 16) || (n[0] === -1 && x0 <= 0) || (n[1] === 1 && y1 >= 16) ||
        (n[1] === -1 && y0 <= 0) || (n[2] === 1 && z1 >= 16) || (n[2] === -1 && z0 <= 0);
      let s = ownS, b = ownB;
      if (onEdge) {
        const np = p + FACE_DELTA[f];
        const nid = ids[np];
        if (OPAQUE[nid]) continue;
        if (extra && extra.cullSame && nid === id) continue;
        const l = light[np];
        if ((l >> 4) > s) s = l >> 4; if ((l & 15) > b) b = l & 15;
      } else {
        // interior face: take max with face neighbour anyway for partial blocks touching light
        const l = light[p + FACE_DELTA[f]];
        if (!OPAQUE[ids[p + FACE_DELTA[f]]]) { if ((l >> 4) > s) s = l >> 4; if ((l & 15) > b) b = l & 15; }
      }
      if (emissive) b = 15;
      const c = FC[f];
      for (let v = 0; v < 4; v++) {
        const cc = c[v];
        tpx[v] = bx * 16 + (cc[0] ? hi[0] : lo[0]);
        tpy[v] = by * 16 + (cc[1] ? hi[1] : lo[1]);
        tpz[v] = bz * 16 + (cc[2] ? hi[2] : lo[2]);
      }
      if (extra && extra.shear) {
        const s2 = extra.shear;
        for (let v = 0; v < 4; v++) { const ry = tpy[v] - by * 16; tpx[v] += s2[0] * ry; tpz[v] += s2[1] * ry; }
      }
      const layer = typeof tex === 'number' ? tex : tex[f];
      this.faceUV(f, tpx, tpy, tpz, extra && extra.rot ? extra.rot[f] | 0 : 0, tu, tv, bx, by, bz);
      if (extra && extra.shear) {
        // uv from unsheared coordinates
        const s2 = extra.shear;
        for (let v = 0; v < 4; v++) { const ry = tpy[v] - by * 16; tpx[v] -= s2[0] * ry; tpz[v] -= s2[1] * ry; }
        this.faceUV(f, tpx, tpy, tpz, 0, tu, tv, bx, by, bz);
        for (let v = 0; v < 4; v++) { const ry = tpy[v] - by * 16; tpx[v] += s2[0] * ry; tpz[v] += s2[1] * ry; }
      }
      if (extra && extra.uvOverride && extra.uvOverride[f]) {
        const o = extra.uvOverride[f];
        tu[0] = o[0]; tv[0] = o[3]; tu[1] = o[2]; tv[1] = o[3]; tu[2] = o[2]; tv[2] = o[1]; tu[3] = o[0]; tv[3] = o[1];
      }
      if (extra && extra.flipU && extra.flipU[f]) for (let v = 0; v < 4; v++) tu[v] = 16 - tu[v];
      const fs = Math.round(FACE_SHADE[f] * 255);
      for (let v = 0; v < 4; v++) { sh[v] = fs; sk[v] = s * 16; bl[v] = b * 16; }
      this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, false);
    }
  }

  // flat double sided quad given 4 corner positions (1/16 units, absolute in section) and uv rect
  flat(buf, pts, layer, u0, v0, u1, v1, s, b, shade, flags, twoSided) {
    const { tpx, tpy, tpz, tu, tv, sh, sk, bl } = this.t;
    for (let v = 0; v < 4; v++) { tpx[v] = pts[v * 3]; tpy[v] = pts[v * 3 + 1]; tpz[v] = pts[v * 3 + 2]; }
    tu[0] = u0; tv[0] = v1; tu[1] = u1; tv[1] = v1; tu[2] = u1; tv[2] = v0; tu[3] = u0; tv[3] = v0;
    const shv = Math.round(shade * 255);
    for (let v = 0; v < 4; v++) { sh[v] = shv; sk[v] = s * 16; bl[v] = b * 16; }
    this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, flags, false);
    if (twoSided) {
      // reversed winding: swap v1<->v3
      let t;
      t = tpx[1]; tpx[1] = tpx[3]; tpx[3] = t; t = tpy[1]; tpy[1] = tpy[3]; tpy[3] = t; t = tpz[1]; tpz[1] = tpz[3]; tpz[3] = t;
      t = tu[1]; tu[1] = tu[3]; tu[3] = t; t = tv[1]; tv[1] = tv[3]; tv[3] = t;
      if (typeof flags !== 'number') { const f2 = [flags[0], flags[3], flags[2], flags[1]]; flags = f2; }
      this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, flags, false);
    }
  }

  maxLight(p) {
    // own light maxed with the 6 neighbours (used for plants / thin shapes)
    const light = this.light, ids = this.ids;
    let l = light[p], s = l >> 4, b = l & 15;
    for (let f = 0; f < 6; f++) {
      const q = p + FACE_DELTA[f];
      if (OPAQUE[ids[q]]) continue;
      const m = light[q];
      if ((m >> 4) > s) s = m >> 4; if ((m & 15) > b) b = m & 15;
    }
    return (s << 4) | b;
  }

  cross(buf, x, y, z, layer, lt, inset, sway, hgt) {
    const a = inset, c = 16 - inset, H = hgt || 16;
    const s = lt >> 4, b = lt & 15;
    const X = x * 16, Y = y * 16, Z = z * 16;
    const fl = sway ? [0, 0, 1, 1] : 0;
    this.flat(buf, [X + a, Y, Z + a, X + c, Y, Z + c, X + c, Y + H, Z + c, X + a, Y + H, Z + a], layer, 0, 16 - H, 16, 16, s, b, 0.95, fl, true);
    this.flat(buf, [X + c, Y, Z + a, X + a, Y, Z + c, X + a, Y + H, Z + c, X + c, Y + H, Z + a], layer, 0, 16 - H, 16, 16, s, b, 0.95, fl, true);
  }

  special(ids, meta, light, p, id, shape, x, y, z) {
    this.ids = ids; this.light = light;
    const m = meta[p];
    const buf = this.bufs[RLAYER[id]];
    const T = id * 6;
    const texs = [FTEX[T], FTEX[T + 1], FTEX[T + 2], FTEX[T + 3], FTEX[T + 4], FTEX[T + 5]];
    switch (shape) {
      case SH.WATER: case SH.LAVA:
        this.fluid(ids, meta, light, p, id, x, y, z);
        return;
      case SH.CROSS: {
        if (FLAGS[id] & BF_AQUATIC) this.fluid(ids, meta, light, p, B.WATER, x, y, z, true);
        const lt = this.maxLight(p);
        const sway = this.sway && (FLAGS[id] & BF_PLANT) && !(FLAGS[id] & BF_AQUATIC) && id !== B.COBWEB;
        let layer = texs[2];
        if (id === B.CAVE_VINES && (m & 1)) layer = texs[2]; // glow berries use same texture
        this.cross(buf, x, y, z, layer, lt, 2.4, sway);
        return;
      }
      case SH.TALL: {
        const lt = this.maxLight(p);
        this.cross(buf, x, y, z, (m & 1) ? texs[2] : texs[3], lt, 2.4, this.sway && (m & 1));
        return;
      }
      case SH.FIRE: {
        const lt = this.maxLight(p);
        this.cross(buf, x, y, z, texs[2], lt | 15, 0.5, false);
        return;
      }
      case SH.SLAB: {
        if (m & 1) this.box(p, id, x, y, z, 0, 8, 0, 16, 16, 16, texs, 0, buf);
        else this.box(p, id, x, y, z, 0, 0, 0, 16, 8, 16, texs, 0, buf);
        return;
      }
      case SH.STAIRS: { this.stairs(p, id, m, x, y, z, texs, buf); return; }
      case SH.FENCE: { this.fence(p, id, x, y, z, texs, buf); return; }
      case SH.WALL: { this.wall(p, id, x, y, z, texs, buf); return; }
      case SH.PANE: { this.pane(p, id, x, y, z, texs, buf); return; }
      case SH.GATE: { this.gate(p, id, m, x, y, z, texs, buf); return; }
      case SH.DOOR: {
        const upper = (m >> 2) & 1, open = (m >> 3) & 1, hingeR = (m >> 4) & 1, f = m & 3;
        const t = upper ? texs[2] : texs[3];
        // closed: thin slab on the edge opposite to facing; open: rotated around the hinge
        let bxs = [0, 0, 13, 16, 16, 16]; // north-facing closed box (south edge)
        if (open) bxs = hingeR ? [13, 0, 0, 16, 16, 16] : [0, 0, 0, 3, 16, 16];
        const r = rotBox(bxs, (f + 2) & 3);
        const flipU = [false, false, false, false, false, false];
        if (hingeR) { flipU[0] = flipU[1] = flipU[4] = flipU[5] = true; }
        this.box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], t, 0, buf, { flipU });
        return;
      }
      case SH.TRAPDOOR: {
        const f = m & 3, top = (m >> 2) & 1, open = (m >> 3) & 1;
        let bxs;
        if (open) bxs = rotBox([0, 0, 13, 16, 16, 16], (f + 2) & 3);
        else bxs = top ? [0, 13, 0, 16, 16, 16] : [0, 0, 0, 16, 3, 16];
        this.box(p, id, x, y, z, bxs[0], bxs[1], bxs[2], bxs[3], bxs[4], bxs[5], texs[2], 0, buf);
        return;
      }
      case SH.LADDER: {
        const f = m & 3; // facing: ladder is on the wall opposite to its facing
        const lt = this.maxLight(p), s = lt >> 4, b = lt & 15;
        const X = x * 16, Y = y * 16, Z = z * 16, e = 15.2;
        let pts;
        if (f === 2) pts = [X, Y, Z + e, X + 16, Y, Z + e, X + 16, Y + 16, Z + e, X, Y + 16, Z + e];          // north-facing, on south wall
        else if (f === 0) pts = [X + 16, Y, Z + 16 - e, X, Y, Z + 16 - e, X, Y + 16, Z + 16 - e, X + 16, Y + 16, Z + 16 - e];
        else if (f === 1) pts = [X + 16 - e, Y, Z, X + 16 - e, Y, Z + 16, X + 16 - e, Y + 16, Z + 16, X + 16 - e, Y + 16, Z];
        else pts = [X + e, Y, Z + 16, X + e, Y, Z, X + e, Y + 16, Z, X + e, Y + 16, Z + 16];
        this.flat(buf, pts, texs[0], 0, 0, 16, 16, s, b, 0.9, 0, true);
        return;
      }
      case SH.TORCH: {
        const uvo = [null, null, [7, 6, 9, 8], [7, 14, 9, 16], null, null];
        if (m === 0 || m > 4) {
          this.box(p, id, x, y, z, 7, 0, 7, 9, 10, 9, texs[0], 0, buf, { uvOverride: uvo });
        } else {
          const d = m - 1; // wall direction: 0 S,1 W,2 N,3 E (wall is on that side)
          const dx = DIRX[d], dz = DIRZ[d];
          const ox = dx * 5.5, oz = dz * 5.5;
          this.box(p, id, x, y, z, 7 + ox, 3.5, 7 + oz, 9 + ox, 13.5, 9 + oz, texs[0], 0, buf,
            { uvOverride: uvo, shear: [-dx * 0.42, -dz * 0.42] });
        }
        return;
      }
      case SH.CARPET: this.box(p, id, x, y, z, 0, 0, 0, 16, 1, 16, texs, 0, buf); return;
      case SH.PLATE: this.box(p, id, x, y, z, 1, 0, 1, 15, 1, 15, texs, 0, buf); return;
      case SH.BUTTON: {
        const d = m & 3;
        const r = rotBox([5, 6, 0, 11, 10, 2], (d + 2) & 3);
        this.box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], texs, 0, buf);
        return;
      }
      case SH.FARMLAND: this.box(p, id, x, y, z, 0, 0, 0, 16, 15, 16, texs, 0, buf); return;
      case SH.CACTUS: {
        this.box(p, id, x, y, z, 0, 0, 0, 16, 16, 16, texs, 0b110011, buf);
        this.box(p, id, x, y, z, 1, 0, 0, 15, 16, 16, texs, 0b111100, buf);
        this.box(p, id, x, y, z, 0, 0, 1, 16, 16, 15, texs, 0b001111, buf);
        return;
      }
      case SH.CHEST: {
        const f = m & 3, t = texs.slice();
        t[FACE_OF_DIR[f]] = FRONT[id];
        this.box(p, id, x, y, z, 1, 0, 1, 15, 14, 15, t, 0, buf);
        return;
      }
      case SH.BED: {
        const f = m & 3, head = (m >> 2) & 1;
        const t = texs.slice();
        t[2] = head ? LAYER_BED_HEAD[id] || texs[2] : texs[2];
        const rot = [0, 0, (f + 2) & 3, 0, 0, 0];
        this.box(p, id, x, y, z, 0, 3, 0, 16, 9, 16, t, 0, buf, { rot });
        // legs
        const lg = BED_LEG;
        this.box(p, id, x, y, z, 0, 0, 0, 3, 3, 3, lg, 0, buf); this.box(p, id, x, y, z, 13, 0, 0, 16, 3, 3, lg, 0, buf);
        this.box(p, id, x, y, z, 0, 0, 13, 3, 3, 16, lg, 0, buf); this.box(p, id, x, y, z, 13, 0, 13, 16, 3, 16, lg, 0, buf);
        return;
      }
      case SH.BAMBOO: this.box(p, id, x, y, z, 7, 0, 7, 9, 16, 9, texs[0], 0, buf); return;
      case SH.POT: this.box(p, id, x, y, z, 5, 0, 5, 11, 6, 11, texs, 0, buf); return;
      case SH.PICKLE: {
        this.fluid(ids, meta, light, p, B.WATER, x, y, z, true);
        const n = (m & 3) + 1, pos = [[6, 6], [3, 4], [10, 9], [5, 11]];
        for (let i = 0; i < n; i++) this.box(p, id, x, y, z, pos[i][0], 0, pos[i][1], pos[i][0] + 4, 6, pos[i][1] + 4, texs, 0, buf);
        return;
      }
      case SH.LILY: {
        const lt = this.maxLight(p);
        const X = x * 16, Y = y * 16 + 0.3, Z = z * 16;
        const r = m & 3;
        const P4 = [[X, Z + 16], [X + 16, Z + 16], [X + 16, Z], [X, Z]];
        const q = [];
        for (let k = 0; k < 4; k++) { const c = P4[(k + r) & 3]; q.push(c[0], Y, c[1]); }
        this.flat(buf, q, texs[2], 0, 0, 16, 16, lt >> 4, lt & 15, 1.0, 0, true);
        return;
      }
      case SH.RAIL: {
        const lt = this.maxLight(p);
        const X = x * 16, Y = y * 16 + 1, Z = z * 16;
        const q = (m & 1) ? [X, Y, Z, X, Y, Z + 16, X + 16, Y, Z + 16, X + 16, Y, Z] : [X, Y, Z + 16, X + 16, Y, Z + 16, X + 16, Y, Z, X, Y, Z];
        this.flat(buf, q, texs[2], 0, 0, 16, 16, lt >> 4, lt & 15, 1.0, 0, true);
        return;
      }
      case SH.VINE: case SH.LICHEN: {
        const lt = this.maxLight(p), s = lt >> 4, b = shape === SH.LICHEN ? Math.max(lt & 15, 7) : lt & 15;
        const X = x * 16, Y = y * 16, Z = z * 16, e = 0.8;
        const t = texs[2];
        const bits = m || 1;
        if (bits & 1) this.flat(buf, [X + 16, Y, Z + 16 - e, X, Y, Z + 16 - e, X, Y + 16, Z + 16 - e, X + 16, Y + 16, Z + 16 - e], t, 0, 0, 16, 16, s, b, 0.8, 0, true);
        if (bits & 4) this.flat(buf, [X, Y, Z + e, X + 16, Y, Z + e, X + 16, Y + 16, Z + e, X, Y + 16, Z + e], t, 0, 0, 16, 16, s, b, 0.8, 0, true);
        if (bits & 2) this.flat(buf, [X + e, Y, Z + 16, X + e, Y, Z, X + e, Y + 16, Z, X + e, Y + 16, Z + 16], t, 0, 0, 16, 16, s, b, 0.72, 0, true);
        if (bits & 8) this.flat(buf, [X + 16 - e, Y, Z, X + 16 - e, Y, Z + 16, X + 16 - e, Y + 16, Z + 16, X + 16 - e, Y + 16, Z], t, 0, 0, 16, 16, s, b, 0.72, 0, true);
        if (bits & 16) this.flat(buf, [X, Y + e, Z + 16, X + 16, Y + e, Z + 16, X + 16, Y + e, Z, X, Y + e, Z], t, 0, 0, 16, 16, s, b, 1, 0, true);
        if (bits & 32) this.flat(buf, [X, Y + 16 - e, Z, X + 16, Y + 16 - e, Z, X + 16, Y + 16 - e, Z + 16, X, Y + 16 - e, Z + 16], t, 0, 0, 16, 16, s, b, 0.55, 0, true);
        return;
      }
      case SH.SNOWLAYER: this.box(p, id, x, y, z, 0, 0, 0, 16, 2 * ((m & 7) + 1), 16, texs, 0, buf); return;
      default:
        this.box(p, id, x, y, z, 0, 0, 0, 16, 16, 16, texs, 0, buf);
    }
  }

  // ---------------------------------------------------------------- stairs
  stairs(p, id, m, x, y, z, texs, buf) {
    const ids = this.ids, meta = this.metaArr;
    const f = m & 3, top = (m >> 2) & 1;
    const isSt = q => SHAPE[ids[q]] === SH.STAIRS && ((meta[q] >> 2) & 1) === top;
    const dq = d => DIRX[d] * SX + DIRZ[d] * SZ;
    let shape = 0; // 0 straight, 1 outer_left, 2 outer_right, 3 inner_left, 4 inner_right
    const ccw = (f + 3) & 3;
    const front = p + dq(f);
    const canTake = d => { const q = p + dq(d); return !(SHAPE[ids[q]] === SH.STAIRS && (meta[q] & 7) === (m & 7)); };
    if (isSt(front)) {
      const f1 = meta[front] & 3;
      if ((f1 & 1) !== (f & 1) && canTake((f1 + 2) & 3)) shape = f1 === ccw ? 1 : 2;
    }
    if (shape === 0) {
      const back = p + dq((f + 2) & 3);
      if (isSt(back)) {
        const f2 = meta[back] & 3;
        if ((f2 & 1) !== (f & 1) && canTake(f2)) shape = f2 === ccw ? 3 : 4;
      }
    }
    const sy0 = top ? 8 : 0, sy1 = top ? 16 : 8, uy0 = top ? 0 : 8, uy1 = top ? 8 : 16;
    this.box(p, id, x, y, z, 0, sy0, 0, 16, sy1, 16, texs, 0, buf);
    // upper parts in north-facing frame (high part at north: z 0..8), left = west (x<8)
    const parts = [];
    if (shape === 0) parts.push([0, uy0, 0, 16, uy1, 8]);
    else if (shape === 1) parts.push([0, uy0, 0, 8, uy1, 8]);
    else if (shape === 2) parts.push([8, uy0, 0, 16, uy1, 8]);
    else if (shape === 3) { parts.push([0, uy0, 0, 16, uy1, 8]); parts.push([0, uy0, 8, 8, uy1, 16]); }
    else { parts.push([0, uy0, 0, 16, uy1, 8]); parts.push([8, uy0, 8, 16, uy1, 16]); }
    const turns = (f + 2) & 3;
    for (const b of parts) { const r = rotBox(b, turns); this.box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], texs, 0, buf); }
  }

  connects(q, kind) {
    const n = this.ids[q];
    if (OPAQUE[n]) return true;
    const s = SHAPE[n];
    if (kind === SH.FENCE) return s === SH.FENCE || s === SH.GATE;
    if (kind === SH.WALL) return s === SH.WALL || s === SH.GATE || s === SH.PANE;
    if (kind === SH.PANE) return s === SH.PANE || s === SH.WALL || (FLAGS[n] & (BF_GLASS | BF_TRANS)) !== 0 && s === SH.CUBE;
    return false;
  }
  fence(p, id, x, y, z, texs, buf) {
    this.box(p, id, x, y, z, 6, 0, 6, 10, 16, 10, texs, 0, buf);
    for (let d = 0; d < 4; d++) {
      if (!this.connects(p + DIRX[d] * SX + DIRZ[d] * SZ, SH.FENCE)) continue;
      const t = (d + 2) & 3; // rotate north-arm to direction d: north=2 -> turns (d-2)
      for (const b of [[7, 12, 0, 9, 15, 6], [7, 6, 0, 9, 9, 6]]) {
        const r = rotBox(b, (d + 2) & 3); void t;
        this.box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], texs, 0, buf);
      }
    }
  }
  wall(p, id, x, y, z, texs, buf) {
    const c = [];
    for (let d = 0; d < 4; d++) c.push(this.connects(p + DIRX[d] * SX + DIRZ[d] * SZ, SH.WALL));
    const above = this.ids[p + SY];
    const straight = ((c[0] && c[2] && !c[1] && !c[3]) || (c[1] && c[3] && !c[0] && !c[2])) && above === 0;
    if (!straight) this.box(p, id, x, y, z, 4, 0, 4, 12, 16, 12, texs, 0, buf);
    for (let d = 0; d < 4; d++) {
      if (!c[d]) continue;
      const r = rotBox([5, 0, 0, 11, 14, straight ? 8 : 4], (d + 2) & 3);
      this.box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], texs, 0, buf);
    }
  }
  pane(p, id, x, y, z, texs, buf) {
    let any = false;
    const ex = { cullSame: false };
    for (let d = 0; d < 4; d++) {
      if (!this.connects(p + DIRX[d] * SX + DIRZ[d] * SZ, SH.PANE)) continue;
      any = true;
      const r = rotBox([7, 0, 0, 9, 16, 7], (d + 2) & 3);
      this.box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], texs, 0, buf, ex);
    }
    if (!any) {
      this.box(p, id, x, y, z, 7, 0, 0, 9, 16, 16, texs, 0, buf, ex);
      this.box(p, id, x, y, z, 0, 0, 7, 16, 16, 9, texs, 0b110011, buf, ex);
    } else this.box(p, id, x, y, z, 7, 0, 7, 9, 16, 9, texs, 0, buf, ex);
  }
  gate(p, id, m, x, y, z, texs, buf) {
    const f = m & 3, open = (m >> 2) & 1;
    const parts = [[0, 5, 7, 2, 16, 9], [14, 5, 7, 16, 16, 9]];
    if (!open) parts.push([2, 6, 7, 14, 9, 9], [2, 12, 7, 14, 15, 9], [6, 9, 7, 10, 12, 9]);
    else parts.push([0, 6, 9, 2, 9, 15], [0, 12, 9, 2, 15, 15], [14, 6, 9, 16, 9, 15], [14, 12, 9, 16, 15, 15]);
    for (const b of parts) { const r = rotBox(b, (f + 2) & 3); this.box(p, id, x, y, z, r[0], r[1], r[2], r[3], r[4], r[5], texs, 0, buf); }
  }

  // ---------------------------------------------------------------- fluids
  fluidHeightAt(q, isW) {
    // height of one block's fluid (0..1), -1 for solid (ignored), 2 when fluid above (forces full)
    const ids = this.ids, meta = this.metaArr;
    const id = ids[q];
    const same = isW ? isWaterId(id) : id === B.LAVA;
    if (same) {
      const up = ids[q + SY];
      if (isW ? isWaterId(up) : up === B.LAVA) return 2;
      if (id !== B.WATER && id !== B.LAVA) return 8 / 9;
      const lv = meta[q] & 7;
      return (meta[q] & 8) ? 8 / 9 : (8 - lv) / 9;
    }
    if (SOLID[id] || OPAQUE[id]) return -1;
    return 0;
  }
  cornerHeight(p, dx, dz, isW) {
    // average over the 4 blocks around corner (dx,dz in {0,1})
    let sum = 0, w = 0;
    for (let k = 0; k < 4; k++) {
      const ox = (k & 1) ? dx : dx - 1, oz = (k & 2) ? dz : dz - 1;
      const h = this.fluidHeightAt(p + ox * SX + oz * SZ, isW);
      if (h === 2) return 1;
      if (h < 0) continue;
      if (h >= 0.8) { sum += h * 10; w += 10; } else { sum += h; w += 1; }
    }
    return w ? sum / w : 8 / 9;
  }
  fluid(ids, meta, light, p, id, x, y, z, forceSource) {
    this.ids = ids; this.light = light;
    const isW = id !== B.LAVA;
    const buf = this.bufs[isW ? RL_TRANS : RL_SOLID];
    const layer = FTEX[(isW ? B.WATER : B.LAVA) * 6 + 2];
    const up = ids[p + SY];
    const upSame = isW ? isWaterId(up) : up === B.LAVA;
    let h00, h10, h01, h11;
    if (upSame) h00 = h10 = h01 = h11 = 1;
    else {
      h00 = this.cornerHeight(p, 0, 0, isW); h10 = this.cornerHeight(p, 1, 0, isW);
      h01 = this.cornerHeight(p, 0, 1, isW); h11 = this.cornerHeight(p, 1, 1, isW);
    }
    const { tpx, tpy, tpz, tu, tv, sh, sk, bl } = this.t;
    const own = light[p];
    const X = x * 16, Y = y * 16, Z = z * 16;
    const lava = !isW;
    const setL = (q) => {
      let l = own;
      if (q !== undefined) { const nl = light[q]; if (!OPAQUE[ids[q]]) l = ((Math.max(nl >> 4, own >> 4)) << 4) | Math.max(nl & 15, own & 15); }
      const s = (l >> 4) * 16, b = lava ? 240 : (l & 15) * 16;
      for (let v = 0; v < 4; v++) { sk[v] = s; bl[v] = b; }
    };
    const shadeAll = (f) => { const s = Math.round(f * 255); for (let v = 0; v < 4; v++) sh[v] = s; };
    // top
    if (!upSame) {
      tpx[0] = X; tpy[0] = Y + h01 * 16; tpz[0] = Z + 16;
      tpx[1] = X + 16; tpy[1] = Y + h11 * 16; tpz[1] = Z + 16;
      tpx[2] = X + 16; tpy[2] = Y + h10 * 16; tpz[2] = Z;
      tpx[3] = X; tpy[3] = Y + h00 * 16; tpz[3] = Z;
      tu[0] = 0; tv[0] = 16; tu[1] = 16; tv[1] = 16; tu[2] = 16; tv[2] = 0; tu[3] = 0; tv[3] = 0;
      setL(p + SY); shadeAll(1);
      this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, false);
      if (isW) {
        // underside of the surface, seen from below water
        let t;
        t = tpx[1]; tpx[1] = tpx[3]; tpx[3] = t; t = tpy[1]; tpy[1] = tpy[3]; tpy[3] = t; t = tpz[1]; tpz[1] = tpz[3]; tpz[3] = t;
        t = tu[1]; tu[1] = tu[3]; tu[3] = t; t = tv[1]; tv[1] = tv[3]; tv[3] = t;
        shadeAll(0.8);
        this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, false);
      }
    }
    // bottom
    const dn = ids[p - SY];
    if (!(isW ? isWaterId(dn) : dn === B.LAVA) && !OPAQUE[dn]) {
      tpx[0] = X + 16; tpy[0] = Y; tpz[0] = Z + 16; tpx[1] = X; tpy[1] = Y; tpz[1] = Z + 16;
      tpx[2] = X; tpy[2] = Y; tpz[2] = Z; tpx[3] = X + 16; tpy[3] = Y; tpz[3] = Z;
      tu[0] = 16; tv[0] = 16; tu[1] = 0; tv[1] = 16; tu[2] = 0; tv[2] = 0; tu[3] = 16; tv[3] = 0;
      setL(p - SY); shadeAll(0.55);
      this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, false);
    }
    // sides
    const sideH = [[h11, h10], [h00, h01], null, null, [h01, h11], [h10, h00]];
    for (const f of [0, 1, 4, 5]) {
      const q = p + FACE_DELTA[f];
      const n = ids[q];
      if (OPAQUE[n]) continue;
      if (isW ? isWaterId(n) : n === B.LAVA) continue;
      const c = FC[f], hh = sideH[f];
      for (let v = 0; v < 4; v++) {
        const cc = c[v];
        tpx[v] = X + cc[0] * 16; tpz[v] = Z + cc[2] * 16;
        tpy[v] = Y + (cc[1] ? (v === 2 ? hh[1] : hh[0]) * 16 : 0);
      }
      // BL,BR,TR,TL ; top corners: v2 = TR (hh[1]) , v3 = TL (hh[0])
      tu[0] = 0; tv[0] = 16; tu[1] = 16; tv[1] = 16; tu[2] = 16; tv[2] = 16 - (tpy[2] - Y); tu[3] = 0; tv[3] = 16 - (tpy[3] - Y);
      setL(q); shadeAll(FACE_SHADE[f]);
      this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, false);
      if (isW) {
        let t;
        t = tpx[1]; tpx[1] = tpx[3]; tpx[3] = t; t = tpy[1]; tpy[1] = tpy[3]; tpy[3] = t; t = tpz[1]; tpz[1] = tpz[3]; tpz[3] = t;
        t = tu[1]; tu[1] = tu[3]; tu[3] = t; t = tv[1]; tv[1] = tv[3]; tv[3] = t;
        this.quad(buf, tpx, tpy, tpz, tu, tv, layer, sh, sk, bl, 0, false);
      }
    }
  }

  // ---------------------------------------------------------------- visibility graph
  visibility(ids) {
    const vis = this.vis, st = this.stack;
    vis.fill(0);
    const res = new Uint8Array(6);
    let openCount = 0;
    for (let i = 0; i < 4096; i++) {
      const x = i & 15, z = (i >> 4) & 15, y = i >> 8;
      if (OPAQUE[ids[pidx(x, y, z)]]) vis[i] = 1; else openCount++;
    }
    if (openCount === 0) return res;
    if (openCount === 4096) { res.fill(63); return res; }
    for (let s = 0; s < 4096; s++) {
      if (vis[s]) continue;
      let sp = 0, faces = 0;
      st[sp++] = s; vis[s] = 1;
      while (sp) {
        const i = st[--sp];
        const x = i & 15, z = (i >> 4) & 15, y = i >> 8;
        if (x === 15) faces |= 1; if (x === 0) faces |= 2; if (y === 15) faces |= 4; if (y === 0) faces |= 8; if (z === 15) faces |= 16; if (z === 0) faces |= 32;
        if (x < 15 && !vis[i + 1]) { vis[i + 1] = 1; st[sp++] = i + 1; }
        if (x > 0 && !vis[i - 1]) { vis[i - 1] = 1; st[sp++] = i - 1; }
        if (z < 15 && !vis[i + 16]) { vis[i + 16] = 1; st[sp++] = i + 16; }
        if (z > 0 && !vis[i - 16]) { vis[i - 16] = 1; st[sp++] = i - 16; }
        if (y < 15 && !vis[i + 256]) { vis[i + 256] = 1; st[sp++] = i + 256; }
        if (y > 0 && !vis[i - 256]) { vis[i - 256] = 1; st[sp++] = i - 256; }
      }
      for (let a = 0; a < 6; a++) if (faces & (1 << a)) res[a] |= faces;
    }
    return res;
  }
}

// rotate a box (1/16 units) by quarter turns around the block's vertical axis: north -> east -> south -> west
function rotBox(b, turns) {
  let x0 = b[0], z0 = b[2], x1 = b[3], z1 = b[5];
  for (let t = 0; t < turns; t++) {
    const nx0 = 16 - z1, nx1 = 16 - z0, nz0 = x0, nz1 = x1;
    x0 = nx0; x1 = nx1; z0 = nz0; z1 = nz1;
  }
  return [x0, b[1], z0, x1, b[4], z1];
}

let LAYER_BED_HEAD = {}, BED_LEG = 0;
function initMesherTextures(layers) {
  LAYER_BED_HEAD = {};
  for (let id = 1; id < NB; id++) {
    if (SHAPE[id] !== SH.BED) continue;
    const key = B_KEY[id];
    const color = key === 'BED' ? '' : '_' + key.slice(4).toLowerCase();
    const l = layers['bed_top' + color];
    if (l !== undefined) LAYER_BED_HEAD[id] = l;
  }
  BED_LEG = layers['oak_planks'] | 0;
}

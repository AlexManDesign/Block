'use strict';
// WebGL2 renderer.

const M4 = {
  persp(o, fovy, aspect, near, far) {
    const f = 1 / Math.tan(fovy / 2), nf = 1 / (near - far);
    o.fill(0); o[0] = f / aspect; o[5] = f; o[10] = (far + near) * nf; o[11] = -1; o[14] = 2 * far * near * nf; return o;
  },
  mul(o, a, b) {
    for (let i = 0; i < 4; i++) for (let j = 0; j < 4; j++) {
      o[j * 4 + i] = a[i] * b[j * 4] + a[4 + i] * b[j * 4 + 1] + a[8 + i] * b[j * 4 + 2] + a[12 + i] * b[j * 4 + 3];
    }
    return o;
  },
  // rotation-only view matrix from yaw/pitch (camera looks along -z at yaw=0)
  view(o, yaw, pitch) {
    const cy = Math.cos(yaw), sy = Math.sin(yaw), cp = Math.cos(pitch), sp = Math.sin(pitch);
    // forward f = (sin(yaw)cp, sp, -cos(yaw)cp)
    const fx = sy * cp, fy = sp, fz = -cy * cp;
    // right r = (cos(yaw), 0, sin(yaw)); up = r x f
    const rx = cy, ry = 0, rz = sy;
    const ux = ry * fz - rz * fy, uy = rz * fx - rx * fz, uz = rx * fy - ry * fx;
    o[0] = rx; o[4] = ry; o[8] = rz; o[12] = 0;
    o[1] = ux; o[5] = uy; o[9] = uz; o[13] = 0;
    o[2] = -fx; o[6] = -fy; o[10] = -fz; o[14] = 0;
    o[3] = 0; o[7] = 0; o[11] = 0; o[15] = 1;
    return o;
  },
  invert(o, m) {
    const a00 = m[0], a01 = m[1], a02 = m[2], a03 = m[3], a10 = m[4], a11 = m[5], a12 = m[6], a13 = m[7];
    const a20 = m[8], a21 = m[9], a22 = m[10], a23 = m[11], a30 = m[12], a31 = m[13], a32 = m[14], a33 = m[15];
    const b00 = a00 * a11 - a01 * a10, b01 = a00 * a12 - a02 * a10, b02 = a00 * a13 - a03 * a10, b03 = a01 * a12 - a02 * a11;
    const b04 = a01 * a13 - a03 * a11, b05 = a02 * a13 - a03 * a12, b06 = a20 * a31 - a21 * a30, b07 = a20 * a32 - a22 * a30;
    const b08 = a20 * a33 - a23 * a30, b09 = a21 * a32 - a22 * a31, b10 = a21 * a33 - a23 * a31, b11 = a22 * a33 - a23 * a32;
    let det = b00 * b11 - b01 * b10 + b02 * b09 + b03 * b08 - b04 * b07 + b05 * b06;
    if (!det) return null; det = 1 / det;
    o[0] = (a11 * b11 - a12 * b10 + a13 * b09) * det; o[1] = (a02 * b10 - a01 * b11 - a03 * b09) * det;
    o[2] = (a31 * b05 - a32 * b04 + a33 * b03) * det; o[3] = (a22 * b04 - a21 * b05 - a23 * b03) * det;
    o[4] = (a12 * b08 - a10 * b11 - a13 * b07) * det; o[5] = (a00 * b11 - a02 * b08 + a03 * b07) * det;
    o[6] = (a32 * b02 - a30 * b05 - a33 * b01) * det; o[7] = (a20 * b05 - a22 * b02 + a23 * b01) * det;
    o[8] = (a10 * b10 - a11 * b08 + a13 * b06) * det; o[9] = (a01 * b08 - a00 * b10 - a03 * b06) * det;
    o[10] = (a30 * b04 - a31 * b02 + a33 * b00) * det; o[11] = (a21 * b02 - a20 * b04 - a23 * b00) * det;
    o[12] = (a11 * b07 - a10 * b09 - a12 * b06) * det; o[13] = (a00 * b09 - a01 * b07 + a02 * b06) * det;
    o[14] = (a31 * b01 - a30 * b03 - a32 * b00) * det; o[15] = (a20 * b03 - a21 * b01 + a22 * b00) * det;
    return o;
  },
};

// A growable vertex buffer of floats kept from frame to frame (models, sprites): push takes one
// vertex of 10 floats, like an array's push, without making a new array every frame.
class VBuf {
  constructor() { this.a = new Float32Array(4096); this.n = 0; }
  get length() { return this.n; }
  push(x, y, z, u, v, l, r, g, b, al) {
    let a = this.a;
    const n = this.n;
    if (n + 10 > a.length) { const b2 = new Float32Array(a.length * 2); b2.set(a); this.a = a = b2; }
    a[n] = x; a[n + 1] = y; a[n + 2] = z; a[n + 3] = u; a[n + 4] = v; a[n + 5] = l; a[n + 6] = r; a[n + 7] = g; a[n + 8] = b; a[n + 9] = al;
    this.n = n + 10;
  }
  view() { return this.a.subarray(0, this.n); }
}

const TERRAIN_VS = `#version 300 es
precision highp float; precision highp int;
layout(location=0) in uvec3 aV;
layout(location=1) in vec3 aOrigin;   // section origin in world blocks, per section (divisor 1)
uniform mat4 uVP; uniform vec3 uCamI; uniform vec3 uCamF; uniform float uTime; uniform uint uTick;
uniform highp usampler2D uAnim; uniform sampler2D uLM; uniform vec2 uFog; uniform float uSway;
#ifdef SHIP
uniform mat3 uShipRot; uniform vec3 uShipRel;   // a vessel: its turn and its wheel cell's centre relative to the camera
#endif
out vec3 vUV; out vec3 vLit; out float vFog;
flat out vec3 vTint; flat out float vOvl;
void main(){
  uint w0 = aV.x, w1 = aV.y, w2 = aV.z;
  vec3 p = vec3(float(w0 & 1023u), float((w0 >> 10) & 1023u), float((w0 >> 20) & 1023u)) * 0.03125;
  // camera-relative: integer parts first, so the result is exact far from the world origin
#ifdef SHIP
  vec3 wp = uShipRot * (aOrigin + p - vec3(0.5, 0.0, 0.5)) + uShipRel;
#else
  vec3 wp = (aOrigin - uCamI) - uCamF + p;
#endif
  uint fl = w0 >> 30;
  if (fl != 0u && uSway > 0.0) {
    vec3 a = aOrigin + p;
    float t = uTime;
    if (fl == 1u) { wp.x += sin(t * 1.7 + a.x * 0.6 + a.z * 0.35) * 0.065 * uSway; wp.z += cos(t * 1.3 + a.z * 0.5 + a.x * 0.2) * 0.05 * uSway; }
    else if (fl == 2u) { wp.x += sin(t * 1.1 + a.x * 0.5 + a.y * 0.4 + a.z * 0.3) * 0.022 * uSway; wp.y += cos(t * 1.3 + a.z * 0.45 + a.x * 0.25) * 0.012 * uSway; }
  }
  gl_Position = uVP * vec4(wp, 1.0);
  uint layer = w1 & 2047u;
  uvec4 an = texelFetch(uAnim, ivec2(int(layer & 255u), int(layer >> 8)), 0);
  if (an.r > 1u) layer += (uTick / max(an.g, 1u)) % an.r;
  vUV = vec3(float((w1 >> 11) & 31u) * 0.0625, float((w1 >> 16) & 31u) * 0.0625, float(layer));
  // light colour per vertex like Minecraft's terrain shaders: lightmap(sky, block) * face shade / AO
  vec2 li = vec2(float(w2 & 255u), float((w2 >> 8) & 255u)) / 240.0;
  vLit = textureLod(uLM, vec2(li.y * 0.9375 + 0.03125, li.x * 0.9375 + 0.03125), 0.0).rgb * (float((w1 >> 21) & 255u) / 255.0);
  uint t = w2 >> 16;
  vTint = vec3(float(t >> 11), float((t >> 5) & 63u), float(t & 31u)) * vec3(1.0 / 31.0, 1.0 / 63.0, 1.0 / 31.0);
  vOvl = float((w1 >> 29) & 1u);
  float d = max(length(wp.xz), abs(wp.y) * 0.5);
  vFog = clamp((d - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0);
}`;
const TERRAIN_FS = `#version 300 es
precision mediump float; precision mediump sampler2DArray;
uniform sampler2DArray uTex; uniform vec3 uFogColor; uniform float uAlphaMul; uniform float uOverlay;
in vec3 vUV; in vec3 vLit; in float vFog;
flat in vec3 vTint; flat in float vOvl;
out vec4 o;
void main(){
  vec4 c = texture(uTex, vUV);
#ifdef CUTOUT
  if (c.a < 0.5) discard;
#endif
  // biome colour; the grass block side lays its tinted grass overlay over the dirt base
  if (vOvl > 0.5) { vec4 ov = texture(uTex, vec3(vUV.xy, uOverlay)); c.rgb = mix(c.rgb, ov.rgb * vTint, ov.a); }
  else c.rgb *= vTint;
  vec3 rgb = c.rgb * vLit;
  rgb = mix(rgb, uFogColor, vFog);
#ifdef TRANS
  o = vec4(rgb, c.a * uAlphaMul);
#else
  o = vec4(rgb, 1.0);
#endif
}`;

const SKY_VS = `#version 300 es
precision highp float;
const vec2 P[3] = vec2[3](vec2(-1.0,-1.0), vec2(3.0,-1.0), vec2(-1.0,3.0));
out vec2 vP;
void main(){ vP = P[gl_VertexID]; gl_Position = vec4(vP, 0.9999, 1.0); }`;
const SKY_FS = `#version 300 es
precision highp float;
uniform mat4 uInvVP; uniform vec3 uZenith; uniform vec3 uHorizon; uniform vec3 uSunDir; uniform vec4 uSunset;
uniform float uStars; uniform float uVoid;
in vec2 vP; out vec4 o;
float hash(vec3 p){ p = fract(p * 0.3183099 + 0.1); p *= 17.0; return fract(p.x * p.y * p.z * (p.x + p.y + p.z)); }
void main(){
  vec4 w = uInvVP * vec4(vP, 1.0, 1.0);
  vec3 d = normalize(w.xyz / w.w);
  float h = d.y;
  float t = pow(clamp(1.0 - max(h, 0.0), 0.0, 1.0), 5.0);
  vec3 col = mix(uZenith, uHorizon, t);
  if (h < 0.0) col = mix(uHorizon, uHorizon * uVoid, clamp(-h * 3.0, 0.0, 1.0));
  // sunrise / sunset glow
  float sd = max(dot(d, normalize(vec3(uSunDir.x, 0.0, uSunDir.z))), 0.0);
  float band = exp(-abs(h - 0.02) * 7.0) * pow(sd, 3.0);
  col = mix(col, uSunset.rgb, clamp(band * uSunset.a, 0.0, 1.0));
  // stars
  if (uStars > 0.0 && h > 0.0) {
    vec3 q = floor(d * 380.0);
    float s = hash(q);
    if (s > 0.9975) col += vec3(uStars * (s - 0.9975) * 400.0 * clamp(h * 4.0, 0.0, 1.0));
  }
  o = vec4(col, 1.0);
}`;

const SPRITE_VS = `#version 300 es
precision highp float;
layout(location=0) in vec3 aPos; layout(location=1) in vec2 aUV; layout(location=2) in vec4 aCol;
uniform mat4 uVP;
out vec2 vUV; out vec4 vCol;
void main(){ vUV = aUV; vCol = aCol; gl_Position = uVP * vec4(aPos, 1.0); }`;
const SPRITE_FS = `#version 300 es
precision mediump float;
uniform sampler2D uTex; uniform int uKeyBlack;
in vec2 vUV; in vec4 vCol; out vec4 o;
void main(){
  vec4 c = texture(uTex, vUV);
  if (uKeyBlack == 1) { float m = max(c.r, max(c.g, c.b)); c.a = min(c.a, m * 2.0); }
  o = c * vCol;
  if (o.a < 0.01) discard;
}`;

// generic textured-array geometry (particles, held item, cracks, entities with array tex)
const ARR_VS = `#version 300 es
precision highp float;
layout(location=0) in vec3 aPos; layout(location=1) in vec3 aUV; layout(location=2) in vec4 aCol;
uniform mat4 uVP; uniform vec2 uFog;
out vec3 vUV; out vec4 vCol; out float vFog;
void main(){ vUV = aUV; vCol = aCol; gl_Position = uVP * vec4(aPos, 1.0); vFog = clamp((max(length(aPos.xz), abs(aPos.y) * 0.5) - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0); }`;
const ARR_FS = `#version 300 es
precision mediump float; precision mediump sampler2DArray;
uniform sampler2DArray uTex; uniform vec3 uFogColor; uniform float uAlphaRef; uniform vec3 uMul;
in vec3 vUV; in vec4 vCol; in float vFog; out vec4 o;
void main(){
  vec4 c = texture(uTex, vUV) * vCol;
  c.rgb *= uMul;
  if (c.a < uAlphaRef) discard;
  o = vec4(mix(c.rgb, uFogColor, vFog), c.a);
}`;
// entity shader: 2D texture, per-vertex light color
const ENT_VS = ARR_VS.replace('in vec3 aUV', 'in vec3 aUV');
const ENT_FS = `#version 300 es
precision mediump float;
uniform sampler2D uTex; uniform vec3 uFogColor;
in vec3 vUV; in vec4 vCol; in float vFog; out vec4 o;
void main(){
  vec4 c = texture(uTex, vUV.xy);
  if (c.a < 0.1) discard;
  c.rgb *= vCol.rgb;
  o = vec4(mix(c.rgb, uFogColor, vFog), c.a);
}`;
const LINE_VS = `#version 300 es
precision highp float;
layout(location=0) in vec3 aPos;
uniform mat4 uVP;
void main(){ gl_Position = uVP * vec4(aPos, 1.0); }`;
const LINE_FS = `#version 300 es
precision mediump float;
uniform vec4 uColor; out vec4 o;
void main(){ o = uColor; }`;

class Renderer {
  constructor(canvas, assets) {
    this.canvas = canvas;
    const gl = canvas.getContext('webgl2', { antialias: false, alpha: false, depth: true, stencil: false,
      powerPreference: 'high-performance', preserveDrawingBuffer: false });
    if (!gl) throw new Error('WebGL2 is not supported');
    this.gl = gl;
    this.assets = assets;
    this.progSolid = this.program(TERRAIN_VS, TERRAIN_FS, '');
    this.progCutout = this.program(TERRAIN_VS, TERRAIN_FS, '#define CUTOUT\n');
    this.progShip = this.program(TERRAIN_VS, TERRAIN_FS, '#define SHIP\n#define CUTOUT\n');
    this.progTrans = this.program(TERRAIN_VS, TERRAIN_FS, '#define TRANS\n');
    this.progSky = this.program(SKY_VS, SKY_FS, '');
    this.progSprite = this.program(SPRITE_VS, SPRITE_FS, '');
    this.progArr = this.program(ARR_VS, ARR_FS, '');
    this.progEnt = this.program(ENT_VS, ENT_FS, '');
    this.progLine = this.program(LINE_VS, LINE_FS, '');
    this.uniforms = new Map();
    this.vp = new Float32Array(16); this.proj = new Float32Array(16); this.view = new Float32Array(16);
    this.invVP = new Float32Array(16);
    this.frustum = new Float32Array(24);
    this.emptyVao = gl.createVertexArray();
    this.initTextures();
    this.initQuadIndex(1 << 17);
    this.initMultiDraw();
    this.dyn = this.makeDyn(); // dynamic buffer for sprites/particles/etc
    this.lineBuf = gl.createBuffer(); this.lineVao = gl.createVertexArray();
    gl.bindVertexArray(this.lineVao); gl.bindBuffer(gl.ARRAY_BUFFER, this.lineBuf);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 12, 0);
    gl.bindVertexArray(null);
    this.lmData = new Uint8Array(16 * 16 * 4);
    this.lmTex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.lmTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA8, 16, 16, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    this.flicker = 0; this.flickerT = 0; this.flickerTarget = 0;
    this.visList = []; this.visCount = 0;
    this.frame = 0;
    this.stats = { draws: 0, quads: 0, sections: 0 };
    this.initProfiler();
    this.clouds = null;
    this.entTex = new Map();
  }

  program(vs, fs, defs) {
    const gl = this.gl;
    const mk = (type, src) => {
      const s = gl.createShader(type);
      const i = src.indexOf('\n') + 1;
      gl.shaderSource(s, src.slice(0, i) + defs + src.slice(i));
      gl.compileShader(s);
      if (!gl.getShaderParameter(s, gl.COMPILE_STATUS)) throw new Error(gl.getShaderInfoLog(s) + '\n' + src);
      return s;
    };
    const p = gl.createProgram();
    gl.attachShader(p, mk(gl.VERTEX_SHADER, vs)); gl.attachShader(p, mk(gl.FRAGMENT_SHADER, fs));
    gl.linkProgram(p);
    if (!gl.getProgramParameter(p, gl.LINK_STATUS)) throw new Error(gl.getProgramInfoLog(p));
    p.u = {};
    const n = gl.getProgramParameter(p, gl.ACTIVE_UNIFORMS);
    for (let i = 0; i < n; i++) { const a = gl.getActiveUniform(p, i); p.u[a.name] = gl.getUniformLocation(p, a.name); }
    return p;
  }

  initTextures() {
    const gl = this.gl, A = this.assets;
    const img = A.atlasImage;
    const cv = document.createElement('canvas'); cv.width = img.width; cv.height = img.height;
    const cx = cv.getContext('2d'); cx.drawImage(img, 0, 0);
    const px = cx.getImageData(0, 0, img.width, img.height).data;
    this.atlasPixels = px; this.atlasW = img.width;
    const N = A.count, cols = A.cols;
    this.layerCount = N;
    const levels = 5;
    const tex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D_ARRAY, tex);
    gl.texStorage3D(gl.TEXTURE_2D_ARRAY, levels, gl.RGBA8, 16, 16, N);
    let lvl = new Uint8Array(16 * 16 * 4 * N);
    for (let l = 0; l < N; l++) {
      const ox = (l % cols) * 16, oy = Math.floor(l / cols) * 16;
      for (let y = 0; y < 16; y++) {
        const src = ((oy + y) * img.width + ox) * 4;
        lvl.set(px.subarray(src, src + 64), (l * 256 + y * 16) * 4);
      }
    }
    gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, 0, 0, 0, 0, 16, 16, N, gl.RGBA, gl.UNSIGNED_BYTE, lvl);
    let size = 16;
    for (let m = 1; m < levels; m++) {
      const ns = size >> 1;
      const nl = new Uint8Array(ns * ns * 4 * N);
      for (let l = 0; l < N; l++) {
        const sb = l * size * size * 4, db = l * ns * ns * 4;
        for (let y = 0; y < ns; y++) for (let x = 0; x < ns; x++) {
          let r = 0, g = 0, b = 0, a = 0, wsum = 0;
          for (let k = 0; k < 4; k++) {
            const i = sb + (((y * 2 + (k >> 1)) * size) + x * 2 + (k & 1)) * 4;
            const al = lvl[i + 3];
            const w = al + 1;
            r += lvl[i] * w; g += lvl[i + 1] * w; b += lvl[i + 2] * w; a += al; wsum += w;
          }
          const o = db + (y * ns + x) * 4;
          nl[o] = r / wsum; nl[o + 1] = g / wsum; nl[o + 2] = b / wsum;
          // keep coverage for cut-out textures (alpha test at 0.5)
          const av = a / 4;
          nl[o + 3] = av > 0 && av < 255 ? Math.min(255, av * 1.25) : av;
        }
      }
      gl.texSubImage3D(gl.TEXTURE_2D_ARRAY, m, 0, 0, 0, ns, ns, N, gl.RGBA, gl.UNSIGNED_BYTE, nl);
      lvl = nl; size = ns;
    }
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MIN_FILTER, gl.NEAREST_MIPMAP_LINEAR);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_S, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_WRAP_T, gl.REPEAT);
    gl.texParameteri(gl.TEXTURE_2D_ARRAY, gl.TEXTURE_MAX_LEVEL, levels - 1);
    this.texArr = tex;
    // animation table
    const rows = Math.ceil(N / 256);
    const an = new Uint8Array(256 * rows * 2);
    for (let l = 0; l < N; l++) { an[l * 2] = 1; an[l * 2 + 1] = 1; }
    for (const k in A.anim) { const [first, frames, ticks] = A.anim[k]; an[first * 2] = frames; an[first * 2 + 1] = ticks; }
    this.animTex = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, this.animTex);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RG8UI, 256, rows, 0, gl.RG_INTEGER, gl.UNSIGNED_BYTE, an);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
    this.sunTex = this.tex2D(A.sunImage, gl.NEAREST);
    this.moonTex = this.tex2D(A.moonImage, gl.NEAREST);
  }
  tex2D(img, filter) {
    const gl = this.gl, t = gl.createTexture();
    gl.bindTexture(gl.TEXTURE_2D, t);
    gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, filter);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE);
    gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
    return t;
  }
  entityTexture(name) {
    let t = this.entTex.get(name);
    if (t === undefined) {
      let img = this.assets.entityImages[name];
      const eyes = this.assets.entityImages[name + '_eyes'];
      if (img && eyes) {
        // spider / enderman: eyes drawn over the skin, as in the original
        const c = document.createElement('canvas'); c.width = img.width; c.height = img.height;
        const x = c.getContext('2d'); x.drawImage(img, 0, 0); x.drawImage(eyes, 0, 0); img = c;
      }
      t = img ? this.tex2D(img, this.gl.NEAREST) : null;
      this.entTex.set(name, t);
    }
    return t;
  }

  // ---------------------------------------------------------------- profiling (F3)
  // GPU time per render phase with EXT_disjoint_timer_query_webgl2: one TIME_ELAPSED query per
  // phase (queries cannot nest), results read back a few frames later, smoothed per phase.
  initProfiler() {
    const gl = this.gl;
    this.tq = gl.getExtension('EXT_disjoint_timer_query_webgl2');
    this.gpuOn = false; this.qFree = []; this.qPend = []; this.qCur = null; this.gpuMs = {}; this.gpuFrameMs = 0;
    const dbg = gl.getExtension('WEBGL_debug_renderer_info');
    this.gpuName = dbg ? gl.getParameter(dbg.UNMASKED_RENDERER_WEBGL) : gl.getParameter(gl.RENDERER);
    this.resetPassStats();
  }
  resetPassStats() {
    const P = () => ({ sec: 0, draws: 0, quads: 0, skipped: 0 });
    this.pstat = [P(), P(), P()];
    this.stats.fogCulled = 0;
  }
  gpuBegin(name) {
    if (!this.gpuOn || !this.tq) return;
    if (this.qCur) this.gpuEnd();
    const gl = this.gl, q = this.qFree.pop() || gl.createQuery();
    gl.beginQuery(this.tq.TIME_ELAPSED_EXT, q);
    this.qCur = { q, name, frame: this.frame };
  }
  gpuEnd() {
    if (!this.qCur) return;
    this.gl.endQuery(this.tq.TIME_ELAPSED_EXT);
    this.qPend.push(this.qCur); this.qCur = null;
  }
  gpuPoll() {
    if (!this.tq) return;
    const gl = this.gl, disjoint = gl.getParameter(this.tq.GPU_DISJOINT_EXT);
    while (this.qPend.length) {
      const e = this.qPend[0];
      if (!gl.getQueryParameter(e.q, gl.QUERY_RESULT_AVAILABLE)) break;
      const ns = gl.getQueryParameter(e.q, gl.QUERY_RESULT);
      this.qPend.shift(); this.qFree.push(e.q);
      if (disjoint) continue;
      const ms = ns / 1e6, M = this.gpuMs;
      // drivers occasionally return garbage for a query: keep only plausible values
      if (!(ms >= 0 && ms < 1000)) { this.gpuBad = (this.gpuBad || 0) + 1; continue; }
      M[e.name] = M[e.name] === undefined ? ms : M[e.name] * 0.9 + ms * 0.1;
      if (this.gpuAcc) { const A = this.gpuAcc[e.name] || (this.gpuAcc[e.name] = { s: 0, n: 0 }); A.s += ms; A.n++; }
    }
    if (this.qPend.length > 64) { for (const e of this.qPend) this.qFree.push(e.q); this.qPend.length = 0; }
  }
  meshMemory(world) {
    let bytes = 0, n = 0;
    for (const c of world.cols.values()) if (c.meshes) for (const m of c.meshes) if (m && m.vbo) { bytes += m.cap + (m.tcap || 0); n++; }
    return { bytes, n };
  }

  initMultiDraw() {
    this.multiDraw = this.gl.getExtension('WEBGL_multi_draw');
    this.rangeCnt = new Int32Array(8); this.rangeOff = new Int32Array(8);
    this.dirCull = true;
  }
  initQuadIndex(maxQuads) {
    const gl = this.gl;
    const idx = new Uint32Array(maxQuads * 6);
    for (let q = 0, i = 0; q < maxQuads; q++) {
      const v = q * 4;
      idx[i++] = v; idx[i++] = v + 1; idx[i++] = v + 2; idx[i++] = v; idx[i++] = v + 2; idx[i++] = v + 3;
    }
    this.quadIbo = gl.createBuffer();
    gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.quadIbo);
    gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, idx, gl.STATIC_DRAW);
    this.maxQuads = maxQuads;
  }

  // ---------------------------------------------------------------- section meshes
  // Every section's origin lives in one buffer, read by the section's VAOs as a per-instance
  // attribute: drawing a section changes no uniform (ANGLE on D3D11 rewrites a constant buffer for
  // every draw whose uniforms changed, which with thousands of sections starved the GPU).
  originSlot(c, sy) {
    const gl = this.gl;
    if (!this.originBuf) {
      this.originBuf = gl.createBuffer(); this.originCap = 1 << 16; this.originFree = []; this.originTop = 0;
      gl.bindBuffer(gl.ARRAY_BUFFER, this.originBuf);
      gl.bufferData(gl.ARRAY_BUFFER, this.originCap * 12, gl.DYNAMIC_DRAW);
    }
    const slot = this.originFree.length ? this.originFree.pop() : this.originTop++;
    if (slot >= this.originCap) throw new Error('section origin slots exhausted');
    gl.bindBuffer(gl.ARRAY_BUFFER, this.originBuf);
    gl.bufferSubData(gl.ARRAY_BUFFER, slot * 12, new Float32Array([c.cx * 16, WORLD_MIN_Y + sy * 16, c.cz * 16]));
    return slot;
  }
  bindOrigin(slot) {
    const gl = this.gl;
    gl.bindBuffer(gl.ARRAY_BUFFER, this.originBuf);
    gl.enableVertexAttribArray(1);
    gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 12, slot * 12);
    gl.vertexAttribDivisor(1, 1);
  }
  freeSectionMesh(m) {
    const gl = this.gl;
    if (m.vbo) { gl.deleteBuffer(m.vbo); gl.deleteVertexArray(m.vao); m.vbo = m.vao = null; }
    this.freeTrans(m);
    if (m.slot !== undefined) { this.originFree.push(m.slot); m.slot = undefined; }
  }
  // Every section mesh carries the same fields from the start, so all of them share one hidden
  // class: the visibility walk and the draw loops read these fields on thousands of meshes a frame
  newMesh(vbo, vao, vis) {
    return { vbo, vao, counts: null, vis, cap: 0, slot: undefined, gc: null, cx: 0, cz: 0, sy: 0,
      tvao: null, tibo: null, tcap: 0, tc: null, tg: null, tfirst: 0, tn: 0, tkey: -1 };
  }
  uploadSection(c, sy, d) {
    const gl = this.gl;
    this.upN = (this.upN || 0) + 1; this.upBytes = (this.upBytes || 0) + d.data.byteLength;
    let m = c.meshes[sy];
    const total = d.counts[0] + d.counts[1] + d.counts[2];
    if (!total) {
      if (m) this.freeSectionMesh(m);
      const e = this.newMesh(null, null, d.vis); e.counts = [0, 0, 0];
      c.meshes[sy] = e;
      return;
    }
    if (!m || !m.vbo) {
      m = this.newMesh(gl.createBuffer(), gl.createVertexArray(), null);
      m.slot = this.originSlot(c, sy);
      gl.bindVertexArray(m.vao);
      this.bindOrigin(m.slot);
      gl.bindBuffer(gl.ARRAY_BUFFER, m.vbo);
      gl.enableVertexAttribArray(0);
      gl.vertexAttribIPointer(0, 3, gl.UNSIGNED_INT, 12, 0);
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, this.quadIbo);
      gl.bindVertexArray(null);
      c.meshes[sy] = m;
    }
    gl.bindBuffer(gl.ARRAY_BUFFER, m.vbo);
    if (d.data.byteLength > m.cap || d.data.byteLength < m.cap / 3) {
      gl.bufferData(gl.ARRAY_BUFFER, d.data, gl.STATIC_DRAW);
      m.cap = d.data.byteLength;
    } else gl.bufferSubData(gl.ARRAY_BUFFER, 0, d.data);
    m.counts = d.counts; m.gc = d.gc || null; m.vis = d.vis;
    this.prepareTrans(m, d);
  }
  // Translucent quads are drawn back to front through a per-section index buffer that is re-sorted
  // when the camera moves to another block (like Minecraft's translucency sorting). Here: quad
  // centres (section-local, in blocks) and facing groups for the sort.
  prepareTrans(m, d) {
    const gl = this.gl, n = d.counts[2];
    if (!n) { this.freeTrans(m); return; }
    const first = d.counts[0] + d.counts[1], w = d.data;
    const tc = new Float32Array(n * 3), tg = new Uint8Array(n);
    for (let q = 0; q < n; q++) {
      let sx = 0, sy = 0, sz = 0;
      for (let v = 0, o = (first + q) * 12; v < 4; v++, o += 3) {
        const w0 = w[o];
        sx += w0 & 1023; sy += (w0 >>> 10) & 1023; sz += (w0 >>> 20) & 1023;
      }
      tc[q * 3] = sx * 0.0078125; tc[q * 3 + 1] = sy * 0.0078125; tc[q * 3 + 2] = sz * 0.0078125;
    }
    if (d.gc) { let q = 0; for (let g = 0; g < 7; g++) for (let k = d.gc[14 + g]; k > 0; k--) tg[q++] = g; }
    else tg.fill(6);
    if (!m.tvao) {
      m.tvao = gl.createVertexArray(); m.tibo = gl.createBuffer();
      gl.bindVertexArray(m.tvao);
      this.bindOrigin(m.slot);
      gl.bindBuffer(gl.ARRAY_BUFFER, m.vbo);
      gl.enableVertexAttribArray(0);
      gl.vertexAttribIPointer(0, 3, gl.UNSIGNED_INT, 12, 0);
      gl.bindBuffer(gl.ELEMENT_ARRAY_BUFFER, m.tibo);
      gl.bindVertexArray(null);
      m.tcap = 0;
    }
    m.tc = tc; m.tg = tg; m.tfirst = first; m.tn = 0; m.tkey = -1;
  }
  freeTrans(m) {
    if (!m.tvao) return;
    this.gl.deleteVertexArray(m.tvao); this.gl.deleteBuffer(m.tibo);
    m.tvao = m.tibo = null; m.tc = m.tg = null; m.tn = 0;
  }
  // camera at (px,py,pz) in section-local blocks; vis: facing groups that can face the camera
  sortTrans(m, px, py, pz, vis) {
    const gl = this.gl, n = m.tg.length, tc = m.tc, tg = m.tg;
    const S = this.tsort || (this.tsort = { key: new Float64Array(4096), e: new Uint32Array(4096 * 6) });
    if (S.key.length < n) { S.key = new Float64Array(n * 2); S.e = new Uint32Array(n * 12); }
    const key = S.key, el = S.e;
    // key = squared distance (1/256 steps) * 65536 + quad: a native numeric sort, no comparator
    let k = 0;
    for (let q = 0; q < n; q++) {
      if (!(vis & (1 << tg[q]))) continue;
      const dx = tc[q * 3] - px, dy = tc[q * 3 + 1] - py, dz = tc[q * 3 + 2] - pz;
      key[k++] = Math.floor((dx * dx + dy * dy + dz * dz) * 256) * 65536 + q;
    }
    const o = key.subarray(0, k);
    o.sort();
    for (let j = k - 1, e = 0; j >= 0; j--) {
      const v = (m.tfirst + o[j] % 65536) * 4;
      el[e++] = v; el[e++] = v + 1; el[e++] = v + 2; el[e++] = v; el[e++] = v + 2; el[e++] = v + 3;
    }
    gl.bindVertexArray(m.tvao);
    const bytes = k * 24;
    if (bytes > m.tcap) { gl.bufferData(gl.ELEMENT_ARRAY_BUFFER, Math.max(bytes, 1536), gl.DYNAMIC_DRAW); m.tcap = Math.max(bytes, 1536); }
    if (k) gl.bufferSubData(gl.ELEMENT_ARRAY_BUFFER, 0, el, 0, k * 6);
    gl.bindVertexArray(null);
    m.tn = k * 6;
  }
  freeColumn(c) {
    const gl = this.gl;
    for (let s = 0; s < SECTIONS; s++) {
      const m = c.meshes[s];
      if (m) this.freeSectionMesh(m);
      c.meshes[s] = null;
    }
  }

  // ---------------------------------------------------------------- per-frame
  updateLightmap(sky, gamma, nightVision) {
    // sky: {day (0..1), sunset(0..1)}; block light warm with flicker
    const now = performance.now();
    if (now - this.flickerT > 90) { this.flickerT = now; this.flickerTarget = (Math.random() - 0.5) * 0.2; }
    this.flicker += (this.flickerTarget - this.flicker) * 0.25;
    const d = this.lmData;
    const day = sky.day;
    const skyB = 0.08 + 0.92 * day;
    // moonlight is blue-ish, daylight neutral, sunset warm
    let sr = 0.55 + 0.45 * day, sg = 0.6 + 0.4 * day, sb = 0.85 + 0.15 * day;
    if (sky.sunset > 0) { sr += (1.1 - sr) * sky.sunset * 0.45; sg += (0.85 - sg) * sky.sunset * 0.45; sb += (0.62 - sb) * sky.sunset * 0.45; }
    const fl = 1 + this.flicker;
    for (let s = 0; s < 16; s++) {
      const sl = s / 15;
      const sv = (sl / (4 - 3 * sl)) * skyB;
      for (let b = 0; b < 16; b++) {
        const bl = Math.min(1, (b / 15) * fl);
        const bv = bl / (4 - 3 * bl);
        let r = sv * sr + bv * 1.0, g = sv * sg + bv * (0.78 + bv * 0.2), bb = sv * sb + bv * (0.55 + bv * 0.3);
        // ambient floor (caves are dark but not pitch black)
        r = 0.045 + r * 0.955; g = 0.045 + g * 0.955; bb = 0.05 + bb * 0.95;
        if (nightVision) { r = Math.max(r, 0.8); g = Math.max(g, 0.8); bb = Math.max(bb, 0.8); }
        r = Math.min(1, r); g = Math.min(1, g); bb = Math.min(1, bb);
        // brightness (gamma) setting like MC
        r = r + ((1 - Math.pow(1 - r, 4)) - r) * gamma; g = g + ((1 - Math.pow(1 - g, 4)) - g) * gamma; bb = bb + ((1 - Math.pow(1 - bb, 4)) - bb) * gamma;
        const o = (s * 16 + b) * 4;
        d[o] = r * 255; d[o + 1] = g * 255; d[o + 2] = bb * 255; d[o + 3] = 255;
      }
    }
    const gl = this.gl;
    gl.bindTexture(gl.TEXTURE_2D, this.lmTex);
    gl.texSubImage2D(gl.TEXTURE_2D, 0, 0, 0, 16, 16, gl.RGBA, gl.UNSIGNED_BYTE, d);
  }

  setCamera(fovDeg, yaw, pitch, far) {
    const gl = this.gl, cv = this.canvas;
    M4.persp(this.proj, fovDeg * Math.PI / 180, cv.width / cv.height, 0.05, far);
    M4.view(this.view, yaw, pitch);
    const b = this.bob;
    if (b && (b[0] || b[1] || b[2] || b[3])) {
      // view bobbing in view space: translate, then roll (z) and nod (x)
      const cz = Math.cos(b[2]), sz = Math.sin(b[2]), cx = Math.cos(b[3]), sx = Math.sin(b[3]), B = this.bobM || (this.bobM = new Float32Array(16));
      B[0] = cz; B[1] = sz; B[2] = 0; B[3] = 0;
      B[4] = -sz * cx; B[5] = cz * cx; B[6] = sx; B[7] = 0;
      B[8] = sz * sx; B[9] = -cz * sx; B[10] = cx; B[11] = 0;
      B[12] = b[0]; B[13] = b[1]; B[14] = 0; B[15] = 1;
      const t = this.tmpM || (this.tmpM = new Float32Array(16));
      M4.mul(t, B, this.view); this.view.set(t);
    }
    M4.mul(this.vp, this.proj, this.view);
    M4.invert(this.invVP, this.vp);
    // frustum planes from vp (camera relative)
    const m = this.vp, f = this.frustum;
    const planes = [[3, 0, 1], [3, 0, -1], [3, 1, 1], [3, 1, -1], [3, 2, 1], [3, 2, -1]];
    for (let i = 0; i < 6; i++) {
      const [w, r, s] = planes[i];
      const a = m[w] + s * m[r], b = m[4 + w] + s * m[4 + r], c = m[8 + w] + s * m[8 + r], d = m[12 + w] + s * m[12 + r];
      const l = Math.hypot(a, b, c);
      f[i * 4] = a / l; f[i * 4 + 1] = b / l; f[i * 4 + 2] = c / l; f[i * 4 + 3] = d / l;
    }
    void gl;
  }
  boxVisible(x0, y0, z0, x1, y1, z1) {
    const f = this.frustum;
    for (let i = 0; i < 24; i += 4) {
      const a = f[i], b = f[i + 1], c = f[i + 2], d = f[i + 3];
      if (a * (a > 0 ? x1 : x0) + b * (b > 0 ? y1 : y0) + c * (c > 0 ? z1 : z0) + d < 0) return false;
    }
    return true;
  }

  // occlusion-culled BFS over sections from the camera section
  collectVisible(world, cam, renderDist) {
    this.frame++;
    const fr = this.frame;
    const list = this.visList;
    let n = 0;
    const ccx = Math.floor(cam[0]) >> 4, ccz = Math.floor(cam[2]) >> 4;
    let csy = (Math.floor(cam[1]) - WORLD_MIN_Y) >> 4;
    if (csy < 0) csy = 0; if (csy >= SECTIONS) csy = SECTIONS - 1;
    const Q = this.bfsQ || (this.bfsQ = new Int32Array(1 << 18));
    let qh = 0, qt = 0;
    const startCol = world.col(ccx, ccz);
    if (!startCol) { this.visCount = 0; return; }
    startCol.visFrame[csy] = fr;
    Q[qt++] = ccx; Q[qt++] = ccz; Q[qt++] = csy; Q[qt++] = -1; Q[qt++] = 0;
    const R2 = (renderDist + 0.5) * (renderDist + 0.5);
    const noCull = this.noOcclusion;
    // fog occlusion (as in Sodium), only where it changes no pixel: see fullyFogged()
    const fc = this.fogCull, fogEnd = fc ? fc.end : 0, fogY = fc ? fc.y : null;
    while (qh < qt) {
      const cx = Q[qh], cz = Q[qh + 1], sy = Q[qh + 2], from = Q[qh + 3], dirs = Q[qh + 4];
      qh += 5;
      const c = world.col(cx, cz);
      const m = c.meshes[sy];
      if (m && m.vbo) {
        if (fogY !== null && this.fullyFogged(cx, sy, cz, cam, fogEnd, fogY)) this.stats.fogCulled++;
        else list[n++] = m, m.cx = cx, m.cz = cz, m.sy = sy;
      }
      const vis = m && m.vis;
      for (let d = 0; d < 6; d++) {
        if (dirs & (1 << OPP6[d])) continue;
        if (from >= 0 && vis && !noCull && !(vis[from] & (1 << d))) continue;
        const nx = cx + DX6[d], ny = sy + DY6[d], nz = cz + DZ6[d];
        if (ny < 0 || ny >= SECTIONS) continue;
        const ddx = nx - ccx, ddz = nz - ccz;
        if (ddx * ddx + ddz * ddz > R2) continue;
        const nc = world.col(nx, nz);
        if (!nc || nc.state < 2) continue;
        if (nc.visFrame[ny] === fr) continue;
        // frustum test in camera-relative coords
        const bx = nx * 16 - cam[0], by = WORLD_MIN_Y + ny * 16 - cam[1], bz = nz * 16 - cam[2];
        if (!this.boxVisible(bx, by, bz, bx + 16, by + 16, bz + 16)) continue;
        nc.visFrame[ny] = fr;
        if (qt + 5 > Q.length) break;
        Q[qt++] = nx; Q[qt++] = nz; Q[qt++] = ny; Q[qt++] = OPP6[d]; Q[qt++] = dirs | (1 << d);
      }
    }
    this.visCount = n;
  }

  // A section is skipped when every point of it is in full fog (the terrain fog distance is
  // max(horizontal distance, |dy| / 2)) and it lies wholly below the eye: every ray to it points
  // down, where the sky shader draws exactly the fog colour (no sunset glow, see renderWorld), so
  // the pixel is the same with or without it.
  fullyFogged(cx, sy, cz, cam, fogEnd, eyeY) {
    const y0 = WORLD_MIN_Y + sy * 16;
    if (y0 + 17 > eyeY) return false;
    // seen from above the clouds, terrain reaching into the cloud layer would hide cloud behind it
    if (eyeY > 191 && y0 + 16 > 191) return false;
    const x0 = cx * 16, z0 = cz * 16;
    const dx = cam[0] < x0 ? x0 - cam[0] : cam[0] > x0 + 16 ? cam[0] - x0 - 16 : 0;
    const dz = cam[2] < z0 ? z0 - cam[2] : cam[2] > z0 + 16 ? cam[2] - z0 - 16 : 0;
    const dy = eyeY - (y0 + 17);
    return Math.max(Math.hypot(dx, dz), dy * 0.5) >= fogEnd + 1;
  }

  // ---------------------------------------------------------------- terrain passes
  bindTerrain(p, cam, env, time, tick) {
    const gl = this.gl;
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uVP, false, this.vp);
    const ix = Math.floor(cam[0]), iy = Math.floor(cam[1]), iz = Math.floor(cam[2]);
    gl.uniform3f(p.u.uCamI, ix, iy, iz);
    gl.uniform3f(p.u.uCamF, cam[0] - ix, cam[1] - iy, cam[2] - iz);
    gl.uniform1f(p.u.uTime, time);
    gl.uniform1ui(p.u.uTick, tick >>> 0);
    gl.uniform2f(p.u.uFog, env.fogStart, env.fogEnd);
    gl.uniform3fv(p.u.uFogColor, env.fogColor);
    gl.uniform1f(p.u.uSway, env.sway);
    if (p.u.uAlphaMul) gl.uniform1f(p.u.uAlphaMul, 1);
    if (p.u.uOverlay) gl.uniform1f(p.u.uOverlay, this.overlayLayer);
    gl.uniform1i(p.u.uTex, 0); gl.uniform1i(p.u.uLM, 1); gl.uniform1i(p.u.uAnim, 2);
  }
  // Draws one pass of every visible section. Quads are grouped by facing (-X,+X,-Y,+Y,-Z,+Z,other);
  // groups that face away from the camera for the whole section are skipped (directional culling),
  // the rest go out in one multi-draw per section.
  drawPass(pass, cam) {
    const gl = this.gl, list = this.visList, n = this.visCount, md = this.multiDraw;
    const p = pass === 0 ? this.progSolid : pass === 1 ? this.progCutout : this.progTrans;
    const RC = this.rangeCnt, RO = this.rangeOff;
    let draws = 0, quads = 0;
    const back = pass === 2;
    for (let k = 0; k < n; k++) {
      const m = list[back ? n - 1 - k : k];
      const cnt = m.counts[pass];
      if (!cnt) continue;
      const ps = this.pstat[pass];
      ps.sec++;
      if (back && m.tvao && m.tkey !== -1) {
        ps.skipped += cnt - m.tn / 6;
        if (!m.tn) continue;
        gl.bindVertexArray(m.tvao);
        gl.drawElements(gl.TRIANGLES, m.tn, gl.UNSIGNED_INT, 0);
        draws++; quads += m.tn / 6;
        continue;
      }
      const first = pass === 0 ? 0 : pass === 1 ? m.counts[0] : m.counts[0] + m.counts[1];
      const ox = m.cx * 16 - cam[0], oy = WORLD_MIN_Y + m.sy * 16 - cam[1], oz = m.cz * 16 - cam[2];
      gl.bindVertexArray(m.vao);
      const gc = m.gc;
      if (!gc || !this.dirCull) {
        gl.drawElements(gl.TRIANGLES, cnt * 6, gl.UNSIGNED_INT, first * 24);
        draws++; quads += cnt;
        continue;
      }
      // camera below / above / beside the section box on each axis (camera sits at the origin)
      const vis = (ox > 0 ? 1 : ox + 16 < 0 ? 2 : 3) | (oy > 0 ? 4 : oy + 16 < 0 ? 8 : 12) | (oz > 0 ? 16 : oz + 16 < 0 ? 32 : 48) | 64;
      let r = 0, at = first, open = false;
      const g0 = pass * 7, q0 = quads;
      for (let g = 0; g < 7; g++) {
        const c = gc[g0 + g];
        if (!c) continue;
        if (vis & (1 << g)) {
          if (open && RO[r - 1] + RC[r - 1] * 4 === at * 24) RC[r - 1] += c * 6;
          else { RC[r] = c * 6; RO[r] = at * 24; r++; open = true; }
          quads += c;
        }
        at += c;
      }
      ps.skipped += cnt - (quads - q0);
      if (!r) continue;
      if (md) { md.multiDrawElementsWEBGL(gl.TRIANGLES, RC, 0, gl.UNSIGNED_INT, RO, 0, r); draws++; }
      else for (let q = 0; q < r; q++) { gl.drawElements(gl.TRIANGLES, RC[q], gl.UNSIGNED_INT, RO[q]); draws++; }
    }
    this.stats.draws += draws; this.stats.quads += quads;
    this.pstat[pass].draws += draws; this.pstat[pass].quads += quads;
  }

  // A vessel (ships.js): its section meshes drawn turned and moved as one, alpha-tested (opaque
  // and cut-out quads alike; its few translucent quads go in the same pass)
  drawShip(S, cam, env, time, tick, bob) {
    const gl = this.gl, p = this.progShip;
    this.bindTerrain(p, cam, env, time, tick);
    gl.uniform1f(p.u.uSway, 0);
    const c = Math.cos(S.yaw), s = Math.sin(S.yaw);
    gl.uniformMatrix3fv(p.u.uShipRot, false, [c, 0, s, 0, 1, 0, -s, 0, c]);
    gl.uniform3f(p.u.uShipRel, S.pos[0] + 0.5 - cam[0], S.pos[1] + bob - cam[1], S.pos[2] + 0.5 - cam[2]);
    gl.enable(gl.DEPTH_TEST); gl.depthMask(true); gl.disable(gl.BLEND); gl.enable(gl.CULL_FACE);
    for (const m of S.meshes) {
      const n = m.counts[0] + m.counts[1] + m.counts[2];
      gl.bindVertexArray(m.vao);
      gl.vertexAttrib3f(1, m.origin[0], m.origin[1], m.origin[2]);
      gl.drawElements(gl.TRIANGLES, n * 6, gl.UNSIGNED_INT, 0);
    }
    gl.bindVertexArray(null);
  }
  renderWorld(world, cam, yaw, pitch, env, time, tick) {
    const gl = this.gl, cv = this.canvas;
    gl.viewport(0, 0, cv.width, cv.height);
    this.stats.draws = 0; this.stats.quads = 0;
    this.resetPassStats();
    this.gpuBegin('solid');
    this.setCamera(env.fov, yaw, pitch, env.far);
    gl.depthMask(true);
    gl.clearColor(env.fogColor[0], env.fogColor[1], env.fogColor[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D_ARRAY, this.texArr);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, this.lmTex);
    gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, this.animTex);
    gl.activeTexture(gl.TEXTURE0);
    // fog occlusion only when the sky below the horizon is plain fog colour
    this.fogCull = !env.underwater && env.sunset < 0.001 && !this.noFogCull ? { end: env.fogEnd, y: cam[1] } : null;
    let tc = performance.now();
    this.collectVisible(world, cam, env.renderDist);
    this.cpuVis = performance.now() - tc;
    this.stats.sections = this.visCount;
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LEQUAL);
    gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK);
    gl.disable(gl.BLEND);
    tc = performance.now();
    this.bindTerrain(this.progSolid, cam, env, time, tick);
    this.drawPass(0, cam);
    this.gpuBegin('cutout');
    this.bindTerrain(this.progCutout, cam, env, time, tick);
    this.drawPass(1, cam);
    this.cpuTerrain = performance.now() - tc;
    // sky after the opaque terrain: it only shades pixels the terrain left empty
    this.gpuBegin('sky');
    if (!env.underwater) this.drawSky(env);
  }
  // Re-sorts translucent quads of sections whose camera block changed, nearest sections first,
  // within a time budget; the rest keep their previous order until a later frame.
  sortTranslucent(cam) {
    const list = this.visList, n = this.visCount, t0 = performance.now();
    const csx = Math.floor(cam[0]) >> 4, csy = (Math.floor(cam[1]) - WORLD_MIN_Y) >> 4, csz = Math.floor(cam[2]) >> 4;
    for (let k = 0; k < n; k++) {
      const m = list[k];
      if (!m.tvao) continue;
      const px = cam[0] - m.cx * 16, py = cam[1] - WORLD_MIN_Y - m.sy * 16, pz = cam[2] - m.cz * 16;
      const vis = !this.dirCull ? 127 : (px < 0 ? 1 : px > 16 ? 2 : 3) | (py < 0 ? 4 : py > 16 ? 8 : 12) | (pz < 0 ? 16 : pz > 16 ? 32 : 48) | 64;
      // re-sort once the camera moved a step: one block near the section, coarser further away
      // (there the order of neighbouring quads barely depends on small camera moves)
      const sd = Math.max(Math.abs(m.cx - csx), Math.abs(m.sy - csy), Math.abs(m.cz - csz));
      const st = sd <= 1 ? 1 : sd <= 3 ? 2 : sd <= 7 ? 4 : 8;
      const key = (((Math.floor(px / st) + 512) * 1024 + Math.floor(py / st) + 512) * 1024 + Math.floor(pz / st) + 512) * 128 + vis;
      if (key === m.tkey) continue;
      if (m.tkey !== -1 && performance.now() - t0 > 1.5) continue;
      this.sortTrans(m, px, py, pz, vis);
      m.tkey = key;
    }
  }
  renderTranslucent(cam, env, time, tick) {
    const gl = this.gl;
    let tc = performance.now();
    this.sortTranslucent(cam);
    this.cpuSort = performance.now() - tc;
    this.gpuBegin('water');
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(false);
    // a water face lying in the plane of a neighbour's face (a snow layer, ice, leaves beside it)
    // stays behind it instead of fighting over the pixels in stripes; Minecraft insets these
    // faces by 0.001 of a block, below what the packed vertices can hold
    gl.enable(gl.POLYGON_OFFSET_FILL); gl.polygonOffset(1, 2);
    this.bindTerrain(this.progTrans, cam, env, time, tick);
    this.drawPass(2, cam);
    gl.disable(gl.POLYGON_OFFSET_FILL);
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.bindVertexArray(null);
  }

  // ---------------------------------------------------------------- sky, sun, moon
  // Drawn after opaque terrain. depthRange(1,1) puts every sky, sun and moon fragment on the far
  // plane, so with LEQUAL they pass only where the depth buffer still holds the clear value.
  drawSky(env) {
    const gl = this.gl, p = this.progSky;
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LEQUAL); gl.depthMask(false); gl.disable(gl.CULL_FACE);
    gl.depthRange(1, 1);
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uInvVP, false, this.invVP);
    gl.uniform3fv(p.u.uZenith, env.zenith);
    gl.uniform3fv(p.u.uHorizon, env.fogColor);
    gl.uniform3fv(p.u.uSunDir, env.sunDir);
    gl.uniform4f(p.u.uSunset, env.sunsetColor[0], env.sunsetColor[1], env.sunsetColor[2], env.sunset);
    gl.uniform1f(p.u.uStars, env.stars);
    gl.uniform1f(p.u.uVoid, 1.0);
    gl.bindVertexArray(this.emptyVao);
    gl.drawArrays(gl.TRIANGLES, 0, 3);
    // sun & moon as billboards at distance 100 along their directions
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE);
    const s = env.sunDir;
    this.drawCelestial(this.sunTex, s, 16, [0, 0, 1, 1], 1);
    const ph = env.moonPhase;
    const u0 = (ph % 4) / 4, v0 = Math.floor(ph / 4) / 2;
    this.drawCelestial(this.moonTex, [-s[0], -s[1], -s[2]], 11, [u0, v0, u0 + 0.25, v0 + 0.5], 1);
    gl.disable(gl.BLEND);
    gl.depthRange(0, 1);
    gl.depthMask(true); gl.enable(gl.CULL_FACE);
  }
  drawCelestial(tex, dir, size, uv, alpha) {
    const gl = this.gl, p = this.progSprite;
    const D = 100;
    const cx = dir[0] * D, cy = dir[1] * D, cz = dir[2] * D;
    // basis perpendicular to dir: axis a = z (sun path is in x-y plane), b = dir x a
    let ax = 0, ay = 0, az = 1;
    let bx = dir[1] * az - dir[2] * ay, by = dir[2] * ax - dir[0] * az, bz = dir[0] * ay - dir[1] * ax;
    const bl = Math.hypot(bx, by, bz) || 1; bx /= bl; by /= bl; bz /= bl;
    // re-orthogonalise a
    ax = by * dir[2] - bz * dir[1]; ay = bz * dir[0] - bx * dir[2]; az = bx * dir[1] - by * dir[0];
    const h = size / 2;
    const v = [];
    const corner = (sa, sb, u, w) => v.push(cx + (ax * sa + bx * sb) * h, cy + (ay * sa + by * sb) * h, cz + (az * sa + bz * sb) * h, u, w, 1, 1, 1, alpha);
    corner(-1, -1, uv[0], uv[3]); corner(1, -1, uv[2], uv[3]); corner(1, 1, uv[2], uv[1]);
    corner(-1, -1, uv[0], uv[3]); corner(1, 1, uv[2], uv[1]); corner(-1, 1, uv[0], uv[1]);
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uVP, false, this.vp);
    gl.uniform1i(p.u.uKeyBlack, 1);
    gl.uniform1i(p.u.uTex, 3);
    gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, tex); gl.activeTexture(gl.TEXTURE0);
    this.drawDynSprite(this.f32(v), 6);
  }

  makeDyn() {
    const gl = this.gl;
    const d = { vbo: gl.createBuffer(), vao: gl.createVertexArray(), vao3: gl.createVertexArray(), cap: 0, off: 0 };
    gl.bindVertexArray(d.vao);
    gl.bindBuffer(gl.ARRAY_BUFFER, d.vbo);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 36, 0);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 2, gl.FLOAT, false, 36, 12);
    gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 4, gl.FLOAT, false, 36, 20);
    gl.bindVertexArray(d.vao3);
    gl.bindBuffer(gl.ARRAY_BUFFER, d.vbo);
    gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 40, 0);
    gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 40, 12);
    gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 4, gl.FLOAT, false, 40, 24);
    gl.bindVertexArray(null);
    return d;
  }
  // Copies a plain array into a reused scratch Float32Array. The view is valid until the next call,
  // so it is meant for data that is uploaded right away.
  scratch(n) {
    let b = this.f32buf;
    if (!b || b.length < n) b = this.f32buf = new Float32Array(Math.max(n, b ? b.length * 2 : 16384));
    return b;
  }
  f32(v) { const b = this.scratch(v.length); b.set(v); return b.subarray(0, v.length); }
  // Appends the vertices behind the ones already written (a ring): every draw of the frame keeps its
  // own range, so an upload never overwrites data a pending draw still reads, which would make the
  // GPU wait or copy. When full, the buffer is re-specified (the driver hands out fresh storage).
  // Returns the first vertex for the given stride; offsets are kept a multiple of both strides.
  uploadDyn(arr, stride) {
    const gl = this.gl, d = this.dyn, n = arr.byteLength;
    gl.bindBuffer(gl.ARRAY_BUFFER, d.vbo);
    let off = Math.ceil(d.off / 360) * 360;
    if (off + n > d.cap) {
      d.cap = Math.max(d.cap, n * 4, 1 << 20);
      gl.bufferData(gl.ARRAY_BUFFER, d.cap, gl.DYNAMIC_DRAW);
      off = 0;
    }
    gl.bufferSubData(gl.ARRAY_BUFFER, off, arr);
    d.off = off + n;
    return off / stride;
  }
  drawDynSprite(arr, count) {
    const gl = this.gl;
    const first = this.uploadDyn(arr, 36);
    gl.bindVertexArray(this.dyn.vao);
    gl.drawArrays(gl.TRIANGLES, first, count);
    gl.bindVertexArray(null);
  }
  // draw triangles with the array-texture program. arr: [x,y,z,u,v,layer,r,g,b,a]*n (camera relative)
  drawArr(arr, count, env, alphaRef, vp) {
    const gl = this.gl, p = this.progArr;
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uVP, false, vp || this.vp);
    gl.uniform2f(p.u.uFog, env ? env.fogStart : 1e9, env ? env.fogEnd : 1e9 + 1);
    gl.uniform3fv(p.u.uFogColor, env ? env.fogColor : [0, 0, 0]);
    gl.uniform1f(p.u.uAlphaRef, alphaRef);
    gl.uniform3f(p.u.uMul, 1, 1, 1);
    gl.uniform1i(p.u.uTex, 0);
    const first = this.uploadDyn(arr, 40);
    gl.bindVertexArray(this.dyn.vao3);
    gl.drawArrays(gl.TRIANGLES, first, count);
    gl.bindVertexArray(null);
  }
  drawEnt(arr, count, tex, env) {
    const gl = this.gl, p = this.progEnt;
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uVP, false, this.vp);
    gl.uniform2f(p.u.uFog, env.fogStart, env.fogEnd);
    gl.uniform3fv(p.u.uFogColor, env.fogColor);
    gl.uniform1i(p.u.uTex, 3);
    gl.activeTexture(gl.TEXTURE3); gl.bindTexture(gl.TEXTURE_2D, tex); gl.activeTexture(gl.TEXTURE0);
    const first = this.uploadDyn(arr, 40);
    gl.bindVertexArray(this.dyn.vao3);
    gl.drawArrays(gl.TRIANGLES, first, count);
    gl.bindVertexArray(null);
  }

  // ---------------------------------------------------------------- clouds (MC-like 12x12x4 cells at y=192)
  buildClouds(seed) {
    const N = 256, g = new Uint8Array(N * N);
    const rnd = (x, z) => { let h = Math.imul(x, 374761393) ^ Math.imul(z, 668265263) ^ seed; h = Math.imul(h ^ (h >>> 13), 1274126177); return ((h ^ (h >>> 16)) >>> 0) / 4294967296; };
    const val = (x, z, s) => {
      const x0 = Math.floor(x / s), z0 = Math.floor(z / s), fx = x / s - x0, fz = z / s - z0;
      const a = rnd(x0 & (N / s - 1), z0 & (N / s - 1)), b = rnd((x0 + 1) & (N / s - 1), z0 & (N / s - 1));
      const c = rnd(x0 & (N / s - 1), (z0 + 1) & (N / s - 1)), d = rnd((x0 + 1) & (N / s - 1), (z0 + 1) & (N / s - 1));
      const sx = fx * fx * (3 - 2 * fx), sz = fz * fz * (3 - 2 * fz);
      return a + (b - a) * sx + (c - a) * sz + (a - b - c + d) * sx * sz;
    };
    for (let z = 0; z < N; z++) for (let x = 0; x < N; x++) {
      const v = val(x, z, 16) * 0.6 + val(x, z, 8) * 0.3 + val(x, z, 4) * 0.1;
      g[z * N + x] = v > 0.56 ? 1 : 0;
    }
    this.cloudGrid = g; this.cloudN = N;
  }
  drawClouds(cam, env, time) {
    if (!this.cloudGrid) return;
    const gl = this.gl, N = this.cloudN, g = this.cloudGrid;
    const S = 12, H = 4, Y = 192.33;
    const drift = time * 0.6;
    const R = Math.min(40, Math.ceil(env.far / S) + 2);
    const ox = cam[0] + drift, oz = cam[2];
    // the mesh covers 8 cells more than is visible around an anchor that moves in 8-cell steps, so
    // it is rebuilt only every ~100 blocks of travel; the colour is a uniform, not baked in
    const M = 8, RR = R + M;
    const gx0 = Math.round(Math.floor(ox / S) / M) * M, gz0 = Math.round(Math.floor(oz / S) / M) * M;
    const col = env.cloudColor;
    const cm = this.cloudMesh || (this.cloudMesh = { vbo: gl.createBuffer(), vao: gl.createVertexArray(), n: 0, key: '', buf: null });
    const key = gx0 + ',' + gz0 + ',' + RR;
    if (cm.key !== key) {
      cm.key = key;
      const at = (x, z) => g[((z % N + N) % N) * N + ((x % N + N) % N)];
      // count first, then write straight into one Float32Array
      let quads = 0;
      for (let dz = -RR; dz <= RR; dz++) for (let dx = -RR; dx <= RR; dx++) {
        if (dx * dx + dz * dz > RR * RR || !at(gx0 + dx, gz0 + dz)) continue;
        const cx = gx0 + dx, cz = gz0 + dz;
        quads += 2 + !at(cx + 1, cz) + !at(cx - 1, cz) + !at(cx, cz + 1) + !at(cx, cz - 1);
      }
      const need = quads * 60;
      if (!cm.buf || cm.buf.length < need) cm.buf = new Float32Array(need);
      const out = cm.buf, L = this.whiteLayer;
      let o = 0;
      const put = (x, y, z, sh) => { out[o++] = x; out[o++] = y; out[o++] = z; out[o++] = 0.5; out[o++] = 0.5; out[o++] = L; out[o++] = sh; out[o++] = sh; out[o++] = sh; out[o++] = 0.8; };
      const quad = (ax, ay, az, bx, by, bz, qx, qy, qz, dx, dy, dz, sh) => {
        put(ax, ay, az, sh); put(bx, by, bz, sh); put(qx, qy, qz, sh); put(ax, ay, az, sh); put(qx, qy, qz, sh); put(dx, dy, dz, sh);
      };
      for (let dz = -RR; dz <= RR; dz++) for (let dx = -RR; dx <= RR; dx++) {
        if (dx * dx + dz * dz > RR * RR) continue;
        const cx = gx0 + dx, cz = gz0 + dz;
        if (!at(cx, cz)) continue;
        const x0 = dx * S, x1 = x0 + S, z0 = dz * S, z1 = z0 + S, y0 = 0, y1 = H;
        quad(x0, y1, z1, x1, y1, z1, x1, y1, z0, x0, y1, z0, 1.0);
        quad(x0, y0, z0, x1, y0, z0, x1, y0, z1, x0, y0, z1, 0.7);
        if (!at(cx + 1, cz)) quad(x1, y0, z1, x1, y0, z0, x1, y1, z0, x1, y1, z1, 0.9);
        if (!at(cx - 1, cz)) quad(x0, y0, z0, x0, y0, z1, x0, y1, z1, x0, y1, z0, 0.9);
        if (!at(cx, cz + 1)) quad(x0, y0, z1, x1, y0, z1, x1, y1, z1, x0, y1, z1, 0.8);
        if (!at(cx, cz - 1)) quad(x1, y0, z0, x0, y0, z0, x0, y1, z0, x1, y1, z0, 0.8);
      }
      cm.n = o / 10;
      gl.bindVertexArray(cm.vao);
      gl.bindBuffer(gl.ARRAY_BUFFER, cm.vbo);
      gl.bufferData(gl.ARRAY_BUFFER, out.subarray(0, o), gl.STATIC_DRAW);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 3, gl.FLOAT, false, 40, 0);
      gl.enableVertexAttribArray(1); gl.vertexAttribPointer(1, 3, gl.FLOAT, false, 40, 12);
      gl.enableVertexAttribArray(2); gl.vertexAttribPointer(2, 4, gl.FLOAT, false, 40, 24);
      gl.bindVertexArray(null);
    }
    if (!cm.n) return;
    // translate grid-space mesh to camera-relative space
    const tx = gx0 * S - drift - cam[0], ty = Y - cam[1], tz = gz0 * S - cam[2];
    const T = this.cloudT || (this.cloudT = new Float32Array(16));
    T.fill(0); T[0] = T[5] = T[10] = T[15] = 1; T[12] = tx; T[13] = ty; T[14] = tz;
    const vp = this.cloudVP || (this.cloudVP = new Float32Array(16));
    M4.mul(vp, this.vp, T);
    const p = this.progArr;
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uVP, false, vp);
    // fog distance is measured in mesh space; approximate by far distances
    gl.uniform2f(p.u.uFog, env.far * 0.9 + 400, env.far * 2 + 600);
    gl.uniform3fv(p.u.uFogColor, env.fogColor);
    gl.uniform1f(p.u.uAlphaRef, 0);
    gl.uniform3f(p.u.uMul, col[0], col[1], col[2]);
    gl.uniform1i(p.u.uTex, 0);
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.enable(gl.CULL_FACE);
    gl.bindVertexArray(cm.vao);
    gl.colorMask(false, false, false, false);
    gl.drawArrays(gl.TRIANGLES, 0, cm.n);
    gl.colorMask(true, true, true, true);
    gl.depthFunc(gl.EQUAL); gl.depthMask(false);
    gl.drawArrays(gl.TRIANGLES, 0, cm.n);
    gl.depthFunc(gl.LEQUAL); gl.depthMask(true);
    gl.bindVertexArray(null);
    gl.disable(gl.BLEND);
  }

  drawLines(pts, color) {
    const gl = this.gl, p = this.progLine;
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uVP, false, this.vp);
    gl.uniform4fv(p.u.uColor, color);
    gl.bindBuffer(gl.ARRAY_BUFFER, this.lineBuf);
    gl.bufferData(gl.ARRAY_BUFFER, pts, gl.DYNAMIC_DRAW);
    gl.bindVertexArray(this.lineVao);
    gl.drawArrays(gl.LINES, 0, pts.length / 3);
    gl.bindVertexArray(null);
  }
}

const DX6 = [1, -1, 0, 0, 0, 0], DY6 = [0, 0, 1, -1, 0, 0], DZ6 = [0, 0, 0, 0, 1, -1], OPP6 = [1, 0, 3, 2, 5, 4];

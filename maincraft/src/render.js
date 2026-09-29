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

const TERRAIN_VS = `#version 300 es
precision highp float; precision highp int;
layout(location=0) in uvec3 aV;
uniform mat4 uVP; uniform vec3 uOrigin; uniform vec3 uCam; uniform float uTime; uniform uint uTick;
uniform highp usampler2D uAnim; uniform vec2 uFog; uniform float uSway;
out vec3 vUV; out float vShade; out vec2 vLight; out float vFog;
void main(){
  uint w0 = aV.x, w1 = aV.y, w2 = aV.z;
  vec3 p = vec3(float(w0 & 1023u), float((w0 >> 10) & 1023u), float((w0 >> 20) & 1023u)) * 0.03125;
  vec3 wp = uOrigin + p;
  uint fl = w0 >> 30;
  if (fl != 0u && uSway > 0.0) {
    vec3 a = wp + uCam;
    float t = uTime;
    if (fl == 1u) { wp.x += sin(t * 1.7 + a.x * 0.6 + a.z * 0.35) * 0.065 * uSway; wp.z += cos(t * 1.3 + a.z * 0.5 + a.x * 0.2) * 0.05 * uSway; }
    else if (fl == 2u) { wp.x += sin(t * 1.1 + a.x * 0.5 + a.y * 0.4 + a.z * 0.3) * 0.022 * uSway; wp.y += cos(t * 1.3 + a.z * 0.45 + a.x * 0.25) * 0.012 * uSway; }
  }
  gl_Position = uVP * vec4(wp, 1.0);
  uint layer = w1 & 2047u;
  uvec4 an = texelFetch(uAnim, ivec2(int(layer & 255u), int(layer >> 8)), 0);
  if (an.r > 1u) layer += (uTick / max(an.g, 1u)) % an.r;
  vUV = vec3(float((w1 >> 11) & 31u) * 0.0625, float((w1 >> 16) & 31u) * 0.0625, float(layer));
  vShade = float((w1 >> 21) & 255u) / 255.0;
  vLight = vec2(float(w2 & 255u), float((w2 >> 8) & 255u)) / 240.0;
  float d = length(wp);
  vFog = clamp((d - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0);
}`;
const TERRAIN_FS = `#version 300 es
precision mediump float; precision mediump sampler2DArray;
uniform sampler2DArray uTex; uniform sampler2D uLM; uniform vec3 uFogColor; uniform float uAlphaMul;
in vec3 vUV; in float vShade; in vec2 vLight; in float vFog;
out vec4 o;
void main(){
  vec4 c = texture(uTex, vUV);
#ifdef CUTOUT
  if (c.a < 0.5) discard;
#endif
  vec3 lm = texture(uLM, vec2(vLight.y * 0.9375 + 0.03125, vLight.x * 0.9375 + 0.03125)).rgb;
  vec3 rgb = c.rgb * vShade * lm;
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
void main(){ vUV = aUV; vCol = aCol; gl_Position = uVP * vec4(aPos, 1.0); vFog = clamp((length(aPos) - uFog.x) / (uFog.y - uFog.x), 0.0, 1.0); }`;
const ARR_FS = `#version 300 es
precision mediump float; precision mediump sampler2DArray;
uniform sampler2DArray uTex; uniform vec3 uFogColor; uniform float uAlphaRef;
in vec3 vUV; in vec4 vCol; in float vFog; out vec4 o;
void main(){
  vec4 c = texture(uTex, vUV) * vCol;
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
  o = vec4(mix(c.rgb, uFogColor, vFog), 1.0);
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
      powerPreference: 'high-performance', preserveDrawingBuffer: false, desynchronized: true });
    if (!gl) throw new Error('WebGL2 is not supported');
    this.gl = gl;
    this.assets = assets;
    this.progSolid = this.program(TERRAIN_VS, TERRAIN_FS, '');
    this.progCutout = this.program(TERRAIN_VS, TERRAIN_FS, '#define CUTOUT\n');
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
      const img = this.assets.entityImages[name];
      t = img ? this.tex2D(img, this.gl.NEAREST) : null;
      this.entTex.set(name, t);
    }
    return t;
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
  uploadSection(c, sy, d) {
    const gl = this.gl;
    let m = c.meshes[sy];
    const total = d.counts[0] + d.counts[1] + d.counts[2];
    if (!total) {
      if (m) { if (m.vbo) { gl.deleteBuffer(m.vbo); gl.deleteVertexArray(m.vao); } }
      c.meshes[sy] = { vbo: null, vao: null, counts: [0, 0, 0], vis: d.vis, cap: 0 };
      return;
    }
    if (!m || !m.vbo) {
      m = { vbo: gl.createBuffer(), vao: gl.createVertexArray(), counts: null, vis: null, cap: 0 };
      gl.bindVertexArray(m.vao);
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
    m.counts = d.counts; m.vis = d.vis;
  }
  freeColumn(c) {
    const gl = this.gl;
    for (let s = 0; s < SECTIONS; s++) {
      const m = c.meshes[s];
      if (m && m.vbo) { gl.deleteBuffer(m.vbo); gl.deleteVertexArray(m.vao); }
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
    if (!startCol.visFrame) startCol.visFrame = new Uint32Array(SECTIONS);
    startCol.visFrame[csy] = fr;
    Q[qt++] = ccx; Q[qt++] = ccz; Q[qt++] = csy; Q[qt++] = -1; Q[qt++] = 0;
    const R2 = (renderDist + 0.5) * (renderDist + 0.5);
    const noCull = this.noOcclusion;
    while (qh < qt) {
      const cx = Q[qh], cz = Q[qh + 1], sy = Q[qh + 2], from = Q[qh + 3], dirs = Q[qh + 4];
      qh += 5;
      const c = world.col(cx, cz);
      const m = c.meshes[sy];
      if (m && m.vbo) list[n++] = m, m.cx = cx, m.cz = cz, m.sy = sy;
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
        if (!nc.visFrame) nc.visFrame = new Uint32Array(SECTIONS);
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

  // ---------------------------------------------------------------- terrain passes
  bindTerrain(p, cam, env, time, tick) {
    const gl = this.gl;
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uVP, false, this.vp);
    gl.uniform3f(p.u.uCam, cam[0], cam[1], cam[2]);
    gl.uniform1f(p.u.uTime, time);
    gl.uniform1ui(p.u.uTick, tick >>> 0);
    gl.uniform2f(p.u.uFog, env.fogStart, env.fogEnd);
    gl.uniform3fv(p.u.uFogColor, env.fogColor);
    gl.uniform1f(p.u.uSway, env.sway);
    if (p.u.uAlphaMul) gl.uniform1f(p.u.uAlphaMul, 1);
    gl.uniform1i(p.u.uTex, 0); gl.uniform1i(p.u.uLM, 1); gl.uniform1i(p.u.uAnim, 2);
  }
  drawPass(pass, cam) {
    const gl = this.gl, list = this.visList, n = this.visCount;
    const p = pass === 0 ? this.progSolid : pass === 1 ? this.progCutout : this.progTrans;
    const u = p.u.uOrigin;
    let draws = 0, quads = 0;
    const back = pass === 2;
    for (let k = 0; k < n; k++) {
      const m = list[back ? n - 1 - k : k];
      const cnt = m.counts[pass];
      if (!cnt) continue;
      const first = pass === 0 ? 0 : pass === 1 ? m.counts[0] : m.counts[0] + m.counts[1];
      gl.uniform3f(u, m.cx * 16 - cam[0], WORLD_MIN_Y + m.sy * 16 - cam[1], m.cz * 16 - cam[2]);
      gl.bindVertexArray(m.vao);
      gl.drawElements(gl.TRIANGLES, cnt * 6, gl.UNSIGNED_INT, first * 24);
      draws++; quads += cnt;
    }
    this.stats.draws += draws; this.stats.quads += quads;
  }

  renderWorld(world, cam, yaw, pitch, env, time, tick) {
    const gl = this.gl, cv = this.canvas;
    gl.viewport(0, 0, cv.width, cv.height);
    this.stats.draws = 0; this.stats.quads = 0;
    this.setCamera(env.fov, yaw, pitch, env.far);
    gl.depthMask(true);
    gl.clearColor(env.fogColor[0], env.fogColor[1], env.fogColor[2], 1);
    gl.clear(gl.COLOR_BUFFER_BIT | gl.DEPTH_BUFFER_BIT);
    gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D_ARRAY, this.texArr);
    gl.activeTexture(gl.TEXTURE1); gl.bindTexture(gl.TEXTURE_2D, this.lmTex);
    gl.activeTexture(gl.TEXTURE2); gl.bindTexture(gl.TEXTURE_2D, this.animTex);
    gl.activeTexture(gl.TEXTURE0);
    // sky
    if (!env.underwater) this.drawSky(env);
    this.collectVisible(world, cam, env.renderDist);
    this.stats.sections = this.visCount;
    gl.enable(gl.DEPTH_TEST); gl.depthFunc(gl.LEQUAL);
    gl.enable(gl.CULL_FACE); gl.cullFace(gl.BACK);
    gl.disable(gl.BLEND);
    this.bindTerrain(this.progSolid, cam, env, time, tick);
    this.drawPass(0, cam);
    this.bindTerrain(this.progCutout, cam, env, time, tick);
    this.drawPass(1, cam);
  }
  renderTranslucent(cam, env, time, tick) {
    const gl = this.gl;
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.depthMask(false);
    this.bindTerrain(this.progTrans, cam, env, time, tick);
    this.drawPass(2, cam);
    gl.depthMask(true);
    gl.disable(gl.BLEND);
    gl.bindVertexArray(null);
  }

  // ---------------------------------------------------------------- sky, sun, moon
  drawSky(env) {
    const gl = this.gl, p = this.progSky;
    gl.disable(gl.DEPTH_TEST); gl.depthMask(false); gl.disable(gl.CULL_FACE);
    gl.useProgram(p);
    gl.uniformMatrix4fv(p.u.uInvVP, false, this.invVP);
    gl.uniform3fv(p.u.uZenith, env.zenith);
    gl.uniform3fv(p.u.uHorizon, env.fogColor);
    gl.uniform3fv(p.u.uSunDir, env.sunDir);
    gl.uniform4f(p.u.uSunset, env.sunsetColor[0], env.sunsetColor[1], env.sunsetColor[2], env.sunset);
    gl.uniform1f(p.u.uStars, env.stars);
    gl.uniform1f(p.u.uVoid, 0.25);
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
    gl.depthMask(true); gl.enable(gl.DEPTH_TEST);
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
    this.drawDynSprite(new Float32Array(v), 6);
  }

  makeDyn() {
    const gl = this.gl;
    const d = { vbo: gl.createBuffer(), vao: gl.createVertexArray(), vao3: gl.createVertexArray(), cap: 0 };
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
  uploadDyn(arr) {
    const gl = this.gl, d = this.dyn;
    gl.bindBuffer(gl.ARRAY_BUFFER, d.vbo);
    if (arr.byteLength > d.cap) { d.cap = Math.max(arr.byteLength, d.cap * 2, 65536); gl.bufferData(gl.ARRAY_BUFFER, d.cap, gl.DYNAMIC_DRAW); }
    gl.bufferSubData(gl.ARRAY_BUFFER, 0, arr);
  }
  drawDynSprite(arr, count) {
    const gl = this.gl;
    this.uploadDyn(arr);
    gl.bindVertexArray(this.dyn.vao);
    gl.drawArrays(gl.TRIANGLES, 0, count);
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
    gl.uniform1i(p.u.uTex, 0);
    this.uploadDyn(arr);
    gl.bindVertexArray(this.dyn.vao3);
    gl.drawArrays(gl.TRIANGLES, 0, count);
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
    this.uploadDyn(arr);
    gl.bindVertexArray(this.dyn.vao3);
    gl.drawArrays(gl.TRIANGLES, 0, count);
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
    const R = Math.min(32, Math.ceil(env.far / S) + 2);
    const ox = cam[0] + drift, oz = cam[2];
    const gx0 = Math.floor(ox / S), gz0 = Math.floor(oz / S);
    const verts = this.cloudVerts || (this.cloudVerts = new Float32Array(200000 * 10));
    let n = 0;
    const cy0 = Y - cam[1], cy1 = Y + H - cam[1];
    const col = env.cloudColor;
    const put = (x, y, z, sh) => {
      if (n + 10 > verts.length) return;
      verts[n++] = x; verts[n++] = y; verts[n++] = z; verts[n++] = 0.5; verts[n++] = 0.5; verts[n++] = this.whiteLayer;
      verts[n++] = col[0] * sh; verts[n++] = col[1] * sh; verts[n++] = col[2] * sh; verts[n++] = 0.8;
    };
    const quad = (a, b, c, d, sh) => { put(...a, sh); put(...b, sh); put(...c, sh); put(...a, sh); put(...c, sh); put(...d, sh); };
    const at = (x, z) => g[((z % N + N) % N) * N + ((x % N + N) % N)];
    for (let dz = -R; dz <= R; dz++) for (let dx = -R; dx <= R; dx++) {
      if (dx * dx + dz * dz > R * R) continue;
      const cx = gx0 + dx, cz = gz0 + dz;
      if (!at(cx, cz)) continue;
      const x0 = cx * S - drift - cam[0], x1 = x0 + S, z0 = cz * S - cam[2], z1 = z0 + S;
      if (cy1 > 0 || true) quad([x0, cy1, z1], [x1, cy1, z1], [x1, cy1, z0], [x0, cy1, z0], 1.0);
      quad([x0, cy0, z0], [x1, cy0, z0], [x1, cy0, z1], [x0, cy0, z1], 0.7);
      if (!at(cx + 1, cz)) quad([x1, cy0, z1], [x1, cy0, z0], [x1, cy1, z0], [x1, cy1, z1], 0.9);
      if (!at(cx - 1, cz)) quad([x0, cy0, z0], [x0, cy0, z1], [x0, cy1, z1], [x0, cy1, z0], 0.9);
      if (!at(cx, cz + 1)) quad([x0, cy0, z1], [x1, cy0, z1], [x1, cy1, z1], [x0, cy1, z1], 0.8);
      if (!at(cx, cz - 1)) quad([x1, cy0, z0], [x0, cy0, z0], [x0, cy1, z0], [x1, cy1, z0], 0.8);
    }
    if (!n) return;
    const cenv = { fogStart: env.far * 0.7, fogEnd: env.far * 1.6, fogColor: env.fogColor };
    gl.enable(gl.BLEND); gl.blendFunc(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA);
    gl.enable(gl.CULL_FACE);
    // depth prepass so overlapping faces do not double-blend
    gl.colorMask(false, false, false, false);
    this.drawArr(verts.subarray(0, n), n / 10, cenv, 0, null);
    gl.colorMask(true, true, true, true);
    gl.depthFunc(gl.EQUAL); gl.depthMask(false);
    this.drawArr(verts.subarray(0, n), n / 10, cenv, 0, null);
    gl.depthFunc(gl.LEQUAL); gl.depthMask(true);
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

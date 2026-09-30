'use strict';
// Tiny procedural sound effects (WebAudio), no sample files needed.

const Sfx = {
  ctx: null, master: null, enabled: true, noiseBuf: null,
  init() {
    if (this.ctx) return;
    try {
      this.ctx = new (window.AudioContext || window.webkitAudioContext)();
      this.master = this.ctx.createGain(); this.master.gain.value = 0.5; this.master.connect(this.ctx.destination);
      const n = this.ctx.sampleRate;
      this.noiseBuf = this.ctx.createBuffer(1, n, n);
      const d = this.noiseBuf.getChannelData(0);
      for (let i = 0; i < n; i++) d[i] = Math.random() * 2 - 1;
    } catch (e) { this.ctx = null; }
  },
  resume() { if (this.ctx && this.ctx.state === 'suspended') this.ctx.resume(); },
  material(id) {
    const k = B_KEY[id] || '';
    if (/GLASS|ICE|PANE/.test(k)) return 'glass';
    if (/LEAVES|GRASS|FERN|SAPLING|VINE|FLOWER|TULIP|KELP|SEAGRASS|MOSS|ROSE|DAISY|ORCHID|POPPY|DANDELION|BUSH/.test(k)) return 'grass';
    if (/SAND|SNOW|POWDER/.test(k)) return 'sand';
    if (/GRAVEL|DIRT|PODZOL|MYCEL|MUD|CLAY|FARMLAND/.test(k)) return 'gravel';
    if (/WOOL|CARPET|BED/.test(k)) return 'cloth';
    if (/LOG|WOOD|PLANK|DOOR|FENCE|GATE|STEM|HYPHAE|BOOKSHELF|CHEST|CRAFTING|LADDER|BAMBOO/.test(k)) return 'wood';
    return 'stone';
  },
  noise(t0, dur, gain, type, f0, f1, q) {
    const c = this.ctx;
    const src = c.createBufferSource(); src.buffer = this.noiseBuf;
    const f = c.createBiquadFilter(); f.type = type; f.Q.value = q || 1;
    f.frequency.setValueAtTime(f0, t0); f.frequency.exponentialRampToValueAtTime(Math.max(40, f1), t0 + dur);
    const g = c.createGain(); g.gain.setValueAtTime(0.0001, t0); g.gain.exponentialRampToValueAtTime(gain, t0 + 0.008);
    g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
    src.connect(f); f.connect(g); g.connect(this.master);
    src.start(t0, Math.random() * 0.5); src.stop(t0 + dur + 0.02);
  },
  block(id, kind, vol) {
    if (!this.enabled || !this.ctx) return;
    const t = this.ctx.currentTime, m = this.material(id), v = (vol || 1) * (kind === 'step' ? 0.35 : kind === 'hit' ? 0.4 : 1);
    const P = {
      stone: ['bandpass', 1200, 500, 1.2, 0.14], wood: ['bandpass', 700, 300, 2.5, 0.16], grass: ['highpass', 2500, 1200, 0.7, 0.13],
      sand: ['highpass', 3000, 1800, 0.5, 0.15], gravel: ['bandpass', 900, 400, 0.8, 0.16], cloth: ['lowpass', 900, 300, 0.7, 0.14],
      glass: ['highpass', 4000, 2500, 3, 0.2],
    }[m];
    const dur = kind === 'step' ? 0.08 : kind === 'hit' ? 0.06 : P[4];
    this.noise(t, dur, 0.5 * v, P[0], P[1] * (0.9 + Math.random() * 0.2), P[2], P[3]);
    if (m === 'glass' && kind === 'break') for (let i = 0; i < 4; i++) this.tone(t + i * 0.03, 2000 + Math.random() * 3000, 0.05, 0.06 * v);
  },
  tone(t0, freq, dur, gain, type) {
    const c = this.ctx, o = c.createOscillator(), g = c.createGain();
    o.type = type || 'sine'; o.frequency.setValueAtTime(freq, t0);
    g.gain.setValueAtTime(0.0001, t0); g.gain.exponentialRampToValueAtTime(gain, t0 + 0.005); g.gain.exponentialRampToValueAtTime(0.0001, t0 + dur);
    o.connect(g); g.connect(this.master); o.start(t0); o.stop(t0 + dur + 0.02);
  },
  pop() { if (!this.enabled || !this.ctx) return; const t = this.ctx.currentTime; this.tone(t, 900 + Math.random() * 400, 0.08, 0.12, 'triangle'); },
  hurt() { if (!this.enabled || !this.ctx) return; const t = this.ctx.currentTime; this.noise(t, 0.18, 0.4, 'bandpass', 500, 200, 1); this.tone(t, 180, 0.15, 0.2, 'square'); },
  fuse() { if (!this.enabled || !this.ctx) return; this.noise(this.ctx.currentTime, 1.4, 0.22, 'highpass', 3000, 5000, 0.7); },
  splash() { if (!this.enabled || !this.ctx) return; this.noise(this.ctx.currentTime, 0.35, 0.3, 'lowpass', 1500, 200, 0.6); },
  click() { if (!this.enabled || !this.ctx) return; this.tone(this.ctx.currentTime, 1200, 0.03, 0.08, 'square'); },
  door(open) { if (!this.enabled || !this.ctx) return; const t = this.ctx.currentTime; this.noise(t, 0.2, 0.35, 'bandpass', open ? 500 : 350, 200, 3); },
};

// Voxel Forge — Unity port. main.js WebAudio topology re-implemented as an offline procedural synth:
// master gain -> lowpass(8500) -> compressor, plus a short noise-convolution reverb send (approximated by a comb/allpass tail).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace VoxelForge
{
    public static partial class VF
    {
        static GameObject audioRoot;
        static readonly List<AudioSource> audioPool = new List<AudioSource>();
        static int audioRate = 44100;
        static bool audioInited = false;

        public static bool audioPaused() { return !Application.isFocused && Application.runInBackground == false || pauseOpen || (player != null && player.sleeping); }
        public static bool initAudio()
        {
            if (audioInited || !gameSettings.sound) return audioInited;
            try
            {
                audioRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 44100;
                audioRoot = new GameObject("VF_Audio");
                UnityEngine.Object.DontDestroyOnLoad(audioRoot);
                audioInited = true;
            }
            catch { audioInited = false; }
            return audioInited;
        }
        public static void syncAudioState()
        {
            if (!audioInited) return;
            AudioListener.pause = !gameSettings.sound || audioPaused();
        }
        static bool soundReady()
        {
            initAudio(); syncAudioState();
            return audioInited && gameSettings.sound && !audioPaused();
        }
        static AudioSource takeSource()
        {
            foreach (var s in audioPool) if (!s.isPlaying) return s;
            if (audioPool.Count >= 24) return audioPool[0];
            var a = audioRoot.AddComponent<AudioSource>(); a.playOnAwake = false; a.spatialBlend = 0; audioPool.Add(a);
            return a;
        }
        static void playSamples(float[] buf, float verb)
        {
            if (verb > 0) applyReverbSend(buf, verb);
            // master: gain .85 -> lowpass 8500 Q.7 -> soft compressor
            var lp = new Biquad(); float g = 0.85f;
            for (int i = 0; i < buf.Length; i++)
            {
                if ((i & 31) == 0) lp.SetLowpass(8500, 0.7, audioRate);
                float x = lp.Process(buf[i] * g);
                // compressor approximation: threshold -12dB (≈0.25), ratio 6, soft knee
                float ax = Math.Abs(x);
                if (ax > 0.25f) x = Math.Sign(x) * (0.25f + (ax - 0.25f) / 6f);
                buf[i] = x;
            }
            var clip = AudioClip.Create("vf_sfx", buf.Length, 1, audioRate, false);
            clip.SetData(buf, 0);
            var src = takeSource();
            if (src.clip != null && src.clip.name == "vf_sfx") UnityEngine.Object.Destroy(src.clip);
            src.clip = clip; src.volume = 1; src.Play();
        }
        static void applyReverbSend(float[] buf, float verb)
        {
            // Short room tail (≈0.45s decay), matching the convolver built from decaying noise.
            int n = buf.Length; int[] d = { (int)(0.0297 * audioRate), (int)(0.0371 * audioRate), (int)(0.0411 * audioRate), (int)(0.0437 * audioRate) };
            float fb = 0.62f;
            var wet = new float[n];
            foreach (int dl in d)
            {
                var line = new float[Math.Max(1, dl)]; int p = 0;
                for (int i = 0; i < n; i++) { float y = line[p]; line[p] = buf[i] + y * fb; p = (p + 1) % line.Length; wet[i] += y * 0.25f; }
            }
            for (int i = 0; i < n; i++) buf[i] += wet[i] * verb;
        }

        sealed class Biquad
        {
            double b0, b1, b2, a1, a2, x1, x2, y1, y2;
            public void SetLowpass(double f, double q, int sr) { f = Math.Min(f, sr * 0.45); double w = 2 * Math.PI * f / sr, al = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + al; b0 = (1 - c) / 2 / a0; b1 = (1 - c) / a0; b2 = (1 - c) / 2 / a0; a1 = -2 * c / a0; a2 = (1 - al) / a0; }
            public void SetBandpass(double f, double q, int sr) { f = Math.Min(f, sr * 0.45); double w = 2 * Math.PI * f / sr, al = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + al; b0 = al / a0; b1 = 0; b2 = -al / a0; a1 = -2 * c / a0; a2 = (1 - al) / a0; }
            public void SetHighpass(double f, double q, int sr) { f = Math.Min(f, sr * 0.45); double w = 2 * Math.PI * f / sr, al = Math.Sin(w) / (2 * q), c = Math.Cos(w), a0 = 1 + al; b0 = (1 + c) / 2 / a0; b1 = -(1 + c) / a0; b2 = (1 + c) / 2 / a0; a1 = -2 * c / a0; a2 = (1 - al) / a0; }
            public float Process(float x) { double y = b0 * x + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2; x2 = x1; x1 = x; y2 = y1; y1 = y; return (float)y; }
            public void Set(string type, double f, double q, int sr) { if (type == "lowpass") SetLowpass(f, q, sr); else if (type == "highpass") SetHighpass(f, q, sr); else SetBandpass(f, q, sr); }
        }
        static double expRamp(double v0, double v1, double t, double t0, double t1)
        {
            if (t <= t0) return v0; if (t >= t1) return v1;
            return v0 * Math.Pow(v1 / v0, (t - t0) / (t1 - t0));
        }
        static double envAt(double t, double vol, double attack, double dur)
        {
            if (t < attack) return expRamp(1e-4, vol, t, 0, attack);
            return expRamp(vol, 1e-4, t, attack, dur);
        }

        public static void audioNoise(double dur = 0.08, double vol = 0.08, string type = "bandpass", double f0 = 900, double f1 = 300, double q = 0.9, double verb = 0.12)
        {
            if (!soundReady()) return;
            try
            {
                int n = (int)((dur + 0.02 + 0.25) * audioRate); var buf = new float[n];
                double rate = 0.85 + JS.random() * 0.3; var flt = new Biquad(); double phase = JS.random() * 0.5 * audioRate;
                var rnd = new System.Random((int)(JS.random() * int.MaxValue));
                int active = (int)((dur + 0.02) * audioRate);
                for (int i = 0; i < active; i++)
                {
                    double t = i / (double)audioRate;
                    if ((i & 15) == 0) flt.Set(type, expRamp(f0, Math.Max(60, f1), t, 0, dur), q, audioRate);
                    phase += rate; float s = (float)(rnd.NextDouble() * 2 - 1);
                    buf[i] = flt.Process(s) * (float)envAt(t, vol, 0.004, dur);
                }
                playSamples(buf, (float)verb);
            }
            catch { }
        }
        public static void audioTone(double freq = 420, double dur = 0.08, double vol = 0.08, double verb = 0.1)
        {
            if (!soundReady()) return;
            try
            {
                int n = (int)((dur + 0.02 + 0.25) * audioRate); var buf = new float[n];
                var parts = new[] { new { typ = "sine", mul = 1.0, gain = 1.0 }, new { typ = "triangle", mul = 2.01, gain = 0.16 }, new { typ = "sine", mul = 0.5, gain = 0.3 } };
                var flt = new Biquad(); double attack = Math.Min(0.014, dur * 0.3);
                var ph = new double[3]; var det = new double[3];
                for (int k = 0; k < 3; k++) det[k] = Math.Pow(2, ((JS.random() * 2 - 1) * 6) / 1200.0);
                int active = (int)((dur + 0.02) * audioRate);
                for (int i = 0; i < active; i++)
                {
                    double t = i / (double)audioRate;
                    if ((i & 15) == 0) flt.SetLowpass(expRamp(Math.Min(12000, freq * 7), Math.Max(120, freq * 1.4), t, 0, dur), 0.9, audioRate);
                    double s = 0;
                    for (int k = 0; k < 3; k++)
                    {
                        double f = expRamp(freq * parts[k].mul * 1.015, freq * parts[k].mul * 0.985, t, 0, dur) * det[k];
                        ph[k] += f / audioRate; ph[k] -= Math.Floor(ph[k]);
                        double w = parts[k].typ == "sine" ? Math.Sin(ph[k] * 2 * Math.PI) : 1 - 4 * Math.Abs(ph[k] - 0.5);
                        s += w * parts[k].gain;
                    }
                    buf[i] = flt.Process((float)s) * (float)envAt(t, vol, attack, dur);
                }
                playSamples(buf, (float)verb);
            }
            catch { }
        }
        static readonly System.Text.RegularExpressions.Regex RX_SND_STONE = new System.Text.RegularExpressions.Regex("кам|руда|кирп|обсиди|песчан|стек|лёд|печ|бетон|террак|deepslate|tuff");
        static readonly System.Text.RegularExpressions.Regex RX_SND_WOOD = new System.Text.RegularExpressions.Regex("бревн|доск|дерев|сундук|верстак|двер|лест|забор|кровать");
        static string soundMaterial(int id)
        {
            var b = id >= 0 && id < blocks.Length ? blocks[id] : null;
            var n = (b?.name ?? "").ToLowerInvariant();
            if (RX_SND_STONE.IsMatch(n)) return "stone";
            if (RX_SND_WOOD.IsMatch(n)) return "wood";
            return "soft";
        }
        public static void sfxBlockBreak(int id)
        {
            var m = soundMaterial(id);
            double[] a = m == "stone" ? new[] { 820, 360, 1.5, 0.075, 0.05 } : m == "wood" ? new[] { 500, 230, 1.2, 0.075, 0.055 } : new[] { 300, 140, 0.9, 0.07, 0.05 };
            audioNoise(a[3], a[4], "bandpass", a[0], a[1], a[2], 0.18);
            audioTone(m == "stone" ? 96 : m == "wood" ? 78 : 62, 0.11, 0.055, 0.16);
        }
        public static void sfxPlace(int id)
        {
            var m = soundMaterial(id);
            audioNoise(0.055, 0.055, "bandpass", m == "stone" ? 700 : m == "wood" ? 430 : 260, m == "stone" ? 300 : m == "wood" ? 200 : 120, 1, 0.08);
        }
        public static void sfxClick() { audioTone(420, 0.08, 0.07, 0.08); }
        public static void sfxPickup() { audioTone(920, 0.07, 0.055, 0.05); Timers.setTimeout(() => audioTone(1220, 0.06, 0.04, 0.04), 35); }
        public static void sfxCraft() { audioTone(620, 0.09, 0.07, 0.09); Timers.setTimeout(() => audioTone(840, 0.08, 0.05, 0.07), 45); }
        public static void sfxHurt() { audioNoise(0.12, 0.12, "lowpass", 1400, 280, 0.7, 0.08); audioTone(150, 0.12, 0.06, 0.08); }
        public static void sfxExplosion()
        {
            if (!soundReady()) return;
            audioNoise(1.05, 0.48, "lowpass", 1200, 90, 0.6, 0.6);
            try
            {
                int n = (int)(1.2 * audioRate); var buf = new float[n]; double ph = 0;
                for (int i = 0; i < (int)(0.9 * audioRate); i++)
                {
                    double t = i / (double)audioRate, f = expRamp(95, 32, t, 0, 0.7);
                    ph += f / audioRate;
                    buf[i] = (float)(Math.Sin(ph * 2 * Math.PI) * envAt(t, 0.55, 0.01, 0.85));
                }
                playSamples(buf, 0.5f);
            }
            catch { }
        }
        public static void sfxSleep() { audioTone(520, 0.12, 0.1, 0.18); }
        public static void setSoundEnabled(bool on)
        {
            gameSettings.sound = on;
            persistGameSettings();
            if (gameSettings.sound) initAudio();
            syncAudioState();
            renderSettingsUI();
        }
    }
}

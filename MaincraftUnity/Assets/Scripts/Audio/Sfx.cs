// Port of audio.js (11749-11834): tiny procedural sound effects, no sample files needed. Every sound is
// a filtered noise burst (1 s white noise from a random offset, a biquad lowpass / highpass / bandpass
// with an exponential frequency sweep, an exponential attack / decay envelope) and / or a plain
// oscillator tone with the same envelope, into a master gain of 0.5.
//
// WebAudio -> Unity: the AudioContext is SfxMixer (Audio/SfxMixer.cs), a MonoBehaviour with an AudioSource whose
// OnAudioFilterRead mixes a fixed voice pool (no allocation on either thread). Sfx (main thread) builds
// voices with start times on the mixer's clock (ctx.currentTime -> Sfx.CurrentTime, seconds of DSP time)
// and hands them over with SfxMixer.Add; the audio thread computes the envelopes, filters and
// oscillators per sample (RBJ biquad as the Web Audio spec defines it: lowpass / highpass Q in dB,
// bandpass Q linear). Randoms (noise offset, pitch) are drawn with Rand.Next() on the calling thread at
// call time, in the JS order, and only when the JS draws them (enabled and initialised).
//
// API skeleton: bodies marked TODO(misc) are filled in by that implementer.
using System;
using UnityEngine;
using static Maincraft.Blocks;

namespace Maincraft
{
    /// <summary>Sfx.block kinds (the JS strings 'step', 'hit', 'break', 'place').</summary>
    public enum SfxKind { Step, Hit, Break, Place }

    /// <summary>Sfx.material results (the JS strings).</summary>
    public enum SfxMaterial { Glass, Grass, Sand, Gravel, Cloth, Wood, Stone }

    /// <summary>BiquadFilterNode.type of a noise burst.</summary>
    public enum SfxFilter { Lowpass, Highpass, Bandpass }

    /// <summary>OscillatorNode.type of a tone (default 'sine').</summary>
    public enum SfxWave { Sine, Triangle, Square, Sawtooth }

    /// <summary>Sfx.trident kinds: 'throw', 'hit', anything else = sticking in the ground.</summary>
    public enum TridentSound { Throw, Hit, Ground }

    /// <summary>One scheduled sound voice: a noise burst through a filter, or an oscillator tone.</summary>
    public struct SfxVoice
    {
        public bool tone;               // false: noise (Sfx.Noise), true: oscillator (Sfx.Tone)
        public double t0, dur;          // start (mixer clock, s), envelope end (t0 + dur); stops at t0 + dur + 0.02
        public double gain;             // envelope peak (0.0001 -> gain at t0 + 0.008 (noise) / 0.005 (tone) -> 0.0001 at t0 + dur)
        // noise
        public SfxFilter filter;
        public double f0, f1, q;        // frequency f0 at t0, exponential ramp to f1 (already max(40, f1)) at t0 + dur; Q
        public int offset;              // start sample in the noise buffer (Math.random() * 0.5 s)
        // tone
        public SfxWave wave;
        public double freq;
    }

    public static class Sfx
    {
        /// <summary>The master gain node's value.</summary>
        public const float MASTER_GAIN = 0.5f;
        /// <summary>The AudioContext: the mixer, or null when not initialised / unsupported (every sound checks it).</summary>
        public static SfxMixer ctx;
        /// <summary>Sound on / off (Settings.sound, written by the UI). Off: the methods return before drawing randoms.</summary>
        public static bool enabled = true;
        /// <summary>1 s of mono white noise (Math.random() * 2 - 1), sampleRate samples, made by Init.</summary>
        public static float[] noiseBuf;
        /// <summary>The output sample rate (ctx.sampleRate): AudioSettings.outputSampleRate.</summary>
        public static int sampleRate;
        /// <summary>Material per block id (the regex tests of material() on B_KEY, precomputed by Init).</summary>
        public static SfxMaterial[] matTable;

        /// <summary>ctx.currentTime: the mixer's clock in seconds, Interlocked.Read(ref ctx.samplesRendered) / (double)sampleRate
        /// (only valid while ctx != null).</summary>
        public static double CurrentTime => throw new NotImplementedException(); // TODO(misc)

        // audio.js init (11755-11765): once (returns if ctx exists): the mixer GameObject, the noise buffer;
        // on any exception ctx = null (sound silently off)
        public static void Init() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js resume (11766): a suspended context resumes (autoplay policy); a no-op in Unity apart from AudioListener.pause
        public static void Resume() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js material (11767-11776): ordered regex tests on B_KEY[id] ('' if missing), the first match wins
        public static SfxMaterial Material(int id) { throw new NotImplementedException(); } // TODO(misc)
        // audio.js noise (11777-11786): a filtered noise burst from a random offset (draws 1 random); q 0 -> 1 (q || 1)
        public static void Noise(double t0, double dur, double gain, SfxFilter type, double f0, double f1, double q) { throw new NotImplementedException(); } // TODO(misc)
        // audio.js block (11787-11798): block sound by material and kind; vol 0 -> 1 (vol || 1)
        public static void Block(int id, SfxKind kind, double vol = 1) { throw new NotImplementedException(); } // TODO(misc)
        // audio.js tone (11799-11804): an oscillator at a constant frequency
        public static void Tone(double t0, double freq, double dur, double gain, SfxWave type = SfxWave.Sine) { throw new NotImplementedException(); } // TODO(misc)
        // audio.js pop (11805): item pickup
        public static void Pop() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js hurt (11806)
        public static void Hurt() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js fuse (11807): TNT / creeper fuse hiss
        public static void Fuse() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js splash (11808)
        public static void Splash() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js drownedConvert (11810-11815): a zombie turned into a drowned
        public static void DrownedConvert() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js trident (11817-11823): thrown, striking a body, sticking in the ground
        public static void Trident(TridentSound kind) { throw new NotImplementedException(); } // TODO(misc)
        // audio.js click (11824): buttons, levers
        public static void Click() { throw new NotImplementedException(); } // TODO(misc)
        // audio.js paddle (11826-11831): a boat paddle's stroke (draws 1 random for the pitch)
        public static void Paddle(bool water) { throw new NotImplementedException(); } // TODO(misc)
        // audio.js door (11832)
        public static void Door(bool open) { throw new NotImplementedException(); } // TODO(misc)
    }
}

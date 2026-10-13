// The audio output of audio.js (11749-11834): the AudioContext's graph as a MonoBehaviour (in its own file,
// so Unity finds its script). Sfx (Audio/Sfx.cs) schedules voices; OnAudioFilterRead mixes them.
//
// API skeleton: bodies marked TODO(misc) are filled in by that implementer.
using System;
using System.Threading;
using UnityEngine;

namespace Maincraft
{
    /// <summary>
    /// The audio output (the AudioContext's graph): a persistent GameObject with an AudioSource; mixes the
    /// active voices sample by sample in OnAudioFilterRead (audio thread) times MASTER_GAIN.
    /// </summary>
    public sealed class SfxMixer : MonoBehaviour
    {
        /// <summary>Voice pool size (voices beyond it are dropped).</summary>
        public const int MAX_VOICES = 64;
        /// <summary>Voices handed over by the main thread, not yet taken by the audio thread (lock-guarded ring).</summary>
        public readonly SfxVoice[] pending = new SfxVoice[MAX_VOICES];
        public int pendingN;
        /// <summary>Voices playing (audio thread only), with their per-voice filter / oscillator state.</summary>
        public readonly SfxVoice[] active = new SfxVoice[MAX_VOICES];
        public readonly double[] state = new double[MAX_VOICES * 6];   // biquad x1, x2, y1, y2; oscillator phase; noise read position
        public int activeN;
        public readonly object sync = new object();
        /// <summary>The mixer clock: frames (samples per channel) rendered so far, written by the audio thread with
        /// Interlocked.Exchange and read with Interlocked.Read (a plain 64-bit field can tear on 32-bit ARM).</summary>
        public long samplesRendered;

        // creates the GameObject (DontDestroyOnLoad) with the AudioSource that drives OnAudioFilterRead
        public static SfxMixer Create() { throw new NotImplementedException(); } // TODO(misc)
        // main thread: schedules a voice
        public void Add(in SfxVoice v) { throw new NotImplementedException(); } // TODO(misc)
        // audio thread: mixes the voices into data (interleaved, channels) and advances the clock
        void OnAudioFilterRead(float[] data, int channels) { throw new NotImplementedException(); } // TODO(misc)
    }
}

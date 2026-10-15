using System;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The Steam Whistle's built-in music: an original "epic Nordic battle anthem" in the spirit of a certain
/// dragon game (war drums, a deep male choir chanting "ahh", horns, D minor). The tune and drum pattern are
/// written for this pack; nothing is copied. Players can still use their own thomas_theme file instead.
/// About 18 seconds: drums and horns build up, the choir comes in, and it ends on a big held chord.
/// </summary>
internal static class ThemeSynth
{
    private const int Rate = 44100;
    private const float Bpm = 92f;
    private const float Beat = 60f / Bpm;
    private const float Bar = Beat * 4f;

    // Pitches (Hz)
    private const float D2 = 73.42f, A2 = 110f, Bb2 = 116.54f, C3 = 130.81f, D3 = 146.83f, E3 = 164.81f;
    private const float F3 = 174.61f, G3 = 196f, A3 = 220f, Bb3 = 233.08f, C4 = 261.63f, D4 = 293.66f;

    public static float[] Synthesize()
    {
        float length = Bar * 7f;
        var s = new float[(int)(length * Rate)];
        var rng = new System.Random(1011);

        // Drums all the way through; the last bar is one big hit.
        for (int bar = 0; bar < 6; bar++)
        {
            float t0 = bar * Bar;
            WarDrum(s, t0, 1f, rng);
            WarDrum(s, t0 + Beat * 1.5f, 0.45f, rng);
            WarDrum(s, t0 + Beat * 2f, 0.9f, rng);
            WarDrum(s, t0 + Beat * 3f, 0.55f, rng);
            WarDrum(s, t0 + Beat * 3.5f, 0.7f, rng);
            if (bar >= 2)
            {
                Taiko(s, t0 + Beat, 0.35f, rng);
                Taiko(s, t0 + Beat * 3f, 0.35f, rng);
            }
        }
        WarDrum(s, 6f * Bar, 1.3f, rng);
        Taiko(s, 6f * Bar, 0.6f, rng);

        // Horns: a low drone that swells in, then follows the chords.
        Horn(s, 0f, Bar * 2f, D2, 0.35f);
        Horn(s, 0f, Bar * 2f, A2, 0.2f);
        float[][] chords =
        {
            new[] { D3, F3, A3 },   // Dm
            new[] { Bb2, D3, F3 },  // Bb
            new[] { C3, E3, G3 },   // C
            new[] { D3, F3, A3 },   // Dm
            new[] { D3, F3, A3 },   // held to the end
        };
        for (int c = 0; c < chords.Length; c++)
        {
            float start = (2 + c) * Bar;
            float duration = c == chords.Length - 1 ? Bar * 1.9f : Bar;
            Horn(s, start, duration, chords[c][0] / 2f, 0.3f);
            foreach (float pitch in chords[c])
            {
                Choir(s, start, duration, pitch, 0.22f, rng);
            }
        }

        // The chant: an original line sung over the chords (quarter notes, a held note at the end of each bar).
        (float pitch, float beats)[] chant =
        {
            (A3, 1), (A3, 1), (C4, 1), (A3, 1),
            (Bb3, 1), (A3, 1), (F3, 2),
            (G3, 1), (A3, 1), (G3, 1), (E3, 1),
            (F3, 1), (E3, 1), (D3, 2),
            (D4, 4),
        };
        float time = 2f * Bar;
        foreach ((float pitch, float beats) in chant)
        {
            Choir(s, time, beats * Beat * 0.97f, pitch, 0.38f, rng);
            time += beats * Beat;
        }

        // Fade the tail and normalize.
        int fadeStart = (int)((length - 1.2f) * Rate);
        for (int i = fadeStart; i < s.Length; i++)
        {
            s[i] *= 1f - (float)(i - fadeStart) / (s.Length - fadeStart);
        }
        float max = 0.0001f;
        foreach (float v in s)
        {
            max = Mathf.Max(max, Mathf.Abs(v));
        }
        for (int i = 0; i < s.Length; i++)
        {
            s[i] *= 0.9f / max;
        }
        return s;
    }

    /// <summary>A deep war drum: a pitch-dropping thump plus a short burst of dark noise.</summary>
    private static void WarDrum(float[] s, float at, float gain, System.Random rng)
    {
        int start = (int)(at * Rate);
        int length = (int)(0.9f * Rate);
        double phase = 0;
        float low = 0f;
        for (int i = 0; i < length && start + i < s.Length; i++)
        {
            float t = (float)i / Rate;
            float freq = Mathf.Lerp(42f, 95f, Mathf.Exp(-t / 0.06f));
            phase += 2 * Math.PI * freq / Rate;
            low += ((float)rng.NextDouble() * 2f - 1f - low) * 0.05f;
            s[start + i] += gain * ((float)Math.Sin(phase) * Mathf.Exp(-t / 0.32f) * 1.4f + low * 4f * Mathf.Exp(-t / 0.08f));
        }
    }

    /// <summary>A higher, sharper drum for accents.</summary>
    private static void Taiko(float[] s, float at, float gain, System.Random rng)
    {
        int start = (int)(at * Rate);
        int length = (int)(0.4f * Rate);
        double phase = 0;
        for (int i = 0; i < length && start + i < s.Length; i++)
        {
            float t = (float)i / Rate;
            phase += 2 * Math.PI * Mathf.Lerp(150f, 230f, Mathf.Exp(-t / 0.03f)) / Rate;
            float noise = (float)rng.NextDouble() * 2f - 1f;
            s[start + i] += gain * ((float)Math.Sin(phase) * Mathf.Exp(-t / 0.12f) + noise * 0.4f * Mathf.Exp(-t / 0.02f));
        }
    }

    /// <summary>A brass swell: bright harmonics rolled off, slow attack, slow release.</summary>
    private static void Horn(float[] s, float at, float duration, float pitch, float gain)
    {
        int start = (int)(at * Rate);
        int length = (int)((duration + 0.5f) * Rate);
        var phases = new double[10];
        for (int i = 0; i < length && start + i < s.Length; i++)
        {
            float t = (float)i / Rate;
            float envelope = Mathf.Clamp01(t / 0.7f) * (t > duration ? Mathf.Clamp01(1f - (t - duration) / 0.5f) : 1f);
            float vibrato = 1f + 0.003f * Mathf.Sin(2f * Mathf.PI * 4.5f * t);
            float v = 0f;
            for (int h = 1; h <= phases.Length; h++)
            {
                phases[h - 1] += 2 * Math.PI * pitch * h * vibrato / Rate;
                v += (float)Math.Sin(phases[h - 1]) * Mathf.Exp(-h / 3.5f) / h;
            }
            s[start + i] += v * envelope * gain;
        }
    }

    /// <summary>
    /// A choir note: three slightly detuned voices singing "ahh". Each voice is a stack of harmonics whose
    /// loudness follows the vowel's resonances (formants), which is what makes it sound sung.
    /// </summary>
    private static void Choir(float[] s, float at, float duration, float pitch, float gain, System.Random rng)
    {
        int start = (int)(at * Rate);
        int length = (int)((duration + 0.45f) * Rate);
        float[] detune = { 0.996f, 1f, 1.005f };
        int harmonics = Mathf.Min(24, (int)(3200f / pitch));
        var amplitude = new float[harmonics + 1];
        for (int h = 1; h <= harmonics; h++)
        {
            float f = pitch * h;
            amplitude[h] = Formant(f, 700f, 130f, 1f) + Formant(f, 1220f, 160f, 0.55f) + Formant(f, 2600f, 260f, 0.22f);
        }
        foreach (float d in detune)
        {
            var phases = new double[harmonics + 1];
            float vibratoRate = 5f + (float)rng.NextDouble();
            float vibratoPhase = (float)rng.NextDouble() * 6.28f;
            for (int i = 0; i < length && start + i < s.Length; i++)
            {
                float t = (float)i / Rate;
                float envelope = Mathf.Clamp01(t / 0.12f) * (t > duration ? Mathf.Clamp01(1f - (t - duration) / 0.45f) : 1f);
                float vibrato = 1f + 0.006f * Mathf.Sin(2f * Mathf.PI * vibratoRate * t + vibratoPhase) * Mathf.Clamp01(t / 0.4f);
                float v = 0f;
                for (int h = 1; h <= harmonics; h++)
                {
                    phases[h] += 2 * Math.PI * pitch * d * h * vibrato / Rate;
                    v += (float)Math.Sin(phases[h]) * amplitude[h];
                }
                s[start + i] += v * envelope * gain / detune.Length;
            }
        }
    }

    private static float Formant(float frequency, float center, float bandwidth, float weight)
    {
        float x = (frequency - center) / bandwidth;
        return weight * Mathf.Exp(-x * x);
    }
}

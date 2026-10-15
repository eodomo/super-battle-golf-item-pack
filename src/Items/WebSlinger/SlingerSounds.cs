using System;
using System.Threading.Tasks;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The web slinger's sounds, synthesized (and replaceable with files named webslinger_thwip, webslinger_snap
/// and webslinger_theme in the item's folder). The theme is an original 60s-cartoon-style hero march, not the
/// real Spider-Man tune.
/// </summary>
internal static class SlingerSounds
{
    private const int Rate = 44100;
    private const float Tempo = 148f;

    private static FMOD.Sound thwip;
    private static FMOD.Sound snap;
    private static FMOD.Sound theme;

    // The theme takes a moment to synthesize, so start on a background thread at launch.
    private static readonly Task<float[]> themeSamples = Task.Run(SynthesizeTheme);

    public static FMOD.Sound Thwip => thwip.hasHandle() ? thwip : thwip = Load("webslinger_thwip", SynthesizeThwip, false);
    public static FMOD.Sound Snap => snap.hasHandle() ? snap : snap = Load("webslinger_snap", SynthesizeSnap, false);
    public static FMOD.Sound Theme => theme.hasHandle() ? theme : theme = Load("webslinger_theme", () => themeSamples.Result, true);

    private static FMOD.Sound Load(string name, Func<float[]> synthesize, bool loop) =>
        ItemAudio.LoadOrSynthesize(WebSlingerItem.Instance, name, synthesize, loop);

    /// <summary>"Thwip!": a quick airy squirt of web fluid.</summary>
    private static float[] SynthesizeThwip()
    {
        return new Synth(0.32f, 11)
            .Noise(Synth.Env(0.004f, 0.02f, 0.2f), t => Mathf.Lerp(0.9f, 0.1f, Mathf.Clamp01(t / 0.22f)), 1f, band: true)
            .Tone(t => Mathf.Lerp(2600f, 480f, Mathf.Clamp01(t / 0.18f)), Synth.Decay(0.06f), 0.4f)
            .Click(0f, 1100f, 0.008f, 0.35f)
            .Normalized(0.85f);
    }

    /// <summary>The web sticking to its target: a wet little smack.</summary>
    private static float[] SynthesizeSnap()
    {
        return new Synth(0.25f, 12)
            .Click(0f, 210f, 0.035f, 1f, 0.6f)
            .Noise(Synth.Env(0.002f, 0.01f, 0.1f), t => 0.5f, 0.5f, band: true)
            .Tone(t => Mathf.Lerp(900f, 200f, Mathf.Clamp01(t / 0.08f)), Synth.Decay(0.03f), 0.4f)
            .Normalized(0.8f);
    }

    // ---- the theme ----

    private static double Hz(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);

    // (midi note, start beat, length in beats): eight bars of 4/4 in E minor.
    private static readonly (int note, float start, float length)[] Melody =
    {
        (64, 0f, .5f), (64, .5f, .5f), (67, 1f, .5f), (64, 1.5f, .5f), (71, 2f, 1f), (69, 3f, .5f), (67, 3.5f, .5f),
        (64, 4f, .5f), (64, 4.5f, .5f), (67, 5f, .5f), (64, 5.5f, .5f), (74, 6f, 1.5f), (71, 7.5f, .5f),
        (69, 8f, .5f), (69, 8.5f, .5f), (72, 9f, .5f), (69, 9.5f, .5f), (76, 10f, 1f), (74, 11f, .5f), (72, 11.5f, .5f),
        (71, 12f, .5f), (71, 12.5f, .5f), (74, 13f, .5f), (71, 13.5f, .5f), (78, 14f, 1.5f), (76, 15.5f, .5f),
        (64, 16f, .5f), (64, 16.5f, .5f), (67, 17f, .5f), (64, 17.5f, .5f), (71, 18f, 1f), (69, 19f, .5f), (67, 19.5f, .5f),
        (64, 20f, .5f), (64, 20.5f, .5f), (67, 21f, .5f), (64, 21.5f, .5f), (74, 22f, 1f), (71, 23f, 1f),
        (79, 24f, .5f), (78, 24.5f, .5f), (76, 25f, .5f), (74, 25.5f, .5f), (71, 26f, 1f), (67, 27f, 1f),
        (76, 28f, 1.5f), (71, 29.5f, .5f), (76, 30f, 2f),
    };

    private static readonly int[] BassRoots = { 40, 40, 45, 47, 40, 40, 36, 40 }; // E E A B E E C E

    private static float[] SynthesizeTheme()
    {
        double beat = 60.0 / Tempo;
        int bars = 8;
        int length = (int)(bars * 4 * beat * Rate);
        var buffer = new float[length];
        var rng = new System.Random(1962);

        foreach ((int note, float start, float len) in Melody)
        {
            AddBrass(buffer, Hz(note), start * beat, len * beat * 0.92, 0.5f);
            AddBrass(buffer, Hz(note - 12), start * beat, len * beat * 0.92, 0.22f);
        }
        // Stabs on the last bar.
        foreach (int note in new[] { 52, 59, 64 })
        {
            AddBrass(buffer, Hz(note), 30 * beat, 2 * beat * 0.95, 0.28f);
        }
        for (int bar = 0; bar < bars; bar++)
        {
            for (int eighth = 0; eighth < 8; eighth++)
            {
                int note = BassRoots[bar] + (eighth % 4 == 3 ? 7 : eighth % 2 == 1 ? 12 : 0);
                AddPluck(buffer, Hz(note), (bar * 4 + eighth * 0.5) * beat, 0.35f);
                AddHat(buffer, rng, (bar * 4 + eighth * 0.5) * beat, eighth % 2 == 0 ? 0.07f : 0.11f);
            }
            for (int b = 0; b < 4; b++)
            {
                double t = (bar * 4 + b) * beat;
                if (b % 2 == 0)
                {
                    AddKick(buffer, t);
                }
                else
                {
                    AddSnare(buffer, rng, t);
                }
            }
        }

        // Fade the very ends so the loop point doesn't click.
        int fade = Rate / 200;
        for (int i = 0; i < fade; i++)
        {
            float g = (float)i / fade;
            buffer[i] *= g;
            buffer[length - 1 - i] *= g;
        }
        float max = 0.0001f;
        foreach (float s in buffer)
        {
            max = Math.Max(max, Math.Abs(s));
        }
        float gain = 0.85f / max;
        for (int i = 0; i < length; i++)
        {
            buffer[i] *= gain;
        }
        return buffer;
    }

    /// <summary>A brassy sawtooth-ish note with vibrato.</summary>
    private static void AddBrass(float[] buffer, double frequency, double start, double length, float gain)
    {
        int first = (int)(start * Rate);
        int count = (int)((length + 0.12) * Rate);
        double phase = 0;
        for (int i = 0; i < count && first + i < buffer.Length; i++)
        {
            double t = (double)i / Rate;
            double envelope = Math.Min(1.0, t / 0.025) * (t < length ? 1.0 : Math.Max(0.0, 1.0 - (t - length) / 0.12));
            double vibrato = 1.0 + 0.004 * Math.Sin(2 * Math.PI * 5.5 * t) * Math.Min(1.0, t / 0.2);
            phase += 2 * Math.PI * frequency * vibrato / Rate;
            double v = 0;
            for (int h = 1; h <= 8; h++)
            {
                v += Math.Sin(phase * h) / h * (h <= 5 ? 1.0 : 0.6);
            }
            buffer[first + i] += (float)(v * envelope * gain * 0.5);
        }
    }

    private static void AddPluck(float[] buffer, double frequency, double start, float gain)
    {
        int first = (int)(start * Rate);
        int count = (int)(0.28 * Rate);
        double phase = 0;
        for (int i = 0; i < count && first + i < buffer.Length; i++)
        {
            double t = (double)i / Rate;
            phase += 2 * Math.PI * frequency / Rate;
            double v = Math.Sin(phase) + 0.45 * Math.Sin(phase * 2) + 0.2 * Math.Sin(phase * 3);
            buffer[first + i] += (float)(v * Math.Exp(-t / 0.09) * gain * 0.6);
        }
    }

    private static void AddKick(float[] buffer, double start)
    {
        int first = (int)(start * Rate);
        int count = (int)(0.22 * Rate);
        double phase = 0;
        for (int i = 0; i < count && first + i < buffer.Length; i++)
        {
            double t = (double)i / Rate;
            phase += 2 * Math.PI * (45.0 + 110.0 * Math.Exp(-t / 0.03)) / Rate;
            buffer[first + i] += (float)(Math.Sin(phase) * Math.Exp(-t / 0.07) * 0.7);
        }
    }

    private static void AddSnare(float[] buffer, System.Random rng, double start)
    {
        int first = (int)(start * Rate);
        int count = (int)(0.2 * Rate);
        float low = 0f;
        for (int i = 0; i < count && first + i < buffer.Length; i++)
        {
            double t = (double)i / Rate;
            float white = (float)rng.NextDouble() * 2f - 1f;
            low += (white - low) * 0.5f;
            double tone = Math.Sin(2 * Math.PI * 190 * t) * Math.Exp(-t / 0.03);
            buffer[first + i] += (float)((low * Math.Exp(-t / 0.06) + tone * 0.4) * 0.45);
        }
    }

    private static void AddHat(float[] buffer, System.Random rng, double start, float gain)
    {
        int first = (int)(start * Rate);
        int count = (int)(0.05 * Rate);
        float previous = 0f;
        for (int i = 0; i < count && first + i < buffer.Length; i++)
        {
            double t = (double)i / Rate;
            float white = (float)rng.NextDouble() * 2f - 1f;
            float highpassed = white - previous;
            previous = white;
            buffer[first + i] += (float)(highpassed * Math.Exp(-t / 0.015) * gain);
        }
    }
}

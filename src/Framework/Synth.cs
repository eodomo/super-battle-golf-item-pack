using System;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// Tiny sound synthesizer for item sound effects, so the pack needs no audio files. Build a buffer, add
/// layers (tones, sweeps, filtered noise, clicks) with envelopes, then normalize.
/// </summary>
internal sealed class Synth
{
    public const int Rate = 44100;

    public readonly float[] Samples;
    private readonly System.Random rng;

    public Synth(float seconds, int seed = 1)
    {
        Samples = new float[Mathf.Max(1, (int)(seconds * Rate))];
        rng = new System.Random(seed);
    }

    public float Duration => (float)Samples.Length / Rate;

    public float Noise() => (float)rng.NextDouble() * 2f - 1f;

    /// <summary>Adds a sine whose frequency follows <paramref name="frequency"/>(t), shaped by <paramref name="envelope"/>(t).</summary>
    public Synth Tone(Func<float, float> frequency, Func<float, float> envelope, float gain = 1f, int harmonics = 1, float start = 0f)
    {
        double phase = 0;
        for (int i = (int)(start * Rate); i < Samples.Length; i++)
        {
            float t = (float)i / Rate - start;
            phase += 2 * Math.PI * frequency(t) / Rate;
            float v = 0f;
            for (int h = 1; h <= harmonics; h++)
            {
                v += (float)Math.Sin(phase * h) / (h * h);
            }
            Samples[i] += v * envelope(t) * gain;
        }
        return this;
    }

    /// <summary>
    /// Adds noise through a one-pole low-pass (<paramref name="smoothing"/> 0..1: small = dark rumble,
    /// 1 = white hiss), optionally made band-like by subtracting a darker copy.
    /// </summary>
    public Synth Noise(Func<float, float> envelope, Func<float, float> smoothing, float gain = 1f, float start = 0f, bool band = false)
    {
        float low = 0f, darker = 0f;
        for (int i = (int)(start * Rate); i < Samples.Length; i++)
        {
            float t = (float)i / Rate - start;
            float a = Mathf.Clamp(smoothing(t), 0.001f, 1f);
            float white = Noise();
            low += (white - low) * a;
            darker += (white - darker) * a * 0.15f;
            Samples[i] += (band ? low - darker : low) * envelope(t) * gain;
        }
        return this;
    }

    /// <summary>A short percussive click/knock at <paramref name="at"/> seconds (pitch in Hz, decay in seconds).</summary>
    public Synth Click(float at, float pitch, float decay, float gain = 1f, float noise = 0.5f)
    {
        int start = (int)(at * Rate);
        int length = (int)(decay * 6f * Rate);
        for (int i = 0; i < length && start + i < Samples.Length; i++)
        {
            float t = (float)i / Rate;
            float v = (float)Math.Sin(2 * Math.PI * pitch * t) + Noise() * noise;
            Samples[start + i] += v * Mathf.Exp(-t / decay) * gain;
        }
        return this;
    }

    /// <summary>Echo: adds delayed, duller copies of the sound so far.</summary>
    public Synth Echo(float delay, float gain, int repeats = 3)
    {
        int offset = (int)(delay * Rate);
        for (int r = 1; r <= repeats; r++)
        {
            float g = Mathf.Pow(gain, r);
            float smooth = 0f;
            for (int i = Samples.Length - 1 - offset * r; i >= 0; i--)
            {
                smooth += (Samples[i] - smooth) * 0.3f;
                Samples[i + offset * r] += smooth * g;
            }
        }
        return this;
    }

    public float[] Normalized(float peak = 0.9f)
    {
        float max = 0.0001f;
        foreach (float s in Samples)
        {
            max = Mathf.Max(max, Mathf.Abs(s));
        }
        float g = peak / max;
        for (int i = 0; i < Samples.Length; i++)
        {
            Samples[i] *= g;
        }
        return Samples;
    }

    // ---- envelopes ----

    /// <summary>Linear attack, hold, then exponential-ish release.</summary>
    public static Func<float, float> Env(float attack, float hold, float release) => t =>
    {
        if (t < 0f)
        {
            return 0f;
        }
        if (t < attack)
        {
            return t / attack;
        }
        if (t < attack + hold)
        {
            return 1f;
        }
        float r = (t - attack - hold) / Mathf.Max(release, 0.0001f);
        return r >= 1f ? 0f : (1f - r) * (1f - r);
    };

    /// <summary>Exponential decay from 1.</summary>
    public static Func<float, float> Decay(float seconds) => t => t < 0f ? 0f : Mathf.Exp(-t / seconds);

    public static Func<float, float> Const(float value) => _ => value;

    /// <summary>Straight line from <paramref name="from"/> to <paramref name="to"/> over <paramref name="seconds"/>, then holds.</summary>
    public static Func<float, float> Ramp(float from, float to, float seconds) => t => Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds));
}

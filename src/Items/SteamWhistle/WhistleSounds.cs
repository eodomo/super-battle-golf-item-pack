using UnityEngine;

namespace ItemPack.Items;

/// <summary>Sounds synthesized at startup, so no audio files need to ship with the pack.</summary>
internal static class WhistleSounds
{
    private const int SampleRate = 44100;

    private static FMOD.Sound whistle;
    private static FMOD.Sound whoosh;

    public static FMOD.Sound Whistle => whistle.hasHandle() ? whistle : whistle = ItemAudio.CreateSound(SynthesizeWhistle(), SampleRate);
    public static FMOD.Sound Whoosh => whoosh.hasHandle() ? whoosh : whoosh = ItemAudio.CreateSound(SynthesizeWhoosh(), SampleRate);

    /// <summary>A small tank engine's "peep peep": two toots of a three-note chime whistle.</summary>
    private static float[] SynthesizeWhistle()
    {
        float[] chord = { 740f, 932f, 1109f }; // F#5, A#5, C#6
        (float start, float end)[] toots = { (0f, 0.26f), (0.36f, 0.95f) };
        int length = (int)(1.1f * SampleRate);
        var samples = new float[length];
        var phases = new float[chord.Length * 6];
        var rng = new System.Random(4069520);
        float noiseState = 0f;

        for (int i = 0; i < length; i++)
        {
            float t = (float)i / SampleRate;
            float envelope = 0f;
            float sinceStart = 0f;
            foreach ((float start, float end) in toots)
            {
                if (t >= start && t < end + 0.08f)
                {
                    float attack = Mathf.Clamp01((t - start) / 0.03f);
                    float release = t > end ? 1f - (t - end) / 0.08f : 1f;
                    envelope = attack * release;
                    sinceStart = t - start;
                }
            }
            if (envelope <= 0f)
            {
                continue;
            }

            // Pitch scoops up as the steam builds, plus a little wobble.
            float glide = Mathf.Lerp(0.93f, 1f, Mathf.Clamp01(sinceStart / 0.07f));
            float vibrato = 1f + 0.004f * Mathf.Sin(2f * Mathf.PI * 5.5f * t);
            float tone = 0f;
            for (int c = 0; c < chord.Length; c++)
            {
                for (int h = 1; h <= 6; h++)
                {
                    int p = c * 6 + h - 1;
                    phases[p] += 2f * Mathf.PI * chord[c] * h * glide * vibrato / SampleRate;
                    tone += Mathf.Sin(phases[p]) / Mathf.Pow(h, 1.6f);
                }
            }
            // Breathy steam: low-passed noise.
            noiseState += ((float)rng.NextDouble() * 2f - 1f - noiseState) * 0.35f;
            samples[i] = envelope * (tone * 0.22f + noiseState * 0.35f);
        }
        return Normalize(samples, 0.8f);
    }

    /// <summary>Fire breath: a rumbling roar that swells and crackles.</summary>
    private static float[] SynthesizeWhoosh()
    {
        int length = (int)(2.4f * SampleRate);
        var samples = new float[length];
        var rng = new System.Random(1993);
        float low = 0f, band = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / SampleRate;
            float envelope = Mathf.Clamp01(t / 0.25f) * Mathf.Clamp01((2.4f - t) / 0.6f);
            float white = (float)rng.NextDouble() * 2f - 1f;
            low += (white - low) * 0.04f;          // deep rumble
            band += (white - band) * 0.25f;        // hiss
            float crackle = rng.NextDouble() < 0.0015 ? ((float)rng.NextDouble() * 2f - 1f) * 3f : 0f;
            samples[i] = envelope * (low * 2.2f + (band - low) * 0.5f + crackle);
        }
        return Normalize(samples, 0.85f);
    }

    private static float[] Normalize(float[] samples, float peak)
    {
        float max = 0.0001f;
        foreach (float s in samples)
        {
            max = Mathf.Max(max, Mathf.Abs(s));
        }
        float gain = peak / max;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] *= gain;
        }
        return samples;
    }
}

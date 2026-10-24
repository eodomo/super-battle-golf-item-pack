using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The sniper's sounds. If the player put replacement files in the item's folder (sniper_fire,
/// sniper_zoom_in, sniper_zoom_out, sniper_reload; .ogg/.wav/.mp3/.flac) those are used. Otherwise the
/// stand-ins synthesized here: a supersonic crack with a deep boom and a long echo, scope clicks, and the
/// clacks of a magazine change. Nothing copyrighted ships with the pack.
/// </summary>
internal static class SniperSounds
{
    private const int SampleRate = 44100;

    private static FMOD.Sound fire, zoomIn, zoomOut, reload;
    private static bool loaded;

    public static FMOD.Sound Fire { get { Load(); return fire; } }
    public static FMOD.Sound ZoomIn { get { Load(); return zoomIn; } }
    public static FMOD.Sound ZoomOut { get { Load(); return zoomOut; } }
    public static FMOD.Sound Reload { get { Load(); return reload; } }

    private static void Load()
    {
        if (loaded)
        {
            return;
        }
        loaded = true;
        string dir = SniperRifleItem.Instance.AssetDir;
        if (!ItemAudio.TryLoadOptional(dir, "sniper_fire", false, out fire))
        {
            fire = ItemAudio.CreateSound(SynthesizeFire(), SampleRate);
        }
        if (!ItemAudio.TryLoadOptional(dir, "sniper_zoom_in", false, out zoomIn))
        {
            zoomIn = ItemAudio.CreateSound(SynthesizeZoom(rising: true), SampleRate);
        }
        if (!ItemAudio.TryLoadOptional(dir, "sniper_zoom_out", false, out zoomOut))
        {
            zoomOut = ItemAudio.CreateSound(SynthesizeZoom(rising: false), SampleRate);
        }
        if (!ItemAudio.TryLoadOptional(dir, "sniper_reload", false, out reload))
        {
            reload = ItemAudio.CreateSound(SynthesizeReload(), SampleRate);
        }
    }

    private static float[] SynthesizeFire()
    {
        int length = (int)(2.4f * SampleRate);
        var dry = new float[length];
        var rng = new System.Random(99);
        float low = 0f, band = 0f, prev = 0f;
        double phase = 0;
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / SampleRate;
            float white = (float)rng.NextDouble() * 2f - 1f;
            low += (white - low) * 0.02f;
            band += (white - band) * 0.3f;
            float high = white - prev;
            prev = white;

            float crack = high * Mathf.Exp(-t / 0.004f) * 1.6f;                  // supersonic snap
            float freq = Mathf.Lerp(38f, 140f, Mathf.Exp(-t / 0.05f));            // pitch-dropping thump
            phase += 2 * Mathf.PI * freq / SampleRate;
            float boom = Mathf.Sin((float)phase) * Mathf.Exp(-t / 0.22f) * 1.4f;
            float blast = band * Mathf.Exp(-t / 0.06f) * 0.9f;                     // muzzle blast
            float rumble = low * 6f * Mathf.Exp(-t / 0.5f);                        // rolling low end
            dry[i] = crack + boom + blast + rumble;
        }
        // Echoes off distant terrain.
        var samples = new float[length];
        (float delay, float gain)[] echoes = { (0f, 1f), (0.11f, 0.35f), (0.29f, 0.22f), (0.52f, 0.14f), (0.86f, 0.08f) };
        foreach ((float delay, float gain) in echoes)
        {
            int offset = (int)(delay * SampleRate);
            float smooth = 0f;
            for (int i = 0; i + offset < length; i++)
            {
                smooth += (dry[i] - smooth) * (delay == 0f ? 1f : 0.15f); // later echoes are duller
                samples[i + offset] += smooth * gain;
            }
        }
        // Soft-clip it hot: the snap no longer sets the level on its own, so the boom and echoes come up with it.
        Normalize(samples, 1f);
        for (int i = 0; i < length; i++)
        {
            samples[i] = (float)System.Math.Tanh(samples[i] * 3f);
        }
        return Normalize(samples, 0.95f);
    }

    private static float[] SynthesizeZoom(bool rising)
    {
        int length = (int)(0.16f * SampleRate);
        var samples = new float[length];
        var rng = new System.Random(rising ? 7 : 8);
        double phase = 0;
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / SampleRate;
            float u = t / 0.16f;
            float freq = rising ? Mathf.Lerp(700f, 1800f, u) : Mathf.Lerp(1800f, 700f, u);
            phase += 2 * Mathf.PI * freq / SampleRate;
            float whirr = Mathf.Sin((float)phase) * 0.25f * Mathf.Sin(Mathf.PI * u);
            float click = Click(t, 0f, rng) + Click(t, 0.13f, rng) * 0.6f;
            samples[i] = whirr + click;
        }
        return Normalize(samples, 0.6f);
    }

    private static float[] SynthesizeReload()
    {
        int length = (int)(2.0f * SampleRate);
        var samples = new float[length];
        var rng = new System.Random(4);
        float slide = 0f;
        for (int i = 0; i < length; i++)
        {
            float t = (float)i / SampleRate;
            float s = Click(t, 0.12f, rng) * 0.8f          // magazine release
                + Click(t, 0.95f, rng) * 1.2f              // new magazine seated
                + Click(t, 1.02f, rng) * 0.5f
                + Click(t, 1.75f, rng) * 1.3f;             // bolt slams home
            if (t > 1.4f && t < 1.7f)
            {
                // bolt pulled back: a short scrape
                float white = (float)rng.NextDouble() * 2f - 1f;
                slide += (white - slide) * 0.4f;
                s += slide * 0.35f * Mathf.Sin(Mathf.PI * (t - 1.4f) / 0.3f);
            }
            samples[i] = s;
        }
        return Normalize(samples, 0.7f);
    }

    /// <summary>A short metallic click starting at <paramref name="at"/>.</summary>
    private static float Click(float t, float at, System.Random rng)
    {
        float dt = t - at;
        if (dt < 0f || dt > 0.03f)
        {
            return 0f;
        }
        float ring = Mathf.Sin(2f * Mathf.PI * 3200f * dt) * 0.6f + Mathf.Sin(2f * Mathf.PI * 5100f * dt) * 0.3f;
        float noise = (float)rng.NextDouble() * 2f - 1f;
        return (ring + noise * 0.5f) * Mathf.Exp(-dt / 0.005f);
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

using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The lightsaber's sounds, synthesized (no audio files): the steady hum, igniting, the "vwoom" of a swing and the
/// sizzle of a cut. Players can drop in their own lightsaber_hum/ignite/swing/cut .ogg/.wav files to replace them.
/// </summary>
internal static class SaberSounds
{
    private static FMOD.Sound hum, ignite, swing, turn, cut;
    private static bool loaded;

    public static FMOD.Sound Hum { get { Load(); return hum; } }
    public static FMOD.Sound Turn { get { Load(); return turn; } }
    public static FMOD.Sound Ignite { get { Load(); return ignite; } }
    public static FMOD.Sound Swing { get { Load(); return swing; } }
    public static FMOD.Sound Cut { get { Load(); return cut; } }

    private static void Load()
    {
        if (loaded)
        {
            return;
        }
        loaded = true;
        CustomItem item = LightsaberItem.Primary;
        hum = ItemAudio.LoadOrSynthesize(item, "lightsaber_hum", SynthHum, loop: true);
        ignite = ItemAudio.LoadOrSynthesize(item, "lightsaber_ignite", SynthIgnite);
        swing = ItemAudio.LoadOrSynthesize(item, "lightsaber_swing", SynthSwing);
        cut = ItemAudio.LoadOrSynthesize(item, "lightsaber_cut", SynthCut);
        turn = ItemAudio.LoadOrSynthesize(item, "lightsaber_turn", SynthTurn);
    }

    // The soft whoosh of turning with the blade: a short breath of air with a faint pitch bend in the hum.
    private static float[] SynthTurn()
    {
        var s = new Synth(0.4f, seed: 45);
        System.Func<float, float> bend = t => 1f + 0.35f * Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI);
        s.Tone(t => 92f * bend(t), Synth.Env(0.08f, 0.04f, 0.2f), gain: 0.5f, harmonics: 3);
        s.Noise(Synth.Env(0.09f, 0.03f, 0.22f), t => 0.1f + 0.2f * Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI), gain: 0.5f, band: true);
        return s.Normalized(0.6f);
    }

    // A low buzzing drone: two slightly detuned buzzy tones beating against each other, plus a little hiss.
    // Exactly 2 s with whole cycles of every partial, so it loops without a click.
    private static float[] SynthHum()
    {
        var s = new Synth(2f, seed: 41);
        s.Tone(_ => 90f, Synth.Const(1f), gain: 0.9f, harmonics: 6);
        s.Tone(_ => 91.5f, Synth.Const(1f), gain: 0.6f, harmonics: 6);
        s.Tone(_ => 180f, Synth.Const(1f), gain: 0.25f, harmonics: 3);
        s.Noise(Synth.Const(1f), Synth.Const(0.08f), gain: 0.08f);
        // Smooth the seam the noise leaves at the loop point.
        int fade = Synth.Rate / 50;
        for (int i = 0; i < fade; i++)
        {
            float w = (float)i / fade;
            int end = s.Samples.Length - fade + i;
            s.Samples[end] = Mathf.Lerp(s.Samples[end], s.Samples[i], w);
        }
        return s.Normalized(0.7f);
    }

    // "Kssh-VMMM": a crackle and a rising buzz that settles into the hum's pitch.
    private static float[] SynthIgnite()
    {
        var s = new Synth(0.9f, seed: 42);
        s.Noise(Synth.Env(0.005f, 0.05f, 0.35f), Synth.Ramp(0.9f, 0.2f, 0.4f), gain: 0.5f);
        s.Tone(t => Mathf.Lerp(40f, 90f, Mathf.Clamp01(t / 0.25f)) + Mathf.Max(0f, 0.25f - t) * 400f, Synth.Env(0.02f, 0.3f, 0.5f), gain: 1f, harmonics: 7);
        s.Tone(t => Mathf.Lerp(60f, 180f, Mathf.Clamp01(t / 0.2f)), Synth.Env(0.02f, 0.15f, 0.4f), gain: 0.4f, harmonics: 4);
        return s.Normalized(0.85f);
    }

    // The swing: the hum's pitch swooping up and back down (the blade rushing past), with a rush of air.
    private static float[] SynthSwing()
    {
        var s = new Synth(0.55f, seed: 43);
        System.Func<float, float> swoop = t => 1f + 0.9f * Mathf.Sin(Mathf.Clamp01(t / 0.45f) * Mathf.PI);
        s.Tone(t => 92f * swoop(t), Synth.Env(0.06f, 0.15f, 0.3f), gain: 1f, harmonics: 6);
        s.Tone(t => 140f * swoop(t), Synth.Env(0.08f, 0.1f, 0.25f), gain: 0.35f, harmonics: 3);
        s.Noise(Synth.Env(0.08f, 0.1f, 0.25f), t => 0.15f + 0.4f * Mathf.Sin(Mathf.Clamp01(t / 0.45f) * Mathf.PI), gain: 0.35f, band: true);
        return s.Normalized(0.85f);
    }

    // The cut: a sharp crackling clash, then a sizzle that fades.
    private static float[] SynthCut()
    {
        var s = new Synth(1.1f, seed: 44);
        s.Click(0f, 1800f, 0.012f, gain: 0.8f, noise: 1f);
        s.Noise(Synth.Env(0.002f, 0.06f, 0.2f), Synth.Const(0.85f), gain: 0.8f);
        s.Noise(Synth.Env(0.01f, 0.2f, 0.8f), Synth.Ramp(0.7f, 0.35f, 1f), gain: 0.35f, band: true); // sizzle
        s.Tone(t => 70f + 60f * Mathf.Exp(-t / 0.08f), Synth.Decay(0.25f), gain: 0.9f, harmonics: 6);
        return s.Normalized(0.9f);
    }
}

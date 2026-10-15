using System;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The web slinger's sounds, synthesized (and replaceable with files named webslinger_thwip and webslinger_snap
/// in the item's folder).
/// </summary>
internal static class SlingerSounds
{
    private static FMOD.Sound thwip;
    private static FMOD.Sound snap;

    public static FMOD.Sound Thwip => thwip.hasHandle() ? thwip : thwip = Load("webslinger_thwip", SynthesizeThwip);
    public static FMOD.Sound Snap => snap.hasHandle() ? snap : snap = Load("webslinger_snap", SynthesizeSnap);

    private static FMOD.Sound Load(string name, Func<float[]> synthesize) =>
        ItemAudio.LoadOrSynthesize(WebSlingerItem.Instance, name, synthesize, false);

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
}

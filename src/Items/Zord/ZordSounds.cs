using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The Zord's sounds, synthesized: a huge footstep and a cry. Players can drop in their own zord_step / zord_cry
/// .ogg/.wav files to replace them.
/// </summary>
internal static class ZordSounds
{
    private static FMOD.Sound step, cry;
    private static bool loaded;

    public static FMOD.Sound Step { get { Load(); return step; } }
    public static FMOD.Sound Cry { get { Load(); return cry; } }

    private static void Load()
    {
        if (loaded)
        {
            return;
        }
        loaded = true;
        CustomItem item = ZordItem.Instance;
        step = ItemAudio.LoadOrSynthesize(item, "zord_step", SynthStep);
        cry = ItemAudio.LoadOrSynthesize(item, "zord_cry", SynthCry);
    }

    public static void Play(FMOD.Sound sound, Vector3 position, float volume)
    {
        float v = ItemAudio.Attenuate(position, volume * ZordItem.SoundVolume.Value, 10f, 90f);
        if (v > 0.01f)
        {
            ItemAudio.Play(sound, ItemAudio.SfxBus, v);
        }
    }

    // A ground-shaking stomp: a very low tone dropping in pitch plus a rumble of noise and a metallic clank.
    private static float[] SynthStep()
    {
        var s = new Synth(0.55f, seed: 81);
        s.Tone(t => Mathf.Lerp(70f, 30f, Mathf.Clamp01(t / 0.18f)), Synth.Decay(0.12f), gain: 1f, harmonics: 2);
        s.Noise(Synth.Decay(0.06f), Synth.Const(0.2f), gain: 0.6f, band: true);
        s.Tone(t => 310f, Synth.Decay(0.03f), gain: 0.25f, harmonics: 3);
        return s.Normalized(0.85f);
    }

    // A war cry: a rising electronic power-up sweep with a metallic clang and a rumble underneath.
    private static float[] SynthCry()
    {
        var s = new Synth(2.4f, seed: 82);
        System.Func<float, float> swell = Synth.Env(0.05f, 0.9f, 0.8f);
        s.Tone(t => 180f + 520f * Mathf.Clamp01(t / 0.9f), swell, gain: 0.8f, harmonics: 4);
        s.Tone(t => 90f + 260f * Mathf.Clamp01(t / 0.9f) + 10f * Mathf.Sin(t * 60f), swell, gain: 0.7f, harmonics: 3);
        s.Tone(t => 880f, Synth.Decay(0.35f), gain: 0.3f, harmonics: 5, start: 0.9f);
        s.Noise(Synth.Decay(0.12f), Synth.Const(0.4f), gain: 0.5f, band: true, start: 0.9f);
        return s.Normalized(0.9f);
    }
}

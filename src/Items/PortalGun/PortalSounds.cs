using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The portal gun's shot: a "wooshy" rush of air that brightens as it leaves the gun, with a soft rising whistle in it
/// and a little echo. Blue shots are pitched a bit higher than orange. A "portal_shot" .ogg/.wav file in the item's
/// folder replaces it.
/// </summary>
internal static class PortalSounds
{
    private static FMOD.Sound shot;

    public static void PlayShot(Vector3 position, byte color)
    {
        if (!shot.hasHandle())
        {
            shot = ItemAudio.LoadOrSynthesize(PortalGunItem.Instance, "portal_shot", SynthesizeShot);
        }
        FMOD.Channel channel = ItemAudio.Play(shot, ItemAudio.SfxBus, ItemAudio.Attenuate(position, PortalGunItem.SoundVolume.Value, 10f, 70f));
        ItemAudio.SetPitch(channel, color == 0 ? 1.12f : 0.9f);
    }

    private static float[] SynthesizeShot()
    {
        var s = new Synth(0.9f, seed: 77);
        // The rush: air that starts dull and opens up, then trails off.
        s.Noise(Synth.Env(0.04f, 0.05f, 0.4f), Synth.Ramp(0.04f, 0.5f, 0.35f), gain: 1.4f);
        s.Noise(Synth.Env(0.06f, 0.02f, 0.3f), Synth.Ramp(0.1f, 0.7f, 0.3f), gain: 0.5f, band: true);
        // A soft whistle rising through it, with a slight warble.
        s.Tone(t => Mathf.Lerp(240f, 820f, Mathf.Clamp01(t / 0.3f)) * (1f + 0.02f * Mathf.Sin(t * 60f)), Synth.Env(0.03f, 0.05f, 0.3f), gain: 0.28f, harmonics: 2);
        s.Tone(t => Mathf.Lerp(120f, 410f, Mathf.Clamp01(t / 0.3f)), Synth.Env(0.03f, 0.05f, 0.3f), gain: 0.2f);
        s.Echo(0.08f, 0.35f, 3);
        return s.Normalized(0.8f);
    }
}

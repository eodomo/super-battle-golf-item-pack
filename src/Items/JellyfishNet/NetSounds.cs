using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Jellyfish net sounds (or the player's own jellyfishnet_swing / jellyfishnet_release files): a quick airy swoosh as the
/// net sweeps, and a longer, deeper whoosh as someone is flung out of it.
/// </summary>
internal static class NetSounds
{
    private static FMOD.Sound swing, release;

    public static void PlaySwing(Vector3 position)
    {
        if (!swing.hasHandle())
        {
            // Rises as the net speeds up through the sweep, then trails off.
            swing = ItemAudio.LoadOrSynthesize(JellyfishNetItem.Instance, "jellyfishnet_swing", () =>
                new Synth(0.45f, 118)
                    .Noise(Synth.Env(0.12f, 0.05f, 0.2f), t => Mathf.Lerp(0.08f, 0.55f, Mathf.Sin(Mathf.Clamp01(t / 0.3f) * Mathf.PI)), 1f, band: true)
                    .Noise(Synth.Env(0.1f, 0.05f, 0.15f), Synth.Const(0.9f), 0.12f)
                    .Normalized(0.75f));
        }
        Play(swing, position);
    }

    public static void PlayRelease(Vector3 position)
    {
        if (!release.hasHandle())
        {
            release = ItemAudio.LoadOrSynthesize(JellyfishNetItem.Instance, "jellyfishnet_release", () =>
                new Synth(0.7f, 119)
                    .Noise(Synth.Env(0.04f, 0.12f, 0.4f), Synth.Ramp(0.45f, 0.06f, 0.6f), 1f, band: true)
                    .Tone(Synth.Ramp(260f, 90f, 0.35f), Synth.Decay(0.12f), 0.25f)   // the net's snap as it lets go
                    .Normalized(0.8f));
        }
        Play(release, position);
    }

    private static void Play(FMOD.Sound sound, Vector3 position)
    {
        float volume = ItemAudio.Attenuate(position, JellyfishNetItem.SoundVolume.Value, 10f, 60f);
        if (volume > 0.01f)
        {
            ItemAudio.Play(sound, ItemAudio.SfxBus, volume);
        }
    }
}

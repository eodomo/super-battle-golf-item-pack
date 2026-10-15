using System;
using System.Collections.Generic;
using FMODUnity;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// Plays the pack's own sounds through the game's FMOD audio (the game doesn't use Unity's audio), routed
/// into the game's buses so its SFX and Music volume sliders apply.
/// </summary>
internal static class ItemAudio
{
    public const string SfxBus = "bus:/SFX";
    public const string MusicBus = "bus:/Music";

    private static readonly Dictionary<string, FMOD.ChannelGroup> busGroups = new();

    /// <summary>Makes an in-memory sound from mono float samples (-1..1).</summary>
    public static FMOD.Sound CreateSound(float[] samples, int sampleRate, bool loop = false)
    {
        var bytes = new byte[samples.Length * sizeof(float)];
        Buffer.BlockCopy(samples, 0, bytes, 0, bytes.Length);
        var info = new FMOD.CREATESOUNDEXINFO
        {
            length = (uint)bytes.Length,
            numchannels = 1,
            defaultfrequency = sampleRate,
            format = FMOD.SOUND_FORMAT.PCMFLOAT,
        };
        info.cbsize = System.Runtime.InteropServices.Marshal.SizeOf(info);
        FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(bytes,
            FMOD.MODE.OPENMEMORY | FMOD.MODE.OPENRAW | FMOD.MODE.CREATESAMPLE | FMOD.MODE._2D | (loop ? FMOD.MODE.LOOP_NORMAL : FMOD.MODE.LOOP_OFF),
            ref info, out FMOD.Sound sound);
        if (result != FMOD.RESULT.OK)
        {
            Plugin.Log.LogWarning($"FMOD createSound (memory) failed: {result}");
        }
        return sound;
    }

    private static readonly string[] AudioExtensions = { ".ogg", ".wav", ".mp3", ".flac" };

    /// <summary>
    /// Loads "{baseName}.ogg/.wav/.mp3/.flac" from <paramref name="directory"/> if the player put one there
    /// (used for optional sound replacements that can't ship with the pack).
    /// </summary>
    public static bool TryLoadOptional(string directory, string baseName, bool stream, out FMOD.Sound sound, bool loop = false)
    {
        foreach (string extension in AudioExtensions)
        {
            string path = System.IO.Path.Combine(directory, baseName + extension);
            if (System.IO.File.Exists(path) && TryLoadFile(path, out sound, stream, loop))
            {
                Plugin.Log.LogInfo($"Using sound file {path}");
                return true;
            }
        }
        sound = default;
        return false;
    }

    /// <summary>
    /// Loads an audio file (ogg, mp3, wav, flac...) from disk. Streams (for music) play one instance at a
    /// time; samples (for effects) can overlap.
    /// </summary>
    public static bool TryLoadFile(string path, out FMOD.Sound sound, bool stream = true, bool loop = false)
    {
        FMOD.MODE mode = (stream ? FMOD.MODE.CREATESTREAM : FMOD.MODE.CREATESAMPLE) | FMOD.MODE._2D | (loop ? FMOD.MODE.LOOP_NORMAL : FMOD.MODE.LOOP_OFF);
        FMOD.RESULT result = RuntimeManager.CoreSystem.createSound(path, mode, out sound);
        if (result != FMOD.RESULT.OK)
        {
            Plugin.Log.LogWarning($"Couldn't load sound {path}: {result}");
            return false;
        }
        return true;
    }

    public static FMOD.Channel Play(FMOD.Sound sound, string bus, float volume)
    {
        if (!sound.hasHandle())
        {
            return default;
        }
        FMOD.RESULT result = RuntimeManager.CoreSystem.playSound(sound, GetBusGroup(bus), true, out FMOD.Channel channel);
        if (result != FMOD.RESULT.OK)
        {
            Plugin.Log.LogWarning($"FMOD playSound failed: {result}");
            return default;
        }
        channel.setVolume(volume);
        channel.setPaused(false);
        return channel;
    }

    /// <summary>Volume for a sound at <paramref name="position"/>, fading out with distance from the camera.</summary>
    public static float Attenuate(Vector3 position, float volume, float fullVolumeDistance, float silentDistance)
    {
        Camera cam = GameManager.Camera;
        if (cam == null)
        {
            return volume;
        }
        float distance = Vector3.Distance(cam.transform.position, position);
        return volume * (1f - Mathf.Clamp01((distance - fullVolumeDistance) / (silentDistance - fullVolumeDistance)));
    }

    /// <summary>Playback speed and pitch (1 = normal).</summary>
    public static void SetPitch(FMOD.Channel channel, float pitch)
    {
        if (channel.hasHandle())
        {
            channel.setPitch(pitch);
        }
    }

    /// <summary>
    /// An item sound: "{name}.ogg/.wav/.mp3/.flac" from the item's folder if the player added one, otherwise
    /// synthesized by <paramref name="synthesize"/> (mono samples at <paramref name="sampleRate"/>).
    /// </summary>
    public static FMOD.Sound LoadOrSynthesize(CustomItem item, string name, System.Func<float[]> synthesize, bool loop = false, int sampleRate = 44100)
    {
        if (TryLoadOptional(item.AssetDir, name, stream: false, out FMOD.Sound sound, loop))
        {
            return sound;
        }
        return CreateSound(synthesize(), sampleRate, loop);
    }

    public static void SetVolume(FMOD.Channel channel, float volume)
    {
        if (channel.hasHandle())
        {
            channel.setVolume(volume);
        }
    }

    public static void Stop(FMOD.Channel channel)
    {
        if (channel.hasHandle())
        {
            channel.stop();
        }
    }

    private static FMOD.ChannelGroup GetBusGroup(string path)
    {
        if (busGroups.TryGetValue(path, out FMOD.ChannelGroup cached) && cached.hasHandle())
        {
            return cached;
        }
        var group = default(FMOD.ChannelGroup);
        try
        {
            FMOD.Studio.Bus bus = RuntimeManager.GetBus(path);
            bus.lockChannelGroup();
            RuntimeManager.StudioSystem.flushCommands();
            if (bus.getChannelGroup(out group) != FMOD.RESULT.OK)
            {
                group = default;
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"No FMOD bus {path}; playing on the master channel group. ({e.Message})");
        }
        busGroups[path] = group;
        return group;
    }
}

using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack.Items;

/// <summary>The energy ball while charging, the beam, and their sounds, on every client.</summary>
internal static class KameEffects
{
    private static readonly Color Outer = new(0.25f, 0.55f, 1f, 0.85f);
    private static readonly Color Core = new(0.85f, 0.95f, 1f, 1f);

    private class Charge
    {
        public PlayerInfo player;
        public GameObject orb;
        public Light light;
        public float start;
        public FMOD.Channel sound;
    }

    /// <summary>One straight piece of a beam (a beam that goes through portals has several).</summary>
    private class Piece
    {
        public GameObject root;
        public Transform outer;
        public Transform core;
        public Vector3 origin;
        public Vector3 direction;
        public float offset;   // how far along the whole beam this piece starts
        public float length;
    }

    private class Beam
    {
        public List<Piece> pieces = new();
        public Transform endBall;
        public float start;
        public float length;
        public float radius;

        public GameObject Root => pieces[0].root;
    }

    private static readonly Dictionary<uint, Charge> charges = new();
    private static readonly List<Beam> beams = new();
    private static readonly List<uint> ended = new();
    private static Material outerMaterial, coreMaterial;
    private static FMOD.Sound chargeSound, fireSound;
    private static bool chargeIsVoice;

    private const float BeamGrow = 0.15f;
    private const float BeamHold = 0.9f;
    private const float BeamFade = 0.45f;

    // ---- charging ----

    public static void SetCharging(uint playerNetId, bool charging)
    {
        if (!charging)
        {
            StopCharging(playerNetId);
            return;
        }
        if (charges.ContainsKey(playerNetId) || !NetworkClient.spawned.TryGetValue(playerNetId, out NetworkIdentity identity) || identity == null
            || !identity.TryGetComponent(out PlayerInfo player))
        {
            return;
        }
        LoadSounds();
        var orb = Primitive(PrimitiveType.Sphere, "KameOrb", OuterMaterial);
        var core = Primitive(PrimitiveType.Sphere, "Core", CoreMaterial);
        core.transform.SetParent(orb.transform, false);
        core.transform.localScale = Vector3.one * 0.55f;
        var light = orb.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.4f, 0.65f, 1f);
        light.range = 4f;
        light.intensity = 0f;
        var charge = new Charge { player = player, orb = orb, light = light, start = Time.time };
        float volume = Volume(player.transform.position);
        charge.sound = ItemAudio.Play(chargeSound, ItemAudio.SfxBus, volume);
        charges[playerNetId] = charge;
    }

    public static void StopCharging(uint playerNetId)
    {
        if (charges.TryGetValue(playerNetId, out Charge charge))
        {
            if (!chargeIsVoice)
            {
                ItemAudio.Stop(charge.sound); // a voice line keeps going into the shout
            }
            if (charge.orb != null)
            {
                Object.Destroy(charge.orb);
            }
            charges.Remove(playerNetId);
        }
    }

    /// <summary>Where the energy gathers: in front of the player's chest.</summary>
    public static Vector3 HandsPosition(PlayerInfo player)
    {
        Transform chest = player.ChestBone != null ? player.ChestBone : player.transform;
        Vector3 basePosition = player.ChestBone != null ? chest.position : player.transform.position + Vector3.up * 1.1f;
        return basePosition + player.transform.forward * 0.55f;
    }

    // ---- beam ----

    public static void Fire(uint playerNetId, Vector3 origin, Vector3 direction, float length, float radius)
    {
        StopCharging(playerNetId);
        LoadSounds();
        ItemAudio.Play(fireSound, ItemAudio.SfxBus, Volume(origin));

        var beam = new Beam { start = Time.time, length = length, radius = radius };
        // Through portals the beam comes out of each exit: work out the pieces the same way the shooter did.
        float offset = 0f;
        foreach (PortalPaths.Segment segment in PortalPaths.Trace(origin, direction, length, 0, out _))
        {
            var root = new GameObject("Kamehameha");
            root.transform.SetPositionAndRotation(segment.origin, Quaternion.LookRotation(segment.direction));
            beam.pieces.Add(new Piece
            {
                root = root, outer = Cylinder(root.transform, OuterMaterial), core = Cylinder(root.transform, CoreMaterial),
                origin = segment.origin, direction = segment.direction, offset = offset, length = segment.length,
            });
            offset += segment.length;
        }
        GameObject first = beam.Root;
        var startBall = Primitive(PrimitiveType.Sphere, "StartBall", OuterMaterial);
        startBall.transform.SetParent(first.transform, false);
        startBall.transform.localScale = Vector3.one * (radius * 2.6f);
        beam.endBall = Primitive(PrimitiveType.Sphere, "EndBall", OuterMaterial).transform;
        beam.endBall.SetParent(first.transform, false);
        var light = first.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.45f, 0.7f, 1f);
        light.range = 12f + radius * 4f;
        light.intensity = 6f;
        beams.Add(beam);
        UpdateBeam(beam, 0f);
        CameraModuleController.Shake(GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings, origin, 0.6f);
    }

    public static void Tick()
    {
        float now = Time.time;
        foreach (KeyValuePair<uint, Charge> entry in charges)
        {
            Charge charge = entry.Value;
            if (charge.player == null || charge.orb == null)
            {
                ended.Add(entry.Key);
                continue;
            }
            float t = Mathf.Clamp01((now - charge.start) / KamehamehaItem.MaxChargeSeconds.Value);
            float pulse = 1f + Mathf.Sin(now * 30f) * 0.08f;
            charge.orb.transform.position = HandsPosition(charge.player);
            charge.orb.transform.localScale = Vector3.one * Mathf.Lerp(0.12f, 0.7f, t) * pulse;
            charge.light.intensity = Mathf.Lerp(0.5f, 5f, t);
            if (!chargeIsVoice)
            {
                ItemAudio.SetPitch(charge.sound, Mathf.Lerp(0.8f, 1.7f, t));
                ItemAudio.SetVolume(charge.sound, Volume(charge.player.transform.position));
            }
        }
        foreach (uint id in ended)
        {
            StopCharging(id);
        }
        ended.Clear();

        for (int i = beams.Count - 1; i >= 0; i--)
        {
            Beam beam = beams[i];
            float age = now - beam.start;
            if (beam.Root == null || age > BeamGrow + BeamHold + BeamFade)
            {
                DestroyBeam(beam);
                beams.RemoveAt(i);
                continue;
            }
            UpdateBeam(beam, age);
        }
    }

    public static void ClearAll()
    {
        foreach (uint id in new List<uint>(charges.Keys))
        {
            StopCharging(id);
        }
        foreach (Beam beam in beams)
        {
            DestroyBeam(beam);
        }
        beams.Clear();
    }

    private static void DestroyBeam(Beam beam)
    {
        foreach (Piece piece in beam.pieces)
        {
            if (piece.root != null)
            {
                Object.Destroy(piece.root);
            }
        }
    }

    private static void UpdateBeam(Beam beam, float age)
    {
        float reach = beam.length * Mathf.Clamp01(age / BeamGrow);
        float fade = age < BeamGrow + BeamHold ? 1f : 1f - (age - BeamGrow - BeamHold) / BeamFade;
        float wobble = 1f + Mathf.Sin(age * 40f) * 0.06f;
        float r = beam.radius * Mathf.Clamp01(fade) * wobble;
        Vector3 head = beam.pieces[0].origin;
        foreach (Piece piece in beam.pieces)
        {
            // How much of this piece the beam has grown into.
            float pieceReach = Mathf.Clamp(reach - piece.offset, 0f, piece.length);
            // Unity's cylinder is 2 units tall along Y: rotate it onto Z, scale Y to half the length.
            piece.outer.localPosition = new Vector3(0f, 0f, pieceReach * 0.5f);
            piece.outer.localScale = new Vector3(r * 2f, pieceReach * 0.5f, r * 2f);
            piece.core.localPosition = piece.outer.localPosition;
            piece.core.localScale = new Vector3(r * 0.9f, pieceReach * 0.5f + 0.01f, r * 0.9f);
            if (reach >= piece.offset)
            {
                head = piece.origin + piece.direction * pieceReach;
            }
        }
        beam.endBall.position = head;
        beam.endBall.localScale = Vector3.one * (r * 2.4f);
    }

    private static Transform Cylinder(Transform parent, Material material)
    {
        GameObject go = Primitive(PrimitiveType.Cylinder, "Beam", material);
        go.transform.SetParent(parent, false);
        go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        return go.transform;
    }

    private static GameObject Primitive(PrimitiveType type, string name, Material material)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        Object.Destroy(go.GetComponent<Collider>());
        go.name = name;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return go;
    }

    private static Material OuterMaterial => outerMaterial ??= UnlitMaterials.Additive(null, Outer);
    private static Material CoreMaterial => coreMaterial ??= UnlitMaterials.Opaque(null, Core * 1.4f);

    private static float Volume(Vector3 position) =>
        Mathf.Max(ItemAudio.Attenuate(position, KamehamehaItem.SoundVolume.Value, 15f, 150f), KamehamehaItem.SoundVolume.Value * 0.25f);

    // ---- sounds ----

    /// <summary>
    /// "kamehameha_charge" / "kamehameha_fire" files from the item folder (e.g. the voice lines), otherwise
    /// a synthesized rising energy hum and a roaring blast.
    /// </summary>
    private static void LoadSounds()
    {
        if (!chargeSound.hasHandle())
        {
            chargeIsVoice = ItemAudio.TryLoadOptional(KamehamehaItem.Instance.AssetDir, "kamehameha_charge", stream: false, out chargeSound);
            if (!chargeIsVoice)
            {
                chargeSound = ItemAudio.CreateSound(new Synth(2f, 21)
                    .Tone(_ => 110f, Synth.Const(1f), 0.7f, harmonics: 5)
                    .Tone(t => 220f + Mathf.Sin(t * 2f * Mathf.PI * 6f) * 6f, Synth.Const(1f), 0.35f, harmonics: 3)
                    .Tone(_ => 880f, Synth.Const(1f), 0.12f)
                    .Noise(Synth.Const(1f), Synth.Const(0.35f), 0.25f, band: true)
                    .Normalized(0.6f), Synth.Rate, loop: true);
            }
        }
        if (!fireSound.hasHandle())
        {
            fireSound = ItemAudio.LoadOrSynthesize(KamehamehaItem.Instance, "kamehameha_fire", () =>
                new Synth(2.4f, 22)
                    .Noise(Synth.Env(0.02f, 1.1f, 1.2f), t => Mathf.Lerp(0.5f, 0.12f, Mathf.Clamp01(t / 1.5f)), 1.2f, band: true)
                    .Noise(Synth.Env(0.05f, 1.2f, 1f), Synth.Const(0.02f), 3f)
                    .Tone(t => Mathf.Lerp(320f, 140f, Mathf.Clamp01(t / 1.8f)), Synth.Env(0.03f, 1f, 1f), 0.6f, harmonics: 6)
                    .Tone(_ => 1250f, Synth.Env(0.01f, 0.8f, 0.8f), 0.1f)
                    .Normalized(0.95f));
        }
    }
}

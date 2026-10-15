using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>Other players' missiles: the model with flame and smoke, smoothed between position updates, and its roar.</summary>
internal static class PredatorRemote
{
    private class Missile
    {
        public GameObject root;
        public Vector3 targetPosition;
        public Vector3 velocity;
        public Quaternion targetRotation;
        public FMOD.Channel loop;
        public ParticleSystem smoke;
    }

    private static readonly Dictionary<int, Missile> missiles = new();
    private static readonly List<int> stale = new();
    private static Material smokeMaterial;
    private static bool sceneHookInstalled;

    public static void Spawn(int id, Vector3 position, Vector3 direction)
    {
        EnsureSceneHook();
        if (missiles.ContainsKey(id))
        {
            return;
        }
        var root = new GameObject($"PredatorMissile_{id}");
        root.transform.SetPositionAndRotation(position, Quaternion.LookRotation(direction));
        ModelAsset model = PredatorItem.MissileModel;
        if (model?.Mesh != null)
        {
            model.InitMaterials(ModelAsset.SharedTemplate);
            model.CreateModel(root.transform, PredatorItem.MissileScale.Value);
        }
        // Exhaust: a flame pointing backwards plus a smoke trail left hanging in the air.
        ParticleSystem flame = FireEffects.CreateBreath(root.transform, new Vector3(0f, 0f, -1.4f * PredatorItem.MissileScale.Value), 0.35f);
        flame.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        flame.Play();
        var missile = new Missile
        {
            root = root,
            targetPosition = position,
            targetRotation = root.transform.rotation,
            velocity = direction * PredatorItem.StartSpeed.Value,
            smoke = CreateSmoke(root.transform),
        };
        missile.loop = PredatorSounds.StartLoop(Volume(position));
        PredatorSounds.PlayLaunch(Volume(position) * 0.6f);
        missiles[id] = missile;
    }

    public static void UpdateMissile(int id, Vector3 position, Vector3 direction, bool boosted)
    {
        if (!missiles.TryGetValue(id, out Missile missile))
        {
            Spawn(id, position, direction);
            return;
        }
        missile.velocity = direction * (boosted ? PredatorItem.BoostSpeed.Value : PredatorItem.CruiseSpeed.Value);
        missile.targetPosition = position;
        missile.targetRotation = Quaternion.LookRotation(direction);
    }

    public static void Explode(int id)
    {
        if (!missiles.TryGetValue(id, out Missile missile))
        {
            return;
        }
        ItemAudio.Stop(missile.loop);
        if (missile.smoke != null)
        {
            // Leave the smoke trail hanging, then let it clean itself up.
            missile.smoke.transform.SetParent(null, true);
            missile.smoke.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            var main = missile.smoke.main;
            main.stopAction = ParticleSystemStopAction.Destroy;
        }
        if (missile.root != null)
        {
            Object.Destroy(missile.root);
        }
        missiles.Remove(id);
    }

    public static void Tick()
    {
        float dt = Time.deltaTime;
        foreach (KeyValuePair<int, Missile> entry in missiles)
        {
            Missile missile = entry.Value;
            if (missile.root == null)
            {
                stale.Add(entry.Key);
                continue;
            }
            // Keep flying between updates, easing toward the latest reported position.
            missile.targetPosition += missile.velocity * dt;
            Transform t = missile.root.transform;
            t.position = Vector3.Lerp(t.position + missile.velocity * dt, missile.targetPosition, 1f - Mathf.Exp(-10f * dt));
            t.rotation = Quaternion.Slerp(t.rotation, missile.targetRotation, 1f - Mathf.Exp(-10f * dt));
            ItemAudio.SetVolume(missile.loop, Volume(t.position));
        }
        foreach (int id in stale)
        {
            Explode(id);
        }
        stale.Clear();
    }

    public static void ClearAll()
    {
        foreach (int id in new List<int>(missiles.Keys))
        {
            Explode(id);
        }
    }

    private static float Volume(Vector3 position) =>
        Mathf.Max(ItemAudio.Attenuate(position, PredatorItem.SoundVolume.Value, 30f, 400f), PredatorItem.SoundVolume.Value * 0.15f);

    private static ParticleSystem CreateSmoke(Transform parent)
    {
        smokeMaterial ??= UnlitMaterials.Particles();
        var go = new GameObject("Smoke");
        go.transform.SetParent(parent, false);
        go.transform.localPosition = new Vector3(0f, 0f, -1.6f * PredatorItem.MissileScale.Value);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.78f, 0.78f), new Color(0.92f, 0.92f, 0.92f));
        main.maxParticles = 800;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.rateOverDistance = 2.5f;
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.2f;
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 3f)));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = smokeMaterial;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        ps.Play();
        return ps;
    }

    private static void EnsureSceneHook()
    {
        if (sceneHookInstalled)
        {
            return;
        }
        sceneHookInstalled = true;
        SceneManager.activeSceneChanged += (_, _) => ClearAll();
    }
}

/// <summary>Missile sounds: ignition, the rocket's roar while flying (looped) and the boost (or predator_*.ogg/wav files).</summary>
internal static class PredatorSounds
{
    private static FMOD.Sound launch, loopSound, boost;

    public static void PlayLaunch(float volume)
    {
        if (!launch.hasHandle())
        {
            launch = ItemAudio.LoadOrSynthesize(PredatorItem.Instance, "predator_launch", () =>
                new Synth(1.6f, 31)
                    .Noise(Synth.Decay(0.08f), Synth.Const(0.3f), 1.4f)                                   // ignition crack
                    .Tone(Synth.Ramp(90f, 40f, 0.4f), Synth.Decay(0.3f), 1.2f)                             // thump
                    .Noise(Synth.Env(0.15f, 0.6f, 0.8f), t => Mathf.Lerp(0.05f, 0.4f, Mathf.Clamp01(t)), 0.9f, band: true) // whoosh
                    .Normalized(0.9f));
        }
        ItemAudio.Play(launch, ItemAudio.SfxBus, volume);
    }

    public static FMOD.Channel StartLoop(float volume)
    {
        if (!loopSound.hasHandle())
        {
            loopSound = ItemAudio.LoadOrSynthesize(PredatorItem.Instance, "predator_loop", () =>
                new Synth(2f, 32)
                    .Noise(Synth.Const(1f), Synth.Const(0.04f), 2.2f)                 // deep rumble
                    .Noise(Synth.Const(1f), Synth.Const(0.45f), 0.35f, band: true)    // hiss
                    .Tone(t => 70f + Mathf.Sin(t * Mathf.PI * 2f) * 3f, Synth.Const(1f), 0.25f, harmonics: 4)
                    .Normalized(0.6f), loop: true);
        }
        return ItemAudio.Play(loopSound, ItemAudio.SfxBus, volume);
    }

    public static void PlayBoost(float volume)
    {
        if (!boost.hasHandle())
        {
            boost = ItemAudio.LoadOrSynthesize(PredatorItem.Instance, "predator_boost", () =>
                new Synth(1.4f, 33)
                    .Noise(Synth.Env(0.03f, 0.5f, 0.8f), Synth.Ramp(0.15f, 0.5f, 0.5f), 1.3f, band: true)
                    .Noise(Synth.Env(0.03f, 0.6f, 0.7f), Synth.Const(0.05f), 1.5f)
                    .Tone(Synth.Ramp(180f, 420f, 0.6f), Synth.Env(0.02f, 0.4f, 0.8f), 0.3f, harmonics: 3)
                    .Normalized(0.9f));
        }
        ItemAudio.Play(boost, ItemAudio.SfxBus, volume);
    }
}

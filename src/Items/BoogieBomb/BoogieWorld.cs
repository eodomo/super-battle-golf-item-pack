using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// Boogie bombs on every client: a spinning disco ball bounces along a path every client computes the same way,
/// pops shortly after it lands, and leaves a disco party (spinning colored lights, confetti, music) for as long
/// as the dancing lasts. The local player dances if they were inside the blast.
/// </summary>
internal static class BoogieWorld
{
    private const float SimStep = 0.01f;
    private const float MaxFlight = 3.5f;
    private const float FuseAfterLanding = 0.6f;

    private class Bomb
    {
        public int id;
        public uint ownerNetId;
        public GameObject go;
        public List<Vector3> path;
        public float explodeAt;
        public float spawnTime;
    }

    private class Party
    {
        public GameObject root;
        public Light[] lights;
        public float start;
        public float until;
        public FMOD.Channel music;
        public Vector3 position;
    }

    private static readonly Color[] DiscoColors =
    {
        new(1f, 0.2f, 0.6f), new(0.2f, 0.9f, 1f), new(1f, 0.85f, 0.15f), new(0.55f, 0.3f, 1f), new(0.3f, 1f, 0.4f),
    };

    private static readonly Dictionary<int, Bomb> bombs = new();
    private static readonly List<int> finished = new();
    private static readonly List<Party> parties = new();
    private static bool sceneHookInstalled;
    private static int groundMask = -1;

    public static void Spawn(int id, uint ownerNetId, Vector3 origin, Vector3 velocity)
    {
        EnsureSceneHook();
        if (bombs.ContainsKey(id))
        {
            return;
        }
        var go = new GameObject($"BoogieBomb_{id}");
        go.transform.position = origin;
        ModelAsset model = BoogieBombItem.Instance.Model;
        if (model?.Mesh != null)
        {
            model.InitMaterials(ModelAsset.SharedTemplate);
            model.CreateModel(go.transform, 1.5f); // a bit bigger than in hand, so you can see it coming
        }
        var bomb = new Bomb { id = id, ownerNetId = ownerNetId, go = go, spawnTime = Time.time };
        bomb.path = ComputePath(origin, velocity, out float landTime);
        bomb.explodeAt = Mathf.Min(landTime + FuseAfterLanding, MaxFlight);
        bombs[id] = bomb;
    }

    public static void Tick()
    {
        float now = Time.time;
        foreach (Bomb bomb in bombs.Values)
        {
            if (bomb.go == null)
            {
                finished.Add(bomb.id);
                continue;
            }
            float t = now - bomb.spawnTime;
            int index = Mathf.Min(Mathf.FloorToInt(t / SimStep), bomb.path.Count - 1);
            int next = Mathf.Min(index + 1, bomb.path.Count - 1);
            Vector3 from = bomb.path[index], to = bomb.path[next];
            bomb.go.transform.position = (to - from).sqrMagnitude > 4f
                ? to // came out of a portal: no sliding across the map in between
                : Vector3.Lerp(from, to, t / SimStep - Mathf.Floor(t / SimStep));
            bomb.go.transform.rotation = Quaternion.Euler(0f, t * 400f, Mathf.Sin(t * 9f) * 12f);
            if (t >= bomb.explodeAt)
            {
                Explode(bomb, bomb.go.transform.position);
                finished.Add(bomb.id);
            }
        }
        foreach (int id in finished)
        {
            if (bombs.TryGetValue(id, out Bomb bomb) && bomb.go != null)
            {
                Object.Destroy(bomb.go);
            }
            bombs.Remove(id);
        }
        finished.Clear();

        for (int i = parties.Count - 1; i >= 0; i--)
        {
            Party party = parties[i];
            if (party.root == null || now >= party.until)
            {
                ItemAudio.Stop(party.music);
                if (party.root != null)
                {
                    Object.Destroy(party.root);
                }
                parties.RemoveAt(i);
                continue;
            }
            float age = now - party.start;
            party.root.transform.rotation = Quaternion.Euler(0f, age * 120f, 0f); // lights swirl around
            for (int l = 0; l < party.lights.Length; l++)
            {
                float beat = Mathf.Repeat(age * 2f + l * 0.25f, 1f); // pulse on the beat (120 bpm)
                party.lights[l].intensity = Mathf.Lerp(7f, 1.5f, beat) * Mathf.Clamp01((party.until - now) / 0.5f);
            }
            ItemAudio.SetVolume(party.music, MusicVolume(party.position) * Mathf.Clamp01((party.until - now) / 0.8f));
        }
        BoogieDance.Tick();
    }

    private static void Explode(Bomb bomb, Vector3 position)
    {
        float radius = BoogieBombItem.Radius.Value;
        float seconds = BoogieBombItem.DanceSeconds.Value;
        BoogieSounds.PlayPop(SoundVolume(position));
        Confetti(position);
        StartParty(position, seconds);

        // Did the blast catch us?
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null || local.Movement == null || !local.Movement.IsVisible || local.IsElectromagnetShieldActive)
        {
            return;
        }
        if (local.netId == bomb.ownerNetId && !BoogieBombItem.AffectThrower.Value)
        {
            return;
        }
        Vector3 center = local.transform.position + Vector3.up * 0.9f;
        if ((center - position).sqrMagnitude <= radius * radius)
        {
            BoogieDance.Start(seconds);
        }
    }

    private static void StartParty(Vector3 position, float seconds)
    {
        var root = new GameObject("BoogieParty");
        root.transform.position = position + Vector3.up * 1.5f;
        var lights = new Light[4];
        for (int i = 0; i < lights.Length; i++)
        {
            var holder = new GameObject($"DiscoLight{i}");
            holder.transform.SetParent(root.transform, false);
            float angle = i * Mathf.PI * 0.5f;
            holder.transform.localPosition = new Vector3(Mathf.Cos(angle) * 2.5f, 1.5f, Mathf.Sin(angle) * 2.5f);
            Light light = holder.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = DiscoColors[i % DiscoColors.Length];
            light.range = BoogieBombItem.Radius.Value * 1.6f;
            light.intensity = 4f;
            lights[i] = light;
        }
        var party = new Party
        {
            root = root,
            lights = lights,
            start = Time.time,
            until = Time.time + seconds,
            position = position,
        };
        party.music = BoogieSounds.StartMusic(MusicVolume(position));
        parties.Add(party);
    }

    private static Material confettiMaterial;

    private static void Confetti(Vector3 position)
    {
        confettiMaterial ??= UnlitMaterials.Particles();
        var go = new GameObject("BoogieConfetti");
        go.transform.position = position + Vector3.up * 0.3f;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.16f);
        main.startRotation3D = true;
        main.gravityModifier = 0.35f;
        main.maxParticles = 300;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var gradient = new Gradient();
        var keys = new GradientColorKey[DiscoColors.Length];
        for (int i = 0; i < DiscoColors.Length; i++)
        {
            keys[i] = new GradientColorKey(DiscoColors[i], i / (float)(DiscoColors.Length - 1));
        }
        gradient.SetKeys(keys, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        main.startColor = new ParticleSystem.MinMaxGradient(gradient) { mode = ParticleSystemGradientMode.RandomColor };
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 220) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Hemisphere;
        shape.radius = 0.4f;
        var rotation = ps.rotationOverLifetime;
        rotation.enabled = true;
        rotation.separateAxes = true;
        rotation.x = new ParticleSystem.MinMaxCurve(-8f, 8f);
        rotation.y = new ParticleSystem.MinMaxCurve(-8f, 8f);
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 1.5f;
        noise.frequency = 0.8f;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = confettiMaterial;
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = QuadMesh;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
    }

    private static Mesh quadMesh;

    private static Mesh QuadMesh
    {
        get
        {
            if (quadMesh == null)
            {
                GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                quadMesh = quad.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(quad);
            }
            return quadMesh;
        }
    }

    /// <summary>Fixed-step bouncing path against static geometry; returns when it settles (or times out).</summary>
    private static List<Vector3> ComputePath(Vector3 start, Vector3 velocity, out float landTime)
    {
        var path = new List<Vector3> { start };
        Vector3 position = start;
        Vector3 v = velocity;
        landTime = -1f;
        int steps = Mathf.CeilToInt(MaxFlight / SimStep);
        int mask = GroundMask;
        Portal lastExit = null; // the portal it just came out of, which it can't go straight back into
        int exitedAt = 0;
        for (int i = 1; i <= steps; i++)
        {
            if (lastExit != null && i - exitedAt > 10)
            {
                lastExit = null;
            }
            v += Physics.gravity * SimStep;
            Vector3 next = position + v * SimStep;
            bool blocked = Physics.Linecast(position, next, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore);
            float stepLength = (next - position).magnitude;
            // Into a portal (before whatever wall it's on): out of the other one, carrying its speed.
            if (stepLength > 0.0001f
                && PortalShots.TryFindEntry(position, (next - position) / stepLength, stepLength, lastExit, out Portal portal, out float t)
                && (!blocked || hit.distance >= t - 0.1f))
            {
                Vector3 direction = (next - position) / stepLength;
                v = portal.MapDirection(v);
                position = portal.MapPoint(position + direction * t) + v.normalized * 0.05f;
                lastExit = portal.Partner;
                exitedAt = i;
                path.Add(position);
                continue;
            }
            if (blocked)
            {
                if (landTime < 0f && hit.normal.y > 0.5f)
                {
                    landTime = i * SimStep;
                }
                // A little bouncy; on the ground it mostly stops sliding, so it ends up about where it landed.
                Vector3 into = Vector3.Project(v, hit.normal);
                Vector3 along = v - into;
                v = -into * 0.35f + along * (hit.normal.y > 0.5f ? 0.2f : 0.5f);
                position = hit.point + hit.normal * 0.15f;
                if (v.magnitude < 0.6f && hit.normal.y > 0.5f)
                {
                    v = Vector3.zero; // settled
                }
            }
            else
            {
                position = next;
            }
            path.Add(position);
            if (v == Vector3.zero && landTime >= 0f && i * SimStep > landTime + FuseAfterLanding)
            {
                break;
            }
        }
        if (landTime < 0f)
        {
            landTime = MaxFlight;
        }
        return path;
    }

    internal static int GroundMask
    {
        get
        {
            if (groundMask == -1)
            {
                LayerSettings layers = GameManager.LayerSettings;
                int mask = Physics.DefaultRaycastLayers;
                mask &= ~layers.PlayersMask & ~layers.GolfCartsMask & ~layers.BallMask & ~layers.DynamicBallMask & ~layers.FoliageMask;
                mask &= ~(1 << layers.ItemsLayer) & ~(1 << layers.VfxLayer) & ~(1 << layers.HittablesLayer)
                    & ~(1 << layers.ElectromagnetShieldLayer) & ~(1 << layers.GolfCartPassengersLayer);
                groundMask = mask;
            }
            return groundMask;
        }
    }

    private static float SoundVolume(Vector3 position) => ItemAudio.Attenuate(position, BoogieBombItem.SoundVolume.Value, 15f, 120f);

    private static float MusicVolume(Vector3 position) =>
        ItemAudio.Attenuate(position, BoogieBombItem.SoundVolume.Value * 0.8f, BoogieBombItem.Radius.Value * 2f, 70f);

    public static void ClearAll()
    {
        foreach (Bomb bomb in bombs.Values)
        {
            if (bomb.go != null)
            {
                Object.Destroy(bomb.go);
            }
        }
        bombs.Clear();
        foreach (Party party in parties)
        {
            ItemAudio.Stop(party.music);
            if (party.root != null)
            {
                Object.Destroy(party.root);
            }
        }
        parties.Clear();
        BoogieDance.Stop();
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

/// <summary>
/// Boogie bomb sounds (or the player's own boogie_pop / boogie_music files): a sparkly pop, and a funky 120 bpm
/// disco loop: four-on-the-floor kick, offbeat hi-hats, octave-jumping bass and a chord stab.
/// </summary>
internal static class BoogieSounds
{
    private static FMOD.Sound pop, music;

    public static void PlayPop(float volume)
    {
        if (!pop.hasHandle())
        {
            pop = ItemAudio.LoadOrSynthesize(BoogieBombItem.Instance, "boogie_pop", () =>
            {
                var s = new Synth(1.2f, 51)
                    .Noise(Synth.Decay(0.06f), Synth.Const(0.6f), 1f)
                    .Tone(Synth.Ramp(180f, 60f, 0.15f), Synth.Decay(0.12f), 1f);
                float[] sparkle = { 1568f, 2093f, 2637f, 3136f, 2093f, 2637f };
                for (int i = 0; i < sparkle.Length; i++)
                {
                    float f = sparkle[i];
                    s.Tone(_ => f, Synth.Decay(0.18f), 0.25f, start: 0.08f + i * 0.07f);
                }
                return s.Normalized(0.8f);
            });
        }
        ItemAudio.Play(pop, ItemAudio.SfxBus, volume);
    }

    public static FMOD.Channel StartMusic(float volume)
    {
        if (!music.hasHandle())
        {
            music = ItemAudio.LoadOrSynthesize(BoogieBombItem.Instance, "boogie_music", SynthesizeDisco, loop: true);
        }
        return ItemAudio.Play(music, ItemAudio.SfxBus, volume);
    }

    /// <summary>Two bars at 120 bpm (4 seconds), so it loops seamlessly.</summary>
    private static float[] SynthesizeDisco()
    {
        const float beat = 0.5f;
        var s = new Synth(4f, 52);
        for (int b = 0; b < 8; b++)
        {
            float t0 = b * beat;
            s.Tone(Synth.Ramp(150f, 45f, 0.08f), t => t < 0.25f ? Mathf.Exp(-t / 0.07f) : 0f, 1.2f, start: t0);   // kick
            s.Noise(t => t < 0.08f ? Mathf.Exp(-t / 0.02f) : 0f, Synth.Const(1f), 0.35f, start: t0 + beat * 0.5f); // open hat
            s.Noise(t => t < 0.05f ? Mathf.Exp(-t / 0.01f) : 0f, Synth.Const(1f), 0.15f, start: t0 + beat * 0.25f);
            s.Noise(t => t < 0.05f ? Mathf.Exp(-t / 0.01f) : 0f, Synth.Const(1f), 0.15f, start: t0 + beat * 0.75f);
            if (b % 2 == 1)
            {
                s.Noise(t => t < 0.15f ? Mathf.Exp(-t / 0.05f) : 0f, Synth.Const(0.45f), 0.5f, start: t0); // clap on 2 and 4
            }
        }
        // Octave-jumping bass on eighth notes: A, A, then up to C, D.
        float[] bassRoots = { 110f, 110f, 130.8f, 146.8f };
        for (int e = 0; e < 16; e++)
        {
            float root = bassRoots[e / 4];
            float f = e % 2 == 0 ? root : root * 2f;
            s.Tone(_ => f, t => t < 0.22f ? Mathf.Exp(-t / 0.12f) : 0f, 0.45f, harmonics: 4, start: e * beat * 0.5f);
        }
        // A bright chord stab on the offbeat every bar.
        float[] chord = { 440f, 554.4f, 659.3f };
        for (int bar = 0; bar < 2; bar++)
        {
            foreach (float f in chord)
            {
                s.Tone(_ => f, t => t < 0.3f ? Mathf.Exp(-t / 0.1f) : 0f, 0.18f, harmonics: 3, start: bar * 2f + beat * 1.5f);
                s.Tone(_ => f, t => t < 0.3f ? Mathf.Exp(-t / 0.1f) : 0f, 0.18f, harmonics: 3, start: bar * 2f + beat * 3.5f);
            }
        }
        return s.Normalized(0.75f);
    }
}

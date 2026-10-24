using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// Ender pearls on every client: the pearl flies a path every client computes the same way (gravity, through
/// portals) until it first hits the level. Where it lands, everyone sees purple particles; the thrower's client
/// teleports its player there.
/// </summary>
internal static class PearlWorld
{
    private const float SimStep = 0.01f;
    private const float MaxFlight = 5f;
    private static readonly Color Purple = new(0.62f, 0.2f, 0.95f);

    private class Pearl
    {
        public int id;
        public uint ownerNetId;
        public GameObject go;
        public List<Vector3> path;
        public float landTime;
        public bool landed; // false: it flew off into nothing
        public Vector3 landPoint;
        public Vector3 landNormal;
        public float spawnTime;
    }

    private static readonly Dictionary<int, Pearl> pearls = new();
    private static readonly List<int> finished = new();
    private static bool sceneHookInstalled;
    private static Material particleMaterial;

    public static void Spawn(int id, uint ownerNetId, Vector3 origin, Vector3 velocity)
    {
        EnsureSceneHook();
        if (pearls.ContainsKey(id))
        {
            return;
        }
        var go = new GameObject($"EnderPearl_{id}");
        go.transform.position = origin;
        ModelAsset model = EnderPearlItem.Instance.Model;
        if (model?.Mesh != null)
        {
            model.InitMaterials(ModelAsset.SharedTemplate);
            model.CreateModel(go.transform, 1.3f);
        }
        var trail = go.AddComponent<TrailRenderer>();
        trail.sharedMaterial = LightsaberItem.GlowMaterial(Purple, 0.6f);
        trail.time = 0.35f;
        trail.minVertexDistance = 0.05f;
        trail.widthMultiplier = 0.12f;
        trail.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        trail.shadowCastingMode = ShadowCastingMode.Off;

        var pearl = new Pearl { id = id, ownerNetId = ownerNetId, go = go, spawnTime = Time.time };
        pearl.path = ComputePath(origin, velocity, out pearl.landTime, out pearl.landed, out pearl.landPoint, out pearl.landNormal);
        pearls[id] = pearl;
        Plugin.Log.LogInfo($"Ender pearl thrown: from {origin} at {velocity.magnitude:0.0} m/s, lands={pearl.landed} after {pearl.landTime:0.00} s at {pearl.landPoint}.");
        PearlSounds.PlayThrow(Volume(origin));
    }

    public static void Tick()
    {
        float now = Time.time;
        foreach (Pearl pearl in pearls.Values)
        {
            if (pearl.go == null)
            {
                finished.Add(pearl.id);
                continue;
            }
            float t = now - pearl.spawnTime;
            int index = Mathf.Min(Mathf.FloorToInt(t / SimStep), pearl.path.Count - 1);
            int next = Mathf.Min(index + 1, pearl.path.Count - 1);
            Vector3 from = pearl.path[index], to = pearl.path[next];
            pearl.go.transform.position = (to - from).sqrMagnitude > 4f
                ? to // came out of a portal
                : Vector3.Lerp(from, to, t / SimStep - Mathf.Floor(t / SimStep));
            pearl.go.transform.rotation = Quaternion.Euler(t * 300f, t * 200f, 0f);
            if (t >= pearl.landTime)
            {
                if (pearl.landed)
                {
                    Land(pearl);
                }
                finished.Add(pearl.id);
            }
        }
        foreach (int id in finished)
        {
            if (pearls.TryGetValue(id, out Pearl pearl) && pearl.go != null)
            {
                Object.Destroy(pearl.go);
            }
            pearls.Remove(id);
        }
        finished.Clear();
    }

    private static void Land(Pearl pearl)
    {
        Vector3 feet = ArrivalFeet(pearl.landPoint, pearl.landNormal);
        PlayerInfo owner = NetworkClient.spawned.TryGetValue(pearl.ownerNetId, out NetworkIdentity identity) && identity != null
            ? identity.GetComponent<PlayerInfo>() : null;
        if (owner != null)
        {
            Burst(owner.transform.position + Vector3.up * 0.9f); // where they left
            PearlSounds.PlayTeleport(Volume(owner.transform.position));
        }
        Burst(feet + Vector3.up * 0.9f);
        PearlSounds.PlayTeleport(Volume(feet));

        if (owner == null || owner != GameManager.LocalPlayerInfo || owner.ActiveGolfCartSeat.IsValid())
        {
            return;
        }
        Plugin.Log.LogInfo($"Ender pearl: teleported from {owner.transform.position} to {feet}.");
        owner.Movement.Teleport(feet, owner.transform.rotation, resetState: false);
    }

    /// <summary>Where the thrower stands after landing: on the spot if it's floor, else at the foot of the wall (or under the ceiling).</summary>
    private static Vector3 ArrivalFeet(Vector3 point, Vector3 normal)
    {
        if (normal.y > 0.5f)
        {
            return point + Vector3.up * 0.02f;
        }
        Vector3 off = point + normal * 0.5f + (normal.y < -0.5f ? Vector3.down * 1.9f : Vector3.zero);
        if (Physics.Raycast(off + Vector3.up * 0.3f, Vector3.down, out RaycastHit ground, 40f,
                GameManager.LayerSettings.PlayerGroundableMask, QueryTriggerInteraction.Ignore))
        {
            return ground.point + Vector3.up * 0.02f;
        }
        return off;
    }

    /// <summary>Fixed-step flight under gravity (through portals) until it first hits the level.</summary>
    private static List<Vector3> ComputePath(Vector3 start, Vector3 velocity, out float landTime, out bool landed, out Vector3 landPoint, out Vector3 landNormal)
    {
        var path = new List<Vector3> { start };
        Vector3 position = start;
        Vector3 v = velocity;
        int steps = Mathf.CeilToInt(MaxFlight / SimStep);
        int mask = BoogieWorld.GroundMask;
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
                path.Add(hit.point);
                landTime = i * SimStep;
                landed = true;
                landPoint = hit.point;
                landNormal = hit.normal;
                return path;
            }
            position = next;
            path.Add(position);
        }
        landTime = MaxFlight;
        landed = false;
        landPoint = position;
        landNormal = Vector3.up;
        return path;
    }

    /// <summary>A puff of purple particles drifting up, like Minecraft's teleport.</summary>
    private static void Burst(Vector3 position)
    {
        particleMaterial ??= UnlitMaterials.Particles();
        var go = new GameObject("PearlParticles");
        go.transform.position = position;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.2f;
        main.loop = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.14f);
        main.gravityModifier = -0.08f;
        main.maxParticles = 120;
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.35f, 1f), new Color(0.35f, 0.05f, 0.6f));
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(0.7f, 1.8f, 0.7f);
        var size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = particleMaterial;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        ps.Play();
    }

    private static float Volume(Vector3 position) => ItemAudio.Attenuate(position, EnderPearlItem.SoundVolume.Value, 12f, 90f);

    public static void ClearAll()
    {
        foreach (Pearl pearl in pearls.Values)
        {
            if (pearl.go != null)
            {
                Object.Destroy(pearl.go);
            }
        }
        pearls.Clear();
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

/// <summary>Ender pearl sounds (or the player's own pearl_throw / pearl_teleport files): a soft whoosh and a warbling "vwoop".</summary>
internal static class PearlSounds
{
    private static FMOD.Sound throwSound, teleport;

    public static void PlayThrow(float volume)
    {
        if (!throwSound.hasHandle())
        {
            throwSound = ItemAudio.LoadOrSynthesize(EnderPearlItem.Instance, "pearl_throw", () => new Synth(0.4f, 161)
                .Noise(t => Mathf.Sin(Mathf.Clamp01(t / 0.35f) * Mathf.PI), Synth.Ramp(0.1f, 0.4f, 0.3f), 0.8f, band: true)
                .Normalized(0.5f));
        }
        ItemAudio.Play(throwSound, ItemAudio.SfxBus, volume);
    }

    public static void PlayTeleport(float volume)
    {
        if (!teleport.hasHandle())
        {
            teleport = ItemAudio.LoadOrSynthesize(EnderPearlItem.Instance, "pearl_teleport", () => new Synth(0.7f, 162)
                .Tone(t => 420f * Mathf.Pow(0.35f, t / 0.5f) * (1f + 0.25f * Mathf.Sin(t * 70f)), Synth.Env(0.01f, 0.25f, 0.4f), 0.7f, harmonics: 4)
                .Tone(t => 640f * Mathf.Pow(0.4f, t / 0.5f), Synth.Env(0.01f, 0.15f, 0.35f), 0.3f, harmonics: 2)
                .Noise(Synth.Decay(0.12f), Synth.Const(0.3f), 0.2f)
                .Normalized(0.7f));
        }
        ItemAudio.Play(teleport, ItemAudio.SfxBus, volume);
    }
}

using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// The blue shell on every client: rises out of the thrower's hands, flies high over the course to the
/// leader with its siren wailing (everyone hears it coming), circles over them, then slams down. The host
/// sends the impact; the explosion is a big rocket blast plus a blue shockwave dome.
/// </summary>
internal static class BlueShellFlight
{
    public const float RiseSeconds = 1.2f;
    public const float HoverSeconds = 1.6f;
    public const float SlamSeconds = 0.35f;
    private const float CruiseHeight = 28f;

    public static float TotalSeconds(float travelSeconds) => RiseSeconds + travelSeconds + HoverSeconds + SlamSeconds;

    private class Shell
    {
        public int id;
        public PlayerInfo target;
        public Vector3 targetPosition;
        public Vector3 start;
        public float travel;
        public float startTime;
        public GameObject root;
        public Transform spinner;
        public FMOD.Channel siren;
    }

    private class Burst
    {
        public Transform sphere;
        public Light light;
        public float start;
        public float size;
    }

    private static readonly Dictionary<int, Shell> shells = new();
    private static readonly List<Burst> bursts = new();
    private static Material burstMaterial;
    private static bool sceneHookInstalled;

    public static void Launch(int id, uint ownerNetId, uint targetNetId, Vector3 start, float travel)
    {
        EnsureSceneHook();
        if (shells.ContainsKey(id))
        {
            return;
        }
        PlayerInfo target = Find(targetNetId);
        var root = new GameObject($"BlueShell_{id}");
        root.transform.position = start;
        var spinner = new GameObject("Spinner").transform;
        spinner.SetParent(root.transform, false);
        ModelAsset model = BlueShellItem.Instance.Model;
        if (model?.Mesh != null)
        {
            model.InitMaterials(ModelAsset.SharedTemplate);
            model.CreateModel(spinner, BlueShellItem.ShellScale.Value);
        }
        var glow = root.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.color = new Color(0.3f, 0.5f, 1f);
        glow.range = 6f;
        glow.intensity = 2.5f;

        var shell = new Shell
        {
            id = id,
            target = target,
            targetPosition = target != null ? target.transform.position : start,
            start = start,
            travel = travel,
            startTime = Time.time,
            root = root,
            spinner = spinner,
        };
        BlueShellSounds.PlayLaunch(Volume(start, 0.5f));
        shell.siren = BlueShellSounds.StartSiren(Volume(start, 0.35f));
        shells[id] = shell;
    }

    public static void Impact(int id, Vector3 position, bool hitBall, Vector3 ballPosition)
    {
        if (shells.TryGetValue(id, out Shell shell))
        {
            ItemAudio.Stop(shell.siren);
            if (shell.root != null)
            {
                Object.Destroy(shell.root);
            }
            shells.Remove(id);
        }
        Explosions.PlayEffects(position, 1.5f);
        BlueShellSounds.PlayImpact(Volume(position, 0.5f));
        AddBurst(position, BlueShellItem.ExplosionScale.Value * GameManager.ItemSettings.RocketExplosionRange * 2f);
        if (hitBall)
        {
            Explosions.PlayEffects(ballPosition, 0.6f);
            AddBurst(ballPosition, 3f);
        }
    }

    public static void Tick()
    {
        float now = Time.time;
        foreach (Shell shell in shells.Values)
        {
            if (shell.root == null)
            {
                continue;
            }
            if (shell.target != null)
            {
                shell.targetPosition = Vector3.Lerp(shell.targetPosition, shell.target.transform.position, 1f - Mathf.Exp(-6f * Time.deltaTime));
            }
            float t = now - shell.startTime;
            shell.root.transform.position = PositionAt(shell, t);
            // Spin like the real thing; spin faster when it's about to drop.
            float spinSpeed = t > RiseSeconds + shell.travel ? 900f : 420f;
            shell.spinner.Rotate(0f, spinSpeed * Time.deltaTime, 0f, Space.Self);
            ItemAudio.SetVolume(shell.siren, Volume(shell.root.transform.position, 0.35f));
            ItemAudio.SetPitch(shell.siren, t > RiseSeconds + shell.travel ? 1.25f : 1f);
        }

        for (int i = bursts.Count - 1; i >= 0; i--)
        {
            Burst burst = bursts[i];
            float u = (now - burst.start) / 0.6f;
            if (burst.sphere == null || u >= 1f)
            {
                if (burst.sphere != null)
                {
                    Object.Destroy(burst.sphere.gameObject);
                }
                bursts.RemoveAt(i);
                continue;
            }
            burst.sphere.localScale = Vector3.one * (burst.size * Mathf.Sqrt(u));
            UnlitMaterials.SetColor(burst.sphere.GetComponent<MeshRenderer>().material, new Color(0.3f, 0.55f, 1f, 1f - u));
            burst.light.intensity = 8f * (1f - u);
        }
    }

    /// <summary>Rise above the thrower, cruise high to the leader, circle over them, slam down.</summary>
    private static Vector3 PositionAt(Shell shell, float t)
    {
        Vector3 top = shell.start + Vector3.up * CruiseHeight;
        Vector3 over = shell.targetPosition + Vector3.up * (CruiseHeight * 0.45f);
        if (t < RiseSeconds)
        {
            float u = t / RiseSeconds;
            return Vector3.Lerp(shell.start + Vector3.up * 1.2f, top, 1f - (1f - u) * (1f - u));
        }
        t -= RiseSeconds;
        if (t < shell.travel)
        {
            float u = Mathf.SmoothStep(0f, 1f, t / shell.travel); // ease in and out between the two points
            Vector3 p = Vector3.Lerp(top, over, u);
            p.y += Mathf.Sin(u * Mathf.PI) * 6f; // arc over the course
            return p;
        }
        t -= shell.travel;
        if (t < HoverSeconds)
        {
            float u = t / HoverSeconds;
            float angle = u * Mathf.PI * 4f;
            float radius = Mathf.Lerp(4f, 1.5f, u);
            float height = Mathf.Lerp(CruiseHeight * 0.45f, 5f, u);
            return shell.targetPosition + new Vector3(Mathf.Cos(angle) * radius, height, Mathf.Sin(angle) * radius);
        }
        t -= HoverSeconds;
        float s = Mathf.Clamp01(t / SlamSeconds);
        Vector3 hoverEnd = shell.targetPosition + new Vector3(1.5f, 5f, 0f);
        return Vector3.Lerp(hoverEnd, shell.targetPosition + Vector3.up * 0.5f, s * s);
    }

    private static void AddBurst(Vector3 position, float size)
    {
        burstMaterial ??= UnlitMaterials.Additive(null, new Color(0.3f, 0.55f, 1f, 1f));
        GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(sphere.GetComponent<Collider>());
        sphere.name = "BlueShellBurst";
        sphere.transform.position = position;
        var renderer = sphere.GetComponent<MeshRenderer>();
        renderer.material = new Material(burstMaterial); // own copy so it can fade on its own
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        var light = sphere.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.35f, 0.55f, 1f);
        light.range = size * 2f;
        bursts.Add(new Burst { sphere = sphere.transform, light = light, start = Time.time, size = size });
    }

    public static void ClearAll()
    {
        foreach (Shell shell in shells.Values)
        {
            ItemAudio.Stop(shell.siren);
            if (shell.root != null)
            {
                Object.Destroy(shell.root);
            }
        }
        shells.Clear();
        foreach (Burst burst in bursts)
        {
            if (burst.sphere != null)
            {
                Object.Destroy(burst.sphere.gameObject);
            }
        }
        bursts.Clear();
    }

    /// <summary>Everyone hears the blue shell: full volume nearby, never below <paramref name="floor"/> of it.</summary>
    private static float Volume(Vector3 position, float floor)
    {
        float volume = BlueShellItem.SoundVolume.Value;
        return Mathf.Max(ItemAudio.Attenuate(position, volume, 25f, 250f), volume * floor);
    }

    private static PlayerInfo Find(uint netId) =>
        NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null && identity.TryGetComponent(out PlayerInfo player)
            ? player
            : null;

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
/// Blue shell sounds (or the player's own blueshell_launch / blueshell_siren / blueshell_impact files):
/// a launch whoosh, a two-tone wailing siren over a buzzing propeller, and a deep boom on impact.
/// </summary>
internal static class BlueShellSounds
{
    private static FMOD.Sound launch, siren, impact;

    public static void PlayLaunch(float volume)
    {
        if (!launch.hasHandle())
        {
            launch = ItemAudio.LoadOrSynthesize(BlueShellItem.Instance, "blueshell_launch", () =>
                new Synth(0.9f, 41)
                    .Noise(Synth.Env(0.05f, 0.25f, 0.5f), Synth.Ramp(0.1f, 0.5f, 0.6f), 1f, band: true)
                    .Tone(Synth.Ramp(500f, 1400f, 0.7f), Synth.Env(0.05f, 0.4f, 0.4f), 0.4f, harmonics: 2)
                    .Normalized(0.8f));
        }
        ItemAudio.Play(launch, ItemAudio.SfxBus, volume);
    }

    public static FMOD.Channel StartSiren(float volume)
    {
        if (!siren.hasHandle())
        {
            // 2.5 s = exactly three siren cycles, so the loop is seamless.
            siren = ItemAudio.LoadOrSynthesize(BlueShellItem.Instance, "blueshell_siren", () =>
                new Synth(2.5f, 42)
                    .Tone(t => 1150f + 330f * Mathf.Sin(t * 2f * Mathf.PI * 1.2f), Synth.Const(1f), 0.55f, harmonics: 3)
                    .Tone(t => 115f + 4f * Mathf.Sin(t * 2f * Mathf.PI * 2f), Synth.Const(1f), 0.5f, harmonics: 7)   // propeller buzz
                    .Noise(Synth.Const(1f), Synth.Const(0.3f), 0.12f, band: true)
                    .Normalized(0.55f), loop: true);
        }
        return ItemAudio.Play(siren, ItemAudio.SfxBus, volume);
    }

    public static void PlayImpact(float volume)
    {
        if (!impact.hasHandle())
        {
            impact = ItemAudio.LoadOrSynthesize(BlueShellItem.Instance, "blueshell_impact", () =>
                new Synth(2f, 43)
                    .Tone(Synth.Ramp(110f, 35f, 0.6f), Synth.Decay(0.45f), 1.4f, harmonics: 3)
                    .Noise(Synth.Decay(0.3f), Synth.Const(0.25f), 1f)
                    .Noise(Synth.Decay(0.9f), Synth.Const(0.03f), 2f)
                    .Echo(0.18f, 0.35f)
                    .Normalized(0.95f));
        }
        ItemAudio.Play(impact, ItemAudio.SfxBus, volume);
    }
}

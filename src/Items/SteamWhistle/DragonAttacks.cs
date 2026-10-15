using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// The Steam Whistle's dragon: flies in from the horizon, hovers near its target, breathes fire, and leaves.
/// Every client plays the same timeline from the host's DragonAttack message. Only the target's own client
/// knocks the target out (players own their movement), like the banana's slip.
/// </summary>
internal static class DragonAttacks
{
    // Timeline (seconds after the attack starts).
    private const float ArriveTime = 3.5f;
    private const float WhistleTime = 2.9f;
    private const float BreathStart = 3.9f;
    private const float IgniteTime = 4.3f;
    private const float BreathEnd = 5.9f;
    private const float LeaveTime = 6.2f;
    private const float EndTime = 10f;

    // Points on the model (Unity space, model scale 1). Must match blender/SteamWhistle/build_thomas_dragon.py.
    private static readonly Vector3 LeftWingHinge = new(-0.78f, 2.15f, 0.2f);
    private static readonly Vector3 RightWingHinge = new(0.78f, 2.15f, 0.2f);
    private static readonly Vector3 Mouth = new(0f, 1.2f, 2.3f);

    private class Attack
    {
        public int id;
        public PlayerInfo summoner;
        public PlayerInfo target;
        public Vector3 targetPosition;
        public Vector3 direction;
        public float startTime;
        public GameObject root;
        public Transform leftWing;
        public Transform rightWing;
        public ParticleSystem breath;
        public Vector3 previousPosition;
        public bool whistled;
        public bool breathing;
        public bool ignited;
        public FMOD.Channel whoosh;
    }

    private static readonly List<Attack> attacks = new();
    private static ModelAsset body;
    private static ModelAsset leftWingModel;
    private static ModelAsset rightWingModel;
    private static FMOD.Sound theme;
    private static FMOD.Channel themeChannel;
    private static float themeStartedAt = -1f;
    private static bool sceneHookInstalled;

    public static void LoadModels(string assetDir)
    {
        body = ModelAsset.Load(assetDir, "thomas_dragon", loadIcon: false);
        leftWingModel = ModelAsset.Load(assetDir, "thomas_wing_left", loadIcon: false);
        rightWingModel = ModelAsset.Load(assetDir, "thomas_wing_right", loadIcon: false);
    }

    public static void PlayWhistle(uint playerNetId)
    {
        if (NetworkClient.spawned.TryGetValue(playerNetId, out NetworkIdentity identity) && identity != null)
        {
            Vector3 position = identity.transform.position;
            ItemAudio.Play(WhistleSounds.Whistle, ItemAudio.SfxBus, WhistleVolume(position));
        }
    }

    /// <summary>Everyone in the lobby hears the whistle: full volume nearby, never quieter than half across the map.</summary>
    private static float WhistleVolume(Vector3 position)
    {
        float volume = SteamWhistleItem.SoundVolume.Value;
        return Mathf.Max(ItemAudio.Attenuate(position, volume, 20f, 200f), volume * 0.5f);
    }

    public static void Begin(DragonAttack msg)
    {
        EnsureSceneHook();
        if (body?.Mesh == null)
        {
            return;
        }
        PlayerInfo target = Find(msg.targetNetId);
        if (target == null)
        {
            return;
        }
        float yaw = msg.approachYaw * Mathf.Deg2Rad;
        var attack = new Attack
        {
            id = msg.id,
            summoner = Find(msg.summonerNetId),
            target = target,
            targetPosition = target.transform.position,
            direction = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw)),
            startTime = Time.time,
        };
        BuildDragon(attack);
        attacks.Add(attack);
        StartTheme();
    }

    public static void ClearAll()
    {
        foreach (Attack attack in attacks)
        {
            ItemAudio.Stop(attack.whoosh);
            if (attack.root != null)
            {
                Object.Destroy(attack.root);
            }
        }
        attacks.Clear();
        ItemAudio.Stop(themeChannel);
        themeStartedAt = -1f;
    }

    public static void Tick()
    {
        UpdateTheme();
        for (int i = attacks.Count - 1; i >= 0; i--)
        {
            Attack attack = attacks[i];
            float t = Time.time - attack.startTime;
            if (t >= EndTime || attack.root == null)
            {
                ItemAudio.Stop(attack.whoosh);
                if (attack.root != null)
                {
                    Object.Destroy(attack.root);
                }
                attacks.RemoveAt(i);
                continue;
            }
            UpdateAttack(attack, t);
        }
    }

    // ---- timeline ----

    private static void UpdateAttack(Attack attack, float t)
    {
        if (attack.target != null)
        {
            attack.targetPosition = Vector3.Lerp(attack.targetPosition, attack.target.transform.position, 1f - Mathf.Exp(-4f * Time.deltaTime));
        }
        float scale = SteamWhistleItem.DragonScale.Value;
        Vector3 T = attack.targetPosition;
        Vector3 d = attack.direction;
        Vector3 up = Vector3.up;
        Vector3 hover = T + d * (13f * scale) + up * (7f * scale) + up * Mathf.Sin(t * 2.2f) * 0.6f;

        Vector3 position;
        if (t < ArriveTime)
        {
            float u = EaseOut(t / ArriveTime);
            position = Bezier(T + d * 110f + up * 55f, T + d * 40f + up * 28f, hover, u);
        }
        else if (t < LeaveTime)
        {
            position = hover;
        }
        else
        {
            float u = EaseIn((t - LeaveTime) / (EndTime - LeaveTime));
            position = Bezier(hover, T - d * 10f + up * 16f, T - d * 120f + up * 75f, u);
        }

        Transform root = attack.root.transform;
        Vector3 velocity = (position - attack.previousPosition) / Mathf.Max(Time.deltaTime, 0.001f);
        attack.previousPosition = position;
        root.position = position;
        Quaternion look;
        if (t >= ArriveTime - 0.4f && t < LeaveTime + 0.3f)
        {
            Vector3 toTarget = T + up - position;
            look = Quaternion.LookRotation(Vector3.Lerp(new Vector3(toTarget.x, 0f, toTarget.z), toTarget, 0.35f), up);
        }
        else
        {
            look = velocity.sqrMagnitude > 0.01f ? Quaternion.LookRotation(velocity, up) : root.rotation;
        }
        root.rotation = Quaternion.Slerp(root.rotation, look, 1f - Mathf.Exp(-5f * Time.deltaTime));

        // Wings: big slow flaps while flying, quicker smaller ones while hovering.
        bool hovering = t >= ArriveTime && t < LeaveTime;
        float flap = Mathf.Sin(t * (hovering ? 9f : 6f)) * (hovering ? 22f : 38f);
        attack.leftWing.localRotation = Quaternion.Euler(0f, 0f, -flap);
        attack.rightWing.localRotation = Quaternion.Euler(0f, 0f, flap);

        if (!attack.whistled && t >= WhistleTime)
        {
            attack.whistled = true;
            ItemAudio.Play(WhistleSounds.Whistle, ItemAudio.SfxBus, WhistleVolume(position));
        }

        if (attack.breath != null)
        {
            attack.breath.transform.LookAt(T + up * 0.9f);
            bool breathe = t >= BreathStart && t < BreathEnd;
            if (breathe && !attack.breathing)
            {
                attack.breath.Play();
                attack.whoosh = ItemAudio.Play(WhistleSounds.Whoosh, ItemAudio.SfxBus, ItemAudio.Attenuate(position, SteamWhistleItem.SoundVolume.Value, 15f, 120f));
            }
            else if (!breathe && attack.breathing)
            {
                attack.breath.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            attack.breathing = breathe;
        }

        if (!attack.ignited && t >= IgniteTime)
        {
            attack.ignited = true;
            Ignite(attack, position);
        }
    }

    private static void Ignite(Attack attack, Vector3 dragonPosition)
    {
        PlayerInfo target = attack.target;
        if (target == null)
        {
            return;
        }
        FireEffects.Burn(target.transform, SteamWhistleItem.BurnDuration.Value);
        VfxManager.PlayPooledVfxLocalOnly(VfxType.RocketLauncherRocketExplosion, target.transform.position + Vector3.up * 0.5f, Quaternion.identity);

        if (!target.isLocalPlayer || target.Movement == null)
        {
            return;
        }
        Vector3 away = target.transform.position - dragonPosition;
        away.y = 0f;
        away = away.sqrMagnitude > 0.01f ? away.normalized : -target.transform.forward;
        Vector3 launch = away * SteamWhistleItem.LaunchHorizontal.Value + Vector3.up * SteamWhistleItem.LaunchUp.Value;
        Vector3 localOrigin = target.transform.InverseTransformPoint(dragonPosition);
        bool knockedOut = target.Movement.TryKnockOut(attack.summoner, KnockoutType.Rocket, isLegSweep: false, localOrigin, 0f, launch,
            ElectromagnetShieldHitBlockType.FullyBlocked, ItemUseId.Invalid, fromSpecialState: false, canFallbackToUnground: true,
            out _, out _);
        if (knockedOut)
        {
            target.Rigidbody.AddForce(launch, ForceMode.VelocityChange);
        }
        Plugin.Log.LogInfo($"Thomas set you on fire (knocked out: {knockedOut}).");
    }

    // ---- model ----

    private static void BuildDragon(Attack attack)
    {
        Material template = ModelAsset.SharedTemplate;
        body.InitMaterials(template);
        leftWingModel.InitMaterials(template);
        rightWingModel.InitMaterials(template);

        float scale = SteamWhistleItem.DragonScale.Value;
        var root = new GameObject($"SteamWhistleDragon_{attack.id}");
        root.transform.localScale = Vector3.one * scale;
        body.CreateModel(root.transform, 1f);
        attack.leftWing = CreateWing(root.transform, leftWingModel, LeftWingHinge);
        attack.rightWing = CreateWing(root.transform, rightWingModel, RightWingHinge);
        attack.breath = FireEffects.CreateBreath(root.transform, Mouth, scale);

        Vector3 start = attack.targetPosition + attack.direction * 110f + Vector3.up * 55f;
        root.transform.SetPositionAndRotation(start, Quaternion.LookRotation(-attack.direction, Vector3.up));
        attack.previousPosition = start;
        attack.root = root;
    }

    private static Transform CreateWing(Transform parent, ModelAsset model, Vector3 hinge)
    {
        var pivot = new GameObject("Wing").transform;
        pivot.SetParent(parent, false);
        pivot.localPosition = hinge;
        if (model?.Mesh != null)
        {
            model.CreateModel(pivot, 1f);
        }
        return pivot;
    }

    // ---- theme music (optional, supplied by the player) ----

    private static readonly string[] ThemeFiles = { "thomas_theme.ogg", "thomas_theme.mp3", "thomas_theme.wav", "thomas_theme.flac" };
    private static bool themeLoaded;
    private static System.Threading.Tasks.Task<float[]> builtInTheme;
    private static float builtInSeconds;

    /// <summary>Starts synthesizing the built-in anthem off the main thread (a few seconds of number crunching).</summary>
    public static void PrepareBuiltInTheme() => builtInTheme ??= System.Threading.Tasks.Task.Run(ThemeSynth.Synthesize);

    private static void StartTheme()
    {
        if (!themeLoaded)
        {
            themeLoaded = true;
            foreach (string file in ThemeFiles)
            {
                string path = Path.Combine(SteamWhistleItem.Instance.AssetDir, file);
                if (File.Exists(path) && ItemAudio.TryLoadFile(path, out theme))
                {
                    Plugin.Log.LogInfo($"Steam Whistle theme: {file}");
                    break;
                }
            }
        }
        if (!theme.hasHandle() && builtInTheme != null && builtInTheme.Status == System.Threading.Tasks.TaskStatus.RanToCompletion)
        {
            // No theme file: use the pack's own anthem (synthesized in the background at startup).
            theme = ItemAudio.CreateSound(builtInTheme.Result, 44100);
            builtInSeconds = builtInTheme.Result.Length / 44100f;
            Plugin.Log.LogInfo("Steam Whistle theme: built-in anthem");
        }
        if (!theme.hasHandle() || themeStartedAt >= 0f)
        {
            return; // not ready yet, or already playing for an earlier attack
        }
        themeChannel = ItemAudio.Play(theme, ItemAudio.MusicBus, SteamWhistleItem.MusicVolume.Value);
        themeStartedAt = Time.time;
    }

    private static void UpdateTheme()
    {
        if (themeStartedAt < 0f)
        {
            return;
        }
        const float fade = 2f;
        // The built-in anthem plays to its end (it has its own ending); a player's file is cut after MusicSeconds.
        float playFor = builtInSeconds > 0f ? builtInSeconds : SteamWhistleItem.MusicSeconds.Value;
        float age = Time.time - themeStartedAt;
        if (age >= playFor + fade)
        {
            ItemAudio.Stop(themeChannel);
            themeStartedAt = -1f;
        }
        else if (age > playFor)
        {
            ItemAudio.SetVolume(themeChannel, SteamWhistleItem.MusicVolume.Value * (1f - (age - playFor) / fade));
        }
    }

    // ---- helpers ----

    private static PlayerInfo Find(uint netId) =>
        NetworkClient.spawned.TryGetValue(netId, out NetworkIdentity identity) && identity != null && identity.TryGetComponent(out PlayerInfo player)
            ? player
            : null;

    private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, float u) => Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(b, c, u), u);
    private static float EaseOut(float u) => 1f - (1f - Mathf.Clamp01(u)) * (1f - Mathf.Clamp01(u));
    private static float EaseIn(float u) => Mathf.Clamp01(u) * Mathf.Clamp01(u);

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

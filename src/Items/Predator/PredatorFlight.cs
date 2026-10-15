using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// Flying the missile on the pilot's client. A separate camera rides the missile and draws over the normal
/// view; the player's controls are switched off (their golfer just stands there, like in MW2) and the mouse
/// steers instead. Left click or space boosts once.
/// </summary>
internal static class PredatorFlight
{
    private const float SendInterval = 0.066f;
    private const float HitRadius = 0.5f;

    public static bool Active { get; private set; }

    private static int id;
    private static Vector3 position;
    private static float yaw, pitch;
    private static float startTime;
    private static bool boosted;
    private static float lastSend;
    private static Camera camera;
    private static FMOD.Channel loop;
    private static bool sceneHookInstalled;

    public static void Launch(PlayerInfo player)
    {
        EnsureSceneHook();
        Camera main = GameManager.Camera;
        Vector3 forward = main != null ? Vector3.ProjectOnPlane(main.transform.forward, Vector3.up) : player.transform.forward;
        forward = forward.sqrMagnitude > 0.01f ? forward.normalized : Vector3.forward;

        // High above and behind the player, aimed at the ground a little ahead of them.
        position = player.transform.position + Vector3.up * PredatorItem.Altitude.Value - forward * (PredatorItem.Altitude.Value * 0.35f);
        Vector3 target = player.transform.position + forward * 25f;
        Vector3 direction = (target - position).normalized;
        yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        pitch = Mathf.Asin(Mathf.Clamp(direction.y, -1f, 1f)) * Mathf.Rad2Deg;
        startTime = Time.time;
        boosted = false;
        id = 0;
        Active = true;

        camera = new GameObject("PredatorCamera").AddComponent<Camera>();
        if (main != null)
        {
            camera.CopyFrom(main);
            camera.depth = main.depth + 5f;
        }
        camera.targetTexture = null;
        camera.fieldOfView = 62f;
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = Mathf.Max(camera.farClipPlane, 2000f);
        UpdateCamera();

        // Hand the mouse to the missile: gameplay controls off, cursor kept locked.
        InputLock.Acquire();

        PredatorSounds.PlayLaunch(1f);
        loop = PredatorSounds.StartLoop(PredatorItem.SoundVolume.Value * 0.8f);
        PredatorNet.RequestLaunch(position, Direction);
    }

    public static void OnLaunchConfirmed(int confirmedId)
    {
        if (Active)
        {
            id = confirmedId;
        }
    }

    public static void Tick()
    {
        if (!Active)
        {
            return;
        }
        float dt = Time.deltaTime;
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            Vector2 delta = mouse.delta.ReadValue() * (PredatorItem.Sensitivity.Value * 0.05f);
            float steer = boosted ? 0.35f : 1f; // boosting makes it hard to turn, like MW2
            yaw += delta.x * steer;
            pitch = Mathf.Clamp(pitch + delta.y * steer, -89f, -15f);
            bool boostPressed = mouse.leftButton.wasPressedThisFrame || (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame);
            if (boostPressed && !boosted)
            {
                boosted = true;
                PredatorSounds.PlayBoost(PredatorItem.SoundVolume.Value);
            }
        }

        float age = Time.time - startTime;
        float speed = boosted ? PredatorItem.BoostSpeed.Value : Mathf.Lerp(PredatorItem.StartSpeed.Value, PredatorItem.CruiseSpeed.Value, age / 2f);
        Vector3 direction = Direction;
        Vector3 step = direction * (speed * dt);

        if (Physics.SphereCast(position, HitRadius, direction, out RaycastHit hit, step.magnitude, ImpactMask, QueryTriggerInteraction.Ignore))
        {
            Impact(hit.point);
            return;
        }
        position += step;
        if (age > PredatorItem.MaxFlightSeconds.Value || position.y < -500f)
        {
            Impact(position);
            return;
        }
        UpdateCamera();
        if (Time.time - lastSend > SendInterval)
        {
            lastSend = Time.time;
            PredatorNet.SendUpdate(id, position, direction, boosted);
        }
    }

    private static Vector3 Direction => Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;

    public static Vector3 Position => position;
    public static bool Boosted => boosted;
    public static float Altitude
    {
        get
        {
            PlayerInfo local = GameManager.LocalPlayerInfo;
            return local != null ? position.y - local.transform.position.y : position.y;
        }
    }

    public static Camera Camera => camera;

    private static void UpdateCamera()
    {
        if (camera != null)
        {
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(Direction, Vector3.up));
        }
    }

    private static void Impact(Vector3 at)
    {
        PredatorNet.SendImpact(id, at);
        if (!NetworkClient.active)
        {
            Explosions.PlayEffects(at, PredatorItem.ExplosionScale.Value);
        }
        End();
    }

    public static void End()
    {
        if (!Active)
        {
            return;
        }
        Active = false;
        ItemAudio.Stop(loop);
        if (camera != null)
        {
            Object.Destroy(camera.gameObject);
            camera = null;
        }
        InputLock.Release();
    }

    private static int impactMask = -1;

    /// <summary>Anything solid, plus players, carts and balls: the missile explodes on whatever it touches.</summary>
    private static int ImpactMask
    {
        get
        {
            if (impactMask == -1)
            {
                LayerSettings layers = GameManager.LayerSettings;
                int mask = Physics.DefaultRaycastLayers;
                mask &= ~(1 << layers.VfxLayer) & ~(1 << layers.ItemsLayer) & ~layers.FoliageMask;
                impactMask = mask | layers.PlayersMask | layers.GolfCartsMask | layers.BallMask | layers.DynamicBallMask;
            }
            return impactMask;
        }
    }

    private static void EnsureSceneHook()
    {
        if (sceneHookInstalled)
        {
            return;
        }
        sceneHookInstalled = true;
        SceneManager.activeSceneChanged += (_, _) => End();
    }

    // ---- HUD ----

    private static GUIStyle style;
    private static readonly Color Tint = new(0.55f, 0.62f, 0.55f, 0.18f);
    private static readonly Color Hud = new(0.9f, 0.95f, 0.9f, 0.9f);
    private static readonly Color Enemy = new(1f, 0.15f, 0.1f, 0.95f);

    /// <summary>MW2-style pilot view: washed-out tint, brackets, readouts and red boxes over other players.</summary>
    public static void OnGUI()
    {
        if (!Active || camera == null || Event.current.type != EventType.Repaint)
        {
            return;
        }
        float w = Screen.width, h = Screen.height;
        float px = Mathf.Max(1f, h / 540f);
        GUI.color = Tint;
        GUI.DrawTexture(new Rect(0, 0, w, h), Texture2D.whiteTexture);

        // Center reticle: a box with corner brackets further out.
        GUI.color = Hud;
        float box = h * 0.06f;
        Frame(w / 2 - box / 2, h / 2 - box / 2, box, box, px);
        float b = h * 0.32f, arm = h * 0.05f;
        Corner(w / 2 - b, h / 2 - b, arm, px, 1, 1);
        Corner(w / 2 + b, h / 2 - b, arm, px, -1, 1);
        Corner(w / 2 - b, h / 2 + b, arm, px, 1, -1);
        Corner(w / 2 + b, h / 2 + b, arm, px, -1, -1);

        // Other players: red boxes.
        GUI.color = Enemy;
        foreach (NetworkIdentity identity in NetworkClient.spawned.Values)
        {
            if (identity == null || identity.isLocalPlayer || !identity.TryGetComponent(out PlayerInfo player) || !player.Movement.IsVisible)
            {
                continue;
            }
            Vector3 screen = camera.WorldToScreenPoint(player.transform.position + Vector3.up * 0.9f);
            if (screen.z <= 0f)
            {
                continue;
            }
            float size = Mathf.Clamp(h * 12f / screen.z, h * 0.015f, h * 0.06f);
            Frame(screen.x - size / 2, h - screen.y - size / 2, size, size, px * 1.5f);
        }

        style ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
        style.fontSize = Mathf.RoundToInt(h * 0.026f);
        style.normal.textColor = Hud;
        GUI.color = Color.white;
        GUI.Label(new Rect(w * 0.04f, h * 0.05f, w * 0.5f, h * 0.05f), "PREDATOR MISSILE", style);
        GUI.Label(new Rect(w * 0.04f, h * 0.1f, w * 0.5f, h * 0.05f), $"ALT {Mathf.Max(0f, Altitude):000} M", style);
        GUI.Label(new Rect(w * 0.04f, h * 0.88f, w * 0.6f, h * 0.05f), Boosted ? "BOOST ENGAGED" : "LMB / SPACE: BOOST", style);
    }

    private static void Frame(float x, float y, float width, float height, float t)
    {
        Line(x, y, width, t);
        Line(x, y + height - t, width, t);
        Line(x, y, t, height);
        Line(x + width - t, y, t, height);
    }

    private static void Corner(float x, float y, float arm, float t, int dx, int dy)
    {
        GUI.DrawTexture(new Rect(dx > 0 ? x : x - arm, dy > 0 ? y : y - t, arm, t), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(dx > 0 ? x : x - t, dy > 0 ? y : y - arm, t, arm), Texture2D.whiteTexture);
    }

    private static void Line(float x, float y, float width, float height) =>
        GUI.DrawTexture(new Rect(x, y, width, height), Texture2D.whiteTexture);
}

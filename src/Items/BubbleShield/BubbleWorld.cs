using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// Bubble shields on every client: the device on the ground and its glowing honeycomb dome, which pops up, hums,
/// and collapses when its time is up. <see cref="Blocks"/> decides whether a dome stands between a
/// victim and an attack, and <see cref="FixedTick"/> bounces golf balls off its edge.
/// </summary>
internal static class BubbleWorld
{
    private const float GrowSeconds = 0.4f;
    private const float CollapseSeconds = 0.3f;
    private static readonly Color DomeColor = new(1f, 0.72f, 0.25f);

    private class Bubble
    {
        public int id;
        public Vector3 center;
        public float radius;
        public float start;
        public float until;
        public float flashUntil;
        public bool collapsed;
        public GameObject root;
        public Transform dome;
        public Material material;
        public Light light;
        public FMOD.Channel hum;
    }

    private static readonly List<Bubble> bubbles = new();
    private static Material domeTemplate;
    private static Texture2D hexTexture;
    private static Mesh domeMesh;
    private static bool sceneHookInstalled;

    public static void Spawn(int id, Vector3 center, float radius, float seconds)
    {
        EnsureSceneHook();
        foreach (Bubble existing in bubbles)
        {
            if (existing.id == id)
            {
                return;
            }
        }
        var root = new GameObject($"BubbleShield_{id}");
        root.transform.position = center;
        ModelAsset model = BubbleShieldItem.Instance.Model;
        if (model?.Mesh != null)
        {
            model.InitMaterials(ModelAsset.SharedTemplate);
            model.CreateModel(root.transform, 1.4f);
        }
        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = Vector3.up * 0.4f;
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = DomeColor;
        light.range = radius * 1.5f;
        light.intensity = 2.5f;
        light.shadows = LightShadows.None;

        var domeGo = new GameObject("Dome");
        domeGo.transform.SetParent(root.transform, false);
        domeGo.AddComponent<MeshFilter>().sharedMesh = DomeMesh;
        var renderer = domeGo.AddComponent<MeshRenderer>();
        var material = new Material(DomeTemplate);
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        domeGo.transform.localScale = Vector3.zero;

        var bubble = new Bubble
        {
            id = id, center = center, radius = radius, start = Time.time, until = Time.time + seconds,
            root = root, dome = domeGo.transform, material = material, light = light,
        };
        bubbles.Add(bubble);
        Plugin.Log.LogInfo($"Bubble shield up at {center} (radius {radius}, {seconds} s).");
        BubbleSounds.PlayDeploy(Volume(center, 1f));
        bubble.hum = BubbleSounds.StartHum(Volume(center, 0.35f));
    }

    public static void Tick()
    {
        float now = Time.time;
        for (int i = bubbles.Count - 1; i >= 0; i--)
        {
            Bubble bubble = bubbles[i];
            if (bubble.root == null || now >= bubble.until + CollapseSeconds)
            {
                Remove(bubble);
                bubbles.RemoveAt(i);
                continue;
            }
            float age = now - bubble.start;
            float left = bubble.until - now;
            float size;
            if (left < 0f)
            {
                if (!bubble.collapsed)
                {
                    bubble.collapsed = true;
                    BubbleSounds.PlayCollapse(Volume(bubble.center, 1f));
                    ItemAudio.Stop(bubble.hum);
                }
                size = 1f - Mathf.Clamp01(-left / CollapseSeconds);
            }
            else
            {
                // Pops up with a little overshoot.
                float t = Mathf.Clamp01(age / GrowSeconds);
                size = t < 1f ? Mathf.Sin(t * Mathf.PI * 0.5f) + Mathf.Sin(t * Mathf.PI) * 0.12f : 1f;
            }
            bubble.dome.localScale = Vector3.one * (bubble.radius * Mathf.Max(0f, size));

            // A slow shimmer, and a bright flash when it stops something.
            float glow = 0.85f + Mathf.Sin(age * 3f) * 0.1f;
            if (now < bubble.flashUntil)
            {
                glow += 1.2f * (bubble.flashUntil - now) / 0.3f;
            }
            Color c = DomeColor * glow;
            UnlitMaterials.SetColor(bubble.material, new Color(c.r, c.g, c.b, 1f));
            bubble.light.intensity = 2.5f * glow * Mathf.Clamp01(size);
            ItemAudio.SetVolume(bubble.hum, Volume(bubble.center, 0.35f));
        }
    }

    // ---- golf balls and carts can't pass through the dome's edge ----

    private const float BallBounciness = 0.6f;
    private const float CartBounciness = 0.3f;
    private const float MaxCartReach = 4f;   // the longest a cart (the Megazord) is taken to reach from its middle
    private const float ShellReach = 4f;     // balls this far from the edge are tracked (more than a fast ball moves in a step)
    private const float ShellMargin = 0.04f; // how far off the edge a bounced ball is put

    private static readonly Collider[] nearBalls = new Collider[64];
    private static readonly Dictionary<Rigidbody, Vector3> lastBallPosition = new();
    private static readonly Dictionary<Rigidbody, float> cartReach = new();
    private static readonly HashSet<Rigidbody> ballsSeen = new();
    private static readonly List<Rigidbody> ballsGone = new();

    /// <summary>
    /// Every physics step: a golf ball whose path crossed a dome's edge since the last step is bounced back to the side it
    /// came from (the host moves balls, and clients copy it for the balls they simulate, like portals do).
    /// </summary>
    public static void FixedTick()
    {
        LayerSettings layers = GameManager.LayerSettings;
        if (bubbles.Count == 0 || layers == null)
        {
            lastBallPosition.Clear();
            cartReach.Clear();
            return;
        }
        int mask = layers.BallMask | layers.DynamicBallMask | layers.GolfCartsMask;
        ballsSeen.Clear();
        foreach (Bubble bubble in bubbles)
        {
            if (!IsUp(bubble))
            {
                continue;
            }
            int count = Physics.OverlapSphereNonAlloc(bubble.center, bubble.radius + ShellReach + MaxCartReach, nearBalls, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = nearBalls[i].attachedRigidbody;
                if (body == null || body.isKinematic || !body.TryGetComponent(out Entity entity)
                    || !(NetworkServer.active || entity.IsSimulatingRigidbody()))
                {
                    continue;
                }
                ballsSeen.Add(body);
                if (lastBallPosition.TryGetValue(body, out Vector3 before))
                {
                    bool cart = entity.IsGolfCart;
                    BounceOffShell(bubble, body, before, cart ? ReachOf(body) : 0f, cart ? CartBounciness : BallBounciness);
                }
                lastBallPosition[body] = body.position;
            }
        }
        // Forget balls that left every dome's neighborhood.
        ballsGone.Clear();
        foreach (Rigidbody body in lastBallPosition.Keys)
        {
            if (body == null || !ballsSeen.Contains(body))
            {
                ballsGone.Add(body);
            }
        }
        foreach (Rigidbody body in ballsGone)
        {
            lastBallPosition.Remove(body);
            cartReach.Remove(body);
        }
    }

    private static void BounceOffShell(Bubble bubble, Rigidbody body, Vector3 before, float reach, float bounciness)
    {
        Vector3 now = body.position;
        float distanceBefore = (before - bubble.center).magnitude;
        float distanceNow = (now - bubble.center).magnitude;
        // A body is kept out of the dome by its own reach (half a cart's length), and kept in by the same.
        bool wasOutside = distanceBefore >= bubble.radius;
        float shell = wasOutside ? bubble.radius + reach : bubble.radius - reach;
        if (shell <= 0.1f || (wasOutside ? !(distanceBefore >= shell && distanceNow < shell) : !(distanceBefore <= shell && distanceNow > shell)))
        {
            return; // didn't cross
        }
        // Where the step's path met the shell: |before + t * step - center| = shell.
        Vector3 step = now - before;
        Vector3 offset = before - bubble.center;
        float a = step.sqrMagnitude;
        float b = 2f * Vector3.Dot(offset, step);
        float c = offset.sqrMagnitude - shell * shell;
        float discriminant = b * b - 4f * a * c;
        if (a < 1e-8f || discriminant < 0f)
        {
            return;
        }
        float root = Mathf.Sqrt(discriminant);
        float t = wasOutside ? (-b - root) / (2f * a) : (-b + root) / (2f * a); // entering: the near crossing; leaving: the far one
        Vector3 hit = bubble.center + (before + step * Mathf.Clamp01(t) - bubble.center).normalized * shell;
        Vector3 normal = (hit - bubble.center).normalized; // outward
        Vector3 velocity = body.linearVelocity;
        float into = Vector3.Dot(velocity, normal);
        // Put the body back on its own side, moving away from the edge.
        body.position = hit + normal * (wasOutside ? ShellMargin : -ShellMargin);
        body.linearVelocity = velocity - (1f + bounciness) * into * normal;
        bubble.flashUntil = Time.time + 0.3f;
        if (Mathf.Abs(into) > 2f)
        {
            BubbleSounds.PlayBlock(Volume(hit, 0.6f));
        }
    }

    /// <summary>How far a cart (or the Megazord) reaches from its middle, sideways: it stops that far from the dome.</summary>
    private static float ReachOf(Rigidbody body)
    {
        if (!cartReach.TryGetValue(body, out float reach))
        {
            Bounds bounds = default;
            bool any = false;
            foreach (Collider collider in body.GetComponentsInChildren<Collider>())
            {
                if (collider.isTrigger || collider.attachedRigidbody != body)
                {
                    continue;
                }
                // Measured in the cart's own frame so a cart that is turned when first seen doesn't look wider than it is.
                Bounds world = collider.bounds;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 local = body.transform.InverseTransformPoint(world.center + Vector3.Scale(world.extents, new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f)));
                    if (any) { bounds.Encapsulate(local); } else { bounds = new Bounds(local, Vector3.zero); any = true; }
                }
            }
            reach = any ? Mathf.Clamp(Mathf.Max(bounds.extents.x, bounds.extents.z), 0.8f, MaxCartReach) : 1.5f;
            cartReach[body] = reach;
        }
        return reach;
    }

    /// <summary>
    /// Whether a dome stands between <paramref name="victim"/> and an attack: one of them is inside it and the other
    /// isn't. The attacker's position counts if there is one; otherwise where the hit came from (if known).
    /// </summary>
    public static bool Blocks(PlayerInfo victim, PlayerInfo attacker, Vector3 source, bool hasSource)
    {
        if (bubbles.Count == 0 || victim == null || (attacker == null && !hasSource))
        {
            return false;
        }
        Vector3 victimCenter = victim.transform.position + Vector3.up * 0.9f;
        Vector3 from = attacker != null ? attacker.transform.position + Vector3.up * 0.9f : source;
        foreach (Bubble bubble in bubbles)
        {
            if (IsUp(bubble) && Inside(bubble, victimCenter) != Inside(bubble, from))
            {
                Plugin.Log.LogInfo($"Bubble shield blocked a hit on {victim.name} from {from}.");
                bubble.flashUntil = Time.time + 0.3f;
                BubbleSounds.PlayBlock(Volume(victimCenter, 1f));
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether a dome's edge lies between two points (e.g. a boogie bomb blast and a player).</summary>
    public static bool Separates(Vector3 a, Vector3 b)
    {
        foreach (Bubble bubble in bubbles)
        {
            if (IsUp(bubble) && Inside(bubble, a) != Inside(bubble, b))
            {
                bubble.flashUntil = Time.time + 0.3f;
                return true;
            }
        }
        return false;
    }

    private static bool IsUp(Bubble bubble) => bubble.root != null && Time.time < bubble.until;

    private static bool Inside(Bubble bubble, Vector3 point) => (point - bubble.center).sqrMagnitude < bubble.radius * bubble.radius;

    private static float Volume(Vector3 position, float scale) =>
        ItemAudio.Attenuate(position, BubbleShieldItem.SoundVolume.Value * scale, 10f, 80f);

    private static void Remove(Bubble bubble)
    {
        ItemAudio.Stop(bubble.hum);
        if (bubble.root != null)
        {
            Object.Destroy(bubble.root);
        }
        if (bubble.material != null)
        {
            Object.Destroy(bubble.material);
        }
    }

    public static void ClearAll()
    {
        foreach (Bubble bubble in bubbles)
        {
            Remove(bubble);
        }
        bubbles.Clear();
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

    // ---- the dome's look ----

    private static Material DomeTemplate
    {
        get
        {
            if (domeTemplate == null)
            {
                domeTemplate = UnlitMaterials.Additive(HexTexture, DomeColor);
                if (domeTemplate.HasProperty("_Cull"))
                {
                    domeTemplate.SetFloat("_Cull", (float)CullMode.Off); // seen from inside too
                }
            }
            return domeTemplate;
        }
    }

    /// <summary>A sphere 2 across (scaled by the radius) with faces both ways, so it shows from inside whatever the shader culls.</summary>
    private static Mesh DomeMesh
    {
        get
        {
            if (domeMesh == null)
            {
                GameObject sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                Mesh source = sphere.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(sphere);
                Vector3[] vertices = source.vertices;
                for (int i = 0; i < vertices.Length; i++)
                {
                    vertices[i] *= 2f; // the primitive is 1 across
                }
                int[] tris = source.triangles;
                var both = new int[tris.Length * 2];
                for (int i = 0; i < tris.Length; i += 3)
                {
                    both[i] = tris[i];
                    both[i + 1] = tris[i + 1];
                    both[i + 2] = tris[i + 2];
                    both[tris.Length + i] = tris[i];
                    both[tris.Length + i + 1] = tris[i + 2];
                    both[tris.Length + i + 2] = tris[i + 1];
                }
                domeMesh = new Mesh { name = "BubbleDome", hideFlags = HideFlags.DontUnloadUnusedAsset };
                domeMesh.vertices = vertices;
                domeMesh.normals = source.normals;
                Vector2[] uv = source.uv;
                for (int i = 0; i < uv.Length; i++)
                {
                    uv[i] = Vector2.Scale(uv[i], new Vector2(10f, 5f)); // the honeycomb's tiling (the shaders ignore material tiling)
                }
                domeMesh.uv = uv;
                domeMesh.triangles = both;
                domeMesh.RecalculateBounds();
            }
            return domeMesh;
        }
    }

    /// <summary>Honeycomb lines over a faint fill, tiling seamlessly (two hexes across, two rows down).</summary>
    private static Texture2D HexTexture
    {
        get
        {
            if (hexTexture == null)
            {
                const int W = 128, H = 222; // 1 : sqrt(3)
                hexTexture = new Texture2D(W, H, TextureFormat.RGBA32, true)
                {
                    wrapMode = TextureWrapMode.Repeat,
                    hideFlags = HideFlags.DontUnloadUnusedAsset,
                };
                var pixels = new Color32[W * H];
                var period = new Vector2(1f, 1.7320508f);
                for (int y = 0; y < H; y++)
                {
                    for (int x = 0; x < W; x++)
                    {
                        var p = new Vector2((x + 0.5f) / W * 2f, (y + 0.5f) / H * 2f * period.y);
                        Vector2 a = Mod(p, period) - period * 0.5f;
                        Vector2 b = Mod(p - period * 0.5f, period) - period * 0.5f;
                        Vector2 g = a.sqrMagnitude < b.sqrMagnitude ? a : b;
                        float edge = 0.5f - HexDistance(g);
                        float line = Mathf.Clamp01(1f - edge / 0.045f);
                        float alpha = Mathf.Max(line * 0.85f, 0.03f + 0.03f * (1f - edge * 2f));
                        byte v = (byte)(Mathf.Clamp01(alpha) * 255f);
                        pixels[y * W + x] = new Color32(v, v, v, v); // brightness in color too, for blend modes that ignore alpha
                    }
                }
                hexTexture.SetPixels32(pixels);
                hexTexture.Apply(true, true);
            }
            return hexTexture;
        }
    }

    private static Vector2 Mod(Vector2 p, Vector2 m) => new(p.x - m.x * Mathf.Floor(p.x / m.x), p.y - m.y * Mathf.Floor(p.y / m.y));

    private static float HexDistance(Vector2 p)
    {
        p = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y));
        return Mathf.Max(p.x * 0.5f + p.y * 0.8660254f, p.x);
    }
}

/// <summary>Bubble shield sounds (or the player's own bubble_deploy / bubble_hum / bubble_block / bubble_collapse files).</summary>
internal static class BubbleSounds
{
    private static FMOD.Sound deploy, hum, block, collapse;

    public static void PlayDeploy(float volume)
    {
        if (!deploy.hasHandle())
        {
            deploy = ItemAudio.LoadOrSynthesize(BubbleShieldItem.Instance, "bubble_deploy", () => new Synth(1.1f, 141)
                .Tone(Synth.Ramp(90f, 45f, 0.2f), Synth.Decay(0.15f), 1f) // thump as it hits the ground
                .Tone(Synth.Ramp(220f, 880f, 0.45f), Synth.Env(0.02f, 0.35f, 0.5f), 0.45f, harmonics: 3, start: 0.05f)
                .Tone(Synth.Ramp(330f, 1320f, 0.45f), Synth.Env(0.02f, 0.35f, 0.5f), 0.25f, start: 0.05f)
                .Noise(Synth.Env(0.05f, 0.2f, 0.6f), Synth.Const(0.5f), 0.25f, start: 0.05f, band: true)
                .Normalized(0.8f));
        }
        ItemAudio.Play(deploy, ItemAudio.SfxBus, volume);
    }

    public static FMOD.Channel StartHum(float volume)
    {
        if (!hum.hasHandle())
        {
            // One second of whole cycles, so it loops cleanly.
            hum = ItemAudio.LoadOrSynthesize(BubbleShieldItem.Instance, "bubble_hum", () => new Synth(1f, 142)
                .Tone(_ => 110f, Synth.Const(1f), 0.6f, harmonics: 4)
                .Tone(_ => 165f, t => 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 3f), 0.25f)
                .Tone(_ => 440f, t => 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 2f), 0.06f)
                .Normalized(0.5f), loop: true);
        }
        return ItemAudio.Play(hum, ItemAudio.SfxBus, volume);
    }

    public static void PlayBlock(float volume)
    {
        if (!block.hasHandle())
        {
            block = ItemAudio.LoadOrSynthesize(BubbleShieldItem.Instance, "bubble_block", () => new Synth(0.6f, 143)
                .Noise(Synth.Decay(0.05f), Synth.Const(0.9f), 0.7f)
                .Tone(Synth.Ramp(1200f, 300f, 0.3f), Synth.Decay(0.15f), 0.6f, harmonics: 3)
                .Tone(_ => 70f, Synth.Decay(0.2f), 0.5f)
                .Normalized(0.75f));
        }
        ItemAudio.Play(block, ItemAudio.SfxBus, volume);
    }

    public static void PlayCollapse(float volume)
    {
        if (!collapse.hasHandle())
        {
            collapse = ItemAudio.LoadOrSynthesize(BubbleShieldItem.Instance, "bubble_collapse", () => new Synth(0.8f, 144)
                .Tone(Synth.Ramp(900f, 120f, 0.4f), Synth.Env(0.01f, 0.1f, 0.5f), 0.6f, harmonics: 3)
                .Noise(Synth.Decay(0.2f), Synth.Ramp(0.8f, 0.05f, 0.5f), 0.4f)
                .Normalized(0.7f));
        }
        ItemAudio.Play(collapse, ItemAudio.SfxBus, volume);
    }
}

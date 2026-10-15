using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Portal Gun: use places an orange portal where you're looking, aim + use places a blue one. Each player
/// has their own pair. You can see through linked portals, and players, balls, carts and items that go
/// through keep their momentum. Portals last until replaced or until the hole ends.
/// </summary>
internal class PortalGunItem : CustomItem
{
    public override int Id => 101;
    public override string Key => "PortalGun";
    public override string DisplayName => "Portal Gun";
    // The dueling pistol gives us a one-handed gun hold, aiming and a shot animation.
    public override ItemType TemplateItem => ItemType.DuelingPistol;
    public override EquipmentType TemplateEquipment => EquipmentType.DuelingPistol;
    public override string AssetName => "portal_gun";
    public override ReticleType AimReticle => ReticleType.DuelingPistol;

    protected override float DefaultItemBoxChance => 0.08f;
    protected override int DefaultUses => 8;
    protected override float DefaultHeldScale => 1f;

    internal static ConfigEntry<float> PortalWidth;
    internal static ConfigEntry<float> PortalHeight;
    internal static ConfigEntry<float> MaxRange;
    internal static ConfigEntry<bool> SeeThrough;
    internal static ConfigEntry<float> RenderScale;

    private const float ShotCooldown = 0.3f;

    protected override void BindConfig()
    {
        PortalWidth = Bind("PortalWidth", 1.5f, "Portal width (meters).");
        PortalHeight = Bind("PortalHeight", 2.3f, "Portal height (meters).");
        MaxRange = Bind("MaxRange", 120f, "How far the gun can place portals (meters).");
        SeeThrough = Bind("SeeThrough", true, "Show the view through linked portals. Turn off if it costs too much frame rate. Local setting.");
        RenderScale = Bind("RenderScale", 0.75f, "Resolution of the view through portals relative to the screen (0.25-1). Local setting.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
        data.IsExplosive = false;
        data.CanHitProjectiles = false;
        data.CanUsageAffectBalls = false;
    }

    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        PlayerInfo player = inventory.PlayerInfo;
        inventory.SetCurrentItemUse(ItemUseType.Regular);
        byte color = (byte)(aiming ? 0 : 1);
        Vector3 muzzle = player.RightHandEquipmentSwitcher.transform.position;

        if (TryFindPlacement(player, color, out Vector3 position, out Quaternion rotation, out Vector3 aimPoint))
        {
            ItemUse.ConsumeUse(inventory);
            PortalNet.RequestPlace(color, position, rotation);
            player.PlayerAudio.PlayPistolShotForAllClients();
        }
        else
        {
            // Fizzle: no portal, no use taken.
            PortalBeams.Show(muzzle, aimPoint, new Color(0.6f, 0.6f, 0.6f), 0.12f);
        }

        yield return new WaitForSeconds(ShotCooldown);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages() => PortalNet.InitSerializers();
    public override void OnStartServer() => PortalNet.RegisterServerHandlers();
    public override void OnStopServer() => PortalNet.ResetServer();
    public override void OnStartClient() => PortalNet.RegisterClientHandlers();
    public override void OnStopClient() => Portals.Clear(0);
    public override void OnServerReady(NetworkConnectionToClient connection) => PortalNet.SendExisting(connection);
    public override void OnServerDisconnect(NetworkConnectionToClient connection) => PortalNet.OnDisconnect(connection);
    public override void Tick()
    {
        Portals.Tick();
        PortalBeams.Tick();
    }
    public override void FixedTick() => PortalTravel.FixedTick();

    // ---- placement ----

    private static readonly RaycastHit[] hits = new RaycastHit[32];
    private const int FitSamples = 16;
    private const int FitIterations = 8;
    private const float FitStep = 0.2f;

    /// <summary>
    /// Finds where a portal shot from the camera lands, oriented to the surface and nudged so the whole
    /// portal sits flat on it (like Portal, shots near an edge slide the portal inward).
    /// </summary>
    private static bool TryFindPlacement(PlayerInfo player, byte color, out Vector3 position, out Quaternion rotation, out Vector3 aimPoint)
    {
        position = default;
        rotation = default;
        Camera cam = GameManager.Camera;
        var ray = new Ray(cam.transform.position, cam.transform.forward);
        aimPoint = ray.origin + ray.direction * MaxRange.Value;
        int mask = GetSurfaceMask();

        if (!TryRaycastSurface(ray, MaxRange.Value, mask, player, out RaycastHit hit))
        {
            return false;
        }
        aimPoint = hit.point;
        if (hit.rigidbody != null && !hit.rigidbody.isKinematic)
        {
            return false; // no portals on things that move
        }

        Vector3 normal = hit.normal;
        Vector3 up = Mathf.Abs(normal.y) < 0.75f
            ? Vector3.ProjectOnPlane(Vector3.up, normal)
            : Vector3.ProjectOnPlane(cam.transform.forward, normal); // floor/ceiling: top points away from you
        if (up.sqrMagnitude < 0.0001f)
        {
            up = Vector3.ProjectOnPlane(player.transform.forward, normal);
        }
        rotation = Quaternion.LookRotation(normal, up.normalized);
        position = hit.point;

        float a = PortalWidth.Value * 0.5f;
        float b = PortalHeight.Value * 0.5f;
        Vector3 right = rotation * Vector3.right;
        Vector3 upAxis = rotation * Vector3.up;
        for (int iteration = 0; iteration < FitIterations; iteration++)
        {
            Vector3 push = Vector3.zero;
            int failures = 0;
            for (int i = 0; i < FitSamples; i++)
            {
                float angle = i * Mathf.PI * 2f / FitSamples;
                Vector3 offset = right * (Mathf.Cos(angle) * a) + upAxis * (Mathf.Sin(angle) * b);
                if (!IsOnSurface(position + offset, normal, position, mask))
                {
                    push -= offset;
                    failures++;
                }
            }
            Portal other = Portals.Get(player.netId, (byte)(1 - color));
            if (other != null && Vector3.Dot(other.Transform.forward, normal) > 0.9f)
            {
                Vector3 apart = Vector3.ProjectOnPlane(position - other.Transform.position, normal);
                float minApart = Mathf.Max(a, b) * 2f + 0.1f;
                if (apart.magnitude < minApart && Mathf.Abs(Vector3.Dot(position - other.Transform.position, normal)) < 0.3f)
                {
                    push += (apart.sqrMagnitude > 0.0001f ? apart.normalized : right) * (minApart - apart.magnitude) * 4f;
                    failures++;
                }
            }
            if (failures == 0)
            {
                position += normal * 0.005f;
                return true;
            }
            if (failures > FitSamples / 2 + 2 || push.sqrMagnitude < 0.0001f)
            {
                return false; // surface too small
            }
            position += Vector3.ProjectOnPlane(push, normal).normalized * FitStep;
        }
        return false;
    }

    private static bool TryRaycastSurface(Ray ray, float range, int mask, PlayerInfo player, out RaycastHit best)
    {
        best = default;
        best.distance = float.MaxValue;
        int count = Physics.RaycastNonAlloc(ray, hits, range, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = hits[i];
            if (candidate.distance >= best.distance || candidate.collider.transform.IsChildOf(player.transform))
            {
                continue;
            }
            best = candidate;
        }
        return best.distance < float.MaxValue;
    }

    private static bool IsOnSurface(Vector3 point, Vector3 normal, Vector3 center, int mask)
    {
        if (!Physics.Raycast(point + normal * 0.3f, -normal, out RaycastHit hit, 0.6f, mask, QueryTriggerInteraction.Ignore))
        {
            return false;
        }
        return Mathf.Abs(Vector3.Dot(hit.point - center, normal)) < 0.12f && Vector3.Dot(hit.normal, normal) > 0.85f;
    }

    private static int surfaceMask = -1;

    private static int GetSurfaceMask()
    {
        if (surfaceMask == -1)
        {
            LayerSettings layers = GameManager.LayerSettings;
            int mask = Physics.DefaultRaycastLayers;
            mask &= ~layers.PlayersMask & ~layers.GolfCartsMask & ~layers.BallMask & ~layers.DynamicBallMask & ~layers.FoliageMask;
            mask &= ~(1 << layers.ItemsLayer) & ~(1 << layers.VfxLayer) & ~(1 << layers.HittablesLayer)
                & ~(1 << layers.ElectromagnetShieldLayer) & ~(1 << layers.GolfCartPassengersLayer);
            surfaceMask = mask;
        }
        return surfaceMask;
    }
}

/// <summary>Short-lived glowing beams from the gun to where the shot landed.</summary>
internal static class PortalBeams
{
    private const float Duration = 0.18f;
    private static readonly List<(LineRenderer line, float start)> beams = new();
    private static Material material;

    public static void Show(Vector3 from, Vector3 to, Color color, float width)
    {
        material ??= UnlitMaterials.Additive(UnlitMaterials.SoftDot, Color.white);
        var go = new GameObject("PortalBeam");
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.positionCount = 2;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        line.startWidth = width;
        line.endWidth = width * 0.6f;
        line.startColor = color;
        line.endColor = color;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        beams.Add((line, Time.time));
    }

    public static void Tick()
    {
        for (int i = beams.Count - 1; i >= 0; i--)
        {
            (LineRenderer line, float start) = beams[i];
            float age = (Time.time - start) / Duration;
            if (line == null || age >= 1f)
            {
                if (line != null)
                {
                    Object.Destroy(line.gameObject);
                }
                beams.RemoveAt(i);
                continue;
            }
            Color c = line.startColor;
            c.a = 1f - age;
            line.startColor = c;
            line.endColor = c;
        }
    }
}

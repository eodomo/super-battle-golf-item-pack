using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Portal Gun: left click places a blue portal where you're looking, right click an orange one, like Portal.
/// Each player has their own pair. You can see through linked portals, and players, balls, carts and items
/// that go through keep their momentum. You can stand halfway in a portal: you only come out of the other one
/// once your middle passes through (see <see cref="PortalTravel"/>). Portals last until replaced or until the hole ends.
///
/// You can shoot as often as you like; the gun counts as used up once you've placed both a blue and an orange portal
/// (the portals stay).
///
/// The gun reads the mouse itself (the game only has one "use" button, and right click is its aim button),
/// so it doesn't enter aim mode and draws its own Portal-style crosshair.
/// </summary>
internal class PortalGunItem : CustomItem
{
    public override int Id => 101;
    public override string Key => "PortalGun";
    public override string DisplayName => "Portal Gun";
    // Mobility: helps you catch up or get back to your ball.
    public override PoolWeights BoxWeights => new(close: 2, medium: 5, far: 6, veryFar: 5, aheadOfBall: 5, drivingRange: 20);
    // The dueling pistol gives us a one-handed gun hold and a shot animation.
    public override ItemType TemplateItem => ItemType.DuelingPistol;
    public override EquipmentType TemplateEquipment => EquipmentType.DuelingPistol;
    public override string AssetName => "portal_gun";
    public override bool HandlesOwnInput => true;
    public override bool SuppressAiming => true;

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 1f;

    public static PortalGunItem Instance { get; private set; }

    public PortalGunItem() => Instance = this;

    internal static ConfigEntry<float> SoundVolume;
    internal static ConfigEntry<float> PortalWidth;
    internal static ConfigEntry<float> PortalHeight;
    internal static ConfigEntry<float> MaxRange;
    internal static ConfigEntry<bool> SeeThrough;
    internal static ConfigEntry<float> RenderScale;

    private const float ShotCooldown = 0.3f;
    private static float nextShotTime;

    // Which colors the local player has placed with the gun they're holding; both means it's used up.
    private static readonly bool[] placedWithThisGun = new bool[2];

    protected override void BindConfig()
    {
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the portal gun's shots (0-1). Local setting.");
        PortalWidth = Bind("PortalWidth", 1.5f, "Portal width (meters).");
        PortalHeight = Bind("PortalHeight", 2.3f, "Portal height (meters).");
        MaxRange = Bind("MaxRange", 500f, "How far the gun can place portals (meters).");
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

    // Firing comes from HandleMouse; the game's use button is swallowed (HandlesOwnInput).
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        yield break;
    }

    /// <summary>Left click: blue. Right click: orange. Only while playing (not in menus, chat or the item menu).</summary>
    private static void HandleMouse()
    {
        Mouse mouse = Mouse.current;
        PlayerInventory inventory = GameManager.LocalPlayerInventory;
        if (mouse == null || inventory == null || !IsHoldingPortalGun(inventory) || InputManager.CurrentMode != InputMode.Regular)
        {
            return;
        }
        int color = mouse.leftButton.wasPressedThisFrame ? 0 : mouse.rightButton.wasPressedThisFrame ? 1 : -1;
        if (color < 0 || Time.time < nextShotTime)
        {
            return;
        }
        // altUse skips the "needs aiming" check; everything else (knocked out, in a cart, no uses...) still applies.
        if (!inventory.CanUseEquippedItem(altUse: true, isAirhornReaction: false, out _, out _, out _, out _))
        {
            return;
        }
        nextShotTime = Time.time + ShotCooldown;
        inventory.ItemUseTimestamp = Time.timeAsDouble;
        inventory.CancelItemUse();
        inventory.itemUseRoutine = inventory.StartCoroutine(FireRoutine(inventory, (byte)color));
        inventory.PlayerInfo.CancelEmote(canHideEmoteMenu: false);
        inventory.CancelItemFlourish();
    }

    private static bool IsHoldingPortalGun(PlayerInventory inventory) =>
        ItemCatalog.TryGet(inventory.GetEffectivelyEquippedItem(), out CustomItem item) && item is PortalGunItem;

    private static IEnumerator FireRoutine(PlayerInventory inventory, byte color)
    {
        PlayerInfo player = inventory.PlayerInfo;
        ItemUse.BreakStealth(inventory);
        inventory.SetCurrentItemUse(ItemUseType.Regular);
        player.Movement.AlignWithCameraImmediately();
        Vector3 muzzle = player.RightHandEquipmentSwitcher.transform.position;

        bool usedUp = false;
        if (TryFindPlacement(player, color, out Vector3 position, out Quaternion rotation, out Vector3 aimPoint))
        {
            PortalNet.RequestPlace(color, position, rotation); // everyone hears the shot when the portal arrives
            placedWithThisGun[color] = true;
            if (placedWithThisGun[0] && placedWithThisGun[1])
            {
                // Both portals are out: that's the use. The portals stay until the hole ends.
                placedWithThisGun[0] = placedWithThisGun[1] = false;
                ItemUse.ConsumeUse(inventory);
                usedUp = true;
            }
        }
        else
        {
            // Fizzle: no portal.
            Beams.Show(muzzle, aimPoint, new Color(0.6f, 0.6f, 0.6f), 0.12f);
        }

        yield return new WaitForSeconds(usedUp ? 0.5f : ShotCooldown);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages() => PortalNet.InitSerializers();
    public override void OnStartServer() => PortalNet.RegisterServerHandlers();
    public override void OnStopServer() => PortalNet.ResetServer();
    public override void OnStartClient() => PortalNet.RegisterClientHandlers();
    public override void OnStopClient()
    {
        Portals.Clear(0);
        placedWithThisGun[0] = placedWithThisGun[1] = false;
    }
    public override void OnServerReady(NetworkConnectionToClient connection) => PortalNet.SendExisting(connection);
    public override void OnServerDisconnect(NetworkConnectionToClient connection) => PortalNet.OnDisconnect(connection);
    public override void Tick()
    {
        Portals.Tick();
        HandleMouse();
    }
    public override void FixedTick() => PortalTravel.FixedTick();
    public override void LateTick() => PortalClones.LateTick();

    // ---- crosshair ----

    private static readonly Texture2D[] crosshairs = new Texture2D[4];

    /// <summary>Portal's crosshair: a blue arc on the left and an orange one on the right, bright once that portal is placed.</summary>
    public override void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
        {
            return;
        }
        PlayerInventory inventory = GameManager.LocalPlayerInventory;
        if (inventory == null || !IsHoldingPortalGun(inventory) || InputManager.CurrentMode != InputMode.Regular
            || !inventory.PlayerInfo.Movement.IsVisible)
        {
            return;
        }
        uint owner = inventory.PlayerInfo.netId;
        int state = (Portals.Get(owner, 0) != null ? 1 : 0) | (Portals.Get(owner, 1) != null ? 2 : 0);
        float size = Screen.height * 0.05f;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect((Screen.width - size) * 0.5f, (Screen.height - size) * 0.5f, size, size), GetCrosshair(state));
    }

    private static Texture2D GetCrosshair(int state)
    {
        if (crosshairs[state] != null)
        {
            return crosshairs[state];
        }
        const int size = 96;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false) { name = $"PortalCrosshair{state}", wrapMode = TextureWrapMode.Clamp };
        var pixels = new Color32[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg; // 0 = right
                Color c = Color.clear;
                if (r < 0.09f)
                {
                    c = new Color(1f, 1f, 1f, 0.9f); // center dot
                }
                else
                {
                    bool left = Mathf.Abs(Mathf.DeltaAngle(angle, 180f)) < 62f;
                    bool right = Mathf.Abs(Mathf.DeltaAngle(angle, 0f)) < 62f;
                    if (left || right)
                    {
                        int color = left ? 0 : 1;
                        bool placed = (state & (1 << color)) != 0;
                        float inner = placed ? 0.62f : 0.72f; // placed: thicker and solid
                        if (r > inner && r < 0.86f)
                        {
                            Color tint = Portals.Colors[color];
                            c = new Color(tint.r, tint.g, tint.b, placed ? 1f : 0.55f);
                        }
                    }
                }
                pixels[y * size + x] = c;
            }
        }
        tex.SetPixels32(pixels);
        tex.Apply();
        crosshairs[state] = tex;
        return tex;
    }

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

using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

internal enum SaberColor : byte
{
    Blue,
    Green,
    Red,
}

/// <summary>
/// Lightsaber: a blue, green or red one at random. Use to swing (aim to swing where you're looking). Anyone it
/// connects with is killed (sent back to their spawn, like a railgun hit) and falls apart in two at the cut; golf
/// carts are cut in half and destroyed (their passengers are killed too). Swing as often as you like; only a hit uses
/// it up. Aim (right click) and use to throw it: it spins out along your aim and comes back like a boomerang if it doesn't hit anyone.
///
/// The three colors are three item types that share one name, settings section and item box entry, so a pickup's
/// color is part of the item itself: everyone sees the same color, and a dropped saber keeps it.
/// The swing is a quick sideways slash posed by <see cref="SaberBlade"/>.
/// </summary>
internal class LightsaberItem : CustomItem
{
    public static LightsaberItem Primary { get; private set; }

    public static readonly Color[] BladeColors =
    {
        new(0.2f, 0.5f, 1f),
        new(0.25f, 1f, 0.3f),
        new(1f, 0.12f, 0.1f),
    };

    internal const int FirstId = 109;
    private const float HiltTop = 0.13f;

    public readonly SaberColor Variant;

    public LightsaberItem(SaberColor color)
    {
        Variant = color;
        Primary ??= this;
    }

    private bool IsPrimary => Primary == this;

    public override int Id => FirstId + (int)Variant;
    public override string Key => "Lightsaber";
    public override string DisplayName => "Lightsaber";
    // Melee: only useful up close.
    public override PoolWeights BoxWeights => new(close: 6, medium: 4, far: 0, veryFar: 0, aheadOfBall: 0, drivingRange: 20);
    public override string MenuName => $"Lightsaber ({Variant})";
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => $"lightsaber_{Variant.ToString().ToLowerInvariant()}";
    protected override bool HasMesh => false;
    // One "Lightsaber" entry in the item boxes (and the setup menu's sliders); the color is picked when it comes out.
    public override bool ListedInItemBoxes => IsPrimary;
    public override ItemType RollFromItemBox() => (ItemType)(FirstId + Random.Range(0, 3));

    // Left click swings; hold right click to aim (the pistol's crosshair) and left click to throw. Both buttons are read
    // in Tick (the game's use button is swallowed).
    public override bool HandlesOwnInput => true;
    public override ReticleType AimReticle => ReticleType.DuelingPistol;

    protected override int DefaultUses => 1;

    public static bool IsLightsaber(ItemType type) => (int)type >= FirstId && (int)type < FirstId + 3;

    internal static ConfigEntry<float> Reach;
    internal static ConfigEntry<float> ThrowRange;
    internal static ConfigEntry<float> BladeLength;
    internal static ConfigEntry<bool> CutCarts;
    internal static ConfigEntry<float> PieceSeconds;
    internal static ConfigEntry<float> SoundVolume;

    private static ModelAsset hilt;
    private static float nextSwing;

    protected override void BindConfig()
    {
        // The three colors share one section; only bind once.
        if (!IsPrimary)
        {
            return;
        }
        Reach = Bind("Reach", 2.6f, "How far a swing reaches (meters).");
        ThrowRange = Bind("ThrowRange", 40f, "How far a thrown saber flies before it comes back (meters).");
        BladeLength = Bind("BladeLength", 1f, "Length of the blade (meters).");
        CutCarts = Bind("CutCarts", true, "Whether swings cut golf carts in half (destroying them). Host setting.");
        PieceSeconds = Bind("PieceSeconds", 6f, "How long the two halves of a cut golf cart stay around. Local setting.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the hum, swings and cuts (0-1). Local setting.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
        data.IsExplosive = false;
        data.CanHitProjectiles = false;
        data.CanUsageAffectBalls = false;
    }

    protected override void RegisterMessages()
    {
        if (!IsPrimary)
        {
            return;
        }
        SaberNet.InitSerializers();
        hilt = ModelAsset.Load(AssetDir, "lightsaber_hilt", loadIcon: false);
    }

    public override void OnStartServer()
    {
        if (IsPrimary)
        {
            SaberNet.RegisterServerHandlers();
        }
    }

    public override void OnStopServer()
    {
        if (IsPrimary)
        {
            SaberNet.ResetServer();
        }
    }

    public override void OnStartClient()
    {
        if (IsPrimary)
        {
            SaberNet.RegisterClientHandlers();
        }
    }

    public override void OnStopClient()
    {
        if (IsPrimary)
        {
            SaberSlicer.ClearAll();
            SaberThrows.ClearAll();
        }
    }

    public override void Tick()
    {
        if (IsPrimary)
        {
            SaberSlicer.Tick();
            SaberThrows.Tick();
            TrySwing();
            TryThrow();
        }
    }

    // ---- model ----

    public override GameObject CreateHeldModel(Transform parent, float scale)
    {
        GameObject root = CreateHilt(parent, scale, "Lightsaber");
        float length = BladeLength.Value;
        Color color = BladeColors[(int)Variant];

        // The blade grows out of the hilt along +Y; SaberBlade scales this transform to ignite it.
        var blade = new GameObject("Blade").transform;
        blade.SetParent(root.transform, false);
        blade.localPosition = new Vector3(0f, HiltTop, 0f);
        Renderer core = BladePart(blade, length, 0.034f, UnlitMaterials.Opaque(null, Color.Lerp(color, Color.white, 0.7f) * 1.5f));
        BladePart(blade, length, 0.075f, GlowMaterial(color, 0.45f));
        BladePart(blade, length, 0.14f, GlowMaterial(color, 0.14f));

        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(blade, false);
        lightGo.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = 3f;
        light.intensity = 1.6f;
        light.shadows = LightShadows.None;

        var tipGo = new GameObject("Tip");
        tipGo.transform.SetParent(blade, false);
        tipGo.transform.localPosition = new Vector3(0f, length, 0f);
        var trail = tipGo.AddComponent<TrailRenderer>();
        trail.sharedMaterial = GlowMaterial(Color.white, 1f);
        trail.time = 0.12f;
        trail.minVertexDistance = 0.04f;
        trail.widthMultiplier = length * 0.9f;
        trail.widthCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 0f);
        trail.colorGradient = new Gradient
        {
            colorKeys = new[] { new GradientColorKey(color, 0f), new GradientColorKey(color, 1f) },
            alphaKeys = new[] { new GradientAlphaKey(0.5f, 0f), new GradientAlphaKey(0f, 1f) },
        };
        trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.emitting = false;

        var saber = root.AddComponent<SaberBlade>();
        saber.Blade = blade;
        saber.Core = core;
        saber.Glow = light;
        saber.Trail = trail;
        saber.Length = length;
        return root;
    }

    // Lying on the ground it's switched off: just the hilt.
    public override GameObject CreateDroppedModel(Transform parent) => CreateHilt(parent, HeldScale.Value, "LightsaberHilt");

    private static GameObject CreateHilt(Transform parent, float scale, string name)
    {
        var root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * scale;
        if (hilt?.Mesh != null)
        {
            hilt.InitMaterials(ModelAsset.SharedTemplate);
            hilt.CreateModel(root.transform, 1f);
        }
        else
        {
            // No model file: a plain metal cylinder.
            GameObject handle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.Destroy(handle.GetComponent<Collider>());
            handle.transform.SetParent(root.transform, false);
            handle.transform.localScale = new Vector3(0.045f, HiltTop, 0.045f);
            handle.GetComponent<MeshRenderer>().sharedMaterial = UnlitMaterials.Opaque(null, new Color(0.62f, 0.63f, 0.66f));
        }
        return root;
    }

    private static Renderer BladePart(Transform blade, float length, float thickness, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Object.Destroy(part.GetComponent<Collider>());
        part.transform.SetParent(blade, false);
        // A capsule is 2 tall including its rounded ends; keep the rounded base inside the hilt.
        part.transform.localPosition = new Vector3(0f, length * 0.5f, 0f);
        part.transform.localScale = new Vector3(thickness, length * 0.5f + thickness * 0.25f, thickness);
        var renderer = part.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return renderer;
    }

    private static readonly Dictionary<(Color, float), Material> softGlowMaterials = new();

    /// <summary>Like <see cref="GlowMaterial"/> but fading out towards the edge of a round quad (no hard square outline).</summary>
    public static Material SoftGlowMaterial(Color color, float alpha)
    {
        if (!softGlowMaterials.TryGetValue((color, alpha), out Material material) || material == null)
        {
            material = UnlitMaterials.Additive(UnlitMaterials.SoftDot, new Color(color.r, color.g, color.b, alpha));
            softGlowMaterials[(color, alpha)] = material;
        }
        return material;
    }

    private static readonly Dictionary<(Color, float), Material> glowMaterials = new();

    /// <summary>Additive glow in <paramref name="color"/> at <paramref name="alpha"/> strength (shared per color).</summary>
    public static Material GlowMaterial(Color color, float alpha)
    {
        if (!glowMaterials.TryGetValue((color, alpha), out Material material) || material == null)
        {
            material = UnlitMaterials.Additive(null, new Color(color.r, color.g, color.b, alpha));
            glowMaterials[(color, alpha)] = material;
        }
        return material;
    }

    // ---- swinging ----

    // Swings are free; a pickup's uses only go down when a swing hits someone (or a cart).
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        if (Time.time < nextSwing)
        {
            yield break;
        }
        PlayerInfo player = inventory.PlayerInfo;
        if (SaberThrows.LocalActive)
        {
            yield break; // it's still flying
        }
        player.Movement.AlignWithCameraImmediately();
        ItemUse.BreakStealth(inventory);
        // Alternate forehands and backhands, each picked at random from its kind.
        lastForehand = !lastForehand;
        int style = Random.Range(0, SaberBlade.StyleCount / 2) * 2 + (lastForehand ? 0 : 1);
        SaberNet.Swing(player, (byte)style);
        nextSwing = Time.time + 0.32f;
        yield return new WaitForSeconds(SaberBlade.StrikeSeconds);

        Strike(inventory, style);
        yield return new WaitForSeconds(0.1f);
        ItemUse.Finish(inventory);
    }

    private static bool lastForehand;
    private static readonly Collider[] overlap = new Collider[64];
    private static readonly HashSet<Hittable> struck = new();
    private static readonly List<(Hittable hittable, Vector3 point, Vector3 flat)> targets = new();

    private void Strike(PlayerInventory inventory, int style)
    {
        PlayerInfo player = inventory.PlayerInfo;
        Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        Vector3 chest = player.transform.position + Vector3.up * 1.1f;
        SaberBlade blade = player.RightHandEquipmentSwitcher.GetComponentInChildren<SaberBlade>();
        Vector3 contact = blade != null ? blade.BladeMiddle : chest + forward * 0.8f;
        float reach = Reach.Value;

        LayerSettings layers = GameManager.LayerSettings;
        int mask = layers.PlayersMask | (CutCarts.Value || !NetworkServer.active ? layers.GolfCartsMask : 0);
        int count = Physics.OverlapSphereNonAlloc(chest + forward * (reach * 0.5f), reach * 0.65f, overlap, mask, QueryTriggerInteraction.Ignore);
        struck.Clear();
        targets.Clear();
        for (int i = 0; i < count; i++)
        {
            Hittable hittable = overlap[i].GetComponentInParent<Hittable>();
            if (hittable == null || hittable.AsEntity == player.AsEntity || !struck.Add(hittable))
            {
                continue;
            }
            Entity entity = hittable.AsEntity;
            if (entity.IsPlayer && entity.PlayerInfo.ActiveGolfCartSeat.IsValid())
            {
                continue; // whoever's in a cart goes down with the cart
            }
            if (!entity.IsPlayer && !entity.IsGolfCart)
            {
                continue;
            }
            if (!hittable.TryGetClosestPointOnAllActiveColliders(contact, out Vector3 point, out _))
            {
                point = overlap[i].ClosestPoint(contact);
            }
            Vector3 toTarget = point - chest;
            Vector3 flat = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            if (toTarget.magnitude > reach || (flat.magnitude > 0.8f && Vector3.Angle(flat, forward) > 95f))
            {
                continue; // behind us or out of reach (the slash sweeps a wide arc in front)
            }
            targets.Add((hittable, point, flat));
        }
        if (targets.Count == 0)
        {
            return; // a miss costs nothing
        }

        player.LocalPlayerCancelSmokeBombInvisibility();
        ulong useHash = inventory.GetEquippedItemUseHash();
        inventory.DecrementUseFromSlotAt(inventory.EquippedItemIndex);
        // Cut along the plane the blade swept through, with a little wobble.
        Vector3 swingNormal = SaberBlade.SwingPlaneNormal(style, forward);
        foreach ((Hittable hittable, Vector3 point, Vector3 flat) in targets)
        {
            Vector3 normal = (swingNormal + Random.insideUnitSphere * 0.15f).normalized;
            if (hittable.AsEntity.IsPlayer)
            {
                Vector3 direction = flat.sqrMagnitude > 0.01f ? flat.normalized : forward;
                hittable.HitWithItem(ItemType.Railgun, inventory.IncrementAndGetCurrentItemUseId(ItemType.Railgun),
                    hittable.transform.InverseTransformPoint(point), direction, hittable.transform.InverseTransformPoint(chest),
                    flat.magnitude, inventory, isReflected: false, isInSpecialState: false,
                    canHitWithNoUser: false, hitTimestamp: NetworkTime.time, itemUseHash: useHash);
            }
            SaberNet.RequestCut(hittable.netId, point, normal, (byte)Variant);
        }
        inventory.CmdValidateAndConsumeItemUse(useHash); // after the hits, like the guns
    }

    /// <summary>Left click (not aiming): swing.</summary>
    private void TrySwing()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.isPressed
            || Time.time < nextSwing || SaberThrows.LocalActive
            || !OwnInput.TryGetHolder<LightsaberItem>(out PlayerInventory inventory)
            || inventory.IsUsingItemAtAll || !OwnInput.CanUse(inventory)
            || !ItemCatalog.TryGet(inventory.GetEffectivelyEquippedItem(), out CustomItem item) || item is not LightsaberItem saber)
        {
            return;
        }
        OwnInput.StartUse(inventory, saber.UseRoutine(inventory, false));
    }

    // ---- throwing ----

    /// <summary>Right click (aiming) + left click: throw the saber where you're looking (see <see cref="SaberThrows"/>).</summary>
    private void TryThrow()
    {
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame || !Mouse.current.rightButton.isPressed
            || SaberThrows.LocalActive
            || !OwnInput.TryGetHolder<LightsaberItem>(out PlayerInventory inventory)
            || inventory.IsUsingItemAtAll || !OwnInput.CanUse(inventory)
            || !ItemCatalog.TryGet(inventory.GetEffectivelyEquippedItem(), out CustomItem item) || item is not LightsaberItem saber)
        {
            return;
        }
        OwnInput.StartUse(inventory, saber.ThrowRoutine(inventory));
    }

    private IEnumerator ThrowRoutine(PlayerInventory inventory)
    {
        PlayerInfo player = inventory.PlayerInfo;
        player.Movement.AlignWithCameraImmediately();
        ItemUse.BreakStealth(inventory);
        inventory.SetCurrentItemUse(ItemUseType.Regular); // the freeze bomb's throw animation, played as it flies (no wind-up)

        Camera cam = GameManager.Camera;
        Vector3 origin = SlingerWebs.HandOf(player);
        Vector3 direction = cam != null ? cam.transform.forward : player.transform.forward;
        // Head for what the crosshair is on, so the saber goes where you aim even though it leaves from the hand.
        if (cam != null && Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit aim, ThrowRange.Value + 10f, ~0, QueryTriggerInteraction.Ignore)
            && !aim.collider.transform.IsChildOf(player.transform) && (aim.point - origin).sqrMagnitude > 4f)
        {
            direction = (aim.point - origin).normalized;
        }
        SaberThrows.LocalThrow(inventory, origin, direction, (byte)Variant);
        // Hands stay empty until the saber is back (or has hit someone, which uses it up).
        while (SaberThrows.LocalActive)
        {
            yield return null;
        }
        // (A hit already finished the use and took the saber away.)
        if (IsLightsaber(inventory.GetEffectiveSlot(inventory.EquippedItemIndex).itemType))
        {
            ItemUse.Finish(inventory);
        }
    }

    /// <summary>A thrown saber hit someone (or a cart): knock them out and cut them, like a swing. Uses the saber up.</summary>
    public static void ThrownHit(PlayerInventory inventory, Hittable hittable, Vector3 point, Vector3 direction, Vector3 source, byte color)
    {
        if (inventory == null || !IsLightsaber(inventory.GetEffectiveSlot(inventory.EquippedItemIndex).itemType))
        {
            return;
        }
        inventory.PlayerInfo.LocalPlayerCancelSmokeBombInvisibility();
        ulong useHash = inventory.GetEquippedItemUseHash();
        inventory.DecrementUseFromSlotAt(inventory.EquippedItemIndex);
        if (hittable.AsEntity.IsPlayer)
        {
            hittable.HitWithItem(ItemType.Railgun, inventory.IncrementAndGetCurrentItemUseId(ItemType.Railgun),
                hittable.transform.InverseTransformPoint(point), direction, hittable.transform.InverseTransformPoint(source),
                (point - source).magnitude, inventory, isReflected: false, isInSpecialState: false,
                canHitWithNoUser: false, hitTimestamp: NetworkTime.time, itemUseHash: useHash);
        }
        // The spinning blade cuts flat across.
        SaberNet.RequestCut(hittable.netId, point, (Vector3.up + Random.insideUnitSphere * 0.15f).normalized, color);
        inventory.CmdValidateAndConsumeItemUse(useHash);
        ItemUse.Finish(inventory);
    }
}

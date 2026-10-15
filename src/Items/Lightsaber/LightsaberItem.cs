using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

internal enum SaberColor : byte
{
    Blue,
    Green,
    Red,
}

/// <summary>
/// Lightsaber: a blue, green or red one at random. Use to swing (aim to swing where you're looking). Anyone it
/// connects with is knocked out and falls apart in two at the cut; golf carts are cut in half and destroyed
/// (their passengers get knocked out too). A few swings per pickup.
///
/// The three colors are three item types that share one name, settings section and item box entry, so a pickup's
/// color is part of the item itself: everyone sees the same color, and a dropped saber keeps it.
/// The swing reuses the freeze bomb's throw (an overarm swing of the right hand, which holds the saber).
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

    private const int FirstId = 109;
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
    public override string MenuName => $"Lightsaber ({Variant})";
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => $"lightsaber_{Variant.ToString().ToLowerInvariant()}";
    protected override bool HasMesh => false;
    // One "Lightsaber" entry in the item boxes (and the setup menu's sliders); the color is picked when it comes out.
    public override bool ListedInItemBoxes => IsPrimary;
    public override ItemType RollFromItemBox() => (ItemType)(FirstId + Random.Range(0, 3));

    protected override int DefaultUses => 3;

    public static bool IsLightsaber(ItemType type) => (int)type >= FirstId && (int)type < FirstId + 3;

    internal static ConfigEntry<float> Reach;
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
        }
    }

    public override void Tick()
    {
        if (IsPrimary)
        {
            SaberSlicer.Tick();
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

    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        if (Time.time < nextSwing)
        {
            yield break;
        }
        PlayerInfo player = inventory.PlayerInfo;
        inventory.SetCurrentItemUse(ItemUseType.Regular); // the freeze bomb's overarm throw is our swing
        if (aiming)
        {
            player.Movement.AlignWithCameraImmediately();
        }
        float throwTime = GameManager.ItemSettings.FreezeBombThrowTime;
        float strikeTime = throwTime > 0.05f && throwTime < 1f ? throwTime * 0.85f : 0.2f;
        nextSwing = Time.time + strikeTime + 0.45f;
        yield return new WaitForSeconds(strikeTime);

        ulong useHash = inventory.GetEquippedItemUseHash();
        inventory.DecrementUseFromSlotAt(inventory.EquippedItemIndex);
        Strike(inventory, useHash);
        inventory.CmdValidateAndConsumeItemUse(useHash); // after the hits, like the guns

        yield return new WaitForSeconds(0.4f);
        ItemUse.Finish(inventory);
    }

    private static readonly Collider[] overlap = new Collider[64];
    private static readonly HashSet<Hittable> struck = new();

    private void Strike(PlayerInventory inventory, ulong useHash)
    {
        PlayerInfo player = inventory.PlayerInfo;
        player.LocalPlayerCancelSmokeBombInvisibility();
        Vector3 forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 chest = player.transform.position + Vector3.up * 1.1f;
        SaberBlade blade = player.RightHandEquipmentSwitcher.GetComponentInChildren<SaberBlade>();
        Vector3 contact = blade != null ? blade.BladeMiddle : chest + forward * 0.8f;
        float reach = Reach.Value;

        LayerSettings layers = GameManager.LayerSettings;
        int mask = layers.PlayersMask | (CutCarts.Value || !NetworkServer.active ? layers.GolfCartsMask : 0);
        int count = Physics.OverlapSphereNonAlloc(chest + forward * (reach * 0.5f), reach * 0.65f, overlap, mask, QueryTriggerInteraction.Ignore);
        struck.Clear();
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
            if (toTarget.magnitude > reach || (flat.magnitude > 0.8f && Vector3.Angle(flat, forward) > 75f))
            {
                continue; // behind us or out of reach
            }

            Vector3 normal;
            if (entity.IsPlayer)
            {
                // Through the body where the blade lands, tilted along the swing: one piece slides off the other.
                normal = (Vector3.up + forward * 0.35f + right * Random.Range(-0.35f, 0.35f)).normalized;
                Vector3 direction = flat.sqrMagnitude > 0.01f ? flat.normalized : forward;
                hittable.HitWithItem(ItemType.DuelingPistol, inventory.IncrementAndGetCurrentItemUseId(ItemType.DuelingPistol),
                    hittable.transform.InverseTransformPoint(point), direction, hittable.transform.InverseTransformPoint(chest),
                    hittable.ItemSettings.DuelingPistolMinKnockbackDistance, inventory, isReflected: false, isInSpecialState: false,
                    canHitWithNoUser: false, hitTimestamp: NetworkTime.time, itemUseHash: useHash);
            }
            else
            {
                // The swing comes down in a vertical plane along our facing; carts split across it.
                normal = right;
            }
            SaberNet.RequestCut(hittable.netId, point, normal, (byte)Variant);
        }
    }
}

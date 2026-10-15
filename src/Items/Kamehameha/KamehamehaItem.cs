using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Kamehameha (Dragon Ball): hold left click to gather energy, release to fire a beam where you're aiming.
/// The longer the charge (up to 3 seconds), the wider the beam and the harder it launches everyone it hits.
/// The beam stops at walls and terrain and blasts golf balls along with it.
/// </summary>
internal class KamehamehaItem : CustomItem
{
    public static KamehamehaItem Instance { get; private set; }

    public override int Id => 105;
    public override string Key => "Kamehameha";
    public override string DisplayName => "Kamehameha";
    // The flash camera is held with both hands in front of the chest: close enough to cupped hands.
    public override ItemType TemplateItem => ItemType.FlashCamera;
    public override EquipmentType TemplateEquipment => EquipmentType.FlashCamera;
    public override string AssetName => "kamehameha";
    public override bool HandlesOwnInput => true;
    // Holding right click aims like the dueling pistol (crosshair, slower walk, closer camera), also while charging.
    public override ReticleType AimReticle => ReticleType.DuelingPistol;
    public override bool AimWhileUsing => true;
    protected override bool HasMesh => false;

    protected override int DefaultUses => 1;

    internal static ConfigEntry<float> MaxChargeSeconds;
    internal static ConfigEntry<float> MaxRange;
    internal static ConfigEntry<float> MinRadius;
    internal static ConfigEntry<float> MaxRadius;
    internal static ConfigEntry<float> MinLaunch;
    internal static ConfigEntry<float> MaxLaunch;
    internal static ConfigEntry<float> SoundVolume;

    private static bool charging;
    private static float chargeStart;
    private static readonly RaycastHit[] hits = new RaycastHit[32];

    public KamehamehaItem() => Instance = this;

    protected override void BindConfig()
    {
        MaxChargeSeconds = Bind("MaxChargeSeconds", 3f, "Seconds to reach full power.");
        MaxRange = Bind("MaxRange", 90f, "Beam length (meters); walls and terrain stop it sooner.");
        MinRadius = Bind("MinRadius", 0.6f, "Beam radius with no charge (meters).");
        MaxRadius = Bind("MaxRadius", 2.2f, "Beam radius at full charge (meters).");
        MinLaunch = Bind("MinLaunch", 14f, "Launch speed for players hit with no charge.");
        MaxLaunch = Bind("MaxLaunch", 40f, "Launch speed for players hit at full charge.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the charge and blast (0-1). Local setting.");
    }

    // No model in hand: the energy is the weapon.
    public override GameObject CreateHeldModel(Transform parent, float scale)
    {
        var empty = new GameObject("KamehamehaHands");
        empty.transform.SetParent(parent, false);
        return empty;
    }

    // Dropped, it's a glowing ball of energy (there's nothing to hold).
    public override GameObject CreateDroppedModel(Transform parent)
    {
        GameObject orb = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Object.Destroy(orb.GetComponent<Collider>());
        orb.name = "KamehamehaOrb";
        orb.transform.SetParent(parent, false);
        orb.transform.localScale = Vector3.one * 0.35f;
        orb.GetComponent<MeshRenderer>().sharedMaterial = UnlitMaterials.Opaque(null, new Color(0.45f, 0.75f, 1f) * 1.4f);
        var light = orb.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(0.4f, 0.65f, 1f);
        light.range = 3f;
        light.intensity = 2f;
        return orb;
    }

    // Charging and firing come from Tick (hold and release left click).
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        yield break;
    }

    protected override void RegisterMessages() => KameNet.InitSerializers();
    public override void OnStartServer() => KameNet.RegisterServerHandlers();
    public override void OnStopServer() => KameNet.ResetServer();
    public override void OnStartClient() => KameNet.RegisterClientHandlers();
    public override void OnStopClient() => KameEffects.ClearAll();

    public override void Tick()
    {
        KameEffects.Tick();
        bool holding = OwnInput.TryGetHolder<KamehamehaItem>(out PlayerInventory inventory);        if (charging && (!holding || inventory.PlayerInfo.Movement.IsKnockedOutOrRecovering))
        {
            CancelCharge(inventory);
            return;
        }
        if (!holding || Mouse.current == null)
        {
            return;
        }
        if (!charging && Mouse.current.leftButton.wasPressedThisFrame && OwnInput.CanUse(inventory))
        {
            charging = true;
            chargeStart = Time.time;
            inventory.SetCurrentItemUse(ItemUseType.Regular); // hands up
            KameEffects.SetCharging(inventory.PlayerInfo.netId, true);
            KameNet.SendCharging(true);
        }
        else if (charging && (!Mouse.current.leftButton.isPressed || Time.time - chargeStart > MaxChargeSeconds.Value + 4f))
        {
            charging = false;
            Fire(inventory, Mathf.Clamp01((Time.time - chargeStart) / MaxChargeSeconds.Value));
        }
    }

    private static void CancelCharge(PlayerInventory inventory)
    {
        charging = false;
        if (GameManager.LocalPlayerInfo != null)
        {
            KameEffects.SetCharging(GameManager.LocalPlayerInfo.netId, false);
        }
        KameNet.SendCharging(false);
        if (inventory != null)
        {
            inventory.SetCurrentItemUse(ItemUseType.None);
        }
    }

    private static void Fire(PlayerInventory inventory, float charge)
    {
        PlayerInfo player = inventory.PlayerInfo;
        player.Movement.AlignWithCameraImmediately();
        Vector3 origin = KameEffects.HandsPosition(player);
        Camera cam = GameManager.Camera;
        Vector3 aim = cam.transform.position + cam.transform.forward * MaxRange.Value;
        if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit aimHit, MaxRange.Value + 20f, SolidMask, QueryTriggerInteraction.Ignore)
            && Vector3.Dot(aimHit.point - origin, cam.transform.forward) > 1f)
        {
            aim = aimHit.point;
        }
        Vector3 direction = (aim - origin).normalized;
        float length = Physics.Raycast(origin, direction, out RaycastHit wall, MaxRange.Value, SolidMask, QueryTriggerInteraction.Ignore)
            ? wall.distance
            : MaxRange.Value;
        float radius = Mathf.Lerp(MinRadius.Value, MaxRadius.Value, charge);
        float power = Mathf.Lerp(MinLaunch.Value, MaxLaunch.Value, charge);

        // Everyone in the beam (except us) gets launched along it.
        var targets = new List<uint>();
        int count = Physics.SphereCastNonAlloc(origin - direction * radius, radius, direction, hits, length + radius,
            GameManager.LayerSettings.PlayersMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            PlayerInfo target = hits[i].collider.GetComponentInParent<PlayerInfo>();
            if (target != null && target != player && !targets.Contains(target.netId))
            {
                targets.Add(target.netId);
            }
        }

        ItemUse.ConsumeUse(inventory);
        KameEffects.Fire(player.netId, origin, direction, length, radius);
        KameNet.SendFire(origin, direction, length, radius, power, targets);
        inventory.StartCoroutine(FinishAfter(inventory, 0.8f));
    }

    private static IEnumerator FinishAfter(PlayerInventory inventory, float seconds)
    {
        yield return new WaitForSeconds(seconds);
        ItemUse.Finish(inventory);
    }

    /// <summary>World geometry that stops the beam (not players, balls or items).</summary>
    private static int SolidMask => WorldMask.Solid;
}

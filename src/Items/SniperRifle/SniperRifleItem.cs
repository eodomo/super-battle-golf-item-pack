using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Sniper Rifle, modeled on Halo's: hold aim to scope in (2x, scroll or middle mouse for 10x), use to fire.
/// A 4-round magazine that reloads itself when empty (dropping you out of the scope), shots that punch
/// through players, a lingering vapor trail and a shot everyone on the map hears.
///
/// Hits reuse the Elephant Gun's own hit handling (shields, knockouts, effects) without its recoil dive.
/// The scope reuses the Railgun's first-person zoom (see <see cref="SniperScopePatches"/>) with Halo's
/// overlay drawn on top (<see cref="SniperScope"/>).
/// </summary>
internal class SniperRifleItem : CustomItem
{
    public static SniperRifleItem Instance { get; private set; }

    public override int Id => 103;
    public override string Key => "SniperRifle";
    public override string DisplayName => "Sniper Rifle";
    // The Elephant Gun gives us a two-handed rifle hold, aiming and a shot animation.
    public override ItemType TemplateItem => ItemType.ElephantGun;
    public override EquipmentType TemplateEquipment => EquipmentType.ElephantGun;
    public override string AssetName => "sniper_rifle";

    protected override float DefaultItemBoxChance => 0.07f;
    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 0.6f;

    public const float ZoomInDuration = 0.08f;

    internal static ConfigEntry<int> MagazineSize;
    internal static ConfigEntry<float> ReloadSeconds;
    internal static ConfigEntry<float> ShotCooldown;
    internal static ConfigEntry<float> MaxRange;
    internal static ConfigEntry<int> MaxPenetration;
    internal static ConfigEntry<float> ZoomLow;
    internal static ConfigEntry<float> ZoomHigh;
    internal static ConfigEntry<float> ScopedSensitivity;
    internal static ConfigEntry<float> HipFireSpread;
    internal static ConfigEntry<float> TrailSeconds;
    internal static ConfigEntry<float> SoundVolume;

    private static float nextShotTime;
    private static bool reloading;

    public SniperRifleItem() => Instance = this;

    public static bool IsSniper(ItemType type) => type == (ItemType)103;

    // Scoped in, the scope overlay replaces the game's crosshair.
    public override ReticleType GetAimReticle(PlayerInventory inventory) =>
        inventory.IsInFirstPersonAim ? ReticleType.None : ReticleType.ElephantGun;

    protected override void BindConfig()
    {
        MagazineSize = Bind("MagazineSize", 4, "Rounds per magazine; it reloads itself when empty. Uses per pickup is the total ammo.");
        ReloadSeconds = Bind("ReloadSeconds", 2f, "Reload time.");
        ShotCooldown = Bind("ShotCooldown", 0.5f, "Seconds between shots.");
        MaxRange = Bind("MaxRange", 400f, "How far a shot reaches (meters).");
        MaxPenetration = Bind("MaxPenetration", 2, "How many players one shot can go through.");
        ZoomLow = Bind("ZoomLow", 2f, "First zoom level (magnification). Local setting.");
        ZoomHigh = Bind("ZoomHigh", 10f, "Second zoom level (scroll up or middle mouse). Local setting.");
        ScopedSensitivity = Bind("ScopedSensitivity", 1f, "Mouse sensitivity while scoped, relative to the zoom (1 = scales exactly with it). Local setting.");
        HipFireSpread = Bind("HipFireSpread", 2.5f, "Inaccuracy in degrees when firing without the scope. Scoped shots are exact.");
        TrailSeconds = Bind("TrailSeconds", 1.6f, "How long the vapor trail hangs in the air. Local setting.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the shot, zoom and reload sounds (0-1). Local setting.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
    }

    protected override void RegisterMessages() => SniperNet.InitSerializers();
    public override void OnStartServer() => SniperNet.RegisterServerHandlers();
    public override void OnStopServer() => SniperNet.ResetServer();
    public override void OnStartClient() => SniperNet.RegisterClientHandlers();
    public override void Tick() => SniperScope.Tick();
    public override void OnGUI() => SniperScope.OnGUI();

    /// <summary>Rounds in the magazine and in reserve, for the scope readout.</summary>
    public static string AmmoText()
    {
        if (reloading)
        {
            return "RELOADING";
        }
        PlayerInventory inventory = GameManager.LocalPlayerInventory;
        if (inventory == null)
        {
            return "";
        }
        int remaining = inventory.GetEffectiveSlot(inventory.EquippedItemIndex).remainingUses;
        int mag = Mathf.Max(1, MagazineSize.Value);
        int inMagazine = remaining <= 0 ? 0 : (remaining - 1) % mag + 1;
        return $"{inMagazine} | {Mathf.Max(0, remaining - inMagazine)}";
    }

    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        // The game won't stop a second shot while scoped (the item isn't "in use"), so pace them here.
        if (Time.time < nextShotTime)
        {
            if (inventory.GetEffectiveSlot(inventory.EquippedItemIndex).remainingUses <= 0)
            {
                ItemUse.Finish(inventory);
            }
            yield break;
        }
        nextShotTime = Time.time + ShotCooldown.Value;

        bool scoped = inventory.IsInFirstPersonAim;
        if (!scoped)
        {
            // The shot animation would drop us out of the scope, so only play it from the hip.
            inventory.SetCurrentItemUse(ItemUseType.Regular);
        }
        ulong useHash = inventory.GetEquippedItemUseHash();
        inventory.DecrementUseFromSlotAt(inventory.EquippedItemIndex);
        Shoot(inventory, useHash, scoped);
        inventory.CmdValidateAndConsumeItemUse(useHash); // after the hits, like the Elephant Gun

        yield return new WaitForSeconds(ShotCooldown.Value);

        int remaining = inventory.GetEffectiveSlot(inventory.EquippedItemIndex).remainingUses;
        int mag = Mathf.Max(1, MagazineSize.Value);
        if (remaining > 0 && remaining % mag == 0)
        {
            // Empty magazine: reload, which (like Halo) takes you out of the scope.
            reloading = true;
            inventory.SetCurrentItemUse(ItemUseType.Regular);
            SniperEffects.PlayReload(inventory.transform.position);
            SniperNet.SendReload();
            nextShotTime = Time.time + ReloadSeconds.Value;
            yield return new WaitForSeconds(ReloadSeconds.Value);
            reloading = false;
        }
        ItemUse.Finish(inventory);
    }

    private static readonly RaycastHit[] hits = new RaycastHit[64];
    private static readonly HashSet<Hittable> hitThisShot = new();

    private static void Shoot(PlayerInventory inventory, ulong useHash, bool scoped)
    {
        PlayerInfo player = inventory.PlayerInfo;
        player.LocalPlayerCancelSmokeBombInvisibility();
        int mask = GameManager.LayerSettings.GunHittablesMask;
        Vector3 muzzle = inventory.GetElephantGunBarrelEndPosition();
        Vector3 aimPoint = inventory.GetFirearmAimPoint(muzzle, inventory.GetElephantGunBarrelForward(), MaxRange.Value, mask, out float localYaw);
        if (Mathf.Abs(localYaw) > 45f)
        {
            player.Movement.AlignWithCameraImmediately();
            muzzle = inventory.GetElephantGunBarrelEndPosition();
            aimPoint = inventory.GetFirearmAimPoint(muzzle, inventory.GetElephantGunBarrelForward(), MaxRange.Value, mask, out _);
        }

        // Scoped: shoot straight down the camera's line of sight, so whatever is under the crosshair gets hit.
        Vector3 origin = scoped ? GameManager.Camera.transform.position : muzzle;
        Vector3 direction = (aimPoint - origin).normalized;
        if (!scoped)
        {
            direction = direction.RandomlyRotatedDeg(HipFireSpread.Value);
        }
        var ray = new Ray(origin, direction);
        int count = Physics.RaycastNonAlloc(ray, hits, MaxRange.Value, mask, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, 0, count, Comparer<RaycastHit>.Create((a, b) => a.distance.CompareTo(b.distance)));

        // Walk along the shot: go through players (up to MaxPenetration), stop at anything else.
        Vector3 end = ray.origin + ray.direction * MaxRange.Value;
        int playersHit = 0;
        hitThisShot.Clear();
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = hits[i];
            Hittable hittable = hit.collider.GetComponentInParent<Hittable>();
            if (hittable == null && DirtWorld.TryBreakHit(hit.collider))
            {
                VfxManager.PlayElephantGunHitForAllClients(inventory, new VfxManager.GunShotHitVfxData(null, hitElectromagnetShield: false, default, hit.point));
                end = hit.point;
                break; // a dirt block stops the round (and breaks)
            }
            if (hittable != null && (!inventory.CanHitWithGunshot(hittable, null) || !hitThisShot.Add(hittable)))
            {
                continue; // ourselves, or another collider of something already hit
            }
            bool isPlayer = hittable != null && hittable.AsEntity.IsPlayer;
            Vector3 hitLocal = hittable != null ? hittable.transform.InverseTransformPoint(hit.point) : default;
            if (isPlayer && hittable.AsEntity.PlayerInfo.IsElectromagnetShieldActive)
            {
                VfxManager.PlayElephantGunHitForAllClients(inventory, new VfxManager.GunShotHitVfxData(hittable, hitElectromagnetShield: true, hitLocal, hit.point));
                end = hit.point;
                break; // shields stop the round
            }
            if (hittable != null)
            {
                hittable.HitWithItem(ItemType.ElephantGun, inventory.IncrementAndGetCurrentItemUseId(ItemType.ElephantGun), hitLocal, ray.direction,
                    hittable.transform.InverseTransformPoint(muzzle), hit.distance, inventory, isReflected: false, isInSpecialState: false,
                    canHitWithNoUser: false, hitTimestamp: NetworkTime.time, itemUseHash: useHash);
            }
            VfxManager.PlayElephantGunHitForAllClients(inventory, new VfxManager.GunShotHitVfxData(hittable, hitElectromagnetShield: false, hitLocal, hit.point));
            if (isPlayer && ++playersHit < Mathf.Max(1, MaxPenetration.Value))
            {
                continue;
            }
            end = hit.point;
            break;
        }

        // Sound and vapor trail: here right away, everyone else through the host.
        Vector3 trailStart = scoped ? origin + direction * 1.2f : muzzle;
        SniperEffects.PlayShot(trailStart, end);
        SniperNet.SendShot(trailStart, end);
    }
}

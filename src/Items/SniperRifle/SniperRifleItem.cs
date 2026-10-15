using System.Collections;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Sniper Rifle (Halo style): hold aim to look through a zoomed scope, use to fire. A hitscan shot that
/// knocks out whoever it hits, with none of the Elephant Gun's recoil dive. 4 shots per pickup.
///
/// The shot reuses the Elephant Gun's own hit handling (shields, knockouts, effects, sounds), and the
/// scope reuses the Railgun's first-person zoom (see <see cref="SniperScopePatches"/>).
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
    protected override int DefaultUses => 4;
    protected override float DefaultHeldScale => 0.7f;

    internal static ConfigEntry<float> MaxRange;
    internal static ConfigEntry<float> ZoomFov;
    internal static ConfigEntry<float> ShotCooldown;
    internal static ConfigEntry<float> HipFireSpread;

    public SniperRifleItem() => Instance = this;

    public static bool IsSniper(ItemType type) => type == (ItemType)103;

    public override ReticleType GetAimReticle(PlayerInventory inventory) =>
        inventory.IsInFirstPersonAim ? ReticleType.RailgunZoom : ReticleType.ElephantGun;

    protected override void BindConfig()
    {
        MaxRange = Bind("MaxRange", 300f, "How far a shot reaches (meters).");
        ZoomFov = Bind("ZoomFov", 14f, "Field of view while scoped in (lower = more zoom). 0 uses the Railgun's zoom. Local setting.");
        ShotCooldown = Bind("ShotCooldown", 0.6f, "Seconds between shots.");
        HipFireSpread = Bind("HipFireSpread", 2.5f, "Inaccuracy in degrees when firing without the scope. Scoped shots are exact.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
    }

    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        PlayerInfo player = inventory.PlayerInfo;
        bool scoped = inventory.IsInFirstPersonAim;
        if (!scoped)
        {
            // Playing the shot animation would drop us out of the scope, so only do it from the hip.
            inventory.SetCurrentItemUse(ItemUseType.Regular);
        }
        ulong useHash = inventory.GetEquippedItemUseHash();
        inventory.DecrementUseFromSlotAt(inventory.EquippedItemIndex);
        Shoot(inventory, useHash, scoped);
        inventory.CmdValidateAndConsumeItemUse(useHash); // after the hit, like the Elephant Gun
        player.PlayerAudio.PlayElephantGunShotForAllClients();

        yield return new WaitForSeconds(ShotCooldown.Value);
        ItemUse.Finish(inventory);
    }

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

        // Scoped: shoot straight down the camera's line of sight, so what's in the crosshair gets hit.
        Vector3 origin = scoped ? GameManager.Camera.transform.position : muzzle;
        Vector3 direction = (aimPoint - origin).normalized;
        if (!scoped)
        {
            direction = direction.RandomlyRotatedDeg(HipFireSpread.Value);
        }
        var ray = new Ray(origin, direction);
        int count = Physics.RaycastNonAlloc(ray, PlayerGolfer.raycastHitBuffer, MaxRange.Value, mask, QueryTriggerInteraction.Ignore);

        if (!inventory.TryParseFirearmRaycastResults(PlayerGolfer.raycastHitBuffer, count, null, out RaycastHit hit, out Hittable hittable))
        {
            Vector3 end = ray.origin + ray.direction * MaxRange.Value;
            VfxManager.PlayElephantGunMissForAllClients(inventory, ray.direction);
            player.PlayerAudio.PlayRegularBulletWhizzForAllClients(ItemType.ElephantGun, null, default, end);
            Beams.Show(muzzle, end, new Color(0.55f, 0.8f, 1f), 0.035f);
            return;
        }

        Beams.Show(muzzle, hit.point, new Color(0.55f, 0.8f, 1f), 0.035f);
        bool canHit = hittable != null && inventory.CanHitWithGunshot(hittable, null);
        Vector3 hitLocal = default;
        if (canHit)
        {
            hitLocal = hittable.transform.InverseTransformPoint(hit.point);
            if (hittable.AsEntity.IsPlayer && hittable.AsEntity.PlayerInfo.IsElectromagnetShieldActive)
            {
                // Shields stop the shot (the Elephant Gun bounces it off; we just block it).
                VfxManager.PlayElephantGunHitForAllClients(inventory, new VfxManager.GunShotHitVfxData(hittable, hitElectromagnetShield: true, hitLocal, hit.point));
                player.PlayerAudio.PlayRegularBulletWhizzForAllClients(ItemType.ElephantGun, hittable, hitLocal, hit.point);
                return;
            }
            hittable.HitWithItem(ItemType.ElephantGun, inventory.IncrementAndGetCurrentItemUseId(ItemType.ElephantGun), hitLocal, ray.direction,
                hittable.transform.InverseTransformPoint(muzzle), hit.distance, inventory, isReflected: false, isInSpecialState: false,
                canHitWithNoUser: false, hitTimestamp: NetworkTime.time, itemUseHash: useHash);
        }
        VfxManager.PlayElephantGunHitForAllClients(inventory, new VfxManager.GunShotHitVfxData(hittable, hitElectromagnetShield: false, hitLocal, hit.point));
        player.PlayerAudio.PlayRegularBulletWhizzForAllClients(ItemType.ElephantGun, hittable, hitLocal, hit.point);
    }
}

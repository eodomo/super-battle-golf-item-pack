using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Banana Peel: use to drop a peel at your feet, aim + use to throw one. Anyone running over a peel slips
/// (leg-sweep knockout plus a launch). Peels are synced by <see cref="BananaNet"/> and simulated by
/// <see cref="BananaWorld"/>.
/// </summary>
internal class BananaPeelItem : CustomItem
{
    public static BananaPeelItem Instance { get; private set; }

    public override int Id => 100;
    public override string Key => "BananaPeel";
    public override string DisplayName => "Banana Peel";
    // A trap: most useful in the crowd near the lead.
    public override PoolWeights BoxWeights => new(close: 10, medium: 5, far: 0, veryFar: 0, aheadOfBall: 0, drivingRange: 20);
    // The landmine gives us hold/plant/toss animations and placement-style use rules.
    public override ItemType TemplateItem => ItemType.Landmine;
    public override string AssetName => "banana_peel";

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 0.8f;
    protected override Vector3 DefaultHeldRotation => new(90f, 0f, 0f);

    internal static ConfigEntry<float> TossSpeedMultiplier;
    internal static ConfigEntry<float> SlipRadius;
    internal static ConfigEntry<float> MinSlipSpeed;
    internal static ConfigEntry<float> MinCartSlipSpeed;
    internal static ConfigEntry<float> CartSpinTurns;
    internal static ConfigEntry<float> SlipForwardBoost;
    internal static ConfigEntry<float> SlipUpBoost;
    internal static ConfigEntry<float> ArmDelay;
    internal static ConfigEntry<float> OwnerArmDelay;
    internal static ConfigEntry<int> MaxBananas;
    internal static ConfigEntry<float> GroundScale;

    public BananaPeelItem() => Instance = this;

    protected override void BindConfig()
    {
        TossSpeedMultiplier = Bind("TossSpeedMultiplier", 1.2f, "Throw speed relative to the landmine toss (hold aim + use to throw).");
        SlipRadius = Bind("SlipRadius", 0.75f, "How close (meters, at GroundScale 1) you must step to a peel to slip. Scales with GroundScale.");
        MinSlipSpeed = Bind("MinSlipSpeed", 1.0f, "Minimum running speed (m/s) to slip.");
        MinCartSlipSpeed = Bind("MinCartSlipSpeed", 2.5f, "Minimum speed (m/s) for a golf cart (or the Megazord) to slip on a peel.");
        CartSpinTurns = Bind("CartSpinTurns", 2f, "How many full turns a cart spins when it slips.");
        SlipForwardBoost = Bind("SlipForwardBoost", 6f, "Forward launch speed when slipping.");
        SlipUpBoost = Bind("SlipUpBoost", 6.5f, "Upward launch speed when slipping.");
        ArmDelay = Bind("ArmDelay", 0.4f, "Seconds after landing before anyone can slip.");
        OwnerArmDelay = Bind("OwnerArmDelay", 2.0f, "Seconds after landing before the player who placed it can slip on it.");
        MaxBananas = Bind("MaxBananas", 40, "Oldest peels disappear past this many. Host setting.");
        GroundScale = Bind("GroundScale", 1.2f, "Size of peels on the ground.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
        data.IsExplosive = false;
        data.CanBreakBreakableIce = false;
        data.CanHitProjectiles = false;
        data.CanUsageAffectBalls = false;
    }

    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming) =>
        aiming ? TossRoutine(inventory) : DropRoutine(inventory);

    protected override void RegisterMessages() => BananaNet.InitSerializers();
    public override void OnStartServer() => BananaNet.RegisterServerHandlers();
    public override void OnStopServer() => BananaNet.ResetServer();
    public override void OnStartClient() => BananaNet.RegisterClientHandlers();
    public override void OnStopClient() => BananaWorld.ClearAll();
    public override void Tick() => BananaWorld.Tick();

    // Use routines, modeled on the game's PlantLandmineRoutine / TossLandmineRoutine.

    /// <summary>Use without aiming: drop a peel at your feet.</summary>
    private static IEnumerator DropRoutine(PlayerInventory inventory)
    {
        ItemSettings settings = GameManager.ItemSettings;
        inventory.SetCurrentItemUse(ItemUseType.Regular);
        while (BMath.GetTimeSince(inventory.ItemUseTimestamp) < settings.LandminePlantingStickIntoGroundTime)
        {
            yield return null;
        }

        ItemUse.ConsumeUse(inventory);
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
        Vector3 handPosition = inventory.PlayerInfo.RightHandEquipmentSwitcher.transform.position;
        BananaNet.RequestSpawn(handPosition, inventory.PlayerInfo.Rigidbody.linearVelocity * 0.2f);

        while (BMath.GetTimeSince(inventory.ItemUseTimestamp) < settings.LandminePlantingTime)
        {
            yield return null;
        }
        yield return new WaitForSeconds(settings.LandminePostPlantingTime);
        ItemUse.Finish(inventory);
    }

    /// <summary>Use while aiming: lob a peel forward.</summary>
    private static IEnumerator TossRoutine(PlayerInventory inventory)
    {
        ItemSettings settings = GameManager.ItemSettings;
        PlayerInfo player = inventory.PlayerInfo;
        inventory.SetCurrentItemUse(ItemUseType.Alt);
        player.Movement.AlignWithCameraImmediately();
        float time = 0f;
        while (time < settings.LandmineTossingTime)
        {
            yield return null;
            time += Time.deltaTime;
            player.Movement.AlignWithCameraImmediately();
        }

        ItemUse.ConsumeUse(inventory);
        Vector3 handPosition = player.RightHandEquipmentSwitcher.transform.position;
        Vector3 direction = inventory.transform.TransformDirection(settings.LandmineTossingDirectionLocalRotation * Vector3.forward);
        Vector3 velocity = player.Rigidbody.linearVelocity + direction * (settings.LandmineTossingSpeed * TossSpeedMultiplier.Value);
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
        BananaNet.RequestSpawn(handPosition, velocity);

        yield return new WaitForSeconds(settings.LandminePostTossingTime);
        ItemUse.Finish(inventory);
    }
}

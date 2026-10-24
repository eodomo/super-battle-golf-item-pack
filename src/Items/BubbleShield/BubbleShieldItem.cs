using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Bubble Shield (Halo 3): plant the device at your feet and it throws up a glowing dome for a few seconds. Nothing
/// gets through its edge either way: shots, rockets, explosions, beams, swings and freezes from outside can't knock out
/// anyone inside, and nobody inside can knock out anyone outside. Anyone can walk in and out, and whoever's inside
/// can still fight each other. Golf balls and carts (the Megazord too) bounce off its edge, from either side. See <see cref="ShieldKnockOutPatch"/>.
/// </summary>
internal class BubbleShieldItem : CustomItem
{
    public static BubbleShieldItem Instance { get; private set; }

    public override int Id => 114;
    public override string Key => "BubbleShield";
    public override string DisplayName => "Bubble Shield";
    // Defense: most worth having where the fighting is, near the lead.
    public override PoolWeights BoxWeights => new(close: 3, medium: 2, far: 1, veryFar: 0, aheadOfBall: 0, drivingRange: 20);
    // Planted at your feet like the landmine.
    public override ItemType TemplateItem => ItemType.Landmine;
    public override string AssetName => "bubble_shield";

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 0.9f;

    internal static ConfigEntry<float> Radius;
    internal static ConfigEntry<float> Seconds;
    internal static ConfigEntry<float> SoundVolume;

    public BubbleShieldItem() => Instance = this;

    protected override void BindConfig()
    {
        Radius = Bind("Radius", 4.5f, "Radius of the dome (meters). Host setting.");
        Seconds = Bind("Seconds", 10f, "How long the dome lasts. Host setting.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the dome's sounds (0-1). Local setting.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
        data.IsExplosive = false;
        data.CanBreakBreakableIce = false;
        data.CanHitProjectiles = false;
        data.CanUsageAffectBalls = false;
    }

    // Modeled on the game's PlantLandmineRoutine, like the banana peel's drop.
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        ItemSettings settings = GameManager.ItemSettings;
        inventory.SetCurrentItemUse(ItemUseType.Regular);
        while (BMath.GetTimeSince(inventory.ItemUseTimestamp) < settings.LandminePlantingStickIntoGroundTime)
        {
            yield return null;
        }

        ItemUse.ConsumeUse(inventory);
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
        BubbleNet.RequestPlace(inventory.PlayerInfo.transform.position + inventory.PlayerInfo.transform.forward * 0.4f);

        while (BMath.GetTimeSince(inventory.ItemUseTimestamp) < settings.LandminePlantingTime)
        {
            yield return null;
        }
        yield return new WaitForSeconds(settings.LandminePostPlantingTime);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages() => BubbleNet.InitSerializers();
    public override void OnStartServer() => BubbleNet.RegisterServerHandlers();
    public override void OnStopServer() => BubbleNet.ResetServer();
    public override void OnStartClient() => BubbleNet.RegisterClientHandlers();
    public override void OnStopClient() => BubbleWorld.ClearAll();
    public override void Tick() => BubbleWorld.Tick();
    public override void FixedTick() => BubbleWorld.FixedTick();
}

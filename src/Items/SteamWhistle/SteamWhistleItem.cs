using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

internal enum WhistleTargetMode
{
    Random,
    Nearest,
    Aimed,
}

/// <summary>
/// Steam Whistle: blow it and a winged blue tank engine answers the call, swoops in and sets a player on fire.
/// </summary>
internal class SteamWhistleItem : CustomItem
{
    public static SteamWhistleItem Instance { get; private set; }

    public override int Id => 102;
    public override string Key => "SteamWhistle";
    public override string DisplayName => "Steam Whistle";
    // The airhorn already has a "raise it and blow" animation.
    public override ItemType TemplateItem => ItemType.Airhorn;
    public override EquipmentType TemplateEquipment => EquipmentType.Airhorn;
    public override string AssetName => "steam_whistle";

    protected override int DefaultUses => 1;

    internal static ConfigEntry<WhistleTargetMode> TargetMode;
    internal static ConfigEntry<float> DragonScale;
    internal static ConfigEntry<float> BurnDuration;
    internal static ConfigEntry<float> LaunchUp;
    internal static ConfigEntry<float> LaunchHorizontal;
    internal static ConfigEntry<float> SoundVolume;

    public SteamWhistleItem() => Instance = this;

    protected override void BindConfig()
    {
        TargetMode = Bind("TargetMode", WhistleTargetMode.Random,
            "Who the dragon goes after: a Random other player, the Nearest one, or the one you're Aimed closest to. Host setting.");
        DragonScale = Bind("DragonScale", 1.5f, "Size of the dragon.");
        BurnDuration = Bind("BurnDuration", 4f, "Seconds the target stays on fire.");
        LaunchUp = Bind("LaunchUp", 10f, "Upward launch speed of the fire blast.");
        LaunchHorizontal = Bind("LaunchHorizontal", 7f, "Sideways launch speed of the fire blast.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the whistle and fire (0-1). Local setting.");
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
        inventory.SetCurrentItemUse(ItemUseType.Regular);
        yield return new WaitForSeconds(0.2f); // raise it to your lips

        ItemUse.ConsumeUse(inventory);
        Camera cam = GameManager.Camera;
        WhistleNet.RequestBlow(cam != null ? cam.transform.forward : inventory.transform.forward);

        yield return new WaitForSeconds(1f);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages()
    {
        WhistleNet.InitSerializers();
        DragonAttacks.LoadModels(AssetDir);
    }

    public override void OnStartServer() => WhistleNet.RegisterServerHandlers();
    public override void OnStopServer() => WhistleNet.ResetServer();
    public override void OnStartClient() => WhistleNet.RegisterClientHandlers();
    public override void OnStopClient() => DragonAttacks.ClearAll();
    public override void Tick() => DragonAttacks.Tick();
}

using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Blue Shell (Mario Kart): use it and it goes after whoever's ball is closest to the hole (even you, if
/// you're in the lead). It flies high over the course with its siren wailing, circles over the leader, and
/// slams down: the leader gets blown up and their ball gets knocked away from the hole.
/// </summary>
internal class BlueShellItem : CustomItem
{
    public static BlueShellItem Instance { get; private set; }

    public override int Id => 107;
    public override string Key => "BlueShell";
    public override string DisplayName => "Blue Shell";
    // Held and thrown one-handed like the freeze bomb.
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => "blue_shell";

    protected override float DefaultItemBoxChance => 0.05f;
    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 0.4f;

    internal static ConfigEntry<float> Speed;
    internal static ConfigEntry<float> ExplosionScale;
    internal static ConfigEntry<bool> HitBall;
    internal static ConfigEntry<float> ShellScale;
    internal static ConfigEntry<float> SoundVolume;

    public BlueShellItem() => Instance = this;

    protected override void BindConfig()
    {
        Speed = Bind("Speed", 30f, "Flying speed (m/s); the flight takes 1.5-10 seconds plus the circling. Host setting.");
        ExplosionScale = Bind("ExplosionScale", 1.4f, "Blast radius relative to a rocket launcher rocket. Host setting.");
        HitBall = Bind("HitBall", true, "Also knock the leader's ball away from the hole. Host setting.");
        ShellScale = Bind("ShellScale", 1f, "Size of the flying shell.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the launch, siren and impact (0-1). Local setting.");
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
        yield return new WaitForSeconds(0.25f);
        ItemUse.ConsumeUse(inventory);
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right); // it leaves your hand
        BlueShellNet.RequestLaunch(inventory.PlayerInfo.RightHandEquipmentSwitcher.transform.position);
        yield return new WaitForSeconds(0.5f);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages() => BlueShellNet.InitSerializers();
    public override void OnStartServer() => BlueShellNet.RegisterServerHandlers();
    public override void OnStopServer() => BlueShellNet.ResetServer();
    public override void OnStartClient() => BlueShellNet.RegisterClientHandlers();
    public override void OnStopClient() => BlueShellFlight.ClearAll();

    public override void Tick()
    {
        BlueShellNet.ServerTick();
        BlueShellFlight.Tick();
    }
}

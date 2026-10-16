using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Ender Pearl (Minecraft): throw it and you teleport to wherever it lands, with a puff of purple particles and a
/// "vwoop" where you left and where you arrive. It's thrown on the flattest arc that lands where the crosshair
/// points (like the boogie bomb) and goes through portals. Lands on a wall: you end up at the foot of it.
/// </summary>
internal class EnderPearlItem : CustomItem
{
    public static EnderPearlItem Instance { get; private set; }

    public override int Id => 116;
    public override string Key => "EnderPearl";
    public override string DisplayName => "Ender Pearl";
    // Getting around: catching up from behind, or back to your ball after walking past it.
    public override PoolWeights BoxWeights => new(close: 1, medium: 3, far: 4, veryFar: 4, aheadOfBall: 8, drivingRange: 20);
    // Thrown one-handed like the freeze bomb.
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => "ender_pearl";
    public override ReticleType AimReticle => ReticleType.FreezeBomb;

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 1f;

    internal static ConfigEntry<float> ThrowSpeed;
    internal static ConfigEntry<float> SoundVolume;

    public EnderPearlItem() => Instance = this;

    protected override void BindConfig()
    {
        ThrowSpeed = Bind("ThrowSpeed", 30f, "Throw speed (m/s). It's thrown on the flattest arc that lands where you aim.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the throw and teleport sounds (0-1). Local setting.");
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
        PlayerInfo player = inventory.PlayerInfo;
        inventory.SetCurrentItemUse(ItemUseType.Regular);
        player.Movement.AlignWithCameraImmediately();
        // Thrown the moment you click (no wind-up); the throw animation plays out afterwards.
        ItemUse.ConsumeUse(inventory);
        Vector3 hand = player.RightHandEquipmentSwitcher.transform.position;
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
        PearlNet.RequestThrow(hand, BoogieBombItem.AimedVelocity(player, hand, ThrowSpeed.Value));

        yield return new WaitForSeconds(0.45f);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages() => PearlNet.InitSerializers();
    public override void OnStartServer() => PearlNet.RegisterServerHandlers();
    public override void OnStopServer() => PearlNet.ResetServer();
    public override void OnStartClient() => PearlNet.RegisterClientHandlers();
    public override void OnStopClient() => PearlWorld.ClearAll();
    public override void Tick() => PearlWorld.Tick();
}

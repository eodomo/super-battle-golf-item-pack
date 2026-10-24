using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Boogie Bomb (Fortnite): throw the disco ball; it flies on the same arc as a freeze bomb shot (and goes through portals) and pops on impact, or as soon as it touches another player. Everyone caught in the blast does their victory dance for a few seconds,
/// unable to move or use items (they can still look around). Electromagnet shields protect you.
/// </summary>
internal class BoogieBombItem : CustomItem
{
    public static BoogieBombItem Instance { get; private set; }

    public override int Id => 108;
    public override string Key => "BoogieBomb";
    public override string DisplayName => "Boogie Bomb";
    // Crowd control: best where players bunch up.
    public override PoolWeights BoxWeights => new(close: 8, medium: 6, far: 2, veryFar: 0, aheadOfBall: 0, drivingRange: 20);
    // Thrown one-handed like the freeze bomb.
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => "boogie_bomb";
    public override ReticleType AimReticle => ReticleType.FreezeBomb;

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 1f;

    internal static ConfigEntry<float> Radius;
    internal static ConfigEntry<float> DanceSeconds;
    internal static ConfigEntry<bool> AffectThrower;
    internal static ConfigEntry<float> SoundVolume;

    public BoogieBombItem() => Instance = this;

    protected override void BindConfig()
    {
        Radius = Bind("Radius", 10f, "Blast radius (meters): everyone inside dances.");
        DanceSeconds = Bind("DanceSeconds", 5f, "How long people dance.");
        AffectThrower = Bind("AffectThrower", true, "Whether the thrower dances too if they're caught in their own blast.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the pop and the disco music (0-1). Local setting.");
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

        ItemUse.ConsumeUse(inventory);
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
        FreezeBombThrow(inventory, out Vector3 start, out Vector3 velocity);
        BoogieNet.RequestThrow(start, velocity);

        yield return new WaitForSeconds(0.45f);
        ItemUse.Finish(inventory);
    }

    /// <summary>
    /// The freeze bomb's own throw: its start point (the hand) and velocity (its shot speed along the camera aim, tipped up
    /// a little by the game's pitch offset), so thrown items fly the same arc.
    /// </summary>
    internal static void FreezeBombThrow(PlayerInventory inventory, out Vector3 start, out Vector3 velocity)
    {
        // The game throws from the raised hand mid-animation; ours throws at once, with the hand still low, so start at shoulder height in front of the body.
        Vector3 aim = inventory.GetFreezeBombAimDirection(out _);
        Vector3 flat = Vector3.ProjectOnPlane(aim, Vector3.up).normalized;
        start = inventory.PlayerInfo.transform.position + Vector3.up * 1.45f + flat * 0.5f;
        velocity = aim * GameManager.ItemSettings.FreezeBombShotSpeed;
    }

    protected override void RegisterMessages() => BoogieNet.InitSerializers();
    public override void OnStartServer() => BoogieNet.RegisterServerHandlers();
    public override void OnStopServer() => BoogieNet.ResetServer();
    public override void OnStartClient() => BoogieNet.RegisterClientHandlers();
    public override void OnStopClient() => BoogieWorld.ClearAll();
    public override void Tick() => BoogieWorld.Tick();
}

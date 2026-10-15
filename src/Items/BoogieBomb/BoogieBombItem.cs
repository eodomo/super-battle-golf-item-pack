using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Boogie Bomb (Fortnite): throw the disco ball (aim to throw where you're looking, otherwise it's lobbed
/// forward). It bounces, pops, and everyone caught in the blast does their victory dance for a few seconds,
/// unable to move or use items (they can still look around). Electromagnet shields protect you.
/// </summary>
internal class BoogieBombItem : CustomItem
{
    public static BoogieBombItem Instance { get; private set; }

    public override int Id => 108;
    public override string Key => "BoogieBomb";
    public override string DisplayName => "Boogie Bomb";
    // Thrown one-handed like the freeze bomb.
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => "boogie_bomb";
    public override ReticleType AimReticle => ReticleType.FreezeBomb;
    public override ItemRarity Rarity => ItemRarity.Common;

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 1f;

    internal static ConfigEntry<float> Radius;
    internal static ConfigEntry<float> DanceSeconds;
    internal static ConfigEntry<float> ThrowSpeed;
    internal static ConfigEntry<bool> AffectThrower;
    internal static ConfigEntry<float> SoundVolume;

    public BoogieBombItem() => Instance = this;

    protected override void BindConfig()
    {
        Radius = Bind("Radius", 6f, "Blast radius (meters): everyone inside dances.");
        DanceSeconds = Bind("DanceSeconds", 5f, "How long people dance.");
        ThrowSpeed = Bind("ThrowSpeed", 17f, "Throw speed (m/s).");
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
        if (aiming)
        {
            player.Movement.AlignWithCameraImmediately();
        }
        float throwTime = GameManager.ItemSettings.FreezeBombThrowTime;
        yield return new WaitForSeconds(throwTime > 0.05f && throwTime < 1f ? throwTime : 0.25f);

        ItemUse.ConsumeUse(inventory);
        Vector3 hand = player.RightHandEquipmentSwitcher.transform.position;
        Camera cam = GameManager.Camera;
        Vector3 direction = aiming && cam != null
            ? (cam.transform.forward + Vector3.up * 0.15f).normalized          // where you're looking, slightly arced
            : (player.transform.forward + Vector3.up * 0.55f).normalized;      // a lob in front of you
        Vector3 velocity = direction * ThrowSpeed.Value + player.Rigidbody.linearVelocity * 0.5f;
        inventory.LocalPlayerMarkThrownItem(PlayerInventory.ThrownItemHand.Right);
        BoogieNet.RequestThrow(hand, velocity);

        yield return new WaitForSeconds(0.45f);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages() => BoogieNet.InitSerializers();
    public override void OnStartServer() => BoogieNet.RegisterServerHandlers();
    public override void OnStopServer() => BoogieNet.ResetServer();
    public override void OnStartClient() => BoogieNet.RegisterClientHandlers();
    public override void OnStopClient() => BoogieWorld.ClearAll();
    public override void Tick() => BoogieWorld.Tick();
}

using System.Collections;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Jellyfish Net (SpongeBob): one swing to catch someone. It's a short reach, so you have to walk right up to
/// them. Anyone it catches hangs in the net and is carried around in front of you for 10 seconds; left click
/// throws them out early. A swing that catches nobody uses the net up.
/// The swing is posed by <see cref="JellyfishNetHeld"/>, catching and carrying by <see cref="NetCatches"/>.
/// </summary>
internal class JellyfishNetItem : CustomItem
{
    public static JellyfishNetItem Instance { get; private set; }

    public override int Id => 118;
    public override string Key => "JellyfishNet";
    public override string DisplayName => "Jellyfish Net";
    // Close-range fighting item.
    public override PoolWeights BoxWeights => new(close: 4, medium: 2, far: 0, veryFar: 0, aheadOfBall: 0, drivingRange: 20);
    public override ItemType TemplateItem => ItemType.FreezeBomb;
    public override EquipmentType TemplateEquipment => EquipmentType.FreezeBomb;
    public override string AssetName => "jellyfish_net";
    public override bool HandlesOwnInput => true;
    // Gripped a little up the pole.
    public override Vector3? HeldGrip => new Vector3(0f, 0.22f, 0f);

    protected override int DefaultUses => 1;

    internal static ConfigEntry<float> Reach;
    internal static ConfigEntry<float> CarrySeconds;
    internal static ConfigEntry<float> ThrowSpeed;
    internal static ConfigEntry<float> SoundVolume;
    internal static ConfigEntry<float> CarrySpeedFactor;

    private static ModelAsset frame;

    private static float nextSwing;

    public JellyfishNetItem() => Instance = this;

    protected override void BindConfig()
    {
        Reach = Bind("Reach", 1.6f, "How far a swing reaches (meters). Short: you have to get right up to someone.");
        CarrySeconds = Bind("CarrySeconds", 10f, "How long you carry someone you caught before they're let go. Host setting.");
        ThrowSpeed = Bind("ThrowSpeed", 16f, "How fast a caught player is thrown when you click (m/s).");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the swooshes (0-1). Local setting.");
        CarrySpeedFactor = Bind("CarrySpeedFactor", 0.55f, "How fast you move while carrying someone, as a fraction of normal walking speed.");
    }

    public override void ConfigureItemData(ItemData data)
    {
        data.NonAimUse = ItemNonAimingUse.Use;
        data.IsExplosive = false;
        data.CanHitProjectiles = false;
        data.CanUsageAffectBalls = false;
    }

    public override GameObject CreateHeldModel(Transform parent, float scale)
    {
        // The pole and ring; the net is a cloth simulation that JellyfishNetHeld adds.
        var root = new GameObject("JellyfishNet");
        root.transform.SetParent(parent, false);
        root.transform.localScale = Vector3.one * scale;
        frame?.InitMaterials(ModelAsset.SharedTemplate);
        frame?.CreateModel(root.transform, 1f);
        root.AddComponent<JellyfishNetHeld>();
        return root;
    }

    // Lying on the ground it's just the net, flat.
    public override GameObject CreateDroppedModel(Transform parent) => base.CreateHeldModel(parent, HeldScale.Value);

    protected override void RegisterMessages()
    {
        NetCatchNet.InitSerializers();
        frame = ModelAsset.Load(AssetDir, "jellyfish_net_frame", loadIcon: false);
    }
    public override void OnStartServer() => NetCatchNet.RegisterServerHandlers();
    public override void OnStopServer() => NetCatchNet.ResetServer();
    public override void OnStartClient() => NetCatchNet.RegisterClientHandlers();
    public override void OnStopClient() => NetCatches.Reset();
    public override void OnServerDisconnect(NetworkConnectionToClient connection) => NetCatchNet.OnDisconnect(connection);
    public override void FixedTick() => NetCatches.FixedTick();

    public override void Tick()
    {
        NetCatches.Tick();
        if (Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame
            || !OwnInput.TryGetHolder<JellyfishNetItem>(out PlayerInventory inventory) || !OwnInput.CanUse(inventory))
        {
            return;
        }
        if (NetCatches.LocalCarrying)
        {
            NetCatches.ThrowCaught(inventory);
        }
        else if (Time.time >= nextSwing && !inventory.IsUsingItemAtAll)
        {
            OwnInput.StartUse(inventory, UseRoutine(inventory, false));
        }
    }

    // One swing: a catch keeps the net until the carry ends; a miss uses it up.
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        PlayerInfo player = inventory.PlayerInfo;
        nextSwing = Time.time + 0.6f;
        player.Movement.AlignWithCameraImmediately();
        ItemUse.BreakStealth(inventory);
        NetCatchNet.SendSwing(player);
        yield return new WaitForSeconds(JellyfishNetHeld.StrikeSeconds);

        PlayerInfo target = NetCatches.FindTarget(player);
        if (target != null)
        {
            NetCatchNet.SendCatch(target.netId, inventory.EquippedItemIndex);
            float until = Time.time + 0.7f;
            while (!NetCatches.LocalCarrying && Time.time < until)
            {
                yield return null;
            }
        }
        yield return new WaitForSeconds(JellyfishNetHeld.RecoverSeconds);
        if (NetCatches.LocalCarrying)
        {
            inventory.SetCurrentItemUse(ItemUseType.None); // keep the net: it's carrying someone
            yield break;
        }
        ItemUse.ConsumeUse(inventory);
        ItemUse.Finish(inventory);
    }
}

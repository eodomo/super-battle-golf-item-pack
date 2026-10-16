using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The Megazord, a ride you climb into. Use it like a golf cart: a giant robot stands up where you are and
/// you get inside its cockpit (a second player can take the other seat with the interact key). Driving makes it stomp
/// along; right click strikes a battle pose with a war cry. Under the armor it is a real golf cart (see <see cref="ZordCarts"/>), so entering, seats,
/// driving and syncing are the game's own.
/// </summary>
internal class ZordItem : CustomItem
{
    public static ZordItem Instance { get; private set; }


    public override int Id => 113;
    public override string Key => "Zord";
    public override string DisplayName => "Megazord";
    public override ItemType TemplateItem => ItemType.GolfCart;
    public override EquipmentType TemplateEquipment => EquipmentType.GolfCartWhole;
    public override string AssetName => "zord";

    protected override bool HasMesh => false;
    protected override float DefaultHeldScale => 0.15f;

    internal static ConfigEntry<float> ModelScale;
    internal static ConfigEntry<float> SoundVolume;
    internal static ConfigEntry<float> MaxSpeed, Acceleration, TurnRate, BallPower;

    public ZordItem() => Instance = this;

    protected override void BindConfig()
    {
        ModelScale = Bind("ModelScale", 2f, "How big the Megazord is (1 = about 3 m tall). Everyone should use the same value or the cockpit will sit at different heights.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of its footsteps and war cry (0-1). Local setting.");
        MaxSpeed = Bind("MaxSpeed", 9f, "Top speed in meters per second (a regular golf cart is much faster; the Megazord is slow and heavy).");
        Acceleration = Bind("Acceleration", 4.5f, "How quickly it builds up speed (meters per second, per second). Lower is heavier.");
        TurnRate = Bind("TurnRate", 60f, "Fastest turning rate, degrees per second.");
        BallPower = Bind("BallPower", 0.85f, "How hard a sword swing hits your own ball (0 to 1, like swing power).");
    }

    /// <summary>A small standing Zord for the hand and for the pickup on the ground.</summary>
    public override GameObject CreateHeldModel(Transform parent, float scale)
    {
        if (!ZordParts.EnsureReady(AssetDir))
        {
            return new GameObject("ZordHeld");
        }
        ZordRig rig = ZordRig.Build(parent, scale);
        rig.Root.gameObject.name = "Zord_model";
        return rig.Root.gameObject;
    }

    // The game's own golf cart placement does the rest: spawns the cart on the host, waits for you to get in.
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming) => inventory.PlaceGolfCartRoutine();

    protected override void RegisterMessages() => ZordCarts.InitSerializers();
    public override void OnStartServer() => ZordCarts.RegisterServerHandlers();
    public override void OnStopServer() => ZordCarts.ResetServer();
    public override void OnStartClient() => ZordCarts.RegisterClientHandlers();
    public override void OnStopClient() => ZordCarts.ResetClient();
    public override void OnServerReady(NetworkConnectionToClient connection) => ZordCarts.SendAllTo(connection);
    public override void Tick() => ZordCarts.Tick();
}

using System.Collections;
using BepInEx.Configuration;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ItemPack.Items;

/// <summary>
/// Predator Missile (Modern Warfare 2): open the laptop and click to launch. Your view becomes the missile,
/// coming down from high above: steer with the mouse, click (or space) once to boost. It explodes on
/// whatever it hits with a bigger version of the rocket launcher's blast. Your golfer stands still meanwhile.
/// </summary>
internal class PredatorItem : CustomItem
{
    public static PredatorItem Instance { get; private set; }

    public override int Id => 104;
    public override string Key => "PredatorMissile";
    public override string DisplayName => "Predator Missile";
    // The flash camera's two-handed hold works for a laptop.
    public override ItemType TemplateItem => ItemType.FlashCamera;
    public override EquipmentType TemplateEquipment => EquipmentType.FlashCamera;
    public override string AssetName => "predator_laptop";
    public override bool HandlesOwnInput => true;
    public override bool SuppressAiming => true;

    protected override float DefaultItemBoxChance => 0.05f;
    protected override int DefaultUses => 1;

    internal static ConfigEntry<float> Altitude;
    internal static ConfigEntry<float> StartSpeed;
    internal static ConfigEntry<float> CruiseSpeed;
    internal static ConfigEntry<float> BoostSpeed;
    internal static ConfigEntry<float> MaxFlightSeconds;
    internal static ConfigEntry<float> ExplosionScale;
    internal static ConfigEntry<float> Sensitivity;
    internal static ConfigEntry<float> MissileScale;
    internal static ConfigEntry<float> SoundVolume;

    public static ModelAsset MissileModel { get; private set; }

    public PredatorItem() => Instance = this;

    protected override void BindConfig()
    {
        Altitude = Bind("Altitude", 140f, "How high above you the missile starts (meters).");
        StartSpeed = Bind("StartSpeed", 18f, "Speed right after launch (m/s).");
        CruiseSpeed = Bind("CruiseSpeed", 38f, "Speed after the first two seconds (m/s).");
        BoostSpeed = Bind("BoostSpeed", 110f, "Speed after boosting (m/s).");
        MaxFlightSeconds = Bind("MaxFlightSeconds", 15f, "The missile blows up on its own after this long.");
        ExplosionScale = Bind("ExplosionScale", 1.8f, "Blast radius relative to a rocket launcher rocket. Host setting.");
        Sensitivity = Bind("Sensitivity", 1f, "Mouse steering sensitivity. Local setting.");
        MissileScale = Bind("MissileScale", 1f, "Size of the missile as others see it.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the missile sounds (0-1). Local setting.");
    }

    // Launching comes from Tick (left click); the game's use button is swallowed (HandlesOwnInput).
    public override IEnumerator UseRoutine(PlayerInventory inventory, bool aiming)
    {
        yield break;
    }

    protected override void RegisterMessages()
    {
        PredatorNet.InitSerializers();
        MissileModel = ModelAsset.Load(AssetDir, "predator_missile", loadIcon: false);
    }

    public override void OnStartServer() => PredatorNet.RegisterServerHandlers();
    public override void OnStopServer() => PredatorNet.ResetServer();
    public override void OnStartClient() => PredatorNet.RegisterClientHandlers();
    public override void OnStopClient()
    {
        PredatorFlight.End();
        PredatorRemote.ClearAll();
    }

    public override void Tick()
    {
        PredatorFlight.Tick();
        PredatorRemote.Tick();
        if (PredatorFlight.Active || Mouse.current == null || !Mouse.current.leftButton.wasPressedThisFrame
            || !OwnInput.TryGetHolder<PredatorItem>(out PlayerInventory inventory) || !OwnInput.CanUse(inventory))
        {
            return;
        }
        ItemUse.ConsumeUse(inventory);
        ItemUse.Finish(inventory);
        PredatorFlight.Launch(inventory.PlayerInfo);
    }

    public override void OnGUI() => PredatorFlight.OnGUI();
}

using System.Collections;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Emergency Meeting (Among Us): press the button and everyone gets the "EMERGENCY MEETING!" screen and the klaxon,
/// then every player is pulled to a round table in front of you and stands frozen around it for a few seconds
/// (they can still look around). Their balls stay where they were, so everyone else has a walk back.
/// </summary>
internal class MeetingItem : CustomItem
{
    public static MeetingItem Instance { get; private set; }

    public override int Id => 117;
    public override string Key => "EmergencyMeeting";
    public override string DisplayName => "Emergency Meeting";
    // A comeback: pulls the leaders back to whoever's far behind.
    public override PoolWeights BoxWeights => new(close: 0, medium: 0, far: 2, veryFar: 3, aheadOfBall: 0, drivingRange: 10);
    // The airhorn's "raise it and press" use.
    public override ItemType TemplateItem => ItemType.Airhorn;
    public override EquipmentType TemplateEquipment => EquipmentType.Airhorn;
    public override string AssetName => "emergency_button";

    protected override int DefaultUses => 1;
    protected override float DefaultHeldScale => 1f;

    internal static ConfigEntry<float> FreezeSeconds;
    internal static ConfigEntry<float> SoundVolume;

    internal static ModelAsset Table { get; private set; }

    public MeetingItem() => Instance = this;

    protected override void BindConfig()
    {
        FreezeSeconds = Bind("FreezeSeconds", 3f, "How long everyone stands frozen around the table. Host setting.");
        SoundVolume = Bind("SoundVolume", 1f, "Volume of the klaxon (0-1). Local setting.");
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
        yield return new WaitForSeconds(0.25f); // reach for the button

        if (MeetingWorld.InProgress)
        {
            ItemUse.Finish(inventory); // one meeting at a time: keep it for later
            yield break;
        }
        ItemUse.ConsumeUse(inventory);
        MeetingNet.RequestMeeting();
        yield return new WaitForSeconds(0.8f);
        ItemUse.Finish(inventory);
    }

    protected override void RegisterMessages()
    {
        MeetingNet.InitSerializers();
        Table = ModelAsset.Load(AssetDir, "meeting_table", loadIcon: false);
        MeetingOverlay.LoadCustomImage(AssetDir);
    }

    public override void OnStartServer() => MeetingNet.RegisterServerHandlers();
    public override void OnStopServer() => MeetingNet.ResetServer();
    public override void OnStartClient() => MeetingNet.RegisterClientHandlers();
    public override void OnStopClient() => MeetingWorld.ClearAll();
    public override void Tick() => MeetingWorld.Tick();
    public override void OnGUI() => MeetingOverlay.Draw();
}

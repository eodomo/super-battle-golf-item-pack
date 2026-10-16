using System.Collections;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace ItemPack.Items;

internal enum WhistleTargetMode
{
    /// <summary>The opponent closest to your crosshair, at any range (shown with the lock-on marker).</summary>
    Aimed,
    /// <summary>Like the orbital laser: whoever is closest to the hole (even you), never a teammate.</summary>
    Leader,
    Random,
    Nearest,
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
    // Hits the opponent you pick from anywhere on the map.
    public override PoolWeights BoxWeights => new(close: 0, medium: 3, far: 4, veryFar: 4, aheadOfBall: 0, drivingRange: 10);
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
        TargetMode = Bind("TargetMode", WhistleTargetMode.Aimed,
            "Who the dragon goes after: the opponent closest to your crosshair (Aimed, at any range, shown with the lock-on marker), the Leader (closest to the hole, like the orbital laser, even if that's you), a Random other player, or the Nearest one. Host setting.");
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
        PlayerInfo target = WhistleLockOnPatch.Target;
        WhistleNet.RequestBlow(cam != null ? cam.transform.forward : inventory.transform.forward, target != null ? target.netId : 0);

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

    /// <summary>Whether the dragon may go after <paramref name="player"/> for <paramref name="user"/>: another player who can be targeted and isn't a teammate.</summary>
    internal static bool IsOpponent(PlayerInfo user, PlayerInfo player) =>
        player != null && player != user && OrbitalLaserManager.CanTargetAtAll(player.AsHittable) && !user.IsTeammateOf(player, excludeSelf: true);

    /// <summary>The opponent at the smallest angle from <paramref name="direction"/> as seen from <paramref name="origin"/>, at any range.</summary>
    internal static PlayerInfo FindAimed(PlayerInfo user, IEnumerable<PlayerInfo> players, Vector3 origin, Vector3 direction)
    {
        PlayerInfo aimed = null;
        float best = float.MinValue;
        foreach (PlayerInfo player in players)
        {
            if (!IsOpponent(user, player))
            {
                continue;
            }
            float dot = Vector3.Dot(direction.normalized, (player.transform.position - origin).normalized);
            if (dot > best)
            {
                best = dot;
                aimed = player;
            }
        }
        return aimed;
    }

    /// <summary>
    /// Who the orbital laser would hit: the targetable player closest to the hole, the user included, minus their
    /// teammates (the game's rules, from <paramref name="user"/>'s side). Null with no hole (Driving Range).
    /// </summary>
    internal static PlayerInfo FindLeader(PlayerInfo user, IEnumerable<PlayerInfo> players)
    {
        GolfHole hole = GolfHoleManager.MainHole;
        if (hole == null)
        {
            return null;
        }
        PlayerInfo leader = null;
        float best = float.MaxValue;
        foreach (PlayerInfo player in players)
        {
            if (player == null || !OrbitalLaserManager.CanTargetAtAll(player.AsHittable) || user.IsTeammateOf(player, excludeSelf: true))
            {
                continue;
            }
            float distance = (hole.transform.position - player.transform.position).sqrMagnitude;
            if (distance < best)
            {
                best = distance;
                leader = player;
            }
        }
        return leader;
    }
}

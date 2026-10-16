using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// While the whistle is held, the game's lock-on marker shows who the dragon will go after: the opponent closest
/// to the crosshair at any range (or the leader, in the Leader mode). The inventory clears the marker every update
/// for items it doesn't know, so this runs after it. Blowing the whistle sends the marked player to the host.
/// </summary>
[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.OnBUpdate))]
internal static class WhistleLockOnPatch
{
    private static readonly List<PlayerInfo> players = new();

    /// <summary>The player the local whistle is locked on, or null.</summary>
    public static PlayerInfo Target { get; private set; }

    private static void Postfix(PlayerInventory __instance)
    {
        if (!__instance.isLocalPlayer)
        {
            return;
        }
        WhistleTargetMode mode = SteamWhistleItem.TargetMode.Value;
        if (SteamWhistleItem.Instance == null || (mode != WhistleTargetMode.Aimed && mode != WhistleTargetMode.Leader)
            || __instance.GetEffectiveSlot(__instance.EquippedItemIndex).itemType != SteamWhistleItem.Instance.Type)
        {
            Target = null;
            return;
        }
        if (__instance.IsUsingItemAtAll)
        {
            // Keep the target picked before blowing, but drop the marker like the laser does.
            __instance.LocalPlayerSetLockOnTarget(null);
            return;
        }
        PlayerInfo user = GameManager.LocalPlayerInfo;
        Camera camera = GameManager.Camera;
        players.Clear();
        players.Add(user);
        players.AddRange(GameManager.RemotePlayers);
        Target = mode == WhistleTargetMode.Leader
            ? SteamWhistleItem.FindLeader(user, players)
            : camera != null ? SteamWhistleItem.FindAimed(user, players, camera.transform.position, camera.transform.forward) : null;
        __instance.LocalPlayerSetLockOnTarget(Target != null ? Target.AsEntity.AsLockOnTarget : null);
    }
}

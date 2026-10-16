using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Where the pack's shields stop hits: every knockout goes through <see cref="PlayerMovement.TryKnockOut"/> on the
/// victim's own client (players own their movement), so a bubble shield between the victim and the attack, or
/// Captain America's shield held toward it, turns it away there, with the game's "blocked" effect.
/// </summary>
[HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.TryKnockOut))]
internal static class ShieldKnockOutPatch
{
    private static bool Prefix(PlayerMovement __instance, PlayerInfo responsiblePlayer, KnockoutType knockoutType, Vector3 localOrigin,
        ref bool isNewKnockout, ref bool blockedByTeamProtection, ref bool __result)
    {
        if (!__instance.isLocalPlayer)
        {
            return true;
        }
        PlayerInfo victim = __instance.PlayerInfo;
        // Where the hit came from: the explosion, shot or swing (in the victim's space), else the attacker.
        bool hasOrigin = localOrigin != Vector3.zero;
        bool hasAttacker = responsiblePlayer != null && responsiblePlayer != victim;
        Vector3 source = hasOrigin ? __instance.transform.TransformPoint(localOrigin)
            : hasAttacker ? responsiblePlayer.transform.position + Vector3.up * 0.9f
            : Vector3.zero;
        bool blocked = BubbleWorld.Blocks(victim, hasAttacker ? responsiblePlayer : null, source, hasOrigin)
            || ((hasOrigin || hasAttacker) && CapShieldItem.TryBlock(victim, source, knockoutType));
        if (!blocked)
        {
            return true;
        }
        __instance.PlayKnockoutBlockedEffectForAllClients();
        isNewKnockout = false;
        blockedByTeamProtection = false;
        __result = false;
        return false;
    }
}

/// <summary>Freeze bombs thrown across a bubble shield's edge don't freeze anyone either.</summary>
[HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.CanBeFrozenBy))]
internal static class ShieldFreezePatch
{
    private static bool Prefix(PlayerMovement __instance, PlayerInfo player, ref bool __result)
    {
        if (!__instance.isLocalPlayer || player == null || player == __instance.PlayerInfo
            || !BubbleWorld.Blocks(__instance.PlayerInfo, player, player.transform.position, false))
        {
            return true;
        }
        __result = false;
        return false;
    }
}

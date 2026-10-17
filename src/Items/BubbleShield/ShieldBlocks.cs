using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Where the pack's shields stop hits: every knockout goes through <see cref="PlayerMovement.TryKnockOut"/> on the
/// victim's own client (players own their movement), so a bubble shield between the victim and the attack, or
/// a held Captain America's shield (from any direction), turns it away there, with the game's "blocked" effect.
/// Instant eliminations (railgun, orbital laser, lightning) skip that check, so <see cref="ShieldEliminationPatch"/>
/// handles those on the host.
/// </summary>
[HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.TryKnockOut))]
internal static class ShieldKnockOutPatch
{
    private static bool Prefix(PlayerMovement __instance, PlayerInfo responsiblePlayer, KnockoutType knockoutType, Vector3 localOrigin,
        Vector3 incomingVelocityChange, ref bool isNewKnockout, ref bool blockedByTeamProtection, ref bool __result)
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
        bool inBubble = BubbleWorld.Blocks(victim, hasAttacker ? responsiblePlayer : null, source, hasOrigin);
        bool blocked = inBubble || CapShieldItem.TryBlock(victim, knockoutType);
        if (!blocked)
        {
            return true;
        }
        if (inBubble)
        {
            // The dome stops the blast's push too, not just the knockout (the game pushes right after asking).
            BubblePushCancelPatch.Cancel(__instance, incomingVelocityChange);
        }
        __instance.PlayKnockoutBlockedEffectForAllClients();
        isNewKnockout = false;
        blockedByTeamProtection = false;
        __result = false;
        return false;
    }
}

/// <summary>
/// <see cref="Hittable"/>'s item hits push the player (AddForceAtPosition) right after the knockout check, whether or not
/// it knocked them out. When a bubble shield blocked that hit, push back by the same amount at the same point.
/// </summary>
[HarmonyPatch(typeof(Hittable), "HitWithItemInternal")]
internal static class BubblePushCancelPatch
{
    private static PlayerMovement pending;
    private static Vector3 pendingVelocityChange;
    private static int pendingFrame = -1;

    public static void Cancel(PlayerMovement movement, Vector3 velocityChange)
    {
        pending = movement;
        pendingVelocityChange = velocityChange;
        pendingFrame = Time.frameCount;
    }

    private static void Postfix(Hittable __instance, Vector3 hitLocalPosition)
    {
        if (pending == null || pendingFrame != Time.frameCount || !__instance.AsEntity.IsPlayer
            || __instance.AsEntity.PlayerInfo.Movement != pending)
        {
            return;
        }
        pending = null;
        __instance.AsEntity.Rigidbody.AddForceAtPosition(-pendingVelocityChange, __instance.transform.TransformPoint(hitLocalPosition),
            ForceMode.VelocityChange);
    }
}

/// <summary>
/// Freeze bombs thrown across a bubble shield's edge, or at someone holding Captain America's shield, don't freeze anyone.
/// The game asks on the host (for whoever was hit), which is where this decides; a shield's charge is used up by its owner's client.
/// </summary>
[HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.CanBeFrozenBy))]
internal static class ShieldFreezePatch
{
    private static bool Prefix(PlayerMovement __instance, PlayerInfo player, ref bool __result)
    {
        if (!NetworkServer.active || player == null || player == __instance.PlayerInfo)
        {
            return true;
        }
        PlayerInfo victim = __instance.PlayerInfo;
        if (BubbleWorld.Blocks(victim, player, player.transform.position, false))
        {
            __result = false;
            return false;
        }
        if (CapShieldItem.IsHoldingCharged(victim, out _, out _))
        {
            CapShieldNet.SendBlockOrder(victim, KnockoutType.FreezeBomb);
            __result = false;
            return false;
        }
        return true;
    }
}

/// <summary>
/// The railgun, the orbital laser's center and a lightning strike's center eliminate on the host without going through a
/// knockout check, so a shield can't turn them away on the victim's client. The host skips the elimination for someone
/// holding a charged Captain America's shield, and tells that player's client to use the charge up.
/// </summary>
[HarmonyPatch(typeof(PlayerGolfer), "OnServerWasHitByItem")]
internal static class ShieldEliminationPatch
{
    private static bool Prefix(PlayerGolfer __instance, ItemType itemType, float distance, bool isReflected, ProtectiveState protectiveState)
    {
        if (!itemType.TryGetEliminationReason(distance, isReflected, protectiveState, out EliminationReason reason, out _)
            || (reason != EliminationReason.RailgunDirectHit && reason != EliminationReason.OrbitalLaserCenter && reason != EliminationReason.ThunderstormDirectHit)
            || !CapShieldItem.IsHoldingCharged(__instance.PlayerInfo, out _, out _))
        {
            return true;
        }
        CapShieldNet.SendBlockOrder(__instance.PlayerInfo, KnockoutType.RailgunDirectHit);
        return false;
    }
}

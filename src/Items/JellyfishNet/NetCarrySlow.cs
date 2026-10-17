using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>Someone in your net is heavy: the carrier moves slower until they're let go.</summary>
[HarmonyPatch(typeof(PlayerMovement), "UpdateMovementSpeed")]
internal static class NetCarrySlowPatch
{
    // The game eases speed toward its target every step; capping it after that slows the carrier smoothly.
    private static void Postfix(PlayerMovement __instance, ref float ___speed)
    {
        PlayerInfo player = __instance.PlayerInfo;
        if (player != null && NetCatches.IsCarrier(player.netId))
        {
            ___speed = Mathf.Min(___speed, GameManager.PlayerMovementSettings.DefaultMoveSpeed * JellyfishNetItem.CarrySpeedFactor.Value);
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Holding right click with a lightsaber takes the smoke bomb's stance (left arm forward, right arm back). Aiming makes the
/// player strafe, and the saber's strafing clips are the smoke bomb's (see <see cref="LightsaberItem"/>); but the animator's
/// "Is aiming item" switch would raise the arms into the freeze bomb's aim pose on top of that. For a saber it's kept off.
/// </summary>
[HarmonyPatch(typeof(PlayerAnimatorIo), nameof(PlayerAnimatorIo.SetIsAimingItem))]
internal static class SaberAimStance
{
    private static readonly int AimingHash = Animator.StringToHash("Is aiming item");

    private static void Postfix(PlayerAnimatorIo __instance, bool isAimingItem)
    {
        if (isAimingItem && __instance.playerInfo != null && LightsaberItem.IsLightsaber(__instance.playerInfo.Inventory.GetEffectivelyEquippedItem()))
        {
            __instance.animator.SetBool(AimingHash, false);
        }
    }
}

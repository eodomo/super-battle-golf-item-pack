using HarmonyLib;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// Shows a timed item's time left on the coffee's speed boost wheel, so it looks like the game's own timer.
/// Items call <see cref="Show"/> every frame while their timer runs; the wheel fades out when they stop.
/// </summary>
internal static class TimerWheel
{
    private static int shownFrame = -10;
    private static float fill;

    /// <summary>Shows the wheel this frame, filled to <paramref name="normalized"/> (1 = full time left).</summary>
    public static void Show(float normalized)
    {
        shownFrame = Time.frameCount;
        fill = Mathf.Clamp01(normalized);
    }

    // Items tick in Update and the wheel draws in late update, so this frame or the last one counts.
    internal static bool Active => Time.frameCount - shownFrame <= 1;
    internal static float Fill => fill;
}

/// <summary>While an item's timer runs, the speed boost wheel shows it instead of the coffee's boost.</summary>
[HarmonyPatch(typeof(SpeedBoostWheelUi), nameof(SpeedBoostWheelUi.OnLateBUpdate))]
internal static class SpeedBoostWheelPatch
{
    private static bool Prefix(SpeedBoostWheelUi __instance)
    {
        if (!TimerWheel.Active)
        {
            return true;
        }
        if (!__instance.isVisible)
        {
            __instance.isVisible = true;
            if (__instance.visibilityCoroutine != null)
            {
                __instance.StopCoroutine(__instance.visibilityCoroutine);
            }
            __instance.visibilityCoroutine = __instance.StartCoroutine(__instance.AnimateVisibilityRoutine(1f, 0.15f));
        }
        __instance.staminaWheel.fillAmount = TimerWheel.Fill;
        // Make the game redraw its own fill if a coffee boost takes the wheel back afterwards.
        __instance.displayedNormalizedSpeedBoost = -1f;
        return false;
    }
}

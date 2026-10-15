using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Gives the sniper the Railgun's scope. The game decides "zoom into first person while aiming" and "slow
/// the mouse down while zoomed" by checking for the Railgun in two small helper functions; these patches
/// make those two checks (and nothing else) see the sniper as a Railgun.
/// </summary>
internal static class SniperScopePatches
{
    private static readonly MethodInfo GetEffectivelyEquippedItem =
        AccessTools.Method(typeof(PlayerInventory), nameof(PlayerInventory.GetEffectivelyEquippedItem));

    private static readonly MethodInfo ScopeItemMethod = AccessTools.Method(typeof(SniperScopePatches), nameof(ScopeItem));

    /// <summary>Stand-in for PlayerInventory.GetEffectivelyEquippedItem(bool) inside the patched helpers.</summary>
    public static ItemType ScopeItem(PlayerInventory inventory, bool ignoreEquipmentHiding)
    {
        ItemType item = inventory.GetEffectivelyEquippedItem(ignoreEquipmentHiding);
        return SniperRifleItem.IsSniper(item) ? ItemType.Railgun : item;
    }

    public static IEnumerable<CodeInstruction> ReplaceEquippedItemCalls(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(GetEffectivelyEquippedItem))
            {
                replaced++;
                yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call, ScopeItemMethod).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
        if (replaced == 0)
        {
            Plugin.Log.LogWarning($"Sniper scope: found nothing to patch in {original.DeclaringType?.Name}.{original.Name}; the scope may not work.");
        }
    }

    /// <summary>Finds a compiler-generated local function, e.g. "&lt;UpdateIsInFirstPersonAim&gt;g__ShouldBeInFirstPersonAim|...".</summary>
    public static MethodBase FindLocalFunction(System.Type type, string outer, string inner) =>
        AccessTools.GetDeclaredMethods(type).FirstOrDefault(m => m.Name.Contains($"<{outer}>") && m.Name.Contains(inner));
}

[HarmonyPatch]
internal static class SniperFirstPersonAimPatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase TargetMethod() =>
        SniperScopePatches.FindLocalFunction(typeof(PlayerInventory), "UpdateIsInFirstPersonAim", "ShouldBeInFirstPersonAim");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        SniperScopePatches.ReplaceEquippedItemCalls(instructions, original);
}

[HarmonyPatch]
internal static class SniperScopeSensitivityPatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase TargetMethod() =>
        SniperScopePatches.FindLocalFunction(typeof(PlayerInput), "Update", "UpdateCameraInput");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        SniperScopePatches.ReplaceEquippedItemCalls(instructions, original);
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.UpdateIsInFirstPersonAim))]
internal static class SniperZoomPatch
{
    private static void Prefix(PlayerInventory __instance, out bool __state) => __state = __instance.IsInFirstPersonAim;

    // Scoping in uses the Railgun's zoom; switch to the sniper's own (stronger) zoom.
    private static void Postfix(PlayerInventory __instance, bool __state)
    {
        float fov = SniperRifleItem.ZoomFov.Value;
        if (__state || !__instance.IsInFirstPersonAim || fov <= 0f
            || !SniperRifleItem.IsSniper(__instance.GetEffectivelyEquippedItem(ignoreEquipmentHiding: true)))
        {
            return;
        }
        if (CameraModuleController.TryGetFirstPersonModule(out FirstPersonCameraModule firstPerson))
        {
            firstPerson.AnimateFovOffsetOverTime(Mathf.Clamp(fov, 3f, 90f), GameManager.ItemSettings.RailgunZoomFovChangeDuration, BMath.EaseOut);
        }
    }
}

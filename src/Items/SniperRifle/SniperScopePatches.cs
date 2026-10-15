using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace ItemPack.Items;

/// <summary>
/// Gives the sniper the Railgun's scope. The game decides "zoom into first person while aiming" and "slow
/// the mouse down while zoomed" by checking for the Railgun in two small helper functions; these patches
/// make those two checks (and nothing else) see the sniper as a Railgun, and swap the Railgun's fixed
/// scoped sensitivity for one that follows the sniper's zoom level.
/// </summary>
internal static class SniperScopePatches
{
    private static readonly MethodInfo GetEffectivelyEquippedItem =
        AccessTools.Method(typeof(PlayerInventory), nameof(PlayerInventory.GetEffectivelyEquippedItem));
    private static readonly MethodInfo RailgunSensitivityGetter =
        AccessTools.PropertyGetter(typeof(ItemSettings), nameof(ItemSettings.RailgunZoomCameraSensitivityFactor));

    private static readonly MethodInfo ScopeItemMethod = AccessTools.Method(typeof(SniperScopePatches), nameof(ScopeItem));
    private static readonly MethodInfo SensitivityMethod = AccessTools.Method(typeof(SniperScope), nameof(SniperScope.SensitivityFactor));

    /// <summary>Stand-in for PlayerInventory.GetEffectivelyEquippedItem(bool) inside the patched helpers.</summary>
    public static ItemType ScopeItem(PlayerInventory inventory, bool ignoreEquipmentHiding)
    {
        ItemType item = inventory.GetEffectivelyEquippedItem(ignoreEquipmentHiding);
        return SniperRifleItem.IsSniper(item) ? ItemType.Railgun : item;
    }

    public static IEnumerable<CodeInstruction> Replace(IEnumerable<CodeInstruction> instructions, MethodBase original, bool sensitivity)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            MethodInfo replacement = instruction.Calls(GetEffectivelyEquippedItem) ? ScopeItemMethod
                : sensitivity && instruction.Calls(RailgunSensitivityGetter) ? SensitivityMethod
                : null;
            if (replacement == null)
            {
                yield return instruction;
                continue;
            }
            replaced++;
            // Same stack shape: (instance, args...) -> static (instance, args...).
            yield return new CodeInstruction(OpCodes.Call, replacement).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
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
        SniperScopePatches.Replace(instructions, original, sensitivity: false);
}

[HarmonyPatch]
internal static class SniperScopeSensitivityPatch
{
    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase TargetMethod() =>
        SniperScopePatches.FindLocalFunction(typeof(PlayerInput), "Update", "UpdateCameraInput");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original) =>
        SniperScopePatches.Replace(instructions, original, sensitivity: true);
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.UpdateIsInFirstPersonAim))]
internal static class SniperZoomPatch
{
    private static void Prefix(PlayerInventory __instance, out bool __state)
    {
        __state = __instance.isLocalPlayer && SniperScope.IsScoped;
        if (__instance.isLocalPlayer && !__instance.IsInFirstPersonAim)
        {
            SniperScope.CaptureBaseFov();
        }
    }

    // Scoping in starts at the Railgun's zoom; switch to the sniper's first zoom level (and play its sounds).
    private static void Postfix(PlayerInventory __instance, bool __state)
    {
        if (!__instance.isLocalPlayer)
        {
            return;
        }
        bool scoped = SniperScope.IsScoped;
        if (scoped && !__state)
        {
            SniperScope.OnScopedIn();
        }
        else if (!scoped && __state)
        {
            SniperScope.OnScopedOut();
        }
    }
}

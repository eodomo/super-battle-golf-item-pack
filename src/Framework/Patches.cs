using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ItemPack;

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.TryUseItem))]
internal static class TryUseItemPatch
{
    // Mirrors the start of the original TryUseItem, then runs the item's routine instead of the item switch
    // (which would throw for an item type it doesn't know).
    private static bool Prefix(PlayerInventory __instance, bool isAirhornReaction, ref bool shouldEatInput, ref bool __result)
    {
        if (!__instance.isLocalPlayer
            || !ItemCatalog.TryGet(__instance.GetEffectiveSlot(__instance.EquippedItemIndex).itemType, out CustomItem item))
        {
            return true;
        }

        if (item.HandlesOwnInput)
        {
            // The item fires from its own mouse handling; just keep the click from doing anything else.
            shouldEatInput = !isAirhornReaction;
            __result = false;
            return false;
        }

        if (!__instance.CanUseEquippedItem(altUse: false, isAirhornReaction, out _, out _, out shouldEatInput, out _))
        {
            __result = false;
            return false;
        }

        __instance.ItemUseTimestamp = Time.timeAsDouble;
        __instance.CancelItemUse();
        bool aiming = isAirhornReaction || __instance.PlayerInfo.Input.IsHoldingAimSwing;
        __instance.itemUseRoutine = __instance.StartCoroutine(item.UseRoutine(__instance, aiming));
        __instance.PlayerInfo.CancelEmote(canHideEmoteMenu: false);
        __instance.CancelItemFlourish();
        __result = true;
        return false;
    }
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.LocalPlayerUpdateEquipmentSwitchers))]
internal static class EquipmentSwitcherPatch
{
    // The original maps known items to held equipment and falls back to nothing; add our mapping.
    private static bool Prefix(PlayerInventory __instance)
    {
        if (!ItemCatalog.TryGet(__instance.GetEffectivelyEquippedItem(), out CustomItem item))
        {
            return true;
        }
        PlayerInfo player = __instance.PlayerInfo;
        bool canHold = !__instance.localPlayerIsEquipmentForceHidden && player.Movement.IsVisible;
        bool thrown = __instance.thrownItem.HasFlag(PlayerInventory.ThrownItemHand.Right);
        player.RightHandEquipmentSwitcher.SetEquipment(canHold && !thrown ? item.Equipment : EquipmentType.None);
        player.LeftHandEquipmentSwitcher.SetEquipment(EquipmentType.None);
        return false;
    }
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.LocalPlayerUpdateAimingReticle))]
internal static class AimingReticlePatch
{
    // The original picks a crosshair per vanilla item; pack items choose their own (possibly none).
    private static void Postfix(PlayerInventory __instance)
    {
        if (__instance.isLocalPlayer && __instance.IsAimingItem
            && ItemCatalog.TryGet(__instance.GetEffectivelyEquippedItem(), out CustomItem item))
        {
            ReticleManager.SetReticle(item.GetAimReticle(__instance));
        }
    }
}

[HarmonyPatch(typeof(ItemSpawnerSettings), nameof(ItemSpawnerSettings.GetRandomItemFor))]
internal static class ItemBoxPatch
{
    // Runs on the host when an item box is opened. Pack items are in the pools themselves (see ItemBoxes).
    private static void Postfix(ref ItemType __result) => __result = ItemBoxes.OnRolled(__result);
}

[HarmonyPatch(typeof(BNetworkManager))]
internal static class NetworkManagerPatches
{
    [HarmonyPostfix, HarmonyPatch(nameof(BNetworkManager.OnStartServer))]
    private static void OnStartServer()
    {
        DroppedItems.RegisterServerHandlers();
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.OnStartServer();
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(BNetworkManager.OnStartClient))]
    private static void OnStartClient()
    {
        DroppedItems.RegisterClientHandlers();
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.OnStartClient();
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(BNetworkManager.OnStopClient))]
    private static void OnStopClient()
    {
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.OnStopClient();
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(BNetworkManager.OnStopServer))]
    private static void OnStopServer()
    {
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.OnStopServer();
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(BNetworkManager.OnServerReady))]
    private static void OnServerReady(NetworkConnectionToClient connection)
    {
        DroppedItems.SendExisting(connection);
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.OnServerReady(connection);
        }
    }

    [HarmonyPrefix, HarmonyPatch(nameof(BNetworkManager.OnServerDisconnect))]
    private static void OnServerDisconnect(NetworkConnectionToClient connection)
    {
        // Prefix: the player object (connection.identity) still exists here.
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.OnServerDisconnect(connection);
        }
    }
}

[HarmonyPatch]
internal static class SuppressAimingPatch
{
    // The game's "should the player be aiming?" helper reads PlayerInput.IsHoldingAimSwing. For items that
    // use the aim button for something else, make that read come back false so no aim mode starts.
    private static readonly MethodInfo AimHeldGetter = AccessTools.PropertyGetter(typeof(PlayerInput), nameof(PlayerInput.IsHoldingAimSwing));
    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(SuppressAimingPatch), nameof(IsHoldingAimForAiming));

    private static bool Prepare() => TargetMethod() != null;

    private static MethodBase TargetMethod() =>
        AccessTools.GetDeclaredMethods(typeof(PlayerInventory)).FirstOrDefault(m => m.Name.Contains("<UpdateIsAimingItem>") && m.Name.Contains("ShouldAim"));

    public static bool IsHoldingAimForAiming(PlayerInput input)
    {
        PlayerInfo player = input.playerInfo;
        if (player != null && ItemCatalog.TryGet(player.Inventory.GetEffectivelyEquippedItem(), out CustomItem item) && item.SuppressAiming)
        {
            return false;
        }
        return input.IsHoldingAimSwing;
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(AimHeldGetter))
            {
                replaced++;
                yield return new CodeInstruction(OpCodes.Call, Replacement).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
        if (replaced == 0)
        {
            Plugin.Log.LogWarning("Aim suppression: nothing to patch; the portal gun's right click will also aim.");
        }
    }
}

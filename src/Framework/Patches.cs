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

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.UserCode_CmdDropItemAt__Int32__Vector3__Vector3__ItemUseId))]
internal static class DropItemPatch
{
    // Dropped items need a networked pickup prefab per item type, which we don't have.
    // Dropping a pack item just discards it.
    private static bool Prefix(PlayerInventory __instance, int index)
    {
        if (index < 0 || index >= __instance.slots.Count || !ItemCatalog.IsCustom(__instance.slots[index].itemType))
        {
            return true;
        }
        if (__instance.serverDropItemCommandRateLimiter.RegisterHit())
        {
            __instance.RemoveItemAt(index, localOnly: false, dueToFinishedItemUse: false);
        }
        return false;
    }
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.LocalPlayerUpdateAimingReticle))]
internal static class AimingReticlePatch
{
    // The original picks a crosshair per vanilla item and shows none for ours.
    private static void Postfix(PlayerInventory __instance)
    {
        if (__instance.isLocalPlayer && __instance.IsAimingItem
            && ItemCatalog.TryGet(__instance.GetEffectivelyEquippedItem(), out CustomItem item) && item.AimReticle != ReticleType.None)
        {
            ReticleManager.SetReticle(item.AimReticle);
        }
    }
}

[HarmonyPatch(typeof(ItemSpawnerSettings), nameof(ItemSpawnerSettings.GetRandomItemFor))]
internal static class ItemBoxPatch
{
    // Runs on the host when an item box is opened.
    private static void Postfix(ref ItemType __result) => __result = ItemCatalog.RollItemBox(__result);
}

[HarmonyPatch(typeof(BNetworkManager))]
internal static class NetworkManagerPatches
{
    [HarmonyPostfix, HarmonyPatch(nameof(BNetworkManager.OnStartServer))]
    private static void OnStartServer()
    {
        foreach (CustomItem item in ItemCatalog.All)
        {
            item.OnStartServer();
        }
    }

    [HarmonyPostfix, HarmonyPatch(nameof(BNetworkManager.OnStartClient))]
    private static void OnStartClient()
    {
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

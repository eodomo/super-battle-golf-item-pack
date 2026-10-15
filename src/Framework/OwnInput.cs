using System.Collections;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// Helpers for items that read the mouse themselves (<see cref="CustomItem.HandlesOwnInput"/>): who's holding
/// what, whether the player is in normal gameplay (not a menu, chat or the item menu), and starting a use.
/// </summary>
internal static class OwnInput
{
    /// <summary>The local player's inventory, if they're holding <typeparamref name="T"/> and playing normally.</summary>
    public static bool TryGetHolder<T>(out PlayerInventory inventory) where T : CustomItem
    {
        inventory = GameManager.LocalPlayerInventory;
        return inventory != null
            && ItemCatalog.TryGet(inventory.GetEffectivelyEquippedItem(), out CustomItem item) && item is T
            && InputManager.CurrentMode == InputMode.Regular;
    }

    /// <summary>Whether the game would let the player use their item now (not knocked out, in a cart, out of uses...).</summary>
    public static bool CanUse(PlayerInventory inventory) =>
        // altUse skips the "needs aiming" rule; every other rule still applies.
        inventory.CanUseEquippedItem(altUse: true, isAirhornReaction: false, out _, out _, out _, out _);

    /// <summary>Starts an item-use coroutine the way the game does (so cancelling, emotes etc. behave).</summary>
    public static void StartUse(PlayerInventory inventory, IEnumerator routine)
    {
        inventory.ItemUseTimestamp = Time.timeAsDouble;
        inventory.CancelItemUse();
        inventory.itemUseRoutine = inventory.StartCoroutine(routine);
        inventory.PlayerInfo.CancelEmote(canHideEmoteMenu: false);
        inventory.CancelItemFlourish();
    }
}

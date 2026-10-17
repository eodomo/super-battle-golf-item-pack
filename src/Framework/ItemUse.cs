namespace ItemPack;

/// <summary>Shared bookkeeping for item-use routines.</summary>
internal static class ItemUse
{
    /// <summary>Using an item shows you again if a smoke bomb has you hidden, like every game item.</summary>
    public static void BreakStealth(PlayerInventory inventory) => inventory.PlayerInfo.LocalPlayerCancelSmokeBombInvisibility();

    /// <summary>Takes one use for an action the player just did (which also ends smoke bomb invisibility), through the game's own anti-cheat bookkeeping, exactly like the coffee item does.</summary>
    public static void ConsumeUse(PlayerInventory inventory)
    {
        BreakStealth(inventory);
        ConsumeUseAt(inventory, inventory.EquippedItemIndex);
    }

    /// <summary>Takes one use from the item in slot <paramref name="index"/> (which needn't be the one in hand any more).</summary>
    public static void ConsumeUseAt(PlayerInventory inventory, int index)
    {
        ulong useHash = inventory.GetEffectiveSlot(index).GetUsageValidationHash();
        inventory.DecrementUseFromSlotAt(index);
        inventory.CmdValidateAndConsumeItemUse(useHash);
    }

    /// <summary>Ends the use animation state and removes the item if it has no uses left.</summary>
    public static void Finish(PlayerInventory inventory)
    {
        inventory.SetCurrentItemUse(ItemUseType.None);
        inventory.RemoveIfOutOfUses(inventory.EquippedItemIndex, dueToFinishedItemUse: true);
    }
}

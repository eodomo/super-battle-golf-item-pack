namespace ItemPack;

/// <summary>Shared bookkeeping for item-use routines.</summary>
internal static class ItemUse
{
    /// <summary>Takes one use, through the game's own anti-cheat bookkeeping, exactly like the coffee item does.</summary>
    public static void ConsumeUse(PlayerInventory inventory)
    {
        ulong useHash = inventory.GetEquippedItemUseHash();
        inventory.DecrementUseFromSlotAt(inventory.EquippedItemIndex);
        inventory.CmdValidateAndConsumeItemUse(useHash);
    }

    /// <summary>Ends the use animation state and removes the item if it has no uses left.</summary>
    public static void Finish(PlayerInventory inventory)
    {
        inventory.SetCurrentItemUse(ItemUseType.None);
        inventory.RemoveIfOutOfUses(inventory.EquippedItemIndex, dueToFinishedItemUse: true);
    }
}

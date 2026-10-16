using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// Puts the pack's items into the game's item box pools, as ordinary weighted entries, so they show up in the
/// match setup's item probability sliders (host adjustable per pool, saved with the other rules) and in the
/// game's own roll.
///
/// The game picks a pool by how much further from the hole you are than the leader (each totals 100; read
/// from its data):
///   close (0-50 m):     pistol, spring boots, landmine, electromagnet: fighting near the lead
///   medium (50-125 m):  coffee, elephant gun, freeze bomb
///   far (125-200 m):    coffee, rocket launcher, golf cart; railgun, orbital laser, thunderstorm
///   very far (200 m+):  golf cart 35, coffee, rockets: mostly getting back in the race
/// plus "ahead of your ball" (coffee and spring boots, for whoever walked past their ball), the mobility
/// mode's pool and the Driving Range's. Most boxes are opened in the close pool (everyone starts a hole at the
/// tee), so each item's <see cref="CustomItem.BoxWeights"/> follow what it is for, and the pack makes up
/// roughly 30% of every pool. ItemBoxFrequency scales them. Items that come in variants are listed once
/// (<see cref="CustomItem.ListedInItemBoxes"/>) and pick their variant when they come out of a box.
///
/// The setup menu builds one slider per entry of its item order and finds them through a lookup indexed by
/// item type, which only covers the vanilla types; both are extended to the pack's types before it builds.
/// </summary>
internal static class ItemBoxes
{
    private static readonly HashSet<ItemPool> filled = new();
    private static bool scanned;

    private enum PoolKind { Close, Medium, Far, VeryFar, AheadOfBall, DrivingRange, Off }

    /// <summary>Default weight of an item in a pool (vanilla pools total 100; the Driving Range's 315).</summary>
    private static float DefaultWeight(CustomItem item, PoolKind kind)
    {
        PoolWeights weights = item.BoxWeights;
        float weight = kind switch
        {
            PoolKind.Close => weights.Close,
            PoolKind.Medium => weights.Medium,
            PoolKind.Far => weights.Far,
            PoolKind.VeryFar => weights.VeryFar,
            PoolKind.AheadOfBall => weights.AheadOfBall,
            PoolKind.DrivingRange => weights.DrivingRange,
            _ => 0f,
        };
        return weight * Mathf.Max(0f, item.ItemBoxFrequency.Value);
    }

    // Pool assets are named e.g. "Far item pool"; the game's runtime copies add "(Clone)".
    private static PoolKind GetPoolKind(ItemPool pool)
    {
        string name = pool.name.ToLowerInvariant();
        if (name.Contains("ahead"))
        {
            return PoolKind.AheadOfBall;
        }
        if (name.Contains("mobility"))
        {
            return PoolKind.Off; // listed (so hosts can turn them on) but off by default
        }
        if (name.Contains("driving"))
        {
            return PoolKind.DrivingRange;
        }
        if (name.Contains("very far"))
        {
            return PoolKind.VeryFar;
        }
        if (name.Contains("far"))
        {
            return PoolKind.Far;
        }
        if (name.Contains("close"))
        {
            return PoolKind.Close;
        }
        return PoolKind.Medium;
    }

    /// <summary>Once, early: the pools that were loaded before the pack.</summary>
    public static void ScanLoaded()
    {
        if (scanned)
        {
            return;
        }
        scanned = true;
        foreach (ItemSpawnerSettings settings in Resources.FindObjectsOfTypeAll<ItemSpawnerSettings>())
        {
            AddTo(settings);
        }
    }

    /// <summary>Adds the pack's items to a spawner's default pools and its current runtime copies.</summary>
    public static void AddTo(ItemSpawnerSettings settings)
    {
        if (settings == null)
        {
            return;
        }
        if (settings.itemPools != null)
        {
            foreach (ItemSpawnerSettings.ItemPoolData data in settings.itemPools)
            {
                AddTo(data.pool);
            }
        }
        foreach (ItemSpawnerSettings.ItemPoolData data in settings.runtimeItemPools)
        {
            AddTo(data.pool);
        }
        AddTo(settings.aheadOfBallItemPool);
        AddTo(settings.runtimeAheadOfBallItemPool);
    }

    private static void AddTo(ItemPool pool)
    {
        if (pool == null || pool.spawnChances == null || !filled.Add(pool))
        {
            return;
        }
        PoolKind kind = GetPoolKind(pool);
        var entries = new List<ItemPool.ItemSpawnChance>(pool.spawnChances);
        foreach (CustomItem item in ItemCatalog.All)
        {
            if (item.ListedInItemBoxes && !pool.ContainsItemType(item.Type))
            {
                entries.Add(new ItemPool.ItemSpawnChance { item = item.Type, spawnChanceWeight = DefaultWeight(item, kind) });
            }
        }
        pool.spawnChances = entries.ToArray();
        pool.UpdateTotalWeight();
    }

    /// <summary>Gives the match setup menu a slider for each pack item.</summary>
    public static void ExtendMenu(MatchSetupRules rules)
    {
        var order = new List<ItemType>(rules.itemOrder);
        foreach (CustomItem item in ItemCatalog.All)
        {
            if (item.ListedInItemBoxes && !order.Contains(item.Type))
            {
                order.Add(item.Type);
            }
        }
        int highest = 0;
        foreach (ItemType type in order)
        {
            highest = Mathf.Max(highest, (int)type);
        }
        var lookup = new int[highest];
        for (int i = 0; i < lookup.Length; i++)
        {
            lookup[i] = order.IndexOf((ItemType)(i + 1));
        }
        rules.itemOrder = order.ToArray();
        rules.itemOrderLookup = lookup;
    }

    /// <summary>What an item box actually gives: the variant, for items that have them.</summary>
    public static ItemType OnRolled(ItemType result)
    {
        if (!ItemCatalog.TryGet(result, out CustomItem item))
        {
            return result;
        }
        ItemType type = item.RollFromItemBox();
        return ItemCatalog.TryGet(type, out CustomItem rolled) && ItemCatalog.IsRegistered(rolled) ? type : ItemType.Coffee;
    }
}

[HarmonyPatch(typeof(ItemSpawnerSettings), nameof(ItemSpawnerSettings.ResetRuntimeData))]
internal static class ItemSpawnerResetPatch
{
    // Before: the runtime pools are copied from the defaults, so add to those. After: in case the copies predate us.
    private static void Prefix(ItemSpawnerSettings __instance) => ItemBoxes.AddTo(__instance);

    private static void Postfix(ItemSpawnerSettings __instance) => ItemBoxes.AddTo(__instance);
}

[HarmonyPatch(typeof(MatchSetupRules), nameof(MatchSetupRules.Initialize))]
internal static class MatchSetupItemsPatch
{
    // Before the menu reads the pools and builds its sliders.
    private static void Prefix(MatchSetupRules __instance)
    {
        ItemBoxes.AddTo(__instance.itemSpawnerSettings);
        ItemBoxes.AddTo(__instance.mobilityItemSpawnerSettings);
        ItemBoxes.ExtendMenu(__instance);
    }
}

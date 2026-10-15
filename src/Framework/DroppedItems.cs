using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ItemPack;

public struct CustomItemDropped : NetworkMessage
{
    public uint netId;
    public int itemType;
}

/// <summary>
/// Dropping pack items. The game drops an item by spawning a networked pickup from the item's prefab, and every
/// pickup prefab is hard-wired to its own item type. Pack items reuse the coffee's pickup, so after the game
/// spawns it, the host switches the pickup to the pack item (that's what picking it up gives, with its remaining
/// uses) and tells every client, which swap the coffee cup for the pack item's model.
/// </summary>
internal static class DroppedItems
{
    private static readonly List<(PhysicalItem pickup, ItemType type)> serverDrops = new();

    public static void InitSerializers()
    {
        Writer<CustomItemDropped>.write = (w, m) => { w.WriteUInt(m.netId); w.WriteInt(m.itemType); };
        Reader<CustomItemDropped>.read = r => new CustomItemDropped { netId = r.ReadUInt(), itemType = r.ReadInt() };
    }

    public static void RegisterServerHandlers() => serverDrops.Clear();

    public static void RegisterClientHandlers() => NetworkClient.ReplaceHandler<CustomItemDropped>(OnClientDropped);

    /// <summary>Tells a client that just joined which pickups lying around are pack items.</summary>
    public static void SendExisting(NetworkConnectionToClient connection)
    {
        serverDrops.RemoveAll(d => d.pickup == null);
        foreach ((PhysicalItem pickup, ItemType type) in serverDrops)
        {
            connection.Send(new CustomItemDropped { netId = pickup.netId, itemType = (int)type });
        }
    }

    /// <summary>Host, right after the game spawned a pickup for a pack item.</summary>
    public static void OnServerSpawned(PhysicalItem pickup, ItemType type, bool networkSpawned)
    {
        pickup.itemType = type;
        if (!networkSpawned)
        {
            return;
        }
        serverDrops.RemoveAll(d => d.pickup == null);
        serverDrops.Add((pickup, type));
        NetworkServer.SendToAll(new CustomItemDropped { netId = pickup.netId, itemType = (int)type });
    }

    private static void OnClientDropped(CustomItemDropped msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.netId, out NetworkIdentity identity) || identity == null
            || !identity.TryGetComponent(out PhysicalItem pickup) || !ItemCatalog.TryGet((ItemType)msg.itemType, out CustomItem item))
        {
            return;
        }
        pickup.itemType = item.Type;
        if (pickup.transform.Find("ItemPackDroppedModel") != null)
        {
            return; // already disguised (the host gets the message for its own pickup too)
        }
        // Hide the coffee cup, show the pack item.
        foreach (Renderer renderer in pickup.GetComponentsInChildren<Renderer>(true))
        {
            renderer.enabled = false;
        }
        var holder = new GameObject("ItemPackDroppedModel");
        holder.transform.SetParent(pickup.transform, false);
        try
        {
            item.CreateDroppedModel(holder.transform);
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Couldn't build the dropped {item.DisplayName}: {e.Message}");
        }
    }
}

// The private instance overload that actually spawns (the public static one just forwards to it).
[HarmonyPatch(typeof(CourseManager), nameof(CourseManager.ServerSpawnItem), new[]
{
    typeof(ItemType), typeof(int), typeof(Vector3), typeof(Quaternion), typeof(Vector3), typeof(Vector3),
    typeof(PlayerInventory), typeof(ItemUseId), typeof(bool),
})]
internal static class SpawnCustomPickupPatch
{
    private static void Postfix(ItemType item, bool networkSpawn, PhysicalItem __result)
    {
        if (__result != null && ItemCatalog.IsCustom(item))
        {
            DroppedItems.OnServerSpawned(__result, item, networkSpawn);
        }
    }
}

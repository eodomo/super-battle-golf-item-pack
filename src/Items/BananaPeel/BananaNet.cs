using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

// Custom Mirror messages. The game's weaver never saw these types, so serializers are registered by hand.
// Every player in the lobby needs the mod: unmodded clients don't know these message ids.
public struct BananaSpawnRequest : NetworkMessage
{
    public Vector3 position;
    public Vector3 velocity;
}

public struct BananaSpawned : NetworkMessage
{
    public int id;
    public uint ownerNetId;
    public Vector3 position;
    public Vector3 velocity;
}

public struct BananaSlipRequest : NetworkMessage
{
    public int id;
}

public struct BananaDamageRequest : NetworkMessage
{
    public int id;
}

public struct BananaRemoved : NetworkMessage
{
    public int id;
    /// <summary>netId of the player who slipped, or 0 if the peel just expired.</summary>
    public uint slipperNetId;
}

internal static class BananaNet
{
    private const float MaxSpawnDistanceFromPlayer = 6f;
    private const int MaxSpawnsPerWindow = 4;
    private const double SpawnWindowSeconds = 2.0;

    // Server state
    private static int nextId;
    private static readonly LinkedList<int> serverLiveIds = new();
    private static readonly Dictionary<int, Queue<double>> recentSpawnsPerConnection = new();

    public static void InitSerializers()
    {
        Writer<BananaSpawnRequest>.write = (w, m) => { w.WriteVector3(m.position); w.WriteVector3(m.velocity); };
        Reader<BananaSpawnRequest>.read = r => new BananaSpawnRequest { position = r.ReadVector3(), velocity = r.ReadVector3() };

        Writer<BananaSpawned>.write = (w, m) => { w.WriteInt(m.id); w.WriteUInt(m.ownerNetId); w.WriteVector3(m.position); w.WriteVector3(m.velocity); };
        Reader<BananaSpawned>.read = r => new BananaSpawned { id = r.ReadInt(), ownerNetId = r.ReadUInt(), position = r.ReadVector3(), velocity = r.ReadVector3() };

        Writer<BananaSlipRequest>.write = (w, m) => w.WriteInt(m.id);
        Reader<BananaSlipRequest>.read = r => new BananaSlipRequest { id = r.ReadInt() };

        Writer<BananaDamageRequest>.write = (w, m) => w.WriteInt(m.id);
        Reader<BananaDamageRequest>.read = r => new BananaDamageRequest { id = r.ReadInt() };

        Writer<BananaRemoved>.write = (w, m) => { w.WriteInt(m.id); w.WriteUInt(m.slipperNetId); };
        Reader<BananaRemoved>.read = r => new BananaRemoved { id = r.ReadInt(), slipperNetId = r.ReadUInt() };
    }

    public static void RegisterServerHandlers()
    {
        ResetServer();
        NetworkServer.RegisterHandler<BananaSpawnRequest>(OnServerSpawnRequest);
        NetworkServer.RegisterHandler<BananaSlipRequest>(OnServerSlipRequest);
        NetworkServer.RegisterHandler<BananaDamageRequest>(OnServerDamageRequest);
        Plugin.Log.LogInfo("Banana server handlers registered.");
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<BananaSpawned>(OnClientSpawned);
        NetworkClient.ReplaceHandler<BananaRemoved>(OnClientRemoved);
        Plugin.Log.LogInfo("Banana client handlers registered.");
    }

    public static void ResetServer()
    {
        serverLiveIds.Clear();
        recentSpawnsPerConnection.Clear();
    }

    // ---- client -> server ----

    public static void RequestSpawn(Vector3 position, Vector3 velocity)
    {
        if (!NetworkClient.active)
        {
            return;
        }
        NetworkClient.Send(new BananaSpawnRequest { position = position, velocity = velocity });
    }

    public static void RequestSlip(int id)
    {
        if (!NetworkClient.active)
        {
            return;
        }
        NetworkClient.Send(new BananaSlipRequest { id = id });
    }

    /// <summary>A client saw damage reach a landed peel (shots, saber): the host clears it.</summary>
    public static void RequestDamage(int id)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new BananaDamageRequest { id = id });
        }
    }

    /// <summary>Host: damage cleared this peel. No slip, so it just disappears for everyone.</summary>
    public static void ServerRemoveByDamage(int id)
    {
        if (serverLiveIds.Remove(id))
        {
            NetworkServer.SendToAll(new BananaRemoved { id = id, slipperNetId = 0 });
        }
    }

    private static void OnServerDamageRequest(NetworkConnectionToClient conn, BananaDamageRequest msg)
    {
        if (conn.identity != null)
        {
            ServerRemoveByDamage(msg.id);
        }
    }

    // ---- server ----

    private static void OnServerSpawnRequest(NetworkConnectionToClient conn, BananaSpawnRequest msg)
    {
        if (conn.identity == null || !IsWithinRateLimit(conn.connectionId))
        {
            return;
        }
        if ((conn.identity.transform.position - msg.position).sqrMagnitude > MaxSpawnDistanceFromPlayer * MaxSpawnDistanceFromPlayer)
        {
            Plugin.Log.LogWarning($"Rejected banana from connection {conn.connectionId}: too far from its player.");
            return;
        }

        int id = ++nextId;
        serverLiveIds.AddLast(id);
        NetworkServer.SendToAll(new BananaSpawned
        {
            id = id,
            ownerNetId = conn.identity.netId,
            position = msg.position,
            velocity = Vector3.ClampMagnitude(msg.velocity, 40f),
        });

        while (serverLiveIds.Count > Mathf.Max(1, BananaPeelItem.MaxBananas.Value))
        {
            int oldest = serverLiveIds.First.Value;
            serverLiveIds.RemoveFirst();
            NetworkServer.SendToAll(new BananaRemoved { id = oldest, slipperNetId = 0 });
        }
    }

    private static void OnServerSlipRequest(NetworkConnectionToClient conn, BananaSlipRequest msg)
    {
        // First slip wins; later requests for the same peel are ignored.
        if (conn.identity == null || !serverLiveIds.Remove(msg.id))
        {
            return;
        }
        NetworkServer.SendToAll(new BananaRemoved { id = msg.id, slipperNetId = conn.identity.netId });
    }

    private static bool IsWithinRateLimit(int connectionId)
    {
        if (!recentSpawnsPerConnection.TryGetValue(connectionId, out Queue<double> times))
        {
            times = new Queue<double>();
            recentSpawnsPerConnection[connectionId] = times;
        }
        double now = NetworkTime.time;
        while (times.Count > 0 && now - times.Peek() > SpawnWindowSeconds)
        {
            times.Dequeue();
        }
        if (times.Count >= MaxSpawnsPerWindow)
        {
            return false;
        }
        times.Enqueue(now);
        return true;
    }

    // ---- client ----

    private static void OnClientSpawned(BananaSpawned msg)
    {
        BananaWorld.Spawn(msg.id, msg.ownerNetId, msg.position, msg.velocity);
    }

    private static void OnClientRemoved(BananaRemoved msg)
    {
        BananaWorld.Remove(msg.id, msg.slipperNetId);
    }
}

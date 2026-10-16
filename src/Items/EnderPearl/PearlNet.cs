using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct PearlThrowRequest : NetworkMessage
{
    public Vector3 origin;
    public Vector3 velocity;
}

public struct PearlThrown : NetworkMessage
{
    public int id;
    public uint ownerNetId;
    public Vector3 origin;
    public Vector3 velocity;
}

/// <summary>
/// The host only hands out ids and relays throws. Every client flies the pearl along the same path against the
/// level, so it lands in the same spot everywhere, and the thrower's own client moves them there (players own
/// their movement).
/// </summary>
internal static class PearlNet
{
    private static int nextId;
    private static readonly Dictionary<int, double> lastThrow = new();

    public static void InitSerializers()
    {
        Writer<PearlThrowRequest>.write = (w, m) => { w.WriteVector3(m.origin); w.WriteVector3(m.velocity); };
        Reader<PearlThrowRequest>.read = r => new PearlThrowRequest { origin = r.ReadVector3(), velocity = r.ReadVector3() };
        Writer<PearlThrown>.write = (w, m) => { w.WriteInt(m.id); w.WriteUInt(m.ownerNetId); w.WriteVector3(m.origin); w.WriteVector3(m.velocity); };
        Reader<PearlThrown>.read = r => new PearlThrown { id = r.ReadInt(), ownerNetId = r.ReadUInt(), origin = r.ReadVector3(), velocity = r.ReadVector3() };
    }

    public static void RegisterServerHandlers()
    {
        lastThrow.Clear();
        NetworkServer.RegisterHandler<PearlThrowRequest>(OnServerThrow);
    }

    public static void RegisterClientHandlers() =>
        NetworkClient.ReplaceHandler<PearlThrown>(msg => PearlWorld.Spawn(msg.id, msg.ownerNetId, msg.origin, msg.velocity));

    public static void ResetServer() => lastThrow.Clear();

    public static void RequestThrow(Vector3 origin, Vector3 velocity)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new PearlThrowRequest { origin = origin, velocity = velocity });
        }
    }

    private static void OnServerThrow(NetworkConnectionToClient conn, PearlThrowRequest msg)
    {
        if (conn.identity == null)
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastThrow.TryGetValue(conn.connectionId, out double last) && now - last < 0.5)
        {
            return;
        }
        lastThrow[conn.connectionId] = now;
        Vector3 origin = conn.identity.transform.position + Vector3.ClampMagnitude(msg.origin - conn.identity.transform.position, 3f);
        NetworkServer.SendToAll(new PearlThrown
        {
            id = ++nextId,
            ownerNetId = conn.identity.netId,
            origin = origin,
            velocity = Vector3.ClampMagnitude(msg.velocity, 50f),
        });
    }
}

using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

// Custom Mirror messages (serializers registered by hand, like the banana's).
public struct PortalPlaceRequest : NetworkMessage
{
    public byte color;
    public Vector3 position;
    public Quaternion rotation;
}

public struct PortalPlaced : NetworkMessage
{
    public uint ownerNetId;
    public byte color;
    public Vector3 position;
    public Quaternion rotation;
    /// <summary>True for a shot just fired (plays its sound); false when replaying existing portals to a late joiner.</summary>
    public bool fresh;
}

public struct PortalsCleared : NetworkMessage
{
    /// <summary>Owner whose portals are removed, or 0 for everyone's.</summary>
    public uint ownerNetId;
}

/// <summary>The host keeps the authoritative set of portals and relays placements to everyone.</summary>
internal static class PortalNet
{
    private const float MaxPlaceDistance = 160f;
    private const double MinPlaceInterval = 0.15;

    private static readonly Dictionary<(uint owner, byte color), PortalPlaced> serverPortals = new();
    private static readonly Dictionary<int, double> lastPlaceTime = new();

    public static void InitSerializers()
    {
        Writer<PortalPlaceRequest>.write = (w, m) => { w.WriteByte(m.color); w.WriteVector3(m.position); w.WriteQuaternion(m.rotation); };
        Reader<PortalPlaceRequest>.read = r => new PortalPlaceRequest { color = r.ReadByte(), position = r.ReadVector3(), rotation = r.ReadQuaternion() };

        Writer<PortalPlaced>.write = (w, m) => { w.WriteUInt(m.ownerNetId); w.WriteByte(m.color); w.WriteVector3(m.position); w.WriteQuaternion(m.rotation); w.WriteBool(m.fresh); };
        Reader<PortalPlaced>.read = r => new PortalPlaced { ownerNetId = r.ReadUInt(), color = r.ReadByte(), position = r.ReadVector3(), rotation = r.ReadQuaternion(), fresh = r.ReadBool() };

        Writer<PortalsCleared>.write = (w, m) => w.WriteUInt(m.ownerNetId);
        Reader<PortalsCleared>.read = r => new PortalsCleared { ownerNetId = r.ReadUInt() };
    }

    public static void RegisterServerHandlers()
    {
        ResetServer();
        NetworkServer.RegisterHandler<PortalPlaceRequest>(OnServerPlaceRequest);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<PortalPlaced>(msg => Portals.Place(msg.ownerNetId, msg.color, msg.position, msg.rotation, msg.fresh));
        NetworkClient.ReplaceHandler<PortalsCleared>(msg => Portals.Clear(msg.ownerNetId));
    }

    public static void ResetServer()
    {
        serverPortals.Clear();
        lastPlaceTime.Clear();
    }

    /// <summary>Portals belong to a hole: everyone's are removed when the scene changes.</summary>
    public static void ServerClearAll()
    {
        serverPortals.Clear();
        if (NetworkServer.active)
        {
            NetworkServer.SendToAll(new PortalsCleared { ownerNetId = 0 });
        }
    }

    /// <summary>Sends the current portals to a client that just finished loading.</summary>
    public static void SendExisting(NetworkConnectionToClient connection)
    {
        foreach (PortalPlaced portal in serverPortals.Values)
        {
            PortalPlaced existing = portal;
            existing.fresh = false;
            connection.Send(existing);
        }
    }

    public static void OnDisconnect(NetworkConnectionToClient connection)
    {
        lastPlaceTime.Remove(connection.connectionId);
        if (connection.identity == null)
        {
            return;
        }
        uint owner = connection.identity.netId;
        if (serverPortals.Remove((owner, 0)) | serverPortals.Remove((owner, 1)))
        {
            NetworkServer.SendToAll(new PortalsCleared { ownerNetId = owner });
        }
    }

    public static void RequestPlace(byte color, Vector3 position, Quaternion rotation)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new PortalPlaceRequest { color = color, position = position, rotation = rotation });
        }
    }

    private static void OnServerPlaceRequest(NetworkConnectionToClient conn, PortalPlaceRequest msg)
    {
        if (conn.identity == null || msg.color > 1)
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastPlaceTime.TryGetValue(conn.connectionId, out double last) && now - last < MinPlaceInterval)
        {
            return;
        }
        lastPlaceTime[conn.connectionId] = now;
        if ((conn.identity.transform.position - msg.position).sqrMagnitude > MaxPlaceDistance * MaxPlaceDistance)
        {
            Plugin.Log.LogWarning($"Rejected portal from connection {conn.connectionId}: too far from its player.");
            return;
        }

        var placed = new PortalPlaced
        {
            ownerNetId = conn.identity.netId,
            color = msg.color,
            position = msg.position,
            rotation = msg.rotation.normalized,
            fresh = true,
        };
        serverPortals[(placed.ownerNetId, placed.color)] = placed;
        NetworkServer.SendToAll(placed);
    }
}

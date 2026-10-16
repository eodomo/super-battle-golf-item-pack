using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct BubblePlaceRequest : NetworkMessage
{
    public Vector3 position;
}

public struct BubblePlaced : NetworkMessage
{
    public int id;
    public Vector3 position;
    public float radius;
    public float seconds;
}

/// <summary>The host puts the dome on the ground under where it was planted and tells everyone, with its own size and time.</summary>
internal static class BubbleNet
{
    private static int nextId;
    private static readonly Dictionary<int, double> lastPlace = new();

    public static void InitSerializers()
    {
        Writer<BubblePlaceRequest>.write = (w, m) => w.WriteVector3(m.position);
        Reader<BubblePlaceRequest>.read = r => new BubblePlaceRequest { position = r.ReadVector3() };
        Writer<BubblePlaced>.write = (w, m) => { w.WriteInt(m.id); w.WriteVector3(m.position); w.WriteFloat(m.radius); w.WriteFloat(m.seconds); };
        Reader<BubblePlaced>.read = r => new BubblePlaced { id = r.ReadInt(), position = r.ReadVector3(), radius = r.ReadFloat(), seconds = r.ReadFloat() };
    }

    public static void RegisterServerHandlers()
    {
        lastPlace.Clear();
        NetworkServer.RegisterHandler<BubblePlaceRequest>(OnServerPlace);
    }

    public static void RegisterClientHandlers() =>
        NetworkClient.ReplaceHandler<BubblePlaced>(msg => BubbleWorld.Spawn(msg.id, msg.position, msg.radius, msg.seconds));

    public static void ResetServer() => lastPlace.Clear();

    public static void RequestPlace(Vector3 position)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new BubblePlaceRequest { position = position });
        }
    }

    private static void OnServerPlace(NetworkConnectionToClient conn, BubblePlaceRequest msg)
    {
        if (conn.identity == null)
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastPlace.TryGetValue(conn.connectionId, out double last) && now - last < 1.0)
        {
            return;
        }
        lastPlace[conn.connectionId] = now;
        Vector3 body = conn.identity.transform.position;
        Vector3 position = body + Vector3.ClampMagnitude(msg.position - body, 2f);
        if (Physics.Raycast(position + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 6f, WorldMask.Solid, QueryTriggerInteraction.Ignore))
        {
            position = ground.point;
        }
        NetworkServer.SendToAll(new BubblePlaced
        {
            id = ++nextId,
            position = position,
            radius = Mathf.Clamp(BubbleShieldItem.Radius.Value, 1f, 15f),
            seconds = Mathf.Clamp(BubbleShieldItem.Seconds.Value, 1f, 60f),
        });
    }
}

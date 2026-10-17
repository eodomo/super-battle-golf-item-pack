using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct CapThrowRequest : NetworkMessage
{
    public byte state;
    public Vector3 origin;
    public Vector3 direction;
}

public struct CapThrowState : NetworkMessage
{
    public uint throwerNetId;
    public byte state;
    public Vector3 origin;
    public Vector3 direction;
}

public struct CapBlockRequest : NetworkMessage
{
    public Vector3 position;
}

public struct CapBlocked : NetworkMessage
{
    public Vector3 position;
}

/// <summary>The host to the player whose shield just stopped an instant elimination: use the charge up (see <see cref="ShieldEliminationPatch"/>).</summary>
public struct CapBlockOrder : NetworkMessage
{
    public byte reason;
}

/// <summary>
/// The host relays the thrower's shield flights (thrown, turned back, gone) and blocks (for the clang), like the
/// lightsaber's throws. Hits are checked by the thrower's client.
/// </summary>
internal static class CapShieldNet
{
    private static readonly Dictionary<int, double> lastBlock = new();

    public static void InitSerializers()
    {
        Writer<CapThrowRequest>.write = (w, m) => { w.WriteByte(m.state); w.WriteVector3(m.origin); w.WriteVector3(m.direction); };
        Reader<CapThrowRequest>.read = r => new CapThrowRequest { state = r.ReadByte(), origin = r.ReadVector3(), direction = r.ReadVector3() };
        Writer<CapThrowState>.write = (w, m) => { w.WriteUInt(m.throwerNetId); w.WriteByte(m.state); w.WriteVector3(m.origin); w.WriteVector3(m.direction); };
        Reader<CapThrowState>.read = r => new CapThrowState { throwerNetId = r.ReadUInt(), state = r.ReadByte(), origin = r.ReadVector3(), direction = r.ReadVector3() };
        Writer<CapBlockRequest>.write = (w, m) => w.WriteVector3(m.position);
        Reader<CapBlockRequest>.read = r => new CapBlockRequest { position = r.ReadVector3() };
        Writer<CapBlocked>.write = (w, m) => w.WriteVector3(m.position);
        Reader<CapBlocked>.read = r => new CapBlocked { position = r.ReadVector3() };
        Writer<CapBlockOrder>.write = (w, m) => w.WriteByte(m.reason);
        Reader<CapBlockOrder>.read = r => new CapBlockOrder { reason = r.ReadByte() };
    }

    public static void RegisterServerHandlers()
    {
        lastBlock.Clear();
        NetworkServer.RegisterHandler<CapThrowRequest>((conn, msg) =>
        {
            if (conn.identity != null)
            {
                NetworkServer.SendToAll(new CapThrowState
                {
                    throwerNetId = conn.identity.netId, state = msg.state, origin = msg.origin, direction = msg.direction,
                });
            }
        });
        NetworkServer.RegisterHandler<CapBlockRequest>((conn, msg) =>
        {
            if (conn.identity == null)
            {
                return;
            }
            double now = NetworkTime.time;
            if (lastBlock.TryGetValue(conn.connectionId, out double last) && now - last < 0.1)
            {
                return;
            }
            lastBlock[conn.connectionId] = now;
            Vector3 body = conn.identity.transform.position;
            NetworkServer.SendToAll(new CapBlocked { position = body + Vector3.ClampMagnitude(msg.position - body, 3f) });
        });
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<CapThrowState>(CapShieldThrows.OnState);
        NetworkClient.ReplaceHandler<CapBlocked>(msg => CapShieldThrows.PlayBlock(msg.position));
        NetworkClient.ReplaceHandler<CapBlockOrder>(msg =>
        {
            if (GameManager.LocalPlayerInfo != null)
            {
                CapShieldItem.TryBlock(GameManager.LocalPlayerInfo, (KnockoutType)msg.reason);
            }
        });
    }

    public static void ResetServer() => lastBlock.Clear();

    public static void SendThrowState(byte state, Vector3 origin, Vector3 direction)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new CapThrowRequest { state = state, origin = origin, direction = direction });
        }
    }

    /// <summary>Host: tells <paramref name="victim"/>'s client their shield stopped an instant elimination.</summary>
    public static void SendBlockOrder(PlayerInfo victim, KnockoutType kind)
    {
        if (NetworkServer.active && victim.connectionToClient != null)
        {
            victim.connectionToClient.Send(new CapBlockOrder { reason = (byte)kind });
        }
    }

    public static void SendBlock(Vector3 position)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new CapBlockRequest { position = position });
        }
    }
}

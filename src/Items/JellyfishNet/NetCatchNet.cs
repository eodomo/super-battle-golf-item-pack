using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct NetSwingRequest : NetworkMessage
{
}

public struct NetSwing : NetworkMessage
{
    public uint swingerNetId;
}

public struct NetCatchRequest : NetworkMessage
{
    public uint targetNetId;
    public int slot;
}

public struct NetCaught : NetworkMessage
{
    public uint carrierNetId;
    public uint targetNetId;
    public float seconds;
}

public struct NetReleaseRequest : NetworkMessage
{
    public Vector3 velocity;
}

public struct NetReleased : NetworkMessage
{
    public uint carrierNetId;
    public uint targetNetId;
    public Vector3 velocity;
}

/// <summary>
/// The host decides who is caught: a carrier asks to catch a player they swung at, the host checks they're close and
/// both are free, and tells everyone. Players move themselves, so the caught player's own client then holds itself in the
/// net (see <see cref="NetCatches"/>) until the host says they're released (the carrier threw them, time ran out, or
/// somebody left).
/// </summary>
internal static class NetCatchNet
{
    private const float MaxCatchDistance = 6f;

    private sealed class Catch
    {
        public uint Target;
        public float Until;
    }

    private static readonly Dictionary<uint, Catch> serverCatches = new();
    private static readonly List<uint> expired = new();

    public static void InitSerializers()
    {
        Writer<NetSwingRequest>.write = (_, _) => { };
        Reader<NetSwingRequest>.read = _ => new NetSwingRequest();
        Writer<NetSwing>.write = (w, m) => w.WriteUInt(m.swingerNetId);
        Reader<NetSwing>.read = r => new NetSwing { swingerNetId = r.ReadUInt() };
        Writer<NetCatchRequest>.write = (w, m) => { w.WriteUInt(m.targetNetId); w.WriteInt(m.slot); };
        Reader<NetCatchRequest>.read = r => new NetCatchRequest { targetNetId = r.ReadUInt(), slot = r.ReadInt() };
        Writer<NetCaught>.write = (w, m) => { w.WriteUInt(m.carrierNetId); w.WriteUInt(m.targetNetId); w.WriteFloat(m.seconds); };
        Reader<NetCaught>.read = r => new NetCaught { carrierNetId = r.ReadUInt(), targetNetId = r.ReadUInt(), seconds = r.ReadFloat() };
        Writer<NetReleaseRequest>.write = (w, m) => w.WriteVector3(m.velocity);
        Reader<NetReleaseRequest>.read = r => new NetReleaseRequest { velocity = r.ReadVector3() };
        Writer<NetReleased>.write = (w, m) => { w.WriteUInt(m.carrierNetId); w.WriteUInt(m.targetNetId); w.WriteVector3(m.velocity); };
        Reader<NetReleased>.read = r => new NetReleased { carrierNetId = r.ReadUInt(), targetNetId = r.ReadUInt(), velocity = r.ReadVector3() };
    }

    public static void RegisterServerHandlers()
    {
        serverCatches.Clear();
        NetworkServer.RegisterHandler<NetSwingRequest>((conn, _) =>
        {
            if (conn.identity != null)
            {
                NetworkServer.SendToAll(new NetSwing { swingerNetId = conn.identity.netId });
            }
        });
        NetworkServer.RegisterHandler<NetCatchRequest>(OnCatchRequest);
        NetworkServer.RegisterHandler<NetReleaseRequest>((conn, msg) =>
        {
            if (conn.identity != null)
            {
                Vector3 velocity = Vector3.ClampMagnitude(msg.velocity, 30f);
                ServerRelease(conn.identity.netId, velocity);
            }
        });
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<NetSwing>(msg =>
        {
            if (NetworkClient.spawned.TryGetValue(msg.swingerNetId, out NetworkIdentity identity) && identity != null
                && identity.TryGetComponent(out PlayerInfo player))
            {
                if (GameManager.LocalPlayerInfo == null || GameManager.LocalPlayerInfo.netId != msg.swingerNetId)
                {
                    NetCatches.PlaySwing(player); // the swinger already played theirs
                }
            }
        });
        NetworkClient.ReplaceHandler<NetCaught>(NetCatches.OnCaught);
        NetworkClient.ReplaceHandler<NetReleased>(NetCatches.OnReleased);
    }

    public static void ResetServer() => serverCatches.Clear();

    public static void SendSwing(PlayerInfo player)
    {
        NetCatches.PlaySwing(player);
        if (NetworkClient.active)
        {
            NetworkClient.Send(new NetSwingRequest());
        }
    }

    public static void SendCatch(uint targetNetId, int slot)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new NetCatchRequest { targetNetId = targetNetId, slot = slot });
        }
    }

    public static void SendRelease(Vector3 velocity)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new NetReleaseRequest { velocity = velocity });
        }
    }

    private static void OnCatchRequest(NetworkConnectionToClient conn, NetCatchRequest msg)
    {
        if (conn.identity == null || !NetworkServer.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity target) || target == null
            || !target.TryGetComponent(out PlayerInfo caught) || target == conn.identity)
        {
            return;
        }
        uint carrier = conn.identity.netId;
        if (serverCatches.ContainsKey(carrier) || IsCaught(msg.targetNetId))
        {
            return;
        }
        if ((target.transform.position - conn.identity.transform.position).sqrMagnitude > MaxCatchDistance * MaxCatchDistance
            || caught.ActiveGolfCartSeat.IsValid())
        {
            return;
        }
        float seconds = Mathf.Clamp(JellyfishNetItem.CarrySeconds.Value, 1f, 60f);
        serverCatches[carrier] = new Catch { Target = msg.targetNetId, Until = Time.time + seconds + 2f };
        NetworkServer.SendToAll(new NetCaught { carrierNetId = carrier, targetNetId = msg.targetNetId, seconds = seconds });
    }

    public static bool IsCaught(uint netId)
    {
        foreach (Catch c in serverCatches.Values)
        {
            if (c.Target == netId)
            {
                return true;
            }
        }
        return false;
    }

    private static void ServerRelease(uint carrier, Vector3 velocity)
    {
        if (serverCatches.TryGetValue(carrier, out Catch c))
        {
            serverCatches.Remove(carrier);
            NetworkServer.SendToAll(new NetReleased { carrierNetId = carrier, targetNetId = c.Target, velocity = velocity });
        }
    }

    public static void OnDisconnect(NetworkConnectionToClient connection)
    {
        if (connection?.identity == null)
        {
            return;
        }
        ServerRelease(connection.identity.netId, Vector3.zero);
        // Whoever they had caught is handled above; if the leaver was the one caught, let their carrier go too.
        foreach (KeyValuePair<uint, Catch> pair in new List<KeyValuePair<uint, Catch>>(serverCatches))
        {
            if (pair.Value.Target == connection.identity.netId)
            {
                ServerRelease(pair.Key, Vector3.zero);
            }
        }
    }

    /// <summary>Host, every physics step: lets go of anyone held past their time, or whose carrier is gone.</summary>
    public static void ServerTick()
    {
        if (!NetworkServer.active || serverCatches.Count == 0)
        {
            return;
        }
        expired.Clear();
        foreach (KeyValuePair<uint, Catch> pair in serverCatches)
        {
            if (Time.time > pair.Value.Until
                || (NetworkServer.spawned.TryGetValue(pair.Key, out NetworkIdentity carrierId) && carrierId.TryGetComponent(out PlayerInfo carrierInfo) && carrierInfo.Movement.IsKnockedOutOrRecovering)
                || !NetworkServer.spawned.ContainsKey(pair.Key) || !NetworkServer.spawned.ContainsKey(pair.Value.Target))
            {
                expired.Add(pair.Key);
            }
        }
        foreach (uint carrier in expired)
        {
            ServerRelease(carrier, Vector3.zero);
        }
    }
}

using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct PredatorLaunchRequest : NetworkMessage
{
    public Vector3 position;
    public Vector3 direction;
}

public struct PredatorLaunched : NetworkMessage
{
    public int id;
    public uint ownerNetId;
    public Vector3 position;
    public Vector3 direction;
}

public struct PredatorUpdate : NetworkMessage
{
    public int id;
    public Vector3 position;
    public Vector3 direction;
    public bool boosted;
}

public struct PredatorImpact : NetworkMessage
{
    public int id;
    public Vector3 position;
}

/// <summary>
/// The pilot flies the missile on their own client and streams its position; the host relays it, and on
/// impact runs the explosion (knockouts) and tells everyone to play it.
/// </summary>
internal static class PredatorNet
{
    private class ServerMissile
    {
        public uint owner;
        public Vector3 lastPosition;
        public double launchTime;
    }

    private static int nextId;
    private static readonly Dictionary<int, ServerMissile> missiles = new();

    public static void InitSerializers()
    {
        Writer<PredatorLaunchRequest>.write = (w, m) => { w.WriteVector3(m.position); w.WriteVector3(m.direction); };
        Reader<PredatorLaunchRequest>.read = r => new PredatorLaunchRequest { position = r.ReadVector3(), direction = r.ReadVector3() };
        Writer<PredatorLaunched>.write = (w, m) => { w.WriteInt(m.id); w.WriteUInt(m.ownerNetId); w.WriteVector3(m.position); w.WriteVector3(m.direction); };
        Reader<PredatorLaunched>.read = r => new PredatorLaunched { id = r.ReadInt(), ownerNetId = r.ReadUInt(), position = r.ReadVector3(), direction = r.ReadVector3() };
        Writer<PredatorUpdate>.write = (w, m) => { w.WriteInt(m.id); w.WriteVector3(m.position); w.WriteVector3(m.direction); w.WriteBool(m.boosted); };
        Reader<PredatorUpdate>.read = r => new PredatorUpdate { id = r.ReadInt(), position = r.ReadVector3(), direction = r.ReadVector3(), boosted = r.ReadBool() };
        Writer<PredatorImpact>.write = (w, m) => { w.WriteInt(m.id); w.WriteVector3(m.position); };
        Reader<PredatorImpact>.read = r => new PredatorImpact { id = r.ReadInt(), position = r.ReadVector3() };
    }

    public static void RegisterServerHandlers()
    {
        missiles.Clear();
        NetworkServer.RegisterHandler<PredatorLaunchRequest>(OnServerLaunch);
        NetworkServer.RegisterHandler<PredatorUpdate>(OnServerUpdate);
        NetworkServer.RegisterHandler<PredatorImpact>(OnServerImpact);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<PredatorLaunched>(msg =>
        {
            if (IsLocal(msg.ownerNetId))
            {
                PredatorFlight.OnLaunchConfirmed(msg.id);
            }
            else
            {
                PredatorRemote.Spawn(msg.id, msg.position, msg.direction);
            }
        });
        NetworkClient.ReplaceHandler<PredatorUpdate>(msg => PredatorRemote.UpdateMissile(msg.id, msg.position, msg.direction, msg.boosted));
        NetworkClient.ReplaceHandler<PredatorImpact>(msg =>
        {
            PredatorRemote.Explode(msg.id);
            Explosions.PlayEffects(msg.position, PredatorItem.ExplosionScale.Value);
        });
    }

    public static void ResetServer() => missiles.Clear();

    public static void RequestLaunch(Vector3 position, Vector3 direction)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new PredatorLaunchRequest { position = position, direction = direction });
        }
    }

    public static void SendUpdate(int id, Vector3 position, Vector3 direction, bool boosted)
    {
        if (NetworkClient.active && id != 0)
        {
            NetworkClient.Send(new PredatorUpdate { id = id, position = position, direction = direction, boosted = boosted });
        }
    }

    public static void SendImpact(int id, Vector3 position)
    {
        if (NetworkClient.active && id != 0)
        {
            NetworkClient.Send(new PredatorImpact { id = id, position = position });
        }
    }

    private static void OnServerLaunch(NetworkConnectionToClient conn, PredatorLaunchRequest msg)
    {
        if (conn.identity == null)
        {
            return;
        }
        foreach (ServerMissile existing in missiles.Values)
        {
            if (existing.owner == conn.identity.netId)
            {
                return; // one at a time
            }
        }
        int id = ++nextId;
        missiles[id] = new ServerMissile { owner = conn.identity.netId, lastPosition = msg.position, launchTime = NetworkTime.time };
        NetworkServer.SendToAll(new PredatorLaunched { id = id, ownerNetId = conn.identity.netId, position = msg.position, direction = msg.direction.normalized });
    }

    private static void OnServerUpdate(NetworkConnectionToClient conn, PredatorUpdate msg)
    {
        if (conn.identity == null || !missiles.TryGetValue(msg.id, out ServerMissile missile) || missile.owner != conn.identity.netId)
        {
            return;
        }
        missile.lastPosition = msg.position;
        foreach (NetworkConnectionToClient other in NetworkServer.connections.Values)
        {
            if (other != conn)
            {
                other.Send(msg);
            }
        }
    }

    private static void OnServerImpact(NetworkConnectionToClient conn, PredatorImpact msg)
    {
        if (conn.identity == null || !missiles.TryGetValue(msg.id, out ServerMissile missile) || missile.owner != conn.identity.netId)
        {
            return;
        }
        missiles.Remove(msg.id);
        // The impact has to be near where the missile last was (it can't teleport across the map).
        Vector3 position = missile.lastPosition + Vector3.ClampMagnitude(msg.position - missile.lastPosition, 40f);
        Explosions.ServerExplode(position, PredatorItem.ExplosionScale.Value, conn.identity.GetComponent<PlayerInfo>());
        NetworkServer.SendToAll(new PredatorImpact { id = msg.id, position = position });
    }

    public static bool IsLocal(uint netId) => GameManager.LocalPlayerInfo != null && GameManager.LocalPlayerInfo.netId == netId;
}

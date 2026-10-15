using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct BlueShellRequest : NetworkMessage
{
    public Vector3 position;
}

public struct BlueShellLaunched : NetworkMessage
{
    public int id;
    public uint ownerNetId;
    public uint targetNetId;
    public Vector3 start;
    public float travelSeconds;
}

public struct BlueShellImpact : NetworkMessage
{
    public int id;
    public Vector3 position;
    public bool hitBall;
    public Vector3 ballPosition;
}

/// <summary>
/// The host picks the leader (the player whose ball is closest to the hole), times the flight, and when the
/// shell comes down runs the explosions: one on the leader, one that knocks their ball away from the hole.
/// Every client flies the same shell using the shared timing (<see cref="BlueShellFlight"/>).
/// </summary>
internal static class BlueShellNet
{
    private class Pending
    {
        public int id;
        public PlayerInfo owner;
        public PlayerInfo target;
        public Vector3 lastTargetPosition;
        public double impactTime;
    }

    private static int nextId;
    private static readonly List<Pending> pending = new();
    private static readonly Dictionary<int, double> lastUse = new();

    public static void InitSerializers()
    {
        Writer<BlueShellRequest>.write = (w, m) => w.WriteVector3(m.position);
        Reader<BlueShellRequest>.read = r => new BlueShellRequest { position = r.ReadVector3() };
        Writer<BlueShellLaunched>.write = (w, m) =>
        {
            w.WriteInt(m.id); w.WriteUInt(m.ownerNetId); w.WriteUInt(m.targetNetId); w.WriteVector3(m.start); w.WriteFloat(m.travelSeconds);
        };
        Reader<BlueShellLaunched>.read = r => new BlueShellLaunched
        {
            id = r.ReadInt(), ownerNetId = r.ReadUInt(), targetNetId = r.ReadUInt(), start = r.ReadVector3(), travelSeconds = r.ReadFloat(),
        };
        Writer<BlueShellImpact>.write = (w, m) => { w.WriteInt(m.id); w.WriteVector3(m.position); w.WriteBool(m.hitBall); w.WriteVector3(m.ballPosition); };
        Reader<BlueShellImpact>.read = r => new BlueShellImpact { id = r.ReadInt(), position = r.ReadVector3(), hitBall = r.ReadBool(), ballPosition = r.ReadVector3() };
    }

    public static void RegisterServerHandlers()
    {
        ResetServer();
        NetworkServer.RegisterHandler<BlueShellRequest>(OnServerRequest);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<BlueShellLaunched>(msg => BlueShellFlight.Launch(msg.id, msg.ownerNetId, msg.targetNetId, msg.start, msg.travelSeconds));
        NetworkClient.ReplaceHandler<BlueShellImpact>(msg => BlueShellFlight.Impact(msg.id, msg.position, msg.hitBall, msg.ballPosition));
    }

    public static void ResetServer()
    {
        pending.Clear();
        lastUse.Clear();
    }

    public static void RequestLaunch(Vector3 position)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new BlueShellRequest { position = position });
        }
    }

    private static void OnServerRequest(NetworkConnectionToClient conn, BlueShellRequest msg)
    {
        if (conn.identity == null || !conn.identity.TryGetComponent(out PlayerInfo owner))
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastUse.TryGetValue(conn.connectionId, out double last) && now - last < 1.0)
        {
            return;
        }
        lastUse[conn.connectionId] = now;

        PlayerInfo target = FindLeader(owner);
        Vector3 start = owner.transform.position + Vector3.ClampMagnitude(msg.position - owner.transform.position, 3f);
        float distance = Vector3.Distance(start, target.transform.position);
        float travel = Mathf.Clamp(distance / Mathf.Max(5f, BlueShellItem.Speed.Value), 1.5f, 10f);
        int id = ++nextId;
        pending.Add(new Pending
        {
            id = id,
            owner = owner,
            target = target,
            lastTargetPosition = target.transform.position,
            impactTime = now + BlueShellFlight.TotalSeconds(travel),
        });
        NetworkServer.SendToAll(new BlueShellLaunched { id = id, ownerNetId = owner.netId, targetNetId = target.netId, start = start, travelSeconds = travel });
        Plugin.Log.LogInfo($"Blue shell {id} is after {target.PlayerId.PlayerName}.");
    }

    /// <summary>Server tick: bring down shells whose time is up.</summary>
    public static void ServerTick()
    {
        if (!NetworkServer.active || pending.Count == 0)
        {
            return;
        }
        double now = NetworkTime.time;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            Pending shell = pending[i];
            if (shell.target != null)
            {
                shell.lastTargetPosition = shell.target.transform.position;
            }
            if (now < shell.impactTime)
            {
                continue;
            }
            pending.RemoveAt(i);
            Explode(shell);
        }
    }

    private static void Explode(Pending shell)
    {
        Vector3 position = shell.lastTargetPosition;
        Explosions.ServerExplode(position, BlueShellItem.ExplosionScale.Value, shell.owner);

        // Knock the leader's ball away from the hole: blast it from the hole's side.
        bool hitBall = false;
        Vector3 ballPosition = default;
        GolfBall ball = shell.target != null ? shell.target.AsGolfer.OwnBall : null;
        if (ball != null && ball.gameObject.activeInHierarchy && BlueShellItem.HitBall.Value)
        {
            ballPosition = ball.transform.position;
            Vector3 towardHole = GolfHoleManager.MainHole != null
                ? Vector3.ProjectOnPlane(GolfHoleManager.MainHole.transform.position - ballPosition, Vector3.up).normalized
                : Vector3.zero;
            Explosions.ServerExplode(ballPosition + towardHole * 0.8f - Vector3.up * 0.3f, 0.5f, shell.owner);
            hitBall = true;
        }
        NetworkServer.SendToAll(new BlueShellImpact { id = shell.id, position = position, hitBall = hitBall, ballPosition = ballPosition });
    }

    /// <summary>
    /// The player whose ball is closest to the hole (players who already finished the hole don't count). In the
    /// Driving Range there's no hole: then any other player, or the thrower if they're alone.
    /// </summary>
    private static PlayerInfo FindLeader(PlayerInfo owner)
    {
        GolfHole hole = GolfHoleManager.MainHole;
        PlayerInfo best = null;
        float bestDistance = float.MaxValue;
        var others = new List<PlayerInfo>();
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection.identity == null || !connection.identity.TryGetComponent(out PlayerInfo player) || player.AsGolfer.IsMatchResolved)
            {
                continue;
            }
            if (player != owner)
            {
                others.Add(player);
            }
            GolfBall ball = player.AsGolfer.OwnBall;
            if (hole == null || ball == null)
            {
                continue;
            }
            float distance = (ball.transform.position - hole.transform.position).sqrMagnitude;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = player;
            }
        }
        if (best != null)
        {
            return best; // can be the thrower: in Mario Kart, first place throwing a blue shell gets hit too
        }
        return others.Count > 0 ? others[Random.Range(0, others.Count)] : owner;
    }
}

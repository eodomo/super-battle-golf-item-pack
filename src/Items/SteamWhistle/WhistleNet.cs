using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct WhistleBlowRequest : NetworkMessage
{
    /// <summary>Where the player was looking (for the "Aimed" target mode, if the target below is gone).</summary>
    public Vector3 aimDirection;
    /// <summary>The opponent the player had locked on (0 for none).</summary>
    public uint targetNetId;
}

public struct WhistleBlown : NetworkMessage
{
    public uint playerNetId;
}

public struct DragonAttack : NetworkMessage
{
    public int id;
    public uint summonerNetId;
    public uint targetNetId;
    /// <summary>Compass direction (degrees) the dragon comes from, so every client flies the same path.</summary>
    public float approachYaw;
}

/// <summary>The host picks who gets burned and tells everyone; each client plays the attack locally.</summary>
internal static class WhistleNet
{
    private const double MinBlowInterval = 1.0;

    private static int nextId;
    private static readonly Dictionary<int, double> lastBlow = new();

    public static void InitSerializers()
    {
        Writer<WhistleBlowRequest>.write = (w, m) => { w.WriteVector3(m.aimDirection); w.WriteUInt(m.targetNetId); };
        Reader<WhistleBlowRequest>.read = r => new WhistleBlowRequest { aimDirection = r.ReadVector3(), targetNetId = r.ReadUInt() };

        Writer<WhistleBlown>.write = (w, m) => w.WriteUInt(m.playerNetId);
        Reader<WhistleBlown>.read = r => new WhistleBlown { playerNetId = r.ReadUInt() };

        Writer<DragonAttack>.write = (w, m) => { w.WriteInt(m.id); w.WriteUInt(m.summonerNetId); w.WriteUInt(m.targetNetId); w.WriteFloat(m.approachYaw); };
        Reader<DragonAttack>.read = r => new DragonAttack { id = r.ReadInt(), summonerNetId = r.ReadUInt(), targetNetId = r.ReadUInt(), approachYaw = r.ReadFloat() };
    }

    public static void RegisterServerHandlers()
    {
        lastBlow.Clear();
        NetworkServer.RegisterHandler<WhistleBlowRequest>(OnServerBlow);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<WhistleBlown>(msg => DragonAttacks.PlayWhistle(msg.playerNetId));
        NetworkClient.ReplaceHandler<DragonAttack>(DragonAttacks.Begin);
    }

    public static void ResetServer() => lastBlow.Clear();

    public static void RequestBlow(Vector3 aimDirection, uint targetNetId)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new WhistleBlowRequest { aimDirection = aimDirection, targetNetId = targetNetId });
        }
    }

    private static void OnServerBlow(NetworkConnectionToClient conn, WhistleBlowRequest msg)
    {
        if (conn.identity == null || !conn.identity.TryGetComponent(out PlayerInfo summoner))
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastBlow.TryGetValue(conn.connectionId, out double last) && now - last < MinBlowInterval)
        {
            return;
        }
        lastBlow[conn.connectionId] = now;

        NetworkServer.SendToAll(new WhistleBlown { playerNetId = summoner.netId });
        PlayerInfo target = PickTarget(summoner, msg);
        NetworkServer.SendToAll(new DragonAttack
        {
            id = ++nextId,
            summonerNetId = summoner.netId,
            targetNetId = target.netId,
            approachYaw = Random.Range(0f, 360f),
        });
    }

    private static PlayerInfo PickTarget(PlayerInfo summoner, WhistleBlowRequest request)
    {
        var everyone = new List<PlayerInfo>();
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection.identity != null && connection.identity.TryGetComponent(out PlayerInfo player))
            {
                everyone.Add(player);
            }
        }
        if (SteamWhistleItem.TargetMode.Value == WhistleTargetMode.Aimed)
        {
            // The opponent the summoner's client had locked on, if still fair game; else the one closest to
            // where they looked.
            PlayerInfo locked = everyone.Find(player => player.netId == request.targetNetId);
            if (request.targetNetId != 0 && SteamWhistleItem.IsOpponent(summoner, locked))
            {
                return locked;
            }
            PlayerInfo aimed = SteamWhistleItem.FindAimed(summoner, everyone, summoner.transform.position, request.aimDirection);
            if (aimed != null)
            {
                return aimed;
            }
        }
        if (SteamWhistleItem.TargetMode.Value == WhistleTargetMode.Leader)
        {
            // No hole (Driving Range) or nobody targetable: a random other player instead.
            PlayerInfo leader = SteamWhistleItem.FindLeader(summoner, everyone);
            if (leader != null)
            {
                return leader;
            }
        }
        List<PlayerInfo> candidates = everyone.FindAll(player => player != summoner && !player.AsGolfer.IsMatchResolved);
        if (candidates.Count == 0)
        {
            return summoner; // nobody else around (e.g. testing alone): Thomas comes for you
        }

        switch (SteamWhistleItem.TargetMode.Value)
        {
            case WhistleTargetMode.Nearest:
                candidates.Sort((a, b) => Distance(summoner, a).CompareTo(Distance(summoner, b)));
                return candidates[0];
            default:
                return candidates[Random.Range(0, candidates.Count)];
        }
    }

    private static float Distance(PlayerInfo a, PlayerInfo b) => (a.transform.position - b.transform.position).sqrMagnitude;
}

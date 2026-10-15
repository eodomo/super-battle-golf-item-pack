using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct WhistleBlowRequest : NetworkMessage
{
    /// <summary>Where the player was looking (for the "Aimed" target mode).</summary>
    public Vector3 aimDirection;
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
        Writer<WhistleBlowRequest>.write = (w, m) => w.WriteVector3(m.aimDirection);
        Reader<WhistleBlowRequest>.read = r => new WhistleBlowRequest { aimDirection = r.ReadVector3() };

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

    public static void RequestBlow(Vector3 aimDirection)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new WhistleBlowRequest { aimDirection = aimDirection });
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
        PlayerInfo target = PickTarget(summoner, msg.aimDirection);
        NetworkServer.SendToAll(new DragonAttack
        {
            id = ++nextId,
            summonerNetId = summoner.netId,
            targetNetId = target.netId,
            approachYaw = Random.Range(0f, 360f),
        });
    }

    private static PlayerInfo PickTarget(PlayerInfo summoner, Vector3 aimDirection)
    {
        var candidates = new List<PlayerInfo>();
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection.identity != null && connection.identity.TryGetComponent(out PlayerInfo player)
                && player != summoner && !player.AsGolfer.IsMatchResolved)
            {
                candidates.Add(player);
            }
        }
        if (candidates.Count == 0)
        {
            return summoner; // nobody else around (e.g. testing alone): Thomas comes for you
        }

        switch (SteamWhistleItem.TargetMode.Value)
        {
            case WhistleTargetMode.Nearest:
                candidates.Sort((a, b) => Distance(summoner, a).CompareTo(Distance(summoner, b)));
                return candidates[0];
            case WhistleTargetMode.Aimed:
                PlayerInfo best = candidates[0];
                float bestDot = float.MinValue;
                foreach (PlayerInfo candidate in candidates)
                {
                    float dot = Vector3.Dot(aimDirection.normalized, (candidate.transform.position - summoner.transform.position).normalized);
                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        best = candidate;
                    }
                }
                return best;
            default:
                return candidates[Random.Range(0, candidates.Count)];
        }
    }

    private static float Distance(PlayerInfo a, PlayerInfo b) => (a.transform.position - b.transform.position).sqrMagnitude;
}

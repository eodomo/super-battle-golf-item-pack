using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct KameChargeMsg : NetworkMessage
{
    public uint playerNetId;
    public bool charging;
}

public struct KameFireMsg : NetworkMessage
{
    public uint playerNetId;
    public Vector3 origin;
    public Vector3 direction;
    public float length;
    public float radius;
    public float power;
}

public struct KameHitRequest : NetworkMessage
{
    public uint[] targets;
    public Vector3 direction;
    public float power;
}

public struct KameHitMsg : NetworkMessage
{
    public uint shooterNetId;
    public uint targetNetId;
    public Vector3 direction;
    public float power;
}

/// <summary>
/// The shooter's client works out who the beam hits; the host relays the charge, the beam and the hits.
/// Each hit player's own client knocks itself out (players own their movement). The host pushes balls.
/// </summary>
internal static class KameNet
{
    private const float MaxPower = 60f;
    private static readonly Dictionary<int, double> lastFire = new();
    private static readonly RaycastHit[] ballHits = new RaycastHit[32];

    public static void InitSerializers()
    {
        Writer<KameChargeMsg>.write = (w, m) => { w.WriteUInt(m.playerNetId); w.WriteBool(m.charging); };
        Reader<KameChargeMsg>.read = r => new KameChargeMsg { playerNetId = r.ReadUInt(), charging = r.ReadBool() };
        Writer<KameFireMsg>.write = (w, m) =>
        {
            w.WriteUInt(m.playerNetId); w.WriteVector3(m.origin); w.WriteVector3(m.direction);
            w.WriteFloat(m.length); w.WriteFloat(m.radius); w.WriteFloat(m.power);
        };
        Reader<KameFireMsg>.read = r => new KameFireMsg
        {
            playerNetId = r.ReadUInt(), origin = r.ReadVector3(), direction = r.ReadVector3(),
            length = r.ReadFloat(), radius = r.ReadFloat(), power = r.ReadFloat(),
        };
        Writer<KameHitRequest>.write = (w, m) =>
        {
            w.WriteInt(m.targets.Length);
            foreach (uint t in m.targets)
            {
                w.WriteUInt(t);
            }
            w.WriteVector3(m.direction);
            w.WriteFloat(m.power);
        };
        Reader<KameHitRequest>.read = r =>
        {
            int n = Mathf.Clamp(r.ReadInt(), 0, 32);
            var targets = new uint[n];
            for (int i = 0; i < n; i++)
            {
                targets[i] = r.ReadUInt();
            }
            return new KameHitRequest { targets = targets, direction = r.ReadVector3(), power = r.ReadFloat() };
        };
        Writer<KameHitMsg>.write = (w, m) => { w.WriteUInt(m.shooterNetId); w.WriteUInt(m.targetNetId); w.WriteVector3(m.direction); w.WriteFloat(m.power); };
        Reader<KameHitMsg>.read = r => new KameHitMsg { shooterNetId = r.ReadUInt(), targetNetId = r.ReadUInt(), direction = r.ReadVector3(), power = r.ReadFloat() };
    }

    public static void RegisterServerHandlers()
    {
        lastFire.Clear();
        NetworkServer.RegisterHandler<KameChargeMsg>((conn, msg) =>
        {
            if (conn.identity != null)
            {
                NetworkServer.SendToAll(new KameChargeMsg { playerNetId = conn.identity.netId, charging = msg.charging });
            }
        });
        NetworkServer.RegisterHandler<KameFireMsg>(OnServerFire);
        NetworkServer.RegisterHandler<KameHitRequest>(OnServerHit);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<KameChargeMsg>(msg =>
        {
            if (!IsLocal(msg.playerNetId))
            {
                KameEffects.SetCharging(msg.playerNetId, msg.charging);
            }
        });
        NetworkClient.ReplaceHandler<KameFireMsg>(msg =>
        {
            if (!IsLocal(msg.playerNetId))
            {
                KameEffects.Fire(msg.playerNetId, msg.origin, msg.direction, msg.length, msg.radius);
            }
        });
        NetworkClient.ReplaceHandler<KameHitMsg>(OnClientHit);
    }

    public static void ResetServer() => lastFire.Clear();

    public static void SendCharging(bool charging)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new KameChargeMsg { charging = charging });
        }
    }

    public static void SendFire(Vector3 origin, Vector3 direction, float length, float radius, float power, List<uint> targets)
    {
        if (!NetworkClient.active)
        {
            return;
        }
        NetworkClient.Send(new KameFireMsg { origin = origin, direction = direction, length = length, radius = radius, power = power });
        if (targets.Count > 0)
        {
            NetworkClient.Send(new KameHitRequest { targets = targets.ToArray(), direction = direction, power = power });
        }
    }

    private static void OnServerFire(NetworkConnectionToClient conn, KameFireMsg msg)
    {
        if (conn.identity == null)
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastFire.TryGetValue(conn.connectionId, out double last) && now - last < 0.5)
        {
            return;
        }
        lastFire[conn.connectionId] = now;
        msg.playerNetId = conn.identity.netId;
        msg.length = Mathf.Clamp(msg.length, 0f, KamehamehaItem.MaxRange.Value + 5f);
        msg.radius = Mathf.Clamp(msg.radius, 0.1f, 4f);
        msg.direction = msg.direction.sqrMagnitude > 0.001f ? msg.direction.normalized : conn.identity.transform.forward;
        NetworkServer.SendToAll(msg);

        // Golf balls in the beam get blasted along it (the host simulates balls).
        LayerSettings layers = GameManager.LayerSettings;
        int count = Physics.SphereCastNonAlloc(msg.origin, msg.radius, msg.direction, ballHits, msg.length, layers.BallMask | layers.DynamicBallMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Rigidbody body = ballHits[i].rigidbody;
            if (body != null && !body.isKinematic)
            {
                body.AddForce((msg.direction + Vector3.up * 0.3f) * Mathf.Min(msg.power, MaxPower), ForceMode.VelocityChange);
            }
        }
    }

    private static void OnServerHit(NetworkConnectionToClient conn, KameHitRequest msg)
    {
        if (conn.identity == null || msg.targets == null)
        {
            return;
        }
        foreach (uint target in msg.targets)
        {
            if (NetworkServer.spawned.TryGetValue(target, out NetworkIdentity identity) && identity != null
                && (identity.transform.position - conn.identity.transform.position).sqrMagnitude < Mathf.Pow(KamehamehaItem.MaxRange.Value + 10f, 2f))
            {
                NetworkServer.SendToAll(new KameHitMsg
                {
                    shooterNetId = conn.identity.netId,
                    targetNetId = target,
                    direction = msg.direction.normalized,
                    power = Mathf.Clamp(msg.power, 0f, MaxPower),
                });
            }
        }
    }

    private static void OnClientHit(KameHitMsg msg)
    {
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null || local.netId != msg.targetNetId || local.Movement == null)
        {
            return;
        }
        PlayerInfo shooter = NetworkClient.spawned.TryGetValue(msg.shooterNetId, out NetworkIdentity s) && s != null ? s.GetComponent<PlayerInfo>() : null;
        Vector3 launch = msg.direction * msg.power + Vector3.up * (msg.power * 0.35f);
        Vector3 localOrigin = local.transform.InverseTransformPoint(local.transform.position - msg.direction * 3f);
        bool knockedOut = local.Movement.TryKnockOut(shooter, KnockoutType.Rocket, isLegSweep: false, localOrigin, 0f, launch,
            ElectromagnetShieldHitBlockType.FullyBlocked, ItemUseId.Invalid, fromSpecialState: false, canFallbackToUnground: true, out _, out _);
        if (knockedOut)
        {
            local.Rigidbody.AddForce(launch, ForceMode.VelocityChange);
        }
    }

    private static bool IsLocal(uint netId) => GameManager.LocalPlayerInfo != null && GameManager.LocalPlayerInfo.netId == netId;
}

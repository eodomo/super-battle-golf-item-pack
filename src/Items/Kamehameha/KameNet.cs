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

/// <summary>
/// The host relays the charge and the beam, and pushes golf balls along the beam (it simulates balls). Whoever the beam
/// hits is handled by the game's own railgun hit, sent from the shooter's client (see <see cref="KamehamehaItem"/>).
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
    }

    public static void ResetServer() => lastFire.Clear();

    public static void SendCharging(bool charging)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new KameChargeMsg { charging = charging });
        }
    }

    public static void SendFire(Vector3 origin, Vector3 direction, float length, float radius, float power)
    {
        if (!NetworkClient.active)
        {
            return;
        }
        NetworkClient.Send(new KameFireMsg { origin = origin, direction = direction, length = length, radius = radius, power = power });
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

        // The beam can go through portals: every piece of it breaks dirt and blasts golf balls along it (the host simulates balls).
        LayerSettings layers = GameManager.LayerSettings;
        foreach (PortalPaths.Segment segment in PortalPaths.Trace(msg.origin, msg.direction, msg.length, 0, out _))
        {
            DirtWorld.ServerBreakInCapsule(segment.origin, segment.End, msg.radius);
            int count = Physics.SphereCastNonAlloc(segment.origin, msg.radius, segment.direction, ballHits, segment.length, layers.BallMask | layers.DynamicBallMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = ballHits[i].rigidbody;
                if (body != null && !body.isKinematic)
                {
                    body.AddForce((segment.direction + Vector3.up * 0.3f) * Mathf.Min(msg.power, MaxPower), ForceMode.VelocityChange);
                }
            }
        }
    }

    private static bool IsLocal(uint netId) => GameManager.LocalPlayerInfo != null && GameManager.LocalPlayerInfo.netId == netId;
}

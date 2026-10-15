using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct SaberCutRequest : NetworkMessage
{
    public uint targetNetId;
    public Vector3 point;
    public Vector3 normal;
    public byte color;
}

public struct SaberCut : NetworkMessage
{
    public uint targetNetId;
    public Vector3 point;
    public Vector3 normal;
    public byte color;
}

/// <summary>
/// Cuts go through the host: the swinger asks, the host checks it's plausible, tells everyone (who all show the
/// pieces), and for golf carts throws the passengers out knocked out and destroys the cart. Knocking out a player
/// who was hit goes through the game's own item hits (see <see cref="LightsaberItem"/>), so shields, teams and
/// the rest work as usual; the pieces only show if that knockout actually happens.
/// </summary>
internal static class SaberNet
{
    private const double MinCutInterval = 0.15;
    private const float MaxCutDistance = 7f;

    private static readonly Dictionary<(int connection, uint target), double> lastCut = new();

    public static void InitSerializers()
    {
        Writer<SaberCutRequest>.write = (w, m) => { w.WriteUInt(m.targetNetId); w.WriteVector3(m.point); w.WriteVector3(m.normal); w.WriteByte(m.color); };
        Reader<SaberCutRequest>.read = r => new SaberCutRequest { targetNetId = r.ReadUInt(), point = r.ReadVector3(), normal = r.ReadVector3(), color = r.ReadByte() };
        Writer<SaberCut>.write = (w, m) => { w.WriteUInt(m.targetNetId); w.WriteVector3(m.point); w.WriteVector3(m.normal); w.WriteByte(m.color); };
        Reader<SaberCut>.read = r => new SaberCut { targetNetId = r.ReadUInt(), point = r.ReadVector3(), normal = r.ReadVector3(), color = r.ReadByte() };
    }

    public static void RegisterServerHandlers()
    {
        lastCut.Clear();
        NetworkServer.RegisterHandler<SaberCutRequest>(OnServerCutRequest);
    }

    public static void RegisterClientHandlers() => NetworkClient.ReplaceHandler<SaberCut>(OnClientCut);

    public static void ResetServer() => lastCut.Clear();

    public static void RequestCut(uint targetNetId, Vector3 point, Vector3 normal, byte color)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new SaberCutRequest { targetNetId = targetNetId, point = point, normal = normal, color = color });
        }
    }

    private static void OnServerCutRequest(NetworkConnectionToClient conn, SaberCutRequest msg)
    {
        if (conn.identity == null || msg.color > 2 || !conn.identity.TryGetComponent(out PlayerInfo attacker))
        {
            return;
        }
        // One cut per swinger and target per swing.
        double now = NetworkTime.time;
        if (lastCut.TryGetValue((conn.connectionId, msg.targetNetId), out double last) && now - last < MinCutInterval)
        {
            return;
        }
        lastCut[(conn.connectionId, msg.targetNetId)] = now;
        if (!NetworkServer.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity target) || target == null
            || !target.TryGetComponent(out Entity entity)
            || (target.transform.position - attacker.transform.position).sqrMagnitude > MaxCutDistance * MaxCutDistance)
        {
            return;
        }
        Vector3 normal = msg.normal.sqrMagnitude > 0.0001f ? msg.normal.normalized : Vector3.up;
        var cut = new SaberCut { targetNetId = msg.targetNetId, point = msg.point, normal = normal, color = msg.color };

        if (entity.IsPlayer)
        {
            NetworkServer.SendToAll(cut);
        }
        else if (entity.IsGolfCart && LightsaberItem.CutCarts.Value)
        {
            ServerCutCart(entity, attacker, cut);
        }
    }

    private static void ServerCutCart(Entity cart, PlayerInfo attacker, SaberCut cut)
    {
        var passengers = new List<PlayerInfo>();
        foreach (PlayerInfo passenger in cart.AsGolfCart.passengers)
        {
            if (passenger != null)
            {
                passengers.Add(passenger);
            }
        }
        // Everyone shows the cart in pieces before the cart's destruction reaches them (same channel, in order).
        // The host's own view handles it right now: its copy of the cart is gone as soon as it's destroyed below.
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection != NetworkServer.localConnection)
            {
                connection.Send(cut);
            }
        }
        if (NetworkClient.active)
        {
            OnClientCut(cut);
        }
        foreach (PlayerInfo passenger in passengers)
        {
            passenger.ExitGolfCart(GolfCartExitType.Knockout);
            Hittable hittable = passenger.AsEntity.AsHittable;
            Vector3 direction = Vector3.ProjectOnPlane(passenger.transform.position - attacker.transform.position, Vector3.up).normalized;
            hittable.HitWithItem(ItemType.DuelingPistol, attacker.Inventory.IncrementAndGetCurrentItemUseId(ItemType.DuelingPistol),
                hittable.transform.InverseTransformPoint(cut.point), direction == Vector3.zero ? Vector3.up : direction,
                hittable.transform.InverseTransformPoint(attacker.transform.position), hittable.ItemSettings.DuelingPistolMinKnockbackDistance,
                attacker.Inventory, isReflected: false, isInSpecialState: false, canHitWithNoUser: true, hitTimestamp: NetworkTime.time);
            NetworkServer.SendToAll(new SaberCut { targetNetId = passenger.netId, point = cut.point, normal = Vector3.up, color = cut.color });
        }
        cart.DestroyEntity();
    }

    private static void OnClientCut(SaberCut msg)
    {
        if (!NetworkClient.spawned.TryGetValue(msg.targetNetId, out NetworkIdentity target) || target == null
            || !target.TryGetComponent(out Entity entity))
        {
            return;
        }
        Color color = LightsaberItem.BladeColors[Mathf.Min(msg.color, (byte)2)];
        try
        {
            if (entity.IsPlayer)
            {
                SaberSlicer.QueuePlayerCut(entity.PlayerInfo, msg.point, msg.normal, color);
            }
            else if (entity.IsGolfCart)
            {
                SaberSlicer.SliceCart(entity, msg.point, msg.normal, color);
            }
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogError($"Lightsaber cut effect failed: {e}");
        }
    }
}

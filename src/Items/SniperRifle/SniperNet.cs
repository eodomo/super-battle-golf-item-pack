using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct SniperShotRequest : NetworkMessage
{
    public Vector3 origin;
    public Vector3 end;
}

public struct SniperShot : NetworkMessage
{
    public uint shooterNetId;
    public Vector3 origin;
    public Vector3 end;
}

public struct SniperReload : NetworkMessage
{
    public uint shooterNetId;
}

/// <summary>
/// Shot effects for everyone: the sound (heard across the map) and the lingering vapor trail. Hits
/// themselves go through the game's own item-hit commands; these messages are only for show.
/// </summary>
internal static class SniperNet
{
    private const double MinShotInterval = 0.25;
    private const float MaxOriginDistance = 6f;
    private static readonly Dictionary<int, double> lastShot = new();

    public static void InitSerializers()
    {
        Writer<SniperShotRequest>.write = (w, m) => { w.WriteVector3(m.origin); w.WriteVector3(m.end); };
        Reader<SniperShotRequest>.read = r => new SniperShotRequest { origin = r.ReadVector3(), end = r.ReadVector3() };
        Writer<SniperShot>.write = (w, m) => { w.WriteUInt(m.shooterNetId); w.WriteVector3(m.origin); w.WriteVector3(m.end); };
        Reader<SniperShot>.read = r => new SniperShot { shooterNetId = r.ReadUInt(), origin = r.ReadVector3(), end = r.ReadVector3() };
        Writer<SniperReload>.write = (w, m) => w.WriteUInt(m.shooterNetId);
        Reader<SniperReload>.read = r => new SniperReload { shooterNetId = r.ReadUInt() };
    }

    public static void RegisterServerHandlers()
    {
        lastShot.Clear();
        NetworkServer.RegisterHandler<SniperShotRequest>(OnServerShot);
        NetworkServer.RegisterHandler<SniperReload>(OnServerReload);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<SniperShot>(msg =>
        {
            if (!IsLocal(msg.shooterNetId))
            {
                SniperEffects.PlayShot(msg.origin, msg.end);
            }
        });
        NetworkClient.ReplaceHandler<SniperReload>(msg =>
        {
            if (!IsLocal(msg.shooterNetId) && NetworkClient.spawned.TryGetValue(msg.shooterNetId, out NetworkIdentity shooter) && shooter != null)
            {
                SniperEffects.PlayReload(shooter.transform.position);
            }
        });
    }

    public static void ResetServer() => lastShot.Clear();

    /// <summary>Called by the shooter, who has already played the effects locally.</summary>
    public static void SendShot(Vector3 origin, Vector3 end)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new SniperShotRequest { origin = origin, end = end });
        }
    }

    public static void SendReload()
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new SniperReload());
        }
    }

    private static void OnServerShot(NetworkConnectionToClient conn, SniperShotRequest msg)
    {
        if (conn.identity == null)
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastShot.TryGetValue(conn.connectionId, out double last) && now - last < MinShotInterval)
        {
            return;
        }
        lastShot[conn.connectionId] = now;
        if ((conn.identity.transform.position - msg.origin).sqrMagnitude > MaxOriginDistance * MaxOriginDistance)
        {
            return;
        }
        Vector3 end = msg.origin + Vector3.ClampMagnitude(msg.end - msg.origin, SniperRifleItem.MaxRange.Value + 10f);
        NetworkServer.SendToAll(new SniperShot { shooterNetId = conn.identity.netId, origin = msg.origin, end = end });
    }

    private static void OnServerReload(NetworkConnectionToClient conn, SniperReload msg)
    {
        if (conn.identity != null)
        {
            NetworkServer.SendToAll(new SniperReload { shooterNetId = conn.identity.netId });
        }
    }

    private static bool IsLocal(uint netId) => GameManager.LocalPlayerInfo != null && GameManager.LocalPlayerInfo.netId == netId;
}

/// <summary>The sniper's sound and trail, played on each client.</summary>
internal static class SniperEffects
{
    private static readonly Color TrailColor = new(0.85f, 0.92f, 1f, 0.55f);

    public static void PlayShot(Vector3 origin, Vector3 end)
    {
        float volume = ItemAudio.Attenuate(origin, SniperRifleItem.SoundVolume.Value, 40f, 600f);
        ItemAudio.Play(SniperSounds.Fire, ItemAudio.SfxBus, Mathf.Max(volume, SniperRifleItem.SoundVolume.Value * 0.15f));
        // Halo's sniper leaves a vapor trail that hangs in the air and spreads out.
        Beams.Show(origin, end, TrailColor, 0.04f, 0.22f, SniperRifleItem.TrailSeconds.Value);
        Beams.Show(origin, end, new Color(1f, 1f, 1f, 0.9f), 0.025f, 0.01f, 0.12f); // bright core flash
    }

    public static void PlayReload(Vector3 position)
    {
        ItemAudio.Play(SniperSounds.Reload, ItemAudio.SfxBus, ItemAudio.Attenuate(position, SniperRifleItem.SoundVolume.Value, 5f, 40f));
    }
}

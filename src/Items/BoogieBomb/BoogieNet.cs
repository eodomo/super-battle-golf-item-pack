using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct BoogieThrowRequest : NetworkMessage
{
    public Vector3 origin;
    public Vector3 velocity;
}

public struct BoogieThrown : NetworkMessage
{
    public int id;
    public uint ownerNetId;
    public Vector3 origin;
    public Vector3 velocity;
}

/// <summary>
/// The host only hands out ids and relays throws. Every client then simulates the same bounce path against the
/// level, so the bomb goes off in the same spot everywhere, and each client checks whether its own player got
/// caught (players own their movement), like the banana peel.
/// </summary>
internal static class BoogieNet
{
    private static int nextId;
    private static readonly Dictionary<int, double> lastThrow = new();

    public static void InitSerializers()
    {
        Writer<BoogieThrowRequest>.write = (w, m) => { w.WriteVector3(m.origin); w.WriteVector3(m.velocity); };
        Reader<BoogieThrowRequest>.read = r => new BoogieThrowRequest { origin = r.ReadVector3(), velocity = r.ReadVector3() };
        Writer<BoogieThrown>.write = (w, m) => { w.WriteInt(m.id); w.WriteUInt(m.ownerNetId); w.WriteVector3(m.origin); w.WriteVector3(m.velocity); };
        Reader<BoogieThrown>.read = r => new BoogieThrown { id = r.ReadInt(), ownerNetId = r.ReadUInt(), origin = r.ReadVector3(), velocity = r.ReadVector3() };
    }

    public static void RegisterServerHandlers()
    {
        lastThrow.Clear();
        NetworkServer.RegisterHandler<BoogieThrowRequest>(OnServerThrow);
    }

    public static void RegisterClientHandlers() =>
        NetworkClient.ReplaceHandler<BoogieThrown>(msg => BoogieWorld.Spawn(msg.id, msg.ownerNetId, msg.origin, msg.velocity));

    public static void ResetServer() => lastThrow.Clear();

    public static void RequestThrow(Vector3 origin, Vector3 velocity)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new BoogieThrowRequest { origin = origin, velocity = velocity });
        }
    }

    private static void OnServerThrow(NetworkConnectionToClient conn, BoogieThrowRequest msg)
    {
        if (conn.identity == null)
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastThrow.TryGetValue(conn.connectionId, out double last) && now - last < 0.5)
        {
            return;
        }
        lastThrow[conn.connectionId] = now;
        Vector3 origin = conn.identity.transform.position + Vector3.ClampMagnitude(msg.origin - conn.identity.transform.position, 3f);
        NetworkServer.SendToAll(new BoogieThrown
        {
            id = ++nextId,
            ownerNetId = conn.identity.netId,
            origin = origin,
            velocity = Vector3.ClampMagnitude(msg.velocity, 55f),
        });
    }
}

/// <summary>
/// Makes the local player do their victory dance for a while: movement and item controls off (so moving can't
/// cancel it) but the camera stays free, and the dance restarted if something interrupts it (e.g. getting knocked
/// out). Others see it through the game's own emote syncing.
/// </summary>
internal static class BoogieDance
{
    private static float danceUntil;
    private static bool dancing;
    private static float nextRetry;

    public static bool IsDancing => dancing;

    public static void Start(float seconds)
    {
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (player == null)
        {
            return;
        }
        danceUntil = Mathf.Max(danceUntil, Time.time + seconds);
        if (!dancing)
        {
            dancing = true;
            SetGameplayControls(false);
            player.Inventory.CancelItemUse();
            StartEmote(player);
        }
    }

    public static void Tick()
    {
        if (!dancing)
        {
            return;
        }
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (player == null || Time.time >= danceUntil)
        {
            Stop();
            return;
        }
        // The game re-enables its controls whenever its input mode changes (closing a menu...): keep them off.
        SetGameplayControls(false);
        // Interrupted (knocked out, landed after a launch...)? Back to dancing as soon as the game allows it.
        if (player.EmoteBeingPlayed != Emote.VictoryDance)
        {
            if (Time.time >= nextRetry)
            {
                nextRetry = Time.time + 0.25f;
                player.SetEmoteToPlay(Emote.VictoryDance);
            }
        }
        else if (player.Cosmetics.victoryDance == VictoryDance.None)
        {
            player.AnimatorIo.SetVictoryDance(FallbackDance); // no dance equipped: borrow one
        }
    }

    public static void Stop()
    {
        if (!dancing)
        {
            return;
        }
        dancing = false;
        if (!MeetingWorld.Frozen)
        {
            SetGameplayControls(true);
        }
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (player != null && player.EmoteBeingPlayed == Emote.VictoryDance)
        {
            player.CancelEmote(canHideEmoteMenu: false);
        }
    }

    /// <summary>
    /// Movement, jumping, swinging, using and switching items. The camera controls are left alone. Only touched in
    /// normal play, so menus, chat and the item menu keep working.
    /// </summary>
    private static void SetGameplayControls(bool enabled)
    {
        if (InputManager.CurrentMode != InputMode.Regular || InputManager.Controls == null)
        {
            return;
        }
        if (enabled)
        {
            InputManager.Controls.Gameplay.Enable();
            InputManager.Controls.Hotkeys.Enable();
        }
        else
        {
            InputManager.Controls.Gameplay.Disable();
            InputManager.Controls.Hotkeys.Disable();
        }
    }

    private static void StartEmote(PlayerInfo player) => player.SetEmoteToPlay(Emote.VictoryDance);

    /// <summary>The first dance the game has, for players with none equipped.</summary>
    private static VictoryDance FallbackDance
    {
        get
        {
            foreach (VictoryDance dance in (VictoryDance[])System.Enum.GetValues(typeof(VictoryDance)))
            {
                if (dance != VictoryDance.None)
                {
                    return dance;
                }
            }
            return VictoryDance.None;
        }
    }
}

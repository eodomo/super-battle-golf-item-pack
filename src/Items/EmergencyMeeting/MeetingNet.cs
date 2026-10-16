using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

public struct MeetingRequest : NetworkMessage
{
}

public struct MeetingCalled : NetworkMessage
{
    public uint callerNetId;
    public Vector3 center;
    public float seatYaw;     // direction from the table to the caller's seat
    public float seatRadius;
    public float freezeSeconds;
    public uint[] seats;      // player netIds, the caller first, clockwise from there
}

/// <summary>
/// The host seats every player around a table in front of the caller and tells everyone. Each client then shows
/// the meeting screen and moves its own player to its seat (players own their movement).
/// </summary>
internal static class MeetingNet
{
    private const double MinInterval = 8.0;
    private static double lastMeeting = double.MinValue;

    public static void InitSerializers()
    {
        Writer<MeetingRequest>.write = (_, _) => { };
        Reader<MeetingRequest>.read = _ => new MeetingRequest();
        Writer<MeetingCalled>.write = (w, m) =>
        {
            w.WriteUInt(m.callerNetId);
            w.WriteVector3(m.center);
            w.WriteFloat(m.seatYaw);
            w.WriteFloat(m.seatRadius);
            w.WriteFloat(m.freezeSeconds);
            w.WriteInt(m.seats.Length);
            foreach (uint netId in m.seats)
            {
                w.WriteUInt(netId);
            }
        };
        Reader<MeetingCalled>.read = r =>
        {
            var m = new MeetingCalled
            {
                callerNetId = r.ReadUInt(), center = r.ReadVector3(), seatYaw = r.ReadFloat(), seatRadius = r.ReadFloat(), freezeSeconds = r.ReadFloat(),
            };
            int count = Mathf.Clamp(r.ReadInt(), 0, 64);
            m.seats = new uint[count];
            for (int i = 0; i < count; i++)
            {
                m.seats[i] = r.ReadUInt();
            }
            return m;
        };
    }

    public static void RegisterServerHandlers()
    {
        lastMeeting = double.MinValue;
        NetworkServer.RegisterHandler<MeetingRequest>(OnServerRequest);
    }

    public static void RegisterClientHandlers() => NetworkClient.ReplaceHandler<MeetingCalled>(MeetingWorld.OnCalled);

    public static void ResetServer() => lastMeeting = double.MinValue;

    public static void RequestMeeting()
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new MeetingRequest());
        }
    }

    private static void OnServerRequest(NetworkConnectionToClient conn, MeetingRequest msg)
    {
        if (conn.identity == null || !conn.identity.TryGetComponent(out PlayerInfo caller))
        {
            return;
        }
        double now = NetworkTime.time;
        if (now - lastMeeting < MinInterval)
        {
            return;
        }
        lastMeeting = now;

        var seats = new List<uint> { caller.netId };
        foreach (NetworkConnectionToClient connection in NetworkServer.connections.Values)
        {
            if (connection?.identity != null && connection.identity.TryGetComponent(out PlayerInfo player) && player != caller)
            {
                seats.Add(player.netId);
            }
        }
        // Room for everyone around the table (which is 1.15 m across the top).
        float radius = Mathf.Max(1.8f, seats.Count * 0.75f / (2f * Mathf.PI));
        Vector3 forward = Vector3.ProjectOnPlane(caller.transform.forward, Vector3.up);
        forward = forward.sqrMagnitude > 0.001f ? forward.normalized : Vector3.forward;
        Vector3 center = caller.transform.position + forward * radius;
        Vector3 toCaller = -forward;
        NetworkServer.SendToAll(new MeetingCalled
        {
            callerNetId = caller.netId,
            center = center,
            seatYaw = Mathf.Atan2(toCaller.x, toCaller.z) * Mathf.Rad2Deg,
            seatRadius = radius,
            freezeSeconds = Mathf.Clamp(MeetingItem.FreezeSeconds.Value, 0f, 15f),
            seats = seats.ToArray(),
        });
    }
}

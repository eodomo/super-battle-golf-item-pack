using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Thrown lightsabers (Battlefront style): the saber spins away from the thrower's hand along their aim, and if
/// it doesn't hit anyone it comes back like a boomerang (after its full range, or sooner when it hits a wall).
/// Every client flies everyone's sabers the same way; only the thrower's own client checks for hits, and tells
/// the others when its saber turns back early or is gone.
/// </summary>
internal static class SaberThrows
{
    private const float Speed = 30f;
    private const float ReturnSpeed = 34f;
    private const float SpinDegreesPerSecond = 1080f;
    private const float CatchDistance = 0.9f;
    private const float MaxFlightSeconds = 5f;
    private const byte Thrown = 0, Returning = 1, Gone = 2;

    private class Flight
    {
        public uint netId;
        public PlayerInfo thrower;
        public byte color;
        public GameObject root;
        public Transform spinner;
        public Vector3 position;
        public Vector3 direction;
        public Vector3 heading;   // which way it's moving on the way back
        public float traveled;
        public bool returning;
        public float startTime;
        public float spin;
    }

    private static readonly Dictionary<uint, Flight> flights = new();
    private static readonly List<uint> ended = new();
    private static readonly Collider[] overlap = new Collider[32];
    private static PlayerInventory localInventory;

    /// <summary>Whether this player's saber is out of their hand.</summary>
    public static bool IsThrown(uint netId) => flights.ContainsKey(netId);

    /// <summary>Whether the local player's saber is out.</summary>
    public static bool LocalActive => GameManager.LocalPlayerInfo != null && flights.ContainsKey(GameManager.LocalPlayerInfo.netId);

    public static void LocalThrow(PlayerInventory inventory, Vector3 origin, Vector3 direction, byte color)
    {
        localInventory = inventory;
        Start(inventory.PlayerInfo, origin, direction, color);
        SaberNet.SendThrowState(Thrown, origin, direction, color);
    }

    public static void OnState(SaberThrowState msg)
    {
        if (GameManager.LocalPlayerInfo != null && GameManager.LocalPlayerInfo.netId == msg.throwerNetId)
        {
            return; // our own saber flies here already
        }
        switch (msg.state)
        {
            case Thrown:
                if (NetworkClient.spawned.TryGetValue(msg.throwerNetId, out NetworkIdentity identity) && identity != null
                    && identity.TryGetComponent(out PlayerInfo thrower))
                {
                    Start(thrower, msg.origin, msg.direction, msg.color);
                }
                break;
            case Returning:
                if (flights.TryGetValue(msg.throwerNetId, out Flight flight))
                {
                    flight.position = msg.origin;
                    flight.returning = true;
                }
                break;
            default:
                End(msg.throwerNetId);
                break;
        }
    }

    private static void Start(PlayerInfo thrower, Vector3 origin, Vector3 direction, byte color)
    {
        End(thrower.netId);
        var root = new GameObject("ThrownLightsaber");
        root.SetActive(false); // the saber's parts are wired up before it wakes, like a held one
        var spinner = new GameObject("Spin").transform;
        spinner.SetParent(root.transform, false);
        // Lay the saber flat (its blade grows along +Y) and spin it around its middle.
        var flat = new GameObject("Flat").transform;
        flat.SetParent(spinner, false);
        flat.localRotation = Quaternion.Euler(90f, 0f, 0f);
        if (ItemCatalog.TryGet((ItemType)(LightsaberItem.FirstId + color), out CustomItem item))
        {
            float scale = item.HeldScale.Value;
            flat.localPosition = new Vector3(0f, 0f, -LightsaberItem.BladeLength.Value * 0.45f * scale);
            item.CreateHeldModel(flat, scale);
        }
        root.transform.position = origin;
        root.SetActive(true);
        flights[thrower.netId] = new Flight
        {
            netId = thrower.netId, thrower = thrower, color = color, root = root, spinner = spinner,
            position = origin, direction = direction.normalized, startTime = Time.time,
        };
    }

    private static void End(uint netId)
    {
        if (flights.TryGetValue(netId, out Flight flight))
        {
            if (flight.root != null)
            {
                Object.Destroy(flight.root);
            }
            flights.Remove(netId);
        }
    }

    public static void ClearAll()
    {
        foreach (Flight flight in flights.Values)
        {
            if (flight.root != null)
            {
                Object.Destroy(flight.root);
            }
        }
        flights.Clear();
        localInventory = null;
    }

    public static void Tick()
    {
        if (flights.Count == 0)
        {
            return;
        }
        float dt = Time.deltaTime;
        ended.Clear();
        foreach (Flight flight in flights.Values)
        {
            if (flight.thrower == null || flight.root == null)
            {
                ended.Add(flight.netId);
                continue;
            }
            bool local = flight.thrower == GameManager.LocalPlayerInfo;
            if (Time.time - flight.startTime > MaxFlightSeconds + 1f)
            {
                // Never left out in the air (or stuck out of your hand) whatever happened to it.
                ended.Add(flight.netId);
                if (local)
                {
                    SaberNet.SendThrowState(Gone, flight.position, flight.direction, flight.color);
                }
                continue;
            }
            if (!flight.returning)
            {
                float step = Speed * dt;
                // The wall a portal sits on doesn't stop a saber that's flying into the portal.
                bool portalAhead = PortalShots.TryFindEntry(flight.position, flight.direction, step + 0.4f, null, out _, out float toPortal);
                if (local && Physics.Raycast(flight.position, flight.direction, out RaycastHit wall, step + 0.3f, WorldMask.Solid, QueryTriggerInteraction.Ignore)
                    && !(portalAhead && wall.distance >= toPortal - 0.1f))
                {
                    // Bounced off a wall: head back from here.
                    flight.position = wall.point - flight.direction * 0.3f;
                    flight.returning = true;
                    SaberNet.SendThrowState(Returning, flight.position, flight.direction, flight.color);
                }
                else
                {
                    PortalPaths.Advance(ref flight.position, ref flight.direction, step);
                    flight.traveled += step;
                    flight.returning = flight.traveled >= LightsaberItem.ThrowRange.Value;
                }
            }
            else
            {
                Vector3 hand = SlingerWebs.HandOf(flight.thrower);
                float distance = (hand - flight.position).magnitude;
                if (distance < CatchDistance || Time.time - flight.startTime > MaxFlightSeconds)
                {
                    ended.Add(flight.netId);
                    if (local)
                    {
                        SaberNet.SendThrowState(Gone, flight.position, flight.direction, flight.color);
                    }
                    continue;
                }
                // Comes home through a portal when that's the shorter way.
                Vector3 aim = PortalPaths.Waypoint(flight.position, hand, out bool viaPortal);
                Vector3 toAim = aim - flight.position;
                float aimDistance = toAim.magnitude;
                if (aimDistance > 0.001f)
                {
                    flight.heading = toAim / aimDistance;
                    if (viaPortal)
                    {
                        // A little past the portal's surface, so it really goes in.
                        PortalPaths.Advance(ref flight.position, ref flight.heading, Mathf.Min(aimDistance + 0.05f, ReturnSpeed * dt));
                    }
                    else
                    {
                        flight.position += flight.heading * Mathf.Min(distance, ReturnSpeed * dt);
                    }
                }
            }
            flight.spin += SpinDegreesPerSecond * dt;
            flight.root.transform.position = flight.position;
            flight.spinner.rotation = Quaternion.AngleAxis(flight.spin, Vector3.up) * Quaternion.AngleAxis(12f, Vector3.right);

            if (local && localInventory != null && CheckHit(flight))
            {
                ended.Add(flight.netId);
                SaberNet.SendThrowState(Gone, flight.position, flight.direction, flight.color);
            }
        }
        foreach (uint netId in ended)
        {
            End(netId);
        }
    }

    /// <summary>The thrower's client: the first player or cart the spinning blade touches is hit.</summary>
    private static bool CheckHit(Flight flight)
    {
        LayerSettings layers = GameManager.LayerSettings;
        int mask = layers.PlayersMask | (LightsaberItem.CutCarts.Value || !NetworkServer.active ? layers.GolfCartsMask : 0);
        float radius = LightsaberItem.BladeLength.Value * 0.6f;
        int count = Physics.OverlapSphereNonAlloc(flight.position, radius, overlap, mask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Hittable hittable = overlap[i].GetComponentInParent<Hittable>();
            if (hittable == null || hittable.AsEntity == flight.thrower.AsEntity)
            {
                continue;
            }
            Entity entity = hittable.AsEntity;
            if ((!entity.IsPlayer && !entity.IsGolfCart) || (entity.IsPlayer && entity.PlayerInfo.ActiveGolfCartSeat.IsValid()))
            {
                continue;
            }
            if (!hittable.TryGetClosestPointOnAllActiveColliders(flight.position, out Vector3 point, out _))
            {
                point = overlap[i].ClosestPoint(flight.position);
            }
            Vector3 travel = flight.returning ? flight.heading : flight.direction;
            Vector3 direction = Vector3.ProjectOnPlane(travel, Vector3.up);
            direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
            LightsaberItem.ThrownHit(localInventory, hittable, point, direction, flight.position - direction, flight.color);
            return true;
        }
        return false;
    }
}

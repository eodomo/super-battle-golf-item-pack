using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Moves things through portals, keeping their momentum: position, rotation, velocity and spin are all
/// carried from the entry portal's frame to the exit portal's frame.
///
/// Runs every physics step, before the simulation, and catches anything that would reach a portal's
/// surface during the coming step. Walls behind portals stay solid, so things must be teleported before
/// they touch them.
///
/// Who moves what: each client moves its own player (players own their movement). The host moves
/// everything else (balls, carts, thrown items) and clients copy that locally too, so their predicted
/// physics doesn't bounce off the wall before the host's correction arrives.
/// </summary>
internal static class PortalTravel
{
    private const float Cooldown = 0.12f;
    private const float ExitClearance = 0.06f;
    private const float TriggerSlack = 0.06f;
    private const float ScanDepth = 4f;

    private static readonly Collider[] overlap = new Collider[64];
    private static readonly HashSet<Rigidbody> seen = new();
    private static readonly Dictionary<Rigidbody, float> recent = new();
    private static readonly List<Rigidbody> expired = new();
    private static int travellerMask = -1;

    /// <summary>Called when portals move, so nothing stays locked out of a freshly placed portal.</summary>
    public static void ForgetRecent() => recent.Clear();

    public static void FixedTick()
    {
        if (Portals.All.Count == 0 || GameManager.LayerSettings == null)
        {
            return;
        }
        PruneRecent();
        seen.Clear();
        int mask = GetTravellerMask();
        foreach (Portal portal in Portals.All)
        {
            if (portal.Transform == null || !portal.IsLinked || portal.Transform.localScale.x < 0.99f)
            {
                continue;
            }
            Transform t = portal.Transform;
            Vector3 center = t.position + t.forward * (ScanDepth * 0.5f - 0.5f);
            var halfExtents = new Vector3(portal.HalfWidth + 1f, portal.HalfHeight + 1f, ScanDepth * 0.5f + 0.5f);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlap, t.rotation, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = overlap[i].attachedRigidbody;
                if (body == null || body.isKinematic || !seen.Add(body) || recent.ContainsKey(body))
                {
                    continue;
                }
                if (!body.TryGetComponentInParent(out Entity entity, includeInactive: false))
                {
                    continue;
                }
                TryTravel(portal, entity, body);
            }
        }
    }

    private static void TryTravel(Portal portal, Entity entity, Rigidbody body)
    {
        bool isPlayer = entity.IsPlayer;
        if (isPlayer)
        {
            PlayerInfo player = entity.PlayerInfo;
            if (!player.isLocalPlayer || player.ActiveGolfCartSeat.IsValid() || !player.Movement.IsVisible)
            {
                return; // remote players move themselves; seated players ride their cart through
            }
        }
        else if (!NetworkServer.active && !entity.IsSimulatingRigidbody())
        {
            return;
        }

        Transform t = portal.Transform;
        Vector3 n = t.forward;
        Bounds bounds = GetBounds(body);
        Vector3 center = bounds.center;
        float extent = SupportExtent(bounds.extents, n);
        Vector3 velocity = body.linearVelocity;
        float distance = Vector3.Dot(center - t.position, n) - extent; // gap between the object and the surface
        float approach = -Vector3.Dot(velocity, n);                     // speed toward the surface
        float step = Time.fixedDeltaTime;

        if (distance < -extent * 2f - 0.3f)
        {
            return; // already well behind the surface (e.g. inside the wall below a floor portal)
        }
        bool isFloor = n.y > 0.6f;
        bool reachesThisStep = distance <= Mathf.Max(TriggerSlack, approach * step * 1.5f) && approach > 0.05f;
        bool restingOnFloorPortal = isFloor && distance <= TriggerSlack;
        bool pushingIntoWall = isPlayer && !isFloor && distance <= TriggerSlack && Vector3.Dot(entity.transform.forward, n) < -0.3f
            && entity.PlayerInfo.Movement.rawMoveVector2d.sqrMagnitude > 0.1f;
        if (!reachesThisStep && !restingOnFloorPortal && !pushingIntoWall)
        {
            return;
        }

        // Where the object's center crosses the surface plane, in the portal's 2D space.
        float timeToSurface = approach > 0.05f ? Mathf.Max(0f, distance) / approach : 0f;
        Vector3 crossing = t.InverseTransformPoint(center + velocity * timeToSurface);
        float ex = crossing.x / portal.HalfWidth;
        float ey = crossing.y / portal.HalfHeight;
        if (ex * ex + ey * ey > 1f)
        {
            return;
        }
        Teleport(portal, entity, body, center, bounds, crossing, isPlayer);
    }

    private static void Teleport(Portal portal, Entity entity, Rigidbody body, Vector3 center, Bounds bounds, Vector3 crossingLocal, bool isPlayer)
    {
        Portal exit = portal.Partner;
        Quaternion travel = portal.TravelRotation;
        Vector3 exitNormal = exit.Transform.forward;

        // Keep the side-to-side offset (pulled in a bit so we don't come out half inside the exit's wall).
        var lateral = new Vector3(crossingLocal.x * 0.7f, crossingLocal.y * 0.7f, 0f);
        Vector3 exitPointOnSurface = exit.Transform.TransformPoint(Portals.Flip * lateral);

        Vector3 velocity = travel * body.linearVelocity;
        Vector3 angularVelocity = travel * body.angularVelocity;
        Vector3 pivotOffset = body.position - center;

        if (isPlayer)
        {
            TeleportPlayer(entity.PlayerInfo, body, exit, exitPointOnSurface, exitNormal, bounds, pivotOffset, travel, velocity);
        }
        else
        {
            float exitExtent = bounds.extents.magnitude; // rotation changes, so use the bounding sphere
            Vector3 exitCenter = exitPointOnSurface + exitNormal * (exitExtent + ExitClearance);
            Quaternion rotation = travel * body.rotation;
            Vector3 position = exitCenter + travel * pivotOffset;

            entity.InformWillTeleport();
            body.position = position;
            body.rotation = rotation;
            entity.transform.SetPositionAndRotation(position, rotation);
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
            entity.InformTeleported();
            RotateLocalCameraIfRiding(entity, travel);
        }
        recent[body] = Time.time;
    }

    private static void TeleportPlayer(PlayerInfo player, Rigidbody body, Portal exit, Vector3 exitPointOnSurface, Vector3 exitNormal,
        Bounds bounds, Vector3 pivotOffset, Quaternion travel, Vector3 velocity)
    {
        // Players stay upright; turn them to face the way they're now moving (or out of the exit).
        Vector3 facing = travel * player.transform.forward;
        facing.y = 0f;
        if (facing.sqrMagnitude < 0.01f)
        {
            facing = Vector3.ProjectOnPlane(exit.Transform.up, Vector3.up);
            if (facing.sqrMagnitude < 0.01f)
            {
                facing = Vector3.ProjectOnPlane(exitNormal, Vector3.up);
            }
        }
        Quaternion rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);

        float exitExtent = SupportExtent(bounds.extents, exitNormal);
        Vector3 exitCenter = exitPointOnSurface + exitNormal * (exitExtent + ExitClearance);
        Vector3 feet = exitCenter + pivotOffset; // upright before and after, so the offset is unchanged

        // Coming out of a wall portal low down: don't end up with our feet in the floor.
        if (Mathf.Abs(exitNormal.y) < 0.6f
            && Physics.Raycast(exitCenter, Vector3.down, out RaycastHit ground, bounds.extents.y + 0.5f,
                GameManager.LayerSettings.PlayerGroundableMask, QueryTriggerInteraction.Ignore)
            && ground.point.y > feet.y)
        {
            feet.y = ground.point.y + 0.02f;
        }

        float yawBefore = player.transform.eulerAngles.y;
        player.Movement.Teleport(feet, rotation, resetState: false);
        body.linearVelocity = velocity; // Teleport zeroes velocity; momentum is the whole point
        RotateLocalCamera(Mathf.DeltaAngle(yawBefore, rotation.eulerAngles.y));
    }

    private static void RotateLocalCameraIfRiding(Entity entity, Quaternion travel)
    {
        PlayerInfo local = GameManager.LocalPlayerInfo;
        if (local == null || !entity.IsGolfCart || !local.ActiveGolfCartSeat.IsValid())
        {
            return;
        }
        if (local.ActiveGolfCartSeat.golfCart != null && local.ActiveGolfCartSeat.golfCart.AsEntity == entity)
        {
            Vector3 forward = travel * Vector3.forward;
            RotateLocalCamera(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg);
        }
    }

    private static void RotateLocalCamera(float yawDelta)
    {
        if (CameraModuleController.CurrentModule is OrbitCameraModule orbit)
        {
            orbit.SetYaw(orbit.Yaw + yawDelta);
        }
    }

    private static Bounds GetBounds(Rigidbody body)
    {
        bool any = false;
        var bounds = new Bounds(body.worldCenterOfMass, Vector3.zero);
        foreach (Collider collider in body.GetComponentsInChildren<Collider>())
        {
            if (!collider.enabled || collider.isTrigger || collider.attachedRigidbody != body)
            {
                continue;
            }
            if (!any)
            {
                bounds = collider.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }
        return bounds;
    }

    /// <summary>Half-size of an axis-aligned box along a direction.</summary>
    private static float SupportExtent(Vector3 extents, Vector3 direction) =>
        Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y + Mathf.Abs(direction.z) * extents.z;

    private static void PruneRecent()
    {
        float now = Time.time;
        foreach (KeyValuePair<Rigidbody, float> entry in recent)
        {
            if (entry.Key == null || now - entry.Value > Cooldown)
            {
                expired.Add(entry.Key);
            }
        }
        foreach (Rigidbody body in expired)
        {
            recent.Remove(body);
        }
        expired.Clear();
    }

    private static int GetTravellerMask()
    {
        if (travellerMask == -1)
        {
            LayerSettings layers = GameManager.LayerSettings;
            travellerMask = layers.PlayersMask | layers.GolfCartsMask | layers.BallMask | layers.DynamicBallMask
                | (1 << layers.ItemsLayer) | (1 << layers.HittablesLayer) | (1 << layers.StationaryBallLayer) | (1 << layers.DynamicBallLayer);
        }
        return travellerMask;
    }
}

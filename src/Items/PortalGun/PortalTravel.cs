using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Moves things through portals the way Portal does: you can stand halfway in one, and you only come out of the
/// other one when your middle crosses the surface. Position, rotation, velocity and spin are carried from the entry
/// portal's frame to the exit portal's frame, so momentum is kept.
///
/// How: while something is in a portal's opening (touching the surface, its center inside the rim), collisions
/// between it and the wall behind the portal are switched off (Physics.IgnoreCollision, which the game's own
/// ground checks also honor), so it can move into the wall. When its center passes the surface it's moved to the
/// same spot relative to the exit, where the exit's wall is switched off for it until it's fully out. Wall portals
/// near the ground get an invisible floor ("sill") inside the wall, in case the wall and the floor are one collider
/// (terrain), so people walking in don't drop through the ground. <see cref="PortalClones"/> draws whatever is
/// sticking out of the other side.
///
/// Who moves what: each client moves its own player (players own their movement). The host moves everything else
/// (balls, carts, thrown items) and clients copy that locally too, so their predicted physics agrees.
/// </summary>
internal static class PortalTravel
{
    private const float TouchSlack = 0.08f;   // a surface this close counts as touching the portal
    private const float EdgeSlack = 1.15f;    // things already going through can drift a little past the rim
    private const float ScanBehind = 2.5f;
    private const float ScanFront = 4f;
    private const float ExitDepth = 0.01f;    // how far past the exit's surface a teleported center ends up
    private const float FloorNormalY = 0.6f;  // portals facing up at least this much are floor portals
    private const float MinFloorExitSpeed = 7f; // how fast a player pops out of a floor portal at least
    private const float SillReach = 1.2f;   // wall portals whose bottom is this close to the floor get a sill

    private sealed class Passage
    {
        public Rigidbody Body;
        public Entity Entity;
        public Portal Portal;
        public int Step;
        public readonly List<(Collider own, Collider wall)> Ignored = new();
    }

    private static readonly Collider[] overlap = new Collider[64];
    private static readonly List<Collider> ownColliders = new();
    private static readonly HashSet<Rigidbody> seen = new();
    private static readonly Dictionary<Rigidbody, Passage> passages = new();
    private static readonly List<Passage> scratch = new();
    private static readonly HashSet<Collider> sills = new();
    private static int travellerMask = -1;
    private static int step;

    // ---- portal lifetime ----

    /// <summary>A portal was placed or moved: find the wall it sits on.</summary>
    public static void OnPortalPlaced(Portal portal)
    {
        EndPassagesAt(portal);
        DestroySill(portal);
        portal.WallColliders.Clear();
        if (GameManager.LayerSettings == null)
        {
            return;
        }
        Transform t = portal.Transform;
        Vector3 n = t.forward;
        var halfExtents = new Vector3(portal.HalfWidth + 0.05f, portal.HalfHeight + 0.05f, 0.32f);
        int count = Physics.OverlapBoxNonAlloc(t.position - n * 0.3f, halfExtents, overlap, t.rotation, ~GetTravellerMask(), QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Collider c = overlap[i];
            if (c.isTrigger || sills.Contains(c) || (c.attachedRigidbody != null && !c.attachedRigidbody.isKinematic))
            {
                continue;
            }
            portal.WallColliders.Add(c);
        }
        if (Mathf.Abs(n.y) < 0.6f)
        {
            BuildSill(portal);
        }
    }

    public static void OnPortalRemoved(Portal portal)
    {
        EndPassagesAt(portal);
        DestroySill(portal);
        portal.WallColliders.Clear();
    }

    private static void BuildSill(Portal portal)
    {
        Transform t = portal.Transform;
        Vector3 flatNormal = Vector3.ProjectOnPlane(t.forward, Vector3.up).normalized;
        Vector3 bottom = t.position - t.up * portal.HalfHeight;
        Vector3 probe = bottom + flatNormal * 0.4f + Vector3.up * 0.6f;
        if (!Physics.Raycast(probe, Vector3.down, out RaycastHit floor, SillReach + 0.6f, GameManager.LayerSettings.PlayerGroundableMask, QueryTriggerInteraction.Ignore)
            || !portal.WallColliders.Contains(floor.collider))
        {
            return; // the floor isn't part of the wall, so it stays solid by itself
        }
        var go = new GameObject($"{t.name}_Sill") { layer = floor.collider.gameObject.layer };
        go.transform.SetPositionAndRotation(new Vector3(bottom.x, floor.point.y, bottom.z), Quaternion.LookRotation(flatNormal, Vector3.up));
        var box = go.AddComponent<BoxCollider>();
        box.center = new Vector3(0f, -0.25f, -0.6f);              // reaches 2 m into the wall and 0.8 m out of it
        box.size = new Vector3(portal.HalfWidth * 2f + 0.3f, 0.5f, 2.8f);
        box.enabled = false;                                       // only while something is going through
        if (floor.collider.sharedMaterial != null)
        {
            box.sharedMaterial = floor.collider.sharedMaterial;
        }
        portal.Sill = box;
        sills.Add(box);
    }

    private static void DestroySill(Portal portal)
    {
        if (portal.Sill != null)
        {
            sills.Remove(portal.Sill);
            Object.Destroy(portal.Sill.gameObject);
        }
        portal.Sill = null;
    }

    // ---- falling out of the world ----

    private static Vector3 lastExit;
    private static float lastExitTime = -100f;

    /// <summary>
    /// Safety nets for the local player coming out of a portal. Collisions with the wall are switched off while going
    /// through, so ending that while still sunk into the ground (terrain is one collider: wall and floor) would drop them
    /// through the world. <see cref="Unstick"/> lifts them onto the surface; <see cref="WorldBottomGuard"/> brings back
    /// anyone who has gone out the bottom anyway.
    /// </summary>
    private static void Unstick(PlayerInfo player)
    {
        Vector3 feet = player.transform.position;
        int mask = GameManager.LayerSettings.PlayerGroundableMask;
        if (!Physics.CheckCapsule(feet + Vector3.up * 0.35f, feet + Vector3.up * 1.4f, 0.3f, mask, QueryTriggerInteraction.Ignore))
        {
            return; // not inside anything
        }
        if (Physics.Raycast(feet + Vector3.up * 1.8f, Vector3.down, out RaycastHit surface, 2.4f, mask, QueryTriggerInteraction.Ignore)
            && surface.point.y > feet.y && surface.point.y - feet.y < 1.7f)
        {
            Vector3 velocity = player.Rigidbody.linearVelocity;
            Plugin.Log.LogInfo($"Portal: lifted {player.name} out of the ground, {surface.point.y - feet.y:0.00} m up.");
            player.Movement.Teleport(new Vector3(feet.x, surface.point.y + 0.05f, feet.z), player.transform.rotation, resetState: false);
            player.Rigidbody.linearVelocity = new Vector3(velocity.x, Mathf.Max(velocity.y, 0f), velocity.z);
        }
    }

    private static void WorldBottomGuard()
    {
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (player == null || Time.time - lastExitTime > 8f || !player.Movement.IsVisible || player.ActiveGolfCartSeat.IsValid())
        {
            return;
        }
        Vector3 position = player.transform.position;
        if (position.y < lastExit.y - 3f
            && !Physics.Raycast(position + Vector3.up, Vector3.down, 400f, GameManager.LayerSettings.PlayerGroundableMask, QueryTriggerInteraction.Ignore))
        {
            Plugin.Log.LogWarning($"Portal: {player.name} fell out of the world at {position}; back to {lastExit}.");
            player.Movement.Teleport(lastExit, player.transform.rotation, resetState: false);
            player.Rigidbody.linearVelocity = Vector3.zero;
        }
    }

    // ---- every physics step ----

    public static void FixedTick()
    {
        WorldBottomGuard();
        if (Portals.All.Count == 0 || GameManager.LayerSettings == null)
        {
            if (passages.Count > 0)
            {
                EndAll();
            }
            return;
        }
        step++;
        seen.Clear();

        // Things already going through stay with their portal.
        scratch.Clear();
        scratch.AddRange(passages.Values);
        foreach (Passage passage in scratch)
        {
            if (passage.Body == null || passage.Entity == null || passage.Portal.Transform == null)
            {
                End(passage);
                continue;
            }
            seen.Add(passage.Body);
            Evaluate(passage.Portal, passage.Body, passage.Entity, passage);
        }

        int mask = GetTravellerMask();
        foreach (Portal portal in Portals.All)
        {
            if (!IsOpen(portal) || !IsOpen(portal.Partner))
            {
                continue;
            }
            Transform t = portal.Transform;
            Vector3 center = t.position + t.forward * ((ScanFront - ScanBehind) * 0.5f);
            var halfExtents = new Vector3(portal.HalfWidth + 1f, portal.HalfHeight + 1f, (ScanFront + ScanBehind) * 0.5f);
            int count = Physics.OverlapBoxNonAlloc(center, halfExtents, overlap, t.rotation, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Rigidbody body = overlap[i].attachedRigidbody;
                if (body == null || body.isKinematic || !seen.Add(body)
                    || !body.TryGetComponentInParent(out Entity entity, includeInactive: false))
                {
                    continue;
                }
                if (entity.IsPlayer && entity.PlayerInfo.ActiveGolfCartSeat.IsValid())
                {
                    continue; // seated players ride their cart through
                }
                Evaluate(portal, body, entity, null);
            }
        }

        // Rockets and freeze bombs, in case their layer isn't one of the travellers'.
        PortalProjectiles.Live.RemoveWhere(body => body == null);
        foreach (Rigidbody body in PortalProjectiles.Live)
        {
            if (body.isKinematic || !seen.Add(body) || !body.TryGetComponent(out Entity entity))
            {
                continue;
            }
            foreach (Portal portal in Portals.All)
            {
                if (IsOpen(portal) && IsOpen(portal.Partner) && (body.position - portal.Transform.position).sqrMagnitude < 36f)
                {
                    Evaluate(portal, body, entity, null);
                    if (passages.ContainsKey(body))
                    {
                        break;
                    }
                }
            }
        }

        // Anything that stopped touching its portal gets its collisions back.
        scratch.Clear();
        scratch.AddRange(passages.Values);
        foreach (Passage passage in scratch)
        {
            if (passage.Step != step)
            {
                End(passage);
            }
        }
        foreach (Portal portal in Portals.All)
        {
            if (portal.Sill != null)
            {
                portal.Sill.enabled = HasPassage(portal);
            }
        }
    }

    private static void Evaluate(Portal portal, Rigidbody body, Entity entity, Passage passage)
    {
        if (!IsOpen(portal) || !IsOpen(portal.Partner))
        {
            return; // not confirmed this step: ends below
        }
        Transform t = portal.Transform;
        Vector3 n = t.forward;
        Bounds bounds = GetBounds(body);
        Vector3 center = bounds.center;
        float extent = SupportExtent(bounds.extents, n);
        Vector3 velocity = body.linearVelocity;
        float depth = Vector3.Dot(center - t.position, n); // center's distance in front of the surface (negative: behind)
        float approach = -Vector3.Dot(velocity, n);

        // Is the center inside the rim (or, for something fast, will it be where it crosses the surface)?
        Quaternion toLocal = Quaternion.Inverse(t.rotation);
        Vector3 local = toLocal * (center - t.position);
        // Past the surface already: what counts is where it crossed (fast things like rockets move sideways a lot in one step).
        Vector3 rimLocal = depth < 0f && approach > 0.05f ? local + toLocal * velocity * (depth / approach) : local;
        bool inside = InsideRim(portal, rimLocal, passage != null ? EdgeSlack : 1f);
        if (!inside && passage == null && approach > 0.05f && depth > 0f)
        {
            float time = depth / approach;
            inside = time < Time.fixedDeltaTime * 2f && InsideRim(portal, local + toLocal * velocity * time, 1f);
        }
        float gap = depth - extent; // between the surface and the nearest point of the object
        // Fast things (rockets) can get a whole step's travel past the surface before they're teleported.
        float reach = extent + 0.6f + Mathf.Max(0f, approach) * Time.fixedDeltaTime * 1.5f;
        bool touching = inside && gap <= Mathf.Max(TouchSlack, approach * Time.fixedDeltaTime * 1.5f) && depth > -reach;
        if (!touching)
        {
            return;
        }

        passage ??= Begin(portal, body, entity);
        passage.Step = step;
        IgnoreWall(passage);
        if (n.y > FloorNormalY && entity.IsPlayer && Controls(entity) && entity.PlayerInfo.Movement.IsGrounded)
        {
            entity.PlayerInfo.Movement.Unground(); // standing on a floor portal: fall in (see FloorPortalGroundingPatch)
        }
        if (depth < 0f && Controls(entity))
        {
            Teleport(passage, center, depth, local);
        }
    }

    private static Passage Begin(Portal portal, Rigidbody body, Entity entity)
    {
        var passage = new Passage { Body = body, Entity = entity, Portal = portal };
        passages[body] = passage;
        return passage;
    }

    /// <summary>Switches off collisions between everything of the traveller and the portal's wall (and keeps it so as colliders change).</summary>
    private static void IgnoreWall(Passage passage)
    {
        ownColliders.Clear();
        passage.Body.GetComponentsInChildren(false, ownColliders);
        foreach (Collider own in ownColliders)
        {
            if (own.isTrigger || !own.enabled)
            {
                continue;
            }
            foreach (Collider wall in passage.Portal.WallColliders)
            {
                if (wall != null && !Physics.GetIgnoreCollision(own, wall))
                {
                    Physics.IgnoreCollision(own, wall, true);
                    passage.Ignored.Add((own, wall));
                }
            }
        }
    }

    private static void RestoreWall(Passage passage, List<Collider> keepIgnoring = null)
    {
        for (int i = passage.Ignored.Count - 1; i >= 0; i--)
        {
            (Collider own, Collider wall) = passage.Ignored[i];
            if (keepIgnoring != null && keepIgnoring.Contains(wall))
            {
                continue;
            }
            if (own != null && wall != null)
            {
                Physics.IgnoreCollision(own, wall, false);
            }
            passage.Ignored.RemoveAt(i);
        }
    }

    private static void End(Passage passage)
    {
        RestoreWall(passage);
        if (passage.Entity != null && passage.Entity.IsPlayer && Controls(passage.Entity))
        {
            Unstick(passage.Entity.PlayerInfo);
        }
        if (passage.Body != null)
        {
            passages.Remove(passage.Body);
        }
        else
        {
            // Destroyed body: find it by value.
            Rigidbody key = null;
            foreach (KeyValuePair<Rigidbody, Passage> entry in passages)
            {
                if (entry.Value == passage)
                {
                    key = entry.Key;
                }
            }
            passages.Remove(key);
        }
    }

    private static void EndPassagesAt(Portal portal)
    {
        scratch.Clear();
        scratch.AddRange(passages.Values);
        foreach (Passage passage in scratch)
        {
            if (passage.Portal == portal)
            {
                End(passage);
            }
        }
    }

    private static void EndAll()
    {
        scratch.Clear();
        scratch.AddRange(passages.Values);
        foreach (Passage passage in scratch)
        {
            End(passage);
        }
        passages.Clear();
    }

    private static bool HasPassage(Portal portal)
    {
        foreach (Passage passage in passages.Values)
        {
            if (passage.Portal == portal)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Whether this player is going through a floor portal right now, so the floor under them shouldn't hold them up.</summary>
    public static bool IsFallingIntoFloorPortal(PlayerInfo player) =>
        passages.TryGetValue(player.Rigidbody, out Passage passage) && passage.Portal.Transform != null && passage.Portal.Transform.forward.y > FloorNormalY;

    /// <summary>Whether this client moves <paramref name="entity"/> (see the class summary).</summary>
    private static bool Controls(Entity entity)
    {
        if (entity.IsPlayer)
        {
            PlayerInfo player = entity.PlayerInfo;
            return player.isLocalPlayer && !player.ActiveGolfCartSeat.IsValid() && player.Movement.IsVisible;
        }
        return NetworkServer.active || entity.IsSimulatingRigidbody();
    }

    // ---- crossing over ----

    private static void Teleport(Passage passage, Vector3 center, float depth, Vector3 centerLocal)
    {
        Portal portal = passage.Portal;
        Portal exit = portal.Partner;
        Rigidbody body = passage.Body;
        Entity entity = passage.Entity;
        Quaternion travel = portal.TravelRotation;

        // The same spot relative to the exit: just past its surface, by however far it was past the entry's.
        Vector3 exitLocal = Portals.Flip * centerLocal;
        exitLocal.z = Mathf.Max(-depth, ExitDepth);
        Vector3 exitCenter = exit.Transform.position + exit.Transform.rotation * exitLocal;
        Vector3 velocity = travel * body.linearVelocity;
        Vector3 exitUp = exit.Transform.forward;
        if (entity.IsPlayer && exitUp.y > FloorNormalY)
        {
            // Dropping slowly into a floor portal still pops you out of the other one (or you'd fall straight back in).
            float outward = Vector3.Dot(velocity, exitUp);
            if (outward < MinFloorExitSpeed)
            {
                velocity += exitUp * (MinFloorExitSpeed - outward);
            }
        }
        Vector3 pivotOffset = body.position - center;

        if (entity.IsPlayer)
        {
            TeleportPlayer(entity.PlayerInfo, body, exit, exitCenter, pivotOffset, travel, velocity);
        }
        else
        {
            Vector3 angularVelocity = travel * body.angularVelocity;
            Quaternion rotation = travel * body.rotation;
            Vector3 position = exitCenter + travel * pivotOffset;
            entity.InformWillTeleport();
            body.position = position;
            body.rotation = rotation;
            entity.transform.SetPositionAndRotation(position, rotation);
            body.linearVelocity = velocity;
            body.angularVelocity = angularVelocity;
            entity.InformTeleported();
            PortalProjectiles.OnTeleported(entity, position);
            RotateLocalCameraIfRiding(entity, travel);
        }

        // Now going through the exit: its wall is off for this traveller, the entry's is back on.
        RestoreWall(passage, exit.WallColliders);
        passage.Portal = exit;
        IgnoreWall(passage);
        if (exit.Sill != null)
        {
            exit.Sill.enabled = true;
        }
    }

    private static void TeleportPlayer(PlayerInfo player, Rigidbody body, Portal exit, Vector3 exitCenter, Vector3 pivotOffset, Quaternion travel, Vector3 velocity)
    {
        // Players stay upright; turn them to face the way they're now moving (or out of the exit).
        Vector3 exitNormal = exit.Transform.forward;
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
        Vector3 feet = exitCenter + pivotOffset; // upright before and after, so the offset is unchanged

        // Coming out of a wall portal low down: don't end up with our feet in the floor.
        if (Mathf.Abs(exitNormal.y) < 0.6f
            && Physics.Raycast(exitCenter, Vector3.down, out RaycastHit ground, -pivotOffset.y + 0.5f,
                GameManager.LayerSettings.PlayerGroundableMask, QueryTriggerInteraction.Ignore)
            && ground.point.y > feet.y)
        {
            feet.y = ground.point.y + 0.02f;
        }

        float yawBefore = player.transform.eulerAngles.y;
        player.Movement.Teleport(feet, rotation, resetState: false);
        body.linearVelocity = velocity; // Teleport zeroes velocity; momentum is the whole point
        Plugin.Log.LogInfo($"Portal: {player.name} came out at {feet} moving {velocity} ({velocity.magnitude:0.0} m/s).");
        RotateLocalCamera(Mathf.DeltaAngle(yawBefore, rotation.eulerAngles.y));
        lastExit = feet;
        lastExitTime = Time.time;
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

    // ---- helpers ----

    /// <summary>Fully opened (the open animation has finished) and linked.</summary>
    public static bool IsOpen(Portal portal) =>
        portal != null && portal.Transform != null && portal.IsLinked && portal.Transform.localScale.x >= 0.99f;

    public static bool InsideRim(Portal portal, Vector3 local, float slack)
    {
        float ex = local.x / (portal.HalfWidth * slack);
        float ey = local.y / (portal.HalfHeight * slack);
        return ex * ex + ey * ey <= 1f;
    }

    public static Bounds GetBounds(Rigidbody body)
    {
        bool any = false;
        var bounds = new Bounds(body.worldCenterOfMass, Vector3.zero);
        ownColliders.Clear();
        body.GetComponentsInChildren(false, ownColliders);
        foreach (Collider collider in ownColliders)
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
    public static float SupportExtent(Vector3 extents, Vector3 direction) =>
        Mathf.Abs(direction.x) * extents.x + Mathf.Abs(direction.y) * extents.y + Mathf.Abs(direction.z) * extents.z;

    public static int GetTravellerMask()
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

/// <summary>
/// The game finds the ground with a ray down from the player's middle, which Physics.IgnoreCollision doesn't affect,
/// so a floor portal would hold the player up. While they're going through one, skip the ground check.
/// </summary>
[HarmonyPatch(typeof(PlayerMovement), "CanGround")]
internal static class FloorPortalGroundingPatch
{
    private static bool Prefix(PlayerMovement __instance, ref bool __result)
    {
        if (__instance.PlayerInfo != null && PortalTravel.IsFallingIntoFloorPortal(__instance.PlayerInfo))
        {
            __result = false;
            return false;
        }
        return true;
    }
}

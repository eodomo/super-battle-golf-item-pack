using System.Collections.Generic;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Straight-line travel that carries on through portals: beams (Kamehameha, railgun) and flying things (thrown
/// lightsabers and shields). A path that enters an open portal's opening comes out of the linked one, turned to match.
/// Every client works the paths out the same way from where they start, so only the start has to go over the network.
/// </summary>
internal static class PortalPaths
{
    public const int MaxHops = 4;
    private const float WallTolerance = 0.1f;
    private const float OutOffset = 0.01f;

    public struct Segment
    {
        public Vector3 origin;
        public Vector3 direction;
        public float length;
        public Vector3 End => origin + direction * length;
    }

    /// <summary>
    /// The pieces of a straight path of up to <paramref name="length"/> meters from <paramref name="origin"/>, split at
    /// each portal it goes through. With a <paramref name="wallMask"/> the path also stops at the first wall (not the wall
    /// a portal it enters sits on), and <paramref name="length"/> is shortened to match; pass 0 to ignore walls.
    /// </summary>
    public static List<Segment> Trace(Vector3 origin, Vector3 direction, float length, int wallMask, out float total)
    {
        var segments = new List<Segment>(2);
        direction = direction.normalized;
        float remaining = length;
        Portal skip = null;
        total = 0f;
        for (int hop = 0; ; hop++)
        {
            Portal into = null;
            float toPortal = remaining;
            if (hop < MaxHops && Portals.All.Count > 0)
            {
                PortalShots.TryFindEntry(origin, direction, remaining, skip, out into, out toPortal);
            }
            float segmentLength = into != null ? toPortal : remaining;
            bool blocked = false;
            if (wallMask != 0 && Physics.Raycast(origin, direction, out RaycastHit wall, segmentLength + WallTolerance, wallMask, QueryTriggerInteraction.Ignore)
                && (into == null || wall.distance < toPortal - WallTolerance))
            {
                segmentLength = wall.distance;
                into = null;
                blocked = true;
            }
            segments.Add(new Segment { origin = origin, direction = direction, length = segmentLength });
            total += segmentLength;
            if (blocked || into == null)
            {
                break;
            }
            Vector3 entry = origin + direction * segmentLength;
            remaining -= segmentLength;
            direction = into.MapDirection(direction).normalized;
            origin = into.MapPoint(entry) + direction * OutOffset;
            skip = into.Partner;
            if (remaining <= OutOffset)
            {
                break;
            }
        }
        return segments;
    }

    /// <summary>
    /// Moves <paramref name="position"/> forward <paramref name="step"/> meters, through any portal it goes into on the way
    /// (turning <paramref name="direction"/> to match). Returns whether it went through one.
    /// </summary>
    public static bool Advance(ref Vector3 position, ref Vector3 direction, float step)
    {
        bool crossed = false;
        Portal skip = null;
        for (int hop = 0; hop < MaxHops; hop++)
        {
            if (Portals.All.Count == 0 || !PortalShots.TryFindEntry(position, direction, step, skip, out Portal portal, out float t))
            {
                break;
            }
            Vector3 entry = position + direction * t;
            direction = portal.MapDirection(direction).normalized;
            position = portal.MapPoint(entry) + direction * OutOffset;
            step -= t;
            skip = portal.Partner;
            crossed = true;
        }
        position += direction * Mathf.Max(step, 0f);
        return crossed;
    }

    /// <summary>
    /// Where something at <paramref name="from"/> heading for <paramref name="target"/> should aim: the portal it should
    /// go into when that way round is shorter than the straight line, otherwise the target itself.
    /// </summary>
    public static Vector3 Waypoint(Vector3 from, Vector3 target, out bool viaPortal)
    {
        viaPortal = false;
        if (Portals.All.Count > 0 && PortalShots.TryFindPathTo(from, target, out Vector3 entry, out Vector3 exit)
            && (entry - from).magnitude + (target - exit).magnitude < (target - from).magnitude)
        {
            viaPortal = true;
            return entry;
        }
        return target;
    }
}

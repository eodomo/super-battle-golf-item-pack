using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Rockets and freeze bombs go through portals. <see cref="PortalTravel"/> already carries them (they're physics
/// bodies), but they find what they hit with their own overlap and sphere-cast checks, which would see the wall behind
/// a portal and blow up on it. Those checks are swapped for versions that skip a portal's wall where it's inside the
/// portal's opening, and a projectile's last position is moved along with it when it's teleported (they sweep from
/// their last position to the current one).
/// </summary>
internal static class PortalProjectiles
{
    private const float InFront = 1.5f;   // how far in front of / behind a portal's surface its wall is skipped
    private const float Behind = 3f;

    private static readonly AccessTools.FieldRef<Rocket, Vector3> RocketPrevious = AccessTools.FieldRefAccess<Rocket, Vector3>("previousPosition");
    private static readonly AccessTools.FieldRef<FreezeBomb, Vector3> FreezeBombPrevious = AccessTools.FieldRefAccess<FreezeBomb, Vector3>("previousPosition");

    public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results, int layerMask, QueryTriggerInteraction triggers)
    {
        int count = Physics.OverlapSphereNonAlloc(position, radius, results, layerMask, triggers);
        if (Portals.All.Count == 0)
        {
            return count;
        }
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            if (!IsPortalWallAt(results[i], position))
            {
                results[kept++] = results[i];
            }
        }
        return kept;
    }

    public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction triggers)
    {
        int count = Physics.SphereCastNonAlloc(ray, radius, results, maxDistance, layerMask, triggers);
        if (Portals.All.Count == 0)
        {
            return count;
        }
        int kept = 0;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = results[i];
            // A hit at distance 0 (already overlapping at the start) has no useful point; use where the sweep starts.
            Vector3 point = hit.distance <= 0f ? ray.origin : hit.point;
            if (!IsPortalWallAt(hit.collider, point))
            {
                results[kept++] = hit;
            }
        }
        return kept;
    }

    /// <summary>Whether <paramref name="collider"/> is the wall of an open portal, and <paramref name="point"/> is in that portal's opening.</summary>
    private static bool IsPortalWallAt(Collider collider, Vector3 point)
    {
        if (collider == null)
        {
            return false;
        }
        foreach (Portal portal in Portals.All)
        {
            if (!PortalTravel.IsOpen(portal) || !PortalTravel.IsOpen(portal.Partner) || !portal.WallColliders.Contains(collider))
            {
                continue;
            }
            Transform t = portal.Transform;
            Vector3 local = Quaternion.Inverse(t.rotation) * (point - t.position);
            if (local.z < InFront && local.z > -Behind && PortalTravel.InsideRim(portal, local, 1f))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Rockets and freeze bombs in flight, so portals check them whatever layer they're on.</summary>
    public static readonly HashSet<Rigidbody> Live = new();

    public static void Track(Component projectile)
    {
        if (projectile.TryGetComponent(out Rigidbody body))
        {
            Live.Add(body);
        }
    }

    /// <summary>A projectile came out of a portal: its next sweep starts at the exit, not back at the entry.</summary>
    public static void OnTeleported(Entity entity, Vector3 position)
    {
        if (entity.TryGetComponent(out Rocket rocket))
        {
            RocketPrevious(rocket) = position;
        }
        else if (entity.TryGetComponent(out FreezeBomb freezeBomb))
        {
            FreezeBombPrevious(freezeBomb) = position;
        }
    }
}

[HarmonyPatch]
internal static class ProjectileTrackPatch
{
    [HarmonyPostfix, HarmonyPatch(typeof(Rocket), "Awake")]
    private static void RocketAwake(Rocket __instance) => PortalProjectiles.Track(__instance);

    [HarmonyPostfix, HarmonyPatch(typeof(FreezeBomb), "Awake")]
    private static void FreezeBombAwake(FreezeBomb __instance) => PortalProjectiles.Track(__instance);
}

/// <summary>Swaps the collision queries in the rocket's and freeze bomb's CheckCollision for <see cref="PortalProjectiles"/>' versions.</summary>
[HarmonyPatch]
internal static class ProjectileCollisionPatch
{
    private static readonly MethodInfo Overlap = AccessTools.Method(typeof(Physics), nameof(Physics.OverlapSphereNonAlloc),
        new[] { typeof(Vector3), typeof(float), typeof(Collider[]), typeof(int), typeof(QueryTriggerInteraction) });
    private static readonly MethodInfo SphereCast = AccessTools.Method(typeof(Physics), nameof(Physics.SphereCastNonAlloc),
        new[] { typeof(Ray), typeof(float), typeof(RaycastHit[]), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
    private static readonly MethodInfo OverlapReplacement = AccessTools.Method(typeof(PortalProjectiles), nameof(PortalProjectiles.OverlapSphereNonAlloc));
    private static readonly MethodInfo SphereCastReplacement = AccessTools.Method(typeof(PortalProjectiles), nameof(PortalProjectiles.SphereCastNonAlloc));

    // CheckCollision is a local function of OnFixedBUpdate in both.
    private static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(Rocket)).Concat(AccessTools.GetDeclaredMethods(typeof(FreezeBomb)))
            .Where(m => m.Name.Contains("<OnFixedBUpdate>g__CheckCollision"));

    private static bool Prepare() => Overlap != null && SphereCast != null && TargetMethods().Any();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(Overlap) || instruction.Calls(SphereCast))
            {
                replaced++;
                MethodInfo replacement = instruction.Calls(Overlap) ? OverlapReplacement : SphereCastReplacement;
                yield return new CodeInstruction(OpCodes.Call, replacement).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
        if (replaced != 2)
        {
            Plugin.Log.LogWarning($"Portal projectiles: replaced {replaced} of 2 collision checks in {original.DeclaringType?.Name}.{original.Name}.");
        }
    }
}

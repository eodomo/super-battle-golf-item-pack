using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// Gunshots (the dueling pistol and the elephant gun) go through portals: a shot that enters a linked portal's
/// opening carries on out of the other one, and can hit whoever is over there. Their bullet trails are drawn in two
/// pieces to match.
///
/// The guns find what they hit with one Physics.RaycastNonAlloc call; that call is swapped for
/// <see cref="RaycastNonAlloc"/>, which follows the shot through portals and reports the hits along the whole path
/// (distances measured along the path, so the nearest one still wins).
/// </summary>
internal static class PortalShots
{
    private const int MaxHops = 4;
    private const float WallTolerance = 0.1f;

    private static readonly List<RaycastHit> path = new();
    private static readonly RaycastHit[] segment = new RaycastHit[64];

    // Hittables reached through a portal by the latest shot, with how the shot was turned and where it seems to
    // come from on their side (so knockback pushes them away from the exit portal, not toward it).
    private static readonly Dictionary<Hittable, (Quaternion turn, Vector3 origin)> throughPortal = new();
    private static int shotFrame = -1;

    /// <summary>Like Physics.RaycastNonAlloc, but the ray continues through any open portal it enters.</summary>
    public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction triggers)
    {
        throughPortal.Clear();
        int count = Physics.RaycastNonAlloc(ray, results, maxDistance, layerMask, triggers);
        Vector3 origin = ray.origin;
        Vector3 direction = ray.direction.normalized;
        if (!TryFindEntry(origin, direction, maxDistance, null, out Portal portal, out float t))
        {
            return count;
        }

        path.Clear();
        float travelled = 0f;
        float remaining = maxDistance;
        Quaternion turn = Quaternion.identity;
        Vector3 virtualOrigin = origin;
        bool firstSegment = true;
        for (int hop = 0; ; hop++)
        {
            // This segment's hits up to the portal count (someone standing in front of it is hit first); the wall it
            // sits on and anything behind it don't.
            int segmentCount = firstSegment ? count : Physics.RaycastNonAlloc(new Ray(origin, direction), segment, remaining, layerMask, triggers);
            RaycastHit[] source = firstSegment ? results : segment;
            float limit = portal != null ? t - WallTolerance : float.MaxValue;
            for (int i = 0; i < segmentCount; i++)
            {
                RaycastHit hit = source[i];
                if (hit.distance >= limit)
                {
                    continue;
                }
                if (!firstSegment)
                {
                    Hittable hittable = hit.collider != null ? hit.collider.GetComponentInParent<Hittable>() : null;
                    if (hittable != null)
                    {
                        throughPortal[hittable] = (turn, virtualOrigin);
                    }
                }
                hit.distance += travelled;
                path.Add(hit);
            }
            firstSegment = false;
            if (portal == null || hop >= MaxHops)
            {
                break;
            }
            // Out of the other portal, a hair in front of it.
            Vector3 entry = origin + direction * t;
            virtualOrigin = portal.MapPoint(virtualOrigin);
            turn = portal.TravelRotation * turn;
            Portal exit = portal.Partner;
            direction = portal.MapDirection(direction).normalized;
            origin = portal.MapPoint(entry) + direction * 0.01f;
            travelled += t;
            remaining -= t;
            if (remaining <= 0.01f || !TryFindEntry(origin, direction, remaining, exit, out portal, out t))
            {
                portal = null;
            }
        }

        int written = Mathf.Min(path.Count, results.Length);
        path.Sort((a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < written; i++)
        {
            results[i] = path[i];
        }
        shotFrame = Time.frameCount;
        return written;
    }

    /// <summary>
    /// Where a ray first goes into the front of an open portal's opening (not <paramref name="skip"/>, the one it
    /// just came out of), within <paramref name="maxDistance"/>.
    /// </summary>
    public static bool TryFindEntry(Vector3 origin, Vector3 direction, float maxDistance, Portal skip, out Portal portal, out float distance)
    {
        portal = null;
        distance = maxDistance;
        foreach (Portal candidate in Portals.All)
        {
            if (candidate == skip || !PortalTravel.IsOpen(candidate))
            {
                continue;
            }
            Vector3 normal = candidate.Transform.forward;
            float facing = Vector3.Dot(direction, normal);
            if (facing > -0.0001f)
            {
                continue; // only into the front of a portal
            }
            float t = Vector3.Dot(candidate.Transform.position - origin, normal) / facing;
            if (t < 0.01f || t > distance)
            {
                continue;
            }
            Vector3 local = candidate.Transform.InverseTransformPoint(origin + direction * t);
            if (PortalTravel.InsideRim(candidate, local, 1f))
            {
                portal = candidate;
                distance = t;
            }
        }
        return portal != null;
    }

    /// <summary>Knockback for someone shot through a portal comes from the exit portal's side.</summary>
    public static void AdjustHit(Hittable hittable, ref Vector3 direction, ref Vector3 localOrigin)
    {
        if (shotFrame != Time.frameCount || hittable == null || !throughPortal.TryGetValue(hittable, out var through))
        {
            return;
        }
        direction = through.turn * direction;
        localOrigin = hittable.transform.InverseTransformPoint(through.origin);
    }

    // ---- bullet trails ----

    /// <summary>
    /// For a hit: whether the shot from <paramref name="barrel"/> reached <paramref name="hitPoint"/> through a portal
    /// (worked out on each client from where the hit is, so nothing extra goes over the network).
    /// </summary>
    public static bool TryFindPathTo(Vector3 barrel, Vector3 hitPoint, out Vector3 entry, out Vector3 exit)
    {
        entry = exit = default;
        foreach (Portal portal in Portals.All)
        {
            if (!PortalTravel.IsOpen(portal))
            {
                continue;
            }
            // Where the hit would be if the far side were right behind this portal.
            Vector3 seen = portal.Partner.MapPoint(hitPoint);
            Vector3 toSeen = seen - barrel;
            float length = toSeen.magnitude;
            if (length < 0.01f || !TryFindEntry(barrel, toSeen / length, length, null, out Portal entered, out float t) || entered != portal)
            {
                continue;
            }
            entry = barrel + toSeen / length * t;
            exit = portal.MapPoint(entry);
            return true;
        }
        return false;
    }

    /// <summary>Draws a two-piece bullet trail: barrel to the entry portal, then exit portal to the end.</summary>
    public static void DrawTrail(Vector3 barrel, Quaternion barrelRotation, Vector3 entry, Vector3 exit, Vector3 end, bool impact)
    {
        if (!VfxPersistentData.TryGetPooledVfx(VfxType.DuelingPistolMuzzle, out PoolableParticleSystem first))
        {
            return;
        }
        if (!first.TryGetComponent(out DuelingPistolVfx firstTrail))
        {
            first.ReturnToPool();
            return;
        }
        first.transform.SetPositionAndRotation(barrel, barrelRotation);
        firstTrail.SetMuzzleEffectsEnabled(enabled: true);
        firstTrail.SetPoints(barrel, entry);
        first.Play();

        Vector3 out_ = end - exit;
        Quaternion outRotation = out_.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(out_) : Quaternion.identity;
        if (VfxPersistentData.TryGetPooledVfx(VfxType.DuelingPistolMuzzle, out PoolableParticleSystem second))
        {
            if (second.TryGetComponent(out DuelingPistolVfx secondTrail))
            {
                second.transform.SetPositionAndRotation(exit, outRotation);
                secondTrail.SetMuzzleEffectsEnabled(enabled: false);
                secondTrail.SetPoints(exit, end);
                second.Play();
            }
            else
            {
                second.ReturnToPool();
            }
        }
        if (impact && out_.sqrMagnitude > 0.0001f && VfxPersistentData.TryGetPooledVfx(VfxType.DuelingPistolImpact, out PoolableParticleSystem spark))
        {
            spark.transform.SetPositionAndRotation(end, Quaternion.LookRotation(-out_));
            spark.Play();
        }
    }

    public static bool DrawHit(Vector3 barrel, Quaternion barrelRotation, VfxManager.GunShotHitVfxData hitData)
    {
        Vector3 hitPoint = hitData.GetHitPoint();
        if (!TryFindPathTo(barrel, hitPoint, out Vector3 entry, out Vector3 exit))
        {
            return false;
        }
        DrawTrail(barrel, barrelRotation, entry, exit, hitPoint, !hitData.hitElectromagnetShield);
        return true;
    }

    public static bool DrawMiss(Vector3 barrel, Quaternion barrelRotation, Vector3 direction, float maxDistance)
    {
        direction = direction.normalized;
        if (!TryFindEntry(barrel, direction, maxDistance, null, out Portal portal, out float t))
        {
            return false;
        }
        Vector3 entry = barrel + direction * t;
        Vector3 exit = portal.MapPoint(entry);
        Vector3 end = exit + portal.MapDirection(direction).normalized * (maxDistance - t);
        DrawTrail(barrel, barrelRotation, entry, exit, end, impact: false);
        return true;
    }
}

/// <summary>Swaps the gunshot raycasts in the dueling pistol's and elephant gun's shooting code for <see cref="PortalShots.RaycastNonAlloc"/>.</summary>
[HarmonyPatch]
internal static class GunshotRaycastPatch
{
    private static readonly MethodInfo Original = AccessTools.Method(typeof(Physics), nameof(Physics.RaycastNonAlloc),
        new[] { typeof(Ray), typeof(RaycastHit[]), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(PortalShots), nameof(PortalShots.RaycastNonAlloc));

    // The shots are local functions of the shooting routines (Shoot, ReflectShotOffElectromagnetShield).
    private static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(PlayerInventory)).Where(m =>
            m.Name.Contains("g__") && (m.Name.Contains("<ShootDuelingPistolRoutine>") || m.Name.Contains("<ShootElephantGunRoutine>")));

    private static bool Prepare() => Original != null && TargetMethods().Any();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(Original))
            {
                replaced++;
                yield return new CodeInstruction(OpCodes.Call, Replacement).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
        if (replaced == 0)
        {
            Plugin.Log.LogWarning($"Portal shots: no raycast found in {original.Name}.");
        }
    }
}

[HarmonyPatch(typeof(Hittable), nameof(Hittable.HitWithItem))]
internal static class GunshotHitPatch
{
    private static void Prefix(Hittable __instance, ItemType itemType, ref Vector3 direction, ref Vector3 localOrigin)
    {
        if (itemType == ItemType.DuelingPistol || itemType == ItemType.ElephantGun)
        {
            PortalShots.AdjustHit(__instance, ref direction, ref localOrigin);
        }
    }
}

[HarmonyPatch(typeof(VfxManager))]
internal static class GunshotTrailPatch
{
    [HarmonyPrefix, HarmonyPatch(nameof(VfxManager.PlayDuelingPistolHitInternal))]
    private static bool PistolHit(PlayerInventory shootingPlayer, VfxManager.GunShotHitVfxData hitData) =>
        shootingPlayer == null || !PortalShots.DrawHit(shootingPlayer.GetDuelingPistolBarrelEndPosition(), shootingPlayer.GetDuelingPistolBarrelEndRotation(), hitData);

    [HarmonyPrefix, HarmonyPatch(nameof(VfxManager.PlayDuelingPistolMissInternal))]
    private static bool PistolMiss(PlayerInventory shootingPlayer, Vector3 direction) =>
        shootingPlayer == null || !PortalShots.DrawMiss(shootingPlayer.GetDuelingPistolBarrelEndPosition(), shootingPlayer.GetDuelingPistolBarrelEndRotation(),
            direction, GameManager.ItemSettings.DuelingPistolMaxShotDistance);

    [HarmonyPrefix, HarmonyPatch(nameof(VfxManager.PlayElephantGunHitInternal))]
    private static bool ElephantHit(PlayerInventory shootingPlayer, VfxManager.GunShotHitVfxData hitData) =>
        shootingPlayer == null || !PortalShots.DrawHit(shootingPlayer.GetElephantGunBarrelEndPosition(), shootingPlayer.GetElephantGunBarrelEndRotation(), hitData);

    [HarmonyPrefix, HarmonyPatch(nameof(VfxManager.PlayElephantGunMissInternal))]
    private static bool ElephantMiss(PlayerInventory shootingPlayer, Vector3 direction) =>
        shootingPlayer == null || !PortalShots.DrawMiss(shootingPlayer.GetElephantGunBarrelEndPosition(), shootingPlayer.GetElephantGunBarrelEndRotation(),
            direction, GameManager.ItemSettings.ElephantGunMaxShotDistance);
}

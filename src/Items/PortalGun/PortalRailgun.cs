using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Brimstone.Geometry;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The railgun's shot goes through portals like the pistol's does (see <see cref="PortalShots"/>): the two casts that
/// find what it hits (the one that finds what blocks it, then the one that finds who it hits) follow it through any
/// portal it enters, and its beam is drawn in pieces to match. Only the casts made while firing are changed; the aim
/// preview keeps its own plain ones.
/// </summary>
internal static class PortalRailgun
{
    /// <summary>True while the local player's railgun shot is being worked out.</summary>
    public static bool Shooting;

    public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction triggers) =>
        Shooting
            ? PortalShots.RaycastNonAlloc(ray, results, maxDistance, layerMask, triggers)
            : Physics.RaycastNonAlloc(ray, results, maxDistance, layerMask, triggers);

    public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, float maxDistance, int layerMask, QueryTriggerInteraction triggers) =>
        Shooting
            ? PortalShots.SphereCastNonAlloc(ray, radius, results, maxDistance, layerMask, triggers)
            : Physics.SphereCastNonAlloc(ray, radius, results, maxDistance, layerMask, triggers);

    /// <summary>The railgun's beam, drawn from the barrel to where the shot ended as it would be seen through portals. False when it didn't go through any.</summary>
    public static bool DrawBeam(PlayerInventory inventory, ItemAimPoint beamEndPoint, bool wasStoppedByHit)
    {
        if (Portals.All.Count == 0)
        {
            return false;
        }
        Vector3 origin = inventory.GetRailgunBeamOriginPosition();
        Vector3 span = beamEndPoint.GetEffectiveWorldPoint() - origin;
        float length = span.magnitude;
        if (length < 0.01f)
        {
            return false;
        }
        // The end point is where the shot would end if it went straight on, so folding that line through the portals gives the real pieces.
        List<PortalPaths.Segment> path = PortalPaths.Trace(origin, span / length, length, 0, out _);
        if (path.Count < 2)
        {
            return false;
        }

        foreach (PortalPaths.Segment segment in path)
        {
            if (!VfxPersistentData.TryGetPooledVfx(VfxType.RailgunShoot, out PoolableParticleSystem beam))
            {
                continue;
            }
            if (beam.TryGetComponent(out RailgunShootVfx shoot))
            {
                shoot.SetPoints(segment.origin, segment.End);
            }
            if (beam.TryGetComponent(out TeamColorVfxHandler color))
            {
                color.SetTeam(inventory.PlayerInfo.GetEffectiveTeam());
            }
            beam.transform.SetPositionAndRotation(segment.origin, Quaternion.LookRotation(segment.direction));
            beam.Play();
        }

        PortalPaths.Segment last = path[path.Count - 1];
        if (wasStoppedByHit && VfxPersistentData.TryGetPooledVfx(VfxType.RailgunShootEnd, out PoolableParticleSystem end))
        {
            if (end.TryGetComponent(out TeamColorVfxHandler color))
            {
                color.SetTeam(inventory.PlayerInfo.GetEffectiveTeam());
            }
            end.transform.SetPositionAndRotation(last.End, Quaternion.LookRotation(-last.direction));
            if (end.TryGetComponent(out RailgunShootEndVfx endVfx))
            {
                endVfx.AdjustToTerrain();
            }
            end.Play();
        }
        if (GameplayCameraManager.ShouldPlayImpactFrameForExplosion(origin, GameManager.ItemSettings.RailgunShotRadius, GameManager.CameraGameplaySettings.RailgunShotOriginImpactFrameDistanceSquared))
        {
            CameraModuleController.PlayImpactFrame(origin, inventory.PlayerInfo.GetEffectiveTeam());
        }
        CameraModuleController.Shake(GameManager.CameraGameplaySettings.ThunderstormExplosionScreenshakeSettings,
            BGeo.ClosestPointOnSegment(GameManager.Camera.transform.position, path[0].origin, path[0].End));
        return true;
    }
}

/// <summary>Marks the railgun's shot (not its aim preview) while it's being worked out.</summary>
[HarmonyPatch]
internal static class RailgunShootPatch
{
    // Shoot is a local function of the charge-and-shoot routine.
    private static IEnumerable<MethodBase> TargetMethods() =>
        AccessTools.GetDeclaredMethods(typeof(PlayerInventory)).Where(m =>
            m.Name.Contains("<ChargeAndShootRailgunRoutine>") && m.Name.Contains("g__Shoot"));

    private static bool Prepare() => TargetMethods().Any();

    private static void Prefix() => PortalRailgun.Shooting = true;

    private static void Finalizer() => PortalRailgun.Shooting = false;
}

/// <summary>Swaps the two casts in <c>PlayerInventory.CastRailgunAim</c> for <see cref="PortalRailgun"/>'s versions.</summary>
[HarmonyPatch]
internal static class RailgunCastPatch
{
    private static readonly MethodInfo RayCast = AccessTools.Method(typeof(Physics), nameof(Physics.RaycastNonAlloc),
        new[] { typeof(Ray), typeof(RaycastHit[]), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
    private static readonly MethodInfo SphereCast = AccessTools.Method(typeof(Physics), nameof(Physics.SphereCastNonAlloc),
        new[] { typeof(Ray), typeof(float), typeof(RaycastHit[]), typeof(float), typeof(int), typeof(QueryTriggerInteraction) });
    private static readonly MethodInfo RayReplacement = AccessTools.Method(typeof(PortalRailgun), nameof(PortalRailgun.RaycastNonAlloc));
    private static readonly MethodInfo SphereReplacement = AccessTools.Method(typeof(PortalRailgun), nameof(PortalRailgun.SphereCastNonAlloc));

    private static MethodBase TargetMethod() => AccessTools.Method(typeof(PlayerInventory), nameof(PlayerInventory.CastRailgunAim),
        new[] { typeof(Vector3), typeof(Vector3), typeof(float), typeof(float), typeof(RaycastHit[]), typeof(Ray).MakeByRefType() });

    private static bool Prepare() => RayCast != null && SphereCast != null && TargetMethod() != null;

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(RayCast) || instruction.Calls(SphereCast))
            {
                replaced++;
                MethodInfo replacement = instruction.Calls(RayCast) ? RayReplacement : SphereReplacement;
                yield return new CodeInstruction(OpCodes.Call, replacement).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
            }
            else
            {
                yield return instruction;
            }
        }
        if (replaced != 2)
        {
            Plugin.Log.LogWarning($"Portal railgun: replaced {replaced} of 2 casts in CastRailgunAim.");
        }
    }
}

/// <summary>The railgun's beam, drawn in pieces when the shot went through a portal (on every client).</summary>
[HarmonyPatch(typeof(PlayerInventory), "PlayRailgunShotEffectsInternal")]
internal static class RailgunBeamPatch
{
    private static bool Prefix(PlayerInventory __instance, ItemAimPoint beamEndPoint, bool wasStoppedByHit) =>
        !PortalRailgun.DrawBeam(__instance, beamEndPoint, wasStoppedByHit);
}

using System.Collections.Generic;
using FMODUnity;
using Mirror;
using UnityEngine;

namespace ItemPack;

/// <summary>The game's rocket explosion, reusable: knockouts on the host, effects and sound on every client.</summary>
internal static class Explosions
{
    private static readonly HashSet<Hittable> hit = new();

    /// <summary>
    /// Host only: knocks back / out everything hittable within <paramref name="radiusScale"/> times the rocket's
    /// blast radius, exactly like a rocket explosion (shields, knockout rules, team rules all apply).
    /// </summary>
    public static void ServerExplode(Vector3 position, float radiusScale, PlayerInfo owner)
    {
        if (!NetworkServer.active)
        {
            return;
        }
        float radius = GameManager.ItemSettings.RocketExplosionRange * radiusScale;
        Items.DirtWorld.ServerBreakInSphere(position, radius);
        int count = Physics.OverlapSphereNonAlloc(position, radius, PlayerGolfer.overlappingColliderBuffer,
            GameManager.LayerSettings.RocketHittablesMask, QueryTriggerInteraction.Ignore);
        ItemUseId useId = owner != null ? owner.Inventory.IncrementAndGetCurrentItemUseId(ItemType.RocketLauncher) : ItemUseId.Invalid;
        hit.Clear();
        for (int i = 0; i < count; i++)
        {
            if (!PlayerGolfer.overlappingColliderBuffer[i].TryGetComponentInParent(out Hittable hittable, includeInactive: true) || !hit.Add(hittable))
            {
                continue;
            }
            if (!hittable.TryGetClosestPointOnAllActiveColliders(position, out Vector3 closest, out float distanceSquared))
            {
                distanceSquared = 0f;
                closest = hittable.AsEntity.HasRigidbody ? hittable.AsEntity.Rigidbody.worldCenterOfMass : hittable.transform.position;
            }
            Vector3 direction = closest - position;
            if (direction == Vector3.zero)
            {
                direction = Vector3.up;
            }
            hittable.HitWithItem(ItemType.RocketLauncher, useId, hittable.transform.InverseTransformPoint(closest), direction,
                hittable.transform.InverseTransformPoint(position), Mathf.Sqrt(distanceSquared), owner != null ? owner.Inventory : null,
                isReflected: false, isInSpecialState: false, canHitWithNoUser: true, hitTimestamp: NetworkTime.time, itemUseHash: 0uL);
        }
    }

    /// <summary>Every client: the rocket explosion's particles, screen shake and sound, optionally scaled up.</summary>
    public static void PlayEffects(Vector3 position, float scale = 1f)
    {
        VfxManager.PlayPooledVfxLocalOnly(VfxType.RocketLauncherRocketExplosion, position, Quaternion.identity, Vector3.one * scale);
        CameraModuleController.Shake(GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings, position, scale);
        RuntimeManager.PlayOneShot(GameManager.AudioSettings.RocketExplosionEvent, position);
    }
}

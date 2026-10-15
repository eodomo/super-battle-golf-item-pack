using System.Collections.Generic;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// While something is halfway through a portal, a copy of it is drawn at the other portal, posed live, so the
/// half that's through is seen coming out of the other side (and the half that isn't yet is seen sticking out of
/// the entry after teleporting). Each half that's "inside" a wall is hidden by the wall and the portal surface, like
/// the real object, so no clipping is needed.
/// </summary>
internal static class PortalClones
{
    private const float RimSlack = 1.2f;

    private sealed class Clone
    {
        public ModelPuppet Puppet;
        public int Frame;
    }

    private static readonly Dictionary<(Entity, Portal), Clone> clones = new();
    private static readonly List<(Entity, Portal)> stale = new();
    private static readonly HashSet<Entity> found = new();
    private static readonly Collider[] overlap = new Collider[64];
    private static readonly List<Renderer> renderers = new();
    private static int frame;

    public static void LateTick()
    {
        frame++;
        if (Portals.All.Count > 0 && GameManager.LayerSettings != null)
        {
            int mask = PortalTravel.GetTravellerMask();
            foreach (Portal portal in Portals.All)
            {
                if (PortalTravel.IsOpen(portal) && PortalTravel.IsOpen(portal.Partner))
                {
                    UpdatePortal(portal, mask);
                }
            }
        }

        stale.Clear();
        foreach (KeyValuePair<(Entity, Portal), Clone> entry in clones)
        {
            if (entry.Value.Frame != frame)
            {
                entry.Value.Puppet.Destroy();
                stale.Add(entry.Key);
            }
        }
        foreach ((Entity, Portal) key in stale)
        {
            clones.Remove(key);
        }
    }

    public static void OnPortalRemoved(Portal portal)
    {
        stale.Clear();
        foreach (KeyValuePair<(Entity, Portal), Clone> entry in clones)
        {
            if (entry.Key.Item2 == portal)
            {
                entry.Value.Puppet.Destroy();
                stale.Add(entry.Key);
            }
        }
        foreach ((Entity, Portal) key in stale)
        {
            clones.Remove(key);
        }
    }

    private static void UpdatePortal(Portal portal, int mask)
    {
        Transform t = portal.Transform;
        var halfExtents = new Vector3(portal.HalfWidth + 1.5f, portal.HalfHeight + 1.5f, 3f);
        int count = Physics.OverlapBoxNonAlloc(t.position, halfExtents, overlap, t.rotation, mask, QueryTriggerInteraction.Ignore);
        found.Clear();
        Matrix4x4 map = default;
        bool mapReady = false;
        for (int i = 0; i < count; i++)
        {
            if (!overlap[i].TryGetComponentInParent(out Entity entity, includeInactive: false) || !found.Add(entity))
            {
                continue;
            }
            if (entity.IsPlayer && entity.PlayerInfo.ActiveGolfCartSeat.IsValid())
            {
                continue; // drawn with their cart
            }
            if (!Straddles(portal, entity))
            {
                continue;
            }
            if (!clones.TryGetValue((entity, portal), out Clone clone))
            {
                clone = new Clone { Puppet = CreatePuppet(entity) };
                clones[(entity, portal)] = clone;
            }
            clone.Frame = frame;
            if (!mapReady)
            {
                Transform exit = portal.Partner.Transform;
                map = Matrix4x4.TRS(exit.position, exit.rotation, Vector3.one) * Matrix4x4.Rotate(Portals.Flip)
                    * Matrix4x4.TRS(t.position, t.rotation, Vector3.one).inverse;
                mapReady = true;
            }
            clone.Puppet.Sync(map);
        }
    }

    /// <summary>Whether the object's bounds cross the portal's surface inside the rim.</summary>
    private static bool Straddles(Portal portal, Entity entity)
    {
        Bounds bounds;
        if (entity.HasRigidbody)
        {
            bounds = PortalTravel.GetBounds(entity.Rigidbody);
        }
        else if (entity.TryGetComponent(out Collider collider))
        {
            bounds = collider.bounds;
        }
        else
        {
            return false;
        }
        Transform t = portal.Transform;
        float depth = Vector3.Dot(bounds.center - t.position, t.forward);
        float extent = PortalTravel.SupportExtent(bounds.extents, t.forward);
        if (Mathf.Abs(depth) >= extent)
        {
            return false;
        }
        Vector3 local = Quaternion.Inverse(t.rotation) * (bounds.center - t.position);
        return PortalTravel.InsideRim(portal, local, RimSlack);
    }

    private static ModelPuppet CreatePuppet(Entity entity)
    {
        renderers.Clear();
        ModelPuppet.CollectRenderers(entity.transform, renderers);
        if (entity.IsGolfCart)
        {
            foreach (PlayerInfo passenger in entity.AsGolfCart.passengers)
            {
                if (passenger != null)
                {
                    ModelPuppet.CollectRenderers(passenger.transform, renderers);
                }
            }
        }
        return ModelPuppet.Create($"PortalClone_{entity.name}", renderers);
    }

    public static void ClearAll()
    {
        foreach (Clone clone in clones.Values)
        {
            clone.Puppet.Destroy();
        }
        clones.Clear();
    }
}

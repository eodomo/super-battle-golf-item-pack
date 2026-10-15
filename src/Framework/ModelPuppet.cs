using System;
using System.Collections.Generic;
using UnityEngine;

namespace ItemPack;

/// <summary>
/// A copy of an object's visible model (players, carts) that can be posed anywhere: every renderer is copied onto
/// its own proxy transform, and skinned meshes get proxy bones. The game's meshes aren't CPU-readable, so this
/// never touches mesh data; the copies render the same GPU meshes with the same materials.
///
/// Uses: the half of a player that's already through a portal (<see cref="Sync"/> every frame), and frozen
/// snapshots for the lightsaber's halves (bones on the far side of the cut collapse into the cut).
/// </summary>
internal sealed class ModelPuppet
{
    public GameObject Root { get; }

    private readonly List<(Transform source, Transform proxy)> parts = new();
    private static readonly MaterialPropertyBlock block = new();

    private ModelPuppet(string name)
    {
        Root = new GameObject(name);
    }

    /// <summary>The mesh renderers and skinned mesh renderers of <paramref name="root"/> worth copying.</summary>
    public static void CollectRenderers(Transform root, List<Renderer> results)
    {
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(false))
        {
            if (IsCopyable(renderer))
            {
                results.Add(renderer);
            }
        }
    }

    private static bool IsCopyable(Renderer renderer)
    {
        if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy)
        {
            return false;
        }
        if (renderer is SkinnedMeshRenderer skinned)
        {
            return skinned.sharedMesh != null;
        }
        if (renderer is MeshRenderer)
        {
            // World-space text (name tags) is a mesh renderer too; leave it out.
            return renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null
                && !renderer.TryGetComponent(out TMPro.TMP_Text _);
        }
        return false;
    }

    /// <summary>
    /// Copies <paramref name="renderers"/>. <paramref name="keep"/> decides per bone / mesh part whether it's part of
    /// the copy; dropped skinned bones are squeezed into <paramref name="collapsePoint"/> (so the mesh ends there),
    /// dropped mesh parts are left out.
    /// </summary>
    public static ModelPuppet Create(string name, List<Renderer> renderers, Func<Transform, bool> keep = null, Vector3 collapsePoint = default)
    {
        var puppet = new ModelPuppet(name);
        var proxies = new Dictionary<Transform, Transform>();
        Transform collapsed = null;

        Transform ProxyFor(Transform source)
        {
            if (source == null)
            {
                return null;
            }
            if (keep != null && !keep(source))
            {
                if (collapsed == null)
                {
                    collapsed = new GameObject("Collapsed").transform;
                    collapsed.SetParent(puppet.Root.transform, false);
                    collapsed.position = collapsePoint;
                    collapsed.localScale = Vector3.one * 0.0001f;
                }
                return collapsed;
            }
            if (!proxies.TryGetValue(source, out Transform proxy))
            {
                proxy = new GameObject(source.name).transform;
                proxy.SetParent(puppet.Root.transform, false);
                proxy.SetPositionAndRotation(source.position, source.rotation);
                proxy.localScale = source.lossyScale;
                proxies[source] = proxy;
                puppet.parts.Add((source, proxy));
            }
            return proxy;
        }

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }
            if (renderer is SkinnedMeshRenderer skinned)
            {
                Transform[] bones = skinned.bones;
                var mapped = new Transform[bones.Length];
                bool anyKept = false;
                for (int i = 0; i < bones.Length; i++)
                {
                    mapped[i] = ProxyFor(bones[i]);
                    anyKept |= mapped[i] != null && mapped[i] != collapsed;
                }
                if (!anyKept && bones.Length > 0)
                {
                    continue; // all of it is on the other side
                }
                Transform rootBone = skinned.rootBone != null ? ProxyFor(skinned.rootBone) : null;
                // Skinned vertices come from the bones alone, so the renderer itself can sit anywhere.
                var go = new GameObject(skinned.name);
                go.transform.SetParent(puppet.Root.transform, false);
                go.layer = skinned.gameObject.layer;
                var copy = go.AddComponent<SkinnedMeshRenderer>();
                copy.sharedMesh = skinned.sharedMesh;
                copy.bones = mapped;
                copy.rootBone = rootBone != collapsed ? rootBone : null;
                copy.updateWhenOffscreen = true; // its bounds would follow the original's root bone otherwise
                CopyRendererSettings(skinned, copy);
            }
            else if (renderer.TryGetComponent(out MeshFilter filter))
            {
                if (keep != null && !keep(renderer.transform))
                {
                    continue;
                }
                Transform proxy = ProxyFor(renderer.transform);
                var go = new GameObject(renderer.name);
                go.transform.SetParent(proxy, false);
                go.layer = renderer.gameObject.layer;
                go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                CopyRendererSettings(renderer, go.AddComponent<MeshRenderer>());
            }
        }
        return puppet;
    }

    private static void CopyRendererSettings(Renderer from, Renderer to)
    {
        to.sharedMaterials = from.sharedMaterials;
        to.shadowCastingMode = from.shadowCastingMode;
        to.receiveShadows = from.receiveShadows;
        to.renderingLayerMask = from.renderingLayerMask;
        if (from.HasPropertyBlock())
        {
            // Per-player colors (skin, outfit, team) live in property blocks.
            from.GetPropertyBlock(block);
            to.SetPropertyBlock(block);
            for (int i = 0; i < from.sharedMaterials.Length; i++)
            {
                from.GetPropertyBlock(block, i);
                if (!block.isEmpty)
                {
                    to.SetPropertyBlock(block, i);
                }
            }
        }
    }

    /// <summary>Poses every proxy at <paramref name="map"/> applied to its source's current pose (rigid map, no scale).</summary>
    public void Sync(Matrix4x4 map)
    {
        Quaternion rotation = map.rotation;
        foreach ((Transform source, Transform proxy) in parts)
        {
            if (source == null)
            {
                continue;
            }
            proxy.SetPositionAndRotation(map.MultiplyPoint3x4(source.position), rotation * source.rotation);
            proxy.localScale = source.lossyScale;
        }
    }

    /// <summary>Positions of the copied (kept) parts, e.g. to size a collider around them.</summary>
    public IEnumerable<Vector3> PartPositions()
    {
        foreach ((Transform _, Transform proxy) in parts)
        {
            yield return proxy.position;
        }
    }

    public void Destroy()
    {
        if (Root != null)
        {
            UnityEngine.Object.Destroy(Root);
        }
    }
}

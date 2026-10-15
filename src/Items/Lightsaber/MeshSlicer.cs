using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack.Items;

/// <summary>
/// Cuts meshes along a plane into two new meshes (one per side), in world space. Triangles crossing the plane are
/// split, and the cut is closed with a cap (a fan around the middle of the cross-section) for the glowing,
/// cauterized face.
/// </summary>
internal sealed class MeshSlicer
{
    /// <summary>One side's output: triangles grouped by material, plus the cap's triangles (last submesh).</summary>
    private sealed class Side
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> Uvs = new();
        public readonly List<(Material material, List<int> triangles)> Groups = new();
        public readonly List<int> Cap = new();

        public List<int> Group(Material material)
        {
            foreach ((Material m, List<int> tris) in Groups)
            {
                if (m == material)
                {
                    return tris;
                }
            }
            var list = new List<int>();
            Groups.Add((material, list));
            return list;
        }

        public void Add(List<int> tris, Vector3 p, Vector3 n, Vector2 uv)
        {
            tris.Add(Positions.Count);
            Positions.Add(p);
            Normals.Add(n);
            Uvs.Add(uv);
        }
    }

    private readonly Plane plane;
    private readonly Side positive = new();
    private readonly Side negative = new();
    private readonly List<(Vector3 a, Vector3 b)> cutEdges = new();

    public MeshSlicer(Plane plane)
    {
        this.plane = plane;
    }

    public bool HasPositive => positive.Groups.Count > 0;
    public bool HasNegative => negative.Groups.Count > 0;

    /// <summary>The middle of the cut surface and the width of its widest side (zero if nothing was cut).</summary>
    public (Vector3 center, float size) CutExtent
    {
        get
        {
            if (cutEdges.Count == 0)
            {
                return (Vector3.zero, 0f);
            }
            var bounds = new Bounds(cutEdges[0].a, Vector3.zero);
            foreach ((Vector3 a, Vector3 b) in cutEdges)
            {
                bounds.Encapsulate(a);
                bounds.Encapsulate(b);
            }
            return (bounds.center, Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z));
        }
    }

    /// <summary>Adds a mesh (its copy, where it is in the world, and its materials per submesh).</summary>
    public void Add(MeshCopy mesh, Matrix4x4 localToWorld, Material[] materials)
    {
        int count = mesh.Positions.Length;
        var world = new Vector3[count];
        var normals = new Vector3[count];
        var distance = new float[count];
        for (int i = 0; i < count; i++)
        {
            world[i] = localToWorld.MultiplyPoint3x4(mesh.Positions[i]);
            normals[i] = localToWorld.MultiplyVector(mesh.Normals[i]).normalized;
            distance[i] = plane.GetDistanceToPoint(world[i]);
        }
        for (int s = 0; s < mesh.Triangles.Length; s++)
        {
            Material material = materials.Length > 0 ? materials[Mathf.Min(s, materials.Length - 1)] : null;
            if (material == null)
            {
                continue;
            }
            int[] tris = mesh.Triangles[s];
            List<int> pos = null, neg = null;
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                bool sa = distance[a] >= 0f, sb = distance[b] >= 0f, sc = distance[c] >= 0f;
                if (sa == sb && sb == sc)
                {
                    Side side = sa ? positive : negative;
                    List<int> group = sa ? (pos ??= positive.Group(material)) : (neg ??= negative.Group(material));
                    side.Add(group, world[a], normals[a], mesh.Uvs[a]);
                    side.Add(group, world[b], normals[b], mesh.Uvs[b]);
                    side.Add(group, world[c], normals[c], mesh.Uvs[c]);
                    continue;
                }
                pos ??= positive.Group(material);
                neg ??= negative.Group(material);
                // Rotate so that 'a' is the vertex alone on its side (keeps the winding).
                if (sb != sa && sb != sc)
                {
                    (a, b, c) = (b, c, a);
                }
                else if (sc != sa && sc != sb)
                {
                    (a, b, c) = (c, a, b);
                }
                Split(mesh, world, normals, distance, a, b, c, pos, neg);
            }
        }
    }

    private void Split(MeshCopy mesh, Vector3[] world, Vector3[] normals, float[] distance, int a, int b, int c, List<int> pos, List<int> neg)
    {
        float tab = distance[a] / (distance[a] - distance[b]);
        float tac = distance[a] / (distance[a] - distance[c]);
        Vector3 pab = Vector3.Lerp(world[a], world[b], tab), pac = Vector3.Lerp(world[a], world[c], tac);
        Vector3 nab = Vector3.Lerp(normals[a], normals[b], tab).normalized, nac = Vector3.Lerp(normals[a], normals[c], tac).normalized;
        Vector2 uab = Vector2.Lerp(mesh.Uvs[a], mesh.Uvs[b], tab), uac = Vector2.Lerp(mesh.Uvs[a], mesh.Uvs[c], tac);

        bool aPositive = distance[a] >= 0f;
        Side lone = aPositive ? positive : negative;
        Side pair = aPositive ? negative : positive;
        List<int> loneTris = aPositive ? pos : neg;
        List<int> pairTris = aPositive ? neg : pos;

        // a, ab, ac on a's side; ab, b, c and ab, c, ac on the other.
        lone.Add(loneTris, world[a], normals[a], mesh.Uvs[a]);
        lone.Add(loneTris, pab, nab, uab);
        lone.Add(loneTris, pac, nac, uac);

        pair.Add(pairTris, pab, nab, uab);
        pair.Add(pairTris, world[b], normals[b], mesh.Uvs[b]);
        pair.Add(pairTris, world[c], normals[c], mesh.Uvs[c]);
        pair.Add(pairTris, pab, nab, uab);
        pair.Add(pairTris, world[c], normals[c], mesh.Uvs[c]);
        pair.Add(pairTris, pac, nac, uac);

        cutEdges.Add((pab, pac));
    }

    /// <summary>
    /// Builds the two meshes. Each one's vertices are relative to its own center (returned), and its last submesh
    /// is the cap, drawn with <paramref name="capMaterial"/>.
    /// </summary>
    public (Mesh mesh, Material[] materials, Vector3 center)? Build(bool positiveSide, Material capMaterial)
    {
        Side side = positiveSide ? positive : negative;
        if (side.Groups.Count == 0)
        {
            return null;
        }
        AddCap(side, positiveSide ? -plane.normal : plane.normal);

        var bounds = new Bounds(side.Positions[0], Vector3.zero);
        foreach (Vector3 p in side.Positions)
        {
            bounds.Encapsulate(p);
        }
        Vector3 center = bounds.center;
        var vertices = new Vector3[side.Positions.Count];
        for (int i = 0; i < vertices.Length; i++)
        {
            vertices[i] = side.Positions[i] - center;
        }

        var mesh = new Mesh { name = "SaberHalf", indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
        mesh.vertices = vertices;
        mesh.SetNormals(side.Normals);
        mesh.SetUVs(0, side.Uvs);
        bool hasCap = side.Cap.Count > 0;
        mesh.subMeshCount = side.Groups.Count + (hasCap ? 1 : 0);
        var materials = new Material[mesh.subMeshCount];
        for (int g = 0; g < side.Groups.Count; g++)
        {
            mesh.SetTriangles(side.Groups[g].triangles, g);
            materials[g] = side.Groups[g].material;
        }
        if (hasCap)
        {
            mesh.SetTriangles(side.Cap, side.Groups.Count);
            materials[side.Groups.Count] = capMaterial;
        }
        mesh.RecalculateBounds();
        return (mesh, materials, center);
    }

    /// <summary>
    /// Closes each cut-open shape with a fan around the middle of its own cross-section, facing out of this side.
    /// Cut edges are grouped by the outline they belong to (edges that share an end point), so separate parts of the
    /// model (a cart's roof, its wheels, its body) each get their own small cap instead of one fan spanning them all.
    /// </summary>
    private void AddCap(Side side, Vector3 outward)
    {
        int count = cutEdges.Count;
        if (count == 0)
        {
            return;
        }
        // Union-find over edges, joined where end points coincide (to within 2 mm).
        var parent = new int[count];
        for (int i = 0; i < count; i++)
        {
            parent[i] = i;
        }
        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }
            return i;
        }
        var owner = new Dictionary<(int, int, int), int>();
        void Join(Vector3 p, int edge)
        {
            var key = (Mathf.RoundToInt(p.x * 500f), Mathf.RoundToInt(p.y * 500f), Mathf.RoundToInt(p.z * 500f));
            if (owner.TryGetValue(key, out int other))
            {
                parent[Find(edge)] = Find(other);
            }
            else
            {
                owner[key] = edge;
            }
        }
        for (int i = 0; i < count; i++)
        {
            Join(cutEdges[i].a, i);
            Join(cutEdges[i].b, i);
        }

        var sums = new Dictionary<int, (Vector3 sum, int points)>();
        for (int i = 0; i < count; i++)
        {
            int root = Find(i);
            sums.TryGetValue(root, out (Vector3 sum, int points) entry);
            sums[root] = (entry.sum + cutEdges[i].a + cutEdges[i].b, entry.points + 2);
        }
        for (int i = 0; i < count; i++)
        {
            (Vector3 a, Vector3 b) = cutEdges[i];
            (Vector3 sum, int points) = sums[Find(i)];
            Vector3 middle = sum / points;
            bool flip = Vector3.Dot(Vector3.Cross(a - middle, b - middle), outward) < 0f;
            side.Add(side.Cap, middle, outward, new Vector2(0.5f, 0.5f));
            side.Add(side.Cap, flip ? b : a, outward, Vector2.zero);
            side.Add(side.Cap, flip ? a : b, outward, Vector2.one);
        }
    }
}

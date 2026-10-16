using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack.Items;

/// <summary>
/// Zord's body as separate parts (zord.obj has one group per part, with a #pivot comment for the joint it turns
/// about), loaded once and assembled into a posable hierarchy for every Zord on the course.
/// </summary>
internal static class ZordParts
{
    // Same order as the Blender script's material list.
    private static readonly string[] MaterialOrder = { "Red", "Black", "Blue", "Yellow", "Pink", "Silver", "Gold", "Visor", "White" };

    private static readonly Dictionary<string, Mesh> meshes = new();
    private static readonly Dictionary<string, Vector3> pivots = new();
    private static ModelAsset materialSource;
    private static bool loaded;

    public static Material[] Materials => materialSource?.Materials;

    public static Vector3 Pivot(string part) => pivots.TryGetValue(part, out Vector3 p) ? p : Vector3.zero;

    public static Mesh MeshOf(string part) => meshes.TryGetValue(part, out Mesh m) ? m : null;

    /// <summary>Loads the meshes (once) and builds the materials from the game's toon shader (needs a template material).</summary>
    public static bool EnsureReady(string directory)
    {
        if (!loaded)
        {
            loaded = true;
            try
            {
                Load(Path.Combine(directory, "zord.obj"), Path.Combine(directory, "zord.mtl"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to load the Zord model from {directory}: {e}");
            }
        }
        if (meshes.Count == 0)
        {
            return false;
        }
        if (materialSource?.Materials == null && ModelAsset.SharedTemplate != null)
        {
            materialSource.InitMaterials(ModelAsset.SharedTemplate);
        }
        return materialSource?.Materials != null;
    }

    private static void Load(string objPath, string mtlPath)
    {
        Dictionary<string, Color> mtl = ObjLoader.LoadMtl(mtlPath);
        var colors = new Color[MaterialOrder.Length];
        for (int i = 0; i < colors.Length; i++)
        {
            colors[i] = mtl.TryGetValue(MaterialOrder[i], out Color c) ? c : Color.magenta;
        }
        materialSource = ModelAsset.FromColors("zord", colors);

        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var rawPivots = new Dictionary<string, Vector3>();

        string partName = null;
        Vector3 partPivot = Vector3.zero;
        List<Vector3> vertices = null;
        List<Vector3> vertexNormals = null;
        Dictionary<(int, int), int> lookup = null;
        List<int>[] triangles = null;
        int material = 0;

        void Finish()
        {
            if (partName == null || vertices.Count == 0)
            {
                return;
            }
            var mesh = new Mesh { indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.name = $"zord_{partName}";
            mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
            mesh.SetVertices(vertices);
            mesh.SetNormals(vertexNormals);
            mesh.subMeshCount = triangles.Length;
            for (int i = 0; i < triangles.Length; i++)
            {
                mesh.SetTriangles(triangles[i], i);
            }
            mesh.RecalculateBounds();
            meshes[partName] = mesh;
            pivots[partName] = partPivot;
        }

        foreach (string raw in File.ReadLines(objPath))
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (line[0] == '#')
            {
                // OBJ is right-handed and Unity left-handed, so X is mirrored (same as ObjLoader).
                if (parts.Length == 5 && parts[0] == "#pivot")
                {
                    rawPivots[parts[1]] = new Vector3(-Parse(parts[2]), Parse(parts[3]), Parse(parts[4]));
                }
                continue;
            }
            switch (parts[0])
            {
                case "o":
                    Finish();
                    partName = parts[1];
                    partPivot = rawPivots.TryGetValue(partName, out Vector3 pv) ? pv : Vector3.zero;
                    vertices = new List<Vector3>();
                    vertexNormals = new List<Vector3>();
                    lookup = new Dictionary<(int, int), int>();
                    triangles = new List<int>[MaterialOrder.Length];
                    for (int i = 0; i < triangles.Length; i++)
                    {
                        triangles[i] = new List<int>();
                    }
                    break;
                case "v":
                    positions.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "vn":
                    normals.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "usemtl":
                    material = Array.IndexOf(MaterialOrder, parts[1]);
                    if (material < 0)
                    {
                        material = 0;
                    }
                    break;
                case "f":
                    var corner = new int[3];
                    for (int c = 0; c < 3; c++)
                    {
                        string[] idx = parts[c + 1].Split('/');
                        int vi = int.Parse(idx[0], CultureInfo.InvariantCulture) - 1;
                        int ni = idx.Length > 2 && idx[2].Length > 0 ? int.Parse(idx[2], CultureInfo.InvariantCulture) - 1 : -1;
                        if (!lookup.TryGetValue((vi, ni), out int index))
                        {
                            index = vertices.Count;
                            vertices.Add(positions[vi] - partPivot);
                            vertexNormals.Add(ni >= 0 ? normals[ni] : Vector3.up);
                            lookup[(vi, ni)] = index;
                        }
                        corner[c] = index;
                    }
                    // Mirroring X flips the winding.
                    triangles[material].Add(corner[0]);
                    triangles[material].Add(corner[2]);
                    triangles[material].Add(corner[1]);
                    break;
            }
        }
        Finish();
        Plugin.Log.LogInfo($"Loaded Zord: {meshes.Count} parts.");
    }

    private static float Parse(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>One Megazord: the part hierarchy (standing pose at scale 1), ready to be posed by <see cref="ZordView"/>.</summary>
internal sealed class ZordRig
{
    // Leg bone lengths (meters at scale 1), from the Blender model.
    public const float Thigh = 0.56f, Shin = 0.62f, AnkleHeight = 0.12f, HipHeight = 1.30f;

    public Transform Root;
    public Transform Pelvis, Torso, Head;
    // Index 0 = left (-x), 1 = right (+x).
    public readonly Transform[] UpperArms = new Transform[2], ForeArms = new Transform[2];
    public readonly Transform[] Thighs = new Transform[2], Shins = new Transform[2], Feet = new Transform[2];
    /// <summary>Where riders' hips go (children of the torso): the cockpit.</summary>
    public readonly Transform[] Seats = new Transform[2];

    public static ZordRig Build(Transform parent, float scale)
    {
        var rig = new ZordRig();
        var root = new GameObject("Megazord");
        root.transform.SetParent(parent, worldPositionStays: false);
        root.transform.localScale = Vector3.one * scale;
        rig.Root = root.transform;

        rig.Pelvis = Part("pelvis", rig.Root, Vector3.zero);
        rig.Torso = Part("torso", rig.Pelvis, ZordParts.Pivot("pelvis"));
        rig.Head = Part("head", rig.Torso, ZordParts.Pivot("torso"));
        for (int side = 0; side < 2; side++)
        {
            string s = side == 0 ? "L" : "R";
            rig.UpperArms[side] = Part("upperArm" + s, rig.Torso, ZordParts.Pivot("torso"));
            rig.ForeArms[side] = Part("foreArm" + s, rig.UpperArms[side], ZordParts.Pivot("upperArm" + s));
            rig.Thighs[side] = Part("thigh" + s, rig.Pelvis, ZordParts.Pivot("pelvis"));
            rig.Shins[side] = Part("shin" + s, rig.Thighs[side], ZordParts.Pivot("thigh" + s));
            rig.Feet[side] = Part("foot" + s, rig.Shins[side], ZordParts.Pivot("shin" + s));

            var seat = new GameObject("Seat" + s).transform;
            seat.SetParent(rig.Torso, worldPositionStays: false);
            seat.localPosition = new Vector3(side == 0 ? -0.15f : 0.15f, 0.45f, -0.05f);
            rig.Seats[side] = seat;
        }
        return rig;
    }

    private static Transform Part(string name, Transform parent, Vector3 parentPivot)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localPosition = ZordParts.Pivot(name) - parentPivot;
        Mesh mesh = ZordParts.MeshOf(name);
        if (mesh != null)
        {
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = ZordParts.Materials ?? Array.Empty<Material>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
        }
        return go.transform;
    }
}

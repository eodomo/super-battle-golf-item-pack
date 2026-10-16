using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;

namespace ItemPack.Items;

/// <summary>
/// The Megazord's body as separate parts (zord.obj has one group per part, with a #pivot comment for the joint it turns
/// about, and UVs into one hand-painted atlas, zord_tex.png), loaded once and assembled into a posable hierarchy for
/// every Megazord on the course. Its material is a clone of a game cosmetic's toon material, so lighting and shading
/// match the rest of the game.
/// </summary>
internal static class ZordParts
{
    // A textured cosmetic the game itself ships: its material is what we clone for the painted atlas.
    private const string TemplateSource = "Assets/Prefabs/Cosmetics/Clubs/Metadata/Katana.asset";

    private static readonly Dictionary<string, Mesh> meshes = new();
    private static readonly Dictionary<string, Vector3> pivots = new();
    private static Material material;
    private static Material[] materialArray;
    private static Material template;
    private static bool templateRequested, templateFailed, loaded;

    public static Material[] Materials => materialArray;

    public static Vector3 Pivot(string part) => pivots.TryGetValue(part, out Vector3 p) ? p : Vector3.zero;

    public static Mesh MeshOf(string part) => meshes.TryGetValue(part, out Mesh m) ? m : null;

    /// <summary>Starts loading the game's textured toon material (needs Addressables to be ready).</summary>
    public static void Prebuild()
    {
        if (templateRequested)
        {
            return;
        }
        templateRequested = true;
        try
        {
            Addressables.LoadAssetAsync<PlayerCosmeticsMetadata>(TemplateSource).Completed += metaHandle =>
            {
                if (metaHandle.Result == null)
                {
                    templateFailed = true;
                    return;
                }
                Addressables.LoadAssetAsync<GameObject>(metaHandle.Result.model.RuntimeKey).Completed += prefabHandle =>
                {
                    MeshRenderer renderer = prefabHandle.Result != null ? prefabHandle.Result.GetComponentInChildren<MeshRenderer>() : null;
                    template = renderer != null ? renderer.sharedMaterial : null;
                    templateFailed = template == null;
                };
            };
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Megazord: template material request failed: {e}");
            templateFailed = true;
        }
    }

    /// <summary>Loads the meshes and texture (once) and builds the material; false until the template material is ready.</summary>
    public static bool EnsureReady(string directory)
    {
        if (!loaded)
        {
            loaded = true;
            try
            {
                Load(Path.Combine(directory, "zord.obj"));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to load the Megazord model from {directory}: {e}");
            }
        }
        if (meshes.Count == 0)
        {
            return false;
        }
        if (material == null)
        {
            Material source = template != null ? template : (templateFailed ? ModelAsset.SharedTemplate : null);
            if (source == null)
            {
                return false;
            }
            material = BuildMaterial(source, Path.Combine(directory, "zord_tex.png"));
            materialArray = new[] { material };
        }
        return true;
    }

    private static Material BuildMaterial(Material source, string texturePath)
    {
        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true)
        {
            name = "zord_tex",
            filterMode = FilterMode.Bilinear,
            anisoLevel = 4,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        if (!texture.LoadImage(File.ReadAllBytes(texturePath)))
        {
            Plugin.Log.LogError($"Could not read {texturePath}");
        }
        var mat = new Material(source) { name = "Megazord", hideFlags = HideFlags.DontUnloadUnusedAsset };
        foreach (string prop in new[] { "_MainTex", "_BaseMap", "_Albedo", "_AlbedoMap" })
        {
            if (mat.HasProperty(prop))
            {
                mat.SetTexture(prop, texture);
            }
        }
        foreach (string prop in new[] { "_Color", "_BaseColor" })
        {
            if (mat.HasProperty(prop))
            {
                mat.SetColor(prop, Color.white);
            }
        }
        Plugin.Log.LogInfo($"Megazord material built from '{source.name}' (shader {source.shader.name}), texture {texture.width}x{texture.height}.");
        return mat;
    }

    private static void Load(string objPath)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var rawPivots = new Dictionary<string, Vector3>();

        string partName = null;
        Vector3 partPivot = Vector3.zero;
        List<Vector3> vertices = null;
        List<Vector3> vertexNormals = null;
        List<Vector2> vertexUvs = null;
        Dictionary<(int, int, int), int> lookup = null;
        List<int> triangles = null;

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
            mesh.SetUVs(0, vertexUvs);
            mesh.SetTriangles(triangles, 0);
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
                    vertexUvs = new List<Vector2>();
                    lookup = new Dictionary<(int, int, int), int>();
                    triangles = new List<int>();
                    break;
                case "v":
                    positions.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "vn":
                    normals.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "vt":
                    uvs.Add(new Vector2(Parse(parts[1]), Parse(parts[2])));
                    break;
                case "f":
                    var corner = new int[3];
                    for (int c = 0; c < 3; c++)
                    {
                        string[] idx = parts[c + 1].Split('/');
                        int vi = int.Parse(idx[0], CultureInfo.InvariantCulture) - 1;
                        int ti = idx.Length > 1 && idx[1].Length > 0 ? int.Parse(idx[1], CultureInfo.InvariantCulture) - 1 : -1;
                        int ni = idx.Length > 2 && idx[2].Length > 0 ? int.Parse(idx[2], CultureInfo.InvariantCulture) - 1 : -1;
                        if (!lookup.TryGetValue((vi, ti, ni), out int index))
                        {
                            index = vertices.Count;
                            vertices.Add(positions[vi] - partPivot);
                            vertexNormals.Add(ni >= 0 ? normals[ni] : Vector3.up);
                            vertexUvs.Add(ti >= 0 ? uvs[ti] : Vector2.zero);
                            lookup[(vi, ti, ni)] = index;
                        }
                        corner[c] = index;
                    }
                    // Mirroring X flips the winding.
                    triangles.Add(corner[0]);
                    triangles.Add(corner[2]);
                    triangles.Add(corner[1]);
                    break;
            }
        }
        Finish();
        Plugin.Log.LogInfo($"Loaded Megazord: {meshes.Count} parts.");
    }

    private static float Parse(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}

/// <summary>One Megazord: the part hierarchy (standing pose at scale 1), ready to be posed by <see cref="ZordView"/>.</summary>
internal sealed class ZordRig
{
    // Leg bone lengths (meters at scale 1), from the Blender model.
    public const float Thigh = 0.56f, Shin = 0.62f, AnkleHeight = 0.12f, HipHeight = 1.30f;

    public Transform Root;
    public Transform Pelvis, Torso, Head, Sword;
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
        rig.Sword = Part("sword", rig.ForeArms[1], ZordParts.Pivot("foreArmR"));
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

using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AddressableAssets;

namespace ItemPack;

/// <summary>
/// The Power Sword: a club cosmetic. A club model is just a mesh the game swaps in for the golf club, and every
/// color variation re-textures it, so the sword's materials become cells of a small palette texture whose first
/// cell takes the ranger's color.
/// </summary>
internal static class RangerSword
{
    private const string TemplateSource = "Assets/Prefabs/Cosmetics/Clubs/Metadata/Katana.asset";
    private static readonly Color[] FixedCells =
    {
        Color.clear, // accent: replaced per variation
        new Color(0.80f, 0.84f, 0.90f),
        new Color(0.93f, 0.72f, 0.16f),
        new Color(0.10f, 0.10f, 0.12f),
    };

    private static Once<GameObject> prefab;

    public static void Register(string pluginDir)
    {
        string dir = Path.Combine(pluginDir, "PowerRanger");
        prefab = new Once<GameObject>(done => Build(dir, done));

        const string name = "Power Sword";
        string modelGuid = CosmeticCatalog.MakeGuid("model:" + name);
        Sprite icon = RangerIcons.FromFile(Path.Combine(dir, "sword_icon.png")) ?? RangerIcons.Make(name, RangerSet.Colors[0].Suit);
        PlayerCosmeticsMetadata md = RangerSet.NewMetadata(name, PlayerCosmeticsMetadata.Category.Club, icon);
        md.model = new AssetReference(modelGuid);
        var variations = new List<PlayerCosmeticsMetadata.Variation>();
        foreach (RangerColor color in RangerSet.Colors)
        {
            string textureGuid = CosmeticCatalog.MakeGuid($"tex:{name}:{color.Name}");
            variations.Add(RangerSet.NewVariation(color, textureGuid));
            RangerColor captured = color;
            CosmeticCatalog.AddTexture(textureGuid, () => Palette(captured.Suit, $"{name}_{captured.Name}"));
        }
        md.variations = variations.ToArray();
        CosmeticCatalog.AddModel(modelGuid, deliver => prefab.Get(deliver));
        CosmeticCatalog.AddMetadata($"ItemPack/Cosmetics/{name}", md);
    }

    public static void Prebuild() => prefab.Get(_ => { });

    private static Texture2D Palette(Color accent, string name)
    {
        var texture = new Texture2D(FixedCells.Length, 1, TextureFormat.RGBA32, false)
        {
            name = name,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            hideFlags = HideFlags.DontUnloadUnusedAsset,
        };
        for (int i = 0; i < FixedCells.Length; i++)
        {
            texture.SetPixel(i, 0, i == 0 ? accent : FixedCells[i]);
        }
        texture.Apply(false, false);
        return texture;
    }

    private static void Build(string dir, Action<GameObject> done)
    {
        try
        {
            Addressables.LoadAssetAsync<PlayerCosmeticsMetadata>(TemplateSource).Completed += metaHandle =>
            {
                if (metaHandle.Result == null)
                {
                    done(null);
                    return;
                }
                Addressables.LoadAssetAsync<GameObject>(metaHandle.Result.model.RuntimeKey).Completed += prefabHandle =>
                {
                    try
                    {
                        MeshRenderer template = prefabHandle.Result != null ? prefabHandle.Result.GetComponentInChildren<MeshRenderer>() : null;
                        if (template == null)
                        {
                            Plugin.Log.LogError("Power Sword: template club has no renderer.");
                            done(null);
                            return;
                        }
                        Mesh mesh = Flatten(ObjLoader.Load(Path.Combine(dir, "sword.obj"), Path.Combine(dir, "sword.mtl"), out _));
                        var material = new Material(template.sharedMaterial) { name = "PowerSword" };
                        material.SetTexture("_MainTex", Palette(RangerSet.Colors[0].Suit, "PowerSword_default"));
                        material.SetColor("_Color", Color.white);

                        var root = new GameObject("Golf club PowerSword");
                        root.transform.position = new Vector3(0f, -10000f, 0f); // active (the club path never activates its copy), but parked out of sight
                        root.hideFlags = HideFlags.HideAndDontSave;
                        UnityEngine.Object.DontDestroyOnLoad(root);
                        root.AddComponent<MeshFilter>().sharedMesh = mesh;
                        root.AddComponent<MeshRenderer>().sharedMaterial = material;
                        Plugin.Log.LogInfo($"Power Sword built: {mesh.vertexCount} verts.");
                        done(root);
                    }
                    catch (Exception e)
                    {
                        Plugin.Log.LogError($"Power Sword build failed: {e}");
                        done(null);
                    }
                };
            };
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Power Sword load failed: {e}");
            done(null);
        }
    }

    /// <summary>Turns a multi-submesh mesh into one submesh whose UVs point at the palette cell of each original submesh.</summary>
    private static Mesh Flatten(Mesh source)
    {
        int cells = FixedCells.Length;
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        Vector3[] sourceVertices = source.vertices;
        Vector3[] sourceNormals = source.normals;
        for (int s = 0; s < source.subMeshCount; s++)
        {
            var remap = new Dictionary<int, int>();
            var uv = new Vector2((Mathf.Min(s, cells - 1) + 0.5f) / cells, 0.5f);
            foreach (int index in source.GetTriangles(s))
            {
                if (!remap.TryGetValue(index, out int mapped))
                {
                    mapped = vertices.Count;
                    remap[index] = mapped;
                    vertices.Add(sourceVertices[index]);
                    normals.Add(sourceNormals[index]);
                    uvs.Add(uv);
                }
                triangles.Add(mapped);
            }
        }
        var mesh = new Mesh { name = "Club_PowerSword", hideFlags = HideFlags.DontUnloadUnusedAsset };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        return mesh;
    }
}

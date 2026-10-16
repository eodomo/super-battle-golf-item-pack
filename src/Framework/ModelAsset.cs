using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack;

/// <summary>
/// An item's Blender-exported mesh (.obj/.mtl, one submesh per material) and HUD icon (_icon.png),
/// with materials built from the game's own toon shader.
/// </summary>
internal class ModelAsset
{
    public string Name { get; private set; }
    public Mesh Mesh { get; private set; }
    public Sprite Icon { get; private set; }

    private Color[] submeshColors = Array.Empty<Color>();
    private Material[] materials;

    private static readonly string[] ColorProperties = { "_BaseColor", "_Color", "_MainColor", "_Tint", "_AlbedoColor" };
    private static readonly string[] TextureProperties = { "_BaseMap", "_MainTex", "_Albedo", "_AlbedoMap" };
    private static bool loggedShader;

    /// <summary>The game material the first item's materials were cloned from; extra models can reuse it.</summary>
    public static Material SharedTemplate { get; private set; }

    public bool HasMaterials => materials != null;

    /// <summary>The materials (one per submesh color) once <see cref="InitMaterials"/> has run.</summary>
    public Material[] Materials => materials;

    /// <summary>A model that is only a set of colors (its meshes are loaded by the caller), e.g. a multi-part character.</summary>
    public static ModelAsset FromColors(string name, Color[] colors) => new ModelAsset { Name = name, submeshColors = colors };

    public static ModelAsset Load(string directory, string name, bool loadIcon = true, bool loadMesh = true)
    {
        var asset = new ModelAsset { Name = name };
        if (loadMesh)
        {
            try
            {
                asset.Mesh = ObjLoader.Load(Path.Combine(directory, $"{name}.obj"), Path.Combine(directory, $"{name}.mtl"), out asset.submeshColors);
                asset.Mesh.name = $"{name}_mesh";
                asset.Mesh.hideFlags = HideFlags.DontUnloadUnusedAsset;
                Plugin.Log.LogInfo($"Loaded {name} mesh: {asset.Mesh.vertexCount} verts, {asset.Mesh.subMeshCount} submeshes, bounds {asset.Mesh.bounds.size}");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Failed to load {name} mesh from {directory}: {e}");
            }
        }

        if (!loadIcon)
        {
            return asset;
        }
        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true);
            texture.LoadImage(File.ReadAllBytes(Path.Combine(directory, $"{name}_icon.png")));
            texture.name = $"{name}_icon";
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            asset.Icon = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            asset.Icon.name = texture.name;
            asset.Icon.hideFlags = HideFlags.DontUnloadUnusedAsset;
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Failed to load {name} icon from {directory}: {e}");
        }
        return asset;
    }

    /// <summary>Creates materials by cloning a material from the game (so it uses the game's shader) and recoloring it.</summary>
    public void InitMaterials(Material template)
    {
        if (materials != null)
        {
            return;
        }
        if (template != null)
        {
            SharedTemplate ??= template;
            LogShaderProperties(template);
        }
        else
        {
            Plugin.Log.LogWarning($"No template material found for {Name}; using URP/Lit.");
        }

        materials = new Material[submeshColors.Length];
        for (int i = 0; i < submeshColors.Length; i++)
        {
            Material mat = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = $"{Name}_{i}";
            mat.hideFlags = HideFlags.DontUnloadUnusedAsset;
            foreach (string prop in TextureProperties)
            {
                if (mat.HasProperty(prop))
                {
                    mat.SetTexture(prop, Texture2D.whiteTexture);
                }
            }
            foreach (string prop in ColorProperties)
            {
                if (mat.HasProperty(prop))
                {
                    mat.SetColor(prop, submeshColors[i]);
                }
            }
            // Templates are the game's toon materials: give them matching shade colors and switch off
            // item-specific features (e.g. the landmine's blinking emissive, normal map, team tint mask).
            SetColorIfPresent(mat, "_1st_ShadeColor", Shade(submeshColors[i], 0.78f));
            SetColorIfPresent(mat, "_2nd_ShadeColor", Shade(submeshColors[i], 0.6f));
            SetFloatIfPresent(mat, "_EmissiveEnable", 0f);
            SetColorIfPresent(mat, "_EmissiveColor", Color.black);
            SetFloatIfPresent(mat, "_UseNormalMap", 0f);
            SetFloatIfPresent(mat, "_TintMaskEnable", 0f);
            materials[i] = mat;
        }
    }

    public GameObject CreateModel(Transform parent, float scale)
    {
        var go = new GameObject($"{Name}_model");
        go.transform.SetParent(parent, worldPositionStays: false);
        go.transform.localScale = Vector3.one * scale;
        go.AddComponent<MeshFilter>().sharedMesh = Mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials ?? Array.Empty<Material>();
        renderer.shadowCastingMode = ShadowCastingMode.On;
        return go;
    }

    private static Color Shade(Color c, float factor) => new Color(c.r * factor, c.g * factor * 0.95f, c.b * factor * 0.85f, 1f);

    private static void SetColorIfPresent(Material mat, string prop, Color value)
    {
        if (mat.HasProperty(prop))
        {
            mat.SetColor(prop, value);
        }
    }

    private static void SetFloatIfPresent(Material mat, string prop, float value)
    {
        if (mat.HasProperty(prop))
        {
            mat.SetFloat(prop, value);
            mat.SetInt(prop, (int)value);
        }
    }

    private static void LogShaderProperties(Material template)
    {
        if (loggedShader)
        {
            return;
        }
        loggedShader = true;
        Shader shader = template.shader;
        var names = new List<string>();
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            names.Add($"{shader.GetPropertyName(i)}:{shader.GetPropertyType(i)}");
        }
        Plugin.Log.LogDebug($"Template material '{template.name}' uses shader '{shader.name}' with properties: {string.Join(", ", names)}");
    }
}

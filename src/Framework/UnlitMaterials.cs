using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack;

/// <summary>
/// Unlit materials for effects (glows, beams, render-texture screens). A built game can't compile new
/// shaders, so these use unlit shaders the game ships with (checked against its data files).
/// </summary>
internal static class UnlitMaterials
{
    private static readonly string[] OpaqueShaders = { "Universal Render Pipeline/Unlit", "Unlit/Texture" };
    private static readonly string[] TransparentShaders = { "Universal Render Pipeline/Particles/Unlit", "Sprites/Default", "Unlit/Transparent" };
    private static readonly Dictionary<string, Shader> cache = new();

    /// <summary>Opaque unlit material showing <paramref name="texture"/> tinted by <paramref name="color"/>.</summary>
    public static Material Opaque(Texture texture, Color color)
    {
        Shader shader = FindFirst(OpaqueShaders);
        var mat = new Material(shader) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        SetTexture(mat, texture != null ? texture : Texture2D.whiteTexture);
        SetColor(mat, color);
        return mat;
    }

    /// <summary>Additive (glowing) unlit material; colors add light on top of what's behind.</summary>
    public static Material Additive(Texture texture, Color color)
    {
        Shader shader = FindFirst(TransparentShaders);
        var mat = new Material(shader) { hideFlags = HideFlags.DontUnloadUnusedAsset };
        SetTexture(mat, texture != null ? texture : Texture2D.whiteTexture);
        SetColor(mat, color);
        if (shader.name.StartsWith("Universal Render Pipeline/Particles"))
        {
            // URP particle shader: transparent surface, additive blend.
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 2f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.One);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        else
        {
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.renderQueue = (int)RenderQueue.Transparent;
        }
        return mat;
    }

    public static void SetTexture(Material mat, Texture texture)
    {
        if (mat.HasProperty("_BaseMap"))
        {
            mat.SetTexture("_BaseMap", texture);
        }
        if (mat.HasProperty("_MainTex"))
        {
            mat.SetTexture("_MainTex", texture);
        }
    }

    public static void SetColor(Material mat, Color color)
    {
        if (mat.HasProperty("_BaseColor"))
        {
            mat.SetColor("_BaseColor", color);
        }
        if (mat.HasProperty("_Color"))
        {
            mat.SetColor("_Color", color);
        }
    }

    /// <summary>A soft round dot, white with alpha falling off to the edge (for particles and glows).</summary>
    public static Texture2D SoftDot
    {
        get
        {
            if (softDot == null)
            {
                const int size = 64;
                softDot = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true) { name = "ItemPackSoftDot", hideFlags = HideFlags.DontUnloadUnusedAsset };
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + 0.5f) / size * 2f - 1f;
                        float dy = (y + 0.5f) / size * 2f - 1f;
                        float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                        softDot.SetPixel(x, y, new Color(1f, 1f, 1f, a * a));
                    }
                }
                softDot.Apply();
            }
            return softDot;
        }
    }

    private static Texture2D softDot;

    private static Shader FindFirst(string[] names)
    {
        foreach (string name in names)
        {
            Shader shader = Find(name);
            if (shader != null)
            {
                return shader;
            }
        }
        Plugin.Log.LogWarning($"None of the unlit shaders [{string.Join(", ", names)}] were found.");
        return Shader.Find("Hidden/InternalErrorShader");
    }

    private static Shader Find(string name)
    {
        if (cache.TryGetValue(name, out Shader cached) && cached != null)
        {
            return cached;
        }
        // Shader.Find only sees shaders that are always included or already loaded; also search loaded objects.
        Shader shader = Shader.Find(name);
        if (shader == null)
        {
            foreach (Shader candidate in Resources.FindObjectsOfTypeAll<Shader>())
            {
                if (candidate.name == name)
                {
                    shader = candidate;
                    break;
                }
            }
        }
        if (shader != null)
        {
            cache[name] = shader;
        }
        return shader;
    }
}

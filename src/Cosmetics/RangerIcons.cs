using System.IO;
using UnityEngine;

namespace ItemPack;

/// <summary>Menu icons: the helmet's comes from a Blender render, the suit's and boots' are drawn here.</summary>
internal static class RangerIcons
{
    private const int Size = 128;

    public static Sprite FromFile(string path)
    {
        try
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, true);
            texture.LoadImage(File.ReadAllBytes(path));
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return ToSprite(texture, Path.GetFileNameWithoutExtension(path));
        }
        catch (System.Exception e)
        {
            Plugin.Log.LogWarning($"Icon {path} not loaded: {e.Message}");
            return null;
        }
    }

    private static Sprite ToSprite(Texture2D texture, string name)
    {
        Sprite sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        sprite.name = name;
        sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return sprite;
    }

    public static Sprite Make(string name, Color color)
    {
        var pixels = new Color[Size * Size];
        Color white = new Color(0.96f, 0.96f, 0.98f), gold = new Color(0.93f, 0.72f, 0.16f);
        bool boots = name.Contains("Boots");

        void Rect(int x0, int y0, int x1, int y1, Color c)
        {
            for (int y = Mathf.Max(0, y0); y < Mathf.Min(Size, y1); y++)
            {
                for (int x = Mathf.Max(0, x0); x < Mathf.Min(Size, x1); x++)
                {
                    pixels[y * Size + x] = c;
                }
            }
        }

        if (boots)
        {
            for (int i = 0; i < 2; i++)
            {
                int ox = i * 56;
                Rect(14 + ox, 24, 42 + ox, 100, white);
                Rect(14 + ox, 24, 56 + ox, 56, white);
                Rect(14 + ox, 78, 42 + ox, 100, color);
                Rect(14 + ox, 18, 56 + ox, 26, new Color(0.72f, 0.76f, 0.82f));
            }
        }
        else
        {
            Rect(36, 36, 92, 80, color); // torso
            Rect(14, 40, 36, 52, color); // arms
            Rect(92, 40, 114, 52, color);
            Rect(8, 36, 20, 56, white); // gloves
            Rect(108, 36, 120, 56, white);
            Rect(38, 6, 62, 36, color); // legs
            Rect(66, 6, 90, 36, color);
            Rect(36, 72, 92, 80, gold); // belt
            Rect(60, 70, 68, 82, new Color(0.72f, 0.76f, 0.82f));
            for (int i = 0; i < 18; i++) // chest chevron
            {
                Rect(64 - 2 * i - 3, 74 + i, 64 - 2 * i + 3, 75 + i, white);
                Rect(64 + 2 * i - 3, 74 + i, 64 + 2 * i + 3, 75 + i, white);
            }
            Rect(52, 80, 76, 96, color); // neck / head stub
        }

        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, true);
        texture.SetPixels(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return ToSprite(texture, name + "_icon");
    }
}

using System;
using System.Collections.Generic;
using ItemPack.Items;
using UnityEngine;

namespace ItemPack;

/// <summary>Runs an async builder once and hands its result to everyone who asks.</summary>
internal sealed class Once<T> where T : class
{
    private readonly Action<Action<T>> start;
    private List<Action<T>> waiting;
    private bool started;
    private bool failed;
    public T Value { get; private set; }

    public Once(Action<Action<T>> start) => this.start = start;

    public void Get(Action<T> callback)
    {
        if (Value != null || failed)
        {
            callback(Value);
            return;
        }
        (waiting ??= new List<Action<T>>()).Add(callback);
        if (started)
        {
            return;
        }
        started = true;
        start(result =>
        {
            Value = result;
            failed = result == null;
            List<Action<T>> list = waiting;
            waiting = null;
            foreach (Action<T> action in list)
            {
                action(result);
            }
        });
    }
}

/// <summary>
/// Paints a texture in a mesh's own UV layout from a 3D function: every texel gets a region id from the position,
/// normal and "hand-ness" of the surface point it covers. Colors are assigned per region afterwards, so one bake
/// serves every color variation.
/// </summary>
internal static class UvBake
{
    public const byte Unpainted = 255;

    public delegate byte RegionFunc(Vector3 position, Vector3 normal, float weight);

    public static byte[] Regions(MeshCopy mesh, int size, float[] vertexWeight, RegionFunc region)
    {
        var map = new byte[size * size];
        for (int i = 0; i < map.Length; i++)
        {
            map[i] = Unpainted;
        }
        foreach (int[] tris in mesh.Triangles)
        {
            for (int t = 0; t + 2 < tris.Length; t += 3)
            {
                int a = tris[t], b = tris[t + 1], c = tris[t + 2];
                Vector2 pa = mesh.Uvs[a] * size, pb = mesh.Uvs[b] * size, pc = mesh.Uvs[c] * size;
                int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.x, Mathf.Min(pb.x, pc.x))) - 1);
                int maxX = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(pa.x, Mathf.Max(pb.x, pc.x))) + 1);
                int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(pa.y, Mathf.Min(pb.y, pc.y))) - 1);
                int maxY = Mathf.Min(size - 1, Mathf.CeilToInt(Mathf.Max(pa.y, Mathf.Max(pb.y, pc.y))) + 1);
                float denom = (pb.y - pc.y) * (pa.x - pc.x) + (pc.x - pb.x) * (pa.y - pc.y);
                if (Mathf.Abs(denom) < 1e-8f)
                {
                    continue;
                }
                for (int y = minY; y <= maxY; y++)
                {
                    for (int x = minX; x <= maxX; x++)
                    {
                        float px = x + 0.5f, py = y + 0.5f;
                        float w0 = ((pb.y - pc.y) * (px - pc.x) + (pc.x - pb.x) * (py - pc.y)) / denom;
                        float w1 = ((pc.y - pa.y) * (px - pc.x) + (pa.x - pc.x) * (py - pc.y)) / denom;
                        float w2 = 1 - w0 - w1;
                        const float Eps = -0.02f;
                        if (w0 < Eps || w1 < Eps || w2 < Eps)
                        {
                            continue;
                        }
                        Vector3 pos = mesh.Positions[a] * w0 + mesh.Positions[b] * w1 + mesh.Positions[c] * w2;
                        Vector3 nrm = mesh.Normals[a] * w0 + mesh.Normals[b] * w1 + mesh.Normals[c] * w2;
                        float weight = vertexWeight[a] * w0 + vertexWeight[b] * w1 + vertexWeight[c] * w2;
                        map[y * size + x] = region(pos, nrm, weight);
                    }
                }
            }
        }
        Dilate(map, size, 4);
        return map;
    }

    private static void Dilate(byte[] map, int size, int iterations)
    {
        for (int it = 0; it < iterations; it++)
        {
            byte[] copy = (byte[])map.Clone();
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (copy[y * size + x] != Unpainted)
                    {
                        continue;
                    }
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = x + (d == 0 ? 1 : d == 1 ? -1 : 0);
                        int ny = y + (d == 2 ? 1 : d == 3 ? -1 : 0);
                        if (nx < 0 || ny < 0 || nx >= size || ny >= size)
                        {
                            continue;
                        }
                        byte neighbour = copy[ny * size + nx];
                        if (neighbour != Unpainted)
                        {
                            map[y * size + x] = neighbour;
                            break;
                        }
                    }
                }
            }
        }
    }

    public static Texture2D ToTexture(byte[] regions, int size, Color[] palette, string name)
    {
        var pixels = new Color32[size * size];
        for (int i = 0; i < pixels.Length; i++)
        {
            byte r = regions[i];
            pixels[i] = r < palette.Length ? (Color32)palette[r] : (Color32)palette[0];
        }
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
        texture.SetPixels32(pixels);
        texture.Apply(true, false);
        texture.name = name;
        texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return texture;
    }
}

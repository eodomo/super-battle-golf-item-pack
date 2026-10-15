using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack;

/// <summary>Minimal OBJ reader for the triangulated, normals-only file our Blender script writes.</summary>
internal static class ObjLoader
{
    public static Mesh Load(string objPath, string mtlPath, out Color[] submeshColors)
    {
        Dictionary<string, Color> mtlColors = LoadMtl(mtlPath);
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var vertices = new List<Vector3>();
        var vertexNormals = new List<Vector3>();
        var vertexLookup = new Dictionary<(int, int), int>();
        var submeshTriangles = new List<List<int>>();
        var submeshNames = new List<string>();
        List<int> current = null;

        foreach (string raw in File.ReadLines(objPath))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "v":
                    // OBJ is right-handed; Unity is left-handed, so mirror X.
                    positions.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "vn":
                    normals.Add(new Vector3(-Parse(parts[1]), Parse(parts[2]), Parse(parts[3])));
                    break;
                case "usemtl":
                    current = new List<int>();
                    submeshTriangles.Add(current);
                    submeshNames.Add(parts[1]);
                    break;
                case "f":
                    if (current == null)
                    {
                        current = new List<int>();
                        submeshTriangles.Add(current);
                        submeshNames.Add("default");
                    }
                    var corner = new int[3];
                    for (int c = 0; c < 3; c++)
                    {
                        string[] idx = parts[c + 1].Split('/');
                        int vi = int.Parse(idx[0], CultureInfo.InvariantCulture) - 1;
                        int ni = idx.Length > 2 && idx[2].Length > 0 ? int.Parse(idx[2], CultureInfo.InvariantCulture) - 1 : -1;
                        if (!vertexLookup.TryGetValue((vi, ni), out int index))
                        {
                            index = vertices.Count;
                            vertices.Add(positions[vi]);
                            vertexNormals.Add(ni >= 0 ? normals[ni] : Vector3.up);
                            vertexLookup[(vi, ni)] = index;
                        }
                        corner[c] = index;
                    }
                    // Mirroring X flips handedness, so reverse the winding to keep faces outward.
                    current.Add(corner[0]);
                    current.Add(corner[2]);
                    current.Add(corner[1]);
                    break;
            }
        }

        var mesh = new Mesh
        {
            indexFormat = vertices.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16
        };
        mesh.SetVertices(vertices);
        mesh.SetNormals(vertexNormals);
        mesh.subMeshCount = submeshTriangles.Count;
        submeshColors = new Color[submeshTriangles.Count];
        for (int i = 0; i < submeshTriangles.Count; i++)
        {
            mesh.SetTriangles(submeshTriangles[i], i);
            submeshColors[i] = mtlColors.TryGetValue(submeshNames[i], out Color color) ? color : Color.magenta;
        }
        mesh.RecalculateBounds();
        return mesh;
    }

    private static Dictionary<string, Color> LoadMtl(string path)
    {
        var colors = new Dictionary<string, Color>();
        if (!File.Exists(path))
        {
            return colors;
        }
        string name = null;
        foreach (string raw in File.ReadLines(path))
        {
            string[] parts = raw.Trim().Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                continue;
            }
            if (parts[0] == "newmtl")
            {
                name = parts[1];
            }
            else if (parts[0] == "Kd" && name != null)
            {
                // Our exporter writes sRGB values, which is what Material.SetColor expects.
                colors[name] = new Color(Parse(parts[1]), Parse(parts[2]), Parse(parts[3]));
            }
        }
        return colors;
    }

    private static float Parse(string s) => float.Parse(s, CultureInfo.InvariantCulture);
}

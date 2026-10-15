using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack.Items;

/// <summary>Triangles of a mesh, read back to the CPU: positions, normals, first UV set, and indices per submesh.</summary>
internal sealed class MeshCopy
{
    public Vector3[] Positions;
    public Vector3[] Normals;
    public Vector2[] Uvs;
    public int[][] Triangles; // per submesh, already offset by the submesh's base vertex
}

/// <summary>
/// Gets a CPU copy of a mesh's geometry. The game ships its meshes non-readable (the CPU copy is thrown away
/// after upload), so for those the data is copied back from the GPU's vertex and index buffers, which takes a
/// frame or two. Results are cached per mesh. The callback gets null if the mesh can't be read.
/// </summary>
internal static class MeshReadback
{
    private static readonly Dictionary<Mesh, MeshCopy> cache = new();
    private static readonly Dictionary<Mesh, List<Action<MeshCopy>>> waiting = new();
    private static readonly HashSet<Mesh> failed = new();

    public static void Request(Mesh mesh, Action<MeshCopy> done)
    {
        if (mesh == null || failed.Contains(mesh))
        {
            done(null);
            return;
        }
        if (cache.TryGetValue(mesh, out MeshCopy copy))
        {
            done(copy);
            return;
        }
        if (waiting.TryGetValue(mesh, out List<Action<MeshCopy>> list))
        {
            list.Add(done);
            return;
        }
        waiting[mesh] = new List<Action<MeshCopy>> { done };
        try
        {
            if (mesh.isReadable)
            {
                Finish(mesh, ReadCpu(mesh));
            }
            else
            {
                ReadGpu(mesh);
            }
        }
        catch (Exception e)
        {
            Plugin.Log.LogWarning($"Can't read mesh {mesh.name}: {e.Message}");
            Finish(mesh, null);
        }
    }

    private static void Finish(Mesh mesh, MeshCopy copy)
    {
        if (copy != null)
        {
            cache[mesh] = copy;
        }
        else
        {
            failed.Add(mesh);
        }
        if (waiting.TryGetValue(mesh, out List<Action<MeshCopy>> list))
        {
            waiting.Remove(mesh);
            foreach (Action<MeshCopy> callback in list)
            {
                try
                {
                    callback(copy);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Mesh readback callback failed: {e}");
                }
            }
        }
    }

    private static MeshCopy ReadCpu(Mesh mesh)
    {
        var copy = new MeshCopy
        {
            Positions = mesh.vertices,
            Normals = mesh.normals,
            Uvs = mesh.uv,
            Triangles = new int[mesh.subMeshCount][],
        };
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            copy.Triangles[i] = mesh.GetSubMesh(i).topology == MeshTopology.Triangles ? mesh.GetTriangles(i) : Array.Empty<int>();
        }
        return copy;
    }

    private struct Attribute
    {
        public bool Present;
        public int Stream, Offset, Dimension;
        public VertexAttributeFormat Format;
    }

    private static Attribute Describe(Mesh mesh, VertexAttribute attribute) =>
        !mesh.HasVertexAttribute(attribute)
            ? default
            : new Attribute
            {
                Present = true,
                Stream = mesh.GetVertexAttributeStream(attribute),
                Offset = mesh.GetVertexAttributeOffset(attribute),
                Dimension = mesh.GetVertexAttributeDimension(attribute),
                Format = mesh.GetVertexAttributeFormat(attribute),
            };

    private static void ReadGpu(Mesh mesh)
    {
        Attribute position = Describe(mesh, VertexAttribute.Position);
        Attribute normal = Describe(mesh, VertexAttribute.Normal);
        Attribute uv = Describe(mesh, VertexAttribute.TexCoord0);
        if (!position.Present)
        {
            throw new InvalidOperationException("no positions");
        }

        // Read every stream we need plus the index buffer; parse once all have arrived.
        var streams = new SortedSet<int> { position.Stream };
        if (normal.Present) streams.Add(normal.Stream);
        if (uv.Present) streams.Add(uv.Stream);
        var streamData = new Dictionary<int, byte[]>();
        byte[] indexData = null;
        int pending = streams.Count + 1;
        bool error = false;
        var buffers = new List<GraphicsBuffer>();

        void Arrived()
        {
            if (--pending > 0)
            {
                return;
            }
            foreach (GraphicsBuffer buffer in buffers)
            {
                buffer.Dispose();
            }
            MeshCopy copy = null;
            if (!error)
            {
                try
                {
                    copy = Parse(mesh, streamData, indexData, position, normal, uv);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Can't parse mesh {mesh.name}: {e.Message}");
                }
            }
            Finish(mesh, copy);
        }

        foreach (int stream in streams)
        {
            GraphicsBuffer vertexBuffer = mesh.GetVertexBuffer(stream);
            buffers.Add(vertexBuffer);
            int captured = stream;
            AsyncGPUReadback.Request(vertexBuffer, request =>
            {
                if (request.hasError)
                {
                    error = true;
                }
                else
                {
                    streamData[captured] = request.GetData<byte>().ToArray();
                }
                Arrived();
            });
        }
        GraphicsBuffer indexBuffer = mesh.GetIndexBuffer();
        buffers.Add(indexBuffer);
        AsyncGPUReadback.Request(indexBuffer, request =>
        {
            if (request.hasError)
            {
                error = true;
            }
            else
            {
                indexData = request.GetData<byte>().ToArray();
            }
            Arrived();
        });
    }

    private static MeshCopy Parse(Mesh mesh, Dictionary<int, byte[]> streams, byte[] indices, Attribute position, Attribute normal, Attribute uv)
    {
        int count = mesh.vertexCount;
        var copy = new MeshCopy
        {
            Positions = new Vector3[count],
            Normals = new Vector3[count],
            Uvs = new Vector2[count],
            Triangles = new int[mesh.subMeshCount][],
        };
        for (int v = 0; v < count; v++)
        {
            copy.Positions[v] = ReadVector(streams, mesh, position, v);
            copy.Normals[v] = normal.Present ? ReadVector(streams, mesh, normal, v) : Vector3.up;
            copy.Uvs[v] = uv.Present ? (Vector2)ReadVector(streams, mesh, uv, v) : Vector2.zero;
        }

        bool wide = mesh.indexFormat == IndexFormat.UInt32;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            SubMeshDescriptor sub = mesh.GetSubMesh(s);
            if (sub.topology != MeshTopology.Triangles)
            {
                copy.Triangles[s] = Array.Empty<int>();
                continue;
            }
            var tris = new int[sub.indexCount];
            for (int i = 0; i < sub.indexCount; i++)
            {
                int at = sub.indexStart + i;
                int index = wide ? (int)BitConverter.ToUInt32(indices, at * 4) : BitConverter.ToUInt16(indices, at * 2);
                tris[i] = Mathf.Clamp(index + sub.baseVertex, 0, count - 1);
            }
            copy.Triangles[s] = tris;
        }
        return copy;
    }

    private static Vector3 ReadVector(Dictionary<int, byte[]> streams, Mesh mesh, Attribute attribute, int vertex)
    {
        byte[] data = streams[attribute.Stream];
        int stride = mesh.GetVertexBufferStride(attribute.Stream);
        int at = vertex * stride + attribute.Offset;
        int size = ComponentSize(attribute.Format);
        Vector3 result = default;
        for (int c = 0; c < Mathf.Min(3, attribute.Dimension); c++)
        {
            result[c] = ReadComponent(data, at + c * size, attribute.Format);
        }
        return result;
    }

    private static int ComponentSize(VertexAttributeFormat format) => format switch
    {
        VertexAttributeFormat.Float32 or VertexAttributeFormat.UInt32 or VertexAttributeFormat.SInt32 => 4,
        VertexAttributeFormat.Float16 or VertexAttributeFormat.UNorm16 or VertexAttributeFormat.SNorm16
            or VertexAttributeFormat.UInt16 or VertexAttributeFormat.SInt16 => 2,
        _ => 1,
    };

    private static float ReadComponent(byte[] data, int at, VertexAttributeFormat format) => format switch
    {
        VertexAttributeFormat.Float32 => BitConverter.ToSingle(data, at),
        VertexAttributeFormat.Float16 => Mathf.HalfToFloat(BitConverter.ToUInt16(data, at)),
        VertexAttributeFormat.UNorm16 => BitConverter.ToUInt16(data, at) / 65535f,
        VertexAttributeFormat.SNorm16 => Mathf.Max(BitConverter.ToInt16(data, at) / 32767f, -1f),
        VertexAttributeFormat.UNorm8 => data[at] / 255f,
        VertexAttributeFormat.SNorm8 => Mathf.Max((sbyte)data[at] / 127f, -1f),
        VertexAttributeFormat.UInt32 => BitConverter.ToUInt32(data, at),
        VertexAttributeFormat.SInt32 => BitConverter.ToInt32(data, at),
        VertexAttributeFormat.UInt16 => BitConverter.ToUInt16(data, at),
        VertexAttributeFormat.SInt16 => BitConverter.ToInt16(data, at),
        VertexAttributeFormat.UInt8 => data[at],
        VertexAttributeFormat.SInt8 => (sbyte)data[at],
        _ => 0f,
    };
}

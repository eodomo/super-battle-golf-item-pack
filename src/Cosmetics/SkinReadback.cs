using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ItemPack;

/// <summary>Reads a non-readable mesh's bone weights (4 per vertex) back from the GPU.</summary>
internal static class SkinReadback
{
    public static void Request(Mesh mesh, Action<BoneWeight[]> done)
    {
        GraphicsBuffer buffer = mesh.GetBoneWeightBuffer(SkinWeights.FourBones);
        if (buffer == null)
        {
            done(null);
            return;
        }
        int count = mesh.vertexCount;
        AsyncGPUReadback.Request(buffer, request =>
        {
            BoneWeight[] result = null;
            if (!request.hasError)
            {
                try
                {
                    result = Parse(request.GetData<byte>().ToArray(), count);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning($"Can't parse bone weights of {mesh.name}: {e.Message}");
                }
            }
            buffer.Dispose();
            done(result);
        });
    }

    /// <summary>Per vertex, 32 bytes: four float weights followed by four int bone indices.</summary>
    private static BoneWeight[] Parse(byte[] data, int count)
    {
        if (data.Length < count * 32)
        {
            throw new InvalidOperationException($"weight buffer too small ({data.Length} bytes for {count} vertices)");
        }
        var result = new BoneWeight[count];
        for (int v = 0; v < count; v++)
        {
            int at = v * 32;
            result[v] = new BoneWeight
            {
                weight0 = BitConverter.ToSingle(data, at),
                weight1 = BitConverter.ToSingle(data, at + 4),
                weight2 = BitConverter.ToSingle(data, at + 8),
                weight3 = BitConverter.ToSingle(data, at + 12),
                boneIndex0 = BitConverter.ToInt32(data, at + 16),
                boneIndex1 = BitConverter.ToInt32(data, at + 20),
                boneIndex2 = BitConverter.ToInt32(data, at + 24),
                boneIndex3 = BitConverter.ToInt32(data, at + 28),
            };
        }
        return result;
    }
}

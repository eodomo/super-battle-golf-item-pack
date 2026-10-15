using System.Collections.Generic;
using System.IO;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

public struct DirtPlaceRequest : NetworkMessage
{
    public Vector3Int cell;
    public byte layer;
}

public struct DirtBreakRequest : NetworkMessage
{
    public Vector3Int cell;
}

public struct DirtPlaced : NetworkMessage
{
    public Vector3Int cell;
    public byte layer;
}

public struct DirtBroken : NetworkMessage
{
    public Vector3Int cell;
}

/// <summary>
/// The blocks in the world, on a shared grid. The host keeps the authoritative set; every client builds the
/// same solid cubes, so players can stand on them and balls bounce off them.
/// </summary>
internal static class DirtWorld
{
    private const double MinPlaceInterval = 0.08;

    private static readonly Dictionary<Vector3Int, GameObject> blocks = new();
    private static readonly Dictionary<Collider, Vector3Int> cellsByCollider = new();
    private static readonly Dictionary<Vector3Int, byte> serverBlocks = new();
    private static readonly Dictionary<int, double> lastPlace = new();
    private static Material material;
    private static Texture2D texture;
    private static bool sceneHookInstalled;

    public static float Size => Mathf.Clamp(DirtBlocksItem.BlockSize.Value, 0.25f, 4f);

    public static Vector3 CellCenter(Vector3Int cell) => ((Vector3)cell + Vector3.one * 0.5f) * Size;

    public static Vector3Int WorldToCell(Vector3 world) =>
        new(Mathf.FloorToInt(world.x / Size), Mathf.FloorToInt(world.y / Size), Mathf.FloorToInt(world.z / Size));

    public static bool TryGetCell(Collider collider, out Vector3Int cell) => cellsByCollider.TryGetValue(collider, out cell);

    public static bool HasBlock(Vector3Int cell) => blocks.ContainsKey(cell);

    // ---- networking ----

    public static void InitSerializers()
    {
        Writer<DirtPlaceRequest>.write = (w, m) => { WriteCell(w, m.cell); w.WriteByte(m.layer); };
        Reader<DirtPlaceRequest>.read = r => new DirtPlaceRequest { cell = ReadCell(r), layer = r.ReadByte() };
        Writer<DirtBreakRequest>.write = (w, m) => WriteCell(w, m.cell);
        Reader<DirtBreakRequest>.read = r => new DirtBreakRequest { cell = ReadCell(r) };
        Writer<DirtPlaced>.write = (w, m) => { WriteCell(w, m.cell); w.WriteByte(m.layer); };
        Reader<DirtPlaced>.read = r => new DirtPlaced { cell = ReadCell(r), layer = r.ReadByte() };
        Writer<DirtBroken>.write = (w, m) => WriteCell(w, m.cell);
        Reader<DirtBroken>.read = r => new DirtBroken { cell = ReadCell(r) };
    }

    private static void WriteCell(NetworkWriter w, Vector3Int c)
    {
        w.WriteInt(c.x);
        w.WriteInt(c.y);
        w.WriteInt(c.z);
    }

    private static Vector3Int ReadCell(NetworkReader r) => new(r.ReadInt(), r.ReadInt(), r.ReadInt());

    public static void RegisterServerHandlers()
    {
        ResetServer();
        NetworkServer.RegisterHandler<DirtPlaceRequest>(OnServerPlace);
        NetworkServer.RegisterHandler<DirtBreakRequest>(OnServerBreak);
    }

    public static void RegisterClientHandlers()
    {
        NetworkClient.ReplaceHandler<DirtPlaced>(msg => Place(msg.cell, msg.layer, withEffects: true));
        NetworkClient.ReplaceHandler<DirtBroken>(msg => Break(msg.cell));
    }

    public static void ResetServer()
    {
        serverBlocks.Clear();
        lastPlace.Clear();
    }

    public static void SendExisting(NetworkConnectionToClient connection)
    {
        foreach (KeyValuePair<Vector3Int, byte> block in serverBlocks)
        {
            connection.Send(new DirtPlaced { cell = block.Key, layer = block.Value });
        }
    }

    public static void RequestPlace(Vector3Int cell, int layer)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new DirtPlaceRequest { cell = cell, layer = (byte)layer });
        }
    }

    public static void RequestBreak(Vector3Int cell)
    {
        if (NetworkClient.active)
        {
            NetworkClient.Send(new DirtBreakRequest { cell = cell });
        }
    }

    private static void OnServerPlace(NetworkConnectionToClient conn, DirtPlaceRequest msg)
    {
        if (conn.identity == null || serverBlocks.ContainsKey(msg.cell) || serverBlocks.Count >= DirtBlocksItem.MaxBlocks.Value)
        {
            return;
        }
        double now = NetworkTime.time;
        if (lastPlace.TryGetValue(conn.connectionId, out double last) && now - last < MinPlaceInterval)
        {
            return;
        }
        Vector3 center = CellCenter(msg.cell);
        if ((conn.identity.transform.position - center).sqrMagnitude > Mathf.Pow(DirtBlocksItem.Reach.Value + 4f, 2f) || IsOccupied(center))
        {
            return;
        }
        lastPlace[conn.connectionId] = now;
        serverBlocks[msg.cell] = msg.layer;
        NetworkServer.SendToAll(new DirtPlaced { cell = msg.cell, layer = msg.layer });
    }

    private static void OnServerBreak(NetworkConnectionToClient conn, DirtBreakRequest msg)
    {
        if (conn.identity != null)
        {
            ServerBreak(msg.cell);
        }
    }

    private static void ServerBreak(Vector3Int cell)
    {
        if (serverBlocks.Remove(cell))
        {
            NetworkServer.SendToAll(new DirtBroken { cell = cell });
        }
    }

    private static readonly List<Vector3Int> toBreak = new();

    /// <summary>Host: breaks every block that overlaps a sphere (an explosion).</summary>
    public static void ServerBreakInSphere(Vector3 center, float radius)
    {
        ServerBreakWhere(cellCenter => (cellCenter - center).sqrMagnitude <= Mathf.Pow(radius + Size * 0.5f, 2f));
    }

    /// <summary>Host: breaks every block that overlaps a capsule from <paramref name="a"/> to <paramref name="b"/> (a beam).</summary>
    public static void ServerBreakInCapsule(Vector3 a, Vector3 b, float radius)
    {
        Vector3 ab = b - a;
        float lengthSquared = Mathf.Max(ab.sqrMagnitude, 0.0001f);
        ServerBreakWhere(cellCenter =>
        {
            float t = Mathf.Clamp01(Vector3.Dot(cellCenter - a, ab) / lengthSquared);
            return (a + ab * t - cellCenter).sqrMagnitude <= Mathf.Pow(radius + Size * 0.5f, 2f);
        });
    }

    private static void ServerBreakWhere(System.Func<Vector3, bool> hit)
    {
        if (!NetworkServer.active || serverBlocks.Count == 0)
        {
            return;
        }
        foreach (Vector3Int cell in serverBlocks.Keys)
        {
            if (hit(CellCenter(cell)))
            {
                toBreak.Add(cell);
            }
        }
        foreach (Vector3Int cell in toBreak)
        {
            ServerBreak(cell);
        }
        toBreak.Clear();
    }

    /// <summary>A shot or swing on this client hit <paramref name="collider"/>: if it's a block, break it.</summary>
    public static bool TryBreakHit(Collider collider)
    {
        if (collider != null && TryGetCell(collider, out Vector3Int cell))
        {
            RequestBreak(cell);
            return true;
        }
        return false;
    }

    /// <summary>Can't place a block inside a player, cart or ball.</summary>
    public static bool IsOccupied(Vector3 center)
    {
        LayerSettings layers = GameManager.LayerSettings;
        int mask = layers.PlayersMask | layers.GolfCartsMask | layers.BallMask | layers.DynamicBallMask;
        return Physics.CheckBox(center, Vector3.one * (Size * 0.47f), Quaternion.identity, mask, QueryTriggerInteraction.Ignore);
    }

    // ---- world ----

    public static void Place(Vector3Int cell, int layer, bool withEffects)
    {
        EnsureSceneHook();
        if (blocks.ContainsKey(cell))
        {
            return;
        }
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        block.name = $"DirtBlock_{cell.x}_{cell.y}_{cell.z}";
        block.layer = layer;
        block.transform.SetPositionAndRotation(CellCenter(cell), Quaternion.identity);
        block.transform.localScale = Vector3.one * Size;
        block.GetComponent<MeshRenderer>().sharedMaterial = Material;
        blocks[cell] = block;
        cellsByCollider[block.GetComponent<Collider>()] = cell;
        if (withEffects)
        {
            DirtSounds.PlayPlace(block.transform.position);
        }
    }

    public static void Break(Vector3Int cell)
    {
        if (!blocks.TryGetValue(cell, out GameObject block))
        {
            return;
        }
        blocks.Remove(cell);
        if (block != null)
        {
            cellsByCollider.Remove(block.GetComponent<Collider>());
            DirtSounds.PlayBreak(block.transform.position);
            DirtParticles.Burst(block.transform.position, Size);
            Object.Destroy(block);
        }
    }

    public static void ClearAll()
    {
        foreach (GameObject block in blocks.Values)
        {
            if (block != null)
            {
                Object.Destroy(block);
            }
        }
        blocks.Clear();
        cellsByCollider.Clear();
    }

    /// <summary>A copy of a block for the player's hand: no collider, smaller.</summary>
    public static GameObject CreateHeldBlock(Transform parent, float scale)
    {
        GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(block.GetComponent<Collider>());
        block.name = "HeldDirtBlock";
        block.transform.SetParent(parent, false);
        block.transform.localScale = Vector3.one * (0.32f * scale);
        block.GetComponent<MeshRenderer>().sharedMaterial = Material;
        return block;
    }

    /// <summary>
    /// The game's own toon material (as used by the other pack items), textured with the dirt texture, so
    /// blocks are lit and shaded like the rest of the world.
    /// </summary>
    public static Material Material
    {
        get
        {
            if (material != null)
            {
                return material;
            }
            Material template = ModelAsset.SharedTemplate;
            material = template != null ? new Material(template) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            material.name = "DirtBlock";
            material.hideFlags = HideFlags.DontUnloadUnusedAsset;
            foreach (string prop in new[] { "_BaseMap", "_MainTex" })
            {
                if (material.HasProperty(prop))
                {
                    material.SetTexture(prop, Texture);
                }
            }
            foreach (string prop in new[] { "_BaseColor", "_Color" })
            {
                if (material.HasProperty(prop))
                {
                    material.SetColor(prop, Color.white);
                }
            }
            SetIfPresent("_1st_ShadeColor", new Color(0.78f, 0.74f, 0.7f));
            SetIfPresent("_2nd_ShadeColor", new Color(0.6f, 0.56f, 0.52f));
            foreach (string toggle in new[] { "_EmissiveEnable", "_UseNormalMap", "_TintMaskEnable" })
            {
                if (material.HasProperty(toggle))
                {
                    material.SetFloat(toggle, 0f);
                }
            }
            return material;

            void SetIfPresent(string prop, Color value)
            {
                if (material.HasProperty(prop))
                {
                    material.SetColor(prop, value);
                }
            }
        }
    }

    /// <summary>16x16 pixel-art dirt (Assets/DirtBlocks/dirt.png), shown with crisp pixels.</summary>
    private static Texture2D Texture
    {
        get
        {
            if (texture != null)
            {
                return texture;
            }
            texture = new Texture2D(16, 16, TextureFormat.RGBA32, mipChain: false) { name = "Dirt" };
            string path = Path.Combine(DirtBlocksItem.Instance.AssetDir, "dirt.png");
            if (!File.Exists(path) || !texture.LoadImage(File.ReadAllBytes(path)))
            {
                var rng = new System.Random(3);
                for (int y = 0; y < 16; y++)
                {
                    for (int x = 0; x < 16; x++)
                    {
                        float v = 0.85f + (float)rng.NextDouble() * 0.3f;
                        texture.SetPixel(x, y, new Color(0.53f * v, 0.36f * v, 0.24f * v));
                    }
                }
                texture.Apply();
            }
            texture.filterMode = FilterMode.Point;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.hideFlags = HideFlags.DontUnloadUnusedAsset;
            return texture;
        }
    }

    private static void EnsureSceneHook()
    {
        if (sceneHookInstalled)
        {
            return;
        }
        sceneHookInstalled = true;
        SceneManager.activeSceneChanged += (_, _) =>
        {
            ClearAll();
            if (NetworkServer.active)
            {
                ResetServer();
            }
        };
    }
}

/// <summary>A puff of dirt bits when a block breaks.</summary>
internal static class DirtParticles
{
    private static Material material;

    public static void Burst(Vector3 position, float size)
    {
        material ??= UnlitMaterials.Particles();
        var go = new GameObject("DirtBurst");
        go.transform.position = position;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.duration = 0.1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.06f * size, 0.14f * size);
        main.gravityModifier = 1.5f;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.3f, 0.19f), new Color(0.6f, 0.42f, 0.28f));
        main.stopAction = ParticleSystemStopAction.Destroy;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = Vector3.one * size;
        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = material;
        renderer.renderMode = ParticleSystemRenderMode.Mesh;
        renderer.mesh = CubeMesh;
        ps.Play();
    }

    private static Mesh cubeMesh;

    private static Mesh CubeMesh
    {
        get
        {
            if (cubeMesh == null)
            {
                GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cubeMesh = cube.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(cube);
            }
            return cubeMesh;
        }
    }
}

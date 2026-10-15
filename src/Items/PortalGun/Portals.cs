using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// One placed portal. Its transform's forward points out of the surface it sits on, so in portal-local
/// space x is right, y is up and +z is "out of the portal".
/// </summary>
internal class Portal
{
    public uint Owner;
    public byte Color;
    public Transform Transform;
    public Portal Partner => Portals.Get(Owner, (byte)(1 - Color));
    public bool IsLinked => Partner != null;

    // Travel (see PortalTravel): the wall it sits on, and the hidden floor inside that wall for walking in.
    public readonly List<Collider> WallColliders = new();
    public BoxCollider Sill;

    // Rendering
    public MeshRenderer Surface;
    public Mesh SurfaceMesh;
    public Vector3[] SurfaceLocalVertices;
    public Vector2[] SurfaceUvs;
    public Material UnlinkedMaterial;
    public Material LinkedMaterial;
    public Camera Camera;
    public RenderTexture Texture;
    public bool Rendering;
    public float OpenedAt;

    public float HalfWidth => PortalGunItem.PortalWidth.Value * 0.5f;
    public float HalfHeight => PortalGunItem.PortalHeight.Value * 0.5f;

    /// <summary>Maps a world point through this portal to the partner's side.</summary>
    public Vector3 MapPoint(Vector3 world) =>
        Partner.Transform.TransformPoint(Portals.Flip * Transform.InverseTransformPoint(world));

    public Vector3 MapDirection(Vector3 world) =>
        Partner.Transform.TransformDirection(Portals.Flip * Transform.InverseTransformDirection(world));

    /// <summary>Rotation that turns things entering this portal into how they leave the partner.</summary>
    public Quaternion TravelRotation => Partner.Transform.rotation * Portals.Flip * Quaternion.Inverse(Transform.rotation);
}

/// <summary>All portals on this client, plus the see-through rendering.</summary>
internal static class Portals
{
    /// <summary>Half turn about local up: entering the front of one portal means leaving the front of the other.</summary>
    public static readonly Quaternion Flip = Quaternion.Euler(0f, 180f, 0f);

    public static readonly Color[] Colors = { new(0.15f, 0.55f, 1f), new(1f, 0.5f, 0.08f) };

    private const int SurfaceRings = 10;
    private const int SurfaceSegments = 40;
    private const float SurfaceOffset = 0.02f;
    private const float OpenDuration = 0.25f;
    private const float MaxRenderDistance = 250f;

    private static readonly Dictionary<(uint owner, byte color), Portal> portals = new();
    private static readonly List<Portal> all = new();
    private static readonly Texture2D[] unlinkedTextures = new Texture2D[2];
    private static readonly Plane[] frustum = new Plane[6];
    private static bool hooksInstalled;

    public static IReadOnlyList<Portal> All => all;

    public static Portal Get(uint owner, byte color) => portals.TryGetValue((owner, color), out Portal p) ? p : null;

    public static void Place(uint owner, byte color, Vector3 position, Quaternion rotation, bool fresh = true)
    {
        InstallHooks();
        if (!portals.TryGetValue((owner, color), out Portal portal))
        {
            portal = Create(owner, color);
            portals[(owner, color)] = portal;
            all.Add(portal);
        }
        portal.Transform.SetPositionAndRotation(position, rotation);
        portal.OpenedAt = Time.time;
        PortalTravel.OnPortalPlaced(portal);

        if (NetworkClient.spawned.TryGetValue(owner, out NetworkIdentity identity) && identity != null
            && identity.TryGetComponent(out PlayerInfo shooter))
        {
            Vector3 muzzle = shooter.RightHandEquipmentSwitcher.transform.position;
            Beams.Show(muzzle, position, Colors[color] * 1.5f, 0.08f);
            if (fresh)
            {
                PortalSounds.PlayShot(muzzle, color);
            }
        }
    }

    public static void Clear(uint owner)
    {
        for (int i = all.Count - 1; i >= 0; i--)
        {
            Portal portal = all[i];
            if (owner != 0 && portal.Owner != owner)
            {
                continue;
            }
            Destroy(portal);
            all.RemoveAt(i);
            portals.Remove((portal.Owner, portal.Color));
        }
    }

    // ---- creation ----

    private static Portal Create(uint owner, byte color)
    {
        var root = new GameObject($"Portal_{owner}_{(color == 0 ? "Blue" : "Orange")}");
        var portal = new Portal { Owner = owner, Color = color, Transform = root.transform };

        // See-through surface: a finely divided ellipse whose UVs are recomputed every frame so that it
        // shows the matching pixels of a render texture taken from the partner portal.
        var surfaceGo = new GameObject("Surface");
        surfaceGo.transform.SetParent(root.transform, false);
        surfaceGo.transform.localPosition = new Vector3(0f, 0f, SurfaceOffset);
        portal.SurfaceMesh = BuildSurfaceMesh(portal.HalfWidth, portal.HalfHeight, out portal.SurfaceLocalVertices, out portal.SurfaceUvs);
        surfaceGo.AddComponent<MeshFilter>().sharedMesh = portal.SurfaceMesh;
        portal.Surface = surfaceGo.AddComponent<MeshRenderer>();
        portal.Surface.shadowCastingMode = ShadowCastingMode.Off;
        portal.Surface.receiveShadows = false;
        portal.UnlinkedMaterial = UnlitMaterials.Opaque(GetUnlinkedTexture(color), UnityEngine.Color.white);
        portal.Surface.sharedMaterial = portal.UnlinkedMaterial;

        // Glowing rim.
        var rimGo = new GameObject("Rim");
        rimGo.transform.SetParent(root.transform, false);
        rimGo.transform.localPosition = new Vector3(0f, 0f, SurfaceOffset * 1.5f);
        rimGo.AddComponent<MeshFilter>().sharedMesh = BuildRimMesh(portal.HalfWidth, portal.HalfHeight, 0.09f);
        var rim = rimGo.AddComponent<MeshRenderer>();
        rim.shadowCastingMode = ShadowCastingMode.Off;
        rim.sharedMaterial = UnlitMaterials.Opaque(null, Colors[color] * 1.6f);
        return portal;
    }

    private static void Destroy(Portal portal)
    {
        PortalTravel.OnPortalRemoved(portal);
        PortalClones.OnPortalRemoved(portal);
        if (portal.Camera != null)
        {
            Object.Destroy(portal.Camera.gameObject);
        }
        if (portal.Texture != null)
        {
            portal.Texture.Release();
            Object.Destroy(portal.Texture);
        }
        if (portal.LinkedMaterial != null)
        {
            Object.Destroy(portal.LinkedMaterial);
        }
        if (portal.Transform != null)
        {
            Object.Destroy(portal.Transform.gameObject);
        }
        if (portal.SurfaceMesh != null)
        {
            Object.Destroy(portal.SurfaceMesh);
        }
    }

    private static Mesh BuildSurfaceMesh(float a, float b, out Vector3[] vertices, out Vector2[] uvs)
    {
        int count = 1 + SurfaceRings * SurfaceSegments;
        vertices = new Vector3[count];
        uvs = new Vector2[count];
        var triangles = new List<int>();
        vertices[0] = Vector3.zero;
        uvs[0] = new Vector2(0.5f, 0.5f);
        for (int r = 1; r <= SurfaceRings; r++)
        {
            float f = (float)r / SurfaceRings;
            for (int s = 0; s < SurfaceSegments; s++)
            {
                float angle = s * Mathf.PI * 2f / SurfaceSegments;
                int i = 1 + (r - 1) * SurfaceSegments + s;
                vertices[i] = new Vector3(Mathf.Cos(angle) * a * f, Mathf.Sin(angle) * b * f, 0f);
                uvs[i] = new Vector2(0.5f + 0.5f * Mathf.Cos(angle) * f, 0.5f + 0.5f * Mathf.Sin(angle) * f);
            }
        }
        // Winding is fixed up below so every face points out of the portal (+z), toward the viewer.
        for (int s = 0; s < SurfaceSegments; s++)
        {
            int next = (s + 1) % SurfaceSegments;
            triangles.Add(0);
            triangles.Add(1 + next);
            triangles.Add(1 + s);
        }
        for (int r = 1; r < SurfaceRings; r++)
        {
            int inner = 1 + (r - 1) * SurfaceSegments;
            int outer = 1 + r * SurfaceSegments;
            for (int s = 0; s < SurfaceSegments; s++)
            {
                int next = (s + 1) % SurfaceSegments;
                triangles.Add(inner + s);
                triangles.Add(inner + next);
                triangles.Add(outer + next);
                triangles.Add(inner + s);
                triangles.Add(outer + next);
                triangles.Add(outer + s);
            }
        }
        var mesh = new Mesh { name = "PortalSurface" };
        mesh.MarkDynamic();
        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.SetTriangles(triangles, 0);
        FixWinding(mesh);
        mesh.RecalculateNormals();
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(a * 2f, b * 2f, 0.2f));
        return mesh;
    }

    private static Mesh BuildRimMesh(float a, float b, float width)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        for (int s = 0; s < SurfaceSegments; s++)
        {
            float angle = s * Mathf.PI * 2f / SurfaceSegments;
            var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
            vertices.Add(new Vector3(dir.x * a, dir.y * b, 0f));
            vertices.Add(new Vector3(dir.x * (a + width), dir.y * (b + width), 0f));
        }
        for (int s = 0; s < SurfaceSegments; s++)
        {
            int i = s * 2;
            int n = ((s + 1) % SurfaceSegments) * 2;
            triangles.AddRange(new[] { i, n, n + 1, i, n + 1, i + 1 });
        }
        var mesh = new Mesh { name = "PortalRim" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        FixWinding(mesh);
        mesh.RecalculateNormals();
        return mesh;
    }

    /// <summary>Makes every triangle face local +z, out of the portal toward a viewer in front of it (Unity: normal = cross(b - a, c - a) points at the viewer).</summary>
    private static void FixWinding(Mesh mesh)
    {
        Vector3[] v = mesh.vertices;
        int[] t = mesh.triangles;
        for (int i = 0; i < t.Length; i += 3)
        {
            Vector3 normal = Vector3.Cross(v[t[i + 1]] - v[t[i]], v[t[i + 2]] - v[t[i]]);
            if (normal.z < 0f)
            {
                (t[i + 1], t[i + 2]) = (t[i + 2], t[i + 1]);
            }
        }
        mesh.triangles = t;
    }

    /// <summary>Swirly fill shown while a portal has no partner yet.</summary>
    private static Texture2D GetUnlinkedTexture(byte color)
    {
        if (unlinkedTextures[color] != null)
        {
            return unlinkedTextures[color];
        }
        const int size = 128;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: true) { name = $"PortalUnlinked{color}", hideFlags = HideFlags.DontUnloadUnusedAsset };
        Color baseColor = Colors[color];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2f - 1f;
                float dy = (y + 0.5f) / size * 2f - 1f;
                float f = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                float angle = Mathf.Atan2(dy, dx);
                float swirl = 0.5f + 0.5f * Mathf.Sin(angle * 5f + f * 14f);
                float brightness = 0.25f + 0.55f * f * f + 0.25f * swirl * f;
                tex.SetPixel(x, y, baseColor * brightness + UnityEngine.Color.white * (0.08f * swirl));
            }
        }
        tex.Apply();
        unlinkedTextures[color] = tex;
        return tex;
    }

    // ---- per-frame ----

    /// <summary>Called every frame: open animation and which portals need a live view.</summary>
    public static void Tick()
    {
        Camera main = GameManager.Camera;
        if (main != null)
        {
            GeometryUtility.CalculateFrustumPlanes(main, frustum);
        }
        foreach (Portal portal in all)
        {
            if (portal.Transform == null)
            {
                continue;
            }
            float open = Mathf.Clamp01((Time.time - portal.OpenedAt) / OpenDuration);
            portal.Transform.localScale = Vector3.one * Mathf.SmoothStep(0.05f, 1f, open);

            bool render = main != null && portal.IsLinked && PortalGunItem.SeeThrough.Value && IsVisible(portal, main);
            SetRendering(portal, render, main);
        }
    }

    private static bool IsVisible(Portal portal, Camera main)
    {
        Vector3 toCamera = main.transform.position - portal.Transform.position;
        if (Vector3.Dot(toCamera, portal.Transform.forward) <= 0f || toCamera.sqrMagnitude > MaxRenderDistance * MaxRenderDistance)
        {
            return false;
        }
        return GeometryUtility.TestPlanesAABB(frustum, portal.Surface.bounds);
    }

    private static void SetRendering(Portal portal, bool render, Camera main)
    {
        if (render)
        {
            EnsureCamera(portal, main);
            portal.Camera.enabled = true;
            portal.Surface.sharedMaterial = portal.LinkedMaterial;
        }
        else
        {
            if (portal.Camera != null)
            {
                portal.Camera.enabled = false;
            }
            portal.Surface.sharedMaterial = portal.UnlinkedMaterial;
            ResetSurfaceUvs(portal);
        }
        portal.Rendering = render;
    }

    private static void EnsureCamera(Portal portal, Camera main)
    {
        float scale = Mathf.Clamp(PortalGunItem.RenderScale.Value, 0.25f, 1f);
        int width = Mathf.Max(64, Mathf.RoundToInt(main.pixelWidth * scale));
        int height = Mathf.Max(64, Mathf.RoundToInt(main.pixelHeight * scale));
        if (portal.Texture == null || portal.Texture.width != width || portal.Texture.height != height)
        {
            if (portal.Texture != null)
            {
                portal.Texture.Release();
                Object.Destroy(portal.Texture);
            }
            // HDR, like the main camera's buffer: the main camera's post-processing then grades the
            // portal view together with everything else.
            portal.Texture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR) { name = "PortalView" };
            portal.Texture.Create();
            if (portal.LinkedMaterial == null)
            {
                portal.LinkedMaterial = UnlitMaterials.Opaque(portal.Texture, UnityEngine.Color.white);
            }
            UnlitMaterials.SetTexture(portal.LinkedMaterial, portal.Texture);
            if (portal.Camera != null)
            {
                portal.Camera.targetTexture = portal.Texture;
            }
        }
        if (portal.Camera == null)
        {
            var go = new GameObject($"{portal.Transform.name}_Camera");
            Object.DontDestroyOnLoad(go);
            Camera cam = go.AddComponent<Camera>();
            cam.CopyFrom(main);
            cam.targetTexture = portal.Texture;
            cam.depth = main.depth - 1f;
            cam.enabled = false;
            portal.Camera = cam;
        }
    }

    private static void ResetSurfaceUvs(Portal portal)
    {
        if (!portal.Rendering)
        {
            return;
        }
        for (int i = 0; i < portal.SurfaceLocalVertices.Length; i++)
        {
            Vector3 v = portal.SurfaceLocalVertices[i];
            portal.SurfaceUvs[i] = new Vector2(0.5f + 0.5f * v.x / portal.HalfWidth, 0.5f + 0.5f * v.y / portal.HalfHeight);
        }
        portal.SurfaceMesh.uv = portal.SurfaceUvs;
    }

    // ---- rendering hooks ----

    private static void InstallHooks()
    {
        if (hooksInstalled)
        {
            return;
        }
        hooksInstalled = true;
        RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
        SceneManager.activeSceneChanged += (_, _) =>
        {
            Clear(0);
            if (NetworkServer.active)
            {
                PortalNet.ServerClearAll();
            }
        };
    }

    /// <summary>
    /// Runs once per frame after all game logic and camera movement, before any camera renders: place each
    /// portal camera where the main camera would be if it looked out of the partner portal.
    /// </summary>
    private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
    {
        Camera main = GameManager.Camera;
        if (main == null)
        {
            return;
        }
        foreach (Portal portal in all)
        {
            if (!portal.Rendering || portal.Camera == null || !portal.IsLinked)
            {
                continue;
            }
            try
            {
                UpdatePortalCamera(portal, portal.Partner, main);
                UpdateSurfaceUvs(portal, main);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Portal render update failed: {e}");
                portal.Rendering = false;
            }
        }
    }

    private static void UpdatePortalCamera(Portal portal, Portal partner, Camera main)
    {
        Camera cam = portal.Camera;
        Transform mainT = main.transform;
        Matrix4x4 m = partner.Transform.localToWorldMatrix * Matrix4x4.Rotate(Flip) * portal.Transform.worldToLocalMatrix * mainT.localToWorldMatrix;
        cam.transform.SetPositionAndRotation(m.GetColumn(3), Quaternion.LookRotation(m.GetColumn(2), m.GetColumn(1)));
        cam.fieldOfView = main.fieldOfView;
        cam.cullingMask = main.cullingMask;

        // Oblique near plane on the partner's surface, so the wall the partner sits on doesn't block the view.
        Transform clip = partner.Transform;
        int side = System.Math.Sign(Vector3.Dot(clip.forward, clip.position - cam.transform.position));
        Vector3 camSpacePos = cam.worldToCameraMatrix.MultiplyPoint(clip.position);
        Vector3 camSpaceNormal = cam.worldToCameraMatrix.MultiplyVector(clip.forward) * side;
        float camSpaceDistance = -Vector3.Dot(camSpacePos, camSpaceNormal) + 0.05f;
        cam.projectionMatrix = Mathf.Abs(camSpaceDistance) > 0.2f
            ? main.CalculateObliqueMatrix(new Vector4(camSpaceNormal.x, camSpaceNormal.y, camSpaceNormal.z, camSpaceDistance))
            : main.projectionMatrix;
    }

    /// <summary>
    /// Screen-space mapping without a custom shader: every surface vertex gets the screen position the main
    /// camera sees it at, so the render texture lines up with the screen. The mesh is finely divided, which
    /// keeps the per-triangle interpolation error invisible.
    /// </summary>
    private static void UpdateSurfaceUvs(Portal portal, Camera main)
    {
        Transform surface = portal.Surface.transform;
        Matrix4x4 viewProjection = main.projectionMatrix * main.worldToCameraMatrix * surface.localToWorldMatrix;
        Vector3[] verts = portal.SurfaceLocalVertices;
        for (int i = 0; i < verts.Length; i++)
        {
            Vector4 clip = viewProjection * new Vector4(verts[i].x, verts[i].y, verts[i].z, 1f);
            float w = Mathf.Max(clip.w, 0.001f); // vertices behind the camera only happen while walking through
            portal.SurfaceUvs[i] = new Vector2(clip.x / w * 0.5f + 0.5f, clip.y / w * 0.5f + 0.5f);
        }
        portal.SurfaceMesh.uv = portal.SurfaceUvs;
    }

    // While a portal camera renders, other portals show their flat fill: a render texture can't show itself.
    private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!IsPortalCamera(camera))
        {
            return;
        }
        foreach (Portal portal in all)
        {
            if (portal.Surface != null)
            {
                portal.Surface.sharedMaterial = portal.UnlinkedMaterial;
            }
        }
    }

    private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (!IsPortalCamera(camera))
        {
            return;
        }
        foreach (Portal portal in all)
        {
            if (portal.Surface != null && portal.Rendering)
            {
                portal.Surface.sharedMaterial = portal.LinkedMaterial;
            }
        }
    }

    private static bool IsPortalCamera(Camera camera)
    {
        foreach (Portal portal in all)
        {
            if (portal.Camera == camera)
            {
                return true;
            }
        }
        return false;
    }
}

using System.Collections.Generic;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// What a lightsaber cut looks like, on every client: the victim falls apart in two pieces at the cut, with
/// glowing, cauterized faces.
///
/// Players: the real body is hidden while they're knocked out and two copies of it (<see cref="ModelPuppet"/>)
/// fall instead, one per side of the cut; in each copy the bones on the other side are squeezed into the cut, so
/// the mesh ends there. When the player gets back up the pieces vanish and the player reappears.
/// Golf carts: the cart is destroyed by the host, and its mesh is really cut in two (read back from the GPU, see
/// <see cref="MeshReadback"/>), or, if that's not possible, split into its separate parts.
/// </summary>
internal static class SaberSlicer
{
    private const float PendingSeconds = 1.5f;    // how long a cut waits for its victim's knockout (or kill) to arrive
    private const float MaxHiddenSeconds = 12f;
    private const float MinPieceSeconds = 1.5f;
    private const float ShrinkSeconds = 0.35f;
    private const float PieceMass = 4f;

    private sealed class PendingCut
    {
        public PlayerInfo Player;
        public Vector3 LocalPoint, LocalNormal;
        public Color Color;
        public float Expires;
    }

    private sealed class Pieces
    {
        public PlayerInfo Victim;                   // null for carts
        public readonly List<GameObject> Objects = new();
        public readonly List<Renderer> Hidden = new();
        public float Start;
        public float EndAt = -1f;                   // set once the pieces start shrinking
        public float CartLifetime;
    }

    private static readonly List<PendingCut> pending = new();
    private static readonly List<Pieces> active = new();
    private static readonly List<Renderer> renderers = new();
    private static readonly Dictionary<Color, Material> capMaterials = new();
    private static readonly List<Collider> victimColliders = new();

    // ---- incoming cuts ----

    /// <summary>A player was cut: it shows once their knockout has registered (shields and the like can stop it).</summary>
    public static void QueuePlayerCut(PlayerInfo player, Vector3 point, Vector3 normal, Color color)
    {
        pending.Add(new PendingCut
        {
            Player = player,
            LocalPoint = player.transform.InverseTransformPoint(point),
            LocalNormal = player.transform.InverseTransformDirection(normal),
            Color = color,
            Expires = Time.time + PendingSeconds,
        });
    }

    public static void Tick()
    {
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            PendingCut cut = pending[i];
            if (cut.Player == null || Time.time > cut.Expires)
            {
                pending.RemoveAt(i);
                continue;
            }
            if (!IsDown(cut.Player) || IsAlreadyInPieces(cut.Player))
            {
                continue;
            }
            pending.RemoveAt(i);
            try
            {
                SlicePlayer(cut.Player, cut.Player.transform.TransformPoint(cut.LocalPoint), cut.Player.transform.TransformDirection(cut.LocalNormal), cut.Color);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Lightsaber cut failed: {e}");
            }
        }

        for (int i = active.Count - 1; i >= 0; i--)
        {
            if (UpdatePieces(active[i]))
            {
                active.RemoveAt(i);
            }
        }
    }

    // A saber hit kills (the victim waits hidden, then respawns); a hit that only knocks out (in a cart) still counts.
    private static bool IsDown(PlayerInfo player) => player.Movement.IsKnockedOutOrRecovering || player.Movement.IsRespawning;

    private static bool IsAlreadyInPieces(PlayerInfo player)
    {
        foreach (Pieces pieces in active)
        {
            if (pieces.Victim == player)
            {
                return true;
            }
        }
        return false;
    }

    // ---- players ----

    private static void SlicePlayer(PlayerInfo player, Vector3 point, Vector3 normal, Color color)
    {
        renderers.Clear();
        // A killed player is hidden as soon as they're eliminated, often before the cut gets here: copy them anyway.
        ModelPuppet.CollectRenderers(player.transform, renderers, includeHidden: player.Movement.IsRespawning);
        if (renderers.Count == 0)
        {
            return;
        }
        point = CenterCutOnBones(renderers, point, normal);
        var plane = new Plane(normal, point);

        var pieces = new Pieces { Victim = player, Start = Time.time };
        Vector3 velocity = player.Rigidbody != null ? player.Rigidbody.linearVelocity : Vector3.zero;
        Vector3 away = Vector3.ProjectOnPlane(normal, Vector3.up).sqrMagnitude > 0.01f ? normal : player.transform.forward;
        for (int side = 0; side < 2; side++)
        {
            bool positive = side == 0;
            ModelPuppet puppet = ModelPuppet.Create($"SaberPiece_{player.name}_{side}", renderers,
                t => plane.GetSide(t.position) == positive, point);
            Vector3 push = (positive ? normal * 1.6f + Vector3.up * 1.2f : -away * 0.6f) + Random.insideUnitSphere * 0.4f;
            GameObject body = MakePhysicsPiece(puppet, velocity + push, color, point, positive ? -normal : normal, 0.16f);
            if (body != null)
            {
                pieces.Objects.Add(body);
            }
        }

        // Hide the real player until they're back up.
        foreach (Renderer renderer in renderers)
        {
            renderer.forceRenderingOff = true;
            pieces.Hidden.Add(renderer);
        }
        IgnoreVictim(pieces, player);
        active.Add(pieces);
        PlayCutEffects(point, color);
    }

    /// <summary>A cut too close to the head or feet would leave one piece with nothing in it; move it in to where both sides get some bones.</summary>
    private static Vector3 CenterCutOnBones(List<Renderer> list, Vector3 point, Vector3 normal)
    {
        var heights = new List<float>();
        foreach (Renderer renderer in list)
        {
            if (renderer is SkinnedMeshRenderer skinned)
            {
                foreach (Transform bone in skinned.bones)
                {
                    if (bone != null)
                    {
                        heights.Add(Vector3.Dot(bone.position - point, normal));
                    }
                }
            }
        }
        if (heights.Count < 8)
        {
            return point;
        }
        heights.Sort();
        int below = heights.FindIndex(h => h >= 0f);
        if (below < 0)
        {
            below = heights.Count;
        }
        int minimum = heights.Count / 5;
        if (below < minimum)
        {
            return point + normal * (heights[minimum] - 0.01f);
        }
        if (heights.Count - below < minimum)
        {
            return point + normal * (heights[heights.Count - minimum] + 0.01f);
        }
        return point;
    }

    private static void IgnoreVictim(Pieces pieces, PlayerInfo player)
    {
        victimColliders.Clear();
        player.GetComponentsInChildren(false, victimColliders);
        var pieceColliders = new List<Collider>();
        foreach (GameObject piece in pieces.Objects)
        {
            pieceColliders.AddRange(piece.GetComponents<Collider>());
        }
        foreach (Collider own in pieceColliders)
        {
            foreach (Collider other in victimColliders)
            {
                Physics.IgnoreCollision(own, other, true);
            }
            foreach (Collider other in pieceColliders)
            {
                if (other != own)
                {
                    Physics.IgnoreCollision(own, other, true);
                }
            }
        }
    }

    /// <summary>Puts a frozen puppet on a rigidbody with a box around its parts, a glowing cap at the cut, and a push.</summary>
    private static GameObject MakePhysicsPiece(ModelPuppet puppet, Vector3 velocity, Color color, Vector3 cutPoint, Vector3 capFacing, float capRadius)
    {
        bool any = false;
        var bounds = new Bounds();
        foreach (Vector3 p in puppet.PartPositions())
        {
            if (!any)
            {
                bounds = new Bounds(p, Vector3.zero);
                any = true;
            }
            else
            {
                bounds.Encapsulate(p);
            }
        }
        if (!any)
        {
            puppet.Destroy();
            return null;
        }
        bounds.Encapsulate(cutPoint);
        bounds.Expand(0.25f);

        var go = new GameObject(puppet.Root.name);
        go.transform.position = bounds.center;
        puppet.Root.transform.SetParent(go.transform, true);
        var box = go.AddComponent<BoxCollider>();
        box.size = bounds.size;
        var body = go.AddComponent<Rigidbody>();
        body.mass = PieceMass;
        body.linearVelocity = velocity;
        body.angularVelocity = Random.insideUnitSphere * 4f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        AddCap(go.transform, cutPoint, capFacing, capRadius, color);
        return go;
    }

    // ---- carts ----

    /// <summary>A golf cart was cut (the host destroys it right after).</summary>
    public static void SliceCart(Entity cart, Vector3 point, Vector3 normal, Color color)
    {
        renderers.Clear();
        foreach (Renderer renderer in cart.GetComponentsInChildren<Renderer>(false))
        {
            // Passengers are cut separately; leave their models out.
            if (renderer is MeshRenderer && renderer.enabled && renderer.GetComponentInParent<PlayerInfo>() == null
                && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null && !renderer.TryGetComponent(out TMPro.TMP_Text _))
            {
                renderers.Add(renderer);
            }
        }
        PlayCutEffects(point, color);
        if (renderers.Count == 0)
        {
            return;
        }
        Vector3 velocity = cart.HasRigidbody ? cart.Rigidbody.linearVelocity : Vector3.zero;
        var plane = new Plane(normal, point);

        // Snapshot what's needed now: the cart is about to be destroyed.
        var parts = new List<(Mesh mesh, Matrix4x4 matrix, Material[] materials, Renderer source)>();
        foreach (Renderer renderer in renderers)
        {
            parts.Add((renderer.GetComponent<MeshFilter>().sharedMesh, renderer.localToWorldMatrix, renderer.sharedMaterials, renderer));
        }
        ModelPuppet fallbackPositive = ModelPuppet.Create("SaberCartPiece_0", renderers, t => plane.GetSide(PartCenter(t)), point);
        ModelPuppet fallbackNegative = ModelPuppet.Create("SaberCartPiece_1", renderers, t => !plane.GetSide(PartCenter(t)), point);
        fallbackPositive.Root.SetActive(false);
        fallbackNegative.Root.SetActive(false);

        var copies = new MeshCopy[parts.Count];
        int remaining = parts.Count;
        bool failed = false;
        for (int i = 0; i < parts.Count; i++)
        {
            int index = i;
            MeshReadback.Request(parts[i].mesh, copy =>
            {
                copies[index] = copy;
                failed |= copy == null;
                if (--remaining > 0)
                {
                    return;
                }
                if (!failed && BuildCutCart(parts, copies, plane, velocity, color))
                {
                    fallbackPositive.Destroy();
                    fallbackNegative.Destroy();
                    return;
                }
                // Couldn't read the meshes: the cart comes apart into its pieces instead.
                var pieces = new Pieces { Start = Time.time, CartLifetime = LightsaberItem.PieceSeconds.Value };
                for (int side = 0; side < 2; side++)
                {
                    ModelPuppet puppet = side == 0 ? fallbackPositive : fallbackNegative;
                    puppet.Root.SetActive(true);
                    GameObject body = MakePhysicsPiece(puppet, velocity + (side == 0 ? normal : -normal) * 1.5f + Vector3.up * 1.5f,
                        color, point, side == 0 ? -normal : normal, 0.35f);
                    if (body != null)
                    {
                        pieces.Objects.Add(body);
                    }
                }
                active.Add(pieces);
            });
        }
    }

    private static Vector3 PartCenter(Transform t) =>
        t.TryGetComponent(out Renderer renderer) ? renderer.bounds.center : t.position;

    private static bool BuildCutCart(List<(Mesh mesh, Matrix4x4 matrix, Material[] materials, Renderer source)> parts, MeshCopy[] copies,
        Plane plane, Vector3 velocity, Color color)
    {
        var slicer = new MeshSlicer(plane);
        for (int i = 0; i < parts.Count; i++)
        {
            slicer.Add(copies[i], parts[i].matrix, parts[i].materials);
        }
        if (!slicer.HasPositive || !slicer.HasNegative)
        {
            return false;
        }
        var pieces = new Pieces { Start = Time.time, CartLifetime = LightsaberItem.PieceSeconds.Value };
        (Vector3 center, float size) cut = slicer.CutExtent;
        MaterialPropertyBlock block = null;
        Renderer first = parts[0].source;
        if (first != null && first.HasPropertyBlock())
        {
            block = new MaterialPropertyBlock();
            first.GetPropertyBlock(block); // team colors
        }
        for (int side = 0; side < 2; side++)
        {
            (Mesh mesh, Material[] materials, Vector3 center)? built = slicer.Build(side == 0, CapMaterial(color));
            if (built == null)
            {
                continue;
            }
            var go = new GameObject($"SaberCartPiece_{side}");
            go.transform.position = built.Value.center;
            go.AddComponent<MeshFilter>().sharedMesh = built.Value.mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = built.Value.materials;
            if (block != null)
            {
                renderer.SetPropertyBlock(block);
            }
            var box = go.AddComponent<BoxCollider>();
            box.center = built.Value.mesh.bounds.center;
            box.size = built.Value.mesh.bounds.size;
            var body = go.AddComponent<Rigidbody>();
            body.mass = PieceMass * 5f;
            body.linearVelocity = velocity + (side == 0 ? plane.normal : -plane.normal) * 1.8f + Vector3.up * 2f;
            body.angularVelocity = Random.insideUnitSphere * 2f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            AddGlowHalo(go.transform, plane, built.Value.center, cut.center, cut.size, color);
            pieces.Objects.Add(go);
        }
        // The two halves start touching at the cut; let them slide apart instead of bouncing off each other.
        if (pieces.Objects.Count == 2)
        {
            Physics.IgnoreCollision(pieces.Objects[0].GetComponent<Collider>(), pieces.Objects[1].GetComponent<Collider>(), true);
        }
        active.Add(pieces);
        return true;
    }

    // ---- shared ----

    /// <summary>Returns true when the pieces are gone.</summary>
    private static bool UpdatePieces(Pieces pieces)
    {
        float age = Time.time - pieces.Start;
        if (pieces.Victim != null && pieces.EndAt < 0f)
        {
            HideVictim(pieces);
        }
        if (pieces.EndAt < 0f)
        {
            bool done = pieces.Victim != null
                ? age > MaxHiddenSeconds || (age > MinPieceSeconds && !IsDown(pieces.Victim))
                : age > pieces.CartLifetime;
            if (pieces.Victim == null && pieces.Hidden.Count > 0)
            {
                done = true; // the victim left
            }
            if (done)
            {
                pieces.EndAt = Time.time + ShrinkSeconds;
                Restore(pieces);
            }
            return false;
        }
        float shrink = Mathf.Clamp01((pieces.EndAt - Time.time) / ShrinkSeconds);
        foreach (GameObject piece in pieces.Objects)
        {
            if (piece != null)
            {
                piece.transform.localScale = Vector3.one * shrink;
            }
        }
        if (shrink > 0f)
        {
            return false;
        }
        foreach (GameObject piece in pieces.Objects)
        {
            if (piece != null)
            {
                Object.Destroy(piece);
            }
        }
        return true;
    }

    private static readonly HashSet<Renderer> hiddenSet = new();

    /// <summary>
    /// Keeps the cut player's own model hidden. The game switches hats and face wear on and off itself (hiding them from
    /// first person, refreshing them), which brought the originals back, floating where the player was cut; so hidden
    /// renderers are forced off every frame, and any new or missed mesh renderer under the victim is hidden too.
    /// </summary>
    private static void HideVictim(Pieces pieces)
    {
        if (pieces.Victim == null)
        {
            return;
        }
        foreach (Renderer renderer in pieces.Hidden)
        {
            if (renderer != null)
            {
                renderer.forceRenderingOff = true;
            }
        }
        if (Time.frameCount % 6 != 0)
        {
            return;
        }
        hiddenSet.Clear();
        hiddenSet.UnionWith(pieces.Hidden);
        foreach (Renderer renderer in pieces.Victim.GetComponentsInChildren<Renderer>(false))
        {
            if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && !hiddenSet.Contains(renderer) && !renderer.TryGetComponent(out TMPro.TMP_Text _))
            {
                renderer.forceRenderingOff = true;
                pieces.Hidden.Add(renderer);
            }
        }
    }

    private static void Restore(Pieces pieces)
    {
        foreach (Renderer renderer in pieces.Hidden)
        {
            // While the game itself hides the player (still waiting to respawn), it shows them again when they're back.
            if (renderer != null && (pieces.Victim == null || pieces.Victim.Movement.IsVisible))
            {
                renderer.forceRenderingOff = false;
            }
        }
        pieces.Hidden.Clear();
    }

    public static void ClearAll()
    {
        pending.Clear();
        foreach (Pieces pieces in active)
        {
            Restore(pieces);
            foreach (GameObject piece in pieces.Objects)
            {
                if (piece != null)
                {
                    Object.Destroy(piece);
                }
            }
        }
        active.Clear();
    }

    private static void PlayCutEffects(Vector3 point, Color color)
    {
        ItemAudio.Play(SaberSounds.Cut, ItemAudio.SfxBus, ItemAudio.Attenuate(point, LightsaberItem.SoundVolume.Value, 6f, 50f));
        Color bright = Color.Lerp(color, Color.white, 0.5f) * 1.5f;
        for (int i = 0; i < 10; i++)
        {
            Vector3 spark = Random.onUnitSphere * Random.Range(0.3f, 0.9f);
            Beams.Show(point, point + spark, bright, 0.03f, 0f, Random.Range(0.12f, 0.3f));
        }
    }

    /// <summary>The cut face of a player piece: a glowing disc (with a soft halo) where the body ends.</summary>
    private static void AddCap(Transform parent, Vector3 point, Vector3 facing, float radius, Color color)
    {
        GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        Object.Destroy(disc.GetComponent<Collider>());
        disc.name = "SaberCut";
        disc.transform.SetParent(parent, false);
        disc.transform.SetPositionAndRotation(point, Quaternion.FromToRotation(Vector3.up, facing));
        disc.transform.localScale = new Vector3(radius * 2f, 0.005f, radius * 2f);
        disc.GetComponent<MeshRenderer>().sharedMaterial = CapMaterial(color);

        GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(halo.GetComponent<Collider>());
        halo.name = "SaberCutGlow";
        halo.transform.SetParent(parent, false);
        halo.transform.SetPositionAndRotation(point + facing * 0.01f, Quaternion.LookRotation(-facing));
        halo.transform.localScale = Vector3.one * radius * 4f;
        halo.GetComponent<MeshRenderer>().sharedMaterial = LightsaberItem.SoftGlowMaterial(Color.Lerp(color, new Color(1f, 0.45f, 0.1f), 0.5f), 0.6f);
    }

    /// <summary>A soft round glow over the middle of a cart's cut face, no wider than the cut itself.</summary>
    private static void AddGlowHalo(Transform parent, Plane plane, Vector3 pieceCenter, Vector3 cutCenter, float cutSize, Color color)
    {
        Vector3 facing = plane.GetSide(pieceCenter) ? -plane.normal : plane.normal;
        GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        Object.Destroy(halo.GetComponent<Collider>());
        halo.name = "SaberCutGlow";
        halo.transform.SetParent(parent, false);
        halo.transform.SetPositionAndRotation(plane.ClosestPointOnPlane(cutCenter) + facing * 0.02f, Quaternion.LookRotation(-facing));
        halo.transform.localScale = Vector3.one * Mathf.Clamp(cutSize * 0.8f, 0.3f, 1.4f);
        halo.GetComponent<MeshRenderer>().sharedMaterial = LightsaberItem.SoftGlowMaterial(Color.Lerp(color, new Color(1f, 0.45f, 0.1f), 0.5f), 0.35f);
    }

    /// <summary>Molten, glowing cut surface: white-hot in the saber's color, shading to orange.</summary>
    private static Material CapMaterial(Color color)
    {
        if (!capMaterials.TryGetValue(color, out Material material) || material == null)
        {
            Color hot = Color.Lerp(Color.Lerp(color, new Color(1f, 0.5f, 0.15f), 0.45f), Color.white, 0.15f) * 1.2f;
            material = UnlitMaterials.Opaque(null, hot);
            capMaterials[color] = material;
        }
        return material;
    }
}

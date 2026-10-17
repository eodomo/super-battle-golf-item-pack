using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// The net itself, as a Verlet cloth: a bag of strings whose rim is pinned to the net's ring. Every particle is a knot;
/// stretch (meridians and rings of strings), shear (diagonals) and bend constraints are relaxed a few times per step, and
/// a sphere (the caught player) pushes the strings out. It swings, trails and sways with the pole, and drapes over whoever
/// is in it. Drawn as camera-facing ribbons, one per string, rebuilt every frame.
/// </summary>
internal sealed class NetCloth
{
    private const int Around = 24;
    private const int Rows = 14;
    private const float Gravity = -16f;
    private const float Damping = 0.988f;
    private const int Substeps = 3;
    private const int Iterations = 6;

    private struct Link
    {
        public int A, B;
        public float Rest, Stiffness;
    }

    private readonly float length, bulbRadius, ringRadius, width;
    private readonly Vector3[] pos, prev, target;
    private readonly Link[] links;
    private readonly int[] strings; // index pairs of the drawn strings
    private readonly Mesh mesh;
    private readonly Vector3[] verts;
    private readonly GameObject go;
    private Vector3 lastCenter, lastBody;
    private Vector3 obstacleA, obstacleB;
    private float obstacleRadius;
    private bool seeded;

    public GameObject Root => go;
    private int Apex => Rows * Around;
    private int Count => Rows * Around + 1;

    public NetCloth(float length, float bulbRadius, float ringRadius, float width, Material material)
    {
        this.length = length;
        this.bulbRadius = bulbRadius;
        this.ringRadius = ringRadius;
        this.width = width;
        pos = new Vector3[Count];
        prev = new Vector3[Count];
        target = new Vector3[Count];

        // Links come from the rest shape in its own frame (axis +Z, ring spanning X and Y).
        var rest = new Vector3[Count];
        for (int k = 0; k < Rows; k++)
        {
            for (int i = 0; i < Around; i++)
            {
                rest[k * Around + i] = RestPoint(k, i, Vector3.zero, Vector3.right, Vector3.up, Vector3.forward);
            }
        }
        rest[Apex] = Vector3.forward * length;

        var list = new System.Collections.Generic.List<Link>();
        var drawn = new System.Collections.Generic.List<int>();
        void Add(int a, int b, float stiffness, bool draw)
        {
            list.Add(new Link { A = a, B = b, Rest = Vector3.Distance(rest[a], rest[b]), Stiffness = stiffness });
            if (draw)
            {
                drawn.Add(a);
                drawn.Add(b);
            }
        }
        int Id(int k, int i) => k * Around + ((i % Around) + Around) % Around;
        for (int k = 0; k < Rows; k++)
        {
            for (int i = 0; i < Around; i++)
            {
                Add(Id(k, i), k + 1 < Rows ? Id(k + 1, i) : Apex, 1f, true);   // meridians
                if (k > 0)
                {
                    Add(Id(k, i), Id(k, i + 1), 0.6f, true);                   // rings of strings
                }
                if (k + 1 < Rows)
                {
                    Add(Id(k, i), Id(k + 1, i + 1), 0.05f, false);             // shear
                }
                if (k + 2 < Rows)
                {
                    Add(Id(k, i), Id(k + 2, i), 0.02f, false);                 // bend
                }
            }
        }
        links = list.ToArray();
        strings = drawn.ToArray();

        mesh = new Mesh { name = "NetCloth" };
        mesh.MarkDynamic();
        int segments = strings.Length / 2;
        verts = new Vector3[segments * 8];
        var triangles = new int[segments * 24];
        // Both windings, so the ribbon shows from either side.
        int[] quad = { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 };
        for (int s = 0; s < segments; s++)
        {
            for (int q = 0; q < 24; q++)
            {
                triangles[s * 24 + q] = s * 8 + (q / 12) * 4 + quad[q % 12];
            }
        }
        mesh.vertices = verts;
        mesh.triangles = triangles;

        go = new GameObject("NetCloth");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    private float RadiusAt(float s)
    {
        if (s <= 0.55f)
        {
            return ringRadius + (bulbRadius - ringRadius) * Mathf.Sin(Mathf.PI / 2f * s / 0.55f);
        }
        return bulbRadius * Mathf.Cos(Mathf.PI / 2f * (s - 0.55f) / 0.45f);
    }

    private Vector3 RestPoint(int k, int i, Vector3 center, Vector3 right, Vector3 up, Vector3 axis)
    {
        float s = (float)k / Rows;
        float a = 2f * Mathf.PI * i / Around;
        return center + axis * (s * length) + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * RadiusAt(s);
    }

    public void SetActive(bool active) => go.SetActive(active);

    /// <summary>Starts over from the rest shape on the next step (e.g. after being hidden).</summary>
    public void Reset() => seeded = false;

    public void Destroy()
    {
        UnityEngine.Object.Destroy(go);
        UnityEngine.Object.Destroy(mesh);
    }

    /// <summary>Moves the pinned rim to the ring and advances the cloth; <paramref name="axis"/> is the way the bag opens at rest.</summary>
    public void Step(Vector3 center, Vector3 right, Vector3 up, Vector3 axis, float dt, Vector3[] sphereCenters, float[] sphereRadii)
    {
        if (!seeded || (center - lastCenter).sqrMagnitude > 4f)
        {
            for (int k = 0; k < Rows; k++)
            {
                for (int i = 0; i < Around; i++)
                {
                    pos[k * Around + i] = prev[k * Around + i] = RestPoint(k, i, center, right, up, axis);
                }
            }
            pos[Apex] = prev[Apex] = center + axis * length;
            seeded = true;
        }
        Simulate(center, right, up, dt, sphereCenters, sphereRadii, 0f);
    }

    /// <summary>
    /// The net with someone in it: the bag hangs from the ring and bulges round a ball of <paramref name="bodyRadius"/>
    /// at <paramref name="body"/>, the mouth gathered in between. Every knot is pulled softly toward that shape, so the
    /// net sways and jiggles but stays wrapped round them instead of swinging off to one side.
    /// </summary>
    public void Wrap(Vector3 ring, Vector3 right, Vector3 up, Vector3 body, float bodyRadius, float dt, Vector3[] sphereCenters, float[] sphereRadii)
    {
        Vector3 toBody = body - ring;
        float d = toBody.magnitude;
        Vector3 axis = d > 1e-4f ? toBody / d : Vector3.down;
        // The rim's own directions squared up round the axis, so the strings run straight down instead of twisting.
        Vector3 e1 = Vector3.ProjectOnPlane(right, axis);
        if (e1.sqrMagnitude < 1e-6f)
        {
            e1 = Vector3.ProjectOnPlane(up, axis);
        }
        e1.Normalize();
        Vector3 e2 = Vector3.ProjectOnPlane(Vector3.ProjectOnPlane(up, axis), e1);
        e2 = e2.sqrMagnitude > 1e-6f ? e2.normalized : Vector3.Cross(axis, e1);

        float ball = bodyRadius * 1.05f;
        float bottom = d + ball;
        for (int k = 0; k < Rows; k++)
        {
            float z = bottom * k / Rows;
            // Flaring out from the ring like a sack down to the body's widest point, then round the bottom of the ball.
            float r = z < d ? ringRadius + (ball - ringRadius) * Mathf.Sin(Mathf.PI / 2f * z / Mathf.Max(d, 0.01f)) : 0.03f;
            float fromMiddle = z - d;
            if (Mathf.Abs(fromMiddle) < ball)
            {
                r = Mathf.Max(r, Mathf.Sqrt(ball * ball - fromMiddle * fromMiddle));
            }
            for (int i = 0; i < Around; i++)
            {
                float a = 2f * Mathf.PI * i / Around;
                target[k * Around + i] = ring + axis * z + (e1 * Mathf.Cos(a) + e2 * Mathf.Sin(a)) * r;
            }
        }
        target[Apex] = ring + axis * bottom;
        if (obstacleRadius > 0f)
        {
            // The resting shape drapes round the pole too, so the cloth isn't forever pulled back into it.
            for (int p = Around; p < Count; p++)
            {
                target[p] = OutOfObstacle(target[p]);
            }
        }
        for (int l = 0; l < links.Length; l++)
        {
            links[l].Rest = Vector3.Distance(target[links[l].A], target[links[l].B]);
        }
        if (!seeded || (ring - lastCenter).sqrMagnitude > 4f)
        {
            System.Array.Copy(target, pos, Count);
            System.Array.Copy(target, prev, Count);
            seeded = true;
        }
        else
        {
            // The whole bag rides along with whoever is in it; the cloth only adds the sway on top. Left to catch up on
            // its own it trails behind when the carrier walks, turns or jumps, and they show through outside it.
            Vector3 moved = body - lastBody;
            for (int p = Around; p < Count; p++)
            {
                pos[p] += moved;
                prev[p] += moved;
            }
        }
        lastBody = body;
        Simulate(ring, right, up, dt, sphereCenters, sphereRadii, 0.12f);
    }

    /// <summary>A rod the strings can't pass through (the pole holding the net), from <paramref name="a"/> to <paramref name="b"/>; radius 0 for none.</summary>
    public void SetObstacle(Vector3 a, Vector3 b, float radius)
    {
        obstacleA = a;
        obstacleB = b;
        obstacleRadius = radius;
    }

    private Vector3 OutOfObstacle(Vector3 point)
    {
        Vector3 ab = obstacleB - obstacleA;
        float t = Mathf.Clamp01(Vector3.Dot(point - obstacleA, ab) / Mathf.Max(ab.sqrMagnitude, 1e-6f));
        Vector3 closest = obstacleA + ab * t;
        Vector3 off = point - closest;
        float dist = off.magnitude;
        if (dist >= obstacleRadius)
        {
            return point;
        }
        // Straight out from the rod (or down, off it, if right on it).
        return closest + (dist > 1e-5f ? off / dist : Vector3.down) * obstacleRadius;
    }

    private void Simulate(Vector3 center, Vector3 right, Vector3 up, float dt, Vector3[] sphereCenters, float[] sphereRadii, float pull)
    {
        lastCenter = center;
        dt = Mathf.Min(dt, 1f / 30f);
        float h = dt / Substeps;
        for (int step = 0; step < Substeps; step++)
        {
            for (int p = Around; p < Count; p++)
            {
                Vector3 velocity = (pos[p] - prev[p]) * Damping;
                prev[p] = pos[p];
                pos[p] += velocity + Vector3.up * (Gravity * h * h);
                if (pull > 0f)
                {
                    // Toward the wrapped shape; most of it goes on the last position too, so it doesn't build up a swing.
                    pos[p] = Vector3.Lerp(pos[p], target[p], pull);
                    prev[p] = Vector3.Lerp(prev[p], target[p], pull * 0.8f);
                }
            }
            for (int i = 0; i < Around; i++)
            {
                float a = 2f * Mathf.PI * i / Around;
                prev[i] = pos[i];
                pos[i] = center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * ringRadius;
            }
            for (int it = 0; it < Iterations; it++)
            {
                foreach (Link link in links)
                {
                    bool pinA = link.A < Around, pinB = link.B < Around;
                    if (pinA && pinB)
                    {
                        continue;
                    }
                    Vector3 d = pos[link.B] - pos[link.A];
                    float dist = d.magnitude;
                    if (dist < 1e-6f)
                    {
                        continue;
                    }
                    Vector3 correction = d * ((dist - link.Rest) / dist * link.Stiffness);
                    if (pinA)
                    {
                        pos[link.B] -= correction;
                    }
                    else if (pinB)
                    {
                        pos[link.A] += correction;
                    }
                    else
                    {
                        pos[link.A] += correction * 0.5f;
                        pos[link.B] -= correction * 0.5f;
                    }
                }
                if (obstacleRadius > 0f)
                {
                    for (int p = Around; p < Count; p++)
                    {
                        pos[p] = OutOfObstacle(pos[p]);
                    }
                }
                if (sphereCenters != null)
                {
                    for (int p = Around; p < Count; p++)
                    {
                        for (int c = 0; c < sphereCenters.Length; c++)
                        {
                            Vector3 o = pos[p] - sphereCenters[c];
                            float dist = o.magnitude;
                            if (dist < sphereRadii[c])
                            {
                                pos[p] = sphereCenters[c] + (dist > 1e-5f ? o / dist : Vector3.up) * sphereRadii[c];
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Rebuilds the strings: each is two thin ribbons crossed at right angles, so it reads as a round string from any side
    /// (no camera needed, so every player's view and every screenshot agree).
    /// </summary>
    public void Render()
    {
        int segments = strings.Length / 2;
        float half = width * 0.5f;
        for (int s = 0; s < segments; s++)
        {
            Vector3 a = pos[strings[s * 2]], b = pos[strings[s * 2 + 1]];
            Vector3 dir = b - a;
            Vector3 side1 = Vector3.Cross(dir, Vector3.up);
            if (side1.sqrMagnitude < 1e-10f)
            {
                side1 = Vector3.Cross(dir, Vector3.right);
            }
            side1 = side1.normalized * half;
            Vector3 side2 = Vector3.Cross(dir, side1).normalized * half;
            int v = s * 8;
            verts[v] = a - side1;
            verts[v + 1] = a + side1;
            verts[v + 2] = b + side1;
            verts[v + 3] = b - side1;
            verts[v + 4] = a - side2;
            verts[v + 5] = a + side2;
            verts[v + 6] = b + side2;
            verts[v + 7] = b - side2;
        }
        mesh.vertices = verts;
        // The strings are in world space on an object at the origin, so the bounds must follow them: a fixed box round
        // the origin left the net culled whenever the camera looked away from the origin.
        mesh.RecalculateBounds();
    }
}

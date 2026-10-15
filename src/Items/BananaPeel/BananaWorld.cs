using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ItemPack.Items;

/// <summary>
/// Client-side banana peels. Every client computes the same flight path from the same spawn data
/// against static level geometry, so peels land in the same spot everywhere without syncing physics.
/// Each client only checks whether its own player slips; the server arbitrates who got the peel.
/// </summary>
internal static class BananaWorld
{
    private const float SimStep = 0.01f;
    private const float MaxFlightTime = 8f;
    private const float FlyAwayDuration = 1.2f;

    private enum State { Flying, Landed, FlyingAway }

    private class Banana
    {
        public int id;
        public uint ownerNetId;
        public GameObject go;
        public State state;
        public float spawnTime;
        public float landTime;
        public List<Vector3> path;
        public Quaternion landedRotation;
        public Vector3 flyAwayVelocity;
        public Vector3 flyAwaySpin;
    }

    private static readonly Dictionary<int, Banana> bananas = new();
    private static readonly List<int> toDelete = new();
    private static bool sceneHookInstalled;

    public static void Spawn(int id, uint ownerNetId, Vector3 position, Vector3 velocity)
    {
        EnsureSceneHook();
        if (bananas.ContainsKey(id) || BananaPeelItem.Instance.Model.Mesh == null)
        {
            return;
        }

        var go = new GameObject($"BananaPeel_{id}");
        BananaPeelItem.Instance.Model.CreateModel(go.transform, BananaPeelItem.GroundScale.Value);
        go.transform.position = position;

        var banana = new Banana
        {
            id = id,
            ownerNetId = ownerNetId,
            go = go,
            state = State.Flying,
            spawnTime = Time.time,
        };
        banana.path = ComputePath(position, velocity, id, out banana.landedRotation);
        bananas[id] = banana;
    }

    public static void Remove(int id, uint slipperNetId)
    {
        if (!bananas.TryGetValue(id, out Banana banana) || banana.state == State.FlyingAway)
        {
            return;
        }
        if (slipperNetId == 0)
        {
            Destroy(banana);
            return;
        }
        Vector3 direction = banana.go.transform.forward;
        if (NetworkClient.spawned.TryGetValue(slipperNetId, out NetworkIdentity slipper) && slipper != null)
        {
            direction = slipper.transform.forward;
        }
        StartFlyAway(banana, direction);
    }

    public static void ClearAll()
    {
        foreach (Banana banana in bananas.Values)
        {
            if (banana.go != null)
            {
                Object.Destroy(banana.go);
            }
        }
        bananas.Clear();
    }

    public static void Tick()
    {
        if (bananas.Count == 0)
        {
            return;
        }
        float now = Time.time;
        foreach (Banana banana in bananas.Values)
        {
            if (banana.go == null)
            {
                toDelete.Add(banana.id); // destroyed with its scene
                continue;
            }
            switch (banana.state)
            {
                case State.Flying:
                    UpdateFlight(banana, now);
                    break;
                case State.FlyingAway:
                    if (UpdateFlyAway(banana, now))
                    {
                        toDelete.Add(banana.id);
                    }
                    break;
            }
        }
        foreach (int id in toDelete)
        {
            if (bananas.TryGetValue(id, out Banana banana))
            {
                Destroy(banana);
            }
        }
        toDelete.Clear();

        CheckLocalPlayerSlip(now);
    }

    private static void UpdateFlight(Banana banana, float now)
    {
        float t = now - banana.spawnTime;
        int index = Mathf.FloorToInt(t / SimStep);
        if (index >= banana.path.Count - 1)
        {
            banana.go.transform.SetPositionAndRotation(banana.path[banana.path.Count - 1], banana.landedRotation);
            banana.state = State.Landed;
            banana.landTime = now;
            return;
        }
        float frac = t / SimStep - index;
        banana.go.transform.position = Vector3.Lerp(banana.path[index], banana.path[index + 1], frac);
        banana.go.transform.rotation = Quaternion.Euler(t * 540f, t * 220f, 0f); // tumble in the air
    }

    private static bool UpdateFlyAway(Banana banana, float now)
    {
        float dt = Time.deltaTime;
        banana.flyAwayVelocity += Physics.gravity * dt;
        Transform t = banana.go.transform;
        t.position += banana.flyAwayVelocity * dt;
        t.Rotate(banana.flyAwaySpin * dt, Space.World);
        float age = now - banana.landTime;
        t.localScale = Vector3.one * Mathf.Lerp(1f, 0.2f, Mathf.Clamp01(age / FlyAwayDuration));
        return age >= FlyAwayDuration;
    }

    private static void StartFlyAway(Banana banana, Vector3 direction)
    {
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        banana.state = State.FlyingAway;
        banana.landTime = Time.time;
        banana.flyAwayVelocity = direction * 3f + Vector3.up * 5f;
        banana.flyAwaySpin = new Vector3(720f, 300f, 0f);
    }

    private static void CheckLocalPlayerSlip(float now)
    {
        PlayerInfo player = GameManager.LocalPlayerInfo;
        if (player == null || player.Movement == null)
        {
            return;
        }
        PlayerMovement movement = player.Movement;
        if (!movement.IsGrounded || movement.IsKnockedOutOrRecovering || !movement.IsVisible || player.ActiveGolfCartSeat.IsValid())
        {
            return;
        }
        Vector3 horizontalVelocity = player.Rigidbody.linearVelocity;
        horizontalVelocity.y = 0f;
        float minSpeed = BananaPeelItem.MinSlipSpeed.Value;
        if (horizontalVelocity.sqrMagnitude < minSpeed * minSpeed)
        {
            return;
        }

        Vector3 feet = player.transform.position;
        float radius = BananaPeelItem.SlipRadius.Value * BananaPeelItem.GroundScale.Value;
        foreach (Banana banana in bananas.Values)
        {
            if (banana.state != State.Landed)
            {
                continue;
            }
            bool isOwner = banana.ownerNetId == player.netId;
            float armDelay = isOwner ? BananaPeelItem.OwnerArmDelay.Value : BananaPeelItem.ArmDelay.Value;
            if (now - banana.landTime < armDelay)
            {
                continue;
            }
            Vector3 offset = feet - banana.go.transform.position;
            if (Mathf.Abs(offset.y) > 0.9f || offset.x * offset.x + offset.z * offset.z > radius * radius)
            {
                continue;
            }
            Slip(player, banana, horizontalVelocity.normalized);
            return;
        }
    }

    private static void Slip(PlayerInfo player, Banana banana, Vector3 direction)
    {
        PlayerInfo owner = null;
        if (NetworkClient.spawned.TryGetValue(banana.ownerNetId, out NetworkIdentity ownerIdentity) && ownerIdentity != null)
        {
            owner = ownerIdentity.GetComponent<PlayerInfo>();
        }

        Vector3 launch = direction * BananaPeelItem.SlipForwardBoost.Value + Vector3.up * BananaPeelItem.SlipUpBoost.Value;
        Vector3 localOrigin = player.transform.InverseTransformPoint(banana.go.transform.position);
        // A leg-sweep swing knockout is the game's "tripped" state: right animation, normal knockout rules
        // (shields and knockout immunity still protect you).
        bool knockedOut = player.Movement.TryKnockOut(owner, KnockoutType.Swing, isLegSweep: true, localOrigin, 0f, launch,
            ElectromagnetShieldHitBlockType.FullyBlocked, ItemUseId.Invalid, fromSpecialState: false, canFallbackToUnground: true,
            out _, out _);
        if (knockedOut)
        {
            player.Rigidbody.AddForce(launch, ForceMode.VelocityChange);
        }
        Plugin.Log.LogInfo($"Slipped on banana {banana.id} (knocked out: {knockedOut}).");

        StartFlyAway(banana, direction);
        BananaNet.RequestSlip(banana.id);
    }

    /// <summary>Deterministic flight: fixed-step ballistic sim against static geometry.</summary>
    private static List<Vector3> ComputePath(Vector3 start, Vector3 velocity, int id, out Quaternion landedRotation)
    {
        var path = new List<Vector3> { start };
        int mask = GetGroundMask();
        Vector3 position = start;
        Vector3 v = velocity;
        int maxSteps = Mathf.CeilToInt(MaxFlightTime / SimStep);
        float yaw = (id * 137.508f) % 360f; // same "random" yaw on every client
        for (int i = 0; i < maxSteps; i++)
        {
            v += Physics.gravity * SimStep;
            Vector3 next = position + v * SimStep;
            if (Physics.Linecast(position, next, out RaycastHit hit, mask, QueryTriggerInteraction.Ignore))
            {
                if (hit.normal.y < 0.55f)
                {
                    // Hit a wall or steep slope: bounce off and keep falling.
                    v = Vector3.Reflect(v, hit.normal) * 0.3f;
                    position = hit.point + hit.normal * 0.02f;
                    path.Add(position);
                    continue;
                }
                path.Add(hit.point);
                Vector3 forward = Vector3.ProjectOnPlane(Quaternion.Euler(0f, yaw, 0f) * Vector3.forward, hit.normal).normalized;
                landedRotation = Quaternion.LookRotation(forward, hit.normal);
                return path;
            }
            position = next;
            path.Add(position);
        }
        // Fell off the world: land wherever the sim ended (it will be far below and harmless).
        landedRotation = Quaternion.Euler(0f, yaw, 0f);
        return path;
    }

    private static int groundMask = -1;

    private static int GetGroundMask()
    {
        if (groundMask != -1)
        {
            return groundMask;
        }
        LayerSettings layers = GameManager.LayerSettings;
        if (layers == null)
        {
            return Physics.DefaultRaycastLayers;
        }
        int mask = layers.PlayerGroundableMask | layers.TerrainMask;
        mask &= ~layers.PlayersMask & ~layers.GolfCartsMask & ~layers.BallMask & ~layers.DynamicBallMask;
        mask &= ~(1 << layers.ItemsLayer) & ~(1 << layers.HittablesLayer) & ~(1 << layers.VfxLayer);
        groundMask = mask;
        Plugin.Log.LogInfo($"Banana ground mask: 0x{mask:X8}");
        return mask;
    }

    private static void Destroy(Banana banana)
    {
        if (banana.go != null)
        {
            Object.Destroy(banana.go);
        }
        bananas.Remove(banana.id);
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
                BananaNet.ResetServer();
            }
        };
    }
}

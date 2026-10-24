using HarmonyLib;
using UnityEngine;

namespace ItemPack.Items;

/// <summary>
/// While you channel a Kamehameha you float: the ground lets go, gravity stops, and walking moves you the way the camera
/// looks (so looking up and walking forward climbs, looking down descends). It's slower than walking, and it's enough to
/// cross a gap. Runs after the game's own movement step, so what it sets is what the physics step uses.
/// </summary>
[HarmonyPatch(typeof(PlayerMovement), "FixedUpdate")]
internal static class KameLevitate
{
    private const float Acceleration = 24f;
    private const float RiseSpeed = 3.5f;

    private static readonly AccessTools.FieldRef<PlayerMovement, Vector3> WorldMove =
        AccessTools.FieldRefAccess<PlayerMovement, Vector3>("worldMoveVector3d");

    private static Vector3 velocity;
    private static bool wasLevitating;

    private static void Postfix(PlayerMovement __instance)
    {
        PlayerInfo local = GameManager.LocalPlayerInfo;
        bool active = KamehamehaItem.Charging && KamehamehaItem.Levitate.Value && local != null && local.Movement == __instance
            && __instance.IsVisible && !__instance.IsKnockedOutOrRecovering && !local.ActiveGolfCartSeat.IsValid();
        if (!active)
        {
            wasLevitating = false;
            return;
        }
        Camera cam = GameManager.Camera;
        if (cam == null)
        {
            return;
        }
        Rigidbody body = local.Rigidbody;
        if (!wasLevitating)
        {
            wasLevitating = true;
            velocity = body.linearVelocity;
        }

        // The walk input (already turned to the camera's yaw by the game), split into forward and sideways.
        Vector3 move = WorldMove(__instance);
        Vector3 look = cam.transform.forward;
        Vector3 flatForward = Vector3.ProjectOnPlane(look, Vector3.up).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, flatForward);
        float forwardInput = Vector3.Dot(move, flatForward);
        float sideInput = Vector3.Dot(move, right);
        Vector3 target = (look * forwardInput + right * sideInput) * KamehamehaItem.LevitateSpeed.Value;

        // Never sink into the ground: below the hover height over solid ground, rise toward it.
        if (Physics.Raycast(body.position + Vector3.up * 0.5f, Vector3.down, out RaycastHit ground, 10f, WorldMask.Solid, QueryTriggerInteraction.Ignore))
        {
            float height = ground.distance - 0.5f;
            float hover = KamehamehaItem.HoverHeight.Value;
            if (height < hover)
            {
                target.y = Mathf.Max(target.y, Mathf.Min(RiseSpeed, (hover - height) * 4f));
            }
        }

        if (__instance.IsGrounded)
        {
            __instance.Unground();
            velocity.y = Mathf.Max(velocity.y, 1f);
        }
        velocity = Vector3.MoveTowards(velocity, target, Acceleration * Time.fixedDeltaTime);
        body.linearVelocity = velocity;
    }
}

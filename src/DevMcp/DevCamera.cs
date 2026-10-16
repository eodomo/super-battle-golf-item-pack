#if DEVTOOLS
using HarmonyLib;
using UnityEngine;

namespace ItemPack.DevMcp;

/// <summary>A free camera for the dev MCP server: while set, it overrides the game camera's pose after the game moves it.</summary>
internal static class DevCamera
{
    public static bool Free { get; private set; }
    public static Vector3 Position { get; private set; }
    public static Quaternion Rotation { get; private set; }
    public static float? Fov { get; private set; }

    public static void SetFree(Vector3 position, Quaternion rotation, float? fov)
    {
        Free = true;
        Position = position;
        Rotation = rotation;
        Fov = fov;
    }

    public static void Release()
    {
        Free = false;
        Fov = null;
    }

    /// <summary>Points the orbit camera (what the player aims with) at a world point. False if there is no orbit camera.</summary>
    public static bool AimOrbitAt(Vector3 target, out string info)
    {
        info = null;
        if (!CameraModuleController.TryGetOrbitModule(out OrbitCameraModule orbit) || Camera.main == null)
        {
            info = "No orbit camera.";
            return false;
        }
        // The camera's position depends on yaw/pitch (it orbits the player), so aim from where it is, let the module
        // recompute its pose, and repeat until the forward ray passes through the target.
        float yaw = orbit.Yaw, pitch = orbit.Pitch, error = 180f;
        orbit.ForceUpdateModule();
        for (int i = 0; i < 12 && error > 0.1f; i++)
        {
            Vector3 direction = target - orbit.position;
            yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
            pitch = -Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg;
            orbit.SetYaw(yaw);
            orbit.SetPitch(pitch);
            orbit.ForceUpdateModule();
            error = Vector3.Angle(orbit.rotation * Vector3.forward, target - orbit.position);
        }
        info = $"orbit yaw {yaw:0.0}, pitch {pitch:0.0} (positive = below horizon), off by {error:0.00} deg";
        return true;
    }
}

[HarmonyPatch(typeof(CameraModuleController), nameof(CameraModuleController.OnPreLateBUpdate))]
internal static class DevCameraPatch
{
    private static void Postfix(CameraModuleController __instance)
    {
        if (!DevCamera.Free)
        {
            return;
        }
        __instance.transform.SetPositionAndRotation(DevCamera.Position, DevCamera.Rotation);
        if (DevCamera.Fov.HasValue)
        {
            __instance.GetComponent<Camera>().fieldOfView = DevCamera.Fov.Value;
        }
    }
}
#endif

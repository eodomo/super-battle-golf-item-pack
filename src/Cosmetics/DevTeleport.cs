#if DEVTOOLS
using UnityEngine;

namespace ItemPack;

/// <summary>Dev-only: Home teleports the local player to the outfit house's door.</summary>
internal static class DevTeleport
{
    public static void ToWardrobe()
    {
        PlayerCustomizationBuilding building = Object.FindFirstObjectByType<PlayerCustomizationBuilding>();
        if (building == null || GameManager.LocalPlayerMovement == null)
        {
            Plugin.Log.LogWarning("Teleport: no outfit house or local player here.");
            return;
        }
        Transform exit = building.exitPosition;
        Vector3 spot = exit.position + exit.forward * 1.2f;
        Quaternion face = Quaternion.LookRotation(-exit.forward, Vector3.up);
        GameManager.LocalPlayerMovement.Teleport(spot, face, true);
        Plugin.Log.LogInfo("Teleported to the outfit house door.");
    }
}
#endif

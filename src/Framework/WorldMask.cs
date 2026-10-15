using UnityEngine;

namespace ItemPack;

/// <summary>Physics layer masks shared by items that raycast against the course.</summary>
internal static class WorldMask
{
    private static int solid = -1;

    /// <summary>World geometry (terrain, walls, buildings), not players, balls, carts, items or foliage.</summary>
    public static int Solid
    {
        get
        {
            if (solid == -1)
            {
                LayerSettings layers = GameManager.LayerSettings;
                int mask = Physics.DefaultRaycastLayers;
                mask &= ~layers.PlayersMask & ~layers.GolfCartsMask & ~layers.BallMask & ~layers.DynamicBallMask & ~layers.FoliageMask;
                mask &= ~(1 << layers.ItemsLayer) & ~(1 << layers.VfxLayer) & ~(1 << layers.ElectromagnetShieldLayer)
                    & ~(1 << layers.GolfCartPassengersLayer) & ~(1 << layers.HittablesLayer);
                solid = mask;
            }
            return solid;
        }
    }
}

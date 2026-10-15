using HarmonyLib;
using Mirror;
using UnityEngine;

namespace ItemPack.Items;

// Any damage breaks dirt blocks. Blocks aren't networked "hittables", so the game's weapons don't know about
// them: these hooks catch each kind of damage where it happens and break the blocks it reaches.
// Area damage (explosions, lasers, lightning) runs on the host, which owns the blocks; hitscan shots run on
// the shooter's client, which asks the host to break the block it hit.
// The pack's own items do the same from their code (Explosions.ServerExplode, the kamehameha, the sniper).

[HarmonyPatch(typeof(Rocket), nameof(Rocket.ServerExplode))]
internal static class RocketBreaksDirt
{
    private static void Postfix(Vector3 worldPosition) =>
        DirtWorld.ServerBreakInSphere(worldPosition, GameManager.ItemSettings.RocketExplosionRange);
}

[HarmonyPatch(typeof(Landmine), nameof(Landmine.ServerExplode))]
internal static class LandmineBreaksDirt
{
    private static void Postfix(Landmine __instance) =>
        DirtWorld.ServerBreakInSphere(__instance.transform.position, GameManager.ItemSettings.LandmineExplosionRange);
}

[HarmonyPatch(typeof(FreezeBomb), nameof(FreezeBomb.ServerExplode))]
internal static class FreezeBombBreaksDirt
{
    private static void Postfix(Vector3 worldPosition) =>
        DirtWorld.ServerBreakInSphere(worldPosition, GameManager.ItemSettings.FreezeBombExplosionRange);
}

[HarmonyPatch(typeof(OrbitalLaser), nameof(OrbitalLaser.OnFixedBUpdate))]
internal static class OrbitalLaserBreaksDirt
{
    private static void Postfix(OrbitalLaser __instance)
    {
        if (!__instance.isServer || __instance.state != OrbitalLaserState.Exploding)
        {
            return;
        }
        ItemSettings settings = GameManager.ItemSettings;
        Vector3 ground = __instance.transform.position;
        DirtWorld.ServerBreakInCapsule(ground, ground + Vector3.up * settings.OrbitalLaserExplosionLaserHeight, settings.OrbitalLaserExplosionLaserRadius);
        DirtWorld.ServerBreakInSphere(ground, settings.OrbitalLaserExplosionMaxRange);
    }
}

[HarmonyPatch(typeof(ThunderstormLightningStrike), nameof(ThunderstormLightningStrike.PlayLightningStrikeEffectsInternal))]
internal static class LightningBreaksDirt
{
    // Runs on every client when lightning strikes; only the host breaks blocks.
    private static void Postfix(ThunderstormLightningStrike __instance)
    {
        if (!NetworkServer.active)
        {
            return;
        }
        ItemSettings settings = GameManager.ItemSettings;
        Vector3 position = __instance.transform.position;
        DirtWorld.ServerBreakInSphere(position, settings.ThunderstormLightningStrikeHitRadius);
        DirtWorld.ServerBreakInCapsule(position, position + Vector3.up * 60f, settings.ThunderstormLightningStrikeVaporizationRadius);
    }
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.UserCode_CmdPlayRailgunShotEffectsForAllClients__ItemAimPoint__Boolean__NetworkConnectionToClient))]
internal static class RailgunBreaksDirt
{
    // The host hears about every railgun shot (to show its beam): break blocks along the beam.
    private static void Postfix(PlayerInventory __instance, ItemAimPoint beamEndPoint)
    {
        Vector3 start = __instance.GetRailgunBeamOriginPosition();
        DirtWorld.ServerBreakInCapsule(start, beamEndPoint.GetEffectiveWorldPoint(), 0.6f);
    }
}

[HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.TryParseFirearmRaycastResults))]
internal static class GunshotBreaksDirt
{
    // Dueling pistol and elephant gun shots (on the shooter's client): if the first thing the bullet hit is a
    // block, break it.
    private static void Postfix(PlayerInventory __instance, ref RaycastHit raycastHit)
    {
        if (__instance.isLocalPlayer)
        {
            DirtWorld.TryBreakHit(raycastHit.collider);
        }
    }
}

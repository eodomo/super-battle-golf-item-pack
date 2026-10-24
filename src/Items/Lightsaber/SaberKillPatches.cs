using HarmonyLib;

namespace ItemPack.Items;

/// <summary>
/// A saber hit is a railgun hit (so it kills, sending the victim back to their spawn), tagged with the saber's own item
/// type in its use id. The host plays the railgun's big hit explosion on whoever a railgun kills; for a saber kill it
/// doesn't, since the victim falls apart in two instead (<see cref="SaberSlicer"/>).
/// </summary>
[HarmonyPatch(typeof(PlayerGolfer), "OnServerWasHitByItem")]
internal static class SaberKillPatch
{
    internal static bool InSaberKill;

    private static void Prefix(ItemType itemType, ItemUseId itemUseId) =>
        InSaberKill = itemType == ItemType.Railgun && LightsaberItem.IsLightsaber(itemUseId.itemType);

    private static void Finalizer() => InSaberKill = false;
}

[HarmonyPatch(typeof(Hittable), nameof(Hittable.PlayRailgunHitEffectsForAllClients))]
internal static class SaberKillEffectPatch
{
    private static bool Prefix() => !SaberKillPatch.InSaberKill;
}

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.ResourceManagement;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace ItemPack;

/// <summary>
/// Adds the pack's clothing to the game's own cosmetics catalog. The outfit menu discovers items through the
/// Addressables label "cosmetics-metadata" and loads everything (metadata, models, variation textures) by key, so
/// a locator + provider that answer for our keys is all it takes for the items to behave like built-in ones.
/// Keys of models and textures must be GUID strings (AssetReference.RuntimeKeyIsValid parses them).
/// </summary>
internal static class CosmeticCatalog
{
    private const string MetadataLabel = "cosmetics-metadata";
    private const string ProviderId = "ItemPack.CosmeticProvider";

    /// <summary>Hands the loaded object to a waiting load request (may be called later, e.g. after a GPU readback).</summary>
    public delegate void Resolver(Action<object> deliver);

    private static readonly Dictionary<string, ResourceLocationBase> locations = new();
    private static readonly Dictionary<string, Resolver> resolvers = new();
    private static readonly Dictionary<string, object> resolved = new();
    private static readonly HashSet<string> waitingKeys = new();
    private static readonly Dictionary<string, List<Action<object>>> waiting = new();
    private static readonly List<IResourceLocation> metadataLocations = new();
    private static readonly HashSet<string> packGuids = new();
    private static bool installed;

    public static bool IsPackGuid(string guid) => guid != null && packGuids.Contains(guid);

    /// <summary>Deterministic GUID (32 hex digits) for a name, so keys stay the same between runs and machines.</summary>
    public static string MakeGuid(string name)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes("itempack:" + name));
        return new Guid(hash).ToString("N");
    }

    internal static void AddKey(string key, Type type, Resolver resolver)
    {
        var location = new ResourceLocationBase(key, key, ProviderId, type);
        locations[key] = location;
        resolvers[key] = resolver;
    }

    /// <summary>Registers a metadata asset under its path-like key and its GUID (the game loads it by either).</summary>
    public static void AddMetadata(string path, PlayerCosmeticsMetadata metadata)
    {
        object boxed = metadata;
        AddKey(path, typeof(PlayerCosmeticsMetadata), deliver => deliver(boxed));
        AddKey(metadata.PersistentGuid, typeof(PlayerCosmeticsMetadata), deliver => deliver(boxed));
        metadataLocations.Add(locations[path]);
        packGuids.Add(metadata.PersistentGuid);
    }

    public static void AddModel(string guid, Resolver resolver) => AddKey(guid, typeof(GameObject), resolver);

    public static void AddTexture(string guid, Func<Texture2D> create) =>
        AddKey(guid, typeof(Texture2D), deliver => deliver(create()));

    public static void Install()
    {
        if (installed)
        {
            return;
        }
        installed = true;
        var provider = new PackProvider();
        Addressables.ResourceManager.ResourceProviders.Add(provider);
        Addressables.AddResourceLocator(new PackLocator());
        RangerSet.Prebuild();
        Plugin.Log.LogInfo($"Cosmetics catalog installed: {metadataLocations.Count} items, {locations.Count} keys.");
    }

    public static bool CanInstall => !installed && Addressables.ResourceLocators.GetEnumerator().MoveNext();

    private static void Resolve(string key, Action<object> deliver)
    {
        if (resolved.TryGetValue(key, out object done))
        {
            deliver(done);
            return;
        }
        if (!resolvers.TryGetValue(key, out Resolver resolver))
        {
            deliver(null);
            return;
        }
        if (!waiting.TryGetValue(key, out List<Action<object>> list))
        {
            waiting[key] = list = new List<Action<object>>();
        }
        list.Add(deliver);
        if (!waitingKeys.Add(key))
        {
            return; // already being built
        }
        try
        {
            resolver(result =>
            {
                if (result != null)
                {
                    resolved[key] = result;
                }
                waitingKeys.Remove(key);
                List<Action<object>> callbacks = waiting[key];
                waiting.Remove(key);
                foreach (Action<object> callback in callbacks)
                {
                    callback(result);
                }
            });
        }
        catch (Exception e)
        {
            Plugin.Log.LogError($"Building cosmetic asset {key} failed: {e}");
            waitingKeys.Remove(key);
            List<Action<object>> callbacks = waiting[key];
            waiting.Remove(key);
            foreach (Action<object> callback in callbacks)
            {
                callback(null);
            }
        }
    }

    private sealed class PackProvider : ResourceProviderBase
    {
        public override string ProviderId => CosmeticCatalog.ProviderId;

        public override void Provide(ProvideHandle provideHandle)
        {
            string key = provideHandle.Location.InternalId;
            Resolve(key, result =>
            {
                if (result != null)
                {
                    provideHandle.Complete(result, true, null);
                }
                else
                {
                    provideHandle.Complete<object>(null, false, new Exception("Item Pack cosmetic asset unavailable: " + key));
                }
            });
        }
    }

    private sealed class PackLocator : IResourceLocator
    {
        public string LocatorId => "ItemPackCosmeticsLocator";

        public IEnumerable<object> Keys
        {
            get
            {
                yield return MetadataLabel;
                foreach (string key in locations.Keys)
                {
                    yield return key;
                }
            }
        }

        public IEnumerable<IResourceLocation> AllLocations => new List<IResourceLocation>(locations.Values);

        public bool Locate(object key, Type type, out IList<IResourceLocation> result)
        {
            result = null;
            string name = key as string;
            if (name == null)
            {
                return false;
            }
            if (name == MetadataLabel)
            {
                if (type != null && type != typeof(object) && !type.IsAssignableFrom(typeof(PlayerCosmeticsMetadata)))
                {
                    return false;
                }
                result = new List<IResourceLocation>(metadataLocations);
                return result.Count > 0;
            }
            if (!locations.TryGetValue(name, out ResourceLocationBase location))
            {
                return false;
            }
            if (type != null && type != typeof(object) && !type.IsAssignableFrom(location.ResourceType))
            {
                return false;
            }
            result = new List<IResourceLocation> { location };
            return true;
        }
    }
}

/// <summary>Everything in the pack's cosmetic set counts as owned: no credits are needed for it.</summary>
[HarmonyPatch(typeof(CosmeticsUnlocksManager), "IsOwnedInternal")]
internal static class FreeCosmeticsPatch
{
    private static void Postfix(string guid, ref bool __result)
    {
        if (CosmeticCatalog.IsPackGuid(guid))
        {
            __result = true;
        }
    }
}

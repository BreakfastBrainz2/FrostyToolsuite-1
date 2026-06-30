using Frosty.Core;
using Frosty.Core.Controls;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace AssetBankPlugin.Ant
{
    public class AntRefTable
    {
        // ConcurrentDictionary because Add() is called from multiple concurrent
        // bundle-loading tasks. Plain Dictionary under concurrent writes corrupts
        // its internal chain pointers, forcing expensive ntdll heap recovery.
        public static ConcurrentDictionary<Guid, Guid> InternalRefs = new ConcurrentDictionary<Guid, Guid>();
        public static ConcurrentDictionary<Guid, AntAsset> Refs = new ConcurrentDictionary<Guid, AntAsset>();

        public static void Add(AntAsset asset)
        {
            Refs[asset.ID] = asset;
        }

        public static AntAsset Get(Guid refId, bool recurse = false)
        {
            // Immediately discard Empty GUIDs to prevent unnecessary cache checks.
            if (refId == Guid.Empty)
                return null;

            // 1. Primary lookup: try to get the asset directly.
            if (Refs.TryGetValue(refId, out var asset))
                return asset;

            // 2. Internal lookup: map the GUID to an internal GUID, then fetch.
            if (InternalRefs.TryGetValue(refId, out var internalId))
            {
                Refs.TryGetValue(internalId, out var internalAsset);
                return internalAsset; // null if still not loaded, caller handles it
            }

            // 3. Cache & bundle loading phase.
            if (recurse)
                return null;

            int bundleId = -1;

            if (Cache.AntStateBundleIndices.TryGetValue(refId, out int directBundleId))
            {
                bundleId = directBundleId;
            }
            else if (Cache.AntRefMap.TryGetValue(refId, out var mappedId) &&
                     Cache.AntStateBundleIndices.TryGetValue(mappedId, out int mappedBundleId))
            {
                bundleId = mappedBundleId;
            }
            else
            {
                // Unknown GUID - not a critical error, caller will handle null.
                return null;
            }

            var bundle = App.AssetManager.GetBundleEntry(bundleId);
            AntStateAssetDefinition.LoadAntStateFromBundle(bundle);

            return Get(refId, true);
        }
    }
}
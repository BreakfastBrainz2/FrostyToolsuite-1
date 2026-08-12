using Frosty.Core;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using System;
using System.Collections.Generic;

namespace PvZBundleManagerPlugin.Ports.Classes.AssetHandlers.Common
{
    public class NewWaveAssetHandler : BaseAssetHandler
    {
        public override string AssetType => "NewWaveAsset";

        public override bool AddToBundle(EbxAssetEntry entry, BundleEntry bentry)
        {
            if (!base.AddToBundle(entry, bentry))
                return false;

            EbxAsset asset = App.AssetManager.GetEbx(entry);
            dynamic soundAsset = asset.RootObject;

            foreach (dynamic soundDataChunk in soundAsset.Chunks)
            {
                ChunkAssetEntry chunkEntry = App.AssetManager.GetChunkEntry(soundDataChunk.ChunkId);
                chunkEntry.AddToBundle(App.AssetManager.GetBundleId(bentry));
                entry.LinkAsset(chunkEntry);
            }

            var resEntry = App.AssetManager.GetResEntry(entry.Name);
            resEntry.AddToBundle(App.AssetManager.GetBundleId(bentry));
            entry.LinkAsset(resEntry);

            return true;
        }

        //create method not implemented

        public override bool RemoveFromBundle(EbxAssetEntry entry, BundleEntry bundle)
        {
            if (!base.RemoveFromBundle(entry, bundle))
                return false;

            EbxAsset soundasset = App.AssetManager.GetEbx(entry);
            dynamic soundobject = soundasset.RootObject;

            foreach (var soundChunk in soundobject.Chunks)
            {
                ChunkAssetEntry ChunkEntry = App.AssetManager.GetChunkEntry(soundChunk.ChunkId);
                ChunkEntry.AddedBundles.Remove(App.AssetManager.GetBundleId(bundle));
                entry.LinkAsset(ChunkEntry);
            }

            var resEntry = App.AssetManager.GetResEntry(entry.Name);
            resEntry.AddedBundles.Remove(App.AssetManager.GetBundleId(bundle));
            entry.LinkAsset(resEntry);

            return true;
        }
    }
}

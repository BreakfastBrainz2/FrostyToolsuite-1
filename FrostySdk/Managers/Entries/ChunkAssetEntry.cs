using System;
using System.Collections.Generic;

namespace FrostySdk.Managers.Entries;

public class ChunkAssetEntry : AssetEntry
{
    public override string Type => "Chunk";

    public override string AssetType => "chunk";

    /// <summary>
    /// Id of this chunk.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Offset of the FirstMip if this is a chunk for a texture, else it's 0.
    /// </summary>
    public uint LogicalOffset { get; internal set; }

    /// <summary>
    /// Size of the chunk from the FirstMip if this is a chunk for a texture, else it's the size of the chunk.
    /// </summary>
    public uint LogicalSize { get; internal set; }

    /// <summary>
    /// SuperBundles that contain this <see cref="ChunkAssetEntry"/>.
    /// </summary>
    public readonly HashSet<int> SuperBundleInstallChunks = new();
    public List<int> AddedSuperBundles = new List<int>();

    public ChunkAssetEntry(Guid inChunkId, Sha1 inSha1, uint inLogicalOffset, uint inLogicalSize, params int[] superBundleIds)
        : base(inSha1, inLogicalOffset + inLogicalSize)
    {
        Id = inChunkId;
        Name = Id.ToString();
        LogicalOffset = inLogicalOffset;
        LogicalSize = inLogicalSize;
        SuperBundleInstallChunks.UnionWith(superBundleIds);
    }

    /// <summary>
    /// Adds the current asset to the specified superbundle
    /// </summary>
    public bool AddToSuperBundle(int sbId)
    {
        if (IsInSuperBundle(sbId))
        {
            return false;
        }

        AddedSuperBundles.Add(sbId);
        IsDirty = true;

        return true;
    }

    /// <summary>
    /// Returns true if asset is in the specified superbundle
    /// </summary>
    public bool IsInSuperBundle(int sbId) => SuperBundleInstallChunks.Contains(sbId);
}
using System;
using System.Collections.Generic;

namespace FrostySdk.Managers.Info;

public class SuperBundleInstallChunk
{
    public string Name { get; }

    public int Id { get; }

    public SuperBundleInfo SuperBundle { get; }

    public InstallChunkInfo InstallChunk { get; }

    public InstallChunkType Type { get; }

    public readonly Dictionary<string, BundleInfo> BundleMapping = new(StringComparer.OrdinalIgnoreCase);

    public SuperBundleInstallChunk(SuperBundleInfo inSuperBundle, InstallChunkInfo inInstallChunk, InstallChunkType inType)
    {
        SuperBundle = inSuperBundle;
        InstallChunk = inInstallChunk;
        Type = inType;

        Name = Type == InstallChunkType.Split
            ? $"{InstallChunk.InstallBundle}{SuperBundle.Name[SuperBundle.Name.IndexOf('/')..]}"
            : SuperBundle.Name;
        Id = Utils.HashString(Name, true);
    }
}
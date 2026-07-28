using Frosty.Hash;
using FrostySdk;
using FrostySdk.IO;
using FrostySdk.Managers;
using FrostySdk.Managers.Entries;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Frosty.ModSupport
{
    public partial class FrostyModExecutor
    {
        [Flags]
        private enum Flags
        {
            HasBaseBundles = 1, // if the base toc has bundles that the patch doesnt have
            HasBaseChunks = 2, // if the base toc has chunks that the patch doesnt have
            HasCompressedNames = 4 // if the bundle names are huffman encoded
        }

        private class CasBundleAction
        {
            [Flags]
            private enum InternalFlags : byte
            {
                HasCompressedStrings = 1 << 0,
                HasInlineSb = 1 << 1,
                HasInlineBundle = 1 << 2,
            }

            private struct CasFileInfo
            {
                public bool IsPatch;
                public byte CatalogIndex;
                public byte CasIndex;
                public uint Offset;
                public uint Size;
            }

            private class BundleInfo
            {
                public string Name;
                public uint NameOffset;
                public long Offset;
                public uint Size;
                public bool IsModified;
                public bool IsPatch;
                public string SbName;
                public int SplitIndex;
            }

            private class ChunkInfo
            {
                public Guid Guid;
                public bool IsPatch;
                public CasFileInfo CasFileInfo;
                public string SbName;
                public int SplitIndex;
            }

            private static readonly object locker = new object();
            private static readonly Comparison<List<byte[]>> s_hashDictComparer = (x, y) => y.Count.CompareTo(x.Count);

            public SuperBundleInfo SuperBundleInfo;
            public bool HasErrored => Exception != null;
            public Exception Exception { get; private set; }
            public bool HasSb = true;

            private ManualResetEvent doneEvent;
            private FrostyModExecutor parent;
            private string m_catalog;

            public static Dictionary<string, int> CasFiles = new Dictionary<string, int>();
            //private static Dictionary<string, NativeWriter> CasWriters = new Dictionary<string, NativeWriter>();

            public CasBundleAction(SuperBundleInfo inSb, ManualResetEvent inDoneEvent, FrostyModExecutor inParent)
            {
                SuperBundleInfo = inSb;
                parent = inParent;
                doneEvent = inDoneEvent;

                m_catalog = GetCatalog(SuperBundleInfo, parent.m_fs.EnumerateCatalogInfos());
            }

            private void Run()
            {
                try
                {
                    NativeWriter casWriter = null;
                    int casFileIndex = 0;

                    Dictionary<int, BundleInfo> bundles = new Dictionary<int, BundleInfo>();
                    Dictionary<Guid, ChunkInfo> chunks = new Dictionary<Guid, ChunkInfo>();
                    InternalFlags tocFlags = 0;

                    bool isDefaultTocModified = false;
                    bool[] isSplitTocModified = new bool[SuperBundleInfo.SplitSuperBundles.Count];

                    // read default toc
                    bool isPatch = true;
                    string tocPath = parent.m_fs.ResolvePath(string.Format("native_patch/{0}.toc", SuperBundleInfo.Name));
                    if (tocPath.Equals(string.Empty))
                    {
                        tocPath = parent.m_fs.ResolvePath(string.Format("native_data/{0}.toc", SuperBundleInfo.Name));
                        isPatch = false;
                    }

                    if (!tocPath.Equals(string.Empty))
                    {
                        using (NativeReader reader = new NativeReader(new FileStream(tocPath, FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator()))
                        {
                            ReadToc(reader, ref bundles, ref chunks, ref tocFlags, isPatch);
                        }
                    }

                    // read split toc's
                    for (int splitIndex = 0; splitIndex < SuperBundleInfo.SplitSuperBundles.Count; splitIndex++)
                    {
                        string sbPath = SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[splitIndex]);

                        // parse superbundle toc files from patch and data
                        isPatch = true;
                        tocPath = parent.m_fs.ResolvePath(string.Format("native_patch/{0}.toc", sbPath));
                        if (tocPath.Equals(string.Empty))
                        {
                            tocPath = parent.m_fs.ResolvePath(string.Format("native_data/{0}.toc", sbPath));
                            isPatch = false;
                        }

                        if (!tocPath.Equals(string.Empty))
                        {
                            using (NativeReader reader = new NativeReader(new FileStream(tocPath, FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator()))
                            {
                                ReadToc(reader, ref bundles, ref chunks, ref tocFlags, isPatch, splitIndex);
                            }
                        }
                    }

                    string modPath = parent.m_fs.BasePath + parent.m_modDirName + "\\" + parent.m_patchPath;

                    // TODO: newer games use little endian
                    Endian endian = Endian.Big;
                    if (ProfilesLibrary.DataVersion >= (int)ProfileVersion.Fifa21)
                    {
                        endian = Endian.Little;
                    }

                    BinarySbWriter modWriter = null;
                    BinarySbWriter modDefaultWriter = new BinarySbWriter(new MemoryStream(), inEndian: endian);
                    List<BinarySbWriter> modSplitWriters = null;
                    if (SuperBundleInfo.SplitSuperBundles.Count > 0)
                    {
                        modSplitWriters = new List<BinarySbWriter>();
                        foreach (string catalog in SuperBundleInfo.SplitSuperBundles)
                        {
                            modSplitWriters.Add(new BinarySbWriter(new MemoryStream(), inEndian: endian));
                        }
                    }

                    // reading in unmodified data and modifying it
                    {
                        // Ensure tocFlags accurately reflects the SuperBundle's format (Inline Sb / Inline Bundles)
                        // before processing any added/modified bundles, by legitimately parsing the first available bundle header.
                        if (bundles.Count > 0)
                        {
                            BundleInfo firstBundle = bundles.Values.First();
                            long offset = firstBundle.Offset;
                            uint size = firstBundle.Size;

                            if ((size & 0xC0000000) == 0x40000000)
                            {
                                tocFlags |= InternalFlags.HasInlineSb;
                                HasSb = false;
                                size &= ~0xC0000000;
                                offset += 0x22C;
                            }

                            string readerPath = firstBundle.IsPatch
                                ? (tocFlags.HasFlag(InternalFlags.HasInlineSb) ? $"native_patch/{firstBundle.SbName}.toc" : $"native_patch/{firstBundle.SbName}.sb")
                                : (tocFlags.HasFlag(InternalFlags.HasInlineSb) ? $"native_data/{firstBundle.SbName}.toc" : $"native_data/{firstBundle.SbName}.sb");

                            string resolvedPath = parent.m_fs.ResolvePath(readerPath);
                            if (!string.IsNullOrEmpty(resolvedPath))
                            {
                                using (NativeReader initReader = new NativeReader(new FileStream(resolvedPath, FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator()))
                                {
                                    using (Stream stream = initReader.CreateViewStream(offset, size))
                                    using (BinarySbReader initBundleReader = new BinarySbReader(stream, parent.m_fs.CreateDeobfuscator()))
                                    {
                                        int bundleOffset = initBundleReader.ReadInt(Endian.Big);
                                        int bundleSize = initBundleReader.ReadInt(Endian.Big);
                                        if (!(bundleOffset == 0 && bundleSize == 0))
                                        {
                                            tocFlags |= InternalFlags.HasInlineBundle;
                                        }
                                    }
                                }
                            }
                        }

                        string patchSbPath = "";
                        string baseSbPath = "";
                        NativeReader reader = null;
                        NativeReader patchReader = null;
                        NativeReader baseReader = null;

                        // check for modified bundles
                        foreach (int bundleHash in parent.m_modifiedBundles.Keys)
                        {
                            if (bundles.ContainsKey(bundleHash))
                            {
                                // modify
                                BundleInfo bundleInfo = bundles[bundleHash];

                                tocFlags &= ~InternalFlags.HasInlineBundle;
                                tocFlags &= ~InternalFlags.HasInlineSb;
                                if ((bundleInfo.Size & 0xC0000000) == 0x40000000)
                                {
                                    tocFlags |= InternalFlags.HasInlineSb;
                                    HasSb = false;
                                    bundleInfo.Size &= ~0xC0000000;
                                    bundleInfo.Offset += 0x22C;
                                }

                                if (bundleInfo.IsPatch)
                                {
                                    if (patchReader == null || patchSbPath != bundleInfo.SbName)
                                    {
                                        patchSbPath = bundleInfo.SbName;
                                        patchReader?.Dispose();
                                        if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                        {
                                            patchReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_patch/{0}.toc", patchSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                        }
                                        else
                                        {
                                            patchReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_patch/{0}.sb", patchSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                        }
                                    }
                                    reader = patchReader;
                                }
                                else
                                {
                                    if (baseReader == null || baseSbPath != bundleInfo.SbName)
                                    {
                                        baseSbPath = bundleInfo.SbName;
                                        baseReader?.Dispose();
                                        if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                        {
                                            baseReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_data/{0}.toc", baseSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                        }
                                        else
                                        {
                                            baseReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_data/{0}.sb", baseSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                        }
                                    }
                                    reader = baseReader;
                                }

                                string catalog;
                                if (bundleInfo.SplitIndex != -1)
                                {
                                    modWriter = modSplitWriters[bundleInfo.SplitIndex];
                                    isSplitTocModified[bundleInfo.SplitIndex] = true;
                                    catalog = SuperBundleInfo.SplitSuperBundles[bundleInfo.SplitIndex];
                                }
                                else
                                {
                                    modWriter = modDefaultWriter;
                                    isDefaultTocModified = true;
                                    catalog = m_catalog;
                                }
                                byte catalogIndex = (byte)parent.m_fs.GetCatalogIndex(catalog);

                                Stream stream = reader.CreateViewStream(bundleInfo.Offset, bundleInfo.Size);

                                DbObject bundleObj;
                                using (BinarySbReader bundleReader = new BinarySbReader(stream, parent.m_fs.CreateDeobfuscator()))
                                {
                                    bundleObj = ReadBundle(bundleReader, ref tocFlags);
                                }

                                stream.Dispose();

                                ModBundleInfo modBundle = parent.m_modifiedBundles[bundleHash];

                                foreach (DbObject ebx in bundleObj.GetValue<DbObject>("ebx"))
                                {
                                    string name = ebx.GetValue<string>("name");
                                    if (modBundle.Modify.Ebx.Contains(name))
                                    {
                                        EbxAssetEntry entry = parent.m_modifiedEbx[name];
                                        byte[] ebxData = parent.m_archiveData[entry.Sha1].Data;

                                        // get next cas (if one hasnt been obtained or the current one will exceed 1gb)
                                        if (casWriter == null || casWriter.Length + ebxData.Length > 1073741824)
                                        {
                                            casWriter?.Close();
                                            casWriter = GetNextCas(catalog, out casFileIndex);
                                        }

                                        ebx.SetValue("sha1", entry.Sha1);
                                        ebx.SetValue("originalSize", entry.OriginalSize);
                                        ebx.SetValue("size", entry.Size);
                                        ebx.SetValue("catalog", catalogIndex);
                                        ebx.SetValue("cas", casFileIndex);
                                        ebx.SetValue("offset", (int)casWriter.Position);
                                        if (parent.m_hasPatchFolder)
                                        {
                                            ebx.SetValue("patch", true);
                                        }

                                        casWriter.Write(ebxData);
                                    }
                                }
                                foreach (string name in modBundle.Add.Ebx)
                                {
                                    EbxAssetEntry entry = parent.m_modifiedEbx[name];
                                    byte[] addEbxData = parent.m_archiveData[entry.Sha1].Data;

                                    // get next cas (if one hasnt been obtained or the current one will exceed 1gb)
                                    if (casWriter == null || casWriter.Length + addEbxData.Length > 1073741824)
                                    {
                                        casWriter?.Close();
                                        casWriter = GetNextCas(catalog, out casFileIndex);
                                    }

                                    DbObject ebx = new DbObject();
                                    ebx.SetValue("name", entry.Name);
                                    ebx.SetValue("sha1", entry.Sha1);
                                    ebx.SetValue("originalSize", entry.OriginalSize);
                                    ebx.SetValue("size", entry.Size);
                                    ebx.SetValue("catalog", catalogIndex);
                                    ebx.SetValue("cas", casFileIndex);
                                    ebx.SetValue("offset", (int)casWriter.Position);
                                    if (parent.m_hasPatchFolder)
                                    {
                                        ebx.SetValue("patch", true);
                                    }
                                    bundleObj.GetValue<DbObject>("ebx").Add(ebx);

                                    casWriter.Write(addEbxData);
                                }

                                foreach (DbObject res in bundleObj.GetValue<DbObject>("res"))
                                {
                                    string name = res.GetValue<string>("name");
                                    if (modBundle.Modify.Res.Contains(name))
                                    {
                                        ResAssetEntry entry = parent.m_modifiedRes[name];
                                        byte[] resData = parent.m_archiveData[entry.Sha1].Data;

                                        // get next cas (if one hasnt been obtained or the current one will exceed 1gb)
                                        if (casWriter == null || casWriter.Length + resData.Length > 1073741824)
                                        {
                                            casWriter?.Close();
                                            casWriter = GetNextCas(catalog, out casFileIndex);
                                        }

                                        res.SetValue("sha1", entry.Sha1);
                                        res.SetValue("originalSize", entry.OriginalSize);
                                        res.SetValue("size", entry.Size);
                                        res.SetValue("catalog", catalogIndex);
                                        res.SetValue("cas", casFileIndex);
                                        res.SetValue("offset", (int)casWriter.Position);
                                        res.SetValue("resRid", (long)entry.ResRid);
                                        res.SetValue("resMeta", entry.ResMeta);
                                        res.SetValue("resType", (int)entry.ResType);
                                        if (parent.m_hasPatchFolder)
                                        {
                                            res.SetValue("patch", true);
                                        }

                                        casWriter.Write(resData);
                                    }
                                }
                                foreach (string name in modBundle.Add.Res)
                                {
                                    ResAssetEntry entry = parent.m_modifiedRes[name];
                                    byte[] addResData = parent.m_archiveData[entry.Sha1].Data;

                                    // get next cas (if one hasnt been obtained or the current one will exceed 1gb)
                                    if (casWriter == null || casWriter.Length + addResData.Length > 1073741824)
                                    {
                                        casWriter?.Close();
                                        casWriter = GetNextCas(catalog, out casFileIndex);
                                    }

                                    DbObject res = new DbObject();
                                    res.SetValue("name", entry.Name);
                                    res.SetValue("sha1", entry.Sha1);
                                    res.SetValue("originalSize", entry.OriginalSize);
                                    res.SetValue("size", entry.Size);
                                    res.SetValue("catalog", catalogIndex);
                                    res.SetValue("cas", casFileIndex);
                                    res.SetValue("offset", (int)casWriter.Position);
                                    res.SetValue("resRid", (long)entry.ResRid);
                                    res.SetValue("resMeta", entry.ResMeta);
                                    res.SetValue("resType", (int)entry.ResType);
                                    if (parent.m_hasPatchFolder)
                                    {
                                        res.SetValue("patch", true);
                                    }
                                    bundleObj.GetValue<DbObject>("res").Add(res);

                                    casWriter.Write(addResData);
                                }

                                DbObject chunkMeta = bundleObj.GetValue<DbObject>("chunkMeta");
                                int chunkIndex = 0;
                                List<(int, int)> chunksToRemove = new List<(int, int)>();

                                // modify chunks
                                foreach (DbObject chunk in bundleObj.GetValue<DbObject>("chunks"))
                                {
                                    Guid id = chunk.GetValue<Guid>("id");
                                    if (modBundle.Remove.Chunks.Contains(id))
                                    {
                                        chunksToRemove.Add((chunkIndex, parent.m_modifiedChunks[id].H32));
                                    }
                                    else
                                    {
                                        id = chunk.GetValue<Guid>("id");
                                        if (modBundle.Modify.Chunks.Contains(id))
                                        {
                                            ChunkAssetEntry entry = parent.m_modifiedChunks[id];

                                            // get next cas (if one hasnt been obtained or the current one will exceed 1gb)
                                            if (casWriter == null || casWriter.Length + parent.m_archiveData[entry.Sha1].Data.Length > 1073741824)
                                            {
                                                casWriter?.Close();
                                                casWriter = GetNextCas(catalog, out casFileIndex);
                                            }

                                            DbObject meta = chunkMeta.Find<DbObject>((object a) => { return (a as DbObject).GetValue<int>("h32") == entry.H32; });

                                            byte[] rawChunkData = parent.m_archiveData[entry.Sha1].Data;
                                            byte[] data = rawChunkData;
                                            if (entry.LogicalOffset != 0)
                                            {
                                                data = new byte[entry.RangeEnd - entry.RangeStart];
                                                Buffer.BlockCopy(rawChunkData, (int)entry.RangeStart, data, 0, data.Length);
                                            }

                                            chunk.SetValue("sha1", entry.Sha1);
                                            chunk.SetValue("originalSize", entry.OriginalSize);
                                            chunk.SetValue("size", data.Length);
                                            chunk.SetValue("catalog", catalogIndex);
                                            chunk.SetValue("cas", casFileIndex);
                                            chunk.SetValue("offset", (uint)casWriter.Position);
                                            chunk.SetValue("logicalOffset", (int)entry.LogicalOffset);
                                            chunk.SetValue("logicalSize", (int)entry.LogicalSize);
                                            if (parent.m_hasPatchFolder)
                                            {
                                                chunk.SetValue("patch", true);
                                            }

                                            if (entry.FirstMip != -1)
                                            {
                                                meta?.GetValue<DbObject>("meta").SetValue("firstMip", entry.FirstMip);
                                            }

                                            casWriter.Write(data);
                                        }
                                    }

                                    chunkIndex++;
                                }
                                chunksToRemove.Reverse();
                                foreach ((int, int) chunk in chunksToRemove)
                                {
                                    bundleObj.GetValue<DbObject>("chunks").RemoveAt(chunk.Item1);
                                    int metaIndex = chunkMeta.FindIndex((object a) => { return (a as DbObject).GetValue<int>("h32") == chunk.Item2; });
                                    if (metaIndex != -1)
                                    {
                                        bundleObj.GetValue<DbObject>("chunkMeta").RemoveAt(metaIndex);
                                    }
                                }
                                foreach (Guid name in modBundle.Add.Chunks)
                                {
                                    ChunkAssetEntry entry = parent.m_modifiedChunks[name];

                                    if (casWriter == null || casWriter.Length + parent.m_archiveData[entry.Sha1].Data.Length > 1073741824)
                                    {
                                        casWriter?.Close();
                                        casWriter = GetNextCas(catalog, out casFileIndex);
                                    }

                                    byte[] rawAddChunkData = parent.m_archiveData[entry.Sha1].Data;
                                    byte[] data = rawAddChunkData;
                                    if (entry.LogicalOffset != 0)
                                    {
                                        data = new byte[entry.RangeEnd - entry.RangeStart];
                                        Buffer.BlockCopy(rawAddChunkData, (int)entry.RangeStart, data, 0, data.Length);
                                    }

                                    uint chunkOffset = (uint)casWriter.Position;

                                    DbObject chunk = new DbObject();
                                    chunk.SetValue("id", name);
                                    chunk.SetValue("sha1", entry.Sha1);
                                    chunk.SetValue("originalSize", entry.OriginalSize);
                                    chunk.SetValue("size", data.Length);
                                    chunk.SetValue("catalog", catalogIndex);
                                    chunk.SetValue("cas", casFileIndex);
                                    chunk.SetValue("offset", chunkOffset);
                                    chunk.SetValue("logicalOffset", (int)entry.LogicalOffset);
                                    chunk.SetValue("logicalSize", (int)entry.LogicalSize);
                                    if (parent.m_hasPatchFolder)
                                        chunk.SetValue("patch", true);

                                    DbObject meta = new DbObject();
                                    meta.SetValue("h32", entry.H32);
                                    meta.SetValue("meta", new DbObject());
                                    if (entry.FirstMip != -1)
                                        meta.GetValue<DbObject>("meta").SetValue("firstMip", entry.FirstMip);
                                    chunkMeta.Add(meta);

                                    bundleObj.GetValue<DbObject>("chunks").Add(chunk);
                                    casWriter.Write(data);

                                    // Ensure chunk is in the SuperBundle's TOC chunks dictionary
                                    if (!chunks.ContainsKey(name))
                                    {
                                        if (casWriter == null || casWriter.Length + rawAddChunkData.Length > 1073741824)
                                        {
                                            casWriter?.Close();
                                            casWriter = GetNextCas(catalog, out casFileIndex);
                                        }

                                        uint fullChunkOffset = (uint)casWriter.Position;

                                        ChunkInfo chunkInfo = new ChunkInfo()
                                        {
                                            Guid = name,
                                            SplitIndex = bundleInfo.SplitIndex,
                                            SbName = bundleInfo.SbName,
                                            IsPatch = true,
                                            CasFileInfo = new CasFileInfo()
                                            {
                                                IsPatch = parent.m_hasPatchFolder,
                                                CatalogIndex = catalogIndex,
                                                CasIndex = (byte)casFileIndex,
                                                Offset = fullChunkOffset,
                                                Size = (uint)rawAddChunkData.Length
                                            }
                                        };
                                        chunks.Add(name, chunkInfo);
                                        casWriter.Write(rawAddChunkData);
                                    }
                                }

                                bundleInfo.Offset = modWriter.Position;
                                bundleInfo.IsModified = true;
                                bundleInfo.IsPatch = true;

                                // fifa 21 uses different layouts in data and patch
                                if (ProfilesLibrary.IsLoaded(ProfileVersion.Fifa21))
                                {
                                    tocFlags |= InternalFlags.HasInlineBundle;
                                }

                                // write bundle
                                uint bundleOffset = 0;
                                uint bundleSize = 0;
                                uint locationOffset = 0;
                                uint totalCount = (uint)(bundleObj.GetValue<DbObject>("ebx").Count + bundleObj.GetValue<DbObject>("res").Count + bundleObj.GetValue<DbObject>("chunks").Count + (tocFlags.HasFlag(InternalFlags.HasInlineBundle) ? 0 : 1));
                                uint dataOffset = 0;

                                modWriter.Write(0xDEADBABE, Endian.Big); // bundleOffset
                                modWriter.Write(0xDEADBABE, Endian.Big); // bundleSize
                                modWriter.Write(0xDEADBABE, Endian.Big); // locationOffset
                                modWriter.Write(0xDEADBABE, Endian.Big); // totalCount
                                modWriter.Write(0xDEADBABE, Endian.Big); // dataOffset


                                modWriter.Write(0xDEADBABE, Endian.Big); // dataOffset
                                modWriter.Write(0xDEADBABE, Endian.Big); // dataOffset
                                modWriter.Write(0, Endian.Big);

                                if (tocFlags.HasFlag(InternalFlags.HasInlineBundle))
                                {
                                    bundleOffset = (uint)(modWriter.Position - bundleInfo.Offset);
                                    modWriter.Write(bundleObj);
                                    bundleSize = (uint)(modWriter.Position - bundleOffset);
                                }

                                byte[] flags = new byte[totalCount];
                                byte unused = 0;
                                bool patch = false;
                                byte catIndex = 0;
                                byte casIndex = 0;
                                int z = 0;

                                dataOffset = (uint)(modWriter.Position - bundleInfo.Offset);

                                if (!tocFlags.HasFlag(InternalFlags.HasInlineBundle))
                                {
                                    MemoryStream ms = new MemoryStream();
                                    using (BinarySbWriter subWriter = new BinarySbWriter(ms, true, endian))
                                        subWriter.Write(bundleObj);

                                    byte[] bundleBuffer = ms.ToArray();
                                    ms.Dispose();

                                    // get next cas (if one hasnt been obtained or the current one will exceed 1gb)
                                    if (casWriter == null || casWriter.Length + bundleBuffer.Length > 1073741824)
                                    {
                                        casWriter?.Close();
                                        casWriter = GetNextCas(catalog, out casFileIndex);
                                    }

                                    flags[z] = 1;

                                    modWriter.Write(unused);
                                    modWriter.Write(patch = parent.m_hasPatchFolder);
                                    modWriter.Write(catIndex = catalogIndex);
                                    modWriter.Write(casIndex = (byte)casFileIndex);
                                    modWriter.Write((uint)casWriter.Position, Endian.Big);
                                    modWriter.Write(bundleBuffer.Length, Endian.Big);
                                    z++;

                                    casWriter.Write(bundleBuffer);
                                }

                                foreach (DbObject ebx in bundleObj.GetValue<DbObject>("ebx"))
                                {
                                    if (patch != ebx.HasValue("patch") || catIndex != ebx.GetValue<byte>("catalog") ||
                                        casIndex != ebx.GetValue<byte>("cas"))
                                    {
                                        flags[z] = 1;

                                        modWriter.Write(unused);
                                        modWriter.Write(patch = ebx.HasValue("patch"));
                                        modWriter.Write(catIndex = ebx.GetValue<byte>("catalog"));
                                        modWriter.Write(casIndex = ebx.GetValue<byte>("cas"));
                                    }

                                    modWriter.Write(ebx.GetValue<int>("offset"), Endian.Big);
                                    modWriter.Write(ebx.GetValue<int>("size"), Endian.Big);
                                    z++;
                                }

                                foreach (DbObject res in bundleObj.GetValue<DbObject>("res"))
                                {
                                    if (patch != res.HasValue("patch") || catIndex != res.GetValue<byte>("catalog") ||
                                        casIndex != res.GetValue<byte>("cas"))
                                    {
                                        flags[z] = 1;

                                        modWriter.Write(unused);
                                        modWriter.Write(patch = res.HasValue("patch"));
                                        modWriter.Write(catIndex = res.GetValue<byte>("catalog"));
                                        modWriter.Write(casIndex = res.GetValue<byte>("cas"));
                                    }

                                    modWriter.Write(res.GetValue<int>("offset"), Endian.Big);
                                    modWriter.Write(res.GetValue<int>("size"), Endian.Big);
                                    z++;
                                }

                                foreach (DbObject chunk in bundleObj.GetValue<DbObject>("chunks"))
                                {
                                    if (patch != chunk.HasValue("patch") || catIndex != chunk.GetValue<byte>("catalog") ||
                                        casIndex != chunk.GetValue<byte>("cas"))
                                    {
                                        flags[z] = 1;

                                        modWriter.Write(unused);
                                        modWriter.Write(patch = chunk.HasValue("patch"));
                                        modWriter.Write(catIndex = chunk.GetValue<byte>("catalog"));
                                        modWriter.Write(casIndex = chunk.GetValue<byte>("cas"));
                                    }

                                    modWriter.Write(chunk.GetValue<int>("offset"), Endian.Big);
                                    modWriter.Write(chunk.GetValue<int>("size"), Endian.Big);
                                    z++;
                                }

                                locationOffset = (uint)(modWriter.Position - bundleInfo.Offset);
                                modWriter.Write(flags);

                                uint size = (uint)(modWriter.Position - bundleInfo.Offset);

                                modWriter.WritePadding(4);

                                // update offsets and sizes
                                modWriter.Position = bundleInfo.Offset;

                                modWriter.Write(bundleOffset, Endian.Big);
                                modWriter.Write(bundleSize, Endian.Big);

                                modWriter.Write(locationOffset, Endian.Big);
                                modWriter.Write(totalCount, Endian.Big);
                                modWriter.Write(dataOffset, Endian.Big);

                                modWriter.Write(dataOffset, Endian.Big);
                                modWriter.Write(dataOffset, Endian.Big);
#if FROSTY_DEVELOPER
                                Debug.Assert(tocFlags.HasFlag(InternalFlags.HasInlineBundle) ? (modWriter.Position + 4 - bundleInfo.Offset) == bundleOffset : (modWriter.Position + 4 - bundleInfo.Offset) == dataOffset);
#endif
                                modWriter.Position = modWriter.Length;
                                bundleInfo.Size = size;
                            }
                        }

                        // Added bundles for this superbundle
                        {
                            int addedsbId = parent.m_am.GetSuperBundleId(SuperBundleInfo.Name);
                            parent.Logger.Log($"[DEBUG-ADD] Checking superbundle '{SuperBundleInfo.Name}' (ID {addedsbId}) for added bundles.");

                            if (parent.m_addedBundles.TryGetValue(addedsbId, out HashSet<string> addedBundleNames) && addedBundleNames.Count > 0)
                            {
                                parent.Logger.Log($"[DEBUG-ADD] Superbundle '{SuperBundleInfo.Name}' has {addedBundleNames.Count} added bundle(s).");

                                foreach (string bundleName in addedBundleNames)
                                {
                                    int addedBundleHash = Fnv1.HashString(bundleName.ToLower());

                                    if (!parent.m_modifiedBundles.TryGetValue(addedBundleHash, out ModBundleInfo modBundle))
                                    {
                                        parent.Logger.Log($"[DEBUG-ADD] No asset registrations found for bundle '{bundleName}', skipping.");
                                        continue;
                                    }

                                    parent.Logger.Log($"[DEBUG-ADD] Writing added bundle '{bundleName}' with {modBundle.Add.Ebx.Count} EBX, {modBundle.Add.Res.Count} Res, {modBundle.Add.Chunks.Count} Chunks.");

                                    // Use default catalog
                                    string catalog = m_catalog;
                                    byte catalogIndex = (byte)parent.m_fs.GetCatalogIndex(catalog);
                                    isDefaultTocModified = true;
                                    modWriter = modDefaultWriter;

                                    // Build bundle object
                                    DbObject bundleObj = new DbObject();
                                    bundleObj.AddValue("ebx", DbObject.CreateList());
                                    bundleObj.AddValue("res", DbObject.CreateList());
                                    bundleObj.AddValue("chunks", DbObject.CreateList());
                                    bundleObj.AddValue("chunkMeta", DbObject.CreateList());

                                    // Write EBX assets
                                    foreach (string name in modBundle.Add.Ebx)
                                    {
                                        EbxAssetEntry entry = parent.m_modifiedEbx[name];
                                        byte[] newBundleEbxData = parent.m_archiveData[entry.Sha1].Data;

                                        if (casWriter == null || casWriter.Length + newBundleEbxData.Length > 1073741824)
                                        {
                                            casWriter?.Close();
                                            casWriter = GetNextCas(catalog, out casFileIndex);
                                        }

                                        DbObject ebx = new DbObject();
                                        ebx.SetValue("name", entry.Name);
                                        ebx.SetValue("sha1", entry.Sha1);
                                        ebx.SetValue("originalSize", entry.OriginalSize);
                                        ebx.SetValue("size", entry.Size);
                                        ebx.SetValue("catalog", catalogIndex);
                                        ebx.SetValue("cas", casFileIndex);
                                        ebx.SetValue("offset", (int)casWriter.Position);
                                        if (parent.m_hasPatchFolder)
                                            ebx.SetValue("patch", true);

                                        bundleObj.GetValue<DbObject>("ebx").Add(ebx);
                                        casWriter.Write(newBundleEbxData);
                                    }

                                    // Write Res assets
                                    foreach (string name in modBundle.Add.Res)
                                    {
                                        ResAssetEntry entry = parent.m_modifiedRes[name];
                                        byte[] newBundleResData = parent.m_archiveData[entry.Sha1].Data;

                                        if (casWriter == null || casWriter.Length + newBundleResData.Length > 1073741824)
                                        {
                                            casWriter?.Close();
                                            casWriter = GetNextCas(catalog, out casFileIndex);
                                        }

                                        DbObject res = new DbObject();
                                        res.SetValue("name", entry.Name);
                                        res.SetValue("sha1", entry.Sha1);
                                        res.SetValue("originalSize", entry.OriginalSize);
                                        res.SetValue("size", entry.Size);
                                        res.SetValue("catalog", catalogIndex);
                                        res.SetValue("cas", casFileIndex);
                                        res.SetValue("offset", (int)casWriter.Position);
                                        res.SetValue("resRid", (long)entry.ResRid);
                                        res.SetValue("resMeta", entry.ResMeta);
                                        res.SetValue("resType", (int)entry.ResType);
                                        if (parent.m_hasPatchFolder)
                                            res.SetValue("patch", true);

                                        bundleObj.GetValue<DbObject>("res").Add(res);
                                        casWriter.Write(newBundleResData);
                                    }

                                    // Write Chunk assets
                                    DbObject chunkMeta = bundleObj.GetValue<DbObject>("chunkMeta");
                                    foreach (Guid chunkId in modBundle.Add.Chunks)
                                    {
                                        ChunkAssetEntry entry = parent.m_modifiedChunks[chunkId];
                                        byte[] rawNewChunkData = parent.m_archiveData[entry.Sha1].Data;
                                        byte[] data = rawNewChunkData;

                                        if (entry.LogicalOffset != 0)
                                        {
                                            data = new byte[entry.RangeEnd - entry.RangeStart];
                                            Buffer.BlockCopy(rawNewChunkData, (int)entry.RangeStart, data, 0, data.Length);
                                        }

                                        if (casWriter == null || casWriter.Length + data.Length > 1073741824)
                                        {
                                            casWriter?.Close();
                                            casWriter = GetNextCas(catalog, out casFileIndex);
                                        }

                                        uint chunkOffset = (uint)casWriter.Position;

                                        DbObject chunk = new DbObject();
                                        chunk.SetValue("id", chunkId);
                                        chunk.SetValue("sha1", entry.Sha1);
                                        chunk.SetValue("originalSize", entry.OriginalSize);
                                        chunk.SetValue("size", data.Length);
                                        chunk.SetValue("catalog", catalogIndex);
                                        chunk.SetValue("cas", casFileIndex);
                                        chunk.SetValue("offset", chunkOffset);
                                        chunk.SetValue("logicalOffset", (int)entry.LogicalOffset);
                                        chunk.SetValue("logicalSize", (int)entry.LogicalSize);
                                        if (parent.m_hasPatchFolder)
                                            chunk.SetValue("patch", true);

                                        DbObject meta = new DbObject();
                                        meta.SetValue("h32", entry.H32);
                                        meta.SetValue("meta", new DbObject());
                                        if (entry.FirstMip != -1)
                                            meta.GetValue<DbObject>("meta").SetValue("firstMip", entry.FirstMip);
                                        chunkMeta.Add(meta);

                                        bundleObj.GetValue<DbObject>("chunks").Add(chunk);
                                        casWriter.Write(data);

                                        // Register chunk in the SuperBundle's TOC chunks dictionary
                                        if (!chunks.ContainsKey(chunkId))
                                        {
                                            if (casWriter == null || casWriter.Length + rawNewChunkData.Length > 1073741824)
                                            {
                                                casWriter?.Close();
                                                casWriter = GetNextCas(catalog, out casFileIndex);
                                            }

                                            uint fullChunkOffset = (uint)casWriter.Position;

                                            ChunkInfo chunkInfo = new ChunkInfo()
                                            {
                                                Guid = chunkId,
                                                SplitIndex = -1,
                                                SbName = SuperBundleInfo.Name,
                                                IsPatch = true,
                                                CasFileInfo = new CasFileInfo()
                                                {
                                                    IsPatch = parent.m_hasPatchFolder,
                                                    CatalogIndex = catalogIndex,
                                                    CasIndex = (byte)casFileIndex,
                                                    Offset = fullChunkOffset,
                                                    Size = (uint)rawNewChunkData.Length
                                                }
                                            };
                                            chunks.Add(chunkId, chunkInfo);
                                            casWriter.Write(rawNewChunkData);
                                        }
                                    }

                                    // Write bundle header and location table
                                    BundleInfo newBundleInfo = new BundleInfo
                                    {
                                        Name = bundleName,
                                        IsModified = true,
                                        IsPatch = true,
                                        SbName = SuperBundleInfo.Name,
                                        SplitIndex = -1,
                                        Offset = modWriter.Position,
                                    };

                                    uint newBundleOffset = 0;
                                    uint newBundleSize = 0;
                                    uint newLocationOffset = 0;
                                    uint newTotalCount = (uint)(
                                        bundleObj.GetValue<DbObject>("ebx").Count +
                                        bundleObj.GetValue<DbObject>("res").Count +
                                        bundleObj.GetValue<DbObject>("chunks").Count +
                                        (tocFlags.HasFlag(InternalFlags.HasInlineBundle) ? 0 : 1));
                                    uint newDataOffset = 0;

                                    modWriter.Write(0xDEADBABE, Endian.Big); // bundleOffset
                                    modWriter.Write(0xDEADBABE, Endian.Big); // bundleSize
                                    modWriter.Write(0xDEADBABE, Endian.Big); // locationOffset
                                    modWriter.Write(0xDEADBABE, Endian.Big); // totalCount
                                    modWriter.Write(0xDEADBABE, Endian.Big); // dataOffset
                                    modWriter.Write(0xDEADBABE, Endian.Big); // dataOffset copy
                                    modWriter.Write(0xDEADBABE, Endian.Big); // dataOffset copy
                                    modWriter.Write(0, Endian.Big);

                                    if (tocFlags.HasFlag(InternalFlags.HasInlineBundle))
                                    {
                                        newBundleOffset = (uint)(modWriter.Position - newBundleInfo.Offset);
                                        modWriter.Write(bundleObj);
                                        newBundleSize = (uint)(modWriter.Position - newBundleOffset);
                                    }

                                    byte[] locFlags = new byte[newTotalCount];
                                    byte locUnused = 0;
                                    bool locPatch = false;
                                    byte locCat = 0;
                                    byte locCas = 0;
                                    int lz = 0;

                                    newDataOffset = (uint)(modWriter.Position - newBundleInfo.Offset);

                                    if (!tocFlags.HasFlag(InternalFlags.HasInlineBundle))
                                    {
                                        MemoryStream ms2 = new MemoryStream();
                                        using (BinarySbWriter subWriter = new BinarySbWriter(ms2, true, endian))
                                            subWriter.Write(bundleObj);
                                        byte[] bundleBuffer = ms2.ToArray();
                                        ms2.Dispose();

                                        if (casWriter == null || casWriter.Length + bundleBuffer.Length > 1073741824)
                                        {
                                            casWriter?.Close();
                                            casWriter = GetNextCas(catalog, out casFileIndex);
                                        }

                                        locFlags[lz] = 1;
                                        modWriter.Write(locUnused);
                                        modWriter.Write(locPatch = parent.m_hasPatchFolder);
                                        modWriter.Write(locCat = catalogIndex);
                                        modWriter.Write(locCas = (byte)casFileIndex);
                                        modWriter.Write((uint)casWriter.Position, Endian.Big);
                                        modWriter.Write(bundleBuffer.Length, Endian.Big);
                                        lz++;
                                        casWriter.Write(bundleBuffer);
                                    }

                                    foreach (DbObject ebx in bundleObj.GetValue<DbObject>("ebx"))
                                    {
                                        if (locPatch != ebx.HasValue("patch") || locCat != ebx.GetValue<byte>("catalog") || locCas != ebx.GetValue<byte>("cas"))
                                        {
                                            locFlags[lz] = 1;
                                            modWriter.Write(locUnused);
                                            modWriter.Write(locPatch = ebx.HasValue("patch"));
                                            modWriter.Write(locCat = ebx.GetValue<byte>("catalog"));
                                            modWriter.Write(locCas = ebx.GetValue<byte>("cas"));
                                        }
                                        modWriter.Write(ebx.GetValue<int>("offset"), Endian.Big);
                                        modWriter.Write(ebx.GetValue<int>("size"), Endian.Big);
                                        lz++;
                                    }

                                    foreach (DbObject res in bundleObj.GetValue<DbObject>("res"))
                                    {
                                        if (locPatch != res.HasValue("patch") || locCat != res.GetValue<byte>("catalog") || locCas != res.GetValue<byte>("cas"))
                                        {
                                            locFlags[lz] = 1;
                                            modWriter.Write(locUnused);
                                            modWriter.Write(locPatch = res.HasValue("patch"));
                                            modWriter.Write(locCat = res.GetValue<byte>("catalog"));
                                            modWriter.Write(locCas = res.GetValue<byte>("cas"));
                                        }
                                        modWriter.Write(res.GetValue<int>("offset"), Endian.Big);
                                        modWriter.Write(res.GetValue<int>("size"), Endian.Big);
                                        lz++;
                                    }

                                    foreach (DbObject chunk in bundleObj.GetValue<DbObject>("chunks"))
                                    {
                                        if (locPatch != chunk.HasValue("patch") || locCat != chunk.GetValue<byte>("catalog") || locCas != chunk.GetValue<byte>("cas"))
                                        {
                                            locFlags[lz] = 1;
                                            modWriter.Write(locUnused);
                                            modWriter.Write(locPatch = chunk.HasValue("patch"));
                                            modWriter.Write(locCat = chunk.GetValue<byte>("catalog"));
                                            modWriter.Write(locCas = chunk.GetValue<byte>("cas"));
                                        }
                                        modWriter.Write(chunk.GetValue<int>("offset"), Endian.Big);
                                        modWriter.Write(chunk.GetValue<int>("size"), Endian.Big);
                                        lz++;
                                    }

                                    newLocationOffset = (uint)(modWriter.Position - newBundleInfo.Offset);
                                    modWriter.Write(locFlags);
                                    uint newSize = (uint)(modWriter.Position - newBundleInfo.Offset);
                                    modWriter.WritePadding(4);

                                    // Patch back the header placeholders
                                    modWriter.Position = newBundleInfo.Offset;
                                    modWriter.Write(newBundleOffset, Endian.Big);
                                    modWriter.Write(newBundleSize, Endian.Big);
                                    modWriter.Write(newLocationOffset, Endian.Big);
                                    modWriter.Write(newTotalCount, Endian.Big);
                                    modWriter.Write(newDataOffset, Endian.Big);
                                    modWriter.Write(newDataOffset, Endian.Big);
                                    modWriter.Write(newDataOffset, Endian.Big);
                                    modWriter.Position = modWriter.Length;

                                    newBundleInfo.Size = newSize;

                                    // Register the new bundle (prevents duplicate key crash)
                                    bundles[addedBundleHash] = newBundleInfo;
                                }
                            }
                            else
                            {
                                parent.Logger.Log($"[DEBUG-ADD] No added bundles for '{SuperBundleInfo.Name}'");
                            }
                        }
                        // End added bundles


                        int sbId = parent.m_am.GetSuperBundleId(SuperBundleInfo.Name);
                        if (parent.m_modifiedSuperBundles.ContainsKey(sbId))
                        {
                            ModBundleInfo bundleInfo = parent.m_modifiedSuperBundles[sbId];

                            foreach (Guid chunkId in bundleInfo.Modify.Chunks)
                            {
                                if (chunks.ContainsKey(chunkId))
                                {
                                    ChunkInfo chunkInfo = chunks[chunkId];

                                    ChunkAssetEntry entry = parent.m_modifiedChunks[chunkId];

                                    string catalog;
                                    if (chunkInfo.SplitIndex != -1)
                                    {
                                        isSplitTocModified[chunkInfo.SplitIndex] = true;
                                        catalog = SuperBundleInfo.SplitSuperBundles[chunkInfo.SplitIndex];
                                    }
                                    else
                                    {
                                        isDefaultTocModified = true;
                                        catalog = m_catalog;
                                    }

                                    byte[] sbChunkData = parent.m_archiveData[entry.Sha1].Data;

                                    // get next cas (if one hasnt been obtained or the current one will exceed 1gb)
                                    if (casWriter == null || casWriter.Length + sbChunkData.Length > 1073741824)
                                    {
                                        casWriter?.Close();
                                        casWriter = GetNextCas(catalog, out casFileIndex);
                                    }

                                    chunkInfo.IsPatch = true;
                                    chunkInfo.CasFileInfo.IsPatch = parent.m_hasPatchFolder;
                                    chunkInfo.CasFileInfo.CatalogIndex = (byte)parent.m_fs.GetCatalogIndex(catalog);
                                    chunkInfo.CasFileInfo.CasIndex = (byte)casFileIndex;
                                    chunkInfo.CasFileInfo.Offset = (uint)casWriter.Position;
                                    chunkInfo.CasFileInfo.Size = (uint)sbChunkData.Length;

                                    casWriter.Write(sbChunkData);
                                }
                                else
                                {
                                    //bundleInfo.Add.AddChunk(chunkId);
                                    throw new Exception($"tried to modify chunk {chunkId} in {SuperBundleInfo.Name}, but chunk is not in there");
                                }
                            }
                            foreach (Guid chunkId in bundleInfo.Add.Chunks)
                            {
                                // If the chunk was already injected by the orphaned bundle logic, skip it
                                if (chunks.ContainsKey(chunkId))
                                    continue;

                                ChunkInfo chunkInfo = new ChunkInfo()
                                {
                                    Guid = chunkId,
                                    SplitIndex = SuperBundleInfo.SplitSuperBundles.Count > 0 ? 0 : -1,
                                    SbName = SuperBundleInfo.Name
                                };

                                chunks.Add(chunkId, chunkInfo);

                                ChunkAssetEntry entry = parent.m_modifiedChunks[chunkId];
                                byte[] sbAddChunkData = parent.m_archiveData[entry.Sha1].Data;

                                string catalog;
                                if (chunkInfo.SplitIndex != -1)
                                {
                                    isSplitTocModified[chunkInfo.SplitIndex] = true;
                                    catalog = SuperBundleInfo.SplitSuperBundles[chunkInfo.SplitIndex];
                                }
                                else
                                {
                                    isDefaultTocModified = true;
                                    catalog = m_catalog;
                                }

                                if (casWriter == null || casWriter.Length + sbAddChunkData.Length > 1073741824)
                                {
                                    casWriter?.Close();
                                    casWriter = GetNextCas(catalog, out casFileIndex);
                                }

                                chunkInfo.IsPatch = true;
                                chunkInfo.CasFileInfo.IsPatch = parent.m_hasPatchFolder;
                                chunkInfo.CasFileInfo.CatalogIndex = (byte)parent.m_fs.GetCatalogIndex(catalog);
                                chunkInfo.CasFileInfo.CasIndex = (byte)casFileIndex;
                                chunkInfo.CasFileInfo.Offset = (uint)casWriter.Position;
                                chunkInfo.CasFileInfo.Size = (uint)sbAddChunkData.Length;

                                casWriter.Write(sbAddChunkData);
                            }
                        }

                        if (isDefaultTocModified || Array.Exists(isSplitTocModified, b => b))
                        {
                            foreach (BundleInfo bundleInfo in bundles.Values)
                            {
                                if (!bundleInfo.IsModified && (bundleInfo.IsPatch || !parent.m_hasPatchFolder))
                                {
                                    tocFlags &= ~InternalFlags.HasInlineSb;
                                    if ((bundleInfo.Size & 0xC0000000) == 0x40000000)
                                    {
                                        tocFlags |= InternalFlags.HasInlineSb;
                                        bundleInfo.Size &= ~0xC0000000;
                                        bundleInfo.Offset += 0x22C;
                                    }

                                    if (bundleInfo.IsPatch)
                                    {
                                        if (patchReader == null || patchSbPath != bundleInfo.SbName)
                                        {
                                            patchSbPath = bundleInfo.SbName;
                                            patchReader?.Dispose();
                                            if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                            {
                                                patchReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_patch/{0}.toc", patchSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                            }
                                            else
                                            {
                                                patchReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_patch/{0}.sb", patchSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                            }
                                        }
                                        reader = patchReader;
                                    }
                                    else
                                    {
                                        if (baseReader == null || baseSbPath != bundleInfo.SbName)
                                        {
                                            baseSbPath = bundleInfo.SbName;
                                            baseReader?.Dispose();
                                            if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                            {
                                                baseReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_data/{0}.toc", baseSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                            }
                                            else
                                            {
                                                baseReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_data/{0}.sb", baseSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                            }
                                        }
                                        reader = baseReader;
                                    }
                                    //if (patchReader == null || patchSbPath != bundleInfo.SbName)
                                    //{
                                    //    patchSbPath = bundleInfo.SbName;
                                    //    patchReader?.Dispose();
                                    //    if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                    //    {
                                    //        patchReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_patch/{0}.toc", patchSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                    //    }
                                    //    else
                                    //    {
                                    //        patchReader = new NativeReader(new FileStream(parent.m_fs.ResolvePath(string.Format("native_patch/{0}.sb", patchSbPath)), FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator());
                                    //    }
                                    //}

                                    if (bundleInfo.SplitIndex != -1)
                                    {
                                        modWriter = modSplitWriters[bundleInfo.SplitIndex];
                                    }
                                    else
                                    {
                                        modWriter = modDefaultWriter;
                                    }

                                    reader.Position = bundleInfo.Offset;
                                    bundleInfo.Offset = modWriter.Position;
                                    modWriter.Write(reader.ReadBytes((int)bundleInfo.Size));
                                    modWriter.WritePadding(4);
                                }
                            }
                        }

                        baseReader?.Dispose();
                        patchReader?.Dispose();
                    }

                    // fifa 21 uses different layouts in data and patch
                    if (ProfilesLibrary.IsLoaded(ProfileVersion.Fifa21))
                    {
                        tocFlags |= InternalFlags.HasInlineBundle;
                        tocFlags &= ~InternalFlags.HasInlineSb;
                        HasSb = true;
                    }

                    // TODO: huffman encode strings, for now just store them uncompressed
                    tocFlags &= ~InternalFlags.HasCompressedStrings;

                    // rewrite toc and sb if necessary
                    if (isDefaultTocModified)
                    {
                        string modTocPath = string.Format("{0}\\{1}.toc", modPath, SuperBundleInfo.Name);
                        FileInfo fi = new FileInfo(modTocPath);
                        Directory.CreateDirectory(fi.DirectoryName);
                        NativeWriter sbWriter = null;
                        if (!tocFlags.HasFlag(InternalFlags.HasInlineSb))
                        {
                            string modSbPath = string.Format("{0}\\{1}.sb", modPath, SuperBundleInfo.Name);
                            sbWriter = new NativeWriter(new FileStream(modSbPath, FileMode.Create, FileAccess.Write));
                        }
                        using (DbWriter writer = new DbWriter(new FileStream(fi.FullName, FileMode.Create, FileAccess.Write)))
                        {
                            writer.Write(0x01CED100);
                            writer.Position += 0x228;

                            uint bundleHashMapOffset;
                            uint bundleDataOffset;
                            int bundlesCount = 0;

                            uint chunkHashMapOffset;
                            uint chunkGuidOffset;
                            int chunksCount = 0;

                            uint namesOffset;

                            uint chunkDataOffset;

                            Flags flags = 0;

                            int namesCount = 0;
                            int tableCount = 0;
                            uint tableOffset = 0;

                            long startPos = writer.Position;

                            writer.Write(0xDEADBEEF, Endian.Big);
                            writer.Write(0xDEADBEEF, Endian.Big);
                            writer.Write(0xDEADBEEF, Endian.Big);

                            writer.Write(0xDEADBEEF, Endian.Big);
                            writer.Write(0xDEADBEEF, Endian.Big);
                            writer.Write(0xDEADBEEF, Endian.Big);

                            writer.Write(0xDEADBEEF, Endian.Big);
                            writer.Write(0xDEADBEEF, Endian.Big);

                            writer.Write(0xDEADBEEF, Endian.Big);

                            writer.Write(0xDEADBEEF, Endian.Big);
                            writer.Write(0xDEADBEEF, Endian.Big);

                            writer.Write(0xDEADBEEF, Endian.Big);

                            if (tocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                            {
                                flags |= Flags.HasCompressedNames;
                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);
                            }

                            Dictionary<byte[], BundleInfo> bundleDic = new Dictionary<byte[], BundleInfo>();
                            foreach (BundleInfo bundle in bundles.Values)
                            {
                                if (bundle.SplitIndex == -1)
                                {
                                    if (bundle.IsPatch || !parent.m_hasPatchFolder)
                                    {
                                        bundleDic.Add(Encoding.ASCII.GetBytes(bundle.Name.ToLower()), bundle);
                                    }
                                    else
                                    {
                                        flags |= Flags.HasBaseBundles;
                                    }
                                }
                            }
                            bundlesCount = bundleDic.Count;

                            Dictionary<byte[], ChunkInfo> chunkDic = new Dictionary<byte[], ChunkInfo>();
                            foreach (ChunkInfo chunk in chunks.Values)
                            {
                                if (chunk.SplitIndex == -1)
                                {
                                    if (chunk.IsPatch || !parent.m_hasPatchFolder)
                                    {
                                        chunkDic.Add(chunk.Guid.ToByteArray(), chunk);
                                    }
                                    else
                                    {
                                        flags |= Flags.HasBaseChunks;
                                    }
                                }
                            }
                            chunksCount = chunkDic.Count;

                            int[] bundleHashMap = CalculateHashMap(bundleDic, out BundleInfo[] bi);

                            byte[] stringData;
                            using (NativeWriter stringWriter = new NativeWriter(new MemoryStream()))
                            {
                                for (int i = 0; i < bundlesCount; i++)
                                {
                                    if (tocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                                    {
                                        // TODO: huffman strings
                                        throw new NotImplementedException("compressed names");
                                    }
                                    else
                                    {
                                        bi[i].NameOffset = (uint)stringWriter.Position;
                                        stringWriter.WriteNullTerminatedString(bi[i].Name);
                                    }
                                }
                                stringData = stringWriter.ToByteArray();
                            }

                            bundleHashMapOffset = (uint)(writer.Position - startPos);
                            for (int i = 0; i < bundlesCount; i++)
                            {
#if FROSTY_DEVELOPER
                                Debug.Assert(i == GetIndex(Encoding.ASCII.GetBytes(bi[i].Name.ToLower()), bundleHashMap));
#endif
                                writer.Write(bundleHashMap[i], Endian.Big);
                            }

                            while (((writer.Position - startPos) % 8) != 0)
                            {
                                writer.Position++;
                            }

                            bundleDataOffset = (uint)(writer.Position - startPos);
                            for (int i = 0; i < bundlesCount; i++)
                            {
#if FROSTY_DEVELOPER
                                Debug.Assert(bi[i].Size <= ~0xC0000000);
#endif
                                writer.Write(bi[i].NameOffset, Endian.Big);
                                writer.Write(bi[i].Size, Endian.Big);
                                writer.Write(bi[i].Offset, Endian.Big);
                            }

                            while (((writer.Position - startPos) % 8) != 0)
                            {
                                writer.Position++;
                            }

                            int[] chunkHashMap = CalculateHashMap(chunkDic, out ChunkInfo[] ci);

                            chunkHashMapOffset = (uint)(writer.Position - startPos);
                            for (int i = 0; i < chunksCount; i++)
                            {
#if FROSTY_DEVELOPER
                                Debug.Assert(i == GetIndex(ci[i].Guid.ToByteArray(), chunkHashMap));
#endif
                                writer.Write(chunkHashMap[i], Endian.Big);
                            }

                            while (((writer.Position - startPos) % 8) != 0)
                            {
                                writer.Position++;
                            }

                            byte[] b;
                            chunkGuidOffset = (uint)(writer.Position - startPos);
                            for (int i = 0; i < chunksCount; i++)
                            {
                                b = ci[i].Guid.ToByteArray();
                                Array.Reverse(b);
                                writer.Write(b);
                                writer.Write((i * 3) | (1 << 24), Endian.Big);
                            }

                            while (((writer.Position - startPos) % 8) != 0)
                            {
                                writer.Position++;
                            }

                            chunkDataOffset = (uint)(writer.Position - startPos);
                            for (int i = 0; i < chunksCount; i++)
                            {
                                writer.Write((byte)0);
                                writer.Write(ci[i].CasFileInfo.IsPatch);
                                writer.Write(ci[i].CasFileInfo.CatalogIndex);
                                writer.Write(ci[i].CasFileInfo.CasIndex);
                                writer.Write(ci[i].CasFileInfo.Offset, Endian.Big);
                                writer.Write(ci[i].CasFileInfo.Size, Endian.Big);
                            }

                            while (((writer.Position - startPos) % 8) != 0)
                            {
                                writer.Position++;
                            }

                            namesOffset = (uint)(writer.Position - startPos);
                            writer.Write(stringData);

                            if (flags.HasFlag(Flags.HasCompressedNames))
                            {
                                tableOffset = (uint)(writer.Position - startPos);
                            }

                            if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                            {
                                // maybe padding not quite sure, since it always ends with huffman table which is always 4 bytes aligned
                                // no game has normal string table and inline sb
                                while (((writer.Position - startPos) % 4) != 0)
                                {
                                    writer.Position++;
                                }
                            }

                            writer.Position = startPos;
                            writer.Write(bundleHashMapOffset, Endian.Big);
                            writer.Write(bundleDataOffset, Endian.Big);
                            writer.Write(bundlesCount, Endian.Big);

                            writer.Write(chunkHashMapOffset, Endian.Big);
                            writer.Write(chunkGuidOffset, Endian.Big);
                            writer.Write(chunksCount, Endian.Big);

                            writer.Write(chunkDataOffset, Endian.Big);
                            writer.Write(chunkDataOffset, Endian.Big);

                            writer.Write(namesOffset, Endian.Big);

                            writer.Write(chunkDataOffset, Endian.Big);
                            writer.Write(chunksCount * 3, Endian.Big);

                            writer.Write((uint)flags, Endian.Big);
                            if (tocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                            {
                                writer.Write(namesCount, Endian.Big);
                                writer.Write(tableCount, Endian.Big);
                                writer.Write(tableOffset, Endian.Big);
                            }

                            if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                            {
                                long size = writer.Length - startPos;
                                writer.Position = bundleDataOffset + startPos;
                                for (int i = 0; i < bundlesCount; i++)
                                {
                                    writer.Write(bi[i].NameOffset, Endian.Big);
                                    writer.Write(bi[i].Size | 0x40000000u, Endian.Big);
                                    writer.Write(bi[i].Offset + size, Endian.Big);
                                }
                            }

                            // write sb
                            writer.Position = writer.Length;

                            if (bundlesCount > 0)
                            {
                                if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                {
                                    writer.Write(modDefaultWriter.ToByteArray());
                                }
                                else
                                {
                                    sbWriter.Write(modDefaultWriter.ToByteArray());
                                }
                            }
                            sbWriter?.Close();
                        }
                    }
                    modDefaultWriter.Close();

                    for (int splitIndex = 0; splitIndex < isSplitTocModified.Length; splitIndex++)
                    {
                        if (isSplitTocModified[splitIndex])
                        {
                            string sbName = SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[splitIndex]);
                            string modTocPath = string.Format("{0}\\{1}.toc", modPath, sbName);
                            FileInfo fi = new FileInfo(modTocPath);
                            Directory.CreateDirectory(fi.DirectoryName);
                            NativeWriter sbWriter = null;
                            if (!tocFlags.HasFlag(InternalFlags.HasInlineSb))
                            {
                                string modSbPath = string.Format("{0}\\{1}.sb", modPath, sbName);
                                sbWriter = new NativeWriter(new FileStream(modSbPath, FileMode.Create, FileAccess.Write));
                            }
                            using (DbWriter writer = new DbWriter(new FileStream(fi.FullName, FileMode.Create, FileAccess.Write)))
                            {
                                writer.Write(0x01CED100);
                                writer.Position += 0x228;

                                uint bundleHashMapOffset;
                                uint bundleDataOffset;
                                int bundlesCount = 0;

                                uint chunkHashMapOffset;
                                uint chunkGuidOffset;
                                int chunksCount = 0;

                                uint namesOffset;

                                uint chunkDataOffset;

                                Flags flags = 0;

                                int namesCount = 0;
                                int tableCount = 0;
                                uint tableOffset = 0;

                                long startPos = writer.Position;

                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);

                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);

                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);

                                writer.Write(0xDEADBEEF, Endian.Big);

                                writer.Write(0xDEADBEEF, Endian.Big);
                                writer.Write(0xDEADBEEF, Endian.Big);

                                writer.Write(0xDEADBEEF, Endian.Big);

                                if (tocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                                {
                                    flags |= Flags.HasCompressedNames;
                                    writer.Write(0xDEADBEEF, Endian.Big);
                                    writer.Write(0xDEADBEEF, Endian.Big);
                                    writer.Write(0xDEADBEEF, Endian.Big);
                                }

                                Dictionary<byte[], BundleInfo> bundleDic = new Dictionary<byte[], BundleInfo>();
                                foreach (BundleInfo bundle in bundles.Values)
                                {
                                    if (bundle.SplitIndex == splitIndex)
                                    {
                                        if (bundle.IsPatch || !parent.m_hasPatchFolder)
                                        {
                                            bundleDic.Add(Encoding.ASCII.GetBytes(bundle.Name.ToLower()), bundle);
                                        }
                                        else
                                        {
                                            flags |= Flags.HasBaseBundles;
                                        }
                                    }
                                }
                                bundlesCount = bundleDic.Count;

                                Dictionary<byte[], ChunkInfo> chunkDic = new Dictionary<byte[], ChunkInfo>();
                                foreach (ChunkInfo chunk in chunks.Values)
                                {
                                    if (chunk.SplitIndex == splitIndex)
                                    {
                                        if (chunk.IsPatch || !parent.m_hasPatchFolder)
                                        {
                                            chunkDic.Add(chunk.Guid.ToByteArray(), chunk);
                                        }
                                        else
                                        {
                                            flags |= Flags.HasBaseChunks;
                                        }
                                    }
                                }
                                chunksCount = chunkDic.Count;

                                int[] bundleHashMap = CalculateHashMap(bundleDic, out BundleInfo[] bi);

                                byte[] stringData;
                                using (NativeWriter stringWriter = new NativeWriter(new MemoryStream()))
                                {
                                    for (int i = 0; i < bundlesCount; i++)
                                    {
                                        if (tocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                                        {
                                            // // TODO: huffman strings
                                            throw new NotImplementedException("compressed names");
                                        }
                                        else
                                        {
                                            bi[i].NameOffset = (uint)stringWriter.Position;
                                            stringWriter.WriteNullTerminatedString(bi[i].Name);
                                        }
                                    }
                                    stringData = stringWriter.ToByteArray();
                                }

                                bundleHashMapOffset = (uint)(writer.Position - startPos);
                                for (int i = 0; i < bundlesCount; i++)
                                {
#if FROSTY_DEVELOPER
                                    Debug.Assert(i == GetIndex(Encoding.ASCII.GetBytes(bi[i].Name.ToLower()), bundleHashMap));
#endif
                                    writer.Write(bundleHashMap[i], Endian.Big);
                                }

                                while (((writer.Position - startPos) % 8) != 0)
                                {
                                    writer.Position++;
                                }

                                bundleDataOffset = (uint)(writer.Position - startPos);
                                for (int i = 0; i < bundlesCount; i++)
                                {
#if FROSTY_DEVELOPER
                                    Debug.Assert(bi[i].Size <= ~0xC0000000);
#endif
                                    writer.Write(bi[i].NameOffset, Endian.Big);
                                    writer.Write(bi[i].Size, Endian.Big);
                                    writer.Write(bi[i].Offset, Endian.Big);
                                }

                                while (((writer.Position - startPos) % 8) != 0)
                                {
                                    writer.Position++;
                                }

                                int[] chunkHashMap = CalculateHashMap(chunkDic, out ChunkInfo[] ci);

                                chunkHashMapOffset = (uint)(writer.Position - startPos);
                                for (int i = 0; i < chunksCount; i++)
                                {
#if FROSTY_DEVELOPER
                                    Debug.Assert(i == GetIndex(ci[i].Guid.ToByteArray(), chunkHashMap));
#endif
                                    writer.Write(chunkHashMap[i], Endian.Big);
                                }

                                while (((writer.Position - startPos) % 8) != 0)
                                {
                                    writer.Position++;
                                }

                                byte[] b;
                                chunkGuidOffset = (uint)(writer.Position - startPos);
                                for (int i = 0; i < chunksCount; i++)
                                {
                                    b = ci[i].Guid.ToByteArray();
                                    Array.Reverse(b);
                                    writer.Write(b);
                                    writer.Write((i * 3) | (1 << 24), Endian.Big);
                                }

                                while (((writer.Position - startPos) % 8) != 0)
                                {
                                    writer.Position++;
                                }

                                chunkDataOffset = (uint)(writer.Position - startPos);
                                for (int i = 0; i < chunksCount; i++)
                                {
                                    writer.Write((byte)0);
                                    writer.Write(ci[i].CasFileInfo.IsPatch);
                                    writer.Write(ci[i].CasFileInfo.CatalogIndex);
                                    writer.Write(ci[i].CasFileInfo.CasIndex);
                                    writer.Write(ci[i].CasFileInfo.Offset, Endian.Big);
                                    writer.Write(ci[i].CasFileInfo.Size, Endian.Big);
                                }

                                while (((writer.Position - startPos) % 8) != 0)
                                {
                                    writer.Position++;
                                }

                                namesOffset = (uint)(writer.Position - startPos);
                                writer.Write(stringData);

                                if (flags.HasFlag(Flags.HasCompressedNames))
                                {
                                    tableOffset = (uint)(writer.Position - startPos);
                                    //writer.Write(huffmanTree);
                                }

                                if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                {
                                    // maybe padding not quite sure, since it always ends with huffman table which is always 4 bytes aligned
                                    // no game has normal string table and inline sb
                                    while (((writer.Position - startPos) % 4) != 0)
                                    {
                                        writer.Position++;
                                    }
                                }

                                writer.Position = startPos;
                                writer.Write(bundleHashMapOffset, Endian.Big);
                                writer.Write(bundleDataOffset, Endian.Big);
                                writer.Write(bundlesCount, Endian.Big);

                                writer.Write(chunkHashMapOffset, Endian.Big);
                                writer.Write(chunkGuidOffset, Endian.Big);
                                writer.Write(chunksCount, Endian.Big);

                                writer.Write(chunkDataOffset, Endian.Big);
                                writer.Write(chunkDataOffset, Endian.Big);

                                writer.Write(namesOffset, Endian.Big);

                                writer.Write(chunkDataOffset, Endian.Big);
                                writer.Write(chunksCount * 3, Endian.Big);

                                writer.Write((uint)flags, Endian.Big);
                                if (tocFlags.HasFlag(InternalFlags.HasCompressedStrings))
                                {
                                    writer.Write(namesCount, Endian.Big);
                                    writer.Write(tableCount, Endian.Big);
                                    writer.Write(tableOffset, Endian.Big);
                                }

                                if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                {
                                    long size = writer.Length - startPos;
                                    writer.Position = bundleDataOffset + startPos;
                                    for (int i = 0; i < bundlesCount; i++)
                                    {
                                        writer.Write(bi[i].NameOffset, Endian.Big);
                                        writer.Write(bi[i].Size | 0x40000000u, Endian.Big);
                                        writer.Write(bi[i].Offset + size, Endian.Big);
                                    }
                                }

                                // write sb
                                writer.Position = writer.Length;

                                if (bundlesCount > 0)
                                {
                                    if (tocFlags.HasFlag(InternalFlags.HasInlineSb))
                                    {
                                        writer.Write(modSplitWriters[splitIndex].ToByteArray());
                                    }
                                    else
                                    {
                                        sbWriter.Write(modSplitWriters[splitIndex].ToByteArray());
                                    }
                                }
                                sbWriter?.Close();
                            }
                        }
                        modSplitWriters[splitIndex].Close();
                    }

                    casWriter?.Close();
                }
                catch (Exception e)
                {
                    Exception = e;
                }
            }

            public void ThreadPoolCallback(object threadContext)
            {
                Run();

                // are all threads done?
                if (Interlocked.Decrement(ref parent.m_numTasks) == 0)
                    doneEvent.Set();
            }

            private string GetCatalog(SuperBundleInfo sb, IEnumerable<CatalogInfo> catalogs)
            {
                CatalogInfo first = null;
                foreach (CatalogInfo catalog in catalogs)
                {
                    if (first == null) first = catalog;
                    if (catalog.SuperBundles.TryGetValue(sb.Name, out var sbVal) && sbVal.Item1)
                    {
                        return catalog.Name;
                    }
                }
                return first?.Name;
            }

            private void ReadToc(NativeReader reader, ref Dictionary<int, BundleInfo> bundles, ref Dictionary<Guid, ChunkInfo> chunks, ref InternalFlags tocFlags, bool patch, int splitIndex = -1)
            {
                string sbName = splitIndex != -1 ? SuperBundleInfo.Name.Replace("win32", SuperBundleInfo.SplitSuperBundles[splitIndex]) : SuperBundleInfo.Name;
                uint startPos = (uint)reader.Position;

                uint bundleHashMapOffset = reader.ReadUInt(Endian.Big) + startPos;
                uint bundleDataOffset = reader.ReadUInt(Endian.Big) + startPos;
                int bundlesCount = reader.ReadInt(Endian.Big);

                uint chunkHashMapOffset = reader.ReadUInt(Endian.Big) + startPos;
                uint chunkGuidOffset = reader.ReadUInt(Endian.Big) + startPos;
                int chunksCount = reader.ReadInt(Endian.Big);

                uint unknownOffset1 = reader.ReadUInt(Endian.Big) + startPos; // not used
                uint unknownOffset2 = reader.ReadUInt(Endian.Big) + startPos; // not used

                uint namesOffset = reader.ReadUInt(Endian.Big) + startPos;

                uint chunkDataOffset = reader.ReadUInt(Endian.Big) + startPos;
                int dataCount = reader.ReadInt(Endian.Big);

                Flags flags = (Flags)reader.ReadInt(Endian.Big);

                if (flags.HasFlag(Flags.HasBaseBundles) || flags.HasFlag(Flags.HasBaseChunks))
                {
                    string tocPath = parent.m_fs.ResolvePath(string.Format("native_data/{0}.toc", sbName));
                    using (NativeReader baseReader = new NativeReader(new FileStream(tocPath, FileMode.Open, FileAccess.Read), parent.m_fs.CreateDeobfuscator()))
                    {
                        InternalFlags discard = 0;
                        ReadToc(baseReader, ref bundles, ref chunks, ref discard, false, splitIndex);
                    }
                }

                uint namesCount = 0;
                uint tableCount = 0;
                uint tableOffset = uint.MaxValue;
                HuffmanDecoder huffmanDecoder = null;

                if (flags.HasFlag(Flags.HasCompressedNames))
                {
                    tocFlags |= InternalFlags.HasCompressedStrings;
                    huffmanDecoder = new HuffmanDecoder();
                    namesCount = reader.ReadUInt(Endian.Big);
                    tableCount = reader.ReadUInt(Endian.Big);
                    tableOffset = reader.ReadUInt(Endian.Big) + startPos;
                }

#if FROSTY_DEVELOPER
                Debug.Assert(unknownOffset1 == chunkDataOffset && unknownOffset2 == chunkDataOffset);
#endif

                if (bundlesCount != 0)
                {
                    if (flags.HasFlag(Flags.HasCompressedNames))
                    {
                        reader.Position = namesOffset;
                        huffmanDecoder.ReadEncodedData(reader, namesCount, Endian.Big);

                        reader.Position = tableOffset;
                        huffmanDecoder.ReadHuffmanTable(reader, tableCount, Endian.Big);
                    }

                    List<int> bundleHashMap = new List<int>(bundlesCount);
                    reader.Position = bundleHashMapOffset;
                    for (int i = 0; i < bundlesCount; i++)
                    {
                        bundleHashMap.Add(reader.ReadInt(Endian.Big));
                    }

                    reader.Position = bundleDataOffset;

                    for (int i = 0; i < bundlesCount; i++)
                    {
                        int nameOffset = reader.ReadInt(Endian.Big);
                        uint bundleSize = reader.ReadUInt(Endian.Big); // flag in first 2 bits: 0x40000000 inline sb
                        long bundleOffset = reader.ReadLong(Endian.Big);

                        string name = "";

                        if (flags.HasFlag(Flags.HasCompressedNames))
                        {
                            name = huffmanDecoder.ReadHuffmanEncodedString(nameOffset);
                        }
                        else
                        {
                            long curPos = reader.Position;
                            reader.Position = namesOffset + nameOffset;
                            name = reader.ReadNullTerminatedString();
                            reader.Position = curPos;
                        }

                        int hash = Fnv1.HashString(name.ToLower());

                        if (bundleSize != uint.MaxValue && bundleOffset != -1)
                        {
                            bundles[hash] = new BundleInfo()
                            {
                                Name = name,
                                Offset = bundleOffset,
                                Size = bundleSize,
                                IsPatch = patch,
                                SbName = sbName,
                                SplitIndex = splitIndex
                            };
                        }
                    }
                    huffmanDecoder?.Dispose();
                }
                if (chunksCount != 0)
                {
                    reader.Position = chunkHashMapOffset;
                    List<int> chunkHashMap = new List<int>(chunksCount);
                    for (int i = 0; i < chunksCount; i++)
                    {
                        chunkHashMap.Add(reader.ReadInt(Endian.Big));
                    }

                    reader.Position = chunkGuidOffset;
                    Guid[] chunkGuids = new Guid[dataCount / 3];
                    for (int i = 0; i < chunksCount; i++)
                    {
                        byte[] b = reader.ReadBytes(16);
                        Guid guid = new Guid(new byte[]
                        {
                            b[15], b[14], b[13], b[12],
                            b[11], b[10], b[9], b[8],
                            b[7], b[6], b[5], b[4],
                            b[3], b[2], b[1], b[0]
                        });

                        // 0xFFFFFFFF remove chunk
                        int index = reader.ReadInt(Endian.Big);

                        if (index != -1)
                        {
                            // im guessing the unknown offsets are connected to this
                            byte flag = (byte)((index & 0xFF000000) >> 24);
#if FROSTY_DEVELOPER
                            Debug.Assert(flag == 1);
#endif
                            index = (index & 0xFFFFFF) / 3;

                            chunkGuids[index] = guid;
                        }
                    }
                    reader.Position = chunkDataOffset;
                    for (int i = 0; i < (dataCount / 3); i++)
                    {
                        byte unk = reader.ReadByte();
                        bool isPatch = reader.ReadBoolean();
                        byte catalogIndex = reader.ReadByte();
                        byte casIndex = reader.ReadByte();
                        uint offset = reader.ReadUInt(Endian.Big);
                        uint size = reader.ReadUInt(Endian.Big);

                        chunks[chunkGuids[i]] = new ChunkInfo()
                        {
                            Guid = chunkGuids[i],
                            IsPatch = patch,
                            CasFileInfo = new CasFileInfo() { IsPatch = isPatch, CatalogIndex = catalogIndex, CasIndex = casIndex, Offset = offset, Size = size },
                            SbName = sbName,
                            SplitIndex = splitIndex
                        };
                    }
                }
            }

            private DbObject ReadBundle(BinarySbReader reader, ref InternalFlags tocFlags)
            {
                int bundleOffset = reader.ReadInt(Endian.Big);
                int bundleSize = reader.ReadInt(Endian.Big);
                uint locationOffset = reader.ReadUInt(Endian.Big);
                uint totalCount = reader.ReadUInt(Endian.Big);
                uint dataOffset = reader.ReadUInt(Endian.Big);

                if (!(bundleOffset == 0 && bundleSize == 0))
                {
                    tocFlags |= InternalFlags.HasInlineBundle;
                }

                reader.Position = locationOffset;

                bool[] flags = new bool[totalCount];
                for (uint i = 0; i < totalCount; i++)
                    flags[i] = reader.ReadBoolean();

                byte unused = 0;
                bool isPatch = false;
                int catalogIndex = 0;
                int casIndex = 0;
                int offset = 0;
                int size = 0;
                int z = 0;

                DbObject bundle;

                if (tocFlags.HasFlag(InternalFlags.HasInlineBundle))
                {
                    reader.Position = bundleOffset;
                    bundle = reader.ReadDbObject();

                    reader.Position = dataOffset;
                }
                else
                {
                    reader.Position = dataOffset;

                    if (flags[z++])
                    {
                        unused = reader.ReadByte();
#if FROSTY_DEVELOPER
                        Debug.Assert(unused == 0);
#endif
                        isPatch = reader.ReadBoolean();
                        catalogIndex = reader.ReadByte();
                        casIndex = reader.ReadByte();
                    }
                    offset = reader.ReadInt(Endian.Big);
                    size = reader.ReadInt(Endian.Big);

                    string path = parent.m_fs.GetFilePath(catalogIndex, casIndex, isPatch);

                    using (Stream casStream = new FileStream(parent.m_fs.ResolvePath(path), FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.RandomAccess))
                    {
                        byte[] buffer = new byte[size];
                        casStream.Position = offset;
                        casStream.Read(buffer, 0, size);

                        using (BinarySbReader casBundleReader = new BinarySbReader(new MemoryStream(buffer), parent.m_fs.CreateDeobfuscator()))
                        {
                            bundle = casBundleReader.ReadDbObject();
#if FROSTY_DEVELOPER
                            Debug.Assert(casBundleReader.TotalCount == totalCount - 1);
#endif
                        }
                    }
                }

                for (int i = 0; i < bundle.GetValue<DbObject>("ebx").Count; i++)
                {
                    if (flags[z++])
                    {
                        unused = reader.ReadByte();
                        isPatch = reader.ReadBoolean();
                        catalogIndex = reader.ReadByte();
                        casIndex = reader.ReadByte();
                    }

                    DbObject ebx = bundle.GetValue<DbObject>("ebx")[i] as DbObject;
                    offset = reader.ReadInt(Endian.Big);
                    size = reader.ReadInt(Endian.Big);

                    ebx.SetValue("catalog", catalogIndex);
                    ebx.SetValue("cas", casIndex);
                    ebx.SetValue("offset", offset);
                    ebx.SetValue("size", size);
                    if (isPatch)
                    {
                        ebx.SetValue("patch", true);
                    }
                }

                for (int i = 0; i < bundle.GetValue<DbObject>("res").Count; i++)
                {
                    if (flags[z++])
                    {
                        unused = reader.ReadByte();
                        isPatch = reader.ReadBoolean();
                        catalogIndex = reader.ReadByte();
                        casIndex = reader.ReadByte();
                    }

                    DbObject res = bundle.GetValue<DbObject>("res")[i] as DbObject;
                    offset = reader.ReadInt(Endian.Big);
                    size = reader.ReadInt(Endian.Big);

                    res.SetValue("catalog", catalogIndex);
                    res.SetValue("cas", casIndex);
                    res.SetValue("offset", offset);
                    res.SetValue("size", size);
                    if (isPatch)
                    {
                        res.SetValue("patch", true);
                    }
                }

                for (int i = 0; i < bundle.GetValue<DbObject>("chunks").Count; i++)
                {
                    if (flags[z++])
                    {
                        unused = reader.ReadByte();
                        isPatch = reader.ReadBoolean();
                        catalogIndex = reader.ReadByte();
                        casIndex = reader.ReadByte();
                    }

                    DbObject chunk = bundle.GetValue<DbObject>("chunks")[i] as DbObject;
                    offset = reader.ReadInt(Endian.Big);
                    size = reader.ReadInt(Endian.Big);

                    chunk.SetValue("catalog", catalogIndex);
                    chunk.SetValue("cas", casIndex);
                    chunk.SetValue("offset", offset);
                    chunk.SetValue("size", size);
                    if (isPatch)
                    {
                        chunk.SetValue("patch", true);
                    }
                }

                return bundle;
            }

            private NativeWriter GetNextCas(string catName, out int casFileIndex)
            {
                lock (locker)
                {
                    // TODO: see if this works
                    casFileIndex = CasFiles[catName];
                    CasFiles[catName]++;
                }

                FileInfo fi = new FileInfo(parent.m_fs.BasePath + parent.m_modDirName + "\\" + parent.m_patchPath + "\\" + catName + "\\cas_" + casFileIndex.ToString("D2") + ".cas");
                Directory.CreateDirectory(fi.DirectoryName);

                return new NativeWriter(new FileStream(fi.FullName, FileMode.Create));
            }

            private int[] CalculateHashMap<T>(Dictionary<byte[], T> input, out T[] sortedItems)
            {
                int size = input.Keys.Count;

                sortedItems = new T[size];

                // default value is -1, so it just returns the first element
                int[] hashMap = new int[size];
                for (int i = 0; i < size; i++)
                {
                    hashMap[i] = -1;
                }

                // add all hashes
                List<byte[]>[] hashDict = new List<byte[]>[size];
                for (int i = 0; i < size; i++)
                {
                    hashDict[i] = new List<byte[]>(1);
                }
                foreach (var key in input.Keys)
                {
                    hashDict[(int)(Hash(key) % size)].Add(key);
                }

                // sort them so that the ones with the most duplicates are first
                Array.Sort(hashDict, s_hashDictComparer);

                bool[] used = new bool[size];

                List<int> indices;
                HashSet<int> indicesSet = new HashSet<int>();

                int hash = 0;
                // process hash conflicts
                for (; hash < size; hash++)
                {
                    int duplicateCount = hashDict[hash].Count;

                    if (hashDict[hash].Count <= 1)
                    {
                        break;
                    }

                    indices = new List<int>(duplicateCount);
                    indicesSet.Clear();

                    // find seed for which all hashes are unique and not already used
                    uint seed = 1;
                    int i = 0;
                    while (i < duplicateCount)
                    {
                        int index = (int)(Hash(hashDict[hash][i], seed) % size);
                        if (used[index] || indicesSet.Contains(index))
                        {
                            seed++;
                            i = 0;
                            indices.Clear();
                            indicesSet.Clear();
                        }
                        else
                        {
                            indices.Add(index);
                            indicesSet.Add(index);
                            i++;
                        }
                    }

                    // set seed as hashmap value
                    hashMap[(int)(Hash(hashDict[hash][0]) % size)] = (int)seed;

                    // set output values and make indices used
                    for (int j = 0; j < duplicateCount; j++)
                    {
                        int index = indices[j];
                        sortedItems[index] = input[hashDict[hash][j]];
                        used[index] = true;
                    }
                }

                // process unique hashes
                int idx = 0;
                for (; hash < size; hash++)
                {
                    if (hashDict[hash].Count == 0)
                    {
                        break;
                    }

                    // find first free spot
                    while (used[idx])
                    {
                        idx = (idx + 1) % size;
                    }

                    // set correct index for hash
                    hashMap[Hash(hashDict[hash][0]) % size] = (-1 * idx) - 1;
                    sortedItems[idx] = input[hashDict[hash][0]];

                    // advance index
                    idx = (idx + 1) % size;
                }

                return hashMap;
            }

            private int GetIndex(byte[] key, int[] hashMap)
            {
                int index = hashMap[Hash(key) % hashMap.Length];

                if (index < 0)
                    index = (index + 1) * -1;
                else
                    index = (int)(Hash(key, (uint)index) % hashMap.Length);

                return index;
            }

            private uint Hash(byte[] bytes, uint offset = 0x811c9dc5)
            {
                const uint prime = 0x01000193;

                uint hash = offset;

                foreach (byte b in bytes)
                {
                    hash = (hash * prime) ^ (uint)(sbyte)b;
                }

                return hash % prime;
            }
        }
    }
}
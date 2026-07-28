using Frosty.Hash;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;

namespace FrostySdk.BaseProfile
{
    public class BaseBinarySbWriter : IBinarySbWriter
    {
        // Lightweight readonly structs for single pass data caching
        // Prevents defensive copies when accessing from arrays, improving performance and reducing GC pressure.
        private readonly struct EbxData
        {
            public readonly Sha1 Sha1;
            public readonly string Name;
            public readonly int OriginalSize;

            public EbxData(Sha1 sha1, string name, int originalSize)
            {
                Sha1 = sha1;
                Name = name;
                OriginalSize = originalSize;
            }
        }

        private readonly struct ResData
        {
            public readonly Sha1 Sha1;
            public readonly string Name;
            public readonly int OriginalSize;
            public readonly int ResType;
            public readonly byte[] ResMeta;
            public readonly long ResRid;

            public ResData(Sha1 sha1, string name, int originalSize, int resType, byte[] resMeta, long resRid)
            {
                Sha1 = sha1;
                Name = name;
                OriginalSize = originalSize;
                ResType = resType;
                ResMeta = resMeta;
                ResRid = resRid;
            }
        }

        private readonly struct ChunkData
        {
            public readonly Sha1 Sha1;
            public readonly Guid Id;
            public readonly int LogicalOffset;
            public readonly int LogicalSize;

            public ChunkData(Sha1 sha1, Guid id, int logicalOffset, int logicalSize)
            {
                Sha1 = sha1;
                Id = id;
                LogicalOffset = logicalOffset;
                LogicalSize = logicalSize;
            }
        }

        // Static readonly array for padding, avoids repeated allocations and optimizes I/O.
        private static readonly byte[] PaddingZeros = new byte[16];

        public void Write(DbWriter writer, DbObject bundleObj, Endian endian)
        {
            writer.Write(0xDEADBABE, Endian.Big);

            long startPos = writer.Position;

            var magicType = BaseBinarySb.GetMagic();
            bool isStandard = magicType == BaseBinarySb.Magic.Standard;

            uint magicSalted = (uint)magicType ^ BaseBinarySb.GetSalt();
            writer.Write(magicSalted, endian);

            // Fetch DbObjects once instead of constantly looking them up
            // (iam not sure who originally wrote this, hopefully this way doesnt break anything with not needing to fetch them alot)
            DbObject ebxObj = bundleObj.GetValue<DbObject>("ebx");
            DbObject resObj = bundleObj.GetValue<DbObject>("res");
            DbObject chunksObj = bundleObj.GetValue<DbObject>("chunks");
            DbObject chunkMetaObj = bundleObj.GetValue<DbObject>("chunkMeta");

            int ebxCount = ebxObj.Count;
            int resCount = resObj.Count;
            int chunksCount = chunksObj.Count;
            int totalCount = ebxCount + resCount + chunksCount;

            writer.Write(totalCount, endian);
            writer.Write(ebxCount, endian);
            writer.Write(resCount, endian);
            writer.Write(chunksCount, endian);

            writer.Write(0xDEADBABE, endian);
            writer.Write(0xDEADBABE, endian);
            writer.Write(0xDEADBABE, endian);

            // Pre extract data into contiguous arrays
            EbxData[] ebxData = new EbxData[ebxCount];
            int idx = 0;
            foreach (DbObject ebx in ebxObj)
            {
                ebxData[idx++] = new EbxData(
                    isStandard ? ebx.GetValue<Sha1>("sha1") : default(Sha1),
                    ebx.GetValue<string>("name"),
                    ebx.GetValue<int>("originalSize")
                );
            }

            ResData[] resData = new ResData[resCount];
            idx = 0;
            foreach (DbObject res in resObj)
            {
                resData[idx++] = new ResData(
                    isStandard ? res.GetValue<Sha1>("sha1") : default(Sha1),
                    res.GetValue<string>("name"),
                    res.GetValue<int>("originalSize"),
                    res.GetValue<int>("resType"),
                    res.GetValue<byte[]>("resMeta"),
                    res.GetValue<long>("resRid")
                );
            }

            ChunkData[] chunkData = new ChunkData[chunksCount];
            idx = 0;
            foreach (DbObject chunk in chunksObj)
            {
                chunkData[idx++] = new ChunkData(
                    isStandard ? chunk.GetValue<Sha1>("sha1") : default(Sha1),
                    chunk.GetValue<Guid>("id"),
                    chunk.GetValue<int>("logicalOffset"),
                    chunk.GetValue<int>("logicalSize")
                );
            }

            // Estimate memory stream capacity to prevent resizing re allocations
            int estimatedCapacity = (ebxCount * 40) + (resCount * 60) + (chunksCount * 30) + 1024;
            MemoryStream ms = new MemoryStream(estimatedCapacity);

            using (NativeWriter bundleWriter = new NativeWriter(ms, true))
            {
                // sha1's
                if (isStandard)
                {
                    for (int i = 0; i < ebxCount; i++) bundleWriter.Write(ebxData[i].Sha1);
                    for (int i = 0; i < resCount; i++) bundleWriter.Write(resData[i].Sha1);
                    for (int i = 0; i < chunksCount; i++) bundleWriter.Write(chunkData[i].Sha1);
                }

                // names
                uint nameOffset = 0;
                int namesCapacity = ebxCount + resCount;
                Dictionary<string, uint> stringToOffsetMap = new Dictionary<string, uint>(namesCapacity, StringComparer.Ordinal);
                List<string> stringsToPrint = new List<string>(namesCapacity);

                for (int i = 0; i < ebxCount; i++)
                {
                    string name = ebxData[i].Name;
                    if (!stringToOffsetMap.TryGetValue(name, out uint offset))
                    {
                        offset = nameOffset;
                        stringsToPrint.Add(name);
                        stringToOffsetMap.Add(name, offset);
                        nameOffset += (uint)name.Length + 1;
                    }
                    bundleWriter.Write(offset, endian);
                    bundleWriter.Write(ebxData[i].OriginalSize, endian);
                }

                for (int i = 0; i < resCount; i++)
                {
                    string name = resData[i].Name;
                    if (!stringToOffsetMap.TryGetValue(name, out uint offset))
                    {
                        offset = nameOffset;
                        stringsToPrint.Add(name);
                        stringToOffsetMap.Add(name, offset);
                        nameOffset += (uint)name.Length + 1;
                    }
                    bundleWriter.Write(offset, endian);
                    bundleWriter.Write(resData[i].OriginalSize, endian);
                }

                // res
                for (int i = 0; i < resCount; i++) bundleWriter.Write(resData[i].ResType, endian);
                for (int i = 0; i < resCount; i++) bundleWriter.Write(resData[i].ResMeta);
                for (int i = 0; i < resCount; i++) bundleWriter.Write(resData[i].ResRid, endian);

                // chunks
                for (int i = 0; i < chunksCount; i++)
                {
                    bundleWriter.Write(chunkData[i].Id, endian);
                    bundleWriter.Write(chunkData[i].LogicalOffset, endian);
                    bundleWriter.Write(chunkData[i].LogicalSize, endian);
                }

                // meta
                long metaOffset = 0;
                long metaSize = 0;
                if (chunkMetaObj != null && chunksCount != 0)
                {
                    metaOffset = bundleWriter.Position + 0x20;
                    using (DbWriter metaWriter = new DbWriter(new MemoryStream()))
                        bundleWriter.Write(metaWriter.WriteDbObject("chunkMeta", chunkMetaObj));

                    metaSize = (bundleWriter.Position + 0x20) - metaOffset;
                }

                // strings
                long stringsOffset = bundleWriter.Position + 0x20;

                // Write strings from pre populated list
                foreach (string str in stringsToPrint)
                    bundleWriter.WriteNullTerminatedString(str);

                // Optimized 16 byte alignment padding (avoids loop operations and allocations)
                long currentPos = bundleWriter.Position + 0x24;
                int paddingNeeded = (int)(16 - (currentPos % 16));
                if (paddingNeeded != 16) // If it's 16, remainder was 0 (should be already aligned)
                {
                    bundleWriter.Write(PaddingZeros, 0, paddingNeeded);
                }

                // update all relevant offsets
                writer.Position = startPos + 0x14;
                writer.Write((uint)stringsOffset, endian);
                writer.Write((uint)metaOffset, endian);
                writer.Write((uint)metaSize, endian);
            }

            // Compress bundle if necessary
            byte[] buffer;
            if (magicType == BaseBinarySb.Magic.Encrypted)
            {
                // I maintained original zero filling logic deliberately to guarantee it shouldnt break anything

                buffer = new byte[ms.Length];
                byte[] key = KeyManager.Instance.GetKey("Key2");

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = key;
                    aes.Padding = PaddingMode.None;

                    using (ICryptoTransform encryptor = aes.CreateEncryptor(aes.Key, aes.IV))
                    using (CryptoStream cryptoStream = new CryptoStream(ms, encryptor, CryptoStreamMode.Write))
                    {
                        cryptoStream.Write(buffer, 0, buffer.Length);
                    }
                }
            }
            else
            {
                buffer = ms.ToArray();
            }

            ms.Dispose();
            writer.Position = startPos - 4;
            writer.Write(buffer.Length + 0x20, Endian.Big);
            writer.Position = startPos + 0x20;
            writer.Write(buffer);
        }
    }
}
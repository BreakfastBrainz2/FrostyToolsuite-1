using Frosty.Hash;
using FrostySdk.Interfaces;
using FrostySdk.IO;
using FrostySdk.Managers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace FrostySdk.BaseProfile
{
    public class BaseBinarySbReader : IBinarySbReader
    {
        public uint TotalCount { get => totalCount; }

        private uint totalCount;

        private uint ebxCount;
        private uint resCount;
        private uint chunkCount;
        private uint stringsOffset;
        private uint metaOffset;
        private uint metaSize;

        private Endian endian = Endian.Big;

        private Sha1[] sha1;

        // Dedicated reader pinned to the string table so it lets main reader stay sequential
        private DbReader _stringsReader;

        public DbObject ReadDbObject(DbReader reader)
        {
            uint size = reader.ReadUInt(Endian.Big);
            BaseBinarySb.Magic magic = (BaseBinarySb.Magic)(reader.ReadUInt(endian) ^ BaseBinarySb.GetSalt());

            // check what endian its written in
            if (!BaseBinarySb.IsValidMagic(magic))
            {
                endian = Endian.Little;
                reader.Position -= 4;
                magic = (BaseBinarySb.Magic)(reader.ReadUInt(endian) ^ BaseBinarySb.GetSalt());

                if (!BaseBinarySb.IsValidMagic(magic))
                    throw new InvalidDataException("magic");
            }

            bool containsSha1 = !(magic == BaseBinarySb.Magic.Fifa || magic == BaseBinarySb.Magic.Encrypted);

            totalCount = reader.ReadUInt(endian);
            ebxCount = reader.ReadUInt(endian);
            resCount = reader.ReadUInt(endian);
            chunkCount = reader.ReadUInt(endian);
            stringsOffset = reader.ReadUInt(endian) - 0x20;
            metaOffset = reader.ReadUInt(endian) - 0x20;
            metaSize = reader.ReadUInt(endian);

            byte[] buffer = reader.ReadBytes((int)(size - 0x20));

            // decrypt the data
            if (magic == BaseBinarySb.Magic.Encrypted)
            {
                byte[] key = KeyManager.Instance.GetKey("Key2");

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.IV = key;
                    aes.Padding = PaddingMode.None;

                    // TransformBlock decrypts inplace have 0 extra allocation.
                    using (ICryptoTransform decryptor = aes.CreateDecryptor(aes.Key, aes.IV))
                        decryptor.TransformBlock(buffer, 0, buffer.Length, buffer, 0);
                }
            }

            // Pre allocate sha1 array with exact size to branch outside loop to avoid periteration conditional
            // Hopefully this makes sense
            sha1 = new Sha1[totalCount];

            // Dedicated reader for the string table so ReadEbx/ReadRes never seek the main reader
            using (_stringsReader = new DbReader(new MemoryStream(buffer, (int)stringsOffset, buffer.Length - (int)stringsOffset), null))
            {
                DbObject bundle = new DbObject(new Dictionary<string, object>(5));
                using (DbReader dbReader = new DbReader(new MemoryStream(buffer), null))
                {
                    if (containsSha1)
                    {
                        for (int i = 0; i < totalCount; i++)
                            sha1[i] = dbReader.ReadSha1();
                    }
                    else
                    {
                        for (int i = 0; i < totalCount; i++)
                            sha1[i] = Sha1.Zero;
                    }

                    bundle.AddValue("ebx", new DbObject(ReadEbx(dbReader)));
                    bundle.AddValue("res", new DbObject(ReadRes(dbReader)));
                    bundle.AddValue("chunks", new DbObject(ReadChunks(dbReader)));
                    bundle.AddValue("dataOffset", (int)size);

                    if (chunkCount > 0)
                    {
                        dbReader.Position = metaOffset;
                        bundle.AddValue("chunkMeta", dbReader.ReadDbObject());
                    }
                }

                return bundle;
            }
        }

        public List<object> ReadEbx(DbReader reader)
        {
            List<object> ebxList = new List<object>((int)ebxCount);
            Endian localEndian = endian; // cache field to avoids repeated indirect load in hot loop

            for (int i = 0; i < ebxCount; i++)
            {
                uint nameOffset = reader.ReadUInt(localEndian);
                uint originalSize = reader.ReadUInt(localEndian);

                // _stringsReader is already positioned at the string table base.
                // Set its position to the relative offset so no seek on main reader needed
                _stringsReader.Position = nameOffset;
                string name = _stringsReader.ReadNullTerminatedString();

                DbObject entry = new DbObject(new Dictionary<string, object>(4));
                entry.AddValue("sha1", sha1[i]);
                entry.AddValue("name", name);
                entry.AddValue("nameHash", Fnv1.HashString(name));
                entry.AddValue("originalSize", originalSize);
                ebxList.Add(entry);
            }

            return ebxList;
        }

        public List<object> ReadRes(DbReader reader)
        {
            List<object> resList = new List<object>((int)resCount);
            int offset = (int)ebxCount;
            Endian localEndian = endian;

            // Pass 1: read header pairs + resolve names in one loop — _stringsReader handles
            // string seeks independently so the main reader stays strictly sequential
            uint[] originalSizes = new uint[resCount];
            string[] names = new string[resCount];

            for (int i = 0; i < resCount; i++)
            {
                uint nameOffset = reader.ReadUInt(localEndian);
                originalSizes[i] = reader.ReadUInt(localEndian);
                _stringsReader.Position = nameOffset;
                names[i] = _stringsReader.ReadNullTerminatedString();
            }

            // Pass 2: read resType / resMeta / resRid sequentially into arrays
            uint[] resTypes = new uint[resCount];
            byte[][] resMetas = new byte[resCount][];
            long[] resRids = new long[resCount];

            for (int i = 0; i < resCount; i++) resTypes[i] = reader.ReadUInt(localEndian);
            for (int i = 0; i < resCount; i++) resMetas[i] = reader.ReadBytes(0x10);
            for (int i = 0; i < resCount; i++) resRids[i] = reader.ReadLong(localEndian);

            // Pass 3: build all DbObjects in one pass: dict presized to exact field count
            for (int i = 0; i < resCount; i++)
            {
                DbObject entry = new DbObject(new Dictionary<string, object>(7));
                entry.AddValue("sha1", sha1[offset + i]);
                entry.AddValue("name", names[i]);
                entry.AddValue("nameHash", Fnv1.HashString(names[i]));
                entry.AddValue("originalSize", originalSizes[i]);
                entry.AddValue("resType", resTypes[i]);
                entry.AddValue("resMeta", resMetas[i]);
                entry.AddValue("resRid", resRids[i]);
                resList.Add(entry);
            }

            return resList;
        }

        public List<object> ReadChunks(DbReader reader)
        {
            List<object> chunkList = new List<object>((int)chunkCount);
            int offset = (int)(ebxCount + resCount);
            Endian localEndian = endian;

            for (int i = 0; i < chunkCount; i++)
            {
                DbObject entry = new DbObject(new Dictionary<string, object>(5));

                Guid chunkId = reader.ReadGuid(localEndian);
                uint logicalOffset = reader.ReadUInt(localEndian);
                uint logicalSize = reader.ReadUInt(localEndian);
                long originalSize = (logicalOffset & 0xFFFF) | logicalSize;

                entry.AddValue("id", chunkId);
                entry.AddValue("sha1", sha1[offset + i]);
                entry.AddValue("logicalOffset", logicalOffset);
                entry.AddValue("logicalSize", logicalSize);
                entry.AddValue("originalSize", originalSize);

                chunkList.Add(entry);
            }

            return chunkList;
        }

        public void ReadChunkMeta(DbReader reader, DbObject bundle, uint metaOffset)
        {
            reader.Position = metaOffset + 4;
            bundle.AddValue("chunkMeta", reader.ReadDbObject());
        }
    }
}
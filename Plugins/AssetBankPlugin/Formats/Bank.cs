using AssetBankPlugin.Ant;
using AssetBankPlugin.Enums;
using AssetBankPlugin.Formats.GenericData;
using AssetBankPlugin.Formats.GenericData2;
using FrostySdk;
using FrostySdk.IO;
using System;
using System.Collections.Generic;

namespace AssetBankPlugin.Formats
{
    public class Bank
    {
        public uint PackagingType { get; set; }
        public List<Section> Sections { get; set; } = new List<Section>();

        public Dictionary<uint, GenericClass> Classes { get; set; } = new Dictionary<uint, GenericClass>();
        // BfN Classes
        public Dictionary<uint, GenericClass2> Classes2 { get; set; } = new Dictionary<uint, GenericClass2>();

        public Dictionary<string, Guid> DataNames { get; set; } = new Dictionary<string, Guid>();

        public Bank(NativeReader r, int bundleId)
        {
            PackagingType = r.ReadUInt(Endian.Big);

            ProcessLegacyAntRefMap(r);

            uint headerStart = (uint)r.BaseStream.Position;
            string str = r.ReadSizedString(3);
            r.BaseStream.Position = headerStart;

            if (str != "GD.")
            {
                uint headerSize = r.ReadUInt(Endian.Big);
                r.BaseStream.Position = headerStart + headerSize;
            }

            // First Pass: Read all section headers and Metadata (REFL/REF2).
            // Also bucket legacy (SectionData) and BfN (SectionData2) sections here so
            // we don't need a second full scan of Sections below.
            var data2Sections = new List<SectionData2>(8);
            var legacySections = new List<SectionData>(8);

            while (r.BaseStream.Position < r.BaseStream.Length)
            {
                Section section = Section.ReadSection(r);
                if (section == null) break;

                Sections.Add(section);

                if (section is SectionRefl refl)
                {
                    foreach (var kvp in refl.Classes) Classes[kvp.Key] = kvp.Value;
                }
                else if (section is SectionRef2 ref2)
                {
                    foreach (var kvp in ref2.Classes) Classes2[kvp.Key] = kvp.Value;
                }
                else if (section is SectionData2 d2)
                {
                    data2Sections.Add(d2);
                }
                else if (section is SectionData legacy)
                {
                    // Collect in first pass - avoids a second O(n) scan later.
                    legacySections.Add(legacy);
                }
            }

            // Legacy (pre-BfN) object deserialization.
            // We do this after the loop to ensure all Metadata (REFL) is fully loaded first.
            foreach (var legacyData in legacySections)
            {
                var asset = AntAsset.Deserialize(r, legacyData, Classes, this);
                if (asset != null) AddAsset(asset, bundleId);
            }

            // BfN Specific Object Deserialization (DAT2).
            // DAT2 contains exactly ONE root object at offset 28.
            // Sub-objects and arrays are stored as heap data in the remainder of the block.
            foreach (var data2Section in data2Sections)
            {
                long currentObjHeader = data2Section.DataOffset + 28;

                if (currentObjHeader + 16 <= data2Section.DataOffset + data2Section.DataSize)
                {
                    var asset = AntAsset.Deserialize(r, data2Section, Classes2, this, currentObjHeader);
                    if (asset != null) AddAsset(asset, bundleId);
                }
            }
        }

        private void ProcessLegacyAntRefMap(NativeReader r)
        {
            ProfileVersion version = (ProfileVersion)ProfilesLibrary.DataVersion;
            if (version == ProfileVersion.PlantsVsZombiesGardenWarfare2 ||
                version == ProfileVersion.PlantsVsZombiesGardenWarfare ||
                version == ProfileVersion.Battlefield4 ||
                version == ProfileVersion.Battlefield1)
            {
                if (PackagingType == 3)
                {
                    r.BaseStream.Position = 56;
                    uint count = r.ReadUInt(Endian.Big) / 20;
                    for (int i = 0; i < count; i++)
                    {
                        Guid a = r.ReadGuid();
                        byte[] bBytes = r.ReadBytes(16);
                        Guid b = new Guid(bBytes);
                        AntRefTable.InternalRefs[a] = b;
                        Cache.AntRefMap[a] = b;
                    }
                    r.BaseStream.Position = 4;
                }
            }
        }

        // Tracks how many times each base name has been seen so AddAsset
        // can assign a unique suffix in O(1) instead of scanning DataNames repeatedly.
        private readonly Dictionary<string, int> _nameCounters = new Dictionary<string, int>();

        private void AddAsset(AntAsset asset, int bundleId)
        {
            string baseName = asset.Name;
            string name;

            if (!_nameCounters.TryGetValue(baseName, out int count))
            {
                // First time seeing this name — use it as-is.
                name = baseName;
                _nameCounters[baseName] = 1;
            }
            else
            {
                // Collision — append counter suffix and bump for next time.
                name = string.Concat(baseName, " [", count.ToString(), "]");
                _nameCounters[baseName] = count + 1;
            }

            DataNames[name] = asset.ID;
            Cache.AntStateBundleIndices[asset.ID] = bundleId;
        }
    }
}
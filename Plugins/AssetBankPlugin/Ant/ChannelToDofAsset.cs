using AssetBankPlugin.Enums;
using FrostySdk;
using System;
using System.Collections.Generic;

namespace AssetBankPlugin.Ant
{
    public class ChannelToDofAsset : AntAsset
    {
        public override string Name { get; set; }
        public override Guid ID { get; set; }
        public StorageType StorageType { get; set; } = StorageType.Overwrite;
        public uint[] IndexData { get; set; }
        public Guid rigId { get; set; }

        public override void SetData(Dictionary<string, object> data)
        {
            ParseBasicData(data);

            // SWBF2 and BfN use "ChannelToDofIds"
            // GW2 uses "DofIds"
            // GW1/Legacy uses "IndexData" (GW1 is legacy now kiddos)
            if (data.TryGetValue("ChannelToDofIds", out object modernIds))
            {
                IndexData = ConvertArray<uint>(modernIds);
            }
            else if (data.TryGetValue("DofIds", out object gw2Ids))
            {
                IndexData = ConvertArray<uint>(gw2Ids);
            }
            else if (data.TryGetValue("IndexData", out object legacyIds))
            {
                byte[] raw = ConvertArray<byte>(legacyIds);
                // Handle GW1 bit-packed ushorts in bytes
                if (ProfilesLibrary.IsLoaded(ProfileVersion.PlantsVsZombiesGardenWarfare) && raw.Length > 256)
                {
                    uint[] converted = new uint[raw.Length / 2];
                    for (int i = 0; i < converted.Length; i++)
                        converted[i] = (uint)((raw[i * 2] << 8) | raw[i * 2 + 1]);
                    IndexData = converted;
                }
                else
                {
                    IndexData = Array.ConvertAll(raw, val => (uint)val);
                }
            }

            // SWBF2/BfN Specific: Direct Rig reference
            // This prevents the slow recursive hierarchy search in AnimationAsset.cs
            if (data.TryGetValue("Rig", out object rId))
            {
                rigId = SafeGuid(rId);
            }

            if (data.TryGetValue("StorageType", out object st))
            {
                StorageType = (StorageType)Convert.ToInt32(st);
            }
        }
    }
}
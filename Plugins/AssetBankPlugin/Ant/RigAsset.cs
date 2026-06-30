using FrostySdk;
using System;
using System.Collections.Generic;

namespace AssetBankPlugin.Ant
{
    public class RigAsset : AntAsset
    {
        public override string Name { get; set; }
        public override Guid ID { get; set; }
        public Guid FeatureCollection { get; set; }
        public Guid Skeleton { get; set; }
        public Guid[] DofSetLists { get; set; }
        public Guid[] RigDofSets { get; set; }
        public uint[] DofIds { get; set; }
        public uint[] DofSetIdIndices { get; set; } // REQUIRED FOR BFN ALIGNMENT

        // BfN specific default value arrays
        public List<DefaultDofVector4> DefaultVector4Values { get; set; }
        public List<DefaultDofVector3> DefaultVector3Values { get; set; }
        public List<DefaultDofFloat> DefaultFloatValues { get; set; }
        public List<DefaultDofInt> DefaultIntValues { get; set; }

        public override void SetData(Dictionary<string, object> data)
        {
            ParseBasicData(data);

            if (data.TryGetValue("FeatureCollection", out object fc)) FeatureCollection = SafeGuid(fc);
            if (data.TryGetValue("Skeleton", out object skel)) Skeleton = SafeGuid(skel);
            if (data.TryGetValue("DofSetLists", out object dsl)) DofSetLists = ConvertArray<Guid>(dsl);
            if (data.TryGetValue("RigDofSets", out object rds)) RigDofSets = ConvertArray<Guid>(rds);
            if (data.TryGetValue("DofIds", out object di)) DofIds = ConvertArray<uint>(di);
            if (data.TryGetValue("DofSetIdIndices", out object dsii)) DofSetIdIndices = ConvertArray<uint>(dsii);

            // Parse BfN default value arrays (present in DAT2 dumps as nested object lists??)
            if (data.TryGetValue("DefaultVector4Values", out object dv4))
                DefaultVector4Values = ParseDefaultList<DefaultDofVector4>(dv4, ParseDefaultVector4);
            if (data.TryGetValue("DefaultVector3Values", out object dv3))
                DefaultVector3Values = ParseDefaultList<DefaultDofVector3>(dv3, ParseDefaultVector3);
            if (data.TryGetValue("DefaultFloatValues", out object df))
                DefaultFloatValues = ParseDefaultList<DefaultDofFloat>(df, ParseDefaultFloat);
            if (data.TryGetValue("DefaultIntValues", out object diVals))
                DefaultIntValues = ParseDefaultList<DefaultDofInt>(diVals, ParseDefaultInt);
        }

        // Helper to parse a list of nested default objects from raw data
        private List<T> ParseDefaultList<T>(object obj, Func<Dictionary<string, object>, T> parser)
        {
            var result = new List<T>();
            if (obj is object[] arr)
            {
                foreach (var item in arr)
                {
                    if (item is Dictionary<string, object> dict)
                    {
                        var parsed = parser(dict);
                        if (parsed != null) result.Add(parsed);
                    }
                }
            }
            else if (obj is System.Collections.IEnumerable ie)
            {
                foreach (var item in ie)
                {
                    if (item is Dictionary<string, object> dict)
                    {
                        var parsed = parser(dict);
                        if (parsed != null) result.Add(parsed);
                    }
                }
            }
            return result;
        }

        private DefaultDofVector4 ParseDefaultVector4(Dictionary<string, object> dict)
        {
            var def = new DefaultDofVector4();
            if (dict.TryGetValue("DofId", out object dofId)) def.DofId = Convert.ToUInt32(dofId);
            if (dict.TryGetValue("Value", out object val))
            {
                if (val is float[] fArr) def.Value = fArr;
                else if (val is object[] oArr && oArr.Length == 4)
                {
                    def.Value = new float[] {
                        Convert.ToSingle(oArr[0]),
                        Convert.ToSingle(oArr[1]),
                        Convert.ToSingle(oArr[2]),
                        Convert.ToSingle(oArr[3])
                    };
                }
            }
            return def;
        }

        private DefaultDofVector3 ParseDefaultVector3(Dictionary<string, object> dict)
        {
            var def = new DefaultDofVector3();
            if (dict.TryGetValue("DofId", out object dofId)) def.DofId = Convert.ToUInt32(dofId);
            if (dict.TryGetValue("Value", out object val))
            {
                if (val is float[] fArr) def.Value = fArr;
                else if (val is object[] oArr && oArr.Length == 3)
                {
                    def.Value = new float[] {
                        Convert.ToSingle(oArr[0]),
                        Convert.ToSingle(oArr[1]),
                        Convert.ToSingle(oArr[2])
                    };
                }
            }
            return def;
        }

        private DefaultDofFloat ParseDefaultFloat(Dictionary<string, object> dict)
        {
            var def = new DefaultDofFloat();
            if (dict.TryGetValue("DofId", out object dofId)) def.DofId = Convert.ToUInt32(dofId);
            if (dict.TryGetValue("Value", out object val)) def.Value = Convert.ToSingle(val);
            return def;
        }

        private DefaultDofInt ParseDefaultInt(Dictionary<string, object> dict)
        {
            var def = new DefaultDofInt();
            if (dict.TryGetValue("DofId", out object dofId)) def.DofId = Convert.ToUInt32(dofId);
            if (dict.TryGetValue("Value", out object val)) def.Value = Convert.ToInt32(val);
            return def;
        }
    }

    // Container classes for BfN default DOF values
    public class DefaultDofVector4
    {
        public uint DofId { get; set; }
        public float[] Value { get; set; } // length 4
    }

    public class DefaultDofVector3
    {
        public uint DofId { get; set; }
        public float[] Value { get; set; } // length 3
    }

    public class DefaultDofFloat
    {
        public uint DofId { get; set; }
        public float Value { get; set; }
    }

    public class DefaultDofInt
    {
        public uint DofId { get; set; }
        public int Value { get; set; }
    }
}
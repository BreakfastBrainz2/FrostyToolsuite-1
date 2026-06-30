namespace AssetBankPlugin.Formats.GenericData2
{
    public class GenericField2
    {
        public string Name;
        public string Type;
        public uint TypeHash;
        public int Offset;
        public object Data;
        public uint Size;
        public uint Alignment;

        public bool IsList;       // Dynamic heap array (Flags and 1)
        public int InlineCount;   // fixed in line array (Count > 1)

        // New properties for correct array element handling, correct here heavy carrying
        public uint ElementTypeHash;   // hash of the actual element type (for arrays)
        public uint ElementSize;       // size of one element
        public uint ElementAlignment;  // alignment of one AND ONE ONLY element

        public bool IsArray
        {
            get => IsList || InlineCount > 1;
            set => IsList = value;
        }

        public bool IsNative;

        public override string ToString()
        {
            string suffix = IsList ? " (List)" : InlineCount > 1 ? $"[{InlineCount}]" : "";
            return $"Field, \"{Name}\", {Type}{suffix}, Offset: {Offset}";
        }
    }
}

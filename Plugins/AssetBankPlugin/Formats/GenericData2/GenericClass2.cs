using System.Collections.Generic;

namespace AssetBankPlugin.Formats.GenericData2
{
    public class GenericClass2
    {
        public string Name;
        public int Alignment;
        public int Size;
        public List<GenericField2> Elements = new List<GenericField2>();

        public GenericClass2() { }

        public override string ToString()
        {
            return $"Class, \"{Name}\", Align {Alignment}, Size {Size}";
        }
    }
}
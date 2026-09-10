using FrostySdk.IO;
using FrostySdk.Sdk.TypeInfoDatas;

namespace FrostySdk.Sdk.TypeInfos;

internal class StructInfo : TypeInfo
{
    public StructInfo(StructInfoData data)
        : base(data)
    {
    }

    public void ReadDefaultValues(MemoryReader reader)
    {
        (m_data as StructInfoData)?.ReadDefaultValues(reader);
    }

    public override string ReadDefaultValue(MemoryReader reader)
    {
        return (m_data as StructInfoData)?.ReadDefaultValue(reader) ?? string.Empty;
    }
}


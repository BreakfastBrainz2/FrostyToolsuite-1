using FrostySdk.IO;
using FrostySdk.Sdk.TypeInfoDatas;

namespace FrostySdk.Sdk.TypeInfos;

internal class PrimitiveInfo : TypeInfo
{
    public PrimitiveInfo(PrimitiveInfoData data)
        : base(data)
    {
    }

    public override string ReadDefaultValue(MemoryReader reader)
    {
        return (m_data as PrimitiveInfoData)?.ReadDefaultValue(reader) ?? string.Empty;
    }
}
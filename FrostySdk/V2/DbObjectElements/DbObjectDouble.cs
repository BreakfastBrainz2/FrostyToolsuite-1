using System.Globalization;
using FrostySdk.IO;

namespace FrostySdk.DbObjectElements;

public class DbObjectDouble : DbObjectV2
{
    private double m_value;

    protected internal DbObjectDouble(Type inType)
        : base(inType)
    {
    }

    public DbObjectDouble(double inValue)
        : base(Type.Double | Type.Anonymous)
    {
        m_value = inValue;
    }

    public DbObjectDouble(string inName, double inValue)
        : base(Type.Double, inName)
    {
        m_value = inValue;
    }

    public override float AsFloat()
    {
        return (float)m_value;
    }

    public override double AsDouble()
    {
        return m_value;
    }

    protected override void InternalSerialize(DataStream stream)
    {
        stream.WriteDouble(m_value);
    }

    protected override void InternalDeserialize(DataStream stream)
    {
        m_value = stream.ReadDouble();
    }

    public override string? ToString()
    {
        return m_value.ToString(CultureInfo.CurrentCulture);
    }
}
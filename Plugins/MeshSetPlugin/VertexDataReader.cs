using FrostySdk;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;

namespace MeshSetPlugin;

public class VertexDataReader
{
    public int Offset { get; set; }
    private byte[] m_data;

    public VertexDataReader(byte[] data, int startOffset)
    {
        m_data = data;
        Offset = startOffset;
    }

    static Vector2 ReadFloat2(byte[] data, int offset)
    {
        float x = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 0));
        float y = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 4));

        return new Vector2(x, y);
    }

    static Vector3 ReadFloat3(byte[] data, int offset)
    {
        float x = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 0));
        float y = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 4));
        float z = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 8));

        return new Vector3(x, y, z);
    }

    static Vector4 ReadFloat4(byte[] data, int offset)
    {
        float x = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 0));
        float y = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 4));
        float z = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 8));
        float w = BitConverter.Int32BitsToSingle(BitConverter.ToInt32(data, offset + 12));

        return new Vector4(x, y, z, w);
    }

    static Vector2 ReadHalf2(byte[] data, int offset)
    {
        Half x = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset));
        Half y = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset + 2));
        return new Vector2((float)x, (float)y);
    }

    static Vector3 ReadHalf3(byte[] data, int offset)
    {
        Half x = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset));
        Half y = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset + 2));
        Half z = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset + 4));
        return new Vector3((float)x, (float)y, (float)z);
    }

    static Vector4 ReadHalf4(byte[] data, int offset)
    {
        Half x = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset));
        Half y = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset + 2));
        Half z = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset + 4));
        Half w = BitConverter.UInt16BitsToHalf(BitConverter.ToUInt16(data, offset + 6));
        return new Vector4((float)x, (float)y, (float)z, (float)w);
    }

    static Vector4 ReadUByte4N(byte[] data, int offset)
    {
        float x = data[offset + 0] / 255.0f;
        float y = data[offset + 1] / 255.0f;
        float z = data[offset + 2] / 255.0f;
        float w = data[offset + 3] / 255.0f;
        return new Vector4(x, y, z, w);
    }

    static ushort[] ReadUShort4(byte[] data, int offset)
    {
        ushort[] values = new ushort[4];

        values[0] = BitConverter.ToUInt16(data, offset + 0);
        values[1] = BitConverter.ToUInt16(data, offset + 2);
        values[2] = BitConverter.ToUInt16(data, offset + 4);
        values[3] = BitConverter.ToUInt16(data, offset + 6);

        return values;
    }

    static ushort[] ReadByte4AsUShort4(byte[] data, int offset)
    {
        ushort[] values = new ushort[4];

        values[0] = data[offset + 0];
        values[1] = data[offset + 1];
        values[2] = data[offset + 2];
        values[3] = data[offset + 3];

        return values;
    }

    public Vector2 ReadVec2(VertexElementFormat format)
    {
        switch (format)
        {
            case VertexElementFormat.Half2: return ReadHalf2(m_data, Offset);
            case VertexElementFormat.Float2: return ReadFloat2(m_data, Offset);
            default: throw new InvalidOperationException($"Unhandled format: {format}");
        }
    }

    public Vector3 ReadVec3(VertexElementFormat format)
    {
        switch (format)
        {
            case VertexElementFormat.Half3:
            case VertexElementFormat.Half4: return ReadHalf3(m_data, Offset);
            case VertexElementFormat.Float3:
            case VertexElementFormat.Float4: return ReadFloat3(m_data, Offset);
            default: throw new InvalidOperationException($"Unhandled format: {format}");
        }
    }

    public Vector4 ReadVec4(VertexElementFormat format)
    {
        switch (format)
        {
            case VertexElementFormat.UByte4N: return ReadUByte4N(m_data, Offset);
            case VertexElementFormat.Half4: return ReadHalf4(m_data, Offset);
            case VertexElementFormat.Float4: return ReadFloat4(m_data, Offset);
            default: throw new InvalidOperationException($"Unhandled format: {format}");
        }
    }

    public ushort[] ReadByte4AsU16(VertexElementFormat format)
    {
        switch(format)
        {
            case VertexElementFormat.UByte4: return ReadByte4AsUShort4(m_data, Offset);
            case VertexElementFormat.UShort4: return ReadUShort4(m_data, Offset);
            default: throw new InvalidOperationException($"Unhandled format: {format}");
        }
    }

    public void ReadBoneIndices(VertexElementFormat format, ushort[] indices, int indexOffset)
    {
        ushort[] temp = ReadByte4AsU16(format);

        indices[indexOffset + 0] = temp[0];
        indices[indexOffset + 1] = temp[1];
        indices[indexOffset + 2] = temp[2];
        indices[indexOffset + 3] = temp[3];
    }

    public void ReadBoneWeights(VertexElementFormat format, float[] weights, int indexOffset)
    {
        Vector4 temp = ReadVec4(format);

        weights[indexOffset + 0] = temp[0];
        weights[indexOffset + 1] = temp[1];
        weights[indexOffset + 2] = temp[2];
        weights[indexOffset + 3] = temp[3];
    }
}
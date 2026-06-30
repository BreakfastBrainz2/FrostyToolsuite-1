using AssetBankPlugin.Extensions;
using FrostySdk.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace AssetBankPlugin.Formats.GenericData2
{
    public class SectionData2 : Section
    {
        public const string Identifier = "GD.DAT2";
        public override Endian Endianness { get; set; }
        public override uint DataSize { get; set; }
        public override uint DataOffset { get; set; }

        public uint ObjectCount { get; private set; }

        private readonly byte[] _patchedData;
        private readonly NativeReader _patchedReader;

        private readonly bool _isBigEndian;

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatUnion { [FieldOffset(0)] public uint UInt; [FieldOffset(0)] public float Float; }

        [StructLayout(LayoutKind.Explicit)]
        private struct DoubleUnion { [FieldOffset(0)] public ulong ULong; [FieldOffset(0)] public double Double; }

        public SectionData2(NativeReader r, Endian bankEndian)
        {
            long startPos = r.BaseStream.Position;
            DataOffset = (uint)startPos;

            string magic = r.ReadSizedString(7);
            _ = r.ReadByte();
            Endianness = bankEndian;
            _isBigEndian = bankEndian == Endian.Big;
            DataSize = r.ReadUInt(Endianness);

            long magicConstant = r.ReadLong(Endianness);
            ObjectCount = (uint)r.ReadULong(Endianness);

            r.BaseStream.Position = startPos;
            _patchedData = r.ReadBytes((int)DataSize);

            _patchedReader = new NativeReader(new MemoryStream(_patchedData), null);
            r.BaseStream.Position = startPos + DataSize;
        }

        public NativeReader GetPatchedReader() => _patchedReader;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint R_U32(int o) => _isBigEndian ? (uint)_patchedData[o] << 24 | (uint)_patchedData[o + 1] << 16 | (uint)_patchedData[o + 2] << 8 | _patchedData[o + 3] : BitConverter.ToUInt32(_patchedData, o);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int R_I32(int o) => _isBigEndian ? _patchedData[o] << 24 | _patchedData[o + 1] << 16 | _patchedData[o + 2] << 8 | _patchedData[o + 3] : BitConverter.ToInt32(_patchedData, o);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ulong R_U64(int o) => _isBigEndian ? (ulong)_patchedData[o] << 56 | (ulong)_patchedData[o + 1] << 48 | (ulong)_patchedData[o + 2] << 40 | (ulong)_patchedData[o + 3] << 32 | (ulong)_patchedData[o + 4] << 24 | (ulong)_patchedData[o + 5] << 16 | (ulong)_patchedData[o + 6] << 8 | _patchedData[o + 7] : BitConverter.ToUInt64(_patchedData, o);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long R_I64(int o) => _isBigEndian ? (long)_patchedData[o] << 56 | (long)_patchedData[o + 1] << 48 | (long)_patchedData[o + 2] << 40 | (long)_patchedData[o + 3] << 32 | (long)_patchedData[o + 4] << 24 | (long)_patchedData[o + 5] << 16 | (long)_patchedData[o + 6] << 8 | _patchedData[o + 7] : BitConverter.ToInt64(_patchedData, o);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ushort R_U16(int o) => _isBigEndian ? (ushort)(_patchedData[o] << 8 | _patchedData[o + 1]) : BitConverter.ToUInt16(_patchedData, o);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private short R_I16(int o) => _isBigEndian ? (short)(_patchedData[o] << 8 | _patchedData[o + 1]) : BitConverter.ToInt16(_patchedData, o);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private float R_F32(int o) => new FloatUnion { UInt = R_U32(o) }.Float;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private double R_F64(int o) => new DoubleUnion { ULong = R_U64(o) }.Double;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private Guid R_Guid(int o) => new Guid((int)R_U32(o), R_I16(o + 4), R_I16(o + 6), _patchedData[o + 8], _patchedData[o + 9], _patchedData[o + 10], _patchedData[o + 11], _patchedData[o + 12], _patchedData[o + 13], _patchedData[o + 14], _patchedData[o + 15]);

        private string R_NullStr(int o)
        {
            if (o < 0 || o >= _patchedData.Length) return "";
            int end = o;
            int bufLen = _patchedData.Length;
            while (end < bufLen && _patchedData[end] != 0) end++;
            return Encoding.UTF8.GetString(_patchedData, o, end - o);
        }

        public Dictionary<string, object> ReadObjectAt(long headerFileOffset, Dictionary<uint, GenericClass2> classes)
        {
            int bufOff = (int)(headerFileOffset - DataOffset);
            if (bufOff < 0 || bufOff + 16 > _patchedData.Length) return null;

            // Reading type hash robustly from the ulong alignment
            ulong typeHash64 = R_U64(bufOff);
            uint typeHash = (uint)(typeHash64 & 0xFFFFFFFF);

            if (!classes.TryGetValue(typeHash, out GenericClass2 layout)) return null;

            var result = ReadValues(bufOff + 16, layout, classes);
            // CRITICAL: Embed TypeHash into the parsed dictionary so the serializer knows how to pack it later
            result["__typeHash"] = typeHash;
            return result;
        }

        private Dictionary<string, object> ReadValues(int bufOff, GenericClass2 layout, Dictionary<uint, GenericClass2> classes)
        {
            var result = new Dictionary<string, object>(layout.Elements.Count);

            foreach (var field in layout.Elements)
            {
                int fieldOff = bufOff + field.Offset;

                if (field.IsList || field.Type == "String")
                    result[field.Name] = ReadHeapItem(fieldOff, field, classes);
                else if (field.InlineCount > 1)
                    result[field.Name] = ReadArray(fieldOff, field, field.InlineCount, classes);
                else
                    result[field.Name] = ReadPrimitive(fieldOff, field, classes);
            }
            return result;
        }

        private object ReadHeapItem(int bufOff, GenericField2 field, Dictionary<uint, GenericClass2> classes)
        {
            int count = R_I32(bufOff + 4);
            long rawVal = R_I64(bufOff + 8);

            if (rawVal == 0 || rawVal == -1 || count <= 0)
                return field.Type == "String" ? (object)"" : new object[0];

            long adjustedRawVal = rawVal << 4 >> 4;
            long absoluteTarget = DataOffset + bufOff + 8 + adjustedRawVal;

            if (absoluteTarget < DataOffset || absoluteTarget > DataOffset + DataSize)
                return field.Type == "String" ? (object)"" : new object[0];

            int targetOff = (int)(absoluteTarget - DataOffset);

            if (field.Type == "String") return R_NullStr(targetOff);

            if (field.ElementTypeHash != 0 && field.ElementSize > 0)
            {
                var elemField = new GenericField2
                {
                    TypeHash = field.ElementTypeHash,
                    Size = field.ElementSize,
                    Alignment = field.ElementAlignment
                };

                if (PrimitiveTypeMap.IsPrimitive(elemField.TypeHash))
                    elemField.Type = PrimitiveTypeMap.GetTypeName(elemField.TypeHash);
                else if (classes.TryGetValue(elemField.TypeHash, out GenericClass2 elemClass))
                    elemField.Type = elemClass.Name;
                else
                    elemField.Type = $"Class_0x{elemField.TypeHash:X8}";

                return ReadArray(targetOff, elemField, count, classes);
            }

            return ReadArray(targetOff, field, count, classes);
        }

        private object ReadPrimitive(int bufOff, GenericField2 field, Dictionary<uint, GenericClass2> classes)
        {
            if (bufOff < 0 || bufOff >= _patchedData.Length) return null;

            switch (field.TypeHash)
            {
                case 0x01: return _patchedData[bufOff] != 0;
                case 0x02: return (sbyte)_patchedData[bufOff];
                case 0x03: return _patchedData[bufOff];
                case 0x04: return R_I16(bufOff);
                case 0x05: return R_U16(bufOff);
                case 0x06: return R_I32(bufOff);
                case 0x07: return R_U32(bufOff);
                case 0x08: return R_I64(bufOff);
                case 0x09: return R_U64(bufOff);
                case 0x0A: return R_F32(bufOff);
                case 0x13: return R_F64(bufOff);
                case 0x23: return R_U64(bufOff).ToString("X16");

                case 0x0B: return new float[] { R_F32(bufOff), R_F32(bufOff + 4) };
                case 0x0C: return new float[] { R_F32(bufOff), R_F32(bufOff + 4), R_F32(bufOff + 8) };
                case 0x0D:
                case 0x0E: return new float[] { R_F32(bufOff), R_F32(bufOff + 4), R_F32(bufOff + 8), R_F32(bufOff + 12) };

                case 0x0F:
                    float[] m = new float[16];
                    for (int i = 0; i < 16; i++) m[i] = R_F32(bufOff + i * 4);
                    return m;

                case 0x12: // DataRef
                    {
                        long pVal = R_I64(bufOff);
                        if (pVal == 0 || pVal == -1) return null;
                        long pPos = DataOffset + bufOff;
                        // Skip +16 bytes into the LayoutData block header where the TypeHash begins
                        return ReadObjectAt(pPos + (pVal << 4 >> 4) + 16, classes);
                    }

                default:
                    if (field.Type == "Guid") return R_Guid(bufOff);

                    if (field.Size == 8 && field.Alignment == 8 && !field.IsNative)
                    {
                        long refVal = R_I64(bufOff);
                        if (refVal == 0 || refVal == -1) return null;

                        long refPos = DataOffset + bufOff;
                        long target = refPos + (refVal << 4 >> 4);

                        if (field.Type == "String" || field.ElementTypeHash == 0x11)
                        {
                            int tOff = (int)(target - DataOffset);
                            uint cnt = R_U32(tOff + 4);
                            long ptr = R_I64(tOff + 8);
                            if (ptr != 0 && ptr != -1 && cnt > 0)
                            {
                                long strTarget = target + 8 + (ptr << 4 >> 4);
                                return R_NullStr((int)(strTarget - DataOffset));
                            }
                            return "";
                        }

                        // Skip +16 bytes into the LayoutData block header
                        return ReadObjectAt(target + 16, classes);
                    }
                    else
                    {
                        if (!classes.TryGetValue(field.TypeHash, out GenericClass2 nestedLayout)) return null;
                        return ReadValues(bufOff, nestedLayout, classes);
                    }
            }
        }

        private object ReadArray(int bufOff, GenericField2 field, int count, Dictionary<uint, GenericClass2> classes)
        {
            if (count <= 0) return new object[0];

            if (count > 5_000_000) return new object[0];

            bool isReference = field.Size == 8 && field.Alignment == 8
                            && !PrimitiveTypeMap.IsPrimitive(field.TypeHash)
                            && field.TypeHash != 0;

            bool isStringArray = field.Type == "String[]"
                              || field.ElementTypeHash != 0 && field.ElementTypeHash == 0x11;

            uint elemSize = field.Size;
            uint elemAlign = field.Alignment;

            if (elemSize == 0 && field.TypeHash != 0 && !PrimitiveTypeMap.IsPrimitive(field.TypeHash))
            {
                if (classes.TryGetValue(field.TypeHash, out GenericClass2 classLayout))
                {
                    elemSize = (uint)classLayout.Size;
                    elemAlign = (uint)classLayout.Alignment;
                }
            }

            if (elemSize == 0) elemSize = 1;
            if (elemAlign == 0) elemAlign = 1;

            uint stride = elemSize + elemAlign - 1 & ~(elemAlign - 1);

            var arr = new object[count];
            for (int i = 0; i < count; i++)
            {
                int offset = (int)(i * stride);

                if (isReference)
                {
                    long refVal = R_I64(bufOff + offset);

                    if (refVal == 0 || refVal == -1)
                    {
                        arr[i] = null;
                        continue;
                    }

                    long refPos = DataOffset + bufOff + offset;
                    long target = refPos + (refVal << 4 >> 4);

                    if (isStringArray)
                    {
                        int tOff = (int)(target - DataOffset);
                        uint cnt = R_U32(tOff + 4);
                        long ptr = R_I64(tOff + 8);
                        string s = "";
                        if (ptr != 0 && ptr != -1 && cnt > 0)
                        {
                            long strTarget = target + 8 + (ptr << 4 >> 4);
                            s = R_NullStr((int)(strTarget - DataOffset));
                        }
                        arr[i] = s;
                    }
                    else
                    {
                        // Skip +16 bytes into the LayoutData block header
                        arr[i] = ReadObjectAt(target + 16, classes);
                    }
                }
                else
                {
                    arr[i] = ReadPrimitive(bufOff + offset, field, classes);
                }
            }
            return arr;
        }
    }
}
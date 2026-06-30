using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetBankPlugin.Import
{
    internal sealed class Dat2SectionWriter
    {
        private const ulong kIID = 0x030E6205UL;
        private const int FieldDataStart = 44;      
        private const int RelocBase = 12;

        private readonly bool _bigEndian;
        private readonly MemoryStream _fieldBuf = new MemoryStream(256);

        private struct HeapBlock
        {
            public int PtrOffsetInField;
            public int ElemAlignment;
            public byte[] Data;
        }
        private readonly List<HeapBlock> _heapBlocks = new List<HeapBlock>();

        public Dat2SectionWriter(bool bigEndian)
        {
            _bigEndian = bigEndian;
        }

        public void WriteKey(ulong key) => EmitU64(key);
        public void WriteUInt64(ulong v) => EmitU64(v);
        public void WriteUInt32(uint v) => EmitU32(v);
        public void WriteUInt16(ushort v) => EmitU16(v);
        public void WriteByte(byte v) => _fieldBuf.WriteByte(v);       
        public void WriteFloat(float v) => EmitU32(FloatBits(v));
        public void WriteBool(bool v) => _fieldBuf.WriteByte(v ? (byte)1 : (byte)0);
        public void WritePad(int count) { for (int i = 0; i < count; i++) _fieldBuf.WriteByte(0); }

        public void PadToFieldOffset(int target)
        {
            int cur = (int)_fieldBuf.Position;
            if (target > cur) WritePad(target - cur);
        }

        public void WriteString(string s)
        {
            byte[] chars = Encoding.UTF8.GetBytes(s);
            byte[] data = new byte[chars.Length + 1];    
            Buffer.BlockCopy(chars, 0, data, 0, chars.Length);
            uint len = (uint)data.Length;
            EmitU32(len); EmitU32(len); ScheduleHeap(data, 1);
        }

        public void WriteUInt16List(IList<ushort> list)
        {
            if (list == null || list.Count == 0) { EmitU32(0); EmitU32(0); EmitU64(0); return; }
            byte[] data = new byte[list.Count * 2];
            for (int i = 0; i < list.Count; i++) WriteU16LE(data, i * 2, _bigEndian ? Swap(list[i]) : list[i]);
            EmitU32((uint)list.Count); EmitU32((uint)list.Count); ScheduleHeap(data, 2);
        }

        public void WriteFloatList(IList<float> list)
        {
            if (list == null || list.Count == 0) { EmitU32(0); EmitU32(0); EmitU64(0); return; }
            byte[] data = new byte[list.Count * 4];
            for (int i = 0; i < list.Count; i++)
                WriteU32LE(data, i * 4, _bigEndian ? Swap(FloatBits(list[i])) : FloatBits(list[i]));
            EmitU32((uint)list.Count); EmitU32((uint)list.Count); ScheduleHeap(data, 4);
        }

        public void WriteUInt32List(IList<uint> list)
        {
            if (list == null || list.Count == 0) { EmitU32(0); EmitU32(0); EmitU64(0); return; }
            byte[] data = new byte[list.Count * 4];
            for (int i = 0; i < list.Count; i++) WriteU32LE(data, i * 4, _bigEndian ? Swap(list[i]) : list[i]);
            EmitU32((uint)list.Count); EmitU32((uint)list.Count); ScheduleHeap(data, 4);
        }

        public byte[] Build(uint layoutHash)
        {
            byte[] fieldData = _fieldBuf.ToArray();
            int fieldSize = fieldData.Length;
            int heapSectionStart = FieldDataStart + fieldSize;

            byte[] heapData;
            using (var heapMs = new MemoryStream())
            {
                int heapWritePos = heapSectionStart;

                foreach (var block in _heapBlocks)
                {
                    int alignedPos = AlignUp(heapWritePos - RelocBase, block.ElemAlignment) + RelocBase;
                    int paddingBytes = alignedPos - heapWritePos;
                    for (int i = 0; i < paddingBytes; i++) heapMs.WriteByte(0);
                    heapWritePos += paddingBytes;

                    int ptrSectionPos = FieldDataStart + block.PtrOffsetInField;
                    long encoded = Encode60BitPtr(ptrSectionPos, heapWritePos);
                    PatchI64(fieldData, block.PtrOffsetInField, encoded);

                    heapMs.Write(block.Data, 0, block.Data.Length);
                    heapWritePos += block.Data.Length;
                }

                heapData = heapMs.ToArray();
            }

            int totalSize = 12 + 32 + fieldSize + heapData.Length + 10;
            byte[] section = new byte[totalSize];

            section[0] = (byte)'G'; section[1] = (byte)'D'; section[2] = (byte)'.';
            section[3] = (byte)'D'; section[4] = (byte)'A'; section[5] = (byte)'T';
            section[6] = (byte)'2';
            section[7] = _bigEndian ? (byte)'b' : (byte)'l';
            PatchU32(section, 8, (uint)totalSize);

            PatchU64(section, 12, kIID);
            PatchU64(section, 28, (ulong)layoutHash);
            PatchU16(section, 40, 32);      
            Buffer.BlockCopy(fieldData, 0, section, FieldDataStart, fieldSize);

            if (heapData.Length > 0)
                Buffer.BlockCopy(heapData, 0, section, FieldDataStart + fieldSize, heapData.Length);

            int footerBase = FieldDataStart + fieldSize + heapData.Length;
            PatchI64(section, footerBase, Encode60BitPtr(footerBase, 12));
            PatchI16(section, footerBase + 8, 1);

            return section;
        }

        private void ScheduleHeap(byte[] data, int elemAlignment)
        {
            int ptrOff = (int)_fieldBuf.Position;
            EmitU64(0);  
            _heapBlocks.Add(new HeapBlock { PtrOffsetInField = ptrOff, ElemAlignment = elemAlignment, Data = data });
        }

        private void EmitU64(ulong v) { if (_bigEndian) v = Swap(v); for (int i = 0; i < 8; i++) _fieldBuf.WriteByte((byte)(v >> (i * 8))); }
        private void EmitU32(uint v) { if (_bigEndian) v = Swap(v); for (int i = 0; i < 4; i++) _fieldBuf.WriteByte((byte)(v >> (i * 8))); }
        private void EmitU16(ushort v) { if (_bigEndian) v = Swap(v); _fieldBuf.WriteByte((byte)v); _fieldBuf.WriteByte((byte)(v >> 8)); }

        private void PatchU64(byte[] b, int o, ulong v) { if (_bigEndian) v = Swap(v); for (int i = 0; i < 8; i++) b[o + i] = (byte)(v >> (i * 8)); }
        private void PatchU32(byte[] b, int o, uint v) { if (_bigEndian) v = Swap(v); for (int i = 0; i < 4; i++) b[o + i] = (byte)(v >> (i * 8)); }
        private void PatchU16(byte[] b, int o, ushort v) { if (_bigEndian) v = Swap(v); b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private void PatchI64(byte[] b, int o, long v) => PatchU64(b, o, (ulong)v);
        private void PatchI16(byte[] b, int o, short v) => PatchU16(b, o, (ushort)v);

        private static void WriteU16LE(byte[] b, int o, ushort v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        private static void WriteU32LE(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }

        private static uint FloatBits(float f)
        {
            byte[] tmp = BitConverter.GetBytes(f);
            return BitConverter.ToUInt32(tmp, 0);
        }

        private static long Encode60BitPtr(long from, long to)
        {
            long delta = to - from;
            return (delta & 0x0FFFFFFFFFFFFFFF) | (1L << 60);
        }

        private static int AlignUp(int v, int a) => a <= 1 ? v : (v + a - 1) & ~(a - 1);

        private static ulong Swap(ulong v) => ((v & 0xFF) << 56) | (((v >> 8) & 0xFF) << 48) | (((v >> 16) & 0xFF) << 40) | (((v >> 24) & 0xFF) << 32) | (((v >> 32) & 0xFF) << 24) | (((v >> 40) & 0xFF) << 16) | (((v >> 48) & 0xFF) << 8) | ((v >> 56) & 0xFF);
        private static uint Swap(uint v) => ((v & 0xFF) << 24) | (((v >> 8) & 0xFF) << 16) | (((v >> 16) & 0xFF) << 8) | ((v >> 24) & 0xFF);
        private static ushort Swap(ushort v) => (ushort)(((v & 0xFF) << 8) | ((v >> 8) & 0xFF));
    }
}
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace FrostySdk.IO
{
    public class NativeWriter : BinaryWriter
    {
        public long Position { get => BaseStream.Position; set => BaseStream.Position = value; }
        public long Length => BaseStream.Length;

        private readonly Encoding _encoding;
        private byte[] _ioBuffer;

        //   Precomputed once at construction. Encoding-aware, so:
        //   Default:  _nullTermBytes=[0x00],         _crlfBytes=[0x0D,0x0A]
        //   Unicode:  _nullTermBytes=[0x00,0x00],    _crlfBytes=[0x0D,0x00,0x0A,0x00]
        private readonly byte[] _nullTermBytes;
        private readonly byte[] _crlfBytes;

        public NativeWriter(Stream inStream, bool leaveOpen = false, bool wide = false)
            : base(inStream, wide ? Encoding.Unicode : Encoding.Default, leaveOpen)
        {
            _encoding = wide ? Encoding.Unicode : Encoding.Default;
            _ioBuffer = new byte[256];
            _nullTermBytes = _encoding.GetBytes(new[] { '\0' });
            _crlfBytes = _encoding.GetBytes("\r\n");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureBuffer(int required)
        {
            if (required > _ioBuffer.Length)
                Array.Resize(ref _ioBuffer, Math.Max(required, _ioBuffer.Length * 2));
        }

        // Zero allocation in both endian paths.
        // On x86/x64, Guid struct memory layout == ToByteArray() output bit for bit.
        public unsafe void Write(Guid value, Endian endian)
        {
            byte* b = (byte*)&value;
            if (endian == Endian.Big)
            {
                _ioBuffer[0] = b[3]; _ioBuffer[1] = b[2]; _ioBuffer[2] = b[1]; _ioBuffer[3] = b[0];
                _ioBuffer[4] = b[5]; _ioBuffer[5] = b[4];
                _ioBuffer[6] = b[7]; _ioBuffer[7] = b[6];
                _ioBuffer[8] = b[8]; _ioBuffer[9] = b[9]; _ioBuffer[10] = b[10]; _ioBuffer[11] = b[11];
                _ioBuffer[12] = b[12]; _ioBuffer[13] = b[13]; _ioBuffer[14] = b[14]; _ioBuffer[15] = b[15];
            }
            else
            {
                // Fully unrolled: avoids loop overhead on the little endian hot path.
                _ioBuffer[0] = b[0]; _ioBuffer[1] = b[1]; _ioBuffer[2] = b[2]; _ioBuffer[3] = b[3];
                _ioBuffer[4] = b[4]; _ioBuffer[5] = b[5]; _ioBuffer[6] = b[6]; _ioBuffer[7] = b[7];
                _ioBuffer[8] = b[8]; _ioBuffer[9] = b[9]; _ioBuffer[10] = b[10]; _ioBuffer[11] = b[11];
                _ioBuffer[12] = b[12]; _ioBuffer[13] = b[13]; _ioBuffer[14] = b[14]; _ioBuffer[15] = b[15];
            }
            Write(_ioBuffer, 0, 16);
        }

        public unsafe void Write(Guid value)
        {
            byte* b = (byte*)&value;
            _ioBuffer[0] = b[0]; _ioBuffer[1] = b[1]; _ioBuffer[2] = b[2]; _ioBuffer[3] = b[3];
            _ioBuffer[4] = b[4]; _ioBuffer[5] = b[5]; _ioBuffer[6] = b[6]; _ioBuffer[7] = b[7];
            _ioBuffer[8] = b[8]; _ioBuffer[9] = b[9]; _ioBuffer[10] = b[10]; _ioBuffer[11] = b[11];
            _ioBuffer[12] = b[12]; _ioBuffer[13] = b[13]; _ioBuffer[14] = b[14]; _ioBuffer[15] = b[15];
            Write(_ioBuffer, 0, 16);
        }

        public void Write(short value, Endian endian)
        {
            if (endian == Endian.Big)
                Write((short)((ushort)(((value & 0xFF) << 8) | ((value & 0xFF00) >> 8))));
            else
                Write(value);
        }

        public void Write(ushort value, Endian endian)
        {
            if (endian == Endian.Big)
                Write(((ushort)(((value & 0xFF) << 8) | ((value & 0xFF00) >> 8))));
            else
                Write(value);
        }

        public void Write(int value, Endian endian)
        {
            if (endian == Endian.Big)
                Write(((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value >> 8) & 0xFF00) | ((value >> 24) & 0xFF));
            else
                Write(value);
        }

        public void Write(uint value, Endian endian)
        {
            if (endian == Endian.Big)
                Write(((value & 0xFF) << 24) | ((value & 0xFF00) << 8) | ((value >> 8) & 0xFF00) | ((value >> 24) & 0xFF));
            else
                Write(value);
        }

        public void Write(long value, Endian endian)
        {
            if (endian == Endian.Big)
            {
                Write(((long)((value & 0xFF) << 56) | ((value & 0xFF00) << 40) | ((value & 0xFF0000) << 24) | ((value & 0xFF000000) << 8))
                    | ((long)((value >> 8) & 0xFF000000) | ((value >> 24) & 0xFF0000) | ((value >> 40) & 0xFF00) | ((value >> 56) & 0xFF)));
            }
            else
                Write(value);
        }

        public void Write(ulong value, Endian endian)
        {
            if (endian == Endian.Big)
            {
                Write(((ulong)((value & 0xFF) << 56) | ((value & 0xFF00) << 40) | ((value & 0xFF0000) << 24) | ((value & 0xFF000000) << 8))
                    | ((ulong)((value >> 8) & 0xFF000000) | ((value >> 24) & 0xFF0000) | ((value >> 40) & 0xFF00) | ((value >> 56) & 0xFF)));
            }
            else
                Write(value);
        }

        private void WriteString(string str)
        {
            if (str.Length == 0) return;
            int byteCount = _encoding.GetByteCount(str);
            EnsureBuffer(byteCount);
            _encoding.GetBytes(str, 0, str.Length, _ioBuffer, 0);
            Write(_ioBuffer, 0, byteCount);
        }

        // String bytes + null terminator packed into _ioBuffer → single Write call.

        public void WriteNullTerminatedString(string str)
        {
            int strBytes = str.Length == 0 ? 0 : _encoding.GetByteCount(str);
            int total = strBytes + _nullTermBytes.Length;
            EnsureBuffer(total);
            if (strBytes > 0)
                _encoding.GetBytes(str, 0, str.Length, _ioBuffer, 0);
            Buffer.BlockCopy(_nullTermBytes, 0, _ioBuffer, strBytes, _nullTermBytes.Length);
            Write(_ioBuffer, 0, total);
        }

        public void WriteSizedString(string str)
        {
            Write7BitEncodedInt(str.Length);
            WriteString(str);
        }

        public void WriteSizedNullTerminatedString(string str)
        {
            Write7BitEncodedInt(str.Length);
            WriteNullTerminatedString(str);
        }

        // Entire string + null padding in one Write call.

        public void WriteFixedSizedString(string str, int size)
        {
            int strBytes = str.Length == 0 ? 0 : _encoding.GetByteCount(str);
            int padCount = size - str.Length;
            int padBytes = padCount > 0 ? padCount * _nullTermBytes.Length : 0;
            int total = strBytes + padBytes;
            if (total == 0) return;

            EnsureBuffer(total);
            if (strBytes > 0)
                _encoding.GetBytes(str, 0, str.Length, _ioBuffer, 0);
            if (padBytes > 0)
                Array.Clear(_ioBuffer, strBytes, padBytes);
            Write(_ioBuffer, 0, total);
        }

        // pack up to 5 bytes into _ioBuffer, then one Wrtie call.

        public new void Write7BitEncodedInt(int value)
        {
            uint v = (uint)value;
            int count = 0;
            while (v >= 0x80)
            {
                _ioBuffer[count++] = (byte)(v | 0x80);
                v >>= 7;
            }
            _ioBuffer[count++] = (byte)v;
            Write(_ioBuffer, 0, count);
        }

        // pack up to 10 bytes into _ioBuffer, then one Wrtie call.

        public void Write7BitEncodedLong(long value)
        {
            ulong v = (ulong)value;
            int count = 0;
            while (v >= 0x80)
            {
                _ioBuffer[count++] = (byte)(v | 0x80);
                v >>= 7;
            }
            _ioBuffer[count++] = (byte)v;
            Write(_ioBuffer, 0, count);
        }

        public void Write(Sha1 value) => Write(value.ToByteArray(), 0, 20);

        // Precomputed encode aware CRLF bytes to single Write call.

        public void WriteLine(string str)
        {
            WriteString(str);
            Write(_crlfBytes, 0, _crlfBytes.Length);
        }

        // All padding 0s in one Write call instead of N Write (byte) calls.
        // Alignment is byte (max 255).
        // Preserves original DivideByZeroException on alignment=0.
        public void WritePadding(byte alignment)
        {
            long rem = Position % alignment;
            if (rem == 0) return;
            int count = (int)(alignment - rem);
            Array.Clear(_ioBuffer, 0, count);
            Write(_ioBuffer, 0, count);
        }

        public byte[] ToByteArray() => BaseStream is MemoryStream stream ? stream.ToArray() : null;
    }
}
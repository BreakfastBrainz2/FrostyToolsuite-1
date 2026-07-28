using System;
using System.IO;
using FrostySdk.Interfaces;
using System.Text;

namespace FrostySdk.IO
{
    public enum Endian
    {
        Little,
        Big
    }

    public class NativeReader : IDisposable
    {
        public Stream BaseStream => stream;

        public virtual long Position {
            get => stream?.Position ?? 0;
            set {
                if (deobfuscator == null || !deobfuscator.AdjustPosition(this, value))
                    stream.Position = value;
            }
        }
        public virtual long Length => streamLength;

        protected Stream stream;
        protected IDeobfuscator deobfuscator;
        protected byte[] buffer;
        protected long streamLength;

        // Instance level reusable buffers to completely eliminate heap allocations during string parsing.
        // Threasafe across different reader instances, but not concurrent or reentrant on the same instance.
        private char[] _stringBuffer;
        private byte[] _stringByteBuffer;

        public NativeReader(Stream inStream)
        {
            stream = inStream;
            if (stream != null)
                streamLength = stream.Length;

            buffer = new byte[20];
        }

        public NativeReader(Stream inStream, IDeobfuscator inDeobfuscator)
            : this(inStream)
        {
            deobfuscator = inDeobfuscator;
            if (deobfuscator != null && stream != null)
            {
                long newLength = deobfuscator.Initialize(this);
                if (newLength != -1)
                    streamLength = newLength;
            }
        }

        public static byte[] ReadInStream(Stream inStream)
        {
            using (NativeReader reader = new NativeReader(inStream))
                return reader.ReadToEnd();
        }

        #region Basic Types

        public char ReadWideChar()
        {
            FillBuffer(2);
            return (char)(buffer[0] | (buffer[1] << 8));
        }

        public bool ReadBoolean() => ReadByte() == 1;

        public byte ReadByte()
        {
            FillBuffer(1);
            return buffer[0];
        }

        public sbyte ReadSByte()
        {
            FillBuffer(1);
            return (sbyte)buffer[0];
        }

        public short ReadShort(Endian inEndian = Endian.Little)
        {
            FillBuffer(2);
            var b = buffer;
            if (inEndian == Endian.Little)
                return (short)(b[0] | (b[1] << 8));
            return (short)(b[1] | (b[0] << 8));
        }

        public ushort ReadUShort(Endian inEndian = Endian.Little)
        {
            FillBuffer(2);
            var b = buffer;
            if (inEndian == Endian.Little)
                return (ushort)(b[0] | (b[1] << 8));
            return (ushort)(b[1] | (b[0] << 8));
        }

        public int ReadInt(Endian inEndian = Endian.Little)
        {
            FillBuffer(4);
            var b = buffer;
            if (inEndian == Endian.Little)
                return b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
            return (b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3];
        }

        public uint ReadUInt(Endian inEndian = Endian.Little)
        {
            FillBuffer(4);
            var b = buffer;
            if (inEndian == Endian.Little)
                return (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
            return (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
        }

        public long ReadLong(Endian inEndian = Endian.Little)
        {
            FillBuffer(8);
            var b = buffer;
            if (inEndian == Endian.Little)
            {
                uint lo = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
                uint hi = (uint)(b[4] | (b[5] << 8) | (b[6] << 16) | (b[7] << 24));
                return (long)(((ulong)hi << 32) | lo);
            }
            else
            {
                uint hi = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
                uint lo = (uint)((b[4] << 24) | (b[5] << 16) | (b[6] << 8) | b[7]);
                return (long)(((ulong)hi << 32) | lo);
            }
        }

        public ulong ReadULong(Endian inEndian = Endian.Little)
        {
            FillBuffer(8);
            var b = buffer;
            if (inEndian == Endian.Little)
            {
                uint lo = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
                uint hi = (uint)(b[4] | (b[5] << 8) | (b[6] << 16) | (b[7] << 24));
                return ((ulong)hi << 32) | lo;
            }
            else
            {
                uint hi = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
                uint lo = (uint)((b[4] << 24) | (b[5] << 16) | (b[6] << 8) | b[7]);
                return ((ulong)hi << 32) | lo;
            }
        }

        public unsafe float ReadFloat(Endian inEndian = Endian.Little)
        {
            FillBuffer(4);
            var b = buffer;
            uint tmpBuffer = (inEndian == Endian.Little)
                ? (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24))
                : (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);

            return *(float*)&tmpBuffer;
        }

        public unsafe double ReadDouble(Endian inEndian = Endian.Little)
        {
            FillBuffer(8);
            var b = buffer;
            ulong tmpBuffer;

            if (inEndian == Endian.Little)
            {
                uint lo = (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));
                uint hi = (uint)(b[4] | (b[5] << 8) | (b[6] << 16) | (b[7] << 24));
                tmpBuffer = ((ulong)hi << 32) | lo;
            }
            else
            {
                uint lo = (uint)((b[0] << 24) | (b[1] << 16) | (b[2] << 8) | b[3]);
                uint hi = (uint)((b[4] << 24) | (b[5] << 16) | (b[6] << 8) | b[7]);
                tmpBuffer = ((ulong)hi << 32) | lo;
            }

            return *(double*)&tmpBuffer;
        }

        #endregion

        #region -- Special Types --

        public Guid ReadGuid(Endian endian = Endian.Little)
        {
            FillBuffer(16);
            var b = buffer;

            if (endian == Endian.Little)
            {
                int a = b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24);
                short c = (short)(b[4] | (b[5] << 8));
                short d = (short)(b[6] | (b[7] << 8));
                return new Guid(a, c, d, b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
            }
            else
            {
                int a = b[3] | (b[2] << 8) | (b[1] << 16) | (b[0] << 24);
                short c = (short)(b[5] | (b[4] << 8));
                short d = (short)(b[7] | (b[6] << 8));
                return new Guid(a, c, d, b[8], b[9], b[10], b[11], b[12], b[13], b[14], b[15]);
            }
        }

        public Sha1 ReadSha1()
        {
            FillBuffer(20);
            return new Sha1(buffer);
        }

        public int Read7BitEncodedInt()
        {
            int result = 0;
            int i = 0;

            while (true)
            {
                int b = ReadByte();
                result |= (b & 127) << i;

                if ((b >> 7) == 0)
                    return result;

                i += 7;
            }
        }

        public long Read7BitEncodedLong()
        {
            long result = 0;
            int i = 0;

            while (true)
            {
                int b = ReadByte();
                result |= (long)(b & 127) << i;

                if ((b >> 7) == 0)
                    return result;

                i += 7;
            }
        }

        #endregion

        #region String Types

        private void EnsureStringBufferCapacity(ref char[] buf, int requiredCapacity, int currentCount)
        {
            if (buf.Length < requiredCapacity)
            {
                int newSize = buf.Length * 2;
                if (newSize < requiredCapacity) newSize = requiredCapacity;

                char[] newBuf = new char[newSize];
                if (currentCount > 0)
                {
                    Array.Copy(buf, newBuf, currentCount);
                }
                _stringBuffer = buf = newBuf;
            }
        }

        public string ReadNullTerminatedString()
        {
            char[] buf = _stringBuffer;
            if (buf == null)
                _stringBuffer = buf = new char[256];
            int count = 0;

            while (true)
            {
                byte b = ReadByte();
                if (b == 0x00)
                    return new string(buf, 0, count);

                if (count == buf.Length)
                {
                    EnsureStringBufferCapacity(ref buf, buf.Length * 2, count);
                }
                buf[count++] = (char)b;
            }
        }

        public string ReadNullTerminatedWideString()
        {
            char[] buf = _stringBuffer;
            if (buf == null)
                _stringBuffer = buf = new char[256];
            int count = 0;

            while (true)
            {
                char c = ReadWideChar();
                if (c == 0x0000)
                    return new string(buf, 0, count);

                if (count == buf.Length)
                {
                    EnsureStringBufferCapacity(ref buf, buf.Length * 2, count);
                }
                buf[count++] = c;
            }
        }

        public string ReadSizedString(int strLen)
        {
            if (strLen <= 0) return string.Empty;

            char[] cBuf = _stringBuffer;
            if (cBuf == null)
                _stringBuffer = cBuf = new char[Math.Max(256, strLen)];
            else if (cBuf.Length < strLen)
                _stringBuffer = cBuf = new char[strLen];

            int count = 0;

            // Reuses the instance level byte array to completely avoid allocating byte[] blocks on the heap
            byte[] bBuf = _stringByteBuffer;
            if (bBuf == null)
                _stringByteBuffer = bBuf = new byte[Math.Max(256, strLen)];
            else if (bBuf.Length < strLen)
                _stringByteBuffer = bBuf = new byte[strLen];

            int bytesRead = Read(bBuf, 0, strLen);

            for (int i = 0; i < bytesRead; i++)
            {
                byte b = bBuf[i];
                if (b != 0x00)
                {
                    cBuf[count++] = (char)b;
                }
            }

            return new string(cBuf, 0, count);
        }

        public string ReadLine()
        {
            char[] buf = _stringBuffer;
            if (buf == null)
                _stringBuffer = buf = new char[256];
            int count = 0;

            byte c = 0x00;
            while (c != 0x0d && c != 0x0a)
            {
                c = ReadByte();
                if (count == buf.Length)
                {
                    EnsureStringBufferCapacity(ref buf, buf.Length * 2, count);
                }
                buf[count++] = (char)c;
                if (c == 0x0a || c == 0x0d || Position >= Length)
                    break;
            }

            if (c == 0x0d)
                ReadByte();

            return new string(buf, 0, count).Trim('\r', '\n');
        }

        public string ReadWideLine()
        {
            char[] buf = _stringBuffer;
            if (buf == null)
                _stringBuffer = buf = new char[256];
            int count = 0;

            char c = (char)0x00;
            while (c != 0x0d && c != 0x0a)
            {
                c = ReadWideChar();
                if (count == buf.Length)
                {
                    EnsureStringBufferCapacity(ref buf, buf.Length * 2, count);
                }
                buf[count++] = c;
                if (c == 0x0a || c == 0x0d || Position >= Length)
                    break;
            }

            if (c == 0x0d)
                ReadWideChar();

            return new string(buf, 0, count).Trim('\r', '\n');
        }

        public void Pad(int alignment)
        {
            if (alignment <= 0) return;

            if (deobfuscator == null)
            {
                // Fast path: O(1) mathematical jump when stream structure is direct
                long currentPos = Position;
                long rem = currentPos % alignment;
                if (rem != 0)
                {
                    Position = currentPos + (alignment - rem);
                }
            }
            else
            {
                // Strict compatibility fallback: Step by byte progression to keep the state of sequential deobfuscator stream ciphers synchronized.
                while (Position % alignment != 0)
                {
                    Position++;
                }
            }
        }

        #endregion

        public byte[] ReadToEnd()
        {
            long totalSize = Length - Position;
            if (totalSize <= 0)
                return Array.Empty<byte>();

            // 0x7FEFFFFF (2,146,435,071 bytes) is the absolute maximum byte array size supported by the CLR.
            // Rejecting sizes above this boundary prevents runtime allocation failures and silent offset wrapping issues yippe.
            if (totalSize >= 0X7FEFFFFF)
            {
                throw new NotSupportedException("Streams larger than 2GB are not supported by ReadToEnd.");
            }

            return ReadBytes((int)totalSize);
        }

        public byte[] ReadBytes(int count)
        {
            if (count <= 0) return Array.Empty<byte>();

            byte[] outBuffer = new byte[count];
            int totalNumBytesRead = 0;

            do
            {
                int numBytesRead = Read(outBuffer, totalNumBytesRead, count);
                if (numBytesRead == 0)
                    break;

                totalNumBytesRead += numBytesRead;
                count -= numBytesRead;

            } while (count > 0);

            return outBuffer;
        }

        public virtual int Read(byte[] inBuffer, int offset, int numBytes)
        {
            int count = stream.Read(inBuffer, offset, numBytes);
            deobfuscator?.Deobfuscate(inBuffer, Position, offset, numBytes);
            return count;
        }

        public Stream CreateViewStream(long offset, long size)
        {
            Position = offset;
            return new MemoryStream(ReadBytes((int)size));
        }

        public void Dispose() => Dispose(true);

        protected virtual void FillBuffer(int numBytes)
        {
            stream.Read(buffer, 0, numBytes);
            deobfuscator?.Deobfuscate(buffer, Position, 0, numBytes);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stream copyOfStream = stream;
                stream = null;

                copyOfStream?.Close();
            }

            stream = null;
            buffer = null;
            _stringBuffer = null;
            _stringByteBuffer = null;
        }
    }
}
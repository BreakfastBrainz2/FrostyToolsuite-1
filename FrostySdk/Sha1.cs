using System;

namespace FrostySdk
{
    public struct Sha1 : IEquatable<Sha1>
    {
        public static readonly Sha1 Zero = new Sha1();
        private uint a, b, c, d, e;

        public Sha1(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 20)
                throw new ArgumentException("Input buffer is too small");

            a = (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);
            b = (uint)(bytes[4] | bytes[5] << 8 | bytes[6] << 16 | bytes[7] << 24);
            c = (uint)(bytes[8] | bytes[9] << 8 | bytes[10] << 16 | bytes[11] << 24);
            d = (uint)(bytes[12] | bytes[13] << 8 | bytes[14] << 16 | bytes[15] << 24);
            e = (uint)(bytes[16] | bytes[17] << 8 | bytes[18] << 16 | bytes[19] << 24);
        }

        public Sha1(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length < 40)
                throw new ArgumentException("Input text is too short to be a Sha1 hash");

            byte[] bytes = new byte[20];

            for (int i = 0; i < 40; i += 2)
            {
                bytes[i / 2] = (byte)((HexToInt(text[i]) << 4) | HexToInt(text[i + 1]));
            }

            a = (uint)(bytes[0] | bytes[1] << 8 | bytes[2] << 16 | bytes[3] << 24);
            b = (uint)(bytes[4] | bytes[5] << 8 | bytes[6] << 16 | bytes[7] << 24);
            c = (uint)(bytes[8] | bytes[9] << 8 | bytes[10] << 16 | bytes[11] << 24);
            d = (uint)(bytes[12] | bytes[13] << 8 | bytes[14] << 16 | bytes[15] << 24);
            e = (uint)(bytes[16] | bytes[17] << 8 | bytes[18] << 16 | bytes[19] << 24);
        }

        private static int HexToInt(char h)
        {
            if (h >= '0' && h <= '9') return h - '0';
            if (h >= 'a' && h <= 'f') return h - 'a' + 10;
            if (h >= 'A' && h <= 'F') return h - 'A' + 10;

            throw new FormatException($"Invalid hex character: '{h}'");
        }

        public static bool operator ==(Sha1 A, Sha1 B) => A.Equals(B);
        public static bool operator !=(Sha1 A, Sha1 B) => !A.Equals(B);

        public bool Equals(Sha1 other) => a == other.a && b == other.b && c == other.c && d == other.d && e == other.e;

        public override bool Equals(object obj)
        {
            if (obj is Sha1 otherSha1)
                return Equals(otherSha1);

            return false;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)2166136261;
                hash = (hash * 16777619) ^ (int)a;
                hash = (hash * 16777619) ^ (int)b;
                hash = (hash * 16777619) ^ (int)c;
                hash = (hash * 16777619) ^ (int)d;
                hash = (hash * 16777619) ^ (int)e;
                return hash;
            }
        }

        public byte[] ToByteArray()
        {
            byte[] bytes = new byte[20];
            WriteTo(bytes, 0);
            return bytes;
        }

        // Used by NativeWriter to push bytes into a buffer allocation free
        public void WriteTo(byte[] buffer, int offset)
        {
            buffer[offset + 0] = (byte)(a & 0xFF); buffer[offset + 1] = (byte)((a >> 8) & 0xFF); buffer[offset + 2] = (byte)((a >> 16) & 0xFF); buffer[offset + 3] = (byte)((a >> 24) & 0xFF);
            buffer[offset + 4] = (byte)(b & 0xFF); buffer[offset + 5] = (byte)((b >> 8) & 0xFF); buffer[offset + 6] = (byte)((b >> 16) & 0xFF); buffer[offset + 7] = (byte)((b >> 24) & 0xFF);
            buffer[offset + 8] = (byte)(c & 0xFF); buffer[offset + 9] = (byte)((c >> 8) & 0xFF); buffer[offset + 10] = (byte)((c >> 16) & 0xFF); buffer[offset + 11] = (byte)((c >> 24) & 0xFF);
            buffer[offset + 12] = (byte)(d & 0xFF); buffer[offset + 13] = (byte)((d >> 8) & 0xFF); buffer[offset + 14] = (byte)((d >> 16) & 0xFF); buffer[offset + 15] = (byte)((d >> 24) & 0xFF);
            buffer[offset + 16] = (byte)(e & 0xFF); buffer[offset + 17] = (byte)((e >> 8) & 0xFF); buffer[offset + 18] = (byte)((e >> 16) & 0xFF); buffer[offset + 19] = (byte)((e >> 24) & 0xFF);
        }

        public override string ToString()
        {
            char[] cArr = new char[40];
            uint[] values = { a, b, c, d, e };

            const string hexAlphabet = "0123456789abcdef";
            int charIndex = 0;

            for (int i = 0; i < 5; i++)
            {
                uint val = values[i];

                byte b1 = (byte)(val & 0xFF);
                byte b2 = (byte)((val >> 8) & 0xFF);
                byte b3 = (byte)((val >> 16) & 0xFF);
                byte b4 = (byte)((val >> 24) & 0xFF);

                cArr[charIndex++] = hexAlphabet[b1 >> 4]; cArr[charIndex++] = hexAlphabet[b1 & 0xF];
                cArr[charIndex++] = hexAlphabet[b2 >> 4]; cArr[charIndex++] = hexAlphabet[b2 & 0xF];
                cArr[charIndex++] = hexAlphabet[b3 >> 4]; cArr[charIndex++] = hexAlphabet[b3 & 0xF];
                cArr[charIndex++] = hexAlphabet[b4 >> 4]; cArr[charIndex++] = hexAlphabet[b4 & 0xF];
            }

            return new string(cArr);
        }
    }
}
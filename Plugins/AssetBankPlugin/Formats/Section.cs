using AssetBankPlugin.Formats.GenericData;
using AssetBankPlugin.Formats.GenericData2;
using Frosty.Controls;
using FrostySdk.IO;
using System;

namespace AssetBankPlugin.Formats
{
    public abstract class Section
    {
        public abstract Endian Endianness { get; set; }
        public abstract uint DataSize { get; set; }
        public abstract uint DataOffset { get; set; }

        public static Section ReadSection(NativeReader r)
        {
            // Scan forward to tolerate alignment padding between continuous chunks
            while (r.BaseStream.Position + 12 <= r.BaseStream.Length)
            {
                long startPos = r.BaseStream.Position;

                // Peekabo first 3 bytes to see if we are at a "GD." tag
                string peek = r.ReadSizedString(3);
                r.BaseStream.Position = startPos; // Reset immediately

                if (peek == "GD.")
                {
                    string blockType = r.ReadSizedString(7); // "GD.REF2" or "GD.DAT2"
                    byte endianChar = r.ReadByte();    // 'l' or 'b'
                    Endian endian = endianChar == 'b' ? Endian.Big : Endian.Little;
                    uint size = r.ReadUInt(endian);

                    r.BaseStream.Position = startPos;

                    switch (blockType)
                    {
                        case SectionStrm.Identifier: return new SectionStrm(r, endian);
                        case SectionRefl.Identifier: return new SectionRefl(r, endian);
                        case SectionData.Identifier: return new SectionData(r, endian);
                        case SectionRef2.Identifier: return new SectionRef2(r, endian);
                        case SectionData2.Identifier: return new SectionData2(r, endian);
                        default:
                            Console.WriteLine($"[DEBUG] Found unknown GD tag '{blockType}' at 0x{startPos:X}. Skipping {size} bytes.");
                            r.BaseStream.Position = startPos + size;
                            continue;
                    }
                }
                else
                {
                    // NOT a header, This is alignment padding (eg , 00 00), advance 1 byte.
                    r.BaseStream.Position = startPos + 1;
                }
            }

            return null;
        }
    }
}
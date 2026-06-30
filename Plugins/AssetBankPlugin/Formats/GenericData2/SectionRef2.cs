using AssetBankPlugin.Extensions;
using AssetBankPlugin.Enums;
using FrostySdk.IO;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace AssetBankPlugin.Formats.GenericData2
{
    public class SectionRef2 : Section
    {
        // Set to true to enable verbose layout/field logging to the console.
        // Flip to false before shipping or when load times matter.
        private const bool VerboseLogging = false;

        public const string Identifier = "GD.REF2";
        public override Endian Endianness { get; set; }
        public override uint DataSize { get; set; }
        public override uint DataOffset { get; set; }
        public Dictionary<uint, GenericClass2> Classes { get; set; }

        public SectionRef2(NativeReader r, Endian bankEndian)
        {
            long sectionStart = r.BaseStream.Position;
            DataOffset = (uint)sectionStart;

            string magic = r.ReadSizedString(7);
            _ = r.ReadByte();
            Endianness = bankEndian;
            DataSize = r.ReadUInt(Endianness);

            ulong layoutCount = r.ReadULong(Endianness);

            // Pre-size the dictionary to layoutCount - avoids rehashing entirely.
            Classes = new Dictionary<uint, GenericClass2>((int)layoutCount);

            if (VerboseLogging)
                Console.WriteLine($"[REF2][DIAG] Start: 0x{sectionStart:X}, DataSize: {DataSize}, Count: {layoutCount}");

            long[] layoutOffsets = new long[layoutCount];
            for (int i = 0; i < (int)layoutCount; i++)
            {
                layoutOffsets[i] = r.ReadReference(Endianness);
            }

            var posToRawLayout = new Dictionary<long, RawLayout>((int)layoutCount);
            for (int i = 0; i < (int)layoutCount; i++)
            {
                if (layoutOffsets[i] == 0) continue;
                r.BaseStream.Position = layoutOffsets[i];
                posToRawLayout[layoutOffsets[i]] = ReadRawLayout(r);
            }

            // First pass: create GenericClass2 for all layouts.
            foreach (var rawLayout in posToRawLayout.Values)
            {
                var genClass = new GenericClass2
                {
                    Name = rawLayout.Name,
                    Size = (int)rawLayout.DataSize,
                    Alignment = (int)rawLayout.Alignment
                };

                // Pre-size Elements to avoid repeated List resizes.
                int entryCount = rawLayout.Entries.Length;
                if (entryCount > 0) genClass.Elements.Capacity = entryCount;

                if (VerboseLogging)
                    Console.WriteLine($"[REF2] Layout: {genClass.Name} (0x{rawLayout.Hash:X8}) size={genClass.Size} align={genClass.Alignment}");

                foreach (var entry in rawLayout.Entries)
                {
                    if (entry.LayoutHash == 0 || entry.LayoutHash == 0xFFFFFFFF) continue;

                    var field = new GenericField2
                    {
                        Name = entry.Name,
                        TypeHash = entry.LayoutHash,
                        Offset = (int)entry.Offset,
                        Size = entry.ElementSize,
                        Alignment = entry.ElementAlign,
                        IsList = (entry.Flags & EFlags.Array) != 0,
                        InlineCount = entry.Count
                    };

                    if (PrimitiveTypeMap.IsPrimitive(entry.LayoutHash))
                    {
                        field.Type = PrimitiveTypeMap.GetTypeName(entry.LayoutHash);
                        field.IsNative = true;
                    }
                    else if (entry.LayoutRef != 0 && posToRawLayout.TryGetValue(entry.LayoutRef, out var linked))
                    {
                        field.Type = linked.Name;
                        field.IsNative = linked.IsPod;
                    }
                    else
                    {
                        field.Type = $"Class_0x{entry.LayoutHash:X8}";
                    }

                    genClass.Elements.Add(field);

                    if (VerboseLogging)
                        Console.WriteLine($"  Field: {field.Name} type={field.Type} off=0x{field.Offset:X} size={field.Size} align={field.Alignment} isList={field.IsList} inlineCount={field.InlineCount}");
                }

                if (rawLayout.Reordered)
                {
                    genClass.Elements.Sort((a, b) => a.Offset.CompareTo(b.Offset));
                }
                Classes[rawLayout.Hash] = genClass;
            }

            // Second pass: resolve element types for array fields.
            // Iterate Classes.Values directly - no .ToList() allocation needed since we
            // only mutate field properties, never the dictionary structure itself.
            foreach (var cls in Classes.Values)
            {
                foreach (var field in cls.Elements)
                {
                    if (field.IsList)
                    {
                        if (!Classes.TryGetValue(field.TypeHash, out var arrayLayout))
                        {
                            if (PrimitiveTypeMap.IsPrimitive(field.TypeHash))
                            {
                                field.ElementTypeHash = field.TypeHash;
                                field.ElementSize = (uint)PrimitiveTypeMap.GetTypeSize(field.TypeHash);
                                field.ElementAlignment = Math.Min(field.ElementSize, 8u);
                                field.Type = PrimitiveTypeMap.GetTypeName(field.TypeHash) + "[]";
                            }
                        }
                        else
                        {
                            // An array element's size is the TOTAL SIZE of its class layout.
                            field.ElementTypeHash = field.TypeHash;
                            field.ElementSize = (uint)arrayLayout.Size;
                            field.ElementAlignment = (uint)arrayLayout.Alignment;

                            field.Type = PrimitiveTypeMap.IsPrimitive(field.ElementTypeHash)
                                ? PrimitiveTypeMap.GetTypeName(field.ElementTypeHash) + "[]"
                                : arrayLayout.Name + "[]";
                        }
                    }
                    else if (field.InlineCount > 1)
                    {
                        if (Classes.TryGetValue(field.TypeHash, out var elemClass))
                        {
                            field.ElementTypeHash = field.TypeHash;
                            field.ElementSize = (uint)elemClass.Size;
                            field.ElementAlignment = (uint)elemClass.Alignment;
                        }
                        else
                        {
                            field.ElementTypeHash = field.TypeHash;
                            field.ElementSize = field.Size;
                            field.ElementAlignment = field.Alignment;
                        }
                    }
                }
            }

            // Third pass: resolve any remaining zero sizes/alignments from class layouts.
            ResolveClassSizes();

            r.BaseStream.Position = sectionStart + DataSize;
        }

        private void ResolveClassSizes()
        {
            bool changed;
            do
            {
                changed = false;
                foreach (var cls in Classes.Values)
                {
                    foreach (var field in cls.Elements)
                    {
                        // field.Size for non-primitive inline fields
                        if (field.Size == 0 && field.TypeHash != 0 && !PrimitiveTypeMap.IsPrimitive(field.TypeHash))
                        {
                            if (Classes.TryGetValue(field.TypeHash, out var fieldClass))
                            {
                                field.Size = (uint)fieldClass.Size;
                                field.Alignment = (uint)fieldClass.Alignment;
                                if (VerboseLogging)
                                    Console.WriteLine($"[REF2] Corrected field '{field.Name}' size to {field.Size} from class layout.");
                                changed = true;
                            }
                        }

                        // Element size for list fields
                        if (field.IsList)
                        {
                            if (field.ElementSize == 0 && field.ElementTypeHash != 0 && !PrimitiveTypeMap.IsPrimitive(field.ElementTypeHash))
                            {
                                if (Classes.TryGetValue(field.ElementTypeHash, out var elemClass))
                                {
                                    field.ElementSize = (uint)elemClass.Size;
                                    field.ElementAlignment = (uint)elemClass.Alignment;
                                    if (VerboseLogging)
                                        Console.WriteLine($"[REF2] Corrected list element size for '{field.Name}' to {field.ElementSize}");
                                    changed = true;
                                }
                            }
                            // Ensure inline field size for the list heap header is correct (should be 16).
                            if (field.Size == 0)
                            {
                                field.Size = 16; // capacity(4) + count(4) + pointer(8)
                                field.Alignment = 8;
                                if (VerboseLogging)
                                    Console.WriteLine($"[REF2] Corrected list field '{field.Name}' inline size to 16.");
                                changed = true;
                            }
                        }
                        // Inline fixed arrays
                        else if (field.InlineCount > 1)
                        {
                            if (field.ElementSize == 0 && field.ElementTypeHash != 0 && !PrimitiveTypeMap.IsPrimitive(field.ElementTypeHash))
                            {
                                if (Classes.TryGetValue(field.ElementTypeHash, out var elemClass))
                                {
                                    field.ElementSize = (uint)elemClass.Size;
                                    field.ElementAlignment = (uint)elemClass.Alignment;
                                    if (VerboseLogging)
                                        Console.WriteLine($"[REF2] Corrected inline array element size for '{field.Name}' to {field.ElementSize}");
                                    changed = true;
                                }
                            }
                        }
                    }
                }
            } while (changed);
        }

        private RawLayout ReadRawLayout(NativeReader r)
        {
            var l = new RawLayout();
            l.MinSlot = r.ReadInt(Endianness);
            l.MaxSlot = r.ReadInt(Endianness);
            l.DataSize = r.ReadUInt(Endianness);
            l.Alignment = r.ReadUInt(Endianness);
            uint stringTableSize = r.ReadUInt(Endianness);
            _ = r.ReadUInt(Endianness);
            l.Reordered = r.ReadByte() != 0;
            l.IsPod = r.ReadByte() != 0;
            _ = r.ReadUShort(Endianness);
            l.Hash = r.ReadUInt(Endianness);

            int count = l.MaxSlot - l.MinSlot + 1;
            l.Entries = new RawEntry[count];

            for (int i = 0; i < count; i++)
            {
                l.Entries[i] = new RawEntry
                {
                    LayoutHash = r.ReadUInt(Endianness),
                    ElementSize = r.ReadUInt(Endianness),
                    Offset = r.ReadUInt(Endianness),
                    NameIdx = r.ReadInt(Endianness),
                    Count = r.ReadUShort(Endianness),
                    Flags = (EFlags)r.ReadUShort(Endianness),
                    ElementAlign = r.ReadUShort(Endianness),
                    RLE = r.ReadShort(Endianness),
                    LayoutRef = r.ReadReference(Endianness)
                };
            }

            if (stringTableSize > 0)
            {
                byte[] strings = r.ReadBytes((int)stringTableSize);
                l.Name = GetString(strings, 1);
                for (int i = 0; i < count; i++)
                {
                    int idx = l.Entries[i].NameIdx;
                    l.Entries[i].Name = idx >= 0 && idx < strings.Length ? GetString(strings, idx) : "unknown";
                }
            }
            return l;
        }

        private static string GetString(byte[] data, int idx)
        {
            // Manual scan is faster than Array.IndexOf for short strings and avoids
            // the IEqualityComparer overhead of the generic overload.
            int end = idx;
            int len = data.Length;
            while (end < len && data[end] != 0) end++;
            return Encoding.ASCII.GetString(data, idx, end - idx);
        }

        private class RawLayout
        {
            public int MinSlot, MaxSlot;
            public uint DataSize, Alignment, Hash;
            public bool Reordered, IsPod;
            public string Name;
            public RawEntry[] Entries;
        }

        private class RawEntry
        {
            public uint LayoutHash, ElementSize, Offset;
            public int NameIdx;
            public ushort Count, ElementAlign;
            public short RLE;
            public EFlags Flags;
            public long LayoutRef;
            public string Name;
        }
    }
}
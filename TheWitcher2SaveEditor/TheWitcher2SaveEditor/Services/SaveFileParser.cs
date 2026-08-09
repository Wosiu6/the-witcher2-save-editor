using System.Text;
using TheWitcher2SaveEditor.Models;

namespace TheWitcher2SaveEditor.Services;

public class SaveFileParser
{
    public W2SaveFile Parse(byte[] fileBytes)
    {
        var save = new W2SaveFile();
        save.DzipHeader = ParseDzipHeader(fileBytes);
        save.FileEntry = ParseFileEntry(fileBytes, (int)save.DzipHeader.MetaOffset);
        save.RawDecompressedData = DecompressFileData(fileBytes, save.FileEntry);
        ParseSavyStructure(save);
        return save;
    }

    public byte[] Rebuild(W2SaveFile save)
    {
        var decompressed = save.RawDecompressedData;

        var compBuffer = new byte[decompressed.Length + decompressed.Length / 16 + 64 + 3];
        int compSize = Lzf.Compress(decompressed, decompressed.Length, compBuffer, compBuffer.Length);

        byte[] compressed;
        if (compSize == 0)
            compressed = EncodeLzfLiterals(decompressed, 0, decompressed.Length);
        else
        {
            compressed = new byte[compSize];
            Array.Copy(compBuffer, compressed, compSize);
        }

        const int decompBlockSize = 65536;
        int numSeekEntries = decompressed.Length / decompBlockSize;
        if (numSeekEntries < 1) numSeekEntries = 0;

        var seekTable = new int[numSeekEntries];
        if (numSeekEntries > 0)
        {
            int iidx = 0;
            int oidx = 0;
            int seekIdx = 0;
            int nextBoundary = decompBlockSize;

            while (iidx < compressed.Length && seekIdx < numSeekEntries)
            {
                uint ctrl = compressed[iidx++];
                if (ctrl < 32)
                {
                    ctrl++;
                    oidx += (int)ctrl;
                    iidx += (int)ctrl;
                }
                else
                {
                    uint len = ctrl >> 5;
                    if (len == 7) len += compressed[iidx++];
                    iidx++;
                    oidx += (int)len + 2;
                }

                while (seekIdx < numSeekEntries && oidx >= nextBoundary)
                {
                    seekTable[seekIdx] = iidx;
                    seekIdx++;
                    nextBoundary += decompBlockSize;
                }
            }
        }

        int seekTableBytes = numSeekEntries * 4;
        int localOffset = 4 + seekTableBytes;
        long compressedLength = localOffset + compressed.Length;
        long dataOffset = 32;
        long metaOffset = dataOffset + compressedLength;

        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        writer.Write(Encoding.ASCII.GetBytes("DZIP"));
        writer.Write(save.DzipHeader.Version);
        writer.Write(save.DzipHeader.FileCount);
        writer.Write(save.DzipHeader.UserId);
        writer.Write(metaOffset);
        writer.Write(save.DzipHeader.Unknown);

        writer.Write(localOffset);
        foreach (var entry in seekTable)
            writer.Write(entry);
        writer.Write(compressed);

        var filename = Encoding.UTF8.GetBytes(save.FileEntry.Filename);
        writer.Write((short)filename.Length);
        writer.Write(filename);
        writer.Write(DateTime.UtcNow.ToFileTimeUtc());
        writer.Write((long)decompressed.Length);
        writer.Write(dataOffset);
        writer.Write(compressedLength);

        return ms.ToArray();
    }

    private static DzipHeader ParseDzipHeader(byte[] data)
    {
        if (data.Length < 32)
            throw new InvalidOperationException("File too small for DZIP header.");

        var magic = Encoding.ASCII.GetString(data, 0, 4);
        if (magic != "DZIP")
            throw new InvalidOperationException($"Not a DZIP file. Magic: {magic}");

        return new DzipHeader
        {
            Magic = magic,
            Version = BitConverter.ToInt32(data, 4),
            FileCount = BitConverter.ToInt32(data, 8),
            UserId = BitConverter.ToInt32(data, 12),
            MetaOffset = BitConverter.ToInt64(data, 16),
            Unknown = BitConverter.ToInt64(data, 24)
        };
    }

    private static DzipFileEntry ParseFileEntry(byte[] data, int offset)
    {
        int pos = offset;
        var filenameLen = BitConverter.ToInt16(data, pos); pos += 2;
        var filename = Encoding.UTF8.GetString(data, pos, filenameLen); pos += filenameLen;
        var crc = BitConverter.ToInt64(data, pos); pos += 8;
        var decompressedLength = BitConverter.ToInt64(data, pos); pos += 8;
        var dataOffset = BitConverter.ToInt64(data, pos); pos += 8;
        var compressedLength = BitConverter.ToInt64(data, pos);

        return new DzipFileEntry
        {
            Filename = filename,
            Crc = crc,
            DecompressedLength = decompressedLength,
            Offset = dataOffset,
            CompressedLength = compressedLength
        };
    }

    private static byte[] DecompressFileData(byte[] data, DzipFileEntry entry)
    {
        int fileOffset = (int)entry.Offset;
        int localOffset = BitConverter.ToInt32(data, fileOffset);
        int compStart = fileOffset + localOffset;
        int compLen = (int)(entry.CompressedLength - localOffset);

        var output = new byte[entry.DecompressedLength];
        int decompressed = Lzf.Decompress(data, compStart, compLen, output, 0, output.Length);

        if (decompressed != entry.DecompressedLength)
            throw new InvalidOperationException(
                $"Decompression size mismatch. Expected {entry.DecompressedLength}, got {decompressed}.");

        return output;
    }

    private static byte[] EncodeLzfLiterals(byte[] data, int offset, int length)
    {
        using var ms = new MemoryStream();
        int pos = 0;
        while (pos < length)
        {
            int runLen = Math.Min(32, length - pos);
            ms.WriteByte((byte)(runLen - 1));
            ms.Write(data, offset + pos, runLen);
            pos += runLen;
        }
        return ms.ToArray();
    }

    private void ParseSavyStructure(W2SaveFile save)
    {
        var data = save.RawDecompressedData;
        if (data.Length < 12)
            throw new InvalidOperationException("Decompressed data too small.");

        save.SaveMagic = Encoding.ASCII.GetString(data, 0, 4);
        if (save.SaveMagic != "SAVY")
            throw new InvalidOperationException($"Invalid save magic: {save.SaveMagic}");

        save.Unknown1 = BitConverter.ToInt32(data, 4);
        save.Unknown2 = BitConverter.ToInt32(data, 8);

        int pos = 12;
        for (int i = 0; i < 32; i++)
        {
            if (pos + 36 > data.Length) break;

            var name = Encoding.UTF8.GetString(data, pos, 32).TrimEnd('\0');
            pos += 32;
            var offset = BitConverter.ToInt32(data, pos);
            pos += 4;

            if (string.IsNullOrEmpty(name)) break;

            save.Sections.Add(new SaveSection { Name = name, Offset = offset });
        }

        foreach (var section in save.Sections)
        {
            try
            {
                section.RootNode = ParseSectionData(data, section.Offset);
            }
            catch
            {
            }
        }
    }

    private static SaveNode? ParseSectionData(byte[] data, int offset)
    {
        if (offset + 4 > data.Length) return null;

        var nodeType = Encoding.ASCII.GetString(data, offset, 4);
        using var ms = new MemoryStream(data, offset + 4, data.Length - offset - 4);
        using var reader = new BinaryReader(ms);

        return nodeType switch
        {
            "BLCK" => ReadBlck(reader),
            _ => null
        };
    }

    private static SaveNode ReadBlck(BinaryReader reader)
    {
        var node = new SaveNode { NodeType = "BLCK" };
        var b = reader.ReadByte();
        var nameLength = b & 0x7f;
        node.Name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));

        var dataLength = reader.ReadInt32();
        var blockData = reader.ReadBytes(dataLength);

        using var blockStream = new MemoryStream(blockData);
        using var blockReader = new BinaryReader(blockStream);
        node.Children = ReadNodes(blockReader);

        return node;
    }

    private static List<SaveNode> ReadNodes(BinaryReader reader)
    {
        var nodes = new List<SaveNode>();
        while (reader.BaseStream.Position < reader.BaseStream.Length - 3)
        {
            try
            {
                var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
                switch (tag)
                {
                    case "AVAL":
                        nodes.Add(ReadAval(reader));
                        break;
                    case "BLCK":
                        nodes.Add(ReadBlck(reader));
                        break;
                    case "ROTS":
                        nodes.Add(ReadRots(reader));
                        break;
                    case "KCUP":
                        nodes.Add(ReadKcup(reader));
                        break;
                    default:
                        return nodes;
                }
            }
            catch
            {
                break;
            }
        }
        return nodes;
    }

    private static SaveNode ReadAval(BinaryReader reader)
    {
        var node = new SaveNode { NodeType = "AVAL" };
        var b = reader.ReadByte();
        var nameLength = b & 0x7f;
        node.Name = Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
        node.Value = ReadValue(reader);
        return node;
    }

    private static SaveNode ReadRots(BinaryReader reader)
    {
        return new SaveNode
        {
            NodeType = "ROTS",
            Name = "ROTS",
            RawData = reader.ReadBytes(4)
        };
    }

    private static SaveNode ReadKcup(BinaryReader reader)
    {
        var node = new SaveNode { NodeType = "KCUP", Name = "KCUP" };
        var magic = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (magic == "STOR")
        {
            var tag = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (tag == "AVAL")
                node.Children.Add(ReadAval(reader));
        }
        return node;
    }

    private static SaveValue ReadValue(BinaryReader reader)
    {
        var b = reader.ReadByte();
        var typeNameLength = b & 0x7f;
        var typeName = Encoding.UTF8.GetString(reader.ReadBytes(typeNameLength));

        var firstTwo = reader.ReadBytes(2);
        int valueLength;
        if (firstTwo[0] == 0xff && firstTwo[1] == 0xff)
        {
            valueLength = reader.ReadInt32() - 4;
        }
        else
        {
            var nextTwo = reader.ReadBytes(2);
            valueLength = BitConverter.ToInt32([firstTwo[0], firstTwo[1], nextTwo[0], nextTwo[1]], 0);
        }

        var rawBytes = reader.ReadBytes(valueLength);

        return new SaveValue
        {
            TypeName = typeName,
            RawBytes = rawBytes,
            Value = ParseTypedValue(typeName, rawBytes)
        };
    }

    private static object? ParseTypedValue(string typeName, byte[] data)
    {
        try
        {
            return typeName switch
            {
                "Bool" => data.Length > 0 && data[0] != 0,
                "Int8" => (sbyte)data[0],
                "Uint8" => data[0],
                "Int16" => BitConverter.ToInt16(data, 0),
                "Uint16" => BitConverter.ToUInt16(data, 0),
                "Int" => BitConverter.ToInt32(data, 0),
                "Uint" => BitConverter.ToUInt32(data, 0),
                "Int64" => BitConverter.ToInt64(data, 0),
                "Uint64" => BitConverter.ToUInt64(data, 0),
                "Float" => data.Length == 4 ? BitConverter.ToSingle(data, 0) : BitConverter.ToDouble(data, 0),
                "String" => ParseString(data),
                "CGUID" => data.Length >= 16 ? new Guid(data[..16]).ToString() : BitConverter.ToString(data),
                "GameTime" => data.Length >= 4 ? BitConverter.ToInt32(data, 0) : (object)BitConverter.ToString(data),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ParseString(byte[] data)
    {
        if (data.Length == 0) return "";

        bool isUnicode = (data[0] & 0x80) != 0x80;
        bool lengthExtension = (data[0] & 0x40) == 0x40;

        int dataIndex = 1;
        int expectedLength = data[0] & 0x3f;
        if (lengthExtension && data.Length > 1)
        {
            dataIndex = 2;
            expectedLength = expectedLength | (data[1] << 6);
        }

        if (isUnicode)
            expectedLength <<= 1;

        if (dataIndex + expectedLength > data.Length)
            return Encoding.UTF8.GetString(data, dataIndex, data.Length - dataIndex);

        return isUnicode
            ? Encoding.Unicode.GetString(data, dataIndex, expectedLength)
            : Encoding.UTF8.GetString(data, dataIndex, expectedLength);
    }

    public bool ApplyEdit(W2SaveFile save, string sectionName, string nodePath, string newValue)
    {
        var section = save.Sections.FirstOrDefault(s => s.Name == sectionName);
        if (section?.RootNode == null) return false;

        var node = FindNode(section.RootNode, nodePath);
        if (node?.Value == null) return false;

        var newBytes = ConvertToBytes(node.Value.TypeName, newValue);
        if (newBytes == null) return false;

        return ReplaceValueInRawData(save, section, node, newBytes);
    }

    private static SaveNode? FindNode(SaveNode root, string path)
    {
        var parts = path.Split('/');
        var current = root;

        foreach (var part in parts)
        {
            if (current == null) return null;
            var child = current.Children.FirstOrDefault(c => c.Name == part);
            if (child == null) return null;
            current = child;
        }
        return current;
    }

    private static byte[]? ConvertToBytes(string typeName, string value)
    {
        try
        {
            return typeName switch
            {
                "Bool" => [bool.Parse(value) ? (byte)1 : (byte)0],
                "Int8" => [(byte)(sbyte)sbyte.Parse(value)],
                "Uint8" => [byte.Parse(value)],
                "Int16" => BitConverter.GetBytes(short.Parse(value)),
                "Uint16" => BitConverter.GetBytes(ushort.Parse(value)),
                "Int" => BitConverter.GetBytes(int.Parse(value)),
                "Uint" => BitConverter.GetBytes(uint.Parse(value)),
                "Int64" => BitConverter.GetBytes(long.Parse(value)),
                "Uint64" => BitConverter.GetBytes(ulong.Parse(value)),
                "Float" => BitConverter.GetBytes(float.Parse(value)),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool ReplaceValueInRawData(W2SaveFile save, SaveSection section, SaveNode node, byte[] newBytes)
    {
        if (node.Value == null) return false;

        // Search for the old value bytes in the section's data region
        var data = save.RawDecompressedData;
        var oldBytes = node.Value.RawBytes;

        if (oldBytes.Length != newBytes.Length)
            return false; // Only support same-size replacements for safety

        // Find the value in the raw data starting from the section offset
        int sectionStart = section.Offset;
        int searchEnd = data.Length - oldBytes.Length;

        // Simple scan for the byte pattern (within section boundaries)
        int nextSectionEnd = save.Sections
            .Where(s => s.Offset > section.Offset)
            .Select(s => s.Offset)
            .DefaultIfEmpty(data.Length)
            .Min();

        for (int i = sectionStart; i < Math.Min(searchEnd, nextSectionEnd); i++)
        {
            bool match = true;
            for (int j = 0; j < oldBytes.Length; j++)
            {
                if (data[i + j] != oldBytes[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                Array.Copy(newBytes, 0, data, i, newBytes.Length);
                node.Value.RawBytes = newBytes;
                node.Value.Value = ParseTypedValue(node.Value.TypeName, newBytes);
                return true;
            }
        }

        return false;
    }
}

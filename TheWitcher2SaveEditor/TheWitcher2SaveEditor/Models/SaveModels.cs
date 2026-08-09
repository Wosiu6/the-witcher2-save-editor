namespace TheWitcher2SaveEditor.Models;

public class DzipHeader
{
    public string Magic { get; set; } = "";
    public int Version { get; set; }
    public int FileCount { get; set; }
    public int UserId { get; set; }
    public long MetaOffset { get; set; }
    public long Unknown { get; set; }
}

public class DzipFileEntry
{
    public string Filename { get; set; } = "";
    public long Crc { get; set; }
    public long DecompressedLength { get; set; }
    public long Offset { get; set; }
    public long CompressedLength { get; set; }
}

public class W2SaveFile
{
    public DzipHeader DzipHeader { get; set; } = new();
    public DzipFileEntry FileEntry { get; set; } = new();
    public string SaveMagic { get; set; } = "";
    public int Unknown1 { get; set; }
    public int Unknown2 { get; set; }
    public List<SaveSection> Sections { get; set; } = [];
    public byte[] RawDecompressedData { get; set; } = [];
}

public class SaveSection
{
    public string Name { get; set; } = "";
    public int Offset { get; set; }
    public SaveNode? RootNode { get; set; }
}

public class SaveNode
{
    public string NodeType { get; set; } = ""; // BLCK, AVAL, ROTS, KCUP
    public string Name { get; set; } = "";
    public List<SaveNode> Children { get; set; } = [];
    public SaveValue? Value { get; set; }
    public byte[]? RawData { get; set; }
}

public class SaveValue
{
    public string TypeName { get; set; } = "";
    public object? Value { get; set; }
    public byte[] RawBytes { get; set; } = [];

    public string DisplayValue => Value?.ToString() ?? BitConverter.ToString(RawBytes);
}

public class SaveEditRequest
{
    public string SectionName { get; set; } = "";
    public string NodePath { get; set; } = "";
    public string NewValue { get; set; } = "";
}

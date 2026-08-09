using System.Text;

// Witcher 2 DZIP/LZF Save File Analyzer
var path = @"C:\Users\Patryk\source\repos\TheWitcher2SaveEditor\save_examples\ManualSave_0001.sav";
var bytes = File.ReadAllBytes(path);

// Parse DZIP header (32 bytes)
var magic = Encoding.ASCII.GetString(bytes, 0, 4);
var version = BitConverter.ToInt32(bytes, 4);
var fileCount = BitConverter.ToInt32(bytes, 8);
var userId = BitConverter.ToInt32(bytes, 12);
var metaOffset = BitConverter.ToInt64(bytes, 16);
var unknown = BitConverter.ToInt64(bytes, 24);

Console.WriteLine($"DZIP: magic={magic}, version={version}, files={fileCount}, metaOffset={metaOffset}");

// Read file entry at metaOffset
int pos = (int)metaOffset;
var filenameLen = BitConverter.ToInt16(bytes, pos); pos += 2;
var filename = Encoding.UTF8.GetString(bytes, pos, filenameLen); pos += filenameLen;
var crc = BitConverter.ToInt64(bytes, pos); pos += 8;
var decompressedLength = BitConverter.ToInt64(bytes, pos); pos += 8;
var dataOffset = BitConverter.ToInt64(bytes, pos); pos += 8;
var compressedLength = BitConverter.ToInt64(bytes, pos); pos += 8;

Console.WriteLine($"File: {filename}");
Console.WriteLine($"  Decompressed: {decompressedLength}, Offset: {dataOffset}, Compressed: {compressedLength}");

// Read localOffset at dataOffset
int localOffset = BitConverter.ToInt32(bytes, (int)dataOffset);
Console.WriteLine($"  LocalOffset: {localOffset}");

// LZF decompress
int compStart = (int)dataOffset + localOffset;
int compLen = (int)(compressedLength - localOffset);
Console.WriteLine($"  Compressed data: offset={compStart}, length={compLen}");

var output = new byte[decompressedLength];
int decompressed = LzfDecompress(bytes, compStart, compLen, output, 0, (int)decompressedLength);
Console.WriteLine($"  Decompressed: {decompressed} bytes");

// Check the decompressed data
if (decompressed > 0)
{
    var savMagic = Encoding.ASCII.GetString(output, 0, 4);
    Console.WriteLine($"  Save magic: {savMagic}");
    var unk1 = BitConverter.ToInt32(output, 4);
    var unk2 = BitConverter.ToInt32(output, 8);
    Console.WriteLine($"  unknown1={unk1}, unknown2={unk2}");
    
    // Read section table (32 sections max, each: 32 byte name + 4 byte offset)
    int sectionPos = 12;
    Console.WriteLine("\n  Sections:");
    for (int i = 0; i < 32; i++)
    {
        var sectionName = Encoding.UTF8.GetString(output, sectionPos, 32).TrimEnd('\0');
        sectionPos += 32;
        var sectionOffset = BitConverter.ToInt32(output, sectionPos);
        sectionPos += 4;
        if (string.IsNullOrEmpty(sectionName)) break;
        Console.WriteLine($"    [{i}] '{sectionName}' at offset {sectionOffset}");
    }
}

static int LzfDecompress(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset, int outputLength)
{
    int iidx = inputOffset;
    int oidx = outputOffset;
    int inputEnd = inputOffset + inputLength;
    int outputEnd = outputOffset + outputLength;

    while (iidx < inputEnd)
    {
        uint ctrl = input[iidx++];

        if (ctrl < 32) // literal run
        {
            ctrl++;
            if (oidx + ctrl > outputEnd) return 0;
            
            for (uint i = 0; i < ctrl; i++)
                output[oidx++] = input[iidx++];
        }
        else // back reference
        {
            uint len = ctrl >> 5;
            int reference = oidx - ((int)(ctrl & 0x1f) << 8) - 1;

            if (len == 7)
                len += input[iidx++];

            reference -= input[iidx++];

            if (oidx + len + 2 > outputEnd) return 0;
            if (reference < outputOffset) return 0;

            output[oidx++] = output[reference++];
            output[oidx++] = output[reference++];

            for (uint i = 0; i < len; i++)
                output[oidx++] = output[reference++];
        }
    }

    return oidx - outputOffset;
}

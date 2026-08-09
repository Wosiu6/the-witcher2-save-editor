namespace TheWitcher2SaveEditor.Services;

/// <summary>
/// LZF compression/decompression (LibLZF algorithm used by Witcher 2 DZIP archives)
/// </summary>
public static class Lzf
{
    private const uint HLog = 14;
    private const uint HSize = 1 << 14;
    private const uint MaxLit = 1 << 5;
    private const uint MaxOff = 1 << 13;
    private const uint MaxRef = (1 << 8) + (1 << 3);

    public static int Decompress(byte[] input, int inputOffset, int inputLength, byte[] output, int outputOffset, int outputLength)
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
                if (oidx + ctrl > outputEnd)
                    throw new InvalidOperationException("LZF decompression overflow (literal).");

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

                if (oidx + len + 2 > outputEnd)
                    throw new InvalidOperationException("LZF decompression overflow (backref).");
                if (reference < outputOffset)
                    throw new InvalidOperationException("LZF decompression invalid reference.");

                output[oidx++] = output[reference++];
                output[oidx++] = output[reference++];

                for (uint i = 0; i < len; i++)
                    output[oidx++] = output[reference++];
            }
        }

        return oidx - outputOffset;
    }

    public static int Compress(byte[] input, int inputLength, byte[] output, int outputLength)
    {
        var hashTable = new long[HSize];
        Array.Clear(hashTable, 0, (int)HSize);

        uint iidx = 0;
        uint oidx = 0;
        long reference;

        uint hval = (uint)((input[iidx] << 8) | input[iidx + 1]);
        long off;
        int lit = 0;

        for (; ; )
        {
            if (iidx < inputLength - 2)
            {
                hval = (hval << 8) | input[iidx + 2];
                long hslot = ((hval ^ (hval << 5)) >> (int)((3 * 8 - HLog) - hval * 5) & (HSize - 1));
                reference = hashTable[hslot];
                hashTable[hslot] = iidx;

                if ((off = iidx - reference - 1) < MaxOff
                    && iidx + 4 < inputLength
                    && reference > 0
                    && input[reference + 0] == input[iidx + 0]
                    && input[reference + 1] == input[iidx + 1]
                    && input[reference + 2] == input[iidx + 2])
                {
                    uint len = 2;
                    uint maxlen = (uint)inputLength - iidx - len;
                    maxlen = maxlen > MaxRef ? MaxRef : maxlen;

                    if (oidx + lit + 1 + 3 >= outputLength)
                        return 0;

                    do
                        len++;
                    while (len < maxlen && input[reference + len] == input[iidx + len]);

                    if (lit != 0)
                    {
                        output[oidx++] = (byte)(lit - 1);
                        lit = -lit;
                        do
                            output[oidx++] = input[iidx + lit];
                        while (++lit != 0);
                    }

                    len -= 2;
                    iidx++;

                    if (len < 7)
                    {
                        output[oidx++] = (byte)((off >> 8) + (len << 5));
                    }
                    else
                    {
                        output[oidx++] = (byte)((off >> 8) + (7 << 5));
                        output[oidx++] = (byte)(len - 7);
                    }

                    output[oidx++] = (byte)off;
                    iidx += len - 1;

                    hval = (uint)((input[iidx] << 8) | input[iidx + 1]);
                    hval = (hval << 8) | input[iidx + 2];
                    hashTable[((hval ^ (hval << 5)) >> (int)((3 * 8 - HLog) - hval * 5) & (HSize - 1))] = iidx;
                    iidx++;

                    hval = (hval << 8) | input[iidx + 2];
                    hashTable[((hval ^ (hval << 5)) >> (int)((3 * 8 - HLog) - hval * 5) & (HSize - 1))] = iidx;
                    iidx++;
                    continue;
                }
            }
            else if (iidx == inputLength)
                break;

            lit++;
            iidx++;

            if (lit == MaxLit)
            {
                if (oidx + 1 + MaxLit >= outputLength)
                    return 0;

                output[oidx++] = (byte)(MaxLit - 1);
                lit = -lit;
                do
                    output[oidx++] = input[iidx + lit];
                while (++lit != 0);
            }
        }

        if (lit != 0)
        {
            if (oidx + lit + 1 >= outputLength)
                return 0;

            output[oidx++] = (byte)(lit - 1);
            lit = -lit;
            do
                output[oidx++] = input[iidx + lit];
            while (++lit != 0);
        }

        return (int)oidx;
    }
}

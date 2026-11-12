using System.Text;

namespace ForgeFlow.Web.Authentication;

/// <summary>
/// Minimal zero-dependency ISO/IEC 18004 QR Code (Versions 1..10, Error Correction L, Byte mode) SVG renderer.
/// Used locally for TOTP authenticator setup without external network calls.
/// </summary>
public static class QrCodeSvg
{
    // (TotalDataCodewords, EcCodewordsPerBlock, Group1Blocks, Group1DataCw, Group2Blocks, Group2DataCw)
    private static readonly (int DataCw, int EcPerBlock, int G1Blocks, int G1Data, int G2Blocks, int G2Data)[] VersionTable =
    [
        default, // 0 unused
        (19, 7, 1, 19, 0, 0),    // V1: 21x21
        (34, 10, 1, 34, 0, 0),   // V2: 25x25
        (55, 15, 1, 55, 0, 0),   // V3: 29x29
        (80, 20, 1, 80, 0, 0),   // V4: 33x33
        (108, 26, 1, 108, 0, 0), // V5: 37x37
        (136, 18, 2, 68, 0, 0),  // V6: 41x41
        (156, 20, 2, 78, 0, 0),  // V7: 45x45
        (194, 24, 2, 97, 0, 0),  // V8: 49x49
        (232, 30, 2, 116, 0, 0), // V9: 53x53
        (274, 18, 2, 68, 2, 69)  // V10: 57x57
    ];

    private static readonly int[][] AlignmentPositions =
    [
        [],
        [],
        [6, 18],
        [6, 22],
        [6, 26],
        [6, 30],
        [6, 34],
        [6, 22, 38],
        [6, 24, 42],
        [6, 26, 46],
        [6, 28, 50]
    ];

    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];

    static QrCodeSvg()
    {
        var x = 1;
        for (var i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)x;
            GfLog[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0)
                x ^= 0x11D;
        }
        for (var i = 255; i < 512; i++)
            GfExp[i] = GfExp[i - 255];
    }

    public static string GenerateSvg(string payload, int quietZone = 4)
    {
        var bytes = Encoding.UTF8.GetBytes(payload);
        var version = SelectVersion(bytes.Length);
        var size = 17 + version * 4;
        var codewords = EncodeCodewords(bytes, version);

        var modules = new bool[size, size];
        var isFunction = new bool[size, size];
        DrawFunctionPatterns(modules, isFunction, version, size);
        DrawCodewords(modules, isFunction, codewords, size);

        var bestMask = 0;
        var minPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(modules, isFunction, mask, size);
            DrawFormatBits(modules, isFunction, mask, size);
            var penalty = ComputePenalty(modules, size);
            if (penalty < minPenalty)
            {
                minPenalty = penalty;
                bestMask = mask;
            }
            ApplyMask(modules, isFunction, mask, size);
        }

        ApplyMask(modules, isFunction, bestMask, size);
        DrawFormatBits(modules, isFunction, bestMask, size);

        var totalSize = size + quietZone * 2;
        var path = new StringBuilder(size * size * 4);
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (modules[y, x])
                    path.Append($"M{x + quietZone},{y + quietZone}h1v1h-1z");
            }
        }

        return $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {totalSize} {totalSize}\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"QR\"><rect width=\"{totalSize}\" height=\"{totalSize}\" fill=\"#ffffff\"/><path d=\"{path}\" fill=\"#000000\"/></svg>";
    }

    private static int SelectVersion(int byteLength)
    {
        for (var v = 1; v < VersionTable.Length; v++)
        {
            var headerBits = v <= 9 ? 12 : 20; // 4-bit mode + 8/16-bit length
            var maxBytes = (VersionTable[v].DataCw * 8 - headerBits) / 8;
            if (byteLength <= maxBytes)
                return v;
        }
        throw new InvalidOperationException("FF-QR-PayloadTooLarge");
    }

    private static byte[] EncodeCodewords(byte[] data, int version)
    {
        var spec = VersionTable[version];
        var bitBuf = new List<bool>(spec.DataCw * 8);
        void AppendBits(int val, int len)
        {
            for (var i = len - 1; i >= 0; i--)
                bitBuf.Add(((val >> i) & 1) != 0);
        }

        AppendBits(0b0100, 4); // Byte mode
        AppendBits(data.Length, version <= 9 ? 8 : 16);
        foreach (var b in data)
            AppendBits(b, 8);

        var capacityBits = spec.DataCw * 8;
        var terminatorLen = Math.Min(4, capacityBits - bitBuf.Count);
        AppendBits(0, terminatorLen);
        while (bitBuf.Count % 8 != 0)
            bitBuf.Add(false);

        var dataCw = new byte[spec.DataCw];
        for (var i = 0; i < bitBuf.Count; i++)
        {
            if (bitBuf[i])
                dataCw[i >> 3] |= (byte)(1 << (7 - (i & 7)));
        }

        var padIndex = bitBuf.Count >> 3;
        for (var i = padIndex; i < dataCw.Length; i++)
            dataCw[i] = (byte)(((i - padIndex) & 1) == 0 ? 0xEC : 0x11);

        var totalBlocks = spec.G1Blocks + spec.G2Blocks;
        var dataBlocks = new byte[totalBlocks][];
        var ecBlocks = new byte[totalBlocks][];
        var divisor = ReedSolomonDivisor(spec.EcPerBlock);
        var offset = 0;

        for (var i = 0; i < totalBlocks; i++)
        {
            var len = i < spec.G1Blocks ? spec.G1Data : spec.G2Data;
            var block = new byte[len];
            Array.Copy(dataCw, offset, block, 0, len);
            offset += len;
            dataBlocks[i] = block;
            ecBlocks[i] = ReedSolomonRemainder(block, divisor);
        }

        var result = new List<byte>(spec.DataCw + totalBlocks * spec.EcPerBlock);
        var maxDataLen = Math.Max(spec.G1Data, spec.G2Data);
        for (var i = 0; i < maxDataLen; i++)
        {
            for (var b = 0; b < totalBlocks; b++)
            {
                if (i < dataBlocks[b].Length)
                    result.Add(dataBlocks[b][i]);
            }
        }
        for (var i = 0; i < spec.EcPerBlock; i++)
        {
            for (var b = 0; b < totalBlocks; b++)
                result.Add(ecBlocks[b][i]);
        }

        return [.. result];
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        var root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < degree; j++)
            {
                result[j] = GfMultiply(result[j], (byte)root);
                if (j + 1 < degree)
                    result[j] ^= result[j + 1];
            }
            root = GfMultiply((byte)root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(byte[] data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
                result[i] ^= GfMultiply(divisor[i], factor);
        }
        return result;
    }

    private static byte GfMultiply(byte x, byte y)
        => x == 0 || y == 0 ? (byte)0 : GfExp[GfLog[x] + GfLog[y]];

    private static void DrawFunctionPatterns(bool[,] modules, bool[,] isFunction, int version, int size)
    {
        void SetFn(int x, int y, bool dark)
        {
            if (x >= 0 && x < size && y >= 0 && y < size)
            {
                modules[y, x] = dark;
                isFunction[y, x] = true;
            }
        }

        void DrawFinder(int cx, int cy)
        {
            for (var dy = -4; dy <= 4; dy++)
            {
                for (var dx = -4; dx <= 4; dx++)
                {
                    var dist = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    SetFn(cx + dx, cy + dy, dist != 2 && dist != 4);
                }
            }
        }

        DrawFinder(3, 3);
        DrawFinder(size - 4, 3);
        DrawFinder(3, size - 4);

        for (var i = 0; i < size; i++)
        {
            if (!isFunction[6, i]) SetFn(i, 6, (i & 1) == 0);
            if (!isFunction[i, 6]) SetFn(6, i, (i & 1) == 0);
        }

        var align = AlignmentPositions[version];
        for (var i = 0; i < align.Length; i++)
        {
            for (var j = 0; j < align.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == align.Length - 1) || (i == align.Length - 1 && j == 0))
                    continue;
                for (var dy = -2; dy <= 2; dy++)
                {
                    for (var dx = -2; dx <= 2; dx++)
                        SetFn(align[i] + dx, align[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }
        }

        SetFn(8, size - 8, true);

        // Reserve format info cells
        for (var i = 0; i <= 8; i++)
        {
            if (i != 6) { SetFn(8, i, false); SetFn(i, 8, false); }
        }
        for (var i = size - 8; i < size; i++)
        {
            if (i >= size - 7) SetFn(8, i, false);
            SetFn(i, 8, false);
        }

        if (version >= 7)
        {
            var rem = version;
            for (var i = 0; i < 12; i++)
                rem = (rem << 1) ^ ((rem >> 11) * 0x1F25);
            var bits = (version << 12) | rem;
            for (var i = 0; i < 18; i++)
            {
                var bit = ((bits >> i) & 1) != 0;
                var a = size - 11 + (i % 3);
                var b = i / 3;
                SetFn(a, b, bit);
                SetFn(b, a, bit);
            }
        }
    }

    private static void DrawFormatBits(bool[,] modules, bool[,] isFunction, int mask, int size)
    {
        var data = (0b01 << 3) | mask; // EC Level L = 01
        var rem = data;
        for (var i = 0; i < 10; i++)
            rem = (rem << 1) ^ ((rem >> 9) * 0x537);
        var bits = ((data << 10) | rem) ^ 0x5412;

        for (var i = 0; i <= 5; i++) modules[i, 8] = ((bits >> i) & 1) != 0;
        modules[7, 8] = ((bits >> 6) & 1) != 0;
        modules[8, 8] = ((bits >> 7) & 1) != 0;
        modules[8, 7] = ((bits >> 8) & 1) != 0;
        for (var i = 9; i < 15; i++) modules[8, 14 - i] = ((bits >> i) & 1) != 0;

        for (var i = 0; i < 8; i++) modules[8, size - 1 - i] = ((bits >> i) & 1) != 0;
        for (var i = 8; i < 15; i++) modules[size - 15 + i, 8] = ((bits >> i) & 1) != 0;
        modules[size - 8, 8] = true;
    }

    private static void DrawCodewords(bool[,] modules, bool[,] isFunction, byte[] data, int size)
    {
        var bitIndex = 0;
        var totalBits = data.Length * 8;
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6) right = 5;
            for (var vert = 0; vert < size; vert++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? size - 1 - vert : vert;
                    if (!isFunction[y, x] && bitIndex < totalBits)
                    {
                        modules[y, x] = ((data[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        bitIndex++;
                    }
                }
            }
        }
    }

    private static void ApplyMask(bool[,] modules, bool[,] isFunction, int mask, int size)
    {
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                if (isFunction[y, x]) continue;
                var invert = mask switch
                {
                    0 => ((x + y) & 1) == 0,
                    1 => (y & 1) == 0,
                    2 => x % 3 == 0,
                    3 => (x + y) % 3 == 0,
                    4 => (((y / 2) + (x / 3)) & 1) == 0,
                    5 => (x * y) % 2 + (x * y) % 3 == 0,
                    6 => ((x * y) % 2 + (x * y) % 3) % 2 == 0,
                    7 => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
                    _ => false
                };
                if (invert) modules[y, x] = !modules[y, x];
            }
        }
    }

    private static int ComputePenalty(bool[,] modules, int size)
    {
        var penalty = 0;
        for (var y = 0; y < size; y++)
        {
            var runX = 1;
            var runY = 1;
            for (var x = 1; x < size; x++)
            {
                if (modules[y, x] == modules[y, x - 1]) { runX++; if (runX == 5) penalty += 3; else if (runX > 5) penalty++; }
                else runX = 1;
                if (modules[x, y] == modules[x - 1, y]) { runY++; if (runY == 5) penalty += 3; else if (runY > 5) penalty++; }
                else runY = 1;
            }
        }
        for (var y = 0; y < size - 1; y++)
        {
            for (var x = 0; x < size - 1; x++)
            {
                var c = modules[y, x];
                if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1])
                    penalty += 3;
            }
        }
        var dark = 0;
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
                if (modules[y, x]) dark++;
        var total = size * size;
        var k = Math.Abs(dark * 20 - total * 10) / total;
        penalty += k * 10;
        return penalty;
    }
}

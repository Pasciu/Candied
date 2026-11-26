using System;
using System.IO;
using System.IO.Compression;

namespace Candied.Vision.Analysis;

/// <summary>
/// Platform-independent pure C# in-memory PNG decoder, pixel difference evaluator, and visual marker renderer.
/// Performs zero filesystem I/O.
/// </summary>
public static class PurePngDecoder
{
    private static readonly byte[] PngHeader = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static double CalculateDiffPercentage(byte[] png1, byte[] png2, int colorDistanceThreshold = 30)
    {
        if (png1 == null || png2 == null || png1.Length == 0 || png2.Length == 0)
        {
            return 100.0;
        }

        if (png1.AsSpan().SequenceEqual(png2))
        {
            return 0.0;
        }

        try
        {
            var p1 = DecodeRgba(png1, out int w1, out int h1);
            var p2 = DecodeRgba(png2, out int w2, out int h2);

            if (w1 != w2 || h1 != h2 || w1 <= 0 || h1 <= 0)
            {
                return 100.0;
            }

            int totalPixels = w1 * h1;
            int changedPixels = 0;

            for (int i = 0; i < p1.Length && i < p2.Length; i += 4)
            {
                int diff = Math.Abs(p1[i] - p2[i]) +
                           Math.Abs(p1[i + 1] - p2[i + 1]) +
                           Math.Abs(p1[i + 2] - p2[i + 2]);

                if (diff > colorDistanceThreshold)
                {
                    changedPixels++;
                }
            }

            return totalPixels > 0 ? (double)changedPixels / totalPixels * 100.0 : 0.0;
        }
        catch
        {
            return 100.0;
        }
    }

    public static bool IsUniformColor(byte[] pngBytes, int tolerance = 4)
    {
        if (pngBytes == null || pngBytes.Length == 0)
        {
            return true;
        }

        try
        {
            byte[] rgba = DecodeRgba(pngBytes, out int width, out int height);
            if (width <= 0 || height <= 0 || rgba.Length < 4)
            {
                return true;
            }

            byte r0 = rgba[0];
            byte g0 = rgba[1];
            byte b0 = rgba[2];
            int step = Math.Max(4, (rgba.Length / 1024) & ~3);

            for (int i = 0; i < rgba.Length; i += step)
            {
                if (Math.Abs(rgba[i] - r0) > tolerance ||
                    Math.Abs(rgba[i + 1] - g0) > tolerance ||
                    Math.Abs(rgba[i + 2] - b0) > tolerance)
                {
                    return false;
                }
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    public static byte[] DrawClickMarker(byte[] pngBytes, int clickX, int clickY)
    {
        var rgba = DecodeRgba(pngBytes, out int width, out int height);
        if (width <= 0 || height <= 0 || rgba == null)
        {
            return pngBytes;
        }

        void SetPixel(int px, int py, byte r, byte g, byte b)
        {
            if (px >= 0 && px < width && py >= 0 && py < height)
            {
                int idx = (py * width + px) * 4;
                rgba[idx] = r;
                rgba[idx + 1] = g;
                rgba[idx + 2] = b;
                rgba[idx + 3] = 255;
            }
        }

        int radius = 9;
        for (int dy = -radius - 1; dy <= radius + 1; dy++)
        {
            for (int dx = -radius - 1; dx <= radius + 1; dx++)
            {
                int distSq = dx * dx + dy * dy;
                if (Math.Abs(dx) <= 1 && Math.Abs(dy) <= 1)
                {
                    SetPixel(clickX + dx, clickY + dy, 255, 0, 0);
                }
                else if (distSq >= (radius - 2) * (radius - 2) && distSq <= (radius + 2) * (radius + 2))
                {
                    if (distSq >= (radius - 1) * (radius - 1) && distSq <= (radius + 1) * (radius + 1))
                    {
                        SetPixel(clickX + dx, clickY + dy, 255, 0, 0);
                    }
                    else
                    {
                        SetPixel(clickX + dx, clickY + dy, 0, 0, 0);
                    }
                }
            }
        }

        int lineStart = radius + 2;
        int lineEnd = radius + 12;
        for (int i = lineStart; i <= lineEnd; i++)
        {
            SetPixel(clickX - i, clickY - 1, 0, 0, 0);
            SetPixel(clickX - i, clickY, 255, 0, 0);
            SetPixel(clickX - i, clickY + 1, 0, 0, 0);

            SetPixel(clickX + i, clickY - 1, 0, 0, 0);
            SetPixel(clickX + i, clickY, 255, 0, 0);
            SetPixel(clickX + i, clickY + 1, 0, 0, 0);

            SetPixel(clickX - 1, clickY - i, 0, 0, 0);
            SetPixel(clickX, clickY - i, 255, 0, 0);
            SetPixel(clickX + 1, clickY - i, 0, 0, 0);

            SetPixel(clickX - 1, clickY + i, 0, 0, 0);
            SetPixel(clickX, clickY + i, 255, 0, 0);
            SetPixel(clickX + 1, clickY + i, 0, 0, 0);
        }

        return EncodeRgbaToPng(rgba, width, height);
    }

    public static byte[] EncodeRgbaToPng(byte[] rgba, int width, int height)
    {
        if (rgba == null || rgba.Length < width * height * 4)
        {
            throw new ArgumentException("RGBA buffer does not match dimensions", nameof(rgba));
        }

        using var ms = new MemoryStream();
        ms.Write(PngHeader, 0, PngHeader.Length);

        byte[] ihdrData = new byte[13];
        ihdrData[0] = (byte)((width >> 24) & 0xFF);
        ihdrData[1] = (byte)((width >> 16) & 0xFF);
        ihdrData[2] = (byte)((width >> 8) & 0xFF);
        ihdrData[3] = (byte)(width & 0xFF);
        ihdrData[4] = (byte)((height >> 24) & 0xFF);
        ihdrData[5] = (byte)((height >> 16) & 0xFF);
        ihdrData[6] = (byte)((height >> 8) & 0xFF);
        ihdrData[7] = (byte)(height & 0xFF);
        ihdrData[8] = 8;
        ihdrData[9] = 6;
        ihdrData[10] = 0;
        ihdrData[11] = 0;
        ihdrData[12] = 0;
        WriteChunk(ms, "IHDR", ihdrData);

        using (var rawScanlines = new MemoryStream((width * 4 + 1) * height))
        {
            int rowSize = width * 4;
            for (int y = 0; y < height; y++)
            {
                rawScanlines.WriteByte(0);
                rawScanlines.Write(rgba, y * rowSize, rowSize);
            }

            rawScanlines.Position = 0;

            using var compressedStream = new MemoryStream();
            using (var zlib = new ZLibStream(compressedStream, CompressionLevel.Optimal, leaveOpen: true))
            {
                rawScanlines.CopyTo(zlib);
            }

            WriteChunk(ms, "IDAT", compressedStream.ToArray());
        }

        WriteChunk(ms, "IEND", Array.Empty<byte>());
        return ms.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        byte[] lengthBytes = new byte[4];
        int len = data.Length;
        lengthBytes[0] = (byte)((len >> 24) & 0xFF);
        lengthBytes[1] = (byte)((len >> 16) & 0xFF);
        lengthBytes[2] = (byte)((len >> 8) & 0xFF);
        lengthBytes[3] = (byte)(len & 0xFF);

        stream.Write(lengthBytes, 0, 4);
        stream.Write(typeBytes, 0, 4);
        if (data.Length > 0)
        {
            stream.Write(data, 0, data.Length);
        }

        uint crc = ComputeCrc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        crcBytes[0] = (byte)((crc >> 24) & 0xFF);
        crcBytes[1] = (byte)((crc >> 16) & 0xFF);
        crcBytes[2] = (byte)((crc >> 8) & 0xFF);
        crcBytes[3] = (byte)(crc & 0xFF);
        stream.Write(crcBytes, 0, 4);
    }

    private static readonly uint[] CrcTable = InitializeCrcTable();

    private static uint[] InitializeCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int j = 0; j < 8; j++)
            {
                if ((c & 1) != 0)
                {
                    c = 0xEDB88320u ^ (c >> 1);
                }
                else
                {
                    c >>= 1;
                }
            }

            table[i] = c;
        }

        return table;
    }

    private static uint ComputeCrc32(byte[] typeBytes, byte[] data)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < typeBytes.Length; i++)
        {
            crc = CrcTable[(crc ^ typeBytes[i]) & 0xFF] ^ (crc >> 8);
        }

        for (int i = 0; i < data.Length; i++)
        {
            crc = CrcTable[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    public static byte[] DecodeRgba(byte[] png, out int width, out int height)
    {
        if (png == null || png.Length < 33)
        {
            throw new ArgumentException("Invalid PNG buffer", nameof(png));
        }

        for (int i = 0; i < 8; i++)
        {
            if (png[i] != PngHeader[i])
            {
                throw new ArgumentException("PNG header signature mismatch", nameof(png));
            }
        }

        width = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        height = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        byte colorType = png[25];
        int bpp = colorType == 6 ? 4 : 3;

        using var idatStream = new MemoryStream();
        int offset = 8;
        while (offset < png.Length - 12)
        {
            int length = (png[offset] << 24) | (png[offset + 1] << 16) | (png[offset + 2] << 8) | png[offset + 3];
            string type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
            {
                idatStream.Write(png, offset + 8, length);
            }

            offset += 12 + length;
        }

        idatStream.Position = 0;
        using var zlib = new ZLibStream(idatStream, CompressionMode.Decompress);
        using var rawStream = new MemoryStream();
        zlib.CopyTo(rawStream);
        byte[] raw = rawStream.ToArray();

        int stride = width * bpp;
        byte[] rgba = new byte[width * height * 4];
        byte[] prevRow = new byte[stride];
        byte[] currRow = new byte[stride];

        int rawOffset = 0;
        int outOffset = 0;

        for (int y = 0; y < height; y++)
        {
            byte filter = raw[rawOffset++];
            for (int i = 0; i < stride; i++)
            {
                byte val = raw[rawOffset++];
                byte a = i >= bpp ? currRow[i - bpp] : (byte)0;
                byte b = prevRow[i];
                byte c = i >= bpp ? prevRow[i - bpp] : (byte)0;

                switch (filter)
                {
                    case 0: currRow[i] = val; break;
                    case 1: currRow[i] = (byte)(val + a); break;
                    case 2: currRow[i] = (byte)(val + b); break;
                    case 3: currRow[i] = (byte)(val + ((a + b) >> 1)); break;
                    case 4:
                        int p = a + b - c;
                        int pa = Math.Abs(p - a);
                        int pb = Math.Abs(p - b);
                        int pc = Math.Abs(p - c);
                        byte pr = (pa <= pb && pa <= pc) ? a : (pb <= pc ? b : c);
                        currRow[i] = (byte)(val + pr);
                        break;
                }
            }

            for (int x = 0; x < width; x++)
            {
                int srcIdx = x * bpp;
                rgba[outOffset++] = currRow[srcIdx];
                rgba[outOffset++] = currRow[srcIdx + 1];
                rgba[outOffset++] = currRow[srcIdx + 2];
                rgba[outOffset++] = bpp == 4 ? currRow[srcIdx + 3] : (byte)255;
            }

            Array.Copy(currRow, prevRow, stride);
        }

        return rgba;
    }
}

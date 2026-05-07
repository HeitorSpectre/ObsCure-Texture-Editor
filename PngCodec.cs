using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace HVTTool;

internal readonly record struct PngImage(int Width, int Height, byte[] Rgba);

internal static class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static void SaveBgra(string path, byte[] bgra, int width, int height)
    {
        if (bgra.Length != width * height * 4)
            throw new InvalidDataException("BGRA buffer size does not match image dimensions.");

        byte[] raw = new byte[(width * 4 + 1) * height];
        int src = 0;
        int dst = 0;
        for (int y = 0; y < height; y++)
        {
            raw[dst++] = 0;
            for (int x = 0; x < width; x++)
            {
                byte b = bgra[src++];
                byte g = bgra[src++];
                byte r = bgra[src++];
                byte a = bgra[src++];
                raw[dst++] = r;
                raw[dst++] = g;
                raw[dst++] = b;
                raw[dst++] = a;
            }
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            z.Write(raw, 0, raw.Length);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(Signature);

        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr[0..4], width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..8], height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(fs, "IHDR", ihdr);
        WriteChunk(fs, "IDAT", compressed.ToArray());
        WriteChunk(fs, "IEND", ReadOnlySpan<byte>.Empty);
    }

    public static PngImage LoadRgba(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read);
        Span<byte> sig = stackalloc byte[8];
        ReadExactly(fs, sig);
        if (!sig.SequenceEqual(Signature))
            throw new InvalidDataException("Not a PNG file.");

        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        int interlace = 0;
        byte[]? palette = null;
        byte[]? transparency = null;
        using var idat = new MemoryStream();

        while (true)
        {
            int length = ReadInt32BE(fs);
            byte[] typeBytes = new byte[4];
            ReadExactly(fs, typeBytes);
            string type = System.Text.Encoding.ASCII.GetString(typeBytes);
            byte[] data = new byte[length];
            ReadExactly(fs, data);
            _ = ReadInt32BE(fs);

            switch (type)
            {
                case "IHDR":
                    width = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(0, 4));
                    height = BinaryPrimitives.ReadInt32BigEndian(data.AsSpan(4, 4));
                    bitDepth = data[8];
                    colorType = data[9];
                    interlace = data[12];
                    break;
                case "PLTE":
                    palette = data;
                    break;
                case "tRNS":
                    transparency = data;
                    break;
                case "IDAT":
                    idat.Write(data, 0, data.Length);
                    break;
                case "IEND":
                    return Decode(width, height, bitDepth, colorType, interlace, palette, transparency, idat.ToArray());
            }
        }
    }

    public static byte[] ResizeRgbaNearest(byte[] src, int srcW, int srcH, int dstW, int dstH)
    {
        if (srcW == dstW && srcH == dstH) return src;
        byte[] dst = new byte[dstW * dstH * 4];
        for (int y = 0; y < dstH; y++)
        {
            int sy = (int)((long)y * srcH / dstH);
            for (int x = 0; x < dstW; x++)
            {
                int sx = (int)((long)x * srcW / dstW);
                Buffer.BlockCopy(src, (sy * srcW + sx) * 4, dst, (y * dstW + x) * 4, 4);
            }
        }
        return dst;
    }

    private static PngImage Decode(
        int width,
        int height,
        int bitDepth,
        int colorType,
        int interlace,
        byte[]? palette,
        byte[]? transparency,
        byte[] compressed)
    {
        if (width <= 0 || height <= 0)
            throw new InvalidDataException("PNG has invalid dimensions.");
        if (interlace != 0)
            throw new NotSupportedException("Interlaced PNGs are not supported.");
        if (bitDepth != 8)
            throw new NotSupportedException("Only 8-bit PNGs are supported.");

        int channels = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            4 => 2,
            6 => 4,
            _ => throw new NotSupportedException($"PNG color type {colorType} is not supported.")
        };

        int rowBytes = width * channels;
        byte[] filtered;
        using (var input = new MemoryStream(compressed))
        using (var z = new ZLibStream(input, CompressionMode.Decompress))
        using (var output = new MemoryStream())
        {
            z.CopyTo(output);
            filtered = output.ToArray();
        }

        int expected = (rowBytes + 1) * height;
        if (filtered.Length < expected)
            throw new InvalidDataException("PNG image data ended unexpectedly.");

        byte[] pixels = Unfilter(filtered, width, height, channels);
        byte[] rgba = new byte[width * height * 4];

        for (int i = 0, p = 0, r = 0; i < width * height; i++)
        {
            switch (colorType)
            {
                case 0:
                    rgba[r++] = pixels[p];
                    rgba[r++] = pixels[p];
                    rgba[r++] = pixels[p++];
                    rgba[r++] = 255;
                    break;
                case 2:
                    rgba[r++] = pixels[p++];
                    rgba[r++] = pixels[p++];
                    rgba[r++] = pixels[p++];
                    rgba[r++] = 255;
                    break;
                case 3:
                    int idx = pixels[p++];
                    if (palette == null || idx * 3 + 2 >= palette.Length)
                        throw new InvalidDataException("PNG palette index is out of range.");
                    rgba[r++] = palette[idx * 3 + 0];
                    rgba[r++] = palette[idx * 3 + 1];
                    rgba[r++] = palette[idx * 3 + 2];
                    rgba[r++] = transparency != null && idx < transparency.Length ? transparency[idx] : (byte)255;
                    break;
                case 4:
                    rgba[r++] = pixels[p];
                    rgba[r++] = pixels[p];
                    rgba[r++] = pixels[p++];
                    rgba[r++] = pixels[p++];
                    break;
                case 6:
                    rgba[r++] = pixels[p++];
                    rgba[r++] = pixels[p++];
                    rgba[r++] = pixels[p++];
                    rgba[r++] = pixels[p++];
                    break;
            }
        }

        return new PngImage(width, height, rgba);
    }

    private static byte[] Unfilter(byte[] filtered, int width, int height, int bpp)
    {
        int rowBytes = width * bpp;
        byte[] pixels = new byte[rowBytes * height];
        int src = 0;
        int dst = 0;

        for (int y = 0; y < height; y++)
        {
            int filter = filtered[src++];
            for (int x = 0; x < rowBytes; x++)
            {
                int raw = filtered[src++];
                int left = x >= bpp ? pixels[dst + x - bpp] : 0;
                int up = y > 0 ? pixels[dst + x - rowBytes] : 0;
                int upLeft = y > 0 && x >= bpp ? pixels[dst + x - rowBytes - bpp] : 0;

                pixels[dst + x] = filter switch
                {
                    0 => (byte)raw,
                    1 => (byte)(raw + left),
                    2 => (byte)(raw + up),
                    3 => (byte)(raw + ((left + up) >> 1)),
                    4 => (byte)(raw + Paeth(left, up, upLeft)),
                    _ => throw new InvalidDataException($"Invalid PNG filter {filter}.")
                };
            }
            dst += rowBytes;
        }

        return pixels;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static int ReadInt32BE(Stream s)
    {
        Span<byte> b = stackalloc byte[4];
        ReadExactly(s, b);
        return BinaryPrimitives.ReadInt32BigEndian(b);
    }

    private static void ReadExactly(Stream s, Span<byte> buffer)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = s.Read(buffer[read..]);
            if (n == 0) throw new EndOfStreamException();
            read += n;
        }
    }

    private static void ReadExactly(Stream s, byte[] buffer) => ReadExactly(s, buffer.AsSpan());

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> len = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, data.Length);
        s.Write(len);

        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(typeBytes);
        s.Write(data);

        uint crc = Crc32(typeBytes, data);
        BinaryPrimitives.WriteUInt32BigEndian(len, crc);
        s.Write(len);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type) crc = UpdateCrc(crc, b);
        foreach (byte b in data) crc = UpdateCrc(crc, b);
        return ~crc;
    }

    private static uint UpdateCrc(uint crc, byte b)
    {
        crc ^= b;
        for (int i = 0; i < 8; i++)
            crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }
}

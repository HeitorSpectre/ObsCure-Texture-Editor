using System;
using System.IO;
using System.Text;

namespace HVTTool;

public enum HvtFormat
{
    Unknown,
    CMPR,    // "S3TW" - 4bpp DXT1-like (Wii/GC)
    IA8,     // "G8A8" - 16bpp gray+alpha
    I8,      // "GRY8" - 8bpp gray
    RGB5A3,  // "4443" - 16bpp RGB5A3
    RGBA32,  // "ARGB" - 32bpp interleaved AR/GB tiles
    C8       // "P8WI" - 8bpp paletted, 256-entry RGB5A3 palette appended at file end
}

public sealed class HvtFile
{
    public const int HeaderSize = 0x18;

    public byte[] Header { get; private set; } = new byte[HeaderSize];
    public string FormatTag { get; private set; } = "";
    public HvtFormat Format { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Bpp { get; private set; }
    public byte[] PixelData { get; private set; } = Array.Empty<byte>();
    public byte[] Palette { get; private set; } = Array.Empty<byte>();
    public byte[] Trailer { get; private set; } = Array.Empty<byte>();
    public string Path { get; }

    public HvtFile(string path)
    {
        Path = path;
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < HeaderSize)
            throw new InvalidDataException("File too small to be HVT.");

        // Magic: " IVH" (0x20 0x49 0x56 0x48)
        if (data[0] != 0x20 || data[1] != 0x49 || data[2] != 0x56 || data[3] != 0x48)
            throw new InvalidDataException("Not an HVT file (magic mismatch).");

        Buffer.BlockCopy(data, 0, Header, 0, HeaderSize);

        FormatTag = Encoding.ASCII.GetString(data, 8, 4);
        Width = (data[0x0E] << 8) | data[0x0F];
        Height = (data[0x12] << 8) | data[0x13];
        Bpp = (data[0x14] << 24) | (data[0x15] << 16) | (data[0x16] << 8) | data[0x17];

        Format = FormatTag switch
        {
            "S3TW" => HvtFormat.CMPR,
            "G8A8" => HvtFormat.IA8,
            "GRY8" => HvtFormat.I8,
            "4443" => HvtFormat.RGB5A3,
            "ARGB" => HvtFormat.RGBA32,
            "P8WI" => HvtFormat.C8,
            _ => HvtFormat.Unknown
        };

        int pixelBytes = ComputePixelDataSize(Width, Height, Bpp);
        int payloadAvailable = data.Length - HeaderSize;

        if (payloadAvailable < pixelBytes)
            throw new InvalidDataException(
                $"File payload ({payloadAvailable}) is smaller than expected pixel data size ({pixelBytes}).");

        PixelData = new byte[pixelBytes];
        Buffer.BlockCopy(data, HeaderSize, PixelData, 0, pixelBytes);

        int afterPixels = HeaderSize + pixelBytes;
        int remainder = data.Length - afterPixels;

        if (Format == HvtFormat.C8 && remainder >= 512)
        {
            Palette = new byte[512];
            Buffer.BlockCopy(data, afterPixels, Palette, 0, 512);
            Trailer = new byte[remainder - 512];
            Buffer.BlockCopy(data, afterPixels + 512, Trailer, 0, Trailer.Length);
        }
        else if (remainder > 0)
        {
            Trailer = new byte[remainder];
            Buffer.BlockCopy(data, afterPixels, Trailer, 0, remainder);
        }
    }

    public static int ComputePixelDataSize(int width, int height, int bpp)
    {
        long bits = (long)width * height * bpp;
        return (int)((bits + 7) / 8);
    }

    public void SaveAs(string path, byte[] newPixelData, byte[]? newPalette = null)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(Header, 0, Header.Length);
        fs.Write(newPixelData, 0, newPixelData.Length);
        if (Format == HvtFormat.C8 && newPalette != null)
            fs.Write(newPalette, 0, newPalette.Length);
        else if (Palette.Length > 0)
            fs.Write(Palette, 0, Palette.Length);
        if (Trailer.Length > 0)
            fs.Write(Trailer, 0, Trailer.Length);
    }
}

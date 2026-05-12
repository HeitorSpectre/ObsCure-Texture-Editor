using System;
using System.IO;

namespace HVTTool;

// PS2/PSP paletted texture format used by some games (extension .hvi).
// Layout (all integers little-endian):
//   0x00  4 bytes  magic "HVI "
//   0x04  u32      version (=1)
//   0x08  u32      reserved (=0)
//   0x0C  u32      width
//   0x10  u32      height
//   0x14  u32      bits per pixel (=8 for the samples seen so far)
//   0x18  1024 b   256-entry RGBA palette  (PS2 CSM1 storage)
//   ...   pixel data (width * height * bpp / 8 bytes), PS2-swizzled
public sealed class HviFile
{
    public const int HeaderSize = 0x18;

    public string Path { get; }
    public byte[] Header { get; private set; } = new byte[HeaderSize];
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Bpp { get; private set; }
    public byte[] Palette { get; private set; } = Array.Empty<byte>();
    public byte[] PixelData { get; private set; } = Array.Empty<byte>();
    public byte[] Trailer { get; private set; } = Array.Empty<byte>();

    public string FormatLabel => $"PS2/PSP HVI PAL{Bpp} {Width}x{Height} {Bpp}bpp";

    public HviFile(string path)
    {
        Path = path;
        byte[] data = File.ReadAllBytes(path);
        if (data.Length < HeaderSize)
            throw new InvalidDataException("File too small to be HVI.");

        // Magic "HVI "
        if (data[0] != 0x48 || data[1] != 0x56 || data[2] != 0x49 || data[3] != 0x20)
            throw new InvalidDataException("Not an HVI file (magic mismatch).");

        Buffer.BlockCopy(data, 0, Header, 0, HeaderSize);

        Width = BitConverter.ToInt32(data, 0x0C);
        Height = BitConverter.ToInt32(data, 0x10);
        Bpp = BitConverter.ToInt32(data, 0x14);

        if (Bpp != 8)
            throw new NotSupportedException($"Only 8bpp HVI textures are supported (file is {Bpp}bpp).");

        const int paletteBytes = 256 * 4; // RGBA8 256 entries
        if (data.Length < HeaderSize + paletteBytes)
            throw new InvalidDataException("File too small to contain a palette.");

        Palette = new byte[paletteBytes];
        Buffer.BlockCopy(data, HeaderSize, Palette, 0, paletteBytes);

        int pixelBytes = (Width * Height * Bpp) / 8;
        int pixelOffset = HeaderSize + paletteBytes;
        if (data.Length < pixelOffset + pixelBytes)
            throw new InvalidDataException(
                $"File payload too small: expected {pixelOffset + pixelBytes} bytes, got {data.Length}.");

        PixelData = new byte[pixelBytes];
        Buffer.BlockCopy(data, pixelOffset, PixelData, 0, pixelBytes);

        int afterPixels = pixelOffset + pixelBytes;
        if (data.Length > afterPixels)
        {
            Trailer = new byte[data.Length - afterPixels];
            Buffer.BlockCopy(data, afterPixels, Trailer, 0, Trailer.Length);
        }
    }

    public void SaveAs(string path, byte[] newPalette, byte[] newPixelData)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        fs.Write(Header, 0, Header.Length);
        fs.Write(newPalette, 0, newPalette.Length);
        fs.Write(newPixelData, 0, newPixelData.Length);
        if (Trailer.Length > 0) fs.Write(Trailer, 0, Trailer.Length);
    }
}

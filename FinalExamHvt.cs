using System;
using System.IO;
using System.Text;

namespace HVTTool;

// HydraVision "modern" texture format used by Final Exam (and likely other
// post-ObsCure HydraVision titles). Same .hvt extension as the ObsCure GC/Wii
// format and shares the " IVH" magic on big-endian platforms — disambiguated
// by the 'HEAD' chunk tag at offset 0x0C, which ObsCure GC/Wii does not have.
//
// Three platform flavours are seen in the wild:
//   PC      magic "HVI "  (HVI + space) — all u32 little-endian, format tags
//                          stored byte-reversed ("1TXD"/"5TXD"/"DAEH"/"ATAD").
//   PS3     magic " IVH"  (space + IVH) — all u32 big-endian, format tags
//                          forward ("DXT1"/"DXT5"/"HEAD"/"DATA"). Pixel data
//                          is plain LE BC blocks.
//   X360    magic " IVH"  (space + IVH) — all u32 big-endian, texture data
//                          stored *tiled* (Xbox 360 GPU 32x32 swizzle). The
//                          DATA size lives at 0x80 and the mip0 payload starts
//                          immediately at 0x84; some files carry 8 trailing
//                          bytes after the declared DATA payload.
//
// Common header layout (offsets identical on PC and PS3; X360 adds an extra
// chunk):
//   0x00  4 bytes                          magic
//   0x04  u32                              source bpp / flag
//   0x08  u32                              HEAD body size (0x24)
//   0x0C  4 bytes                          'HEAD' tag
//   0x10  u32                              0
//   0x14  4 bytes                          pixel format ASCII
//   0x18  u32                              width
//   0x1C  u32                              height
//   0x20  u32                              bpp
//   0x24  4 bytes                          architecture tag ("X86\0"/"PS3\0"/"X360")
//   0x28  u32                              mipmap count
//   0x2C  u32                              next-chunk body size
//   0x30  4 bytes                          next chunk tag — 'DATA' on PC/PS3,
//                                          'X360' on Xbox 360 (with a 64-byte
//                                          GPU-config body, then a 'DATA' tag
//                                          at 0x74 followed by mip0 size at
//                                          0x80 and pixel data at 0x84).
public enum FxPixelFormat { Unknown, BGRA, BGRX, TXD1, TXD3, TXD5, ARGB }

public enum FxPlatform { PC, PS3, X360 }

public sealed class FinalExamHvt
{
    public string Path { get; }
    public byte[] Data { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Bpp { get; private set; }
    public int MipmapCount { get; private set; }
    public FxPixelFormat Format { get; private set; }
    public string FormatTag { get; private set; } = "";
    public int PixelOffset { get; private set; }
    public int Mip0Size { get; private set; }
    public bool IsBigEndian { get; private set; }
    public FxPlatform Platform { get; private set; }

    /// <summary>Width rounded up to the GPU tile alignment (X360 only — equals Width on PC/PS3).</summary>
    public int AlignedWidth { get; private set; }
    public int AlignedHeight { get; private set; }

    public bool IsBlockCompressed =>
        Format == FxPixelFormat.TXD1 || Format == FxPixelFormat.TXD3 || Format == FxPixelFormat.TXD5;

    public string FormatLabel => $"FinalExam/{Platform} {FormatTag} {Width}×{Height} {Bpp}bpp mips={MipmapCount}";

    public FinalExamHvt(string path)
    {
        Path = path;
        Data = File.ReadAllBytes(path);
        if (Data.Length < 0x40)
            throw new InvalidDataException("File too small to be a Final Exam HVT.");

        bool pcMagic  = Data[0] == 0x48 && Data[1] == 0x56 && Data[2] == 0x49 && Data[3] == 0x20;
        bool beMagic  = Data[0] == 0x20 && Data[1] == 0x49 && Data[2] == 0x56 && Data[3] == 0x48;
        if (!pcMagic && !beMagic)
            throw new InvalidDataException("Not a Final Exam HVT (magic mismatch).");
        IsBigEndian = beMagic;

        // Architecture tag at 0x24 chooses PS3 vs X360 for big-endian files.
        Platform = pcMagic ? FxPlatform.PC :
            (Data[0x24] == 'X' && Data[0x25] == '3' && Data[0x26] == '6' && Data[0x27] == '0')
                ? FxPlatform.X360 : FxPlatform.PS3;

        FormatTag = Encoding.ASCII.GetString(Data, 0x14, 4);
        Format = FormatTag switch
        {
            // PC (LE) — tags appear byte-reversed
            "BGRA" => FxPixelFormat.BGRA,
            "BGRX" => FxPixelFormat.BGRX,
            "1TXD" => FxPixelFormat.TXD1,
            "3TXD" => FxPixelFormat.TXD3,
            "5TXD" => FxPixelFormat.TXD5,
            // BE — tags appear forward
            "TXD1" or "DXT1" => FxPixelFormat.TXD1,
            "TXD3" or "DXT3" => FxPixelFormat.TXD3,
            "TXD5" or "DXT5" => FxPixelFormat.TXD5,
            "ARGB" => FxPixelFormat.ARGB,
            "XRGB" => FxPixelFormat.BGRX,
            _ => FxPixelFormat.Unknown
        };

        Width  = ReadU32(0x18);
        Height = ReadU32(0x1C);
        Bpp    = ReadU32(0x20);
        MipmapCount = ReadU32(0x28);

        if (Platform == FxPlatform.X360)
        {
            // Layout: HEAD(0x24-byte body) + X360(0x44-byte body) + DATA + 0,0,mip0Size, pixels.
            //   0x74 'DATA'
            //   0x80 mip0 size
            //   0x84 pixel data start
            // X360 textures are tiled in 32×32 GPU "macro" tiles. The unit of
            // a "texel" depends on the format: for ARGB it's a pixel, for BC
            // formats it's a 4×4 block. So the alignment in pixels is 32 for
            // ARGB but 128 (= 32 blocks × 4 px) for BC1/BC2/BC3.
            int tileTexel = IsBlockCompressed ? 4 : 1;
            int alignTexels = 32 * tileTexel;
            AlignedWidth  = (Width  + alignTexels - 1) & ~(alignTexels - 1);
            AlignedHeight = (Height + alignTexels - 1) & ~(alignTexels - 1);
            Mip0Size    = ReadU32(0x80);
            PixelOffset = 0x84;
        }
        else
        {
            AlignedWidth  = Width;
            AlignedHeight = Height;
            Mip0Size    = ReadU32(0x3C);
            PixelOffset = 0x40;
        }

        if (Width <= 0 || Width > 16384 || Height <= 0 || Height > 16384)
            throw new InvalidDataException($"Implausible texture size {Width}×{Height}.");
    }

    private int ReadU32(int off) => IsBigEndian
        ? (Data[off] << 24) | (Data[off + 1] << 16) | (Data[off + 2] << 8) | Data[off + 3]
        : Data[off] | (Data[off + 1] << 8) | (Data[off + 2] << 16) | (Data[off + 3] << 24);

    public void Save(string path) => File.WriteAllBytes(path, Data);

    public void SaveAs(string path, byte[] newMip0)
    {
        ReplaceMip0(newMip0);
        File.WriteAllBytes(path, Data);
    }

    public void ReplaceMip0(byte[] newMip0)
    {
        if (newMip0.Length != Mip0Size)
            throw new InvalidOperationException(
                $"Encoded mip0 is {newMip0.Length} bytes; slot expects {Mip0Size}.");
        Buffer.BlockCopy(newMip0, 0, Data, PixelOffset, newMip0.Length);
    }

    /// <summary>True if this looks like a Final Exam .hvt (PC, PS3, or X360 flavour).</summary>
    public static bool LooksLikeFinalExam(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[16];
            if (fs.Read(buf) != 16) return false;

            if (buf[0] == 0x48 && buf[1] == 0x56 && buf[2] == 0x49 && buf[3] == 0x20)
                return true;

            // " IVH" is shared with ObsCure GC/Wii — Final Exam BE files have
            // 'HEAD' at 0x0C, ObsCure GC/Wii does not.
            if (buf[0] == 0x20 && buf[1] == 0x49 && buf[2] == 0x56 && buf[3] == 0x48)
                return buf[0x0C] == 'H' && buf[0x0D] == 'E' && buf[0x0E] == 'A' && buf[0x0F] == 'D';

            return false;
        }
        catch { return false; }
    }
}

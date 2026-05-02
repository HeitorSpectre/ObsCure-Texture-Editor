using System;
using System.IO;
using System.Text;

namespace HVTTool;

// HydraVision "modern" texture format used by Final Exam (and likely other
// post-ObsCure HydraVision titles). Same .hvt extension as the ObsCure GC/Wii
// format but a totally different layout — distinguish by magic:
//   ObsCure GC/Wii:  " IVH"  (space + IVH, big-endian fields)
//   Final Exam:      "HVI "  (HVI + space, little-endian chunked format)
//
// Layout (all u32 little-endian):
//   0x00  "HVI "                       magic
//   0x04  u32                          source bpp / flag (0x28, 0x0C, 0x08 …)
//   0x08  u32                          HEAD body size (always 0x24 in samples)
//   0x0C  u32 'HEAD' multi-char        chunk tag — bytes 'D','A','E','H' in memory
//   0x10  u32                          0
//   0x14  4 bytes                      pixel format ASCII ("BGRA", "BGRX", "TXD1", "TXD5")
//   0x18  u32                          width
//   0x1C  u32                          height
//   0x20  u32                          bpp
//   0x24  u32 'X86\0' multi-char       architecture tag
//   0x28  u32                          mipmap count
//   0x2C  u32                          (engine reserved)
//   0x30  u32 'DATA' multi-char        chunk tag — bytes 'A','T','A','D'
//   0x34  u32                          0
//   0x38  u32                          0
//   0x3C  u32                          mip0 byte size
//   0x40+ pixel data                   mip0 followed by remaining mip levels
// Pixel format strings stored at header offset 0x14 (4 ASCII bytes).
// HydraVision uses a numbered-prefix convention for DXT variants ("1TXD" =
// DXT1, "5TXD" = DXT5), and forward strings for uncompressed ("BGRA", "BGRX").
public enum FxPixelFormat { Unknown, BGRA, BGRX, TXD1, TXD3, TXD5 }

public sealed class FinalExamHvt
{
    public const int HeaderSize = 0x40;

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

    public string FormatLabel => $"FinalExam {FormatTag} {Width}×{Height} {Bpp}bpp mips={MipmapCount}";

    public FinalExamHvt(string path)
    {
        Path = path;
        Data = File.ReadAllBytes(path);
        if (Data.Length < HeaderSize)
            throw new InvalidDataException("File too small to be a Final Exam HVT.");

        // Magic "HVI " (note the trailing space — distinguishes from ObsCure " IVH")
        if (Data[0] != 0x48 || Data[1] != 0x56 || Data[2] != 0x49 || Data[3] != 0x20)
            throw new InvalidDataException("Not a Final Exam HVT (magic mismatch).");

        FormatTag = Encoding.ASCII.GetString(Data, 0x14, 4);
        Format = FormatTag switch
        {
            "BGRA" => FxPixelFormat.BGRA,
            "BGRX" => FxPixelFormat.BGRX,
            "1TXD" => FxPixelFormat.TXD1,
            "3TXD" => FxPixelFormat.TXD3,
            "5TXD" => FxPixelFormat.TXD5,
            // Accept the swapped-spelling variants too in case other titles do it forward.
            "TXD1" => FxPixelFormat.TXD1,
            "TXD3" => FxPixelFormat.TXD3,
            "TXD5" => FxPixelFormat.TXD5,
            _ => FxPixelFormat.Unknown
        };

        Width = BitConverter.ToInt32(Data, 0x18);
        Height = BitConverter.ToInt32(Data, 0x1C);
        Bpp = BitConverter.ToInt32(Data, 0x20);
        MipmapCount = BitConverter.ToInt32(Data, 0x28);
        Mip0Size = BitConverter.ToInt32(Data, 0x3C);
        PixelOffset = HeaderSize;

        if (Width <= 0 || Width > 16384 || Height <= 0 || Height > 16384)
            throw new InvalidDataException($"Implausible texture size {Width}×{Height}.");
    }

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

    /// <summary>Discriminates between the two .hvt sub-formats without throwing.</summary>
    public static bool LooksLikeFinalExam(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            Span<byte> buf = stackalloc byte[4];
            if (fs.Read(buf) != 4) return false;
            return buf[0] == 0x48 && buf[1] == 0x56 && buf[2] == 0x49 && buf[3] == 0x20;
        }
        catch { return false; }
    }
}

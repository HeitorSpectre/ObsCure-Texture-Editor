using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HVTTool;

// Texture dictionary used by ObsCure 1 on the original Xbox.
// Layout (all u32 little-endian):
//   0x00  u32     descriptor table size in bytes (texture count = size / 20)
//   0x04  u32     total file size
//   0x08  u32     pixel data start offset (typically 0x800 or 0x2000)
//   0x0C  N x 20  texture descriptors
//   ...   variable metadata (8*(texCount+2)+4 bytes empirically)
//   ...   N null-terminated names matching descriptor order
//   ...   "SYMBOLTABLE\0"
//   ...   0xFF FF FF FF marker
//   ...   0xAD padding up to dataOffset
//   ...   pixel data (mipmapped, NV2A swizzled for SZ_* formats)
//
// Texture descriptor (20 bytes):
//   +0x00  u32  type/flag (always 0x00040001 in samples)
//   +0x04  u32  pixel data offset relative to dataOffset
//   +0x08  u32  zero
//   +0x0C  u32  NV097_SET_TEXTURE_FORMAT word — packs CONTEXT/DIM/COLOR/MIPMAPS/SIZE_U/SIZE_V/SIZE_P
//   +0x10  u32  zero
public enum XbrColorFormat
{
    Unknown = 0,
    SZ_A1R5G5B5 = 0x02,
    SZ_R5G6B5 = 0x05,
    SZ_A8R8G8B8 = 0x06,
    SZ_X8R8G8B8 = 0x07,
    SZ_A4R4G4B4 = 0x04,
    SZ_X1R5G5B5 = 0x03,
    SZ_DXT1 = 0x0C,
    SZ_DXT3 = 0x0E,
    SZ_DXT5 = 0x0F,
    LU_R5G6B5 = 0x11,
    LU_A8R8G8B8 = 0x12,
    LU_X8R8G8B8 = 0x13,
}

public sealed class XbrTexture
{
    public int Index;
    public string Name = "";
    public int Width;
    public int Height;
    public int MipmapLevels;
    public XbrColorFormat Format;
    public int Bpp;
    public string FormatLabel = "";
    public uint FormatWord;
    public int ImageOffset;     // absolute file offset to first mipmap
    public int ImageSize;       // bytes occupied by all mipmap levels (incl. padding to next entry)
    public string Platform = "Xbox";
    public XbrFile Owner = null!;
}

public sealed class XbrFile
{
    public string Path { get; }
    public byte[] Data { get; }
    public int DataOffset { get; private set; }
    public List<XbrTexture> Textures { get; } = new();

    public XbrFile(string path)
    {
        Path = path;
        Data = File.ReadAllBytes(path);
        if (Data.Length < 0x20) throw new InvalidDataException("XBR file too small.");

        int tableSize = BitConverter.ToInt32(Data, 0);
        int fileSize = BitConverter.ToInt32(Data, 4);
        DataOffset = BitConverter.ToInt32(Data, 8);

        if (tableSize <= 0 || tableSize % 20 != 0)
            throw new InvalidDataException($"XBR: invalid descriptor table size {tableSize}.");
        if (DataOffset <= 0 || DataOffset > Data.Length)
            throw new InvalidDataException($"XBR: invalid data offset 0x{DataOffset:X}.");

        int count = tableSize / 20;

        // Parse descriptors
        var entries = new List<(uint flag, int relOff, uint fmtWord)>();
        for (int i = 0; i < count; i++)
        {
            int off = 0x0C + i * 20;
            uint flag = BitConverter.ToUInt32(Data, off + 0);
            int rel = BitConverter.ToInt32(Data, off + 4);
            uint fmtWord = BitConverter.ToUInt32(Data, off + 12);
            entries.Add((flag, rel, fmtWord));
        }

        // Names live after the descriptor table and a fixed-size metadata block
        // of (texCount + 2) * 8 + 4 bytes (empirically derived from samples).
        int metadataLen = (count + 2) * 8 + 4;
        int p = 0x0C + tableSize + metadataLen;
        var names = new List<string>();
        while (names.Count < count && p < Data.Length)
        {
            int end = p;
            while (end < Data.Length && Data[end] != 0) end++;
            string s = Encoding.ASCII.GetString(Data, p, end - p);
            if (s == "SYMBOLTABLE" || s.Length == 0) break;
            names.Add(s);
            p = end + 1;
        }

        // Build texture entries with sizes inferred from offsets (last one runs to EOF).
        for (int i = 0; i < count; i++)
        {
            var (_, rel, fmtWord) = entries[i];
            int absOffset = DataOffset + rel;
            int nextOffset = (i + 1 < count) ? (DataOffset + entries[i + 1].relOff) : fileSize;
            int imageBytes = nextOffset - absOffset;

            int color = (int)((fmtWord >> 8) & 0xFF);
            int mip = (int)((fmtWord >> 16) & 0xF);
            int sizeU = (int)((fmtWord >> 20) & 0xF);
            int sizeV = (int)((fmtWord >> 24) & 0xF);

            var fmt = (XbrColorFormat)color;
            int bpp = BppForFormat(fmt);

            Textures.Add(new XbrTexture
            {
                Index = i,
                Name = i < names.Count ? names[i] : $"texture_{i:D3}",
                Width = 1 << sizeU,
                Height = 1 << sizeV,
                MipmapLevels = mip,
                Format = fmt,
                FormatWord = fmtWord,
                Bpp = bpp,
                FormatLabel = $"Xbox {fmt}",
                ImageOffset = absOffset,
                ImageSize = imageBytes,
                Owner = this
            });
        }
    }

    public static int BppForFormat(XbrColorFormat f) => f switch
    {
        XbrColorFormat.SZ_A1R5G5B5 => 16,
        XbrColorFormat.SZ_X1R5G5B5 => 16,
        XbrColorFormat.SZ_R5G6B5 => 16,
        XbrColorFormat.SZ_A4R4G4B4 => 16,
        XbrColorFormat.SZ_A8R8G8B8 => 32,
        XbrColorFormat.SZ_X8R8G8B8 => 32,
        XbrColorFormat.LU_R5G6B5 => 16,
        XbrColorFormat.LU_A8R8G8B8 => 32,
        XbrColorFormat.LU_X8R8G8B8 => 32,
        XbrColorFormat.SZ_DXT1 => 4,
        XbrColorFormat.SZ_DXT3 => 8,
        XbrColorFormat.SZ_DXT5 => 8,
        _ => 32
    };

    private static bool IsPrintable(byte b) => b >= 0x20 && b <= 0x7E;

    public void ReplaceImageBytes(XbrTexture tex, byte[] newImage, int level0Bytes)
    {
        if (level0Bytes > tex.ImageSize)
            throw new InvalidOperationException(
                $"Encoded mip0 ({level0Bytes}) exceeds slot ({tex.ImageSize}).");
        Buffer.BlockCopy(newImage, 0, Data, tex.ImageOffset, level0Bytes);
    }

    public void Save(string path) => File.WriteAllBytes(path, Data);
}

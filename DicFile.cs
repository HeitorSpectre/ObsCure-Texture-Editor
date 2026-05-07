using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HVTTool;

public enum DicVariant { Unknown, PC, PcDip, PS2, PSP, Wii }

public enum DicPixelFormat
{
    // PC variants (TexDict.py)
    PC_R8G8B8A8,    // 21 — PC .dic (TexDict.py says R first)
    PC_R5G6B5,      // 23
    PC_R5G5B5A1,    // 25
    Dip_B8G8R8A8,   // 21 — PC .dip (Noesis fmt_dip.py says B first)
    Dip_B5G6R5,     // 23
    Dip_B5G5R5A1,   // 25
    // PS2 (RenderWare TXD)
    PS2_8bpp_swizzled,
    PS2_4bpp_swizzled,
    PS2_RGB5551,
    PS2_RGBA8888,
    // PSP Obscure 2 indexed textures
    PSP_8bpp_swizzled,
    PSP_4bpp_swizzled,
    PSP_RGBA8888,
    // Wii (custom GX)
    Wii_I8,
    Wii_IA8,
    Wii_RGB5A3,
    Wii_RGBA8,
    Wii_C4,
    Wii_C8,
    Wii_CMPR
}

public sealed class DicTexture
{
    public int Index;
    public string Name = "";
    public int Width;
    public int Height;
    public int Bpp;
    public DicPixelFormat Format;
    public string FormatLabel = "";
    public int ImageOffset;
    public int ImageSize;
    public int PaletteOffset;       // -1 if none
    public int PaletteSize;
    public int PaletteFormat;       // Wii TLUT format (0=IA8, 1=RGB565, 2=RGB5A3); ignored for PS2
    public bool AlphaFlag;
    public bool OneBitAlphaFlag;
    public int MipmapsCount = 1;
    // Used by PC variant — captures per-mipmap raw bytes so re-encoding can place
    // them back identically (mipmap[0] is what we display/edit).
    public List<byte[]> Mipmaps = new();
    public string Platform = "";
    public DicFile Owner = null!;
}

public sealed class DicFile
{
    public string Path { get; }
    public byte[] Data { get; private set; }
    public DicVariant Variant { get; private set; }
    public List<DicTexture> Textures { get; } = new();

    public DicFile(string path)
    {
        Path = path;
        Data = File.ReadAllBytes(path);
        Variant = Detect(Data, path);

        switch (Variant)
        {
            case DicVariant.PS2: ParsePs2(); break;
            case DicVariant.PSP: ParsePsp(); break;
            case DicVariant.Wii: ParseWii(); break;
            case DicVariant.PC: ParsePc(); break;
            case DicVariant.PcDip: ParseDip(); break;
            default: throw new InvalidDataException("Could not identify DIC/DIP variant.");
        }
    }

    private static DicVariant Detect(byte[] d, string path)
    {
        if (d.Length < 12) return DicVariant.Unknown;

        // .dip = PC ObsCure 1 LE format (no magic — identify by extension).
        if (path.EndsWith(".dip", StringComparison.OrdinalIgnoreCase))
            return DicVariant.PcDip;

        // PS2 RenderWare: first chunk_id (LE u32) = 0x16 (RW_TEXTURE_DICTIONARY)
        int id = BitConverter.ToInt32(d, 0);
        if (id == 0x16) return DicVariant.PS2;
        if (LooksLikePspDic(d)) return DicVariant.PSP;
        // Wii: first u32 BE = small count (1..1024) and the 8th byte starts a printable name length 1..48
        if (d.Length > 64)
        {
            int countBe = (d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3];
            if (countBe > 0 && countBe < 4096)
            {
                int nameLen = d[7];
                if (nameLen >= 1 && nameLen <= 48 && 7 + 1 + nameLen + 28 < d.Length)
                {
                    bool printable = true;
                    for (int i = 0; i < nameLen; i++)
                        if (d[8 + i] < 32 || d[8 + i] >= 127) { printable = false; break; }
                    if (printable) return DicVariant.Wii;
                }
            }
        }
        // PC fallback: first u32 BE = small texture count, then per-texture format value 21/23/25
        if (d.Length > 32)
        {
            int countBe = (d[0] << 24) | (d[1] << 16) | (d[2] << 8) | d[3];
            if (countBe > 0 && countBe < 4096) return DicVariant.PC;
        }
        return DicVariant.Unknown;
    }

    private static bool LooksLikePspDic(byte[] d)
    {
        if (d.Length < 32) return false;
        int count = BitConverter.ToInt32(d, 0);
        if (count <= 0 || count > 4096) return false;

        int nameLen = BitConverter.ToInt32(d, 4);
        if (nameLen < 1 || nameLen > 96) return false;
        if (8 + nameLen + 16 > d.Length) return false;
        if (!IsPrintableAsciiName(d, 8, nameLen)) return false;

        int p = 8 + nameLen;
        int width = BitConverter.ToUInt16(d, p);
        int height = BitConverter.ToUInt16(d, p + 2);
        int paletteEntries = BitConverter.ToUInt16(d, p + 4);
        int bpp = d[p + 6];
        int paletteSize = BitConverter.ToInt32(d, p + 12);
        if (!IsPow2_4to2048(width) || !IsPow2_4to2048(height)) return false;

        int imageSize;
        int imageOffset;
        if (bpp == 32)
        {
            if (paletteEntries != 0 || paletteSize != 0) return false;
            imageSize = width * height * 4;
            imageOffset = p + 20;
        }
        else
        {
            if ((bpp != 4 && bpp != 8) || paletteEntries != (1 << bpp)) return false;
            if (paletteSize != paletteEntries * 4) return false;
            imageSize = (width * height * bpp + 7) / 8;
            imageOffset = p + 16 + paletteSize + 4;
        }
        return imageOffset + imageSize <= d.Length;
    }

    // ==================================================================
    // PS2 parser — RenderWare TXD walking
    // ==================================================================
    private void ParsePs2()
    {
        const int RW_STRUCT = 0x01;
        const int RW_STRING = 0x02;
        const int RW_TEXTURE_NATIVE = 0x15;
        const int RW_TEXTURE_DICTIONARY = 0x16;

        int rootId = BitConverter.ToInt32(Data, 0);
        int rootSize = BitConverter.ToInt32(Data, 4);
        if (rootId != RW_TEXTURE_DICTIONARY)
            throw new InvalidDataException("PS2: not a TXD root.");

        int rootStart = 12;
        int rootEnd = rootStart + rootSize;
        int index = 0;

        foreach (var c in IterChunks(Data, rootStart, rootEnd))
        {
            if (c.Id != RW_TEXTURE_NATIVE) continue;
            string name = "";
            int blobOffset = -1;
            int blobSize = 0;

            foreach (var cc in IterChunks(Data, c.BodyStart, c.BodyEnd))
            {
                if (cc.Id == RW_STRING && string.IsNullOrEmpty(name))
                {
                    int len = cc.BodyEnd - cc.BodyStart;
                    int nullTerm = Array.IndexOf<byte>(Data, 0, cc.BodyStart, len);
                    int strLen = (nullTerm >= 0 ? nullTerm : cc.BodyEnd) - cc.BodyStart;
                    name = Encoding.ASCII.GetString(Data, cc.BodyStart, strLen);
                }
                else if (cc.Id == RW_STRUCT && cc.Size > 64 && blobOffset < 0)
                {
                    // Skip the small "PS2\0" platform marker struct that comes first.
                    if (Data.Length >= cc.BodyStart + 4 &&
                        Data[cc.BodyStart] == (byte)'P' && Data[cc.BodyStart + 1] == (byte)'S' &&
                        Data[cc.BodyStart + 2] == (byte)'2' && Data[cc.BodyStart + 3] == 0)
                        continue;
                    blobOffset = cc.BodyStart;
                    blobSize = cc.Size;
                }
            }

            if (blobOffset < 0) { index++; continue; }

            int width = BitConverter.ToInt32(Data, blobOffset + 0x0C);
            int height = BitConverter.ToInt32(Data, blobOffset + 0x10);
            int bpp = BitConverter.ToInt32(Data, blobOffset + 0x14);
            int imagePacketSize = BitConverter.ToInt32(Data, blobOffset + 0x3C);
            int palettePacketSize = BitConverter.ToInt32(Data, blobOffset + 0x40);

            int imageOffset = blobOffset + 0xA8;
            int imageSize = bpp switch
            {
                4 => (width * height + 1) / 2,
                8 => width * height,
                _ => width * height * (bpp / 8)
            };
            int paletteOffset = palettePacketSize > 0 ? blobOffset + 0x50 + imagePacketSize + 0x58 : -1;
            int paletteSize = bpp switch
            {
                4 when palettePacketSize > 0 => 16 * 4,
                8 when palettePacketSize >= 0x450 => 256 * 4,
                8 when palettePacketSize > 0 => 256 * 2,
                _ => 0
            };

            DicPixelFormat fmt = bpp switch
            {
                4 => DicPixelFormat.PS2_4bpp_swizzled,
                8 => DicPixelFormat.PS2_8bpp_swizzled,
                16 => DicPixelFormat.PS2_RGB5551,
                32 => DicPixelFormat.PS2_RGBA8888,
                _ => throw new NotSupportedException($"PS2 bpp {bpp} not supported")
            };
            string label = bpp switch
            {
                4 => $"PS2 PAL4 ({(paletteSize >= 64 ? "RGBA8888 pal" : "RGB5551 pal")})",
                8 => $"PS2 PAL8 ({(paletteSize >= 1024 ? "RGBA8888 pal" : "RGB5551 pal")})",
                16 => "PS2 RGB5551",
                32 => "PS2 RGBA8888",
                _ => $"PS2 {bpp}bpp"
            };

            Textures.Add(new DicTexture
            {
                Index = index,
                Name = string.IsNullOrEmpty(name) ? $"texture_{index:D3}" : name,
                Width = width,
                Height = height,
                Bpp = bpp,
                Format = fmt,
                FormatLabel = label,
                ImageOffset = imageOffset,
                ImageSize = imageSize,
                PaletteOffset = bpp == 4 || bpp == 8 ? paletteOffset : -1,
                PaletteSize = bpp == 4 || bpp == 8 ? paletteSize : 0,
                Platform = "PS2",
                Owner = this
            });
            index++;
        }
    }

    // ==================================================================
    // PSP parser - Obscure 2 custom indexed texture dictionary
    // ==================================================================
    private void ParsePsp()
    {
        int count = BitConverter.ToInt32(Data, 0);
        int offset = 4;

        for (int i = 0; i < count; i++)
        {
            if (offset + 4 > Data.Length)
                throw new InvalidDataException($"PSP: unexpected end before entry {i}.");

            int nameLen = BitConverter.ToInt32(Data, offset);
            offset += 4;
            if (nameLen < 1 || nameLen > 256 || offset + nameLen + 16 > Data.Length)
                throw new InvalidDataException($"PSP: invalid name length at entry {i}.");

            string name = Encoding.ASCII.GetString(Data, offset, nameLen);
            offset += nameLen;

            int width = BitConverter.ToUInt16(Data, offset);
            int height = BitConverter.ToUInt16(Data, offset + 2);
            int paletteEntries = BitConverter.ToUInt16(Data, offset + 4);
            int bpp = Data[offset + 6];
            int paletteSize = BitConverter.ToInt32(Data, offset + 12);

            int paletteOffset;
            int imageOffset;
            int imageSize;
            if (bpp == 32)
            {
                if (paletteEntries != 0 || paletteSize != 0)
                    throw new NotSupportedException($"PSP entry {name}: 32bpp texture has unexpected palette metadata.");
                paletteOffset = -1;
                imageOffset = offset + 20;
                imageSize = width * height * 4;
            }
            else
            {
                if ((bpp != 4 && bpp != 8) || paletteEntries != (1 << bpp) || paletteSize != paletteEntries * 4)
                    throw new NotSupportedException($"PSP entry {name}: bpp {bpp}, palette entries {paletteEntries}, palette size {paletteSize} not supported");
                paletteOffset = offset + 16;
                imageOffset = paletteOffset + paletteSize + 4;
                imageSize = (width * height * bpp + 7) / 8;
            }
            if (imageOffset + imageSize > Data.Length)
                throw new InvalidDataException($"PSP: texture {name} data exceeds file size.");

            DicPixelFormat fmt = bpp switch
            {
                4 => DicPixelFormat.PSP_4bpp_swizzled,
                8 => DicPixelFormat.PSP_8bpp_swizzled,
                32 => DicPixelFormat.PSP_RGBA8888,
                _ => throw new NotSupportedException($"PSP bpp {bpp} not supported")
            };

            Textures.Add(new DicTexture
            {
                Index = i,
                Name = name,
                Width = width,
                Height = height,
                Bpp = bpp,
                Format = fmt,
                FormatLabel = bpp == 32 ? "PSP RGBA8888" : $"PSP PAL{bpp} (RGBA8888 pal)",
                ImageOffset = imageOffset,
                ImageSize = imageSize,
                PaletteOffset = paletteOffset,
                PaletteSize = bpp == 32 ? 0 : paletteSize,
                Platform = "PSP",
                Owner = this
            });

            // PSP entries carry a 4-byte pad between the palette and indexed
            // payload. Reinsert only replaces ImageSize, so the pad remains
            // intact and the next entry stays aligned.
            offset = imageOffset + imageSize;
        }
    }

    // ==================================================================
    // Wii parser — custom GX format from Obscure 2
    // ==================================================================
    private void ParseWii()
    {
        // u32 BE count, then 3 padding bytes, then entries
        int count = (Data[0] << 24) | (Data[1] << 16) | (Data[2] << 8) | Data[3];
        int offset = 7;

        for (int i = 0; i < count; i++)
        {
            if (offset >= Data.Length) break;
            int nameLen = Data[offset];
            string name = Encoding.ASCII.GetString(Data, offset + 1, nameLen);
            int p = offset + 1 + nameLen;

            int width = ReadBeU32(Data, p);
            int height = ReadBeU32(Data, p + 4);
            int gxFormat = ReadBeU32(Data, p + 16);
            int dataSize = ReadBeU32(Data, p + 24);
            int dataOffset = p + 28;
            int minNext = dataOffset + dataSize;

            // Find next plausible entry boundary (palette for C4/C8 lives between).
            int nextOffset;
            if (i == count - 1)
            {
                nextOffset = Data.Length;
            }
            else
            {
                nextOffset = -1;
                for (int cand = minNext; cand < Math.Min(minNext + 4096, Data.Length); cand++)
                {
                    if (IsPlausibleWiiEntry(Data, cand)) { nextOffset = cand; break; }
                }
                if (nextOffset < 0) throw new InvalidDataException($"Wii: cannot locate next entry after {name}");
            }

            (DicPixelFormat fmt, int bpp, string label, int paletteFmt) = gxFormat switch
            {
                1 => (DicPixelFormat.Wii_I8, 8, "Wii I8", 0),
                3 => (DicPixelFormat.Wii_IA8, 16, "Wii IA8", 0),
                5 => (DicPixelFormat.Wii_RGB5A3, 16, "Wii RGB5A3", 0),
                6 => (DicPixelFormat.Wii_RGBA8, 32, "Wii RGBA8", 0),
                8 => (DicPixelFormat.Wii_C4, 4, "Wii C4 (TLUT)", 2),
                9 => (DicPixelFormat.Wii_C8, 8, "Wii C8 (TLUT)", 2),
                14 => (DicPixelFormat.Wii_CMPR, 4, "Wii CMPR", 0),
                _ => throw new NotSupportedException($"Wii GX format {gxFormat} not supported")
            };

            int paletteOffset = (gxFormat == 8 || gxFormat == 9) ? minNext : -1;
            int paletteSize = paletteOffset >= 0 ? Math.Max(0, nextOffset - minNext) : 0;

            Textures.Add(new DicTexture
            {
                Index = i,
                Name = name,
                Width = width,
                Height = height,
                Bpp = bpp,
                Format = fmt,
                FormatLabel = label,
                ImageOffset = dataOffset,
                ImageSize = dataSize,
                PaletteOffset = paletteOffset,
                PaletteSize = paletteSize,
                PaletteFormat = paletteFmt,
                Platform = "Wii",
                Owner = this
            });

            offset = nextOffset;
        }
    }

    private static bool IsPlausibleWiiEntry(byte[] data, int offset)
    {
        if (offset >= data.Length) return false;
        int nameLen = data[offset];
        if (nameLen < 1 || nameLen > 48) return false;
        if (offset + 1 + nameLen > data.Length) return false;
        for (int i = 0; i < nameLen; i++)
        {
            byte c = data[offset + 1 + i];
            if (c < 32 || c >= 127) return false;
        }
        int p = offset + 1 + nameLen;
        if (p + 28 > data.Length) return false;
        int width = ReadBeU32(data, p);
        int height = ReadBeU32(data, p + 4);
        int gxFormat = ReadBeU32(data, p + 16);
        int dataSize = ReadBeU32(data, p + 24);
        if (!IsPow2_4to1024(width) || !IsPow2_4to1024(height)) return false;
        if (gxFormat != 1 && gxFormat != 3 && gxFormat != 5 && gxFormat != 6 && gxFormat != 8 && gxFormat != 9 && gxFormat != 14) return false;
        if (dataSize <= 0 || dataSize > data.Length) return false;
        if (p + 28 + dataSize > data.Length) return false;
        return true;
    }

    private static bool IsPow2_4to1024(int v) =>
        v == 4 || v == 8 || v == 16 || v == 32 || v == 64 || v == 128 || v == 256 || v == 512 || v == 1024;

    private static bool IsPow2_4to2048(int v) =>
        v == 4 || v == 8 || v == 16 || v == 32 || v == 64 || v == 128 || v == 256 || v == 512 || v == 1024 || v == 2048;

    private static bool IsPrintableAsciiName(byte[] data, int offset, int length)
    {
        if (offset < 0 || length < 1 || offset + length > data.Length) return false;
        for (int i = 0; i < length; i++)
        {
            byte c = data[offset + i];
            if (c < 32 || c >= 127) return false;
        }
        return true;
    }

    // ==================================================================
    // PC parser — TexDict.py
    // ==================================================================
    private void ParsePc()
    {
        int count = ReadBeU32(Data, 0);
        int offset = 4;
        for (int i = 0; i < count; i++)
        {
            offset += 4; // skip 4 bytes
            int nameLen = ReadBeU32(Data, offset); offset += 4;
            string name = DecodePcTextureName(Data, offset, nameLen);
            offset += nameLen;
            int mipmaps = ReadBeU32(Data, offset); offset += 4;
            int alphaFlag = ReadBeU32(Data, offset); offset += 4;
            int oneBitAlphaFlag = ReadBeU32(Data, offset); offset += 4;
            int width = ReadBeU32(Data, offset); offset += 4;
            int height = ReadBeU32(Data, offset); offset += 4;
            int format = ReadBeU32(Data, offset); offset += 4;

            (DicPixelFormat fmt, int bpp, string label) = format switch
            {
                21 => (DicPixelFormat.PC_R8G8B8A8, 32, "PC R8G8B8A8"),
                23 => (DicPixelFormat.PC_R5G6B5, 16, "PC R5G6B5"),
                25 => (DicPixelFormat.PC_R5G5B5A1, 16, "PC R5G5B5A1"),
                _ => throw new NotSupportedException($"PC format {format} not supported")
            };

            int firstMipOffset = -1;
            int firstMipSize = 0;
            var mips = new List<byte[]>();
            for (int m = 0; m < mipmaps; m++)
            {
                int mipSize = ReadBeU32(Data, offset); offset += 4;
                if (m == 0) { firstMipOffset = offset; firstMipSize = mipSize; }
                byte[] mip = new byte[mipSize];
                Buffer.BlockCopy(Data, offset, mip, 0, mipSize);
                mips.Add(mip);
                offset += mipSize;
            }

            Textures.Add(new DicTexture
            {
                Index = i,
                Name = name,
                Width = width,
                Height = height,
                Bpp = bpp,
                Format = fmt,
                FormatLabel = label,
                ImageOffset = firstMipOffset,
                ImageSize = firstMipSize,
                PaletteOffset = -1,
                PaletteSize = 0,
                AlphaFlag = alphaFlag != 0,
                OneBitAlphaFlag = oneBitAlphaFlag != 0,
                MipmapsCount = mipmaps,
                Mipmaps = mips,
                Platform = "PC",
                Owner = this
            });
        }
    }

    // ==================================================================
    // PC .dip parser — ObsCure 1 PC, little-endian, 4-byte prefix skip
    // (per fmt_dip.py Noesis script)
    // ==================================================================
    private void ParseDip()
    {
        int offset = 4; // skip leading u32 (always zero in samples)
        int count = BitConverter.ToInt32(Data, offset); offset += 4;
        for (int i = 0; i < count; i++)
        {
            offset += 4; // skip 4 bytes
            int nameLen = BitConverter.ToInt32(Data, offset); offset += 4;
            string name = Encoding.ASCII.GetString(Data, offset, nameLen);
            offset += nameLen;
            int mipmaps = BitConverter.ToInt32(Data, offset); offset += 4;
            int alphaFlag = BitConverter.ToInt32(Data, offset); offset += 4;
            int oneBitAlphaFlag = BitConverter.ToInt32(Data, offset); offset += 4;
            int width = BitConverter.ToInt32(Data, offset); offset += 4;
            int height = BitConverter.ToInt32(Data, offset); offset += 4;
            int format = BitConverter.ToInt32(Data, offset); offset += 4;

            (DicPixelFormat fmt, int bpp, string label) = format switch
            {
                21 => (DicPixelFormat.Dip_B8G8R8A8, 32, "PC B8G8R8A8"),
                23 => (DicPixelFormat.Dip_B5G6R5, 16, "PC B5G6R5"),
                25 => (DicPixelFormat.Dip_B5G5R5A1, 16, "PC B5G5R5A1"),
                _ => throw new NotSupportedException($"DIP format {format} not supported")
            };

            int firstMipOffset = -1;
            int firstMipSize = 0;
            var mips = new List<byte[]>();
            for (int m = 0; m < mipmaps; m++)
            {
                int mipSize = BitConverter.ToInt32(Data, offset); offset += 4;
                if (m == 0) { firstMipOffset = offset; firstMipSize = mipSize; }
                byte[] mip = new byte[mipSize];
                Buffer.BlockCopy(Data, offset, mip, 0, mipSize);
                mips.Add(mip);
                offset += mipSize;
            }

            Textures.Add(new DicTexture
            {
                Index = i,
                Name = name,
                Width = width,
                Height = height,
                Bpp = bpp,
                Format = fmt,
                FormatLabel = label,
                ImageOffset = firstMipOffset,
                ImageSize = firstMipSize,
                PaletteOffset = -1,
                PaletteSize = 0,
                AlphaFlag = alphaFlag != 0,
                OneBitAlphaFlag = oneBitAlphaFlag != 0,
                MipmapsCount = mipmaps,
                Mipmaps = mips,
                Platform = "PC (DIP)",
                Owner = this
            });
        }
    }

    // ==================================================================
    // Reinsert helpers
    // ==================================================================
    public void ReplaceImageBytes(DicTexture tex, byte[] newImage)
    {
        if (newImage.Length != tex.ImageSize)
            throw new InvalidOperationException(
                $"Encoded image is {newImage.Length} bytes; container slot expects {tex.ImageSize}.");
        Buffer.BlockCopy(newImage, 0, Data, tex.ImageOffset, newImage.Length);
    }

    public void Save(string path) => File.WriteAllBytes(path, Data);

    // ==================================================================
    // Helpers
    // ==================================================================
    private static int ReadBeU32(byte[] d, int off) =>
        (d[off] << 24) | (d[off + 1] << 16) | (d[off + 2] << 8) | d[off + 3];

    private static string DecodePcTextureName(byte[] data, int offset, int length)
    {
        try
        {
            return Encoding.GetEncoding("Shift_JIS").GetString(data, offset, length);
        }
        catch (ArgumentException)
        {
            // .NET 5+ does not enable legacy code pages by default. Obscure 2
            // PC names seen so far are ASCII-compatible, so this keeps loading
            // working without depending on machine-global encoding providers.
            return Encoding.Latin1.GetString(data, offset, length);
        }
    }

    private struct Chunk { public int Offset; public int Id; public int Size; public int Version; public int BodyStart; public int BodyEnd; }
    private static IEnumerable<Chunk> IterChunks(byte[] data, int start, int end)
    {
        int off = start;
        while (off + 12 <= end)
        {
            int id = BitConverter.ToInt32(data, off);
            int size = BitConverter.ToInt32(data, off + 4);
            int ver = BitConverter.ToInt32(data, off + 8);
            int bodyStart = off + 12;
            int bodyEnd = bodyStart + size;
            if (bodyEnd > end) yield break;
            yield return new Chunk { Offset = off, Id = id, Size = size, Version = ver, BodyStart = bodyStart, BodyEnd = bodyEnd };
            off = bodyEnd;
        }
    }
}

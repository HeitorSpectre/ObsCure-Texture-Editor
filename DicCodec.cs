using System;

namespace HVTTool;

// Decoders for textures stored inside .dic files (PC, PS2 RenderWare, Wii GX).
// Ported from "Obscure 1 DIC Texture Extractor (PS2/Wii) Versão 1.1.py" and
// "TexDict.py" reference scripts.
public static class DicCodec
{
    // ------------------------------------------------------------------
    // Public entry — return BGRA32 buffer of size width*height*4
    // ------------------------------------------------------------------
    public static byte[] DecodeToBgra(DicTexture t)
    {
        byte[] data = t.Owner.Data;
        byte[] raw = new byte[t.ImageSize];
        Buffer.BlockCopy(data, t.ImageOffset, raw, 0, t.ImageSize);

        return t.Format switch
        {
            DicPixelFormat.PC_R8G8B8A8 => DecodePc_R8G8B8A8(raw, t.Width, t.Height),
            DicPixelFormat.PC_R5G6B5 => DecodePc_R5G6B5(raw, t.Width, t.Height),
            DicPixelFormat.PC_R5G5B5A1 => DecodePc_R5G5B5A1(raw, t.Width, t.Height, t.AlphaFlag || t.OneBitAlphaFlag),
            DicPixelFormat.Dip_B8G8R8A8 => DecodeDip_B8G8R8A8(raw, t.Width, t.Height),
            DicPixelFormat.Dip_B5G6R5 => DecodeDip_B5G6R5(raw, t.Width, t.Height),
            DicPixelFormat.Dip_B5G5R5A1 => DecodeDip_B5G5R5A1(raw, t.Width, t.Height, t.AlphaFlag || t.OneBitAlphaFlag),
            DicPixelFormat.PS2_4bpp_swizzled => DecodePs2_Pal4(raw, GetPalette(t), t.Width, t.Height),
            DicPixelFormat.PS2_8bpp_swizzled => DecodePs2_Pal8(raw, GetPalette(t), t.Width, t.Height),
            DicPixelFormat.PS2_RGB5551 => DecodePs2_RGB5551(raw, t.Width, t.Height),
            DicPixelFormat.PS2_RGBA8888 => DecodePs2_RGBA8888(raw, t.Width, t.Height),
            DicPixelFormat.PSP_4bpp_swizzled => DecodePsp_Pal4(raw, GetPalette(t), t.Width, t.Height),
            DicPixelFormat.PSP_8bpp_swizzled => DecodePsp_Pal8(raw, GetPalette(t), t.Width, t.Height),
            DicPixelFormat.PSP_RGBA8888 => DecodePsp_Rgba8888(raw, t.Width, t.Height),
            DicPixelFormat.Wii_I8 => DecodeWii_I8(raw, t.Width, t.Height),
            DicPixelFormat.Wii_IA8 => DecodeWii_IA8(raw, t.Width, t.Height),
            DicPixelFormat.Wii_RGB5A3 => DecodeWii_RGB5A3(raw, t.Width, t.Height),
            DicPixelFormat.Wii_RGBA8 => DecodeWii_RGBA8(raw, t.Width, t.Height),
            DicPixelFormat.Wii_C4 => DecodeWii_C4(raw, GetWiiTlut(t), t.Width, t.Height),
            DicPixelFormat.Wii_C8 => DecodeWii_C8(raw, GetWiiTlut(t), t.Width, t.Height),
            DicPixelFormat.Wii_CMPR => DecodeWii_CMPR(raw, t.Width, t.Height),
            _ => throw new NotSupportedException($"Format {t.Format} not supported")
        };
    }

    public static byte[] EncodeFromRgba(DicTexture t, byte[] rgba)
    {
        return t.Format switch
        {
            DicPixelFormat.PC_R8G8B8A8 => EncodePc_R8G8B8A8(rgba, t.Width, t.Height),
            DicPixelFormat.PC_R5G6B5 => EncodePc_R5G6B5(rgba, t.Width, t.Height),
            DicPixelFormat.PC_R5G5B5A1 => EncodePc_R5G5B5A1(rgba, t.Width, t.Height),
            DicPixelFormat.Dip_B8G8R8A8 => EncodeDip_B8G8R8A8(rgba, t.Width, t.Height),
            DicPixelFormat.Dip_B5G6R5 => EncodeDip_B5G6R5(rgba, t.Width, t.Height),
            DicPixelFormat.Dip_B5G5R5A1 => EncodeDip_B5G5R5A1(rgba, t.Width, t.Height),
            DicPixelFormat.PS2_4bpp_swizzled => EncodePs2_Pal4(t, rgba),
            DicPixelFormat.PS2_8bpp_swizzled => EncodePs2_Pal8(t, rgba),
            DicPixelFormat.PS2_RGB5551 => EncodePs2_RGB5551(rgba, t.Width, t.Height),
            DicPixelFormat.PS2_RGBA8888 => EncodePs2_RGBA8888(rgba, t.Width, t.Height),
            DicPixelFormat.PSP_4bpp_swizzled => EncodePsp_Pal4(rgba, GetPalette(t), t.Width, t.Height),
            DicPixelFormat.PSP_8bpp_swizzled => EncodePsp_Pal8(rgba, GetPalette(t), t.Width, t.Height),
            DicPixelFormat.PSP_RGBA8888 => EncodePsp_Rgba8888(rgba, t.Width, t.Height),
            DicPixelFormat.Wii_I8 => EncodeWii_I8(rgba, t.Width, t.Height),
            DicPixelFormat.Wii_IA8 => EncodeWii_IA8(rgba, t.Width, t.Height),
            DicPixelFormat.Wii_RGB5A3 => EncodeWii_RGB5A3(t, rgba),
            DicPixelFormat.Wii_RGBA8 => EncodeWii_RGBA8(rgba, t.Width, t.Height),
            DicPixelFormat.Wii_C4 => EncodeWii_C4(t, rgba),
            DicPixelFormat.Wii_C8 => EncodeWii_C8(t, rgba),
            DicPixelFormat.Wii_CMPR => EncodeWii_CMPR(t, rgba),
            _ => throw new NotSupportedException($"Format {t.Format} not supported")
        };
    }

    // ==================================================================
    // PC formats (linear, big-endian per ReadBeU16 etc.)
    // ==================================================================
    private static byte[] DecodePc_R8G8B8A8(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int n = w * h;
        for (int i = 0; i < n; i++)
        {
            // R8G8B8A8 stored as RGBA bytes in file
            byte r = raw[i * 4 + 0], g = raw[i * 4 + 1], b = raw[i * 4 + 2], a = raw[i * 4 + 3];
            o[i * 4 + 0] = b; o[i * 4 + 1] = g; o[i * 4 + 2] = r; o[i * 4 + 3] = a;
        }
        return o;
    }
    private static byte[] EncodePc_R8G8B8A8(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            o[i * 4 + 0] = rgba[i * 4 + 0];
            o[i * 4 + 1] = rgba[i * 4 + 1];
            o[i * 4 + 2] = rgba[i * 4 + 2];
            o[i * 4 + 3] = rgba[i * 4 + 3];
        }
        return o;
    }

    private static byte[] DecodePc_R5G6B5(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int v = raw[i * 2] | (raw[i * 2 + 1] << 8); // little-endian on PC
            int r = ((v >> 11) & 0x1F) * 255 / 31;
            int g = ((v >> 5) & 0x3F) * 255 / 63;
            int b = (v & 0x1F) * 255 / 31;
            o[i * 4 + 0] = (byte)b; o[i * 4 + 1] = (byte)g; o[i * 4 + 2] = (byte)r; o[i * 4 + 3] = 0xFF;
        }
        return o;
    }
    private static byte[] EncodePc_R5G6B5(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++)
        {
            int r5 = (rgba[i * 4 + 0] * 31 + 127) / 255;
            int g6 = (rgba[i * 4 + 1] * 63 + 127) / 255;
            int b5 = (rgba[i * 4 + 2] * 31 + 127) / 255;
            int v = (r5 << 11) | (g6 << 5) | b5;
            o[i * 2] = (byte)(v & 0xFF); o[i * 2 + 1] = (byte)(v >> 8);
        }
        return o;
    }

    private static byte[] DecodePc_R5G5B5A1(byte[] raw, int w, int h, bool useAlpha)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int v = raw[i * 2] | (raw[i * 2 + 1] << 8);
            int r = ((v >> 10) & 0x1F) * 255 / 31;
            int g = ((v >> 5) & 0x1F) * 255 / 31;
            int b = (v & 0x1F) * 255 / 31;
            int a = useAlpha ? ((v & 0x8000) != 0 ? 255 : 0) : 255;
            o[i * 4 + 0] = (byte)b; o[i * 4 + 1] = (byte)g; o[i * 4 + 2] = (byte)r; o[i * 4 + 3] = (byte)a;
        }
        return o;
    }
    private static byte[] EncodePc_R5G5B5A1(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++)
        {
            int r5 = (rgba[i * 4 + 0] * 31 + 127) / 255;
            int g5 = (rgba[i * 4 + 1] * 31 + 127) / 255;
            int b5 = (rgba[i * 4 + 2] * 31 + 127) / 255;
            int a1 = rgba[i * 4 + 3] >= 128 ? 1 : 0;
            int v = (a1 << 15) | (r5 << 10) | (g5 << 5) | b5;
            o[i * 2] = (byte)(v & 0xFF); o[i * 2 + 1] = (byte)(v >> 8);
        }
        return o;
    }

    // ==================================================================
    // PC .dip formats — channels stored as B,G,R,A (Noesis fmt_dip.py)
    // ==================================================================
    private static byte[] DecodeDip_B8G8R8A8(byte[] raw, int w, int h)
    {
        // File: B,G,R,A → Bitmap BGRA: B,G,R,A. Direct copy.
        byte[] o = new byte[w * h * 4];
        Buffer.BlockCopy(raw, 0, o, 0, Math.Min(raw.Length, o.Length));
        return o;
    }
    private static byte[] EncodeDip_B8G8R8A8(byte[] rgba, int w, int h)
    {
        // Caller's RGBA → file BGRA
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            o[i * 4 + 0] = rgba[i * 4 + 2]; // B
            o[i * 4 + 1] = rgba[i * 4 + 1]; // G
            o[i * 4 + 2] = rgba[i * 4 + 0]; // R
            o[i * 4 + 3] = rgba[i * 4 + 3]; // A
        }
        return o;
    }

    // Format 23 in .dip is actually D3DFMT_R5G6B5 (DirectX standard):
    //   16-bit LE value with bits 15-11=R, 10-5=G, 4-0=B
    // The "b5 g6 r5" string in fmt_dip.py is Noesis bit-stream notation, not
    // the storage layout — empirically the bark/wood textures decode brown
    // only with R-in-high-bits.
    private static byte[] DecodeDip_B5G6R5(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int v = raw[i * 2] | (raw[i * 2 + 1] << 8);
            int r = ((v >> 11) & 0x1F) * 255 / 31;
            int g = ((v >> 5) & 0x3F) * 255 / 63;
            int b = (v & 0x1F) * 255 / 31;
            o[i * 4 + 0] = (byte)b; o[i * 4 + 1] = (byte)g; o[i * 4 + 2] = (byte)r; o[i * 4 + 3] = 0xFF;
        }
        return o;
    }
    private static byte[] EncodeDip_B5G6R5(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++)
        {
            int r5 = (rgba[i * 4 + 0] * 31 + 127) / 255;
            int g6 = (rgba[i * 4 + 1] * 63 + 127) / 255;
            int b5 = (rgba[i * 4 + 2] * 31 + 127) / 255;
            int v = (r5 << 11) | (g6 << 5) | b5;
            o[i * 2] = (byte)(v & 0xFF); o[i * 2 + 1] = (byte)(v >> 8);
        }
        return o;
    }

    // Format 25 in .dip is D3DFMT_A1R5G5B5:
    //   16-bit LE value with bits 15=A, 14-10=R, 9-5=G, 4-0=B
    private static byte[] DecodeDip_B5G5R5A1(byte[] raw, int w, int h, bool useAlpha)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int v = raw[i * 2] | (raw[i * 2 + 1] << 8);
            int r = ((v >> 10) & 0x1F) * 255 / 31;
            int g = ((v >> 5) & 0x1F) * 255 / 31;
            int b = (v & 0x1F) * 255 / 31;
            int a = useAlpha ? ((v & 0x8000) != 0 ? 255 : 0) : 255;
            o[i * 4 + 0] = (byte)b; o[i * 4 + 1] = (byte)g; o[i * 4 + 2] = (byte)r; o[i * 4 + 3] = (byte)a;
        }
        return o;
    }
    private static byte[] EncodeDip_B5G5R5A1(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++)
        {
            int r5 = (rgba[i * 4 + 0] * 31 + 127) / 255;
            int g5 = (rgba[i * 4 + 1] * 31 + 127) / 255;
            int b5 = (rgba[i * 4 + 2] * 31 + 127) / 255;
            int a1 = rgba[i * 4 + 3] >= 128 ? 1 : 0;
            int v = (a1 << 15) | (r5 << 10) | (g5 << 5) | b5;
            o[i * 2] = (byte)(v & 0xFF); o[i * 2 + 1] = (byte)(v >> 8);
        }
        return o;
    }

    // ==================================================================
    // PS2 formats
    // ==================================================================
    private static byte[] GetPalette(DicTexture t)
    {
        if (t.PaletteOffset < 0 || t.PaletteSize <= 0) return Array.Empty<byte>();
        byte[] p = new byte[t.PaletteSize];
        Buffer.BlockCopy(t.Owner.Data, t.PaletteOffset, p, 0, t.PaletteSize);
        return p;
    }

    private static int Ps2SwizzleId8(int x, int y, int w)
    {
        int blockLocation = (y & ~0xF) * w + (x & ~0xF) * 2;
        int swapSelector = (((y + 2) >> 2) & 0x1) * 4;
        int posY = (((y & ~3) >> 1) + (y & 1)) & 0x7;
        int columnLocation = posY * w * 2 + ((x + swapSelector) & 0x7) * 4;
        int byteNum = ((y >> 1) & 1) + ((x >> 2) & 2);
        return blockLocation + columnLocation + byteNum;
    }

    private static int RemapClutIndex(int i) => (i & 0xE7) | ((i & 0x08) << 1) | ((i & 0x10) >> 1);

    private static (byte r, byte g, byte b, byte a)[] DecodePs2Palette(byte[] pal, int paletteSize, int colorCount = 256)
    {
        colorCount = Math.Clamp(colorCount, 1, 256);
        var colors = new (byte r, byte g, byte b, byte a)[256];
        bool isRgba8888 = paletteSize >= colorCount * 4;
        if (isRgba8888)
        {
            int n = Math.Min(colorCount, paletteSize / 4);
            for (int i = 0; i < n; i++)
            {
                byte r = pal[i * 4 + 0], g = pal[i * 4 + 1], b = pal[i * 4 + 2], a = pal[i * 4 + 3];
                colors[i] = (r, g, b, (byte)Math.Min(255, a * 2));
            }
        }
        else // RGB5551 LE
        {
            int n = Math.Min(colorCount, paletteSize / 2);
            for (int i = 0; i < n; i++)
            {
                int v = pal[i * 2] | (pal[i * 2 + 1] << 8);
                int r = (v & 0x1F) * 255 / 31;
                int g = ((v >> 5) & 0x1F) * 255 / 31;
                int b = ((v >> 10) & 0x1F) * 255 / 31;
                int a = (v & 0x8000) != 0 ? 255 : 0;
                colors[i] = ((byte)r, (byte)g, (byte)b, (byte)a);
            }
        }
        if (colorCount == 256)
        {
            // CLUT remap for 8bpp CSM1 palettes. 16-color PS2 palettes are
            // already addressed directly by their low-nibble indices.
            var fixedPal = new (byte r, byte g, byte b, byte a)[256];
            for (int i = 0; i < 256; i++) fixedPal[RemapClutIndex(i)] = colors[i];
            return fixedPal;
        }
        return colors;
    }

    private static byte[] Unpack4Bpp(byte[] raw, int pixelCount)
    {
        byte[] indices = new byte[pixelCount];
        int di = 0;
        for (int i = 0; i < raw.Length && di < pixelCount; i++)
        {
            byte v = raw[i];
            indices[di++] = (byte)(v & 0x0F);
            if (di < pixelCount) indices[di++] = (byte)(v >> 4);
        }
        return indices;
    }

    private static byte[] Pack4Bpp(byte[] indices)
    {
        byte[] raw = new byte[(indices.Length + 1) / 2];
        for (int i = 0; i < indices.Length; i += 2)
        {
            int lo = indices[i] & 0x0F;
            int hi = i + 1 < indices.Length ? (indices[i + 1] & 0x0F) : 0;
            raw[i / 2] = (byte)(lo | (hi << 4));
        }
        return raw;
    }

    private static byte[] DecodePs2_Pal4(byte[] raw, byte[] pal, int w, int h)
    {
        byte[] packedIndices = Unpack4Bpp(raw, w * h);
        byte[] indices = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sid = Ps2SwizzleId8(x, y, w);
                if (sid < packedIndices.Length) indices[y * w + x] = packedIndices[sid];
            }

        var palette = DecodePs2Palette(pal, pal.Length, 16);
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            var c = palette[indices[i] & 0x0F];
            o[i * 4 + 0] = c.b; o[i * 4 + 1] = c.g; o[i * 4 + 2] = c.r; o[i * 4 + 3] = c.a;
        }
        return o;
    }

    private static byte[] EncodePs2_Pal4(DicTexture t, byte[] rgba)
    {
        int w = t.Width, h = t.Height;
        byte[] pal = GetPalette(t);
        var palette = DecodePs2Palette(pal, pal.Length, 16);
        byte[] raw = GetImageBytes(t);
        byte[] packedOriginal = Unpack4Bpp(raw, w * h);
        byte[] originalIndices = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sid = Ps2SwizzleId8(x, y, w);
                if (sid < packedOriginal.Length) originalIndices[y * w + x] = packedOriginal[sid];
            }

        byte[] indices = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int original = originalIndices[i] & 0x0F;
            indices[i] = Ps2PaletteColorMatches(rgba, i * 4, palette[original])
                ? (byte)original
                : (byte)NearestPs2PaletteIndex(rgba, i * 4, palette, 16);
        }

        byte[] swizzled = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sid = Ps2SwizzleId8(x, y, w);
                if (sid < swizzled.Length) swizzled[sid] = indices[y * w + x];
            }
        return Pack4Bpp(swizzled);
    }

    private static byte[] DecodePs2_Pal8(byte[] raw, byte[] pal, int w, int h)
    {
        // Unswizzle
        byte[] indices = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sid = Ps2SwizzleId8(x, y, w);
                if (sid < raw.Length) indices[y * w + x] = raw[sid];
            }
        var palette = DecodePs2Palette(pal, pal.Length);
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            var c = palette[indices[i]];
            o[i * 4 + 0] = c.b; o[i * 4 + 1] = c.g; o[i * 4 + 2] = c.r; o[i * 4 + 3] = c.a;
        }
        return o;
    }

    private static byte[] EncodePs2_Pal8(DicTexture t, byte[] rgba)
    {
        int w = t.Width, h = t.Height;
        byte[] pal = GetPalette(t);
        var palette = DecodePs2Palette(pal, pal.Length);
        byte[] raw = GetImageBytes(t);
        byte[] originalIndices = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sid = Ps2SwizzleId8(x, y, w);
                if (sid < raw.Length) originalIndices[y * w + x] = raw[sid];
            }

        byte[] indices = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int original = originalIndices[i];
            indices[i] = Ps2PaletteColorMatches(rgba, i * 4, palette[original])
                ? (byte)original
                : (byte)NearestPs2PaletteIndex(rgba, i * 4, palette, 256);
        }
        // Swizzle indices
        byte[] o = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int sid = Ps2SwizzleId8(x, y, w);
                if (sid < o.Length) o[sid] = indices[y * w + x];
            }
        return o;
    }

    private static byte[] GetImageBytes(DicTexture t)
    {
        byte[] bytes = new byte[t.ImageSize];
        Buffer.BlockCopy(t.Owner.Data, t.ImageOffset, bytes, 0, t.ImageSize);
        return bytes;
    }

    private static bool Ps2PaletteColorMatches(byte[] rgba, int offset, (byte r, byte g, byte b, byte a) c) =>
        rgba[offset] == c.r && rgba[offset + 1] == c.g && rgba[offset + 2] == c.b && rgba[offset + 3] == c.a;

    private static int NearestPs2PaletteIndex(byte[] rgba, int offset, (byte r, byte g, byte b, byte a)[] palette, int count)
    {
        byte r = rgba[offset], g = rgba[offset + 1], b = rgba[offset + 2], a = rgba[offset + 3];
        int best = 0, bestD = int.MaxValue;
        for (int k = 0; k < count; k++)
        {
            var c = palette[k];
            int dr = r - c.r, dg = g - c.g, db = b - c.b, da = a - c.a;
            int d = dr * dr + dg * dg + db * db + da * da * 2;
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    // ==================================================================
    // PSP formats
    // ==================================================================
    private static (byte r, byte g, byte b, byte a)[] DecodePspPalette(byte[] pal, int colorCount)
    {
        colorCount = Math.Clamp(colorCount, 1, 256);
        var colors = new (byte r, byte g, byte b, byte a)[256];
        int n = Math.Min(colorCount, pal.Length / 4);
        for (int i = 0; i < n; i++)
        {
            colors[i] = (pal[i * 4 + 0], pal[i * 4 + 1], pal[i * 4 + 2], pal[i * 4 + 3]);
        }
        return colors;
    }

    private static byte[] DecodePsp_Pal4(byte[] raw, byte[] pal, int w, int h)
    {
        byte[] linearPacked = UnswizzlePsp(raw, w, h, 4);
        byte[] indices = Unpack4Bpp(linearPacked, w * h);
        var palette = DecodePspPalette(pal, 16);
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            var c = palette[indices[i] & 0x0F];
            o[i * 4 + 0] = c.b; o[i * 4 + 1] = c.g; o[i * 4 + 2] = c.r; o[i * 4 + 3] = c.a;
        }
        return o;
    }

    private static byte[] EncodePsp_Pal4(byte[] rgba, byte[] pal, int w, int h)
    {
        var palette = DecodePspPalette(pal, 16);
        byte[] indices = new byte[w * h];
        for (int i = 0; i < w * h; i++)
            indices[i] = (byte)NearestIndex(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3], palette, 16);

        return SwizzlePsp(Pack4Bpp(indices), w, h, 4);
    }

    private static byte[] DecodePsp_Pal8(byte[] raw, byte[] pal, int w, int h)
    {
        byte[] indices = UnswizzlePsp(raw, w, h, 8);
        var palette = DecodePspPalette(pal, 256);
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            var c = palette[indices[i]];
            o[i * 4 + 0] = c.b; o[i * 4 + 1] = c.g; o[i * 4 + 2] = c.r; o[i * 4 + 3] = c.a;
        }
        return o;
    }

    private static byte[] EncodePsp_Pal8(byte[] rgba, byte[] pal, int w, int h)
    {
        var palette = DecodePspPalette(pal, 256);
        byte[] indices = new byte[w * h];
        for (int i = 0; i < w * h; i++)
            indices[i] = (byte)NearestIndex(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3], palette, 256);

        return SwizzlePsp(indices, w, h, 8);
    }

    private static byte[] DecodePsp_Rgba8888(byte[] raw, int w, int h)
    {
        byte[] linear = UnswizzlePsp(raw, w, h, 32);
        byte[] o = new byte[w * h * 4];
        int n = Math.Min(w * h, linear.Length / 4);
        for (int i = 0; i < n; i++)
        {
            o[i * 4 + 0] = linear[i * 4 + 2];
            o[i * 4 + 1] = linear[i * 4 + 1];
            o[i * 4 + 2] = linear[i * 4 + 0];
            o[i * 4 + 3] = linear[i * 4 + 3];
        }
        return o;
    }

    private static byte[] EncodePsp_Rgba8888(byte[] rgba, int w, int h)
    {
        byte[] linear = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            linear[i * 4 + 0] = rgba[i * 4 + 0];
            linear[i * 4 + 1] = rgba[i * 4 + 1];
            linear[i * 4 + 2] = rgba[i * 4 + 2];
            linear[i * 4 + 3] = rgba[i * 4 + 3];
        }
        return SwizzlePsp(linear, w, h, 32);
    }

    private static byte[] UnswizzlePsp(byte[] raw, int w, int h, int bpp)
    {
        int stride = w * bpp / 8;
        int paddedStride = (stride + 15) & ~15;
        int rowBlocks = paddedStride / 16;
        byte[] linear = new byte[stride * h];
        int dst = 0;

        for (int y = 0; y < h; y++)
            for (int x = 0; x < stride; x++)
            {
                int blockX = x / 16;
                int blockY = y / 8;
                int blockIndex = blockX + blockY * rowBlocks;
                int src = blockIndex * 16 * 8 + (x % 16) + (y % 8) * 16;
                if (src < raw.Length) linear[dst] = raw[src];
                dst++;
            }

        return linear;
    }

    private static byte[] SwizzlePsp(byte[] linear, int w, int h, int bpp)
    {
        int stride = w * bpp / 8;
        int paddedStride = (stride + 15) & ~15;
        int paddedHeight = (h + 7) & ~7;
        int rowBlocks = paddedStride / 16;
        byte[] swizzled = new byte[paddedStride * paddedHeight];

        for (int y = 0; y < h; y++)
            for (int x = 0; x < stride; x++)
            {
                int blockX = x / 16;
                int blockY = y / 8;
                int blockIndex = blockX + blockY * rowBlocks;
                int dst = blockIndex * 16 * 8 + (x % 16) + (y % 8) * 16;
                int src = y * stride + x;
                if (src < linear.Length && dst < swizzled.Length) swizzled[dst] = linear[src];
            }

        int expectedSize = stride * h;
        if (swizzled.Length == expectedSize) return swizzled;
        byte[] trimmed = new byte[expectedSize];
        Buffer.BlockCopy(swizzled, 0, trimmed, 0, Math.Min(trimmed.Length, swizzled.Length));
        return trimmed;
    }

    private static byte[] DecodePs2_RGB5551(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            int v = raw[i * 2] | (raw[i * 2 + 1] << 8);
            int r = (v & 0x1F) * 255 / 31;
            int g = ((v >> 5) & 0x1F) * 255 / 31;
            int b = ((v >> 10) & 0x1F) * 255 / 31;
            int a = (v & 0x8000) != 0 ? 255 : 0;
            o[i * 4 + 0] = (byte)b; o[i * 4 + 1] = (byte)g; o[i * 4 + 2] = (byte)r; o[i * 4 + 3] = (byte)a;
        }
        return o;
    }
    private static byte[] EncodePs2_RGB5551(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++)
        {
            int r5 = (rgba[i * 4 + 0] * 31 + 127) / 255;
            int g5 = (rgba[i * 4 + 1] * 31 + 127) / 255;
            int b5 = (rgba[i * 4 + 2] * 31 + 127) / 255;
            int a = rgba[i * 4 + 3] >= 128 ? 0x8000 : 0;
            int v = a | (b5 << 10) | (g5 << 5) | r5;
            o[i * 2] = (byte)(v & 0xFF); o[i * 2 + 1] = (byte)(v >> 8);
        }
        return o;
    }

    private static byte[] DecodePs2_RGBA8888(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            byte r = raw[i * 4 + 0], g = raw[i * 4 + 1], b = raw[i * 4 + 2], a = raw[i * 4 + 3];
            o[i * 4 + 0] = b; o[i * 4 + 1] = g; o[i * 4 + 2] = r;
            o[i * 4 + 3] = (byte)Math.Min(255, a * 2);
        }
        return o;
    }
    private static byte[] EncodePs2_RGBA8888(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            o[i * 4 + 0] = rgba[i * 4 + 0];
            o[i * 4 + 1] = rgba[i * 4 + 1];
            o[i * 4 + 2] = rgba[i * 4 + 2];
            o[i * 4 + 3] = (byte)Math.Min(128, (rgba[i * 4 + 3] + 1) / 2);
        }
        return o;
    }

    // ==================================================================
    // Wii formats (GX tile layouts)
    // ==================================================================
    private static byte[] DecodeWii_I8(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        if (pos >= raw.Length) goto done;
                        int px = bx + x, py = by + y;
                        byte v = raw[pos++];
                        if (px < w && py < h)
                        {
                            int d = (py * w + px) * 4;
                            o[d + 0] = v; o[d + 1] = v; o[d + 2] = v; o[d + 3] = 0xFF;
                        }
                    }
        done:
        return o;
    }
    private static byte[] EncodeWii_I8(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[((w + 7) / 8 * 8) * ((h + 3) / 4 * 4)];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int px = bx + x, py = by + y;
                        byte v = 0;
                        if (px < w && py < h)
                        {
                            int s = (py * w + px) * 4;
                            v = (byte)((rgba[s] + rgba[s + 1] + rgba[s + 2]) / 3);
                        }
                        if (pos < o.Length) o[pos++] = v;
                    }
        Array.Resize(ref o, pos);
        return o;
    }

    private static byte[] DecodeWii_IA8(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        if (pos + 1 >= raw.Length) goto done;
                        int px = bx + x, py = by + y;
                        byte intensity = raw[pos], alpha = raw[pos + 1];
                        pos += 2;
                        if (px < w && py < h)
                        {
                            int d = (py * w + px) * 4;
                            o[d + 0] = intensity; o[d + 1] = intensity; o[d + 2] = intensity; o[d + 3] = alpha;
                        }
                    }
        done:
        return o;
    }
    private static byte[] EncodeWii_IA8(byte[] rgba, int w, int h)
    {
        byte[] o = new byte[((w + 3) / 4 * 4) * ((h + 3) / 4 * 4) * 2];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx + x, py = by + y;
                        byte i = 0, a = 0;
                        if (px < w && py < h)
                        {
                            int s = (py * w + px) * 4;
                            i = (byte)((rgba[s] + rgba[s + 1] + rgba[s + 2]) / 3);
                            a = rgba[s + 3];
                        }
                        if (pos + 1 < o.Length) { o[pos] = i; o[pos + 1] = a; pos += 2; }
                    }
        Array.Resize(ref o, pos);
        return o;
    }

    private static (byte r, byte g, byte b, byte a) GxRgb5A3(int v)
    {
        if ((v & 0x8000) != 0)
        {
            int r = ((v >> 10) & 0x1F) * 255 / 31;
            int g = ((v >> 5) & 0x1F) * 255 / 31;
            int b = (v & 0x1F) * 255 / 31;
            return ((byte)r, (byte)g, (byte)b, 255);
        }
        else
        {
            int a = ((v >> 12) & 0x7) * 255 / 7;
            int r = ((v >> 8) & 0xF) * 255 / 15;
            int g = ((v >> 4) & 0xF) * 255 / 15;
            int b = (v & 0xF) * 255 / 15;
            return ((byte)r, (byte)g, (byte)b, (byte)a);
        }
    }
    private static int GxEncodeRgb5A3(byte r, byte g, byte b, byte a)
    {
        if (a < 0xFF - 8 || (a == 255 && IsExact4Bit(r) && IsExact4Bit(g) && IsExact4Bit(b)))
            return (((a * 7 + 127) / 255) << 12)
                | (((r * 15 + 127) / 255) << 8)
                | (((g * 15 + 127) / 255) << 4)
                | ((b * 15 + 127) / 255);
        return 0x8000
            | (((r * 31 + 127) / 255) << 10)
            | (((g * 31 + 127) / 255) << 5)
            | ((b * 31 + 127) / 255);
    }

    private static bool IsExact4Bit(byte value) => value % 17 == 0;

    private static byte[] DecodeWii_RGB5A3(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        if (pos + 1 >= raw.Length) goto done;
                        int px = bx + x, py = by + y;
                        int v = (raw[pos] << 8) | raw[pos + 1];
                        pos += 2;
                        if (px < w && py < h)
                        {
                            var c = GxRgb5A3(v);
                            int d = (py * w + px) * 4;
                            o[d + 0] = c.b; o[d + 1] = c.g; o[d + 2] = c.r; o[d + 3] = c.a;
                        }
                    }
        done:
        // Output is BGRA — decoders return BGRA directly.
        return o;
    }
    private static byte[] EncodeWii_RGB5A3(DicTexture t, byte[] rgba)
    {
        int w = t.Width, h = t.Height;
        byte[] original = GetImageBytes(t);
        byte[] o = new byte[((w + 3) / 4 * 4) * ((h + 3) / 4 * 4) * 2];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx + x, py = by + y;
                        int srcPos = pos;
                        int source = srcPos + 1 < original.Length ? (original[srcPos] << 8) | original[srcPos + 1] : 0;
                        int v = source;
                        if (px < w && py < h)
                        {
                            int s = (py * w + px) * 4;
                            v = GxEncodeRgb5A3WithTemplate(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3], source);
                        }
                        if (pos + 1 < o.Length) { o[pos] = (byte)(v >> 8); o[pos + 1] = (byte)(v & 0xFF); pos += 2; }
                    }
        Array.Resize(ref o, pos);
        return o;
    }

    private static int GxEncodeRgb5A3WithTemplate(byte r, byte g, byte b, byte a, int original)
    {
        var c = GxRgb5A3(original);
        if (c.r == r && c.g == g && c.b == b && c.a == a)
            return original;
        return GxEncodeRgb5A3(r, g, b, a);
    }

    private static byte[] DecodeWii_RGBA8(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
            {
                if (pos + 64 > raw.Length) return o;
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx + x, py = by + y;
                        if (px >= w || py >= h) continue;
                        int i = y * 4 + x;
                        byte a = raw[pos + i * 2];
                        byte r = raw[pos + i * 2 + 1];
                        byte g = raw[pos + 32 + i * 2];
                        byte b = raw[pos + 32 + i * 2 + 1];
                        int d = (py * w + px) * 4;
                        o[d + 0] = b; o[d + 1] = g; o[d + 2] = r; o[d + 3] = a;
                    }
                pos += 64;
            }
        return o;
    }
    private static byte[] EncodeWii_RGBA8(byte[] rgba, int w, int h)
    {
        int blocksX = (w + 3) / 4, blocksY = (h + 3) / 4;
        byte[] o = new byte[blocksX * blocksY * 64];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 4)
            {
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx + x, py = by + y;
                        byte r = 0, g = 0, b = 0, a = 0;
                        if (px < w && py < h)
                        {
                            int s = (py * w + px) * 4;
                            r = rgba[s]; g = rgba[s + 1]; b = rgba[s + 2]; a = rgba[s + 3];
                        }
                        int i = y * 4 + x;
                        o[pos + i * 2] = a;
                        o[pos + i * 2 + 1] = r;
                        o[pos + 32 + i * 2] = g;
                        o[pos + 32 + i * 2 + 1] = b;
                    }
                pos += 64;
            }
        return o;
    }

    private static (byte r, byte g, byte b, byte a)[] GetWiiTlut(DicTexture t)
    {
        var pal = new (byte r, byte g, byte b, byte a)[256];
        if (t.PaletteOffset < 0 || t.PaletteSize < 12) return pal;
        byte[] d = t.Owner.Data;
        int off = t.PaletteOffset;
        int count = ((d[off] << 24) | (d[off + 1] << 16) | (d[off + 2] << 8) | d[off + 3]);
        int paletteFormat = ((d[off + 4] << 24) | (d[off + 5] << 16) | (d[off + 6] << 8) | d[off + 7]);
        int size = ((d[off + 8] << 24) | (d[off + 9] << 16) | (d[off + 10] << 8) | d[off + 11]);
        int dataStart = off + 12;
        int entries = Math.Min(count, 256);
        for (int i = 0; i < entries; i++)
        {
            int p = dataStart + i * 2;
            if (p + 1 >= d.Length) break;
            int v = (d[p] << 8) | d[p + 1];
            if (paletteFormat == 0) // IA8
            {
                byte intensity = (byte)(v & 0xFF);
                byte alpha = (byte)((v >> 8) & 0xFF);
                pal[i] = (intensity, intensity, intensity, alpha);
            }
            else if (paletteFormat == 1) // RGB565
            {
                int r = ((v >> 11) & 0x1F) * 255 / 31;
                int g = ((v >> 5) & 0x3F) * 255 / 63;
                int b = (v & 0x1F) * 255 / 31;
                pal[i] = ((byte)r, (byte)g, (byte)b, 255);
            }
            else // RGB5A3
            {
                pal[i] = GxRgb5A3(v);
            }
        }
        return pal;
    }

    private static byte[] DecodeWii_C4(byte[] raw, (byte r, byte g, byte b, byte a)[] pal, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int pos = 0;
        for (int by = 0; by < h; by += 8)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 8; y++)
                    for (int xpair = 0; xpair < 4; xpair++)
                    {
                        if (pos >= raw.Length) goto done;
                        byte v = raw[pos++];
                        for (int dx = 0; dx < 2; dx++)
                        {
                            int idx = dx == 0 ? (v >> 4) : (v & 0x0F);
                            int px = bx + xpair * 2 + dx, py = by + y;
                            if (px < w && py < h)
                            {
                                var c = pal[idx];
                                int d = (py * w + px) * 4;
                                o[d + 0] = c.b; o[d + 1] = c.g; o[d + 2] = c.r; o[d + 3] = c.a;
                            }
                        }
                    }
        done:
        return o;
    }
    private static byte[] EncodeWii_C4(DicTexture t, byte[] rgba)
    {
        int w = t.Width, h = t.Height;
        var pal = GetWiiTlut(t);
        byte[] originalIndices = DecodeWii_C4_Indices(GetImageBytes(t), w, h);

        // Re-quantize against existing palette.
        byte[] indices = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int original = originalIndices[i] & 0x0F;
            indices[i] = PaletteColorMatches(rgba, i * 4, pal[original])
                ? (byte)original
                : (byte)NearestIndex(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3], pal, 16);
        }

        int blocksX = (w + 7) / 8, blocksY = (h + 7) / 8;
        byte[] o = new byte[blocksX * blocksY * 32];
        int pos = 0;
        for (int by = 0; by < h; by += 8)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 8; y++)
                    for (int xpair = 0; xpair < 4; xpair++)
                    {
                        int px0 = bx + xpair * 2, px1 = px0 + 1, py = by + y;
                        int a = (px0 < w && py < h) ? indices[py * w + px0] : 0;
                        int b = (px1 < w && py < h) ? indices[py * w + px1] : 0;
                        if (pos < o.Length) o[pos++] = (byte)(((a & 0x0F) << 4) | (b & 0x0F));
                    }
        Array.Resize(ref o, pos);
        return o;
    }

    private static byte[] DecodeWii_C8(byte[] raw, (byte r, byte g, byte b, byte a)[] pal, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        if (pos >= raw.Length) goto done;
                        int px = bx + x, py = by + y;
                        if (px < w && py < h)
                        {
                            var c = pal[raw[pos]];
                            int d = (py * w + px) * 4;
                            o[d + 0] = c.b; o[d + 1] = c.g; o[d + 2] = c.r; o[d + 3] = c.a;
                        }
                        pos++;
                    }
        done:
        return o;
    }
    private static byte[] EncodeWii_C8(DicTexture t, byte[] rgba)
    {
        int w = t.Width, h = t.Height;
        var pal = GetWiiTlut(t);
        byte[] originalIndices = DecodeWii_C8_Indices(GetImageBytes(t), w, h);

        byte[] indices = new byte[w * h];
        for (int i = 0; i < w * h; i++)
        {
            int original = originalIndices[i];
            indices[i] = PaletteColorMatches(rgba, i * 4, pal[original])
                ? (byte)original
                : (byte)NearestIndex(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3], pal, 256);
        }

        int blocksX = (w + 7) / 8, blocksY = (h + 3) / 4;
        byte[] o = new byte[blocksX * blocksY * 32];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        int px = bx + x, py = by + y;
                        byte v = (px < w && py < h) ? indices[py * w + px] : (byte)0;
                        if (pos < o.Length) o[pos++] = v;
                    }
        Array.Resize(ref o, pos);
        return o;
    }

    private static byte[] DecodeWii_C4_Indices(byte[] raw, int w, int h)
    {
        byte[] indices = new byte[w * h];
        int pos = 0;
        for (int by = 0; by < h; by += 8)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 8; y++)
                    for (int xpair = 0; xpair < 4; xpair++)
                    {
                        if (pos >= raw.Length) return indices;
                        byte v = raw[pos++];
                        int x0 = bx + xpair * 2;
                        int py = by + y;
                        if (py < h && x0 < w) indices[py * w + x0] = (byte)(v >> 4);
                        if (py < h && x0 + 1 < w) indices[py * w + x0 + 1] = (byte)(v & 0x0F);
                    }
        return indices;
    }

    private static byte[] DecodeWii_C8_Indices(byte[] raw, int w, int h)
    {
        byte[] indices = new byte[w * h];
        int pos = 0;
        for (int by = 0; by < h; by += 4)
            for (int bx = 0; bx < w; bx += 8)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 8; x++)
                    {
                        if (pos >= raw.Length) return indices;
                        int px = bx + x;
                        int py = by + y;
                        byte index = raw[pos++];
                        if (px < w && py < h) indices[py * w + px] = index;
                    }
        return indices;
    }

    private static bool PaletteColorMatches(byte[] rgba, int offset, (byte r, byte g, byte b, byte a) c) =>
        rgba[offset] == c.r && rgba[offset + 1] == c.g && rgba[offset + 2] == c.b && rgba[offset + 3] == c.a;

    private static int NearestIndex(byte r, byte g, byte b, byte a, (byte r, byte g, byte b, byte a)[] pal, int paletteCount)
    {
        int best = 0, bestD = int.MaxValue;
        for (int k = 0; k < paletteCount; k++)
        {
            var c = pal[k];
            int dr = r - c.r, dg = g - c.g, db = b - c.b, da = a - c.a;
            int d = dr * dr + dg * dg + db * db + da * da * 2;
            if (d < bestD) { bestD = d; best = k; }
        }
        return best;
    }

    private static byte[] DecodeWii_CMPR(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int pos = 0;
        for (int by = 0; by < h; by += 8)
            for (int bx = 0; bx < w; bx += 8)
                for (int sub = 0; sub < 4; sub++)
                {
                    int sx = bx + (sub & 1) * 4;
                    int sy = by + (sub >> 1) * 4;
                    if (pos + 8 > raw.Length) return o;
                    var block = DecodeDxt1Block(raw, pos);
                    pos += 8;
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++)
                        {
                            int px = sx + x, py = sy + y;
                            if (px < w && py < h)
                            {
                                var c = block[y * 4 + x];
                                int d = (py * w + px) * 4;
                                o[d + 0] = c.b; o[d + 1] = c.g; o[d + 2] = c.r; o[d + 3] = c.a;
                            }
                        }
                }
        return o;
    }

    private static (byte r, byte g, byte b, byte a)[] DecodeDxt1Block(byte[] data, int off)
    {
        int c0 = (data[off] << 8) | data[off + 1];
        int c1 = (data[off + 2] << 8) | data[off + 3];
        var p = new (byte r, byte g, byte b, byte a)[4];
        p[0] = ((byte)(((c0 >> 11) & 0x1F) * 255 / 31), (byte)(((c0 >> 5) & 0x3F) * 255 / 63), (byte)((c0 & 0x1F) * 255 / 31), (byte)255);
        p[1] = ((byte)(((c1 >> 11) & 0x1F) * 255 / 31), (byte)(((c1 >> 5) & 0x3F) * 255 / 63), (byte)((c1 & 0x1F) * 255 / 31), (byte)255);
        if (c0 > c1)
        {
            p[2] = ((byte)((2 * p[0].r + p[1].r) / 3), (byte)((2 * p[0].g + p[1].g) / 3), (byte)((2 * p[0].b + p[1].b) / 3), (byte)255);
            p[3] = ((byte)((p[0].r + 2 * p[1].r) / 3), (byte)((p[0].g + 2 * p[1].g) / 3), (byte)((p[0].b + 2 * p[1].b) / 3), (byte)255);
        }
        else
        {
            p[2] = ((byte)((p[0].r + p[1].r) / 2), (byte)((p[0].g + p[1].g) / 2), (byte)((p[0].b + p[1].b) / 2), (byte)255);
            p[3] = (0, 0, 0, 0);
        }
        uint bits = ((uint)data[off + 4] << 24) | ((uint)data[off + 5] << 16) | ((uint)data[off + 6] << 8) | data[off + 7];
        var r = new (byte r, byte g, byte b, byte a)[16];
        for (int i = 0; i < 16; i++) r[i] = p[(int)((bits >> (30 - i * 2)) & 3)];
        return r;
    }

    private static byte[] EncodeWii_CMPR(DicTexture t, byte[] rgba)
    {
        return WiiGcCodec.EncodeCmpr(rgba, t.Width, t.Height, GetImageBytes(t));
    }
}

// Tiny adapter that exposes WiiGcCodec's CMPR encoder (which is private inside its class).
internal static class WiiGcCodecCmprBridge
{
    public static byte[] Encode(byte[] rgba, int w, int h)
    {
        // Mirror the algorithm: PCA endpoints + RGB565 quantize. Inlined here to
        // keep WiiGcCodec internals untouched.
        int sw = (w + 7) & ~7;
        int sh = (h + 7) & ~7;
        int outSize = (sw / 8) * (sh / 8) * 32;
        byte[] outBuf = new byte[outSize];
        int wp = 0;

        var block = new (byte R, byte G, byte B, byte A)[16];
        for (int y = 0; y < sh; y += 8)
            for (int x = 0; x < sw; x += 8)
                for (int sy = 0; sy < 8; sy += 4)
                    for (int sx = 0; sx < 8; sx += 4)
                    {
                        for (int dy = 0; dy < 4; dy++)
                            for (int dx = 0; dx < 4; dx++)
                            {
                                int px = x + sx + dx, py = y + sy + dy;
                                if (px < w && py < h)
                                {
                                    int s = (py * w + px) * 4;
                                    block[dy * 4 + dx] = (rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
                                }
                                else
                                {
                                    block[dy * 4 + dx] = (0, 0, 0, 0);
                                }
                            }

                        int opaque = 0;
                        foreach (var p in block) if (p.A >= 128) opaque++;
                        bool alphaMode = opaque != 16;

                        int c0, c1;
                        byte[] e0 = new byte[3], e1 = new byte[3];
                        if (opaque == 0) { c0 = 0; c1 = 0; }
                        else
                        {
                            double mR = 0, mG = 0, mB = 0;
                            foreach (var p in block) if (p.A >= 128) { mR += p.R; mG += p.G; mB += p.B; }
                            mR /= opaque; mG /= opaque; mB /= opaque;
                            double cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
                            foreach (var p in block)
                            {
                                if (p.A < 128) continue;
                                double dr = p.R - mR, dg = p.G - mG, db = p.B - mB;
                                cxx += dr * dr; cyy += dg * dg; czz += db * db;
                                cxy += dr * dg; cxz += dr * db; cyz += dg * db;
                            }
                            double vx = cxx, vy = cxy, vz = cxz;
                            if (vx * vx + vy * vy + vz * vz < 1e-9) { vx = 1; vy = 1; vz = 1; }
                            for (int it = 0; it < 12; it++)
                            {
                                double nx = cxx * vx + cxy * vy + cxz * vz;
                                double ny = cxy * vx + cyy * vy + cyz * vz;
                                double nz = cxz * vx + cyz * vy + czz * vz;
                                double mag = Math.Sqrt(nx * nx + ny * ny + nz * nz);
                                if (mag < 1e-9) { nx = 1; ny = 0; nz = 0; mag = 1; }
                                vx = nx / mag; vy = ny / mag; vz = nz / mag;
                            }
                            double minP = double.PositiveInfinity, maxP = double.NegativeInfinity;
                            foreach (var p in block)
                            {
                                if (p.A < 128) continue;
                                double t = (p.R - mR) * vx + (p.G - mG) * vy + (p.B - mB) * vz;
                                if (t < minP) minP = t;
                                if (t > maxP) maxP = t;
                            }
                            int e0r = (int)Math.Round(Math.Clamp(mR + maxP * vx, 0, 255));
                            int e0g = (int)Math.Round(Math.Clamp(mG + maxP * vy, 0, 255));
                            int e0b = (int)Math.Round(Math.Clamp(mB + maxP * vz, 0, 255));
                            int e1r = (int)Math.Round(Math.Clamp(mR + minP * vx, 0, 255));
                            int e1g = (int)Math.Round(Math.Clamp(mG + minP * vy, 0, 255));
                            int e1b = (int)Math.Round(Math.Clamp(mB + minP * vz, 0, 255));
                            c0 = ((e0r * 31 + 127) / 255 << 11) | ((e0g * 63 + 127) / 255 << 5) | ((e0b * 31 + 127) / 255);
                            c1 = ((e1r * 31 + 127) / 255 << 11) | ((e1g * 63 + 127) / 255 << 5) | ((e1b * 31 + 127) / 255);
                            if (alphaMode && c0 > c1) (c0, c1) = (c1, c0);
                            else if (!alphaMode && c0 == c1) { if (c0 > 0) c1 = c0 - 1; else c0 = 1; }
                            else if (!alphaMode && c0 < c1) (c0, c1) = (c1, c0);
                        }

                        byte[] p0 = Rgb565ToRgba(c0), p1 = Rgb565ToRgba(c1), p2 = new byte[4], p3 = new byte[4];
                        if (c0 > c1)
                        {
                            for (int i = 0; i < 4; i++) { p2[i] = (byte)((2 * p0[i] + p1[i]) / 3); p3[i] = (byte)((2 * p1[i] + p0[i]) / 3); }
                        }
                        else
                        {
                            for (int i = 0; i < 4; i++) p2[i] = (byte)((p0[i] + p1[i]) >> 1);
                        }
                        outBuf[wp++] = (byte)((c0 >> 8) & 0xFF);
                        outBuf[wp++] = (byte)(c0 & 0xFF);
                        outBuf[wp++] = (byte)((c1 >> 8) & 0xFF);
                        outBuf[wp++] = (byte)(c1 & 0xFF);

                        bool transparentSlot = !(c0 > c1);
                        for (int row = 0; row < 4; row++)
                        {
                            int bits = 0;
                            for (int col = 0; col < 4; col++)
                            {
                                var px = block[row * 4 + col];
                                int idx;
                                if (alphaMode && px.A < 128 && transparentSlot) idx = 3;
                                else
                                {
                                    int max = transparentSlot ? 2 : 3;
                                    int bestI = 0, bestD = int.MaxValue;
                                    byte[][] palette = { p0, p1, p2, p3 };
                                    for (int k = 0; k <= max; k++)
                                    {
                                        int dr = px.R - palette[k][0], dg = px.G - palette[k][1], db = px.B - palette[k][2];
                                        int d = dr * dr + dg * dg + db * db;
                                        if (d < bestD) { bestD = d; bestI = k; }
                                    }
                                    idx = bestI;
                                }
                                bits |= (idx & 0x3) << (6 - col * 2);
                            }
                            outBuf[wp++] = (byte)bits;
                        }
                    }
        return outBuf;
    }

    private static byte[] Rgb565ToRgba(int c)
    {
        byte[] p = new byte[4];
        p[0] = (byte)(((c >> 11) & 0x1F) * 255 / 31);
        p[1] = (byte)(((c >> 5) & 0x3F) * 255 / 63);
        p[2] = (byte)((c & 0x1F) * 255 / 31);
        p[3] = 255;
        return p;
    }
}

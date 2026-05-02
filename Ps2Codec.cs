using System;

#pragma warning disable CS0162 // unreachable code (compile-time const branches)

namespace HVTTool;

// PS2 8bpp paletted decode/encode.
// Ported from ReverseBox: reversebox/image/swizzling/swizzle_ps2.py
// - _convert_ps2_8bit          : 8bpp swizzle (Type 1/2)
// - _convert_ps2_palette       : palette CSM1 <-> CSM2 reordering
public static class Ps2Codec
{
    // PS2 stores alpha in 0..128 where 128 = fully opaque. Multiplying by 2
    // (clamped) recovers the visible 0..255 range. Some games store a normal
    // 0..255 alpha already; the heuristic at the bottom decides per file.
    private const bool ScalePs2Alpha = true;

    // ------------------------------------------------------------------
    // Public entry points
    // ------------------------------------------------------------------
    // Empirically verified against the sample HVIs:
    //  - pixel data is stored linearly (NOT PS2-swizzled)
    //  - palette is ALREADY in linear (CSM2) form, no reorder needed
    //    (originally I assumed CSM1 and applied the swap; that scrambled
    //     indices 8..23 in every 32-entry block and produced the red-splotch
    //     "noise" some users reported on art_*.hvi and mmaph_*.hvi)
    //  - palette bytes are BGRA, not RGBA (verified by flesh-tone correctness)
    private const bool PixelDataIsSwizzled = false;
    private const bool PaletteIsSwizzled = false;
    private const bool PaletteIsBgra = true;

    public static byte[] DecodeToBgra(HviFile file)
    {
        if (file.Bpp != 8)
            throw new NotSupportedException("Only 8bpp HVI is supported.");

        bool needsAlphaScale = NeedsAlphaScale(file.Palette);
        byte[] palOrdered = PaletteIsSwizzled
            ? UnswizzlePaletteCsm(file.Palette, needsAlphaScale)
            : ApplyAlphaScale((byte[])file.Palette.Clone(), needsAlphaScale);
        byte[] linearIndices = PixelDataIsSwizzled
            ? UnswizzlePs2_8bit(file.PixelData, file.Width, file.Height)
            : (byte[])file.PixelData.Clone();

        // Palette is BGRA in storage, so palette[+0]=B, [+1]=G, [+2]=R, [+3]=A.
        // Output buffer is BGRA32 (Bitmap-friendly).
        byte[] outBgra = new byte[file.Width * file.Height * 4];
        for (int i = 0; i < linearIndices.Length; i++)
        {
            int p = linearIndices[i] * 4;
            int d = i * 4;
            if (PaletteIsBgra)
            {
                outBgra[d + 0] = palOrdered[p + 0]; // B
                outBgra[d + 1] = palOrdered[p + 1]; // G
                outBgra[d + 2] = palOrdered[p + 2]; // R
                outBgra[d + 3] = palOrdered[p + 3]; // A
            }
            else
            {
                outBgra[d + 0] = palOrdered[p + 2];
                outBgra[d + 1] = palOrdered[p + 1];
                outBgra[d + 2] = palOrdered[p + 0];
                outBgra[d + 3] = palOrdered[p + 3];
            }
        }
        return outBgra;
    }

    public static (byte[] palette, byte[] pixels) EncodeFromRgba(HviFile file, byte[] rgba)
    {
        if (file.Bpp != 8)
            throw new NotSupportedException("Only 8bpp HVI is supported.");

        bool needsAlphaScale = NeedsAlphaScale(file.Palette);
        byte[] palOrdered = PaletteIsSwizzled
            ? UnswizzlePaletteCsm(file.Palette, needsAlphaScale)
            : ApplyAlphaScale((byte[])file.Palette.Clone(), needsAlphaScale);

        // Re-quantize into the existing palette by nearest match.
        // Source rgba is in (R, G, B, A) order; palette is stored BGRA.
        byte[] linearIndices = new byte[file.Width * file.Height];
        for (int i = 0; i < linearIndices.Length; i++)
        {
            byte r = rgba[i * 4 + 0];
            byte g = rgba[i * 4 + 1];
            byte b = rgba[i * 4 + 2];
            byte a = rgba[i * 4 + 3];
            int best = 0, bestD = int.MaxValue;
            for (int k = 0; k < 256; k++)
            {
                int pi = k * 4;
                int palR = PaletteIsBgra ? palOrdered[pi + 2] : palOrdered[pi + 0];
                int palG = palOrdered[pi + 1];
                int palB = PaletteIsBgra ? palOrdered[pi + 0] : palOrdered[pi + 2];
                int palA = palOrdered[pi + 3];
                int dr = r - palR;
                int dg = g - palG;
                int db = b - palB;
                int da = a - palA;
                int d = dr * dr + dg * dg + db * db + da * da;
                if (d < bestD) { bestD = d; best = k; }
            }
            linearIndices[i] = (byte)best;
        }

        // Pixel data stays linear (no PS2 swizzle for these HVIs) and the
        // palette is preserved exactly as it was in the original file (still
        // CSM1, still BGRA).
        byte[] outPixels = PixelDataIsSwizzled
            ? SwizzlePs2_8bit(linearIndices, file.Width, file.Height)
            : linearIndices;
        return (file.Palette, outPixels);
    }

    // ------------------------------------------------------------------
    // PS2 8bpp swizzle (Type 1/2 — same formula).
    // ------------------------------------------------------------------
    private static int Ps2_8bit_SwizzleId(int x, int y, int w)
    {
        int blockLocation = (y & ~0xF) * w + (x & ~0xF) * 2;
        int swapSelector = (((y + 2) >> 2) & 0x1) * 4;
        int posY = (((y & ~3) >> 1) + (y & 1)) & 0x7;
        int columnLocation = posY * w * 2 + ((x + swapSelector) & 0x7) * 4;
        int byteNum = ((y >> 1) & 1) + ((x >> 2) & 2);
        return blockLocation + columnLocation + byteNum;
    }

    private static byte[] UnswizzlePs2_8bit(byte[] data, int w, int h)
    {
        byte[] outBuf = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                outBuf[y * w + x] = data[Ps2_8bit_SwizzleId(x, y, w)];
        return outBuf;
    }

    private static byte[] SwizzlePs2_8bit(byte[] data, int w, int h)
    {
        byte[] outBuf = new byte[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                outBuf[Ps2_8bit_SwizzleId(x, y, w)] = data[y * w + x];
        return outBuf;
    }

    // ------------------------------------------------------------------
    // PS2 palette CSM1 <-> CSM2 reorder. The function is its own inverse
    // for the standard 256-entry / 32-bpp case.
    // ------------------------------------------------------------------
    // CSM1 <-> CSM2 reordering: for every 32-entry block (128 bytes for RGBA8),
    // swap entries [8..15] with [16..23]. Self-inverse, so it works for both
    // unswizzling (read) and swizzling (write).
    private static byte[] UnswizzlePaletteCsm(byte[] palette, bool scaleAlpha)
    {
        const int bytesPerEntry = 4; // RGBA8
        const int entriesPerGroup = 32;
        const int bytesPerGroup = entriesPerGroup * bytesPerEntry; // 128
        byte[] outBuf = new byte[palette.Length];
        Buffer.BlockCopy(palette, 0, outBuf, 0, palette.Length);

        for (int g = 0; g < palette.Length; g += bytesPerGroup)
        {
            int chunkBytes = 8 * bytesPerEntry; // 32
            // Swap [g+32 .. g+63] with [g+64 .. g+95]
            byte[] tmp = new byte[chunkBytes];
            Buffer.BlockCopy(outBuf, g + chunkBytes, tmp, 0, chunkBytes);
            Buffer.BlockCopy(outBuf, g + 2 * chunkBytes, outBuf, g + chunkBytes, chunkBytes);
            Buffer.BlockCopy(tmp, 0, outBuf, g + 2 * chunkBytes, chunkBytes);
        }

        if (scaleAlpha)
        {
            for (int i = 3; i < outBuf.Length; i += 4)
            {
                int v = outBuf[i] * 2;
                outBuf[i] = (byte)Math.Min(255, v);
            }
        }
        return outBuf;
    }

    // ------------------------------------------------------------------
    // Heuristic: PS2 uses 0..128 alpha, but some games store full 0..255.
    // If the palette's alpha values never exceed ~0x80 we assume PS2 scale.
    // ------------------------------------------------------------------
    private static byte[] ApplyAlphaScale(byte[] data, bool scale)
    {
        if (!scale) return data;
        for (int i = 3; i < data.Length; i += 4)
            data[i] = (byte)Math.Min(255, data[i] * 2);
        return data;
    }

    private static bool NeedsAlphaScale(byte[] palette)
    {
        byte maxA = 0;
        for (int i = 3; i < palette.Length; i += 4)
            if (palette[i] > maxA) maxA = palette[i];
        // 0x80..0x8F covers PS2's "full opacity" with a small slack for noise.
        return maxA <= 0x90;
    }
}

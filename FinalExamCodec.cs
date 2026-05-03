using System;

namespace HVTTool;

// Decoders/encoders for HydraVision Final Exam .hvt textures.
// Supported formats:
//   BGRA  — 32 bpp linear, bytes B,G,R,A per pixel
//   BGRX  — 32 bpp linear, alpha byte ignored (treated as opaque)
//   TXD1  — DXT1 (BC1) compressed, 4 bpp effective
//   TXD5  — DXT5 (BC3) compressed, 8 bpp effective
public static class FinalExamCodec
{
    public static byte[] DecodeToBgra(FinalExamHvt file)
    {
        byte[] raw = new byte[Math.Min(file.Mip0Size, file.Data.Length - file.PixelOffset)];
        Buffer.BlockCopy(file.Data, file.PixelOffset, raw, 0, raw.Length);

        // X360 textures (pixels for ARGB, 4×4 blocks for BC1/3/5) are stored in
        // the GPU 32-tile swizzle. Untile first, then run the generic PC-LE
        // decoders below.
        if (file.Platform == FxPlatform.X360)
        {
            if (file.Format == FxPixelFormat.ARGB)
            {
                byte[] linearArgb = UnswizzleX360(raw, file.AlignedWidth, file.AlignedHeight,
                    blockPixelSize: 1, texelBytePitch: 4);
                byte[] full = DecodeArgbBe(linearArgb, file.AlignedWidth, file.AlignedHeight);
                return CropBgra(full, file.AlignedWidth, file.Width, file.Height);
            }
            if (file.IsBlockCompressed)
            {
                int blockBytes = file.Format == FxPixelFormat.TXD1 ? 8 : 16;
                byte[] linearBc = UnswizzleX360(raw, file.AlignedWidth, file.AlignedHeight,
                    blockPixelSize: 4, texelBytePitch: blockBytes);
                X360BcByteSwap(linearBc); // 16-bit pair swap (X360 stores BC blocks big-endian)
                byte[] full = file.Format switch
                {
                    FxPixelFormat.TXD1 => DecodeDxt1(linearBc, file.AlignedWidth, file.AlignedHeight),
                    FxPixelFormat.TXD3 => DecodeDxt3(linearBc, file.AlignedWidth, file.AlignedHeight),
                    FxPixelFormat.TXD5 => DecodeDxt5(linearBc, file.AlignedWidth, file.AlignedHeight),
                    _ => throw new NotSupportedException()
                };
                return CropBgra(full, file.AlignedWidth, file.Width, file.Height);
            }
        }

        return file.Format switch
        {
            FxPixelFormat.BGRA => DecodeBgra(raw, file.Width, file.Height),
            FxPixelFormat.BGRX => DecodeBgrx(raw, file.Width, file.Height),
            FxPixelFormat.ARGB => DecodeArgbBe(raw, file.Width, file.Height),
            FxPixelFormat.TXD1 => DecodeDxt1(raw, file.Width, file.Height),
            FxPixelFormat.TXD3 => DecodeDxt3(raw, file.Width, file.Height),
            FxPixelFormat.TXD5 => DecodeDxt5(raw, file.Width, file.Height),
            _ => throw new NotSupportedException($"Format \"{file.FormatTag}\" not supported.")
        };
    }

    public static byte[] EncodeFromRgba(FinalExamHvt file, byte[] rgba)
    {
        if (file.Platform == FxPlatform.X360)
        {
            if (file.Format == FxPixelFormat.ARGB)
            {
                byte[] padded = PadRgba(rgba, file.Width, file.Height, file.AlignedWidth, file.AlignedHeight);
                byte[] linearArgb = EncodeArgbBe(padded, file.AlignedWidth, file.AlignedHeight);
                return SwizzleX360(linearArgb, file.AlignedWidth, file.AlignedHeight,
                    blockPixelSize: 1, texelBytePitch: 4);
            }
            if (file.IsBlockCompressed)
            {
                byte[] padded = PadRgba(rgba, file.Width, file.Height, file.AlignedWidth, file.AlignedHeight);
                byte[] linear = file.Format switch
                {
                    FxPixelFormat.TXD1 => EncodeDxt1(padded, file.AlignedWidth, file.AlignedHeight),
                    FxPixelFormat.TXD3 => EncodeDxt3(padded, file.AlignedWidth, file.AlignedHeight),
                    FxPixelFormat.TXD5 => EncodeDxt5(padded, file.AlignedWidth, file.AlignedHeight),
                    _ => throw new NotSupportedException()
                };
                int blockBytes = file.Format == FxPixelFormat.TXD1 ? 8 : 16;
                X360BcByteSwap(linear);
                return SwizzleX360(linear, file.AlignedWidth, file.AlignedHeight,
                    blockPixelSize: 4, texelBytePitch: blockBytes);
            }
        }

        return file.Format switch
        {
            FxPixelFormat.BGRA => EncodeBgra(rgba, file.Width, file.Height),
            FxPixelFormat.BGRX => EncodeBgrx(rgba, file.Width, file.Height),
            FxPixelFormat.ARGB => EncodeArgbBe(rgba, file.Width, file.Height),
            FxPixelFormat.TXD1 => EncodeDxt1(rgba, file.Width, file.Height),
            FxPixelFormat.TXD3 => EncodeDxt3(rgba, file.Width, file.Height),
            FxPixelFormat.TXD5 => EncodeDxt5(rgba, file.Width, file.Height),
            _ => throw new NotSupportedException($"Format \"{file.FormatTag}\" not supported.")
        };
    }

    // ==================================================================
    // X360 GPU swizzle — direct port of ReverseBox's swizzle_x360.py, which
    // has unit tests against several captured X360 BC1/BC2 textures. Works
    // by enumerating the LINEAR (tiled) buffer and computing the destination
    // (x, y) of every texel/block — the inverse of XGAddress2DTiledOffset.
    //
    // For BC formats use blockPixelSize=4, texelBytePitch=blockBytes (8 for
    // BC1, 16 for BC2/BC3). For ARGB use blockPixelSize=1, texelBytePitch=4.
    // ==================================================================
    private static byte[] UnswizzleX360(byte[] swizzled, int width, int height,
        int blockPixelSize, int texelBytePitch)
    {
        int wb = width  / blockPixelSize;
        int hb = height / blockPixelSize;
        int paddedWb = (wb + 31) & ~31;
        int paddedHb = (hb + 31) & ~31;
        int total = paddedWb * paddedHb;

        byte[] o = new byte[wb * hb * texelBytePitch];
        for (int blockOffset = 0; blockOffset < total; blockOffset++)
        {
            int x = X360TiledX(blockOffset, paddedWb, texelBytePitch);
            int y = X360TiledY(blockOffset, paddedWb, texelBytePitch);
            if (x >= wb || y >= hb) continue;
            int src = blockOffset * texelBytePitch;
            int dst = (y * wb + x) * texelBytePitch;
            if (src + texelBytePitch > swizzled.Length) continue;
            Buffer.BlockCopy(swizzled, src, o, dst, texelBytePitch);
        }
        return o;
    }

    private static byte[] SwizzleX360(byte[] linear, int width, int height,
        int blockPixelSize, int texelBytePitch)
    {
        int wb = width  / blockPixelSize;
        int hb = height / blockPixelSize;
        int paddedWb = (wb + 31) & ~31;
        int paddedHb = (hb + 31) & ~31;
        int total = paddedWb * paddedHb;

        byte[] o = new byte[total * texelBytePitch];
        for (int blockOffset = 0; blockOffset < total; blockOffset++)
        {
            int x = X360TiledX(blockOffset, paddedWb, texelBytePitch);
            int y = X360TiledY(blockOffset, paddedWb, texelBytePitch);
            if (x >= wb || y >= hb) continue;
            int src = (y * wb + x) * texelBytePitch;
            int dst = blockOffset * texelBytePitch;
            if (src + texelBytePitch > linear.Length) continue;
            Buffer.BlockCopy(linear, src, o, dst, texelBytePitch);
        }
        return o;
    }

    private static byte[] CropBgra(byte[] bgra, int srcWidth, int dstWidth, int dstHeight)
    {
        byte[] o = new byte[dstWidth * dstHeight * 4];
        int rowBytes = dstWidth * 4;
        for (int y = 0; y < dstHeight; y++)
            Buffer.BlockCopy(bgra, y * srcWidth * 4, o, y * rowBytes, rowBytes);
        return o;
    }

    private static byte[] PadRgba(byte[] rgba, int srcWidth, int srcHeight, int dstWidth, int dstHeight)
    {
        if (srcWidth == dstWidth && srcHeight == dstHeight)
            return rgba;

        byte[] o = new byte[dstWidth * dstHeight * 4];
        for (int y = 0; y < dstHeight; y++)
        {
            int sy = Math.Min(y, srcHeight - 1);
            for (int x = 0; x < dstWidth; x++)
            {
                int sx = Math.Min(x, srcWidth - 1);
                Buffer.BlockCopy(rgba, (sy * srcWidth + sx) * 4, o, (y * dstWidth + x) * 4, 4);
            }
        }
        return o;
    }

    private static int X360TiledX(int blockOffset, int widthInBlocks, int texelBytePitch)
    {
        int alignedWidth = (widthInBlocks + 31) & ~31;
        int logBpp = (texelBytePitch >> 2) + ((texelBytePitch >> 1) >> (texelBytePitch >> 2));
        int offsetByte = blockOffset << logBpp;
        int offsetTile = ((offsetByte & ~0xFFF) >> 3) + ((offsetByte & 0x700) >> 2) + (offsetByte & 0x3F);
        int offsetMacro = offsetTile >> (7 + logBpp);

        int macroX = (offsetMacro % (alignedWidth >> 5)) << 2;
        int tile   = (((offsetTile >> (5 + logBpp)) & 2) + (offsetByte >> 6)) & 3;
        int macro  = (macroX + tile) << 3;
        int micro  = ((((offsetTile >> 1) & ~0xF) + (offsetTile & 0xF)) & ((texelBytePitch << 3) - 1)) >> logBpp;
        return macro + micro;
    }

    private static int X360TiledY(int blockOffset, int widthInBlocks, int texelBytePitch)
    {
        int alignedWidth = (widthInBlocks + 31) & ~31;
        int logBpp = (texelBytePitch >> 2) + ((texelBytePitch >> 1) >> (texelBytePitch >> 2));
        int offsetByte = blockOffset << logBpp;
        int offsetTile = ((offsetByte & ~0xFFF) >> 3) + ((offsetByte & 0x700) >> 2) + (offsetByte & 0x3F);
        int offsetMacro = offsetTile >> (7 + logBpp);

        int macroY = (offsetMacro / (alignedWidth >> 5)) << 2;
        int tile   = ((offsetTile >> (6 + logBpp)) & 1) + ((offsetByte & 0x800) >> 10);
        int macro  = (macroY + tile) << 3;
        int micro  = (((offsetTile & ((texelBytePitch << 6) - 1 & ~0x1F)) + ((offsetTile & 0xF) << 1)) >> (3 + logBpp)) & ~1;
        return macro + micro + ((offsetTile & 0x10) >> 4);
    }

    /// <summary>Byte-swap every 16-bit pair (ImageHeat's swap_byte_order_x360).</summary>
    private static void X360BcByteSwap(byte[] data)
    {
        for (int i = 0; i + 1 < data.Length; i += 2)
            (data[i], data[i + 1]) = (data[i + 1], data[i]);
    }

    // ==================================================================
    // ARGB (PS3 big-endian linear) — bytes per pixel are A, R, G, B.
    // ==================================================================
    private static byte[] DecodeArgbBe(byte[] raw, int w, int h)
    {
        int n = w * h;
        byte[] o = new byte[n * 4];
        int lim = Math.Min(raw.Length, n * 4);
        for (int i = 0; i + 3 < lim; i += 4)
        {
            o[i + 0] = raw[i + 3]; // B
            o[i + 1] = raw[i + 2]; // G
            o[i + 2] = raw[i + 1]; // R
            o[i + 3] = raw[i + 0]; // A
        }
        return o;
    }

    private static byte[] EncodeArgbBe(byte[] rgba, int w, int h)
    {
        int n = w * h;
        byte[] o = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            o[i * 4 + 0] = rgba[i * 4 + 3]; // A
            o[i * 4 + 1] = rgba[i * 4 + 0]; // R
            o[i * 4 + 2] = rgba[i * 4 + 1]; // G
            o[i * 4 + 3] = rgba[i * 4 + 2]; // B
        }
        return o;
    }

    // ==================================================================
    // DXT3 (BC2) — 16 bytes per 4x4 block (8 alpha 4-bit + 8 color/DXT1)
    // ==================================================================
    private static byte[] DecodeDxt3(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int blocksX = (w + 3) / 4, blocksY = (h + 3) / 4;
        int pos = 0;
        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (pos + 16 > raw.Length) return o;
                ulong abits = 0;
                for (int i = 0; i < 8; i++) abits |= (ulong)raw[pos + i] << (i * 8);
                // Color block at +8 (same as DXT1 but always 4-color mode)
                int c0 = raw[pos + 8] | (raw[pos + 9] << 8);
                int c1 = raw[pos + 10] | (raw[pos + 11] << 8);
                var (b0, g0, r0, _) = Rgb565(c0);
                var (b1, g1, r1, _) = Rgb565(c1);
                var p = new (byte b, byte g, byte r)[4];
                p[0] = (b0, g0, r0); p[1] = (b1, g1, r1);
                p[2] = ((byte)((2 * b0 + b1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * r0 + r1) / 3));
                p[3] = ((byte)((b0 + 2 * b1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((r0 + 2 * r1) / 3));
                uint cbits = (uint)(raw[pos + 12] | (raw[pos + 13] << 8) | (raw[pos + 14] << 16) | (raw[pos + 15] << 24));
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        int px = bx * 4 + x, py = by * 4 + y;
                        if (px >= w || py >= h) continue;
                        int idx = y * 4 + x;
                        int a = (int)((abits >> (idx * 4)) & 0xF) * 0x11;
                        int cIdx = (int)((cbits >> (idx * 2)) & 3);
                        int d = (py * w + px) * 4;
                        o[d + 0] = p[cIdx].b; o[d + 1] = p[cIdx].g; o[d + 2] = p[cIdx].r; o[d + 3] = (byte)a;
                    }
                pos += 16;
            }
        return o;
    }

    private static byte[] EncodeDxt3(byte[] rgba, int w, int h)
    {
        // Reuse DXT5 color encode + replace alpha block with 4-bit per pixel.
        byte[] dxt5 = EncodeDxt5(rgba, w, h);
        for (int blkOff = 0; blkOff + 16 <= dxt5.Length; blkOff += 16)
        {
            // Recompute the 4-bit alpha block from the original pixels.
            int blockIdx = blkOff / 16;
            int blocksX = (w + 3) / 4;
            int bx = blockIdx % blocksX, by = blockIdx / blocksX;
            ulong abits = 0;
            for (int y = 0; y < 4; y++)
                for (int x = 0; x < 4; x++)
                {
                    int sx = Math.Min(bx * 4 + x, w - 1);
                    int sy = Math.Min(by * 4 + y, h - 1);
                    byte a = rgba[(sy * w + sx) * 4 + 3];
                    int a4 = (a + 8) / 17; // round to 4-bit
                    if (a4 > 15) a4 = 15;
                    abits |= (ulong)a4 << ((y * 4 + x) * 4);
                }
            for (int i = 0; i < 8; i++) dxt5[blkOff + i] = (byte)((abits >> (i * 8)) & 0xFF);
        }
        return dxt5;
    }

    // ==================================================================
    // BGRA / BGRX (linear)
    // ==================================================================
    private static byte[] DecodeBgra(byte[] raw, int w, int h)
    {
        int n = w * h * 4;
        byte[] o = new byte[n];
        Buffer.BlockCopy(raw, 0, o, 0, Math.Min(raw.Length, n));
        return o;
    }
    private static byte[] EncodeBgra(byte[] rgba, int w, int h)
    {
        int n = w * h;
        byte[] o = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            o[i * 4 + 0] = rgba[i * 4 + 2]; // B
            o[i * 4 + 1] = rgba[i * 4 + 1]; // G
            o[i * 4 + 2] = rgba[i * 4 + 0]; // R
            o[i * 4 + 3] = rgba[i * 4 + 3]; // A
        }
        return o;
    }

    private static byte[] DecodeBgrx(byte[] raw, int w, int h)
    {
        int n = w * h;
        byte[] o = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            o[i * 4 + 0] = raw[i * 4 + 0];
            o[i * 4 + 1] = raw[i * 4 + 1];
            o[i * 4 + 2] = raw[i * 4 + 2];
            o[i * 4 + 3] = 0xFF; // ignore X, force opaque
        }
        return o;
    }
    private static byte[] EncodeBgrx(byte[] rgba, int w, int h)
    {
        int n = w * h;
        byte[] o = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            o[i * 4 + 0] = rgba[i * 4 + 2];
            o[i * 4 + 1] = rgba[i * 4 + 1];
            o[i * 4 + 2] = rgba[i * 4 + 0];
            o[i * 4 + 3] = 0xFF;
        }
        return o;
    }

    // ==================================================================
    // DXT1 (BC1) — 8 bytes per 4x4 block
    // ==================================================================
    private static byte[] DecodeDxt1(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int blocksX = (w + 3) / 4, blocksY = (h + 3) / 4;
        int pos = 0;
        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (pos + 8 > raw.Length) return o;
                DecodeDxt1Block(raw, pos, o, bx * 4, by * 4, w, h);
                pos += 8;
            }
        return o;
    }

    private static void DecodeDxt1Block(byte[] data, int off, byte[] dst, int x0, int y0, int w, int h)
    {
        int c0 = data[off] | (data[off + 1] << 8);
        int c1 = data[off + 2] | (data[off + 3] << 8);
        var p = new (byte b, byte g, byte r, byte a)[4];
        p[0] = Rgb565(c0);
        p[1] = Rgb565(c1);
        if (c0 > c1)
        {
            p[2] = ((byte)((2 * p[0].b + p[1].b) / 3),
                    (byte)((2 * p[0].g + p[1].g) / 3),
                    (byte)((2 * p[0].r + p[1].r) / 3),
                    (byte)255);
            p[3] = ((byte)((p[0].b + 2 * p[1].b) / 3),
                    (byte)((p[0].g + 2 * p[1].g) / 3),
                    (byte)((p[0].r + 2 * p[1].r) / 3),
                    (byte)255);
        }
        else
        {
            p[2] = ((byte)((p[0].b + p[1].b) / 2),
                    (byte)((p[0].g + p[1].g) / 2),
                    (byte)((p[0].r + p[1].r) / 2),
                    (byte)255);
            p[3] = (0, 0, 0, 0);
        }
        uint bits = (uint)(data[off + 4] | (data[off + 5] << 8) | (data[off + 6] << 16) | (data[off + 7] << 24));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int px = x0 + x, py = y0 + y;
                if (px >= w || py >= h) continue;
                var c = p[(int)((bits >> ((y * 4 + x) * 2)) & 3)];
                int d = (py * w + px) * 4;
                dst[d + 0] = c.b; dst[d + 1] = c.g; dst[d + 2] = c.r; dst[d + 3] = c.a;
            }
    }

    private static (byte b, byte g, byte r, byte a) Rgb565(int v)
    {
        int r = ((v >> 11) & 0x1F) * 255 / 31;
        int g = ((v >> 5) & 0x3F) * 255 / 63;
        int b = (v & 0x1F) * 255 / 31;
        return ((byte)b, (byte)g, (byte)r, (byte)255);
    }

    // ==================================================================
    // DXT5 (BC3) — 16 bytes per 4x4 block (8 alpha + 8 color/DXT1)
    // ==================================================================
    private static byte[] DecodeDxt5(byte[] raw, int w, int h)
    {
        byte[] o = new byte[w * h * 4];
        int blocksX = (w + 3) / 4, blocksY = (h + 3) / 4;
        int pos = 0;
        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                if (pos + 16 > raw.Length) return o;
                DecodeDxt5Block(raw, pos, o, bx * 4, by * 4, w, h);
                pos += 16;
            }
        return o;
    }

    private static void DecodeDxt5Block(byte[] data, int off, byte[] dst, int x0, int y0, int w, int h)
    {
        // ----- Alpha block (8 bytes) -----
        byte a0 = data[off + 0];
        byte a1 = data[off + 1];
        var alpha = new byte[8];
        alpha[0] = a0;
        alpha[1] = a1;
        if (a0 > a1)
        {
            for (int i = 1; i <= 6; i++) alpha[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);
        }
        else
        {
            for (int i = 1; i <= 4; i++) alpha[i + 1] = (byte)(((5 - i) * a0 + i * a1) / 5);
            alpha[6] = 0;
            alpha[7] = 255;
        }
        // 48-bit alpha index field (bytes 2-7)
        ulong abits = 0;
        for (int i = 0; i < 6; i++) abits |= (ulong)data[off + 2 + i] << (i * 8);

        // ----- Color block (8 bytes, DXT1 layout) -----
        int c0 = data[off + 8] | (data[off + 9] << 8);
        int c1 = data[off + 10] | (data[off + 11] << 8);
        var p = new (byte b, byte g, byte r)[4];
        var (b0, g0, r0, _) = Rgb565(c0);
        var (b1, g1, r1, _) = Rgb565(c1);
        p[0] = (b0, g0, r0);
        p[1] = (b1, g1, r1);
        // DXT5 always uses 4-color interpolation regardless of c0/c1 ordering.
        p[2] = ((byte)((2 * b0 + b1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * r0 + r1) / 3));
        p[3] = ((byte)((b0 + 2 * b1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((r0 + 2 * r1) / 3));

        uint cbits = (uint)(data[off + 12] | (data[off + 13] << 8) | (data[off + 14] << 16) | (data[off + 15] << 24));
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int px = x0 + x, py = y0 + y;
                if (px >= w || py >= h) continue;
                int idx = y * 4 + x;
                int cIdx = (int)((cbits >> (idx * 2)) & 3);
                int aIdx = (int)((abits >> (idx * 3)) & 7);
                int d = (py * w + px) * 4;
                dst[d + 0] = p[cIdx].b;
                dst[d + 1] = p[cIdx].g;
                dst[d + 2] = p[cIdx].r;
                dst[d + 3] = alpha[aIdx];
            }
    }

    // ==================================================================
    // DXT1 / DXT5 encoders — bounding-box endpoint selection (simple and fast,
    // good enough for editing-and-reinsertion workflows; not as good as PCA but
    // matches what most lightweight compressors produce).
    // ==================================================================
    private static byte[] EncodeDxt1(byte[] rgba, int w, int h)
    {
        int blocksX = (w + 3) / 4, blocksY = (h + 3) / 4;
        byte[] o = new byte[blocksX * blocksY * 8];
        int pos = 0;
        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                EncodeDxt1Block(rgba, w, h, bx * 4, by * 4, o, pos);
                pos += 8;
            }
        return o;
    }

    private static void EncodeDxt1Block(byte[] rgba, int w, int h, int x0, int y0, byte[] outBuf, int outOff)
    {
        var px = new (byte r, byte g, byte b, byte a)[16];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int sx = Math.Min(x0 + x, w - 1);
                int sy = Math.Min(y0 + y, h - 1);
                int s = (sy * w + sx) * 4;
                px[y * 4 + x] = (rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
            }
        int rmin = 255, gmin = 255, bmin = 255, rmax = 0, gmax = 0, bmax = 0;
        foreach (var p in px)
        {
            if (p.r < rmin) rmin = p.r; if (p.r > rmax) rmax = p.r;
            if (p.g < gmin) gmin = p.g; if (p.g > gmax) gmax = p.g;
            if (p.b < bmin) bmin = p.b; if (p.b > bmax) bmax = p.b;
        }
        int c0 = ((rmax * 31 + 127) / 255 << 11) | ((gmax * 63 + 127) / 255 << 5) | ((bmax * 31 + 127) / 255);
        int c1 = ((rmin * 31 + 127) / 255 << 11) | ((gmin * 63 + 127) / 255 << 5) | ((bmin * 31 + 127) / 255);
        if (c0 == c1) { if (c0 > 0) c1 = c0 - 1; else c0 = 1; }
        if (c0 < c1) (c0, c1) = (c1, c0); // ensure c0 > c1 (4-color mode)

        var (b0, g0, r0, _) = Rgb565(c0);
        var (b1, g1, r1, _) = Rgb565(c1);
        var p2 = ((byte)((2 * r0 + r1) / 3), (byte)((2 * g0 + g1) / 3), (byte)((2 * b0 + b1) / 3));
        var p3 = ((byte)((r0 + 2 * r1) / 3), (byte)((g0 + 2 * g1) / 3), (byte)((b0 + 2 * b1) / 3));

        outBuf[outOff + 0] = (byte)(c0 & 0xFF);
        outBuf[outOff + 1] = (byte)((c0 >> 8) & 0xFF);
        outBuf[outOff + 2] = (byte)(c1 & 0xFF);
        outBuf[outOff + 3] = (byte)((c1 >> 8) & 0xFF);

        uint bits = 0;
        for (int i = 0; i < 16; i++)
        {
            var p = px[i];
            int best = 0, bestD = int.MaxValue;
            (byte r, byte g, byte b)[] palette = { (r0, g0, b0), (r1, g1, b1), p2, p3 };
            for (int k = 0; k < 4; k++)
            {
                int dr = p.r - palette[k].r, dg = p.g - palette[k].g, db = p.b - palette[k].b;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = k; }
            }
            bits |= (uint)best << (i * 2);
        }
        outBuf[outOff + 4] = (byte)(bits & 0xFF);
        outBuf[outOff + 5] = (byte)((bits >> 8) & 0xFF);
        outBuf[outOff + 6] = (byte)((bits >> 16) & 0xFF);
        outBuf[outOff + 7] = (byte)((bits >> 24) & 0xFF);
    }

    private static byte[] EncodeDxt5(byte[] rgba, int w, int h)
    {
        int blocksX = (w + 3) / 4, blocksY = (h + 3) / 4;
        byte[] o = new byte[blocksX * blocksY * 16];
        int pos = 0;
        for (int by = 0; by < blocksY; by++)
            for (int bx = 0; bx < blocksX; bx++)
            {
                EncodeDxt5Block(rgba, w, h, bx * 4, by * 4, o, pos);
                pos += 16;
            }
        return o;
    }

    private static void EncodeDxt5Block(byte[] rgba, int w, int h, int x0, int y0, byte[] outBuf, int outOff)
    {
        // Gather pixels
        var px = new (byte r, byte g, byte b, byte a)[16];
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
            {
                int sx = Math.Min(x0 + x, w - 1);
                int sy = Math.Min(y0 + y, h - 1);
                int s = (sy * w + sx) * 4;
                px[y * 4 + x] = (rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
            }

        // ----- Alpha block: pick min/max alpha as endpoints (8-color mode, a0 > a1) -----
        byte a0 = 0, a1 = 255;
        foreach (var p in px) { if (p.a > a0) a0 = p.a; if (p.a < a1) a1 = p.a; }
        if (a0 == a1) { if (a0 < 255) a0++; else a1--; }
        if (a0 < a1) (a0, a1) = (a1, a0);
        var alpha = new byte[8];
        alpha[0] = a0; alpha[1] = a1;
        for (int i = 1; i <= 6; i++) alpha[i + 1] = (byte)(((7 - i) * a0 + i * a1) / 7);

        ulong abits = 0;
        for (int i = 0; i < 16; i++)
        {
            byte a = px[i].a;
            int best = 0, bestD = int.MaxValue;
            for (int k = 0; k < 8; k++)
            {
                int d = a - alpha[k]; if (d < 0) d = -d;
                if (d < bestD) { bestD = d; best = k; }
            }
            abits |= (ulong)best << (i * 3);
        }
        outBuf[outOff + 0] = a0;
        outBuf[outOff + 1] = a1;
        for (int i = 0; i < 6; i++) outBuf[outOff + 2 + i] = (byte)((abits >> (i * 8)) & 0xFF);

        // ----- Color block: same as DXT1 but always 4-color mode (c0 > c1) -----
        int rmin = 255, gmin = 255, bmin = 255, rmax = 0, gmax = 0, bmax = 0;
        foreach (var p in px)
        {
            if (p.r < rmin) rmin = p.r; if (p.r > rmax) rmax = p.r;
            if (p.g < gmin) gmin = p.g; if (p.g > gmax) gmax = p.g;
            if (p.b < bmin) bmin = p.b; if (p.b > bmax) bmax = p.b;
        }
        int c0 = ((rmax * 31 + 127) / 255 << 11) | ((gmax * 63 + 127) / 255 << 5) | ((bmax * 31 + 127) / 255);
        int c1 = ((rmin * 31 + 127) / 255 << 11) | ((gmin * 63 + 127) / 255 << 5) | ((bmin * 31 + 127) / 255);
        if (c0 == c1) { if (c0 > 0) c1 = c0 - 1; else c0 = 1; }
        if (c0 < c1) (c0, c1) = (c1, c0);

        var (b0e, g0e, r0e, _) = Rgb565(c0);
        var (b1e, g1e, r1e, _) = Rgb565(c1);
        var p2 = ((byte)((2 * r0e + r1e) / 3), (byte)((2 * g0e + g1e) / 3), (byte)((2 * b0e + b1e) / 3));
        var p3 = ((byte)((r0e + 2 * r1e) / 3), (byte)((g0e + 2 * g1e) / 3), (byte)((b0e + 2 * b1e) / 3));

        outBuf[outOff + 8] = (byte)(c0 & 0xFF);
        outBuf[outOff + 9] = (byte)((c0 >> 8) & 0xFF);
        outBuf[outOff + 10] = (byte)(c1 & 0xFF);
        outBuf[outOff + 11] = (byte)((c1 >> 8) & 0xFF);

        uint cbits = 0;
        for (int i = 0; i < 16; i++)
        {
            var p = px[i];
            int best = 0, bestD = int.MaxValue;
            (byte r, byte g, byte b)[] palette = { (r0e, g0e, b0e), (r1e, g1e, b1e), p2, p3 };
            for (int k = 0; k < 4; k++)
            {
                int dr = p.r - palette[k].r, dg = p.g - palette[k].g, db = p.b - palette[k].b;
                int d = dr * dr + dg * dg + db * db;
                if (d < bestD) { bestD = d; best = k; }
            }
            cbits |= (uint)best << (i * 2);
        }
        outBuf[outOff + 12] = (byte)(cbits & 0xFF);
        outBuf[outOff + 13] = (byte)((cbits >> 8) & 0xFF);
        outBuf[outOff + 14] = (byte)((cbits >> 16) & 0xFF);
        outBuf[outOff + 15] = (byte)((cbits >> 24) & 0xFF);
    }
}

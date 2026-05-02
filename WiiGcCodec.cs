using System;

namespace HVTTool;

// Decoders/encoders for Nintendo Wii / GameCube pixel formats.
// Ported from the ReverseBox Python implementation:
//   reversebox/image/swizzling/swizzle_gamecube.py
//   reversebox/image/decoders/n64_decoder_encoder.py
//   reversebox/image/image_decoder.py (RGB5A3, IA8, I8 pixel decoders)
public static class WiiGcCodec
{
    // ------------------------------------------------------------------
    // Tile/block sizes per bpp (used by the generic GC swizzle layout).
    // ------------------------------------------------------------------
    private static (int bw, int bh) BlockSize(int bpp) => bpp switch
    {
        4 => (8, 8),
        8 => (8, 4),
        16 => (4, 4),
        32 => (4, 4),
        _ => throw new NotSupportedException($"Unsupported bpp {bpp}")
    };

    // ------------------------------------------------------------------
    // Swizzle pixel offsets (mirror of Python get_pixel_offset_*)
    // ------------------------------------------------------------------
    private static int OffsetBpp32(int x, int y, int w)
    {
        int blocksX = (w + 3) >> 2;
        int xb = x >> 2, yb = y >> 2, xp = x & 3, yp = y & 3;
        return ((yb * blocksX + xb) << 6) + ((yp << 3) + (xp << 1));
    }
    private static int OffsetBpp16(int x, int y, int w)
    {
        int blocksX = (w + 3) >> 2;
        int xb = x >> 2, yb = y >> 2, xp = x & 3, yp = y & 3;
        return ((yb * blocksX + xb) << 5) + ((yp << 3) + (xp << 1));
    }
    private static int OffsetBpp8(int x, int y, int w)
    {
        int blocksX = (w + 7) >> 3;
        int xb = x >> 3, yb = y >> 2, xp = x & 7, yp = y & 3;
        return ((yb * blocksX + xb) << 5) + ((yp << 3) + xp);
    }
    private static int OffsetBpp4(int x, int y, int w)
    {
        int blocksX = (w + 7) >> 3;
        int xb = x >> 3, yb = y >> 3, xp = x & 7, yp = y & 7;
        return ((yb * blocksX + xb) << 5) + ((yp << 2) + (xp >> 1));
    }

    // ------------------------------------------------------------------
    // Main entry — decode HVT pixel buffer to BGRA32 (Bitmap-friendly).
    // ------------------------------------------------------------------
    public static byte[] DecodeToBgra(HvtFile file)
    {
        return file.Format switch
        {
            HvtFormat.CMPR => DecodeCmpr(file.PixelData, file.Width, file.Height),
            HvtFormat.RGBA32 => DecodeRgba32(file.PixelData, file.Width, file.Height),
            HvtFormat.RGB5A3 => DecodeUnswizzled16(file.PixelData, file.Width, file.Height, DecodeRgb5A3Pixel),
            HvtFormat.IA8 => DecodeUnswizzled16(file.PixelData, file.Width, file.Height, DecodeIa8Pixel),
            HvtFormat.I8 => DecodeUnswizzled8(file.PixelData, file.Width, file.Height, DecodeI8Pixel),
            HvtFormat.C8 => DecodeC8(file.PixelData, file.Palette, file.Width, file.Height),
            _ => throw new NotSupportedException($"Unsupported format: {file.FormatTag}")
        };
    }

    // ------------------------------------------------------------------
    // Encoders — RGBA32 input from caller (decoded PNG, R/G/B/A order)
    // produce the swizzled byte stream that goes back into the HVT.
    // ------------------------------------------------------------------
    public static byte[] EncodeFromRgba(HvtFile file, byte[] rgba)
    {
        return file.Format switch
        {
            HvtFormat.CMPR => EncodeCmpr(rgba, file.Width, file.Height),
            HvtFormat.RGBA32 => EncodeRgba32(rgba, file.Width, file.Height),
            HvtFormat.RGB5A3 => EncodeSwizzled16(rgba, file.Width, file.Height, EncodeRgb5A3Pixel),
            HvtFormat.IA8 => EncodeSwizzled16(rgba, file.Width, file.Height, EncodeIa8Pixel),
            HvtFormat.I8 => EncodeSwizzled8(rgba, file.Width, file.Height, EncodeI8Pixel),
            HvtFormat.C8 => EncodeC8(rgba, file.Palette, file.Width, file.Height),
            _ => throw new NotSupportedException($"Unsupported format: {file.FormatTag}")
        };
    }

    // ==================================================================
    // CMPR (S3TW) — DXT1-style with GC byte/bit ordering
    // ==================================================================
    private static byte[] DecodeCmpr(byte[] data, int width, int height)
    {
        int sw = (width + 7) & ~7;
        int sh = (height + 7) & ~7;
        byte[] outBgra = new byte[sw * sh * 4];
        int off = 0;

        for (int y = 0; y < sh; y += 8)
        {
            for (int x = 0; x < sw; x += 8)
            {
                for (int sy = 0; sy < 8; sy += 4)
                {
                    for (int sx = 0; sx < 8; sx += 4)
                    {
                        int c0 = (data[off] << 8) | data[off + 1];
                        int c1 = (data[off + 2] << 8) | data[off + 3];
                        off += 4;

                        var p0 = Rgb565ToRgba(c0);
                        var p1 = Rgb565ToRgba(c1);
                        byte[] p2 = new byte[4];
                        byte[] p3 = new byte[4];

                        if (c0 > c1)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                p2[i] = (byte)((2 * p0[i] + p1[i]) / 3);
                                p3[i] = (byte)((2 * p1[i] + p0[i]) / 3);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < 4; i++)
                                p2[i] = (byte)((p0[i] + p1[i]) >> 1);
                            // p3 stays (0,0,0,0)
                        }

                        for (int row = 0; row < 4; row++)
                        {
                            byte bits = data[off++];
                            for (int col = 0; col < 4; col++)
                            {
                                int idx = (bits >> (6 - (col * 2))) & 0x3;
                                byte[] src = idx switch { 0 => p0, 1 => p1, 2 => p2, _ => p3 };
                                int px = x + sx + col;
                                int py = y + sy + row;
                                int dst = (py * sw + px) * 4;
                                outBgra[dst + 0] = src[2]; // B
                                outBgra[dst + 1] = src[1]; // G
                                outBgra[dst + 2] = src[0]; // R
                                outBgra[dst + 3] = src[3]; // A
                            }
                        }
                    }
                }
            }
        }
        return Crop(outBgra, sw, sh, width, height);
    }

    private static byte[] EncodeCmpr(byte[] rgba, int width, int height)
    {
        int sw = (width + 7) & ~7;
        int sh = (height + 7) & ~7;
        // 8x8 GC super-block contains 4 sub-blocks of 4x4 (8 bytes each) = 32 bytes.
        int outSize = (sw / 8) * (sh / 8) * 32;
        byte[] outBuf = new byte[outSize];
        int wp = 0;

        for (int y = 0; y < sh; y += 8)
        {
            for (int x = 0; x < sw; x += 8)
            {
                for (int sy = 0; sy < 8; sy += 4)
                {
                    for (int sx = 0; sx < 8; sx += 4)
                    {
                        // Gather 4x4 block in RGBA order
                        var block = new (byte R, byte G, byte B, byte A)[16];
                        for (int dy = 0; dy < 4; dy++)
                        {
                            for (int dx = 0; dx < 4; dx++)
                            {
                                int px = x + sx + dx;
                                int py = y + sy + dy;
                                if (px < width && py < height)
                                {
                                    int srcIdx = (py * width + px) * 4;
                                    block[dy * 4 + dx] = (rgba[srcIdx], rgba[srcIdx + 1], rgba[srcIdx + 2], rgba[srcIdx + 3]);
                                }
                                else
                                {
                                    block[dy * 4 + dx] = (0, 0, 0, 0);
                                }
                            }
                        }

                        int opaque = 0;
                        foreach (var p in block) if (p.A >= 128) opaque++;
                        bool alphaMode = opaque != 16;

                        int c0, c1;
                        if (opaque == 0)
                        {
                            c0 = 0; c1 = 0;
                        }
                        else
                        {
                            // PCA-based endpoint selection.
                            // 1) Mean of opaque pixels.
                            double mR = 0, mG = 0, mB = 0;
                            foreach (var p in block)
                            {
                                if (p.A < 128) continue;
                                mR += p.R; mG += p.G; mB += p.B;
                            }
                            mR /= opaque; mG /= opaque; mB /= opaque;

                            // 2) Covariance matrix (3x3, symmetric).
                            double cxx = 0, cxy = 0, cxz = 0, cyy = 0, cyz = 0, czz = 0;
                            foreach (var p in block)
                            {
                                if (p.A < 128) continue;
                                double dr = p.R - mR, dg = p.G - mG, db = p.B - mB;
                                cxx += dr * dr; cyy += dg * dg; czz += db * db;
                                cxy += dr * dg; cxz += dr * db; cyz += dg * db;
                            }

                            // 3) Power iteration for the principal eigenvector.
                            double vx = cxx, vy = cxy, vz = cxz;
                            // Avoid all-zero start when one row is degenerate.
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

                            // 4) Project onto axis, find extremes.
                            double minProj = double.PositiveInfinity, maxProj = double.NegativeInfinity;
                            foreach (var p in block)
                            {
                                if (p.A < 128) continue;
                                double t = (p.R - mR) * vx + (p.G - mG) * vy + (p.B - mB) * vz;
                                if (t < minProj) minProj = t;
                                if (t > maxProj) maxProj = t;
                            }

                            int e0r = (int)Math.Round(Math.Clamp(mR + maxProj * vx, 0, 255));
                            int e0g = (int)Math.Round(Math.Clamp(mG + maxProj * vy, 0, 255));
                            int e0b = (int)Math.Round(Math.Clamp(mB + maxProj * vz, 0, 255));
                            int e1r = (int)Math.Round(Math.Clamp(mR + minProj * vx, 0, 255));
                            int e1g = (int)Math.Round(Math.Clamp(mG + minProj * vy, 0, 255));
                            int e1b = (int)Math.Round(Math.Clamp(mB + minProj * vz, 0, 255));

                            c0 = EncodeRgb565(e0r, e0g, e0b);
                            c1 = EncodeRgb565(e1r, e1g, e1b);

                            // In alpha mode the encoder needs c0 <= c1 so the 4th palette
                            // slot becomes the transparent index. In opaque mode we want
                            // c0 > c1 to use the standard 1/3, 2/3 interpolation.
                            if (alphaMode && c0 > c1) (c0, c1) = (c1, c0);
                            else if (!alphaMode && c0 == c1)
                            {
                                // Single-color block; force c0 > c1 so we stay in interpolation mode.
                                if (c0 > 0) c1 = c0 - 1;
                                else c0 = 1;
                            }
                            else if (!alphaMode && c0 < c1) (c0, c1) = (c1, c0);
                        }

                        var p0 = Rgb565ToRgba(c0);
                        var p1 = Rgb565ToRgba(c1);
                        byte[] p2 = new byte[4];
                        byte[] p3 = new byte[4];

                        if (c0 > c1)
                        {
                            for (int i = 0; i < 4; i++)
                            {
                                p2[i] = (byte)((2 * p0[i] + p1[i]) / 3);
                                p3[i] = (byte)((2 * p1[i] + p0[i]) / 3);
                            }
                        }
                        else
                        {
                            for (int i = 0; i < 4; i++)
                                p2[i] = (byte)((p0[i] + p1[i]) >> 1);
                            // p3 = transparent
                        }

                        // GC big-endian 565
                        outBuf[wp++] = (byte)((c0 >> 8) & 0xFF);
                        outBuf[wp++] = (byte)(c0 & 0xFF);
                        outBuf[wp++] = (byte)((c1 >> 8) & 0xFF);
                        outBuf[wp++] = (byte)(c1 & 0xFF);

                        bool transparentSlot = (!(c0 > c1));
                        for (int row = 0; row < 4; row++)
                        {
                            int bits = 0;
                            for (int col = 0; col < 4; col++)
                            {
                                var px = block[row * 4 + col];
                                int idx;
                                if (alphaMode && px.A < 128 && transparentSlot)
                                {
                                    idx = 3;
                                }
                                else
                                {
                                    int max = transparentSlot ? 2 : 3;
                                    int best = 0, bestD = int.MaxValue;
                                    byte[][] palette = { p0, p1, p2, p3 };
                                    for (int k = 0; k <= max; k++)
                                    {
                                        int dr = px.R - palette[k][0];
                                        int dg = px.G - palette[k][1];
                                        int db = px.B - palette[k][2];
                                        int d = dr * dr + dg * dg + db * db;
                                        if (d < bestD) { bestD = d; best = k; }
                                    }
                                    idx = best;
                                }
                                bits |= (idx & 0x3) << (6 - col * 2);
                            }
                            outBuf[wp++] = (byte)bits;
                        }
                    }
                }
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
    private static int EncodeRgb565(int r, int g, int b)
    {
        int r5 = (r * 31 + 127) / 255;
        int g6 = (g * 63 + 127) / 255;
        int b5 = (b * 31 + 127) / 255;
        return (r5 << 11) | (g6 << 5) | b5;
    }

    // ==================================================================
    // RGBA32 (ARGB) — 4x4 tile, AR pairs in first 32 bytes, GB in next 32
    // ==================================================================
    private static byte[] DecodeRgba32(byte[] data, int width, int height)
    {
        int sw = (width + 3) & ~3;
        int sh = (height + 3) & ~3;
        byte[] outBgra = new byte[sw * sh * 4];
        int off = 0;
        for (int y = 0; y < sh; y += 4)
        {
            for (int x = 0; x < sw; x += 4)
            {
                for (int dy = 0; dy < 4; dy++)
                {
                    for (int dx = 0; dx < 4; dx++)
                    {
                        int dst = ((y + dy) * sw + (x + dx)) * 4;
                        outBgra[dst + 2] = data[off + 1];      // R
                        outBgra[dst + 1] = data[off + 32];     // G
                        outBgra[dst + 0] = data[off + 33];     // B
                        outBgra[dst + 3] = data[off + 0];      // A
                        off += 2;
                    }
                }
                off += 32; // skip GB half consumed
            }
        }
        return Crop(outBgra, sw, sh, width, height);
    }

    private static byte[] EncodeRgba32(byte[] rgba, int width, int height)
    {
        int sw = (width + 3) & ~3;
        int sh = (height + 3) & ~3;
        byte[] outBuf = new byte[sw * sh * 4];
        int off = 0;
        for (int y = 0; y < sh; y += 4)
        {
            for (int x = 0; x < sw; x += 4)
            {
                for (int dy = 0; dy < 4; dy++)
                {
                    for (int dx = 0; dx < 4; dx++)
                    {
                        int px = x + dx;
                        int py = y + dy;
                        byte r = 0, g = 0, b = 0, a = 0;
                        if (px < width && py < height)
                        {
                            int src = (py * width + px) * 4;
                            r = rgba[src]; g = rgba[src + 1]; b = rgba[src + 2]; a = rgba[src + 3];
                        }
                        outBuf[off + 0] = a;
                        outBuf[off + 1] = r;
                        outBuf[off + 32] = g;
                        outBuf[off + 33] = b;
                        off += 2;
                    }
                }
                off += 32;
            }
        }
        return outBuf;
    }

    // ==================================================================
    // Generic swizzled formats — 16bpp (RGB5A3, IA8) and 8bpp (I8)
    // ==================================================================
    private delegate void Decode16Pixel(int pixel, byte[] outRgba, int outIndex);
    private delegate int Encode16Pixel(byte r, byte g, byte b, byte a);
    private delegate void Decode8Pixel(int pixel, byte[] outRgba, int outIndex);
    private delegate int Encode8Pixel(byte r, byte g, byte b, byte a);

    private static byte[] DecodeUnswizzled16(byte[] data, int width, int height, Decode16Pixel decoder)
    {
        var (bw, bh) = BlockSize(16);
        int sw = (width + bw - 1) / bw * bw;
        int sh = (height + bh - 1) / bh * bh;
        // Linear RGBA buffer for storage-sized image.
        byte[] linearRgba = new byte[sw * sh * 4];

        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                int srcOff = OffsetBpp16(x, y, sw);
                if (srcOff + 1 >= data.Length) continue;
                int pixel = (data[srcOff] << 8) | data[srcOff + 1];
                int dstIdx = (y * sw + x) * 4;
                decoder(pixel, linearRgba, dstIdx);
            }
        }
        return CropAsBgra(linearRgba, sw, sh, width, height);
    }

    private static byte[] EncodeSwizzled16(byte[] rgba, int width, int height, Encode16Pixel encoder)
    {
        var (bw, bh) = BlockSize(16);
        int sw = (width + bw - 1) / bw * bw;
        int sh = (height + bh - 1) / bh * bh;
        byte[] outBuf = new byte[sw * sh * 2];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                byte r = 0, g = 0, b = 0, a = 0;
                if (x < width && y < height)
                {
                    int s = (y * width + x) * 4;
                    r = rgba[s]; g = rgba[s + 1]; b = rgba[s + 2]; a = rgba[s + 3];
                }
                int p = encoder(r, g, b, a);
                int dst = OffsetBpp16(x, y, sw);
                outBuf[dst] = (byte)((p >> 8) & 0xFF);
                outBuf[dst + 1] = (byte)(p & 0xFF);
            }
        }
        return outBuf;
    }

    private static byte[] DecodeUnswizzled8(byte[] data, int width, int height, Decode8Pixel decoder)
    {
        var (bw, bh) = BlockSize(8);
        int sw = (width + bw - 1) / bw * bw;
        int sh = (height + bh - 1) / bh * bh;
        byte[] linearRgba = new byte[sw * sh * 4];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                int srcOff = OffsetBpp8(x, y, sw);
                if (srcOff >= data.Length) continue;
                int pixel = data[srcOff];
                int dstIdx = (y * sw + x) * 4;
                decoder(pixel, linearRgba, dstIdx);
            }
        }
        return CropAsBgra(linearRgba, sw, sh, width, height);
    }

    private static byte[] EncodeSwizzled8(byte[] rgba, int width, int height, Encode8Pixel encoder)
    {
        var (bw, bh) = BlockSize(8);
        int sw = (width + bw - 1) / bw * bw;
        int sh = (height + bh - 1) / bh * bh;
        byte[] outBuf = new byte[sw * sh];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                byte r = 0, g = 0, b = 0, a = 0;
                if (x < width && y < height)
                {
                    int s = (y * width + x) * 4;
                    r = rgba[s]; g = rgba[s + 1]; b = rgba[s + 2]; a = rgba[s + 3];
                }
                outBuf[OffsetBpp8(x, y, sw)] = (byte)encoder(r, g, b, a);
            }
        }
        return outBuf;
    }

    // ----- Pixel decoders -----
    // Wii/GameCube RGB5A3 hardware layout (big-endian u16):
    //   MSB=1: 1 RRRRR GGGGG BBBBB        (opaque, R in bits 14-10)
    //   MSB=0: 0 AAA RRRR GGGG BBBB       (translucent, R in bits 11-8)
    private static void DecodeRgb5A3Pixel(int p, byte[] outRgba, int idx)
    {
        if ((p & 0x8000) != 0)
        {
            outRgba[idx + 0] = (byte)(((p >> 10) & 0x1F) * 255 / 31); // R
            outRgba[idx + 1] = (byte)(((p >> 5) & 0x1F) * 255 / 31);  // G
            outRgba[idx + 2] = (byte)(((p >> 0) & 0x1F) * 255 / 31);  // B
            outRgba[idx + 3] = 0xFF;
        }
        else
        {
            outRgba[idx + 0] = (byte)(((p >> 8) & 0x0F) * 255 / 15);  // R
            outRgba[idx + 1] = (byte)(((p >> 4) & 0x0F) * 255 / 15);  // G
            outRgba[idx + 2] = (byte)(((p >> 0) & 0x0F) * 255 / 15);  // B
            outRgba[idx + 3] = (byte)(((p >> 12) & 0x07) * 255 / 7);  // A
        }
    }
    private static int EncodeRgb5A3Pixel(byte r, byte g, byte b, byte a)
    {
        if (a >= 0xFF - 8) // fully opaque -> RGB555 with MSB=1
        {
            int r5 = (r * 31) / 255;
            int g5 = (g * 31) / 255;
            int b5 = (b * 31) / 255;
            return 0x8000 | (r5 << 10) | (g5 << 5) | b5;
        }
        else
        {
            int r4 = (r * 15) / 255;
            int g4 = (g * 15) / 255;
            int b4 = (b * 15) / 255;
            int a3 = (a * 7) / 255;
            return (a3 << 12) | (r4 << 8) | (g4 << 4) | b4;
        }
    }

    private static void DecodeIa8Pixel(int p, byte[] outRgba, int idx)
    {
        // ReverseBox: p[0..2] = pixel & 0xFF, p[3] = pixel >> 8
        byte i = (byte)(p & 0xFF);
        byte a = (byte)((p >> 8) & 0xFF);
        outRgba[idx + 0] = i;
        outRgba[idx + 1] = i;
        outRgba[idx + 2] = i;
        outRgba[idx + 3] = a;
    }
    private static int EncodeIa8Pixel(byte r, byte g, byte b, byte a)
    {
        int i = (r + g + b) / 3;
        return (a << 8) | (i & 0xFF);
    }

    private static void DecodeI8Pixel(int p, byte[] outRgba, int idx)
    {
        byte v = (byte)(p & 0xFF);
        outRgba[idx + 0] = v;
        outRgba[idx + 1] = v;
        outRgba[idx + 2] = v;
        outRgba[idx + 3] = 0xFF;
    }
    private static int EncodeI8Pixel(byte r, byte g, byte b, byte a)
    {
        return (r + g + b) / 3;
    }

    // ==================================================================
    // P8WI — 8bpp paletted, 256-entry RGB5A3 BE palette
    // ==================================================================
    private static byte[] DecodeC8(byte[] data, byte[] palette, int width, int height)
    {
        var (bw, bh) = BlockSize(8);
        int sw = (width + bw - 1) / bw * bw;
        int sh = (height + bh - 1) / bh * bh;

        // Build palette as RGBA
        var palRgba = new byte[256 * 4];
        if (palette.Length >= 512)
        {
            for (int i = 0; i < 256; i++)
            {
                int p = (palette[i * 2] << 8) | palette[i * 2 + 1];
                DecodeRgb5A3Pixel(p, palRgba, i * 4);
            }
        }
        else
        {
            // No palette → grayscale fallback
            for (int i = 0; i < 256; i++)
            {
                palRgba[i * 4 + 0] = (byte)i;
                palRgba[i * 4 + 1] = (byte)i;
                palRgba[i * 4 + 2] = (byte)i;
                palRgba[i * 4 + 3] = 0xFF;
            }
        }

        byte[] linearRgba = new byte[sw * sh * 4];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                int srcOff = OffsetBpp8(x, y, sw);
                if (srcOff >= data.Length) continue;
                int idx = data[srcOff];
                int dst = (y * sw + x) * 4;
                int pal = idx * 4;
                linearRgba[dst + 0] = palRgba[pal + 0];
                linearRgba[dst + 1] = palRgba[pal + 1];
                linearRgba[dst + 2] = palRgba[pal + 2];
                linearRgba[dst + 3] = palRgba[pal + 3];
            }
        }
        return CropAsBgra(linearRgba, sw, sh, width, height);
    }

    private static byte[] EncodeC8(byte[] rgba, byte[] palette, int width, int height)
    {
        // Re-encode using nearest-match against the existing palette.
        var (bw, bh) = BlockSize(8);
        int sw = (width + bw - 1) / bw * bw;
        int sh = (height + bh - 1) / bh * bh;

        var palRgba = new byte[256 * 4];
        if (palette.Length >= 512)
        {
            for (int i = 0; i < 256; i++)
            {
                int p = (palette[i * 2] << 8) | palette[i * 2 + 1];
                DecodeRgb5A3Pixel(p, palRgba, i * 4);
            }
        }

        byte[] outBuf = new byte[sw * sh];
        for (int y = 0; y < sh; y++)
        {
            for (int x = 0; x < sw; x++)
            {
                byte r = 0, g = 0, b = 0, a = 0;
                if (x < width && y < height)
                {
                    int s = (y * width + x) * 4;
                    r = rgba[s]; g = rgba[s + 1]; b = rgba[s + 2]; a = rgba[s + 3];
                }
                int best = 0;
                int bestD = int.MaxValue;
                for (int i = 0; i < 256; i++)
                {
                    int pi = i * 4;
                    int dr = r - palRgba[pi];
                    int dg = g - palRgba[pi + 1];
                    int db = b - palRgba[pi + 2];
                    int da = a - palRgba[pi + 3];
                    int d = dr * dr + dg * dg + db * db + da * da;
                    if (d < bestD) { bestD = d; best = i; }
                }
                outBuf[OffsetBpp8(x, y, sw)] = (byte)best;
            }
        }
        return outBuf;
    }

    // ==================================================================
    // Helpers
    // ==================================================================
    // Crop a stored RGBA buffer to the displayed size and convert to BGRA.
    private static byte[] CropAsBgra(byte[] rgbaStored, int sw, int sh, int dw, int dh)
    {
        byte[] outBgra = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
        {
            for (int x = 0; x < dw; x++)
            {
                int s = (y * sw + x) * 4;
                int d = (y * dw + x) * 4;
                outBgra[d + 0] = rgbaStored[s + 2]; // B
                outBgra[d + 1] = rgbaStored[s + 1]; // G
                outBgra[d + 2] = rgbaStored[s + 0]; // R
                outBgra[d + 3] = rgbaStored[s + 3]; // A
            }
        }
        return outBgra;
    }

    // Crop an already-BGRA buffer (used by CMPR/RGBA32 paths).
    private static byte[] Crop(byte[] bgraStored, int sw, int sh, int dw, int dh)
    {
        if (sw == dw && sh == dh) return bgraStored;
        byte[] outBgra = new byte[dw * dh * 4];
        for (int y = 0; y < dh; y++)
            Buffer.BlockCopy(bgraStored, (y * sw) * 4, outBgra, (y * dw) * 4, dw * 4);
        return outBgra;
    }
}

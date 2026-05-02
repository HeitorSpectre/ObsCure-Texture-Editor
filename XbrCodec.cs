using System;

namespace HVTTool;

// Xbox NV2A texture decoder/encoder.
// Swizzled (SZ_*) formats use Morton/Z-order: bits of x and y coordinates are
// interleaved to map (x, y) -> linear offset. The classic algorithm walks the
// power-of-2 ladders for both axes and pulls one bit at a time.
public static class XbrCodec
{
    public static byte[] DecodeToBgra(XbrTexture t)
    {
        byte[] raw = new byte[Math.Min(t.ImageSize, t.Owner.Data.Length - t.ImageOffset)];
        Buffer.BlockCopy(t.Owner.Data, t.ImageOffset, raw, 0, raw.Length);

        int bpp = t.Bpp;
        int w = t.Width;
        int h = t.Height;

        return t.Format switch
        {
            XbrColorFormat.SZ_R5G6B5 => DecodeSwizzled16(raw, w, h, R5G6B5_ToBgra),
            XbrColorFormat.SZ_A1R5G5B5 => DecodeSwizzled16(raw, w, h, A1R5G5B5_ToBgra),
            XbrColorFormat.SZ_X1R5G5B5 => DecodeSwizzled16(raw, w, h, X1R5G5B5_ToBgra),
            XbrColorFormat.SZ_A4R4G4B4 => DecodeSwizzled16(raw, w, h, A4R4G4B4_ToBgra),
            XbrColorFormat.SZ_A8R8G8B8 => DecodeSwizzled32(raw, w, h, A8R8G8B8_ToBgra),
            XbrColorFormat.SZ_X8R8G8B8 => DecodeSwizzled32(raw, w, h, X8R8G8B8_ToBgra),
            XbrColorFormat.LU_R5G6B5 => DecodeLinear16(raw, w, h, R5G6B5_ToBgra),
            XbrColorFormat.LU_A8R8G8B8 => DecodeLinear32(raw, w, h, A8R8G8B8_ToBgra),
            XbrColorFormat.LU_X8R8G8B8 => DecodeLinear32(raw, w, h, X8R8G8B8_ToBgra),
            _ => throw new NotSupportedException($"Xbox format {t.Format} (0x{(int)t.Format:X2}) not supported.")
        };
    }

    // Encoder returns ONLY the level-0 mipmap. The container reinsert will
    // overwrite that prefix and leave subsequent mip levels untouched.
    public static byte[] EncodeFromRgba(XbrTexture t, byte[] rgba)
    {
        int w = t.Width;
        int h = t.Height;

        return t.Format switch
        {
            XbrColorFormat.SZ_R5G6B5 => EncodeSwizzled16(rgba, w, h, Rgba_To_R5G6B5),
            XbrColorFormat.SZ_A1R5G5B5 => EncodeSwizzled16(rgba, w, h, Rgba_To_A1R5G5B5),
            XbrColorFormat.SZ_X1R5G5B5 => EncodeSwizzled16(rgba, w, h, Rgba_To_X1R5G5B5),
            XbrColorFormat.SZ_A4R4G4B4 => EncodeSwizzled16(rgba, w, h, Rgba_To_A4R4G4B4),
            XbrColorFormat.SZ_A8R8G8B8 => EncodeSwizzled32(rgba, w, h, Rgba_To_A8R8G8B8),
            XbrColorFormat.SZ_X8R8G8B8 => EncodeSwizzled32(rgba, w, h, Rgba_To_X8R8G8B8),
            XbrColorFormat.LU_R5G6B5 => EncodeLinear16(rgba, w, h, Rgba_To_R5G6B5),
            XbrColorFormat.LU_A8R8G8B8 => EncodeLinear32(rgba, w, h, Rgba_To_A8R8G8B8),
            XbrColorFormat.LU_X8R8G8B8 => EncodeLinear32(rgba, w, h, Rgba_To_X8R8G8B8),
            _ => throw new NotSupportedException($"Xbox format {t.Format} (0x{(int)t.Format:X2}) not supported.")
        };
    }

    // ==================================================================
    // Xbox swizzle: interleave low bits of x and y up to log2(min(w,h)).
    // For non-square textures, the remaining high bits of the longer side
    // append after the interleaved part.
    // ==================================================================
    // Walk one bit at a time through x and y, placing each into the next free
    // position of the destination offset. Equivalent to Morton order for square
    // power-of-2 textures; for non-square textures the longer-axis high bits
    // tail straight onto the top of the offset (matches XQEMU/NV2A behaviour).
    private static int SwizzleOffset(int x, int y, int w, int h)
    {
        int offset = 0;
        int xs = 0, ys = 0, dest = 0;
        while ((1 << xs) < w || (1 << ys) < h)
        {
            if ((1 << xs) < w)
            {
                offset |= ((x >> xs) & 1) << dest;
                xs++; dest++;
            }
            if ((1 << ys) < h)
            {
                offset |= ((y >> ys) & 1) << dest;
                ys++; dest++;
            }
        }
        return offset;
    }

    private delegate void Decode16(int v, byte[] dst, int idx);
    private delegate void Decode32(uint v, byte[] dst, int idx);
    private delegate int Encode16(byte r, byte g, byte b, byte a);
    private delegate uint Encode32(byte r, byte g, byte b, byte a);

    private static byte[] DecodeSwizzled16(byte[] raw, int w, int h, Decode16 decoder)
    {
        byte[] o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = SwizzleOffset(x, y, w, h) * 2;
                if (s + 1 >= raw.Length) continue;
                int v = raw[s] | (raw[s + 1] << 8);
                decoder(v, o, (y * w + x) * 4);
            }
        return o;
    }

    private static byte[] DecodeSwizzled32(byte[] raw, int w, int h, Decode32 decoder)
    {
        byte[] o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = SwizzleOffset(x, y, w, h) * 4;
                if (s + 3 >= raw.Length) continue;
                uint v = (uint)(raw[s] | (raw[s + 1] << 8) | (raw[s + 2] << 16) | (raw[s + 3] << 24));
                decoder(v, o, (y * w + x) * 4);
            }
        return o;
    }

    private static byte[] DecodeLinear16(byte[] raw, int w, int h, Decode16 decoder)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            if (i * 2 + 1 >= raw.Length) break;
            int v = raw[i * 2] | (raw[i * 2 + 1] << 8);
            decoder(v, o, i * 4);
        }
        return o;
    }

    private static byte[] DecodeLinear32(byte[] raw, int w, int h, Decode32 decoder)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            if (i * 4 + 3 >= raw.Length) break;
            uint v = (uint)(raw[i * 4] | (raw[i * 4 + 1] << 8) | (raw[i * 4 + 2] << 16) | (raw[i * 4 + 3] << 24));
            decoder(v, o, i * 4);
        }
        return o;
    }

    private static byte[] EncodeSwizzled16(byte[] rgba, int w, int h, Encode16 encoder)
    {
        byte[] o = new byte[w * h * 2];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4;
                int v = encoder(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
                int d = SwizzleOffset(x, y, w, h) * 2;
                o[d] = (byte)(v & 0xFF);
                o[d + 1] = (byte)((v >> 8) & 0xFF);
            }
        return o;
    }

    private static byte[] EncodeSwizzled32(byte[] rgba, int w, int h, Encode32 encoder)
    {
        byte[] o = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int s = (y * w + x) * 4;
                uint v = encoder(rgba[s], rgba[s + 1], rgba[s + 2], rgba[s + 3]);
                int d = SwizzleOffset(x, y, w, h) * 4;
                o[d + 0] = (byte)(v & 0xFF);
                o[d + 1] = (byte)((v >> 8) & 0xFF);
                o[d + 2] = (byte)((v >> 16) & 0xFF);
                o[d + 3] = (byte)((v >> 24) & 0xFF);
            }
        return o;
    }

    private static byte[] EncodeLinear16(byte[] rgba, int w, int h, Encode16 encoder)
    {
        byte[] o = new byte[w * h * 2];
        for (int i = 0; i < w * h; i++)
        {
            int v = encoder(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            o[i * 2] = (byte)(v & 0xFF); o[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
        }
        return o;
    }

    private static byte[] EncodeLinear32(byte[] rgba, int w, int h, Encode32 encoder)
    {
        byte[] o = new byte[w * h * 4];
        for (int i = 0; i < w * h; i++)
        {
            uint v = encoder(rgba[i * 4], rgba[i * 4 + 1], rgba[i * 4 + 2], rgba[i * 4 + 3]);
            o[i * 4 + 0] = (byte)(v & 0xFF);
            o[i * 4 + 1] = (byte)((v >> 8) & 0xFF);
            o[i * 4 + 2] = (byte)((v >> 16) & 0xFF);
            o[i * 4 + 3] = (byte)((v >> 24) & 0xFF);
        }
        return o;
    }

    // ==================================================================
    // Pixel format converters (D3D-style: A8R8G8B8 = bytes B,G,R,A in memory)
    // ==================================================================
    private static void R5G6B5_ToBgra(int v, byte[] o, int idx)
    {
        // 16-bit value: R(5)G(6)B(5)
        o[idx + 0] = (byte)((v & 0x1F) * 255 / 31);
        o[idx + 1] = (byte)(((v >> 5) & 0x3F) * 255 / 63);
        o[idx + 2] = (byte)(((v >> 11) & 0x1F) * 255 / 31);
        o[idx + 3] = 0xFF;
    }
    private static int Rgba_To_R5G6B5(byte r, byte g, byte b, byte a) =>
        ((r * 31 + 127) / 255 << 11) | ((g * 63 + 127) / 255 << 5) | ((b * 31 + 127) / 255);

    private static void A1R5G5B5_ToBgra(int v, byte[] o, int idx)
    {
        // 16-bit value: A(1)R(5)G(5)B(5)
        o[idx + 0] = (byte)((v & 0x1F) * 255 / 31);
        o[idx + 1] = (byte)(((v >> 5) & 0x1F) * 255 / 31);
        o[idx + 2] = (byte)(((v >> 10) & 0x1F) * 255 / 31);
        o[idx + 3] = (byte)((v & 0x8000) != 0 ? 255 : 0);
    }
    private static int Rgba_To_A1R5G5B5(byte r, byte g, byte b, byte a) =>
        ((a >= 128 ? 1 : 0) << 15) | ((r * 31 + 127) / 255 << 10) | ((g * 31 + 127) / 255 << 5) | ((b * 31 + 127) / 255);

    private static void X1R5G5B5_ToBgra(int v, byte[] o, int idx)
    {
        o[idx + 0] = (byte)((v & 0x1F) * 255 / 31);
        o[idx + 1] = (byte)(((v >> 5) & 0x1F) * 255 / 31);
        o[idx + 2] = (byte)(((v >> 10) & 0x1F) * 255 / 31);
        o[idx + 3] = 0xFF;
    }
    private static int Rgba_To_X1R5G5B5(byte r, byte g, byte b, byte a) =>
        ((r * 31 + 127) / 255 << 10) | ((g * 31 + 127) / 255 << 5) | ((b * 31 + 127) / 255);

    private static void A4R4G4B4_ToBgra(int v, byte[] o, int idx)
    {
        o[idx + 0] = (byte)((v & 0x0F) * 255 / 15);
        o[idx + 1] = (byte)(((v >> 4) & 0x0F) * 255 / 15);
        o[idx + 2] = (byte)(((v >> 8) & 0x0F) * 255 / 15);
        o[idx + 3] = (byte)(((v >> 12) & 0x0F) * 255 / 15);
    }
    private static int Rgba_To_A4R4G4B4(byte r, byte g, byte b, byte a) =>
        ((a * 15 + 127) / 255 << 12) | ((r * 15 + 127) / 255 << 8) | ((g * 15 + 127) / 255 << 4) | ((b * 15 + 127) / 255);

    private static void A8R8G8B8_ToBgra(uint v, byte[] o, int idx)
    {
        // 32-bit LE value: bits 31..0 = A,R,G,B → bytes in memory: B,G,R,A
        o[idx + 0] = (byte)(v & 0xFF);
        o[idx + 1] = (byte)((v >> 8) & 0xFF);
        o[idx + 2] = (byte)((v >> 16) & 0xFF);
        o[idx + 3] = (byte)((v >> 24) & 0xFF);
    }
    private static uint Rgba_To_A8R8G8B8(byte r, byte g, byte b, byte a) =>
        ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;

    private static void X8R8G8B8_ToBgra(uint v, byte[] o, int idx)
    {
        o[idx + 0] = (byte)(v & 0xFF);
        o[idx + 1] = (byte)((v >> 8) & 0xFF);
        o[idx + 2] = (byte)((v >> 16) & 0xFF);
        o[idx + 3] = 0xFF;
    }
    private static uint Rgba_To_X8R8G8B8(byte r, byte g, byte b, byte a) =>
        ((uint)r << 16) | ((uint)g << 8) | b;
}

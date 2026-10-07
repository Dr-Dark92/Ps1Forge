using System.Buffers.Binary;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace Ps1Forge.Core;

/// <summary>
/// Minimal legacy DDS/DXT1 (BC1) encoder used for PS4 sce_sys artwork.
/// Emits the 128-byte legacy DDS header followed by one DXT1 surface,
/// with no mipmaps. This deliberately avoids a DX10 extended header.
/// </summary>
public static class DdsDxt1Encoder
{
    public static byte[] Encode(byte[] imageBytes, int width, int height)
    {
        if (width <= 0 || height <= 0 || (width & 3) != 0 || (height & 3) != 0)
            throw new ArgumentOutOfRangeException(nameof(width), "DXT1 dimensions must be positive multiples of four.");

        using var input = new MemoryStream(imageBytes, writable: false);
        using var source = Image.FromStream(input);
        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CompositingMode = CompositingMode.SourceCopy;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.DrawImage(source, new Rectangle(0, 0, width, height));
        }

        var payloadSize = checked((width / 4) * (height / 4) * 8);
        var output = new byte[128 + payloadSize];
        WriteHeader(output, width, height, payloadSize);

        var rect = new Rectangle(0, 0, width, height);
        var bits = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            unsafe
            {
                var basePtr = (byte*)bits.Scan0;
                var dst = 128;
                Span<Rgba> block = stackalloc Rgba[16];

                for (var by = 0; by < height; by += 4)
                {
                    for (var bx = 0; bx < width; bx += 4)
                    {
                        for (var py = 0; py < 4; py++)
                        {
                            var row = basePtr + ((by + py) * bits.Stride);
                            for (var px = 0; px < 4; px++)
                            {
                                var p = row + ((bx + px) * 4);
                                block[(py * 4) + px] = new Rgba(p[2], p[1], p[0], p[3]);
                            }
                        }

                        EncodeBlock(block, output.AsSpan(dst, 8));
                        dst += 8;
                    }
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(bits);
        }

        return output;
    }

    private static void WriteHeader(Span<byte> dds, int width, int height, int linearSize)
    {
        // DDS magic + DDS_HEADER (124 bytes).
        BinaryPrimitives.WriteUInt32LittleEndian(dds[0..4], 0x20534444); // "DDS "
        BinaryPrimitives.WriteUInt32LittleEndian(dds[4..8], 124);
        BinaryPrimitives.WriteUInt32LittleEndian(dds[8..12], 0x00081007); // CAPS|HEIGHT|WIDTH|PIXELFORMAT|LINEARSIZE
        BinaryPrimitives.WriteUInt32LittleEndian(dds[12..16], checked((uint)height));
        BinaryPrimitives.WriteUInt32LittleEndian(dds[16..20], checked((uint)width));
        BinaryPrimitives.WriteUInt32LittleEndian(dds[20..24], checked((uint)linearSize));
        BinaryPrimitives.WriteUInt32LittleEndian(dds[76..80], 32);       // DDS_PIXELFORMAT size
        BinaryPrimitives.WriteUInt32LittleEndian(dds[80..84], 0x4);      // DDPF_FOURCC
        BinaryPrimitives.WriteUInt32LittleEndian(dds[84..88], 0x31545844); // "DXT1"
        BinaryPrimitives.WriteUInt32LittleEndian(dds[108..112], 0x1000); // DDSCAPS_TEXTURE
    }

    private static void EncodeBlock(ReadOnlySpan<Rgba> pixels, Span<byte> output)
    {
        var hasTransparent = false;
        var min = new Rgba(255, 255, 255, 255);
        var max = new Rgba(0, 0, 0, 255);
        var minLum = int.MaxValue;
        var maxLum = int.MinValue;

        foreach (var p in pixels)
        {
            if (p.A < 128)
            {
                hasTransparent = true;
                continue;
            }

            var lum = (p.R * 299) + (p.G * 587) + (p.B * 114);
            if (lum < minLum) { minLum = lum; min = p; }
            if (lum > maxLum) { maxLum = lum; max = p; }
        }

        if (minLum == int.MaxValue)
        {
            BinaryPrimitives.WriteUInt64LittleEndian(output, 0);
            return;
        }

        var cMin = To565(min);
        var cMax = To565(max);

        ushort c0;
        ushort c1;
        if (hasTransparent)
        {
            c0 = Math.Min(cMin, cMax);
            c1 = Math.Max(cMin, cMax);
        }
        else
        {
            c0 = Math.Max(cMin, cMax);
            c1 = Math.Min(cMin, cMax);
            if (c0 == c1)
            {
                if (c0 < ushort.MaxValue) c0++;
                else if (c1 > 0) c1--;
            }
        }

        BinaryPrimitives.WriteUInt16LittleEndian(output[0..2], c0);
        BinaryPrimitives.WriteUInt16LittleEndian(output[2..4], c1);

        Span<Rgba> palette = stackalloc Rgba[4];
        palette[0] = From565(c0);
        palette[1] = From565(c1);
        if (c0 > c1)
        {
            palette[2] = Mix(palette[0], palette[1], 2, 1, 3);
            palette[3] = Mix(palette[0], palette[1], 1, 2, 3);
        }
        else
        {
            palette[2] = Mix(palette[0], palette[1], 1, 1, 2);
            palette[3] = new Rgba(0, 0, 0, 0);
        }

        uint indices = 0;
        for (var i = 0; i < 16; i++)
        {
            var p = pixels[i];
            var best = 0;
            if (hasTransparent && p.A < 128)
            {
                best = 3;
            }
            else
            {
                var bestDistance = int.MaxValue;
                var limit = c0 > c1 ? 4 : 3;
                for (var j = 0; j < limit; j++)
                {
                    var dr = p.R - palette[j].R;
                    var dg = p.G - palette[j].G;
                    var db = p.B - palette[j].B;
                    var distance = (dr * dr) + (dg * dg) + (db * db);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = j;
                    }
                }
            }
            indices |= checked((uint)best) << (i * 2);
        }

        BinaryPrimitives.WriteUInt32LittleEndian(output[4..8], indices);
    }

    private static ushort To565(Rgba p)
        => checked((ushort)(((p.R >> 3) << 11) | ((p.G >> 2) << 5) | (p.B >> 3)));

    private static Rgba From565(ushort c)
    {
        var r5 = (c >> 11) & 31;
        var g6 = (c >> 5) & 63;
        var b5 = c & 31;
        return new Rgba(
            (byte)((r5 << 3) | (r5 >> 2)),
            (byte)((g6 << 2) | (g6 >> 4)),
            (byte)((b5 << 3) | (b5 >> 2)),
            255);
    }

    private static Rgba Mix(Rgba a, Rgba b, int aw, int bw, int divisor)
        => new(
            (byte)(((a.R * aw) + (b.R * bw)) / divisor),
            (byte)(((a.G * aw) + (b.G * bw)) / divisor),
            (byte)(((a.B * aw) + (b.B * bw)) / divisor),
            255);

    private readonly record struct Rgba(byte R, byte G, byte B, byte A);
}

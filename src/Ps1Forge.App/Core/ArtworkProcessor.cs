using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.IO.Compression;

namespace Ps1Forge.Core;

public static class ArtworkProcessor
{
    public static string CreateIcon(string sourcePath, string stagingDirectory)
    {
        if (!File.Exists(sourcePath))
            throw new FileNotFoundException("Artwork file was not found.", sourcePath);

        Directory.CreateDirectory(stagingDirectory);
        var output = Path.Combine(stagingDirectory, "icon0.png");

        using var source = Image.FromFile(sourcePath);
        using var canvas = new Bitmap(512, 512, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(canvas))
        {
            graphics.Clear(Color.Black);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.SmoothingMode = SmoothingMode.HighQuality;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            var scale = Math.Max(512d / source.Width, 512d / source.Height);
            var width = (int)Math.Ceiling(source.Width * scale);
            var height = (int)Math.Ceiling(source.Height * scale);
            var x = (512 - width) / 2;
            var y = (512 - height) / 2;
            graphics.DrawImage(source, x, y, width, height);
        }

        // System.Drawing's PNG encoder produced ~782 KiB for otherwise simple
        // 512x512 artwork. Write a deterministic RGB PNG with PNG filtering +
        // zlib compression so package metadata stays compact.
        WriteIndexedPng(canvas, output);
        return output;
    }

    private static void WriteIndexedPng(Bitmap bitmap, string output)
    {
        const int width = 512, height = 512;
        var raw = new byte[height * (1 + width)];
        var rect = new Rectangle(0, 0, width, height);
        var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
        try
        {
            var stride = Math.Abs(data.Stride);
            var bgr = new byte[stride];
            byte[]? previous = null;
            var dst = 0;
            for (var y = 0; y < height; y++)
            {
                var srcPtr = data.Stride > 0
                    ? IntPtr.Add(data.Scan0, y * data.Stride)
                    : IntPtr.Add(data.Scan0, (height - 1 - y) * stride);
                Marshal.Copy(srcPtr, bgr, 0, stride);

                var indices = new byte[width];
                for (var x = 0; x < width; x++)
                {
                    var b = bgr[x * 3];
                    var g = bgr[x * 3 + 1];
                    var r = bgr[x * 3 + 2];
                    // Deterministic RGB332 palette: 3 red bits, 3 green, 2 blue.
                    indices[x] = (byte)((r & 0xE0) | ((g & 0xE0) >> 3) | (b >> 6));
                }

                var best = FilterIndexedRow(indices, previous, 0);
                for (byte filter = 1; filter <= 4; filter++)
                {
                    var candidate = FilterIndexedRow(indices, previous, filter);
                    if (candidate.Score < best.Score) best = candidate;
                }

                raw[dst++] = best.Type;
                Buffer.BlockCopy(best.Bytes, 0, raw, dst, width);
                dst += width;
                previous = indices;
            }
        }
        finally { bitmap.UnlockBits(data); }

        using var fs = File.Create(output);
        fs.Write(new byte[] { 137,80,78,71,13,10,26,10 });
        WriteChunk(fs, "IHDR", BuildIhdr(width, height, 3));
        WriteChunk(fs, "PLTE", BuildRgb332Palette());

        byte[] compressed;
        using (var ms = new MemoryStream())
        {
            using (var z = new ZLibStream(ms, CompressionLevel.SmallestSize, true))
                z.Write(raw);
            compressed = ms.ToArray();
        }
        WriteChunk(fs, "IDAT", compressed);
        WriteChunk(fs, "IEND", Array.Empty<byte>());
    }

    private static byte[] BuildRgb332Palette()
    {
        var palette = new byte[256 * 3];
        for (var i = 0; i < 256; i++)
        {
            var r3 = (i >> 5) & 7;
            var g3 = (i >> 2) & 7;
            var b2 = i & 3;
            palette[i * 3] = (byte)((r3 * 255 + 3) / 7);
            palette[i * 3 + 1] = (byte)((g3 * 255 + 3) / 7);
            palette[i * 3 + 2] = (byte)((b2 * 255 + 1) / 3);
        }
        return palette;
    }

    private static (byte Type, byte[] Bytes, long Score) FilterIndexedRow(byte[] row, byte[]? previous, byte type)
    {
        var result = new byte[row.Length];
        long score = 0;
        for (var x = 0; x < row.Length; x++)
        {
            var value = row[x];
            var left = x == 0 ? 0 : row[x - 1];
            var up = previous is null ? 0 : previous[x];
            var upLeft = x == 0 || previous is null ? 0 : previous[x - 1];
            var predictor = type switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => 0
            };
            var residual = unchecked((byte)(value - predictor));
            result[x] = residual;
            score += Math.Abs((int)(sbyte)residual);
        }
        return (type, result, score);
    }

    private static int Paeth(int a, int b, int c)
    {
        var p = a + b - c;
        var pa = Math.Abs(p - a);
        var pb = Math.Abs(p - b);
        var pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static byte[] BuildIhdr(int width, int height, byte colorType = 2)
    {
        var b = new byte[13];
        WriteBe32(b, 0, (uint)width); WriteBe32(b, 4, (uint)height);
        b[8] = 8; b[9] = colorType;
        return b;
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        Span<byte> length = stackalloc byte[4];
        WriteBe32(length, 0, (uint)data.Length);
        stream.Write(length);
        var typeBytes = System.Text.Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        var crcInput = new byte[4 + data.Length];
        Buffer.BlockCopy(typeBytes, 0, crcInput, 0, 4);
        Buffer.BlockCopy(data, 0, crcInput, 4, data.Length);
        Span<byte> crc = stackalloc byte[4];
        WriteBe32(crc, 0, Crc32(crcInput));
        stream.Write(crc);
    }

    private static void WriteBe32(Span<byte> b, int offset, uint value)
    {
        b[offset] = (byte)(value >> 24); b[offset + 1] = (byte)(value >> 16);
        b[offset + 2] = (byte)(value >> 8); b[offset + 3] = (byte)value;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
                crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        }
        return ~crc;
    }
}

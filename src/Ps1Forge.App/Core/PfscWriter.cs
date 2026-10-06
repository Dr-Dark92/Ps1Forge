using System.Buffers.Binary;
using System.IO.Compression;

namespace Ps1Forge.Core;

/// <summary>
/// PFSC wrapper for a completed inner PFS image. Uses 64 KiB logical blocks
/// and a seek table compatible with the PS4 PFSC container layout.
/// </summary>
public static class PfscWriter
{
    public const int BlockSize = 0x10000;

    public static async Task WrapAsync(
        string sourcePath,
        string outputPath,
        bool compress,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, BlockSize, true);
        var blockCount = (input.Length + BlockSize - 1) / BlockSize;
        var headerSize = HeaderSize(blockCount);

        var blocks = new List<byte[]>(checked((int)blockCount));
        var buffer = new byte[BlockSize];

        for (long i = 0; i < blockCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = 0;
            while (read < BlockSize)
            {
                var n = await input.ReadAsync(buffer.AsMemory(read, BlockSize - read), cancellationToken);
                if (n == 0) break;
                read += n;
            }

            byte[] stored;
            if (compress && read > 0)
            {
                using var ms = new MemoryStream();
                using (var z = CreatePs4ZlibStream(ms))
                    z.Write(buffer, 0, read);
                var zipped = ms.ToArray();

                if (zipped.Length < BlockSize)
                    stored = zipped;
                else
                {
                    stored = new byte[BlockSize];
                    Buffer.BlockCopy(buffer, 0, stored, 0, read);
                }
            }
            else
            {
                stored = new byte[BlockSize];
                Buffer.BlockCopy(buffer, 0, stored, 0, read);
            }

            blocks.Add(stored);
            progress?.Report($"PFSC block {i + 1}/{blockCount}");
        }

        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, BlockSize, true);
        output.SetLength(headerSize + blocks.Sum(x => (long)x.Length));

        var header = new byte[0x400];
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(0, 4), 0x50465343);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8, 4), 6);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(12, 4), BlockSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16, 8), BlockSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(24, 8), 0x400);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(32, 8), headerSize);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(40, 8), blockCount * BlockSize);
        await output.WriteAsync(header, cancellationToken);

        output.Position = 0x400;
        var offset = headerSize;
        foreach (var block in blocks)
        {
            var p = new byte[8];
            BinaryPrimitives.WriteInt64LittleEndian(p, offset);
            await output.WriteAsync(p, cancellationToken);
            offset += block.Length;
        }
        var finalPointer = new byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(finalPointer, offset);
        await output.WriteAsync(finalPointer, cancellationToken);

        output.Position = headerSize;
        foreach (var block in blocks)
            await output.WriteAsync(block, cancellationToken);
    }

    private static Stream CreatePs4ZlibStream(Stream output)
    {
        // A zlib stream's CMF byte encodes CINFO = log2(windowSize)-8.
        // PS4 PFSC requires CINFO=4 (windowBits=12). .NET 8's ZLibStream
        // emits the normal 32 KiB window (CINFO=7), so it cannot be used
        // for compressed PFSC blocks without a lower-level deflateInit2 API.
        // Fail closed here; production will keep PFSC uncompressed until the
        // native windowBits=12 encoder is integrated.
        throw new PlatformNotSupportedException(
            "PS4 PFSC compression requires zlib deflateInit2(windowBits=12).");
    }

    private static long HeaderSize(long blockCount)
    {
        var pointerTableSize = 8 + blockCount * 8;
        var additional = (pointerTableSize - 0xFC00 + 0xFFFF) / 0x10000;
        if (additional < 0) additional = 0;
        return 0x10000L + additional * BlockSize;
    }
}

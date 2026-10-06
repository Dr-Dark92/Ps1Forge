using System.Buffers.Binary;
using System.Runtime.InteropServices;

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
                var zipped = Ps4Zlib.TryCompress(buffer.AsSpan(0, read));

                if (zipped is not null && zipped.Length < BlockSize)
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

    private static class Ps4Zlib
    {
        private const int ZOk = 0;
        private const int ZStreamEnd = 1;
        private const int ZFinish = 4;
        private const int ZDeflated = 8;
        private const int ZDefaultStrategy = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct ZStream
        {
            public IntPtr next_in;
            public uint avail_in;
            public ulong total_in;
            public IntPtr next_out;
            public uint avail_out;
            public ulong total_out;
            public IntPtr msg;
            public IntPtr state;
            public IntPtr zalloc;
            public IntPtr zfree;
            public IntPtr opaque;
            public int data_type;
            public ulong adler;
            public ulong reserved;
        }

        [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr zlibVersion();

        [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int deflateInit2_(
            ref ZStream stream, int level, int method, int windowBits,
            int memLevel, int strategy, IntPtr version, int streamSize);

        [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int deflate(ref ZStream stream, int flush);

        [DllImport("zlib1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int deflateEnd(ref ZStream stream);

        public static byte[]? TryCompress(ReadOnlySpan<byte> input)
        {
            var source = input.ToArray();
            var target = new byte[source.Length + source.Length / 1000 + 64];
            var inHandle = GCHandle.Alloc(source, GCHandleType.Pinned);
            var outHandle = GCHandle.Alloc(target, GCHandleType.Pinned);
            try
            {
                var stream = new ZStream
                {
                    next_in = inHandle.AddrOfPinnedObject(),
                    avail_in = checked((uint)source.Length),
                    next_out = outHandle.AddrOfPinnedObject(),
                    avail_out = checked((uint)target.Length)
                };

                var rc = deflateInit2_(ref stream, 6, ZDeflated, 12, 8,
                    ZDefaultStrategy, zlibVersion(), Marshal.SizeOf<ZStream>());
                if (rc != ZOk)
                    throw new InvalidOperationException($"zlib deflateInit2 failed: {rc}.");

                try
                {
                    rc = deflate(ref stream, ZFinish);
                    if (rc != ZStreamEnd || stream.total_out >= (ulong)input.Length)
                        return null;
                    return target.AsSpan(0, checked((int)stream.total_out)).ToArray();
                }
                finally
                {
                    deflateEnd(ref stream);
                }
            }
            finally
            {
                outHandle.Free();
                inHandle.Free();
            }
        }
    }

    private static long HeaderSize(long blockCount)
    {
        var pointerTableSize = 8 + blockCount * 8;
        var additional = (pointerTableSize - 0xFC00 + 0xFFFF) / 0x10000;
        if (additional < 0) additional = 0;
        return 0x10000L + additional * BlockSize;
    }
}

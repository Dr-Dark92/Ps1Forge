using System.Buffers.Binary;

namespace Ps1Forge.Core;

public sealed record SignedBlockRef(byte[] Signature, int Block);

public static class SignedPfsPrimitives
{
    public const int SignedInode32Size = 0x2C8;
    public const int SignedInode64Size = 0x310;
    public const int DirectCount = 12;
    public const int IndirectCount = 5;
    public const int BlockRef32Size = 36;

    public static byte[] SignBlock(byte[] signingKey, ReadOnlySpan<byte> block) =>
        PackageCrypto.HmacSha256(signingKey, block);

    public static byte[] BuildSignedInode32(
        ushort mode,
        ushort nlink,
        uint flags,
        long size,
        uint blocks,
        IReadOnlyList<SignedBlockRef> direct,
        IReadOnlyList<SignedBlockRef>? indirect = null)
    {
        if (direct.Count > DirectCount) throw new ArgumentOutOfRangeException(nameof(direct));
        indirect ??= Array.Empty<SignedBlockRef>();
        if (indirect.Count > IndirectCount) throw new ArgumentOutOfRangeException(nameof(indirect));

        var d = new byte[SignedInode32Size];
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(0,2), mode);
        BinaryPrimitives.WriteUInt16LittleEndian(d.AsSpan(2,2), nlink);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(4,4), flags);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(8,8), size);
        BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16,8), size);
        BinaryPrimitives.WriteUInt32LittleEndian(d.AsSpan(88,4), blocks);

        var offset = 92;
        for (var i=0;i<DirectCount;i++,offset+=BlockRef32Size)
            WriteRef(d.AsSpan(offset,BlockRef32Size), i<direct.Count ? direct[i] : null);
        for (var i=0;i<IndirectCount;i++,offset+=BlockRef32Size)
            WriteRef(d.AsSpan(offset,BlockRef32Size), i<indirect.Count ? indirect[i] : null);
        return d;
    }

    public static byte[] BuildIndirectSignatureBlock(
        byte[] signingKey,
        IReadOnlyList<int> dataBlocks,
        Func<int, byte[]> readBlock)
    {
        var result = new byte[PfsPrimitives.BlockSize];
        var max = result.Length / BlockRef32Size;
        if (dataBlocks.Count > max) throw new ArgumentOutOfRangeException(nameof(dataBlocks));

        for (var i=0;i<dataBlocks.Count;i++)
        {
            var block = readBlock(dataBlocks[i]);
            var sig = SignBlock(signingKey, block);
            WriteRef(result.AsSpan(i*BlockRef32Size,BlockRef32Size), new SignedBlockRef(sig,dataBlocks[i]));
        }
        return result;
    }

    private static void WriteRef(Span<byte> target, SignedBlockRef? value)
    {
        target.Clear();
        if (value is null) return;
        if (value.Signature.Length != 32) throw new ArgumentException("PFS block signature must be 32 bytes.");
        value.Signature.CopyTo(target[..32]);
        BinaryPrimitives.WriteInt32LittleEndian(target[32..36], value.Block);
    }
}

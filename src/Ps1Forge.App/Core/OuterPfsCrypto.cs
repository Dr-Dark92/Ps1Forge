using System.Buffers.Binary;

namespace Ps1Forge.Core;

public static class OuterPfsCrypto
{
    public const int BlockSize = 0x10000;
    public const int SectorSize = 0x1000;

    public static async Task SignAsync(
        FileStream image,
        byte[] signingKey,
        IEnumerable<(long Block,long SignatureOffset,int Size)> dataSignatures,
        IReadOnlyList<(long Block,long SignatureOffset,int Size)> finalSignatures,
        CancellationToken ct)
    {
        foreach (var item in dataSignatures)
            await SignOneAsync(image, signingKey, item, ct);

        for (var i=finalSignatures.Count-1;i>=0;i--)
            await SignOneAsync(image, signingKey, finalSignatures[i], ct);
    }

    public static async Task EncryptAsync(
        FileStream image,
        byte[] dataKey,
        byte[] tweakKey,
        long? plaintextBlock,
        CancellationToken ct)
    {
        if ((image.Length % SectorSize) != 0)
            throw new InvalidDataException("Outer PFS length must be sector aligned.");

        var sector = new byte[SectorSize];
        var startSector = BlockSize / SectorSize;
        for (var sectorNo=startSector; (long)sectorNo*SectorSize<image.Length; sectorNo++)
        {
            ct.ThrowIfCancellationRequested();
            var blockNo=(long)sectorNo/(BlockSize/SectorSize);
            if (plaintextBlock.HasValue && blockNo==plaintextBlock.Value) continue;

            image.Position=(long)sectorNo*SectorSize;
            await ReadExactlyAsync(image,sector,ct);
            AesXts.EncryptInPlace(sector,dataKey,tweakKey,SectorSize,sectorNo);
            image.Position=(long)sectorNo*SectorSize;
            await image.WriteAsync(sector,ct);
        }
        await image.FlushAsync(ct);
    }

    private static async Task SignOneAsync(
        FileStream image,
        byte[] key,
        (long Block,long SignatureOffset,int Size) item,
        CancellationToken ct)
    {
        var data=new byte[item.Size];
        image.Position=item.Block*BlockSize;
        await ReadExactlyAsync(image,data,ct);
        var sig=PackageCrypto.HmacSha256(key,data);
        var record=new byte[36];
        sig.CopyTo(record,0);
        BinaryPrimitives.WriteInt32LittleEndian(record.AsSpan(32,4),checked((int)item.Block));
        image.Position=item.SignatureOffset;
        await image.WriteAsync(record,ct);
    }

    private static async Task ReadExactlyAsync(Stream stream,byte[] buffer,CancellationToken ct)
    {
        var read=0;
        while(read<buffer.Length)
        {
            var n=await stream.ReadAsync(buffer.AsMemory(read),ct);
            if(n==0) throw new EndOfStreamException();
            read+=n;
        }
    }
}

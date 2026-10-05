using System.Buffers.Binary;

namespace Ps1Forge.Core;

public static class OuterPfsWriter
{
    private const int BlockSize=0x10000;
    private const ushort Rx=1|4|8|32|64|256;
    private const ushort Dir=16384, File=32768;
    private const uint Internal=0x20000, Unk2=4, Unk3=8;

    public static async Task<long> BuildAsync(
        string pfscPath,string outputPath,byte[] ekpfs,byte[] seed,
        long logicalInnerPfsSize,CancellationToken ct)
    {
        if(seed.Length!=16) throw new ArgumentException("PFS seed must be 16 bytes.",nameof(seed));
        var pfscSize=new FileInfo(pfscPath).Length;

        // Four inodes: super-root, flat-path-table, uroot, pfs_image.dat.
        const int inodeCount=4;
        const int inodeBlockCount=1;
        const long inodeBlock=1, superRootBlock=2, flatBlock=3, emptyBlock=4;
        var nextIndirect=5L;

        var dataBlocks=Math.Max(1,(pfscSize+BlockSize-1)/BlockSize);
        var perIndirect=BlockSize/36;
        var indirectCount=dataBlocks<=12?0:
            dataBlocks<=12+perIndirect?1:
            2+(int)((dataBlocks-(12+perIndirect)+perIndirect-1)/perIndirect);
        var nextData=nextIndirect+indirectCount;

        // pfs_image inode is #3, at block 1 + 3*0x2c8.
        var fileInodeOffset=inodeBlock*BlockSize+3L*SignedPfsPrimitives.SignedInode32Size;
        var layout=OuterPfsAllocator.AllocateLargeFile(
            pfscSize,fileInodeOffset,ref nextIndirect,ref nextData);

        var urootBlock=nextData++;
        var totalBlocks=nextData;
        await using var fs=new FileStream(outputPath,FileMode.Create,FileAccess.ReadWrite,FileShare.None,1024*1024,FileOptions.Asynchronous);
        fs.SetLength(totalBlocks*BlockSize);

        var finalSigs=new List<(long Block,long SignatureOffset,int Size)>();
        var dataSigs=new List<(long Block,long SignatureOffset,int Size)>(layout.DataSignatures);

        // Header block signature is stored at 0x380; inode-block signature in header at 0xB8.
        finalSigs.Add((0,0x380,0x5A0));
        finalSigs.Add((inodeBlock,0xB8,BlockSize));

        var superInodeOff=inodeBlock*BlockSize;
        var flatInodeOff=superInodeOff+SignedPfsPrimitives.SignedInode32Size;
        var rootInodeOff=flatInodeOff+SignedPfsPrimitives.SignedInode32Size;

        finalSigs.Add((superRootBlock,superInodeOff+100,BlockSize));
        finalSigs.Add((flatBlock,flatInodeOff+100,BlockSize));
        dataSigs.Add((urootBlock,rootInodeOff+100,BlockSize));
        finalSigs.AddRange(layout.FinalSignatures);

        WriteHeader(fs,totalBlocks,inodeCount,inodeBlockCount,seed);
        WriteInode(fs,superInodeOff,(ushort)(Dir|Rx),1,Internal|Unk2|Unk3,BlockSize,1,superRootBlock,null);
        WriteInode(fs,flatInodeOff,(ushort)(File|Rx),1,Internal|Unk2|Unk3,8,1,flatBlock,null);
        WriteInode(fs,rootInodeOff,(ushort)(Dir|Rx),2,Unk2|Unk3,BlockSize,1,urootBlock,null);
        WriteInode(fs,fileInodeOffset,(ushort)(File|Rx),1,Unk2|Unk3,pfscSize,checked((uint)layout.DataBlocks),layout.DataStartBlock,layout.IndirectBlocks,logicalInnerPfsSize);

        fs.Position=superRootBlock*BlockSize;
        PfsPrimitives.WriteDirent(fs,1,2,"flat_path_table");
        PfsPrimitives.WriteDirent(fs,2,3,"uroot");

        fs.Position=flatBlock*BlockSize;
        var flat=new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(flat.AsSpan(0,4),PfsPrimitives.PathHash("/pfs_image.dat"));
        BinaryPrimitives.WriteUInt32LittleEndian(flat.AsSpan(4,4),3);
        await fs.WriteAsync(flat,ct);

        fs.Position=urootBlock*BlockSize;
        PfsPrimitives.WriteDirent(fs,2,4,".");
        PfsPrimitives.WriteDirent(fs,2,5,"..");
        PfsPrimitives.WriteDirent(fs,3,2,"pfs_image.dat");

        await using(var src=new FileStream(pfscPath,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan))
        {
            fs.Position=layout.DataStartBlock*BlockSize;
            await src.CopyToAsync(fs,1024*1024,ct);
        }
        await fs.FlushAsync(ct);

        var signKey=PackageCrypto.PfsSigningKey(ekpfs,seed);
        await OuterPfsCrypto.SignAsync(fs,signKey,dataSigs,finalSigs,ct);
        var (tweakKey,dataKey)=PackageCrypto.PfsEncryptionKeys(ekpfs,seed);
        await OuterPfsCrypto.EncryptAsync(fs,dataKey,tweakKey,emptyBlock,ct);
        return fs.Length;
    }

    private static void WriteHeader(Stream s,long blocks,int inodeCount,int inodeBlocks,byte[] seed)
    {
        var h=new byte[BlockSize];
        BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(0,8),1);
        BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(8,8),20130315);
        h[0x1A]=1;
        BinaryPrimitives.WriteUInt16LittleEndian(h.AsSpan(0x1C,2),0xD); // signed|encrypted|unknown
        BinaryPrimitives.WriteUInt32LittleEndian(h.AsSpan(0x20,4),BlockSize);
        BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(0x28,8),blocks);
        BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(0x30,8),inodeCount);
        BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(0x38,8),blocks);
        BinaryPrimitives.WriteInt64LittleEndian(h.AsSpan(0x40,8),inodeBlocks);
        // Header's embedded dinodeS64 starts at 0x50. Its first signed block
        // reference begins at +0x68, therefore global offset 0xB8.
        var inodeSig=SignedPfsPrimitives.BuildSignedInode64(
            0,1,0,inodeBlocks*BlockSize,(uint)inodeBlocks,
            [new SignedBlockRef(new byte[32],1)]);
        inodeSig.CopyTo(h,0x50);
        BinaryPrimitives.WriteInt32LittleEndian(h.AsSpan(0x36C,4),1);
        seed.CopyTo(h,0x370);
        s.Position=0;s.Write(h);
    }

    private static void WriteInode(Stream s,long off,ushort mode,ushort links,uint flags,long size,uint blocks,long first,IReadOnlyList<long>? indirect,long? compressed=null)
    {
        var direct=new List<SignedBlockRef>{new(new byte[32],checked((int)first))};
        var inds=(indirect??Array.Empty<long>()).Select(x=>new SignedBlockRef(new byte[32],checked((int)x))).ToList();
        var d=SignedPfsPrimitives.BuildSignedInode32(mode,links,flags,size,blocks,direct,inds);
        if(compressed.HasValue) BinaryPrimitives.WriteInt64LittleEndian(d.AsSpan(16,8),compressed.Value);
        s.Position=off;s.Write(d);
    }
}

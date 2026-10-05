namespace Ps1Forge.Core;

public sealed record OuterPfsFileLayout(
    long DataStartBlock,
    long DataBlocks,
    IReadOnlyList<long> InodeIndirectBlocks,
    IReadOnlyList<long> AllIndirectBlocks,
    IReadOnlyList<(long Block,long SignatureOffset,int Size)> DataSignatures,
    IReadOnlyList<(long Block,long SignatureOffset,int Size)> FinalSignatures);

public static class OuterPfsAllocator
{
    public const int BlockSize=0x10000;
    public const int SignatureRecordSize=36;
    public const int DirectCount=12;

    public static OuterPfsFileLayout AllocateLargeFile(
        long fileSize,long inodeOffset,ref long nextBlock)
    {
        var blocks=Math.Max(1,CeilDiv(fileSize,BlockSize));
        var perIndirect=BlockSize/SignatureRecordSize;
        var remaining=blocks-Math.Min(blocks,DirectCount);

        var inodeIndirect=new List<long>();
        var allIndirect=new List<long>();
        long level1=-1,level2Root=-1;
        var leaves=new List<long>();

        // Reserve the complete signature tree before allocating file data.
        if(remaining>0)
        {
            level1=nextBlock++;
            inodeIndirect.Add(level1); allIndirect.Add(level1);
            remaining-=Math.Min(remaining,perIndirect);

            if(remaining>0)
            {
                level2Root=nextBlock++;
                inodeIndirect.Add(level2Root); allIndirect.Add(level2Root);
                var leafCount=CeilDiv(remaining,perIndirect);
                if(leafCount>perIndirect)
                    throw new NotSupportedException("Outer PFS file exceeds two-level signature capacity.");
                for(long i=0;i<leafCount;i++){var leaf=nextBlock++; leaves.Add(leaf); allIndirect.Add(leaf);}
            }
        }

        var dataStart=nextBlock;
        nextBlock+=blocks;

        var dataSigs=new List<(long,long,int)>();
        var finalSigs=new List<(long,long,int)>();
        var dataIndex=0L;
        for(;dataIndex<Math.Min(blocks,DirectCount);dataIndex++)
            dataSigs.Add((dataStart+dataIndex,inodeOffset+SignedInodeDirectOffset((int)dataIndex),BlockSize));

        remaining=blocks-dataIndex;
        if(level1>=0)
        {
            finalSigs.Add((level1,inodeOffset+SignedInodeIndirectOffset(0),BlockSize));
            var count=Math.Min(remaining,perIndirect);
            for(long i=0;i<count;i++,dataIndex++)
                dataSigs.Add((dataStart+dataIndex,level1*BlockSize+i*SignatureRecordSize,BlockSize));
            remaining-=count;
        }

        if(level2Root>=0)
        {
            finalSigs.Add((level2Root,inodeOffset+SignedInodeIndirectOffset(1),BlockSize));
            for(var li=0;li<leaves.Count;li++)
            {
                var leaf=leaves[li];
                finalSigs.Add((leaf,level2Root*BlockSize+(long)li*SignatureRecordSize,BlockSize));
                var count=Math.Min(remaining,perIndirect);
                for(long j=0;j<count;j++,dataIndex++)
                    dataSigs.Add((dataStart+dataIndex,leaf*BlockSize+j*SignatureRecordSize,BlockSize));
                remaining-=count;
            }
        }

        if(remaining!=0 || dataIndex!=blocks)
            throw new InvalidDataException("Outer PFS allocation did not account for every file block.");

        return new(dataStart,blocks,inodeIndirect,allIndirect,dataSigs,finalSigs);
    }

    public static long SignedInodeDirectOffset(int index)
    {
        if(index is <0 or >=12) throw new ArgumentOutOfRangeException(nameof(index));
        return 100L+index*SignatureRecordSize;
    }

    public static long SignedInodeIndirectOffset(int index)
    {
        if(index is <0 or >=5) throw new ArgumentOutOfRangeException(nameof(index));
        return 100L+12L*SignatureRecordSize+index*SignatureRecordSize;
    }

    private static long CeilDiv(long v,long d)=>(v+d-1)/d;
}

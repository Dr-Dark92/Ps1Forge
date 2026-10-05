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
    public const int BlockSize = 0x10000;
    public const int SignedInodeSize = 0x2C8;
    public const int SignatureRecordSize = 36;
    public const int DirectCount = 12;

    public static OuterPfsFileLayout AllocateLargeFile(
        long fileSize,
        long inodeOffset,
        ref long nextIndirectBlock,
        ref long nextDataBlock)
    {
        var blocks=Math.Max(1,CeilDiv(fileSize,BlockSize));
        var perIndirect=BlockSize/SignatureRecordSize;
        var inodeIndirect=new List<long>();
        var allIndirect=new List<long>();
        var dataSigs=new List<(long,long,int)>();
        var finalSigs=new List<(long,long,int)>();

        var direct=Math.Min(blocks,DirectCount);
        var dataStart=nextDataBlock;
        for(long i=0;i<direct;i++)
        {
            dataSigs.Add((nextDataBlock,inodeOffset+SignedInodeDirectOffset((int)i),BlockSize));
            nextDataBlock++;
        }

        var remaining=blocks-direct;
        if(remaining>0)
        {
            var level1=nextIndirectBlock++;
            inodeIndirect.Add(level1);
            allIndirect.Add(level1);
            finalSigs.Add((level1,inodeOffset+SignedInodeIndirectOffset(0),BlockSize));

            var first=Math.Min(remaining,perIndirect);
            for(long i=0;i<first;i++)
            {
                dataSigs.Add((nextDataBlock,level1*BlockSize+i*SignatureRecordSize,BlockSize));
                nextDataBlock++;
            }
            remaining-=first;

            if(remaining>0)
            {
                var level2Root=nextIndirectBlock++;
                inodeIndirect.Add(level2Root);
                allIndirect.Add(level2Root);
                finalSigs.Add((level2Root,inodeOffset+SignedInodeIndirectOffset(1),BlockSize));

                long rootSlot=0;
                while(remaining>0)
                {
                    if(rootSlot>=perIndirect)
                        throw new NotSupportedException("Outer PFS file exceeds two-level signature capacity.");

                    var leaf=nextIndirectBlock++;
                    allIndirect.Add(leaf);
                    finalSigs.Add((leaf,level2Root*BlockSize+rootSlot*SignatureRecordSize,BlockSize));
                    rootSlot++;

                    var count=Math.Min(remaining,perIndirect);
                    for(long j=0;j<count;j++)
                    {
                        dataSigs.Add((nextDataBlock,leaf*BlockSize+j*SignatureRecordSize,BlockSize));
                        nextDataBlock++;
                    }
                    remaining-=count;
                }
            }
        }

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

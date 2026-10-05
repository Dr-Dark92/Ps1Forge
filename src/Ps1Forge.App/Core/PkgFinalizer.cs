using System.Security.Cryptography;

namespace Ps1Forge.Core;

public static class PkgFinalizer
{
    public static async Task ApplyCoreDigestsAsync(
        FileStream pkg,
        PkgBodyLayout layout,
        CancellationToken ct,
        string? contentId=null,
        byte[]? paramSfo=null)
    {
        var pfsSize=checked((long)(layout.PackageSize-layout.PfsOffset));
        var fullPfs=await HashRangeAsync(pkg,checked((long)layout.PfsOffset),pfsSize,ct);
        var signedPfs=await HashRangeAsync(pkg,checked((long)layout.PfsOffset),Math.Min(0x10000L,pfsSize),ct);
        await WriteAtAsync(pkg,0x440,fullPfs,ct);
        await WriteAtAsync(pkg,0x460,signedPfs,ct);

        if(contentId is not null && paramSfo is not null)
        {
            var generalEntry=layout.Entries.FirstOrDefault(x=>x.Id==PkgBodyBuilder.GeneralDigests);
            if(generalEntry is null) throw new InvalidDataException("GENERAL_DIGESTS entry is missing.");
            var header=await ReadAtAsync(pkg,0,PkgHeader.HeaderSize,ct);
            var general=PkgGeneralDigests.Build(header,contentId,paramSfo,fullPfs);
            if(general.Length!=generalEntry.DataSize) throw new InvalidDataException("GENERAL_DIGESTS size mismatch.");
            await WriteAtAsync(pkg,generalEntry.DataOffset,general,ct);
        }

        var sorted=layout.Entries.OrderBy(x=>x.Id).ToList();
        var digestEntry=sorted.FirstOrDefault(x=>x.Id==PkgBodyBuilder.Digests);
        if(digestEntry is not null)
        {
            var table=new byte[sorted.Count*32];
            for(var i=1;i<sorted.Count;i++)
            {
                var e=sorted[i];
                var hash=await HashRangeAsync(pkg,e.DataOffset,e.DataSize,ct);
                hash.CopyTo(table,i*32);
            }
            if(table.Length!=digestEntry.DataSize) throw new InvalidDataException("PKG digest table size mismatch.");
            await WriteAtAsync(pkg,digestEntry.DataOffset,table,ct);
            await WriteAtAsync(pkg,0x140,SHA256.HashData(table),ct);
        }

        var bodyHash=await HashRangeAsync(pkg,checked((long)layout.BodyOffset),checked((long)layout.BodySize),ct);
        await WriteAtAsync(pkg,0x160,bodyHash,ct);

        var sc1=await ConcatEntriesAsync(pkg,layout.Entries,
            [PkgBodyBuilder.EntryKeys,PkgBodyBuilder.ImageKey,PkgBodyBuilder.GeneralDigests,PkgBodyBuilder.Metas,PkgBodyBuilder.Digests],false,ct);
        if(sc1.Length>0) await WriteAtAsync(pkg,0x100,SHA256.HashData(sc1),ct);

        var sc2=await ConcatEntriesAsync(pkg,layout.Entries,
            [PkgBodyBuilder.EntryKeys,PkgBodyBuilder.ImageKey,PkgBodyBuilder.GeneralDigests,PkgBodyBuilder.Metas],true,ct);
        if(sc2.Length>0) await WriteAtAsync(pkg,0x120,SHA256.HashData(sc2),ct);
    }

    public static async Task ApplyHeaderDigestAsync(FileStream pkg,CancellationToken ct)
    {
        var header=await ReadAtAsync(pkg,0,0xFE0,ct);
        await WriteAtAsync(pkg,0xFE0,SHA256.HashData(header),ct);
    }

    private static async Task<byte[]> ConcatEntriesAsync(FileStream pkg,IReadOnlyList<PkgBodyEntry> entries,uint[] ids,bool sc2,CancellationToken ct)
    {
        using var ms=new MemoryStream();
        foreach(var id in ids)
        {
            var e=entries.FirstOrDefault(x=>x.Id==id);
            if(e is null) return [];
            var size=sc2&&id==PkgBodyBuilder.Metas?Math.Min(e.DataSize,6u*0x20u):e.DataSize;
            var data=await ReadAtAsync(pkg,e.DataOffset,checked((int)size),ct);
            await ms.WriteAsync(data,ct);
        }
        return ms.ToArray();
    }

    private static async Task<byte[]> HashRangeAsync(FileStream fs,long offset,long length,CancellationToken ct)
    {
        fs.Position=offset;
        using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer=new byte[1024*1024];
        long remaining=length;
        while(remaining>0)
        {
            var read=await fs.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,remaining)),ct);
            if(read==0) throw new EndOfStreamException();
            hash.AppendData(buffer,0,read);
            remaining-=read;
        }
        return hash.GetHashAndReset();
    }

    private static async Task<byte[]> ReadAtAsync(FileStream fs,long offset,int length,CancellationToken ct)
    {
        var data=new byte[length]; fs.Position=offset; var done=0;
        while(done<length){var n=await fs.ReadAsync(data.AsMemory(done,length-done),ct);if(n==0)throw new EndOfStreamException();done+=n;}
        return data;
    }

    private static async Task WriteAtAsync(FileStream fs,long offset,byte[] data,CancellationToken ct)
    {
        fs.Position=offset; await fs.WriteAsync(data,ct);
    }
}

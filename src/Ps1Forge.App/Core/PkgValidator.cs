using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ps1Forge.Core;

public sealed record PkgValidation(bool Valid,IReadOnlyList<string> Errors);

public static class PkgValidator
{
    public static PkgValidation ValidateHeader(ReadOnlySpan<byte> pkg)
    {
        var errors=new List<string>();
        if(pkg.Length<PkgHeader.HeaderSize){errors.Add("PKG header is truncated.");return new(false,errors);}
        if(pkg[0]!=0x7F||pkg[1]!=(byte)'C'||pkg[2]!=(byte)'N'||pkg[3]!=(byte)'T') errors.Add("Invalid PKG magic.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x04,4))!=0x40000001) errors.Add("Unexpected PKG flags.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x0C,4))!=0xF) errors.Add("Unexpected PKG header type.");
        if(BinaryPrimitives.ReadUInt64BigEndian(pkg.Slice(0x20,8))!=PkgHeader.BodyOffset) errors.Add("Unexpected body offset.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x70,4))!=0xF) errors.Add("Unexpected DRM type.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x74,4))!=0x1A) errors.Add("Unexpected content type.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x78,4))!=0x0A000000) errors.Add("Unexpected content flags.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x80,4))!=0x20171106) errors.Add("Unexpected PKG version date.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x84,4))!=0x01889410) errors.Add("Unexpected PKG version hash.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x9C,4))!=1) errors.Add("Unexpected EKC version.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x400,4))!=1) errors.Add("Unexpected PFS header marker.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x404,4))!=1) errors.Add("Unexpected PFS image count.");
        if(BinaryPrimitives.ReadUInt64BigEndian(pkg.Slice(0x408,8))!=0x80000000000003CCUL) errors.Add("Unexpected PFS flags.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x438,4))!=0x10000) errors.Add("Unexpected signed PFS size.");
        if(BinaryPrimitives.ReadUInt32BigEndian(pkg.Slice(0x43C,4))!=0xE0000) errors.Add("Unexpected PFS cache size.");
        return new(errors.Count==0,errors);
    }

    public static async Task<PkgValidation> ValidateFileAsync(string path,CancellationToken ct=default)
    {
        var errors=new List<string>();
        await using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        if(fs.Length<0x2000){errors.Add("File is smaller than minimum PKG layout.");return new(false,errors);}
        var h=new byte[PkgHeader.HeaderSize]; await ReadExactAsync(fs,h,ct);
        errors.AddRange(ValidateHeader(h).Errors);
        var headerDigest=SHA256.HashData(h.AsSpan(0,0xFE0));
        if(!headerDigest.AsSpan().SequenceEqual(h.AsSpan(0xFE0,32))) errors.Add("Final PKG header SHA-256 mismatch.");
        var count=BE32(h,0x10); var table=BE32(h,0x18);
        var body=BE64(h,0x20); var bodySize=BE64(h,0x28);
        var pfs=BE64(h,0x410); var pfsSize=BE64(h,0x418); var size=BE64(h,0x430);
        if(count==0) errors.Add("PKG contains no entries.");
        if(table!=PkgHeader.EntryTableOffset) errors.Add("Unexpected entry table offset.");
        if(pfs!=body+bodySize) errors.Add("PFS offset does not follow body.");
        if(size!=(ulong)fs.Length) errors.Add("Package size does not match file length.");
        if(pfs+pfsSize!=size) errors.Add("PFS extent does not end at package boundary.");
        if(pfsSize==0) errors.Add("PFS payload is empty.");
        var entries=await ValidateEntriesAsync(fs,count,table,body,bodySize,errors,ct);
        await ValidateEntryDigestsAsync(fs,h,entries,errors,ct);
        await ValidateScDigestsAsync(fs,h,entries,errors,ct);
        if(pfsSize>0&&pfs+pfsSize<=(ulong)fs.Length)
        {
            var full=await HashRangeAsync(fs,(long)pfs,(long)pfsSize,ct);
            if(!full.AsSpan().SequenceEqual(h.AsSpan(0x440,32))) errors.Add("Full PFS SHA-256 mismatch.");
            var signed=await HashRangeAsync(fs,(long)pfs,(long)Math.Min(pfsSize,0x10000UL),ct);
            if(!signed.AsSpan().SequenceEqual(h.AsSpan(0x460,32))) errors.Add("Signed PFS SHA-256 mismatch.");
        }
        if(body+bodySize<=(ulong)fs.Length)
        {
            var hash=await HashRangeAsync(fs,(long)body,(long)bodySize,ct);
            if(!hash.AsSpan().SequenceEqual(h.AsSpan(0x160,32))) errors.Add("Body SHA-256 mismatch.");
        }
        return new(errors.Count==0,errors);
    }

    private sealed record ParsedEntry(uint Id,uint DataOffset,uint DataSize,uint Flags1);

    private static async Task<List<ParsedEntry>> ValidateEntriesAsync(FileStream fs,uint count,uint table,ulong body,ulong bodySize,List<string> errors,CancellationToken ct)
    {
        var tableSize=(ulong)count*32UL;
        if((ulong)table+tableSize>PkgHeader.BodyOffset+0x1000UL){errors.Add("Entry table exceeds the PKG metadata area.");return [];}
        var parsed=new List<ParsedEntry>();
        var raw=new byte[checked((int)tableSize)];
        fs.Position=table; await ReadExactAsync(fs,raw,ct);
        var ranges=new List<(ulong Start,ulong End,uint Id)>();
        uint lastId=0;
        for(var i=0;i<count;i++)
        {
            var o=checked((int)i*32);
            var id=BE32(raw,o); var flags1=BE32(raw,o+8); var dataOffset=BE32(raw,o+16); var dataSize=BE32(raw,o+20);
            if(i>0&&id<lastId) errors.Add("Entry table is not sorted by ID.");
            lastId=id;
            var start=(ulong)dataOffset; var end=start+dataSize;
            if(start<body||end>body+bodySize) errors.Add($"Entry 0x{id:X8} lies outside the package body.");
            if((start&0xFUL)!=0) errors.Add($"Entry 0x{id:X8} is not 16-byte aligned.");
            parsed.Add(new ParsedEntry(id,dataOffset,dataSize,flags1));
            if(dataSize>0) ranges.Add((start,end,id));
        }
        var ordered=ranges.OrderBy(x=>x.Start).ToList();
        for(var i=1;i<ordered.Count;i++)
            if(ordered[i].Start<ordered[i-1].End) errors.Add($"Entries 0x{ordered[i-1].Id:X8} and 0x{ordered[i].Id:X8} overlap.");
        return parsed;
    }

    private static async Task ValidateEntryDigestsAsync(FileStream fs,byte[] header,List<ParsedEntry> entries,List<string> errors,CancellationToken ct)
    {
        var sorted=entries.OrderBy(x=>x.Id).ToList();
        var digestEntry=sorted.FirstOrDefault(x=>x.Id==PkgBodyBuilder.Digests);
        if(digestEntry is null){errors.Add("DIGESTS entry is missing.");return;}
        var expectedSize=checked(sorted.Count*32);
        if(digestEntry.DataSize!=expectedSize){errors.Add("DIGESTS entry has unexpected size.");return;}
        var table=await ReadRangeAsync(fs,digestEntry.DataOffset,expectedSize,ct);
        if(table.AsSpan(0,32).IndexOfAnyExcept((byte)0)>=0) errors.Add("DIGESTS slot 0 must be zero.");
        for(var i=1;i<sorted.Count;i++)
        {
            var e=sorted[i];
            var storedSize=(e.Flags1&0x80000000u)!=0 ? (e.DataSize+15u)&~15u : e.DataSize;
            var actual=await HashRangeAsync(fs,e.DataOffset,storedSize,ct);
            if(!actual.AsSpan().SequenceEqual(table.AsSpan(i*32,32)))
                errors.Add($"Entry digest mismatch for 0x{e.Id:X8}.");
        }
        var tableHash=SHA256.HashData(table);
        if(!tableHash.AsSpan().SequenceEqual(header.AsSpan(0x140,32))) errors.Add("DIGESTS table SHA-256 mismatch.");
    }

    private static async Task ValidateScDigestsAsync(FileStream fs,byte[] header,List<ParsedEntry> entries,List<string> errors,CancellationToken ct)
    {
        var sc1Ids=new[]{PkgBodyBuilder.EntryKeys,PkgBodyBuilder.ImageKey,PkgBodyBuilder.GeneralDigests,PkgBodyBuilder.Metas,PkgBodyBuilder.Digests};
        var sc2Ids=new[]{PkgBodyBuilder.EntryKeys,PkgBodyBuilder.ImageKey,PkgBodyBuilder.GeneralDigests,PkgBodyBuilder.Metas};

        var sc1=await ConcatEntryDataAsync(fs,entries,sc1Ids,false,ct);
        if(sc1 is not null)
        {
            var hash=SHA256.HashData(sc1);
            if(!hash.AsSpan().SequenceEqual(header.AsSpan(0x100,32))) errors.Add("SC1 SHA-256 mismatch.");
        }

        var sc2=await ConcatEntryDataAsync(fs,entries,sc2Ids,true,ct);
        if(sc2 is not null)
        {
            var hash=SHA256.HashData(sc2);
            if(!hash.AsSpan().SequenceEqual(header.AsSpan(0x120,32))) errors.Add("SC2 SHA-256 mismatch.");
        }
    }

    private static async Task<byte[]?> ConcatEntryDataAsync(FileStream fs,List<ParsedEntry> entries,uint[] ids,bool sc2MetasSize,CancellationToken ct)
    {
        using var output=new MemoryStream();
        foreach(var id in ids)
        {
            var e=entries.FirstOrDefault(x=>x.Id==id);
            if(e is null) return null;
            var size=e.DataSize;
            if(sc2MetasSize&&id==PkgBodyBuilder.Metas)
                size=Math.Min(size,6u*0x20u);
            var data=await ReadRangeAsync(fs,e.DataOffset,checked((int)size),ct);
            await output.WriteAsync(data,ct);
        }
        return output.ToArray();
    }

    private static async Task<byte[]> ReadRangeAsync(FileStream fs,long offset,int length,CancellationToken ct)
    {
        var data=new byte[length]; fs.Position=offset; await ReadExactAsync(fs,data,ct); return data;
    }

    private static uint BE32(byte[] b,int o)=>BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(o,4));
    private static ulong BE64(byte[] b,int o)=>BinaryPrimitives.ReadUInt64BigEndian(b.AsSpan(o,8));
    private static async Task ReadExactAsync(Stream s,byte[] b,CancellationToken ct){var done=0;while(done<b.Length){var n=await s.ReadAsync(b.AsMemory(done),ct);if(n==0)throw new EndOfStreamException();done+=n;}}
    private static async Task<byte[]> HashRangeAsync(FileStream fs,long offset,long length,CancellationToken ct)
    {
        fs.Position=offset; using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256); var buffer=new byte[1024*1024]; long left=length;
        while(left>0){var n=await fs.ReadAsync(buffer.AsMemory(0,(int)Math.Min(buffer.Length,left)),ct);if(n==0)throw new EndOfStreamException();hash.AppendData(buffer,0,n);left-=n;}
        return hash.GetHashAndReset();
    }
}

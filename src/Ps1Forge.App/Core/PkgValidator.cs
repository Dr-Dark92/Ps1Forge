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
        if(BinaryPrimitives.ReadUInt64BigEndian(pkg.Slice(0x20,8))!=PkgHeader.BodyOffset) errors.Add("Unexpected body offset.");
        return new(errors.Count==0,errors);
    }

    public static async Task<PkgValidation> ValidateFileAsync(string path,CancellationToken ct=default)
    {
        var errors=new List<string>();
        await using var fs=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan);
        if(fs.Length<0x2000){errors.Add("File is smaller than minimum PKG layout.");return new(false,errors);}
        var h=new byte[PkgHeader.HeaderSize]; await ReadExactAsync(fs,h,ct);
        errors.AddRange(ValidateHeader(h).Errors);
        var count=BE32(h,0x10); var table=BE32(h,0x18);
        var body=BE64(h,0x20); var bodySize=BE64(h,0x28);
        var pfs=BE64(h,0x410); var pfsSize=BE64(h,0x418); var size=BE64(h,0x430);
        if(count==0) errors.Add("PKG contains no entries.");
        if(table!=PkgHeader.EntryTableOffset) errors.Add("Unexpected entry table offset.");
        if(pfs!=body+bodySize) errors.Add("PFS offset does not follow body.");
        if(size!=(ulong)fs.Length) errors.Add("Package size does not match file length.");
        if(pfs+pfsSize!=size) errors.Add("PFS extent does not end at package boundary.");
        if(pfsSize==0) errors.Add("PFS payload is empty.");
        await ValidateEntriesAsync(fs,h,count,table,body,bodySize,errors,ct);
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

    private static async Task ValidateEntriesAsync(FileStream fs,byte[] header,uint count,uint table,ulong body,ulong bodySize,List<string> errors,CancellationToken ct)
    {
        var tableSize=(ulong)count*32UL;
        if((ulong)table+tableSize>body){errors.Add("Entry table overlaps package body.");return;}
        var raw=new byte[checked((int)tableSize)];
        fs.Position=table; await ReadExactAsync(fs,raw,ct);
        var ranges=new List<(ulong Start,ulong End,uint Id)>();
        uint lastId=0;
        for(var i=0;i<count;i++)
        {
            var o=checked((int)i*32);
            var id=BE32(raw,o); var dataOffset=BE32(raw,o+16); var dataSize=BE32(raw,o+20);
            if(i>0&&id<lastId) errors.Add("Entry table is not sorted by ID.");
            lastId=id;
            var start=(ulong)dataOffset; var end=start+dataSize;
            if(start<body||end>body+bodySize) errors.Add($"Entry 0x{id:X8} lies outside the package body.");
            if((start&0xFUL)!=0) errors.Add($"Entry 0x{id:X8} is not 16-byte aligned.");
            if(dataSize>0) ranges.Add((start,end,id));
        }
        var ordered=ranges.OrderBy(x=>x.Start).ToList();
        for(var i=1;i<ordered.Count;i++)
            if(ordered[i].Start<ordered[i-1].End) errors.Add($"Entries 0x{ordered[i-1].Id:X8} and 0x{ordered[i].Id:X8} overlap.");
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

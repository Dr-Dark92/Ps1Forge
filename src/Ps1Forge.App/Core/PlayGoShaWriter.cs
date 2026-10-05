using System.Security.Cryptography;

namespace Ps1Forge.Core;

public static class PlayGoShaWriter
{
    private const int ChunkSize=0x10000;

    public static async Task ApplyAsync(FileStream pkg,PkgBodyLayout layout,CancellationToken ct)
    {
        var entry=layout.Entries.FirstOrDefault(e=>e.Id==PkgBodyBuilder.PlayGoChunkSha);
        if(entry is null) return;

        var totalChunks=layout.PackageSize/(ulong)ChunkSize;
        var startChunk=layout.PfsOffset/(ulong)ChunkSize;
        var expected=checked((int)(totalChunks*4));
        if(entry.DataSize!=expected)
            throw new InvalidDataException($"PlayGo SHA size mismatch: planned {entry.DataSize}, expected {expected}.");

        var table=new byte[expected];
        var buffer=new byte[ChunkSize];
        for(var chunk=startChunk;chunk<totalChunks;chunk++)
        {
            var offset=checked((long)(chunk*(ulong)ChunkSize));
            pkg.Position=offset;
            var done=0;
            while(done<ChunkSize)
            {
                var n=await pkg.ReadAsync(buffer.AsMemory(done,ChunkSize-done),ct);
                if(n==0) throw new EndOfStreamException($"PlayGo chunk {chunk} is truncated.");
                done+=n;
            }
            var hash=SHA256.HashData(buffer);
            hash.AsSpan(0,4).CopyTo(table.AsSpan(checked((int)chunk*4),4));
        }

        pkg.Position=entry.DataOffset;
        await pkg.WriteAsync(table,ct);
    }
}

namespace Ps1Forge.Core;

public static class PkgAssembler
{
    public static async Task<PkgValidation> AssembleAsync(
        string outputPath,
        string outerPfsPath,
        string contentId,
        string passcode,
        IReadOnlyList<PkgBodyEntry> entries,
        byte[] paramSfo,
        CancellationToken ct=default)
    {
        var pfsInfo=new FileInfo(outerPfsPath);
        if(!pfsInfo.Exists||pfsInfo.Length<=0) throw new FileNotFoundException("Outer PFS is missing.",outerPfsPath);
        if(entries.Count==0) throw new ArgumentException("At least one PKG entry is required.",nameof(entries));

        var layout=PkgBodyBuilder.Plan(entries,checked((ulong)pfsInfo.Length));
        var mainSize=layout.Entries
            .Where(e=>e.Id is PkgBodyBuilder.EntryKeys or PkgBodyBuilder.ImageKey or PkgBodyBuilder.GeneralDigests or PkgBodyBuilder.Metas or PkgBodyBuilder.Digests)
            .Aggregate(0u,(sum,e)=>checked(sum+e.DataSize));
        var header=PkgHeader.Build(contentId,layout.Entries.Count,mainSize,layout.BodySize,layout.PfsOffset,checked((ulong)pfsInfo.Length),layout.PackageSize);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        await using(var pkg=new FileStream(outputPath,FileMode.Create,FileAccess.ReadWrite,FileShare.None,1024*1024,FileOptions.Asynchronous))
        {
            await pkg.WriteAsync(header,ct);
            await PkgBodyBuilder.WriteBodyAsync(pkg,layout,contentId,passcode,ct);

            pkg.Position=checked((long)layout.PfsOffset);
            await using(var pfs=new FileStream(outerPfsPath,FileMode.Open,FileAccess.Read,FileShare.Read,1024*1024,FileOptions.Asynchronous|FileOptions.SequentialScan))
                await pfs.CopyToAsync(pkg,1024*1024,ct);

            if((ulong)pkg.Length!=layout.PackageSize)
                throw new InvalidDataException($"PKG size mismatch after assembly: {pkg.Length} != {layout.PackageSize}.");

            await pkg.FlushAsync(ct);
            await PkgFinalizer.ApplyCoreDigestsAsync(pkg,layout,ct,contentId,paramSfo);
            await PkgFinalizer.ApplyHeaderDigestAsync(pkg,ct);
            await pkg.FlushAsync(ct);
        }

        return await PkgValidator.ValidateFileAsync(outputPath,ct);
    }
}

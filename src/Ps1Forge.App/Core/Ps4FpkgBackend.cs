namespace Ps1Forge.Core;

/// <summary>
/// PS4 fPKG backend boundary. Runtime validation and PS1HD configuration are
/// implemented here; native PFS/PFSC/PKG serialization is the remaining stage.
/// Sony runtime binaries are never embedded in Ps1Forge.
/// </summary>
public sealed class Ps4FpkgBackend : IPackageBackend
{
    private readonly string _runtimeRoot;

    public Ps4FpkgBackend(string runtimeRoot)
    {
        _runtimeRoot = runtimeRoot;
    }

    public string Name => "PS4 fPKG";

    public async Task<string> BuildAsync(
        DiscAnalysis analysis,
        string artworkPath,
        string stagingDirectory,
        string outputDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        if (analysis.Serial is null)
            throw new InvalidDataException(
                "The PS1 serial could not be detected. Refusing to create a package with guessed metadata.");

        progress?.Report("Validating PS1HD runtime...");
        var runtime = Ps1Runtime.Validate(_runtimeRoot);
        if (!runtime.IsValid)
            throw new InvalidDataException(
                "PS1HD runtime is incomplete. Missing: " + string.Join(", ", runtime.MissingFiles));

        cancellationToken.ThrowIfCancellationRequested();

        var app0 = Path.Combine(stagingDirectory, "app0");
        var sceSys = Path.Combine(app0, "sce_sys");
        var data = Path.Combine(app0, "data");
        Directory.CreateDirectory(sceSys);
        Directory.CreateDirectory(data);

        progress?.Report("Normalizing PS1 disc...");
        var normalized = await Ps1DiscNormalizer.NormalizeAsync(
            analysis,
            data,
            progress,
            cancellationToken);

        progress?.Report($"Normalized disc: {Path.GetFileName(normalized.BinPath)} ({normalized.TrackCount} track(s))");

        if (analysis.Tracks.Count > 0)
        {
            progress?.Report("Generating PS1 TOC...");
            var toc = Ps1TocWriter.Build(analysis.Tracks, new FileInfo(normalized.BinPath).Length);
            await File.WriteAllBytesAsync(Path.Combine(data, "disc1.toc"), toc, cancellationToken);
        }

        progress?.Report("Preparing PS1HD configuration...");
        await File.WriteAllTextAsync(
            Path.Combine(app0, "config-title.txt"),
            Ps1ConfigBuilder.Build(analysis.Serial, analysis.Region),
            cancellationToken);

        progress?.Report("Preparing PS4 metadata...");
        var title = analysis.Serial;
        var paramSfo = Ps4Metadata.BuildParamSfo(title, analysis.Serial);
        await File.WriteAllBytesAsync(Path.Combine(sceSys, "param.sfo"), paramSfo, cancellationToken);

        progress?.Report("Generating keystone...");
        const string packagePasscode = "00000000000000000000000000000000";
        await File.WriteAllBytesAsync(
            Path.Combine(sceSys, "keystone"),
            Keystone.Build(packagePasscode),
            cancellationToken);

        progress?.Report("Preparing artwork...");
        ArtworkProcessor.CreateIcon(artworkPath, sceSys);

        progress?.Report("Preparing runtime files...");
        foreach (var relative in Ps1Runtime.RequiredFiles)
        {
            var source = Path.Combine(_runtimeRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var target = Path.Combine(app0, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target, true);
        }

        progress?.Report("Building inner PFS...");
        var innerPfs = Path.Combine(stagingDirectory, "inner.pfs");
        await InnerPfsWriter.BuildAsync(app0, innerPfs, progress, cancellationToken);

        progress?.Report("Wrapping inner PFS as PFSC...");
        var pfsc = Path.Combine(stagingDirectory, "pfs_image.dat");
        await PfscWriter.WrapAsync(innerPfs, pfsc, true, progress, cancellationToken);

        progress?.Report("Inner PFS/PFSC complete.");

        // Do not silently emit a fake .pkg. The remaining stage is the signed,
        // encrypted outer PFS plus final PKG entry table and metadata.
        throw new NotSupportedException(
            "Inner PFS and PFSC are complete. Outer PFS/PKG serialization is not implemented yet.");
    }
}

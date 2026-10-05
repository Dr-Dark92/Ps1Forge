namespace Ps1Forge.Core;

public sealed class ConversionService
{
    private readonly IPackageBackend _backend;

    public ConversionService(IPackageBackend backend)
    {
        _backend = backend;
    }

    public async Task<ConversionResult> ConvertAsync(
        ConversionRequest request,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        var log = new List<string>();
        void Write(string line)
        {
            log.Add(line);
            progress?.Report(line);
        }

        Write("Analyzing disc image...");
        var analysis = DiscAnalyzer.Analyze(request.DiscPath);

        Write($"Format: {analysis.Format}");
        Write($"Serial: {analysis.Serial ?? "Unknown"}");
        Write($"Region: {analysis.Region}");

        if (analysis.Tracks.Count > 0)
            Write($"Tracks: {analysis.Tracks.Count}");

        foreach (var warning in analysis.Warnings)
            Write("Warning: " + warning);

        var workRoot = Path.Combine(
            Path.GetTempPath(),
            "Ps1Forge",
            Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(workRoot);

        try
        {
            Write($"Packaging backend: {_backend.Name}");
            var output = await _backend.BuildAsync(
                analysis,
                request.ArtworkPath,
                workRoot,
                request.OutputDirectory,
                new Progress<string>(Write),
                cancellationToken);

            Write("Output: " + output);
            return new ConversionResult(output, analysis, log);
        }
        finally
        {
            try { Directory.Delete(workRoot, true); }
            catch { }
        }
    }
}

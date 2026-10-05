using System.Text.Json;

namespace Ps1Forge.Core;

public sealed class ManifestPackageBackend : IPackageBackend
{
    public string Name => "Pipeline validation backend";

    public async Task<string> BuildAsync(
        DiscAnalysis analysis,
        string artworkPath,
        string stagingDirectory,
        string outputDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(stagingDirectory);

        progress?.Report("Preparing artwork...");
        var icon = ArtworkProcessor.CreateIcon(artworkPath, stagingDirectory);

        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Writing conversion manifest...");

        var safeName = analysis.Serial ?? Path.GetFileNameWithoutExtension(analysis.SelectedPath);
        foreach (var c in Path.GetInvalidFileNameChars())
            safeName = safeName.Replace(c, '_');

        var output = Path.Combine(outputDirectory, safeName + ".ps1forge.json");

        var payload = new
        {
            schema = 1,
            createdUtc = DateTimeOffset.UtcNow,
            source = analysis.SelectedPath,
            effectiveSource = analysis.EffectivePath,
            format = analysis.Format,
            serial = analysis.Serial,
            region = analysis.Region,
            tracks = analysis.Tracks,
            artwork = icon,
            note = "Pipeline validation artifact. Replace ManifestPackageBackend with a tested PS4 fPKG backend to emit .pkg."
        };

        await File.WriteAllTextAsync(
            output,
            JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }),
            cancellationToken);

        progress?.Report("Pipeline validation complete.");
        return output;
    }
}

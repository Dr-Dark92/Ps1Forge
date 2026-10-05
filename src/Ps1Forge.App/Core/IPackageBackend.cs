namespace Ps1Forge.Core;

public interface IPackageBackend
{
    string Name { get; }
    Task<string> BuildAsync(
        DiscAnalysis analysis,
        string artworkPath,
        string stagingDirectory,
        string outputDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken);
}

using System.Text;

namespace Ps1Forge.Core;

public sealed record NormalizedDisc(
    string BinPath,
    string CuePath,
    int TrackCount);

public static class Ps1DiscNormalizer
{
    public static async Task<NormalizedDisc> NormalizeAsync(
        DiscAnalysis analysis,
        string dataDirectory,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(dataDirectory);
        var outBin = Path.Combine(dataDirectory, "disc1.bin");
        var outCue = Path.Combine(dataDirectory, "disc1.cue");

        if (analysis.Tracks.Count > 0)
        {
            progress?.Report("Normalizing CUE/BIN tracks...");
            var uniqueFiles = analysis.Tracks
                .Select(t => t.FilePath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            await using (var output = new FileStream(outBin, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true))
            {
                foreach (var source in uniqueFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
                    await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
                }
            }

            await File.WriteAllTextAsync(
                outCue,
                BuildPackageCue(analysis.Tracks),
                Encoding.ASCII,
                cancellationToken);

            return new NormalizedDisc(outBin, outCue, analysis.Tracks.Count);
        }

        progress?.Report("Copying disc image...");
        await CopyAsync(analysis.SelectedPath, outBin, cancellationToken);
        await File.WriteAllTextAsync(
            outCue,
            "FILE \"disc1.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n",
            Encoding.ASCII,
            cancellationToken);

        return new NormalizedDisc(outBin, outCue, 1);
    }

    private static string BuildPackageCue(IReadOnlyList<CueTrack> tracks)
    {
        var sb = new StringBuilder();
        sb.AppendLine("FILE \"disc1.bin\" BINARY");

        foreach (var track in tracks)
        {
            sb.AppendLine($"  TRACK {track.Number:00} {track.Mode}");
            sb.AppendLine($"    INDEX 01 {track.Index01 ?? "00:00:00"}");
        }

        return sb.ToString().Replace("\n", "\r\n");
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
    }
}

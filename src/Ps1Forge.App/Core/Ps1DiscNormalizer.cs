using System.Text;

namespace Ps1Forge.Core;

public sealed record NormalizedDisc(
    string BinPath,
    string CuePath,
    int TrackCount,
    IReadOnlyList<CueTrack> Tracks);

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
                BuildPackageCue(AdjustTracks(analysis.Tracks)),
                Encoding.ASCII,
                cancellationToken);

            var adjustedTracks=AdjustTracks(analysis.Tracks);
            return new NormalizedDisc(outBin,outCue,adjustedTracks.Count,adjustedTracks);
        }

        var extension = Path.GetExtension(analysis.SelectedPath).ToLowerInvariant();
        if (extension is ".iso" or ".img")
            throw new NotSupportedException(
                "ISO/IMG normalization is not enabled yet because sector layout must be verified. Use a CUE/BIN dump for the first Ps1Forge builds.");

        progress?.Report("Copying disc image...");
        await CopyAsync(analysis.SelectedPath, outBin, cancellationToken);
        await File.WriteAllTextAsync(
            outCue,
            "FILE \"disc1.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n",
            Encoding.ASCII,
            cancellationToken);

        return new NormalizedDisc(outBin,outCue,1,[new CueTrack(outBin,1,"MODE2/2352","00:00:00")]);
    }

    private static IReadOnlyList<CueTrack> AdjustTracks(IReadOnlyList<CueTrack> tracks)
    {
        var result=new List<CueTrack>(tracks.Count);
        long accumulatedSectors=0;
        string? currentFile=null;
        foreach(var track in tracks)
        {
            if(!string.Equals(currentFile,track.FilePath,StringComparison.OrdinalIgnoreCase))
            {
                if(currentFile is not null)
                    accumulatedSectors+=new FileInfo(currentFile).Length/2352;
                currentFile=track.FilePath;
            }
            var index01=SectorsToCueTime(accumulatedSectors+CueTimeToSectors(track.Index01));
            var index00=string.IsNullOrWhiteSpace(track.Index00)
                ? null
                : SectorsToCueTime(accumulatedSectors+CueTimeToSectors(track.Index00));
            result.Add(track with { FilePath="disc1.bin", Index01=index01, Index00=index00 });
        }
        return result;
    }

    private static string BuildPackageCue(IReadOnlyList<CueTrack> tracks)
    {
        var sb = new StringBuilder();
        sb.Append("FILE \"disc1.bin\" BINARY\r\n");

        foreach (var track in tracks)
        {
            var absolute01=CueTimeToSectors(track.Index01);
            sb.Append($"  TRACK {track.Number:00} {track.Mode}\r\n");
            if(!string.IsNullOrWhiteSpace(track.Index00))
            {
                var absolute00=CueTimeToSectors(track.Index00);
                sb.Append($"    INDEX 00 {SectorsToCueTime(absolute00)}\r\n");
            }
            sb.Append($"    INDEX 01 {SectorsToCueTime(absolute01)}\r\n");
        }
        return sb.ToString();
    }

    private static long CueTimeToSectors(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var p = value.Split(':');
        if (p.Length != 3 ||
            !int.TryParse(p[0], out var m) ||
            !int.TryParse(p[1], out var s) ||
            !int.TryParse(p[2], out var f))
            throw new InvalidDataException($"Invalid CUE INDEX time: {value}");
        return ((long)m * 60 + s) * 75 + f;
    }

    private static string SectorsToCueTime(long sectors)
    {
        var m = sectors / (60 * 75);
        var s = (sectors / 75) % 60;
        var f = sectors % 75;
        return $"{m:00}:{s:00}:{f:00}";
    }

    private static async Task CopyAsync(string source, string target, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
        await using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, true);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
    }
}

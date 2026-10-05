using System.Text.RegularExpressions;

namespace Ps1Forge.Core;

public static partial class CueParser
{
    [GeneratedRegex("^\\s*FILE\\s+\"([^\"]+)\"\\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex FileRegex();

    [GeneratedRegex("^\\s*TRACK\\s+(\\d+)\\s+(.+)$", RegexOptions.IgnoreCase)]
    private static partial Regex TrackRegex();

    [GeneratedRegex("^\\s*INDEX\\s+(00|01)\\s+(\\d{2}:\\d{2}:\\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex IndexRegex();

    public static IReadOnlyList<CueTrack> Parse(string cuePath)
    {
        var baseDir = Path.GetDirectoryName(Path.GetFullPath(cuePath))!;
        var tracks = new List<CueTrack>();
        string? currentFile = null;
        int currentTrackIndex = -1;

        foreach (var raw in File.ReadLines(cuePath))
        {
            var fileMatch = FileRegex().Match(raw);
            if (fileMatch.Success)
            {
                currentFile = Path.GetFullPath(Path.Combine(baseDir, fileMatch.Groups[1].Value));
                currentTrackIndex = -1;
                continue;
            }

            var trackMatch = TrackRegex().Match(raw);
            if (trackMatch.Success)
            {
                if (currentFile is null)
                    throw new InvalidDataException("CUE contains TRACK before FILE.");

                tracks.Add(new CueTrack(
                    currentFile,
                    int.Parse(trackMatch.Groups[1].Value),
                    trackMatch.Groups[2].Value.Trim(),
                    null,
                    null));

                currentTrackIndex = tracks.Count - 1;
                continue;
            }

            var indexMatch = IndexRegex().Match(raw);
            if (indexMatch.Success && currentTrackIndex >= 0)
            {
                var old = tracks[currentTrackIndex];
                var index=indexMatch.Groups[1].Value;
                var time=indexMatch.Groups[2].Value;
                tracks[currentTrackIndex] = index=="00"
                    ? old with { Index00=time }
                    : old with { Index01=time };
            }
        }

        if (tracks.Count == 0)
            throw new InvalidDataException("No tracks were found in the CUE file.");

        var missing = tracks
            .Select(t => t.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(path))
            .ToArray();

        if (missing.Length > 0)
            throw new FileNotFoundException("CUE references missing track file(s): " + string.Join(", ", missing));

        return tracks;
    }
}

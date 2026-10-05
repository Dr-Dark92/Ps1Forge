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

        foreach(var track in tracks)
        {
            if(string.IsNullOrWhiteSpace(track.Index01))
                throw new InvalidDataException($"CUE track {track.Number:00} is missing INDEX 01.");
            ValidateCueTime(track.Index01,$"track {track.Number:00} INDEX 01");
            if(!string.IsNullOrWhiteSpace(track.Index00))
            {
                ValidateCueTime(track.Index00,$"track {track.Number:00} INDEX 00");
                if(CueTimeToFrames(track.Index00)>CueTimeToFrames(track.Index01))
                    throw new InvalidDataException($"CUE track {track.Number:00} INDEX 00 occurs after INDEX 01.");
            }
        }

        var missing = tracks
            .Select(t => t.FilePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(path => !File.Exists(path))
            .ToArray();

        if (missing.Length > 0)
            throw new FileNotFoundException("CUE references missing track file(s): " + string.Join(", ", missing));

        foreach(var path in tracks.Select(t=>t.FilePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var length=new FileInfo(path).Length;
            if(length==0 || length%2352!=0)
                throw new InvalidDataException($"CUE track file is not a non-empty raw 2352-byte-sector image: {path}");
        }

        return tracks;
    }

    private static void ValidateCueTime(string value,string label)
    {
        var p=value.Split(':');
        if(p.Length!=3 || !int.TryParse(p[0],out var m) || !int.TryParse(p[1],out var s) || !int.TryParse(p[2],out var f)
            || m<0 || s is <0 or >=60 || f is <0 or >=75)
            throw new InvalidDataException($"Invalid CUE time for {label}: {value}");
    }

    private static long CueTimeToFrames(string value)
    {
        var p=value.Split(':');
        return ((long)int.Parse(p[0])*60+int.Parse(p[1]))*75+int.Parse(p[2]);
    }
}

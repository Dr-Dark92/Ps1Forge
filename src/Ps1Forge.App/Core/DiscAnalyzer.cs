using System.Text;
using System.Text.RegularExpressions;

namespace Ps1Forge.Core;

public static partial class DiscAnalyzer
{
    private static readonly string[] SupportedExtensions = [".iso", ".img", ".cue", ".bin"];

    [GeneratedRegex(@"(?:BOOT\s*=\s*cdrom:\\?[^\r\n]*?\\)?(?<id>[A-Z]{4})[_-]?(?<a>\d{3})[\._-]?(?<b>\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex SerialRegex();

    public static DiscAnalysis Analyze(string selectedPath)
    {
        var full = Path.GetFullPath(selectedPath);
        if (!File.Exists(full))
            throw new FileNotFoundException("Disc image was not found.", full);

        var ext = Path.GetExtension(full).ToLowerInvariant();
        if (!SupportedExtensions.Contains(ext))
            throw new NotSupportedException($"Unsupported disc format: {ext}");

        var warnings = new List<string>();
        IReadOnlyList<CueTrack> tracks = Array.Empty<CueTrack>();
        var effective = full;

        if (ext == ".cue")
        {
            tracks = CueParser.Parse(full);
            effective = tracks[0].FilePath;
        }
        else if (ext == ".bin")
        {
            var siblingCue = Path.ChangeExtension(full, ".cue");
            if (File.Exists(siblingCue))
            {
                tracks = CueParser.Parse(siblingCue);
                effective = siblingCue;
                warnings.Add("Matching CUE detected and will be preferred over the raw BIN.");
            }
        }

        var serial = TryFindSerial(full, tracks);
        var region = DetectRegion(serial);

        if (serial is null)
            warnings.Add("PS1 serial could not be detected automatically.");

        return new DiscAnalysis(
            full,
            effective,
            ext.TrimStart('.').ToUpperInvariant(),
            serial,
            region,
            tracks,
            warnings);
    }

    private static string? TryFindSerial(string selectedPath, IReadOnlyList<CueTrack> tracks)
    {
        var candidates = new List<string>();

        if (Path.GetExtension(selectedPath).Equals(".cue", StringComparison.OrdinalIgnoreCase))
            candidates.AddRange(tracks.Select(t => t.FilePath).Distinct(StringComparer.OrdinalIgnoreCase));
        else
            candidates.Add(selectedPath);

        foreach (var path in candidates)
        {
            var serial = ScanBinaryForSerial(path);
            if (serial is not null)
                return serial;
        }

        return null;
    }

    private static string? ScanBinaryForSerial(string path)
    {
        const int maxScanBytes = 64 * 1024 * 1024;
        using var stream = File.OpenRead(path);
        var count = (int)Math.Min(stream.Length, maxScanBytes);
        var buffer = new byte[count];
        var read = 0;

        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n <= 0) break;
            read += n;
        }

        var text = Encoding.Latin1.GetString(buffer, 0, read);
        var match = SerialRegex().Match(text);
        if (!match.Success)
            return null;

        return $"{match.Groups["id"].Value.ToUpperInvariant()}-{match.Groups["a"].Value}{match.Groups["b"].Value}";
    }

    private static string DetectRegion(string? serial)
    {
        if (serial is null) return "Unknown";

        if (serial.StartsWith("SLUS", StringComparison.OrdinalIgnoreCase) ||
            serial.StartsWith("SCUS", StringComparison.OrdinalIgnoreCase))
            return "NTSC-U";

        if (serial.StartsWith("SLES", StringComparison.OrdinalIgnoreCase) ||
            serial.StartsWith("SCES", StringComparison.OrdinalIgnoreCase))
            return "PAL";

        if (serial.StartsWith("SLPS", StringComparison.OrdinalIgnoreCase) ||
            serial.StartsWith("SCPS", StringComparison.OrdinalIgnoreCase) ||
            serial.StartsWith("SLPM", StringComparison.OrdinalIgnoreCase))
            return "NTSC-J";

        return "Unknown";
    }
}

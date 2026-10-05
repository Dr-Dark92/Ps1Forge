namespace Ps1Forge.Core;

public sealed record CueTrack(
    string FilePath,
    int Number,
    string Mode,
    string? Index01,
    string? Index00 = null);

public sealed record DiscAnalysis(
    string SelectedPath,
    string EffectivePath,
    string Format,
    string? Serial,
    string Region,
    IReadOnlyList<CueTrack> Tracks,
    IReadOnlyList<string> Warnings);

public sealed record ConversionRequest(
    string DiscPath,
    string ArtworkPath,
    string OutputDirectory);

public sealed record ConversionResult(
    string OutputPath,
    DiscAnalysis Analysis,
    IReadOnlyList<string> Log);

namespace Ps1Forge.Core;

public sealed record RuntimeValidation(
    bool IsValid,
    string Root,
    IReadOnlyList<string> MissingFiles);

public static class Ps1Runtime
{
    public static readonly string[] RequiredFiles =
    [
        "eboot.bin",
        "sce_module/libc.prx",
        "sce_module/libSceFios2.prx",
        "sce_module/libSceNpToolkit2.prx"
    ];

    public static RuntimeValidation Validate(string root)
    {
        root = Path.GetFullPath(root);
        var missing = RequiredFiles
            .Where(relative => !File.Exists(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar))))
            .ToArray();

        return new RuntimeValidation(missing.Length == 0, root, missing);
    }
}

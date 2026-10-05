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
            .ToList();

        var bios = Path.Combine(root, "bios");
        if (!Directory.Exists(bios) || !Directory.EnumerateFiles(bios, "*", SearchOption.AllDirectories).Any())
            missing.Add("bios/ (non-empty directory)");

        return new RuntimeValidation(missing.Count == 0, root, missing);
    }
}

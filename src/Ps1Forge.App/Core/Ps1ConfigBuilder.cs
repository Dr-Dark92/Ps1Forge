using System.Text;

namespace Ps1Forge.Core;

public static class Ps1ConfigBuilder
{
    public static string Build(string titleId, string region)
    {
        var normalized = titleId.Replace("-", "", StringComparison.Ordinal)
                                .Replace("_", "", StringComparison.Ordinal)
                                .Replace(".", "", StringComparison.Ordinal)
                                .ToUpperInvariant();

        var ps1HdRegion = region switch
        {
            "PAL" => "SCEE",
            "NTSC-J" => "SCEI",
            _ => "SCEA"
        };

        var b = new StringBuilder();
        b.AppendLine("# Ps1Forge generated PS1HD configuration");
        b.AppendLine("--scale=6");
        b.AppendLine("--has-shown-start-select-help=0");
        b.AppendLine();
        b.AppendLine("# following settings are machine-generated");
        b.AppendLine($"--ps1-title-id={normalized}");
        b.AppendLine($"--title-id={normalized}");
        b.AppendLine($"--region=\"{ps1HdRegion}\"");
        b.AppendLine("--image=\"data/disc1.bin\"");
        b.AppendLine("--ps4-trophies=0");
        b.AppendLine("--ps5-uds=0");
        b.AppendLine("--trophies=0");
        b.AppendLine("--bios-dir=\"bios\"");
        return b.ToString();
    }
}

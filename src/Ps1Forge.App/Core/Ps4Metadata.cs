using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public static class Ps4Metadata
{
    public static string NormalizeTitleId(string ps1Serial)
    {
        var clean = new string(ps1Serial.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
        if (clean.Length < 9)
            throw new ArgumentException("Invalid PS1 serial.", nameof(ps1Serial));

        // PS4 title IDs require a 4-letter prefix plus five digits.
        return clean[..9];
    }

    public static string ContentId(string ps1Serial)
    {
        var titleId = NormalizeTitleId(ps1Serial);
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes("ps1_" + titleId));
        var suffix = Convert.ToHexString(hash)[..16];
        return $"UP9000-{titleId}_00-{suffix}";
    }

    public static byte[] BuildParamSfo(string title, string ps1Serial)
    {
        var titleId = NormalizeTitleId(ps1Serial);
        var contentId = ContentId(ps1Serial);

        var sfo = new SfoWriter();
        sfo.AddInt32("APP_TYPE", 1);
        sfo.AddString("CATEGORY", "gd", 4);
        sfo.AddString("CONTENT_ID", contentId, 48);
        sfo.AddString("FORMAT", "obs", 4);
        sfo.AddString("TITLE", title, 128);
        sfo.AddString("TITLE_ID", titleId, 12);
        sfo.AddString("VERSION", "01.00", 8);
        return sfo.Build();
    }
}

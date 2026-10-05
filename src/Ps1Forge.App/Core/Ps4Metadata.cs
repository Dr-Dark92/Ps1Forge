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

    public static byte[] BuildParamSfo(string title, string ps1Serial, ulong packageSize = 0)
    {
        var titleId = NormalizeTitleId(ps1Serial);
        var contentId = ContentId(ps1Serial);

        var sfo = new SfoWriter();
        sfo.AddInt32("APP_TYPE", 1);
        sfo.AddString("APP_VER", "01.00", 8);
        sfo.AddInt32("ATTRIBUTE", 0);
        sfo.AddInt32("ATTRIBUTE2", 0x400);
        sfo.AddString("CATEGORY", "gd", 4);
        sfo.AddString("CONTENT_ID", contentId, 48);
        sfo.AddInt32("DEV_FLAG", 0);
        sfo.AddInt32("DOWNLOAD_DATA_SIZE", 0);
        sfo.AddString("FORMAT", "obs", 4);
        sfo.AddInt32("PARENTAL_LEVEL", 5);
        var img0SizeMiB=(packageSize+0xFFFFFUL)/(1024UL*1024UL);
        sfo.AddString("PUBTOOLINFO", $"c_date={DateTime.UtcNow:yyyyMMdd},sdk_ver=05050000,st_type=digital50,img0_l0_size={img0SizeMiB},img0_l1_size=0,img0_sc_ksize=512,img0_pc_ksize=576", 512);
        sfo.AddInt32("PUBTOOLMINVER", 0x02990000);
        sfo.AddInt32("PUBTOOLVER", 0x03380000);
        sfo.AddInt32("SYSTEM_VER", 0x05050000);
        sfo.AddString("TITLE", title, 128);
        sfo.AddString("TITLE_ID", titleId, 12);
        sfo.AddString("VERSION", "01.00", 8);
        return sfo.Build();
    }
}

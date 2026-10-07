namespace Ps1Forge.Core;

public sealed record PkgPreparedCrypto(
    byte[] EntryKeys,
    byte[] ImageKey,
    byte[] SignedLicenseDat,
    Func<byte[],byte[]> HeaderWrapper);

public static class PkgEntryBuilder
{
    public static List<PkgBodyEntry> Build(
        string contentId,
        byte[] paramSfo,
        byte[] icon0,
        ulong innerPfsSize,
        PkgPreparedCrypto crypto,
        byte[] npbindDat,
        byte[]? pic1Png = null,
        byte[]? shareParamJson = null,
        byte[]? saveDataPng = null,
        byte[]? changeInfoXml = null,
        byte[]? icon0Dds = null,
        byte[]? pic1Dds = null)
    {
        RequireSize(crypto.EntryKeys,0x800,nameof(crypto.EntryKeys));
        RequireSize(crypto.ImageKey,0x100,nameof(crypto.ImageKey));
        RequireSize(crypto.SignedLicenseDat,0x400,nameof(crypto.SignedLicenseDat));

        var entries = new List<PkgBodyEntry>
        {
            new(PkgBodyBuilder.EntryKeys,"",crypto.EntryKeys,0x60000000),
            new(PkgBodyBuilder.ImageKey,"",crypto.ImageKey,0xE0000000,3u<<12),
            new(PkgBodyBuilder.GeneralDigests,"",new byte[0x180],0x60000000),
            new(PkgBodyBuilder.Metas,"",[] ,0x60000000),
            new(PkgBodyBuilder.Digests,"",[] ,0x40000000),
            new(PkgBodyBuilder.EntryNames,"",[] ,0x40000000),
            new(PkgBodyBuilder.PlayGoChunkDat,"playgo-chunk.dat",PlayGo.BuildChunkDat(contentId,0,innerPfsSize)),
            new(PkgBodyBuilder.PlayGoChunkSha,"playgo-chunk.sha",[]),
            new(PkgBodyBuilder.PlayGoManifest,"playgo-manifest.xml",PlayGo.Manifest()),
            new(PkgBodyBuilder.LicenseDat,"",crypto.SignedLicenseDat,0x80000000,3u<<12),
            new(PkgBodyBuilder.LicenseInfo,"",PkgLicense.BuildLicenseInfo(contentId),0x80000000,2u<<12),
            new(PkgBodyBuilder.ParamSfo,"param.sfo",paramSfo),
            new(PkgBodyBuilder.PsReservedDat,"",new byte[0x2000]),
            new(PkgBodyBuilder.Icon0Png,"icon0.png",icon0)
        };

        RequireSize(npbindDat,0x214,nameof(npbindDat));
        entries.Add(new PkgBodyEntry(PkgBodyBuilder.NpBindDat,"",npbindDat,0x80000000,3u<<12));

        if(pic1Png is { Length: > 0 })
            entries.Add(new PkgBodyEntry(PkgBodyBuilder.Pic1Png,"pic1.png",pic1Png));
        if(shareParamJson is { Length: > 0 })
            entries.Add(new PkgBodyEntry(PkgBodyBuilder.ShareParamJson,"shareparam.json",shareParamJson));
        if(saveDataPng is { Length: > 0 })
            entries.Add(new PkgBodyEntry(PkgBodyBuilder.SaveDataPng,"save_data.png",saveDataPng));
        if(changeInfoXml is { Length: > 0 })
            entries.Add(new PkgBodyEntry(PkgBodyBuilder.ChangeInfoXml,"changeinfo/changeinfo.xml",changeInfoXml));
        if(icon0Dds is { Length: > 0 })
            entries.Add(new PkgBodyEntry(PkgBodyBuilder.Icon0Dds,"icon0.dds",icon0Dds));
        if(pic1Dds is { Length: > 0 })
            entries.Add(new PkgBodyEntry(PkgBodyBuilder.Pic1Dds,"pic1.dds",pic1Dds));

        return entries;
    }

    private static void RequireSize(byte[] value,int size,string name)
    {
        if(value.Length!=size) throw new ArgumentException($"{name} must be exactly 0x{size:X} bytes.",name);
    }
}

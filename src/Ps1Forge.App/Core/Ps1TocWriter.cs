namespace Ps1Forge.Core;

public static class Ps1TocWriter
{
    public static byte[] Build(IReadOnlyList<CueTrack> tracks, long imageSize)
    {
        if (tracks.Count == 0) return Array.Empty<byte>();

        var toc = new List<byte>
        {
            0x41,0x00,0xa0,0x00,0x00,0x00,0x00,0x01,0x20,0x00,
            0x01,0x00,0xa1,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
            0x01,0x00,0xa2,0x00,0x00,0x00,0x00,0x00,0x00,0x00
        };

        toc[17] = Bcd(tracks.Count);
        var (em, es, ef) = MsfAlt((int)(imageSize / 2352) + 150);
        toc[27] = Bcd(em); toc[28] = Bcd(es); toc[29] = Bcd(ef);

        for (var i = 0; i < tracks.Count; i++)
        {
            var entry = new byte[10];
            entry[0] = i == 0 ? (byte)0x41 : (byte)0x01;
            entry[2] = Bcd(i + 1);
            if(i>0 && !string.IsNullOrWhiteSpace(tracks[i].Index00))
            {
                var pregap=CueTimeToLba(tracks[i].Index00)+150;
                var (pm,ps,pf)=MsfAlt(pregap);
                entry[3]=Bcd(pm); entry[4]=Bcd(ps); entry[5]=Bcd(pf);
            }

            var lba = CueTimeToLba(tracks[i].Index01) + 150;
            if (i == 0) lba = 150;
            var (m,s,f) = i == 0 ? Msf(lba) : MsfAlt(lba);
            entry[7] = Bcd(m); entry[8] = Bcd(s); entry[9] = Bcd(f);
            toc.AddRange(entry);
        }
        return toc.ToArray();
    }

    private static int CueTimeToLba(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return 0;
        var p = value.Split(':');
        return p.Length == 3 &&
               int.TryParse(p[0], out var m) &&
               int.TryParse(p[1], out var s) &&
               int.TryParse(p[2], out var f)
            ? ((m * 60) + s) * 75 + f : 0;
    }

    private static byte Bcd(int value) => (byte)((value % 10) + 16 * ((value / 10) % 10));

    private static (int,int,int) Msf(int sectors)
    {
        sectors = Math.Max(0, sectors);
        return (sectors / 4500, (sectors / 75) % 60, sectors % 75);
    }

    private static (int,int,int) MsfAlt(int sectors)
    {
        var (m,s,f) = Msf(sectors);
        if (f != 0) return (m,s,f);
        f = 75;
        if (s != 0) s--;
        else { s = 59; m--; }
        return m < 0 ? (0,0,0) : (m,s,f);
    }
}

using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public sealed record PkgBodyEntry(uint Id, string Name, byte[] Data, uint Flags1 = 0, uint Flags2 = 0)
{
    public uint NameOffset { get; set; }
    public uint DataOffset { get; set; }
    public uint LogicalSize { get; set; }
    public uint DataSize => LogicalSize == 0 ? checked((uint)Data.Length) : LogicalSize;
}

public sealed record PkgBodyLayout(
    IReadOnlyList<PkgBodyEntry> Entries,
    byte[] EntryNames,
    ulong BodyOffset,
    ulong BodySize,
    ulong PfsOffset,
    ulong PackageSize);

public static class PkgBodyBuilder
{
    public const uint Digests = 0x00000001;
    public const uint EntryKeys = 0x00000010;
    public const uint ImageKey = 0x00000020;
    public const uint GeneralDigests = 0x00000080;
    public const uint Metas = 0x00000100;
    public const uint EntryNames = 0x00000200;
    public const uint LicenseDat = 0x00000400;
    public const uint LicenseInfo = 0x00000401;
    public const uint PsReservedDat = 0x00000409;
    public const uint ParamSfo = 0x00001000;
    public const uint PlayGoChunkDat = 0x00001001;
    public const uint PlayGoChunkSha = 0x00001002;
    public const uint PlayGoManifest = 0x00001003;
    public const uint Pic1Png = 0x00001006;
    public const uint SaveDataPng = 0x0000100D;
    public const uint Icon0Png = 0x00001200;
    public const uint Pic0Png = 0x00001220;

    public static PkgBodyLayout Plan(
        IEnumerable<PkgBodyEntry> input,
        ulong outerPfsSize)
    {
        // Preserve construction order for body placement; METAS themselves are
        // sorted by ID. This mirrors the reference builder.
        var entries = input.ToList();
        StabilizePlayGoShaSize(entries, outerPfsSize);
        var names = BuildNames(entries);
        var count = entries.Count;

        var cursor = PkgHeader.BodyOffset;
        foreach (var e in entries)
        {
            cursor = Align(cursor, 16);
            e.DataOffset = checked((uint)cursor);
            e.LogicalSize = e.Id is Metas or Digests
                ? checked((uint)(count * 32))
                : checked((uint)e.Data.Length);
            cursor += e.LogicalSize;
        }

        var bodySize = Align(cursor, 0x80000) - PkgHeader.BodyOffset;
        var pfsOffset = PkgHeader.BodyOffset + bodySize;
        var packageSize = pfsOffset + outerPfsSize;
        return new(entries, names, PkgHeader.BodyOffset, bodySize, pfsOffset, packageSize);
    }

    public static byte[] BuildEntryNames(IEnumerable<PkgBodyEntry> entries) => BuildNames(entries);

    public static byte[] BuildMetas(IReadOnlyList<PkgBodyEntry> entries)
    {
        var sorted = entries.OrderBy(x => x.Id).ToList();
        var data = new byte[sorted.Count * 32];
        for (var i = 0; i < sorted.Count; i++)
        {
            var e = sorted[i];
            var s = data.AsSpan(i * 32, 32);
            BinaryPrimitives.WriteUInt32BigEndian(s[0..4], e.Id);
            BinaryPrimitives.WriteUInt32BigEndian(s[4..8], e.NameOffset);
            BinaryPrimitives.WriteUInt32BigEndian(s[8..12], e.Flags1);
            BinaryPrimitives.WriteUInt32BigEndian(s[12..16], e.Flags2);
            BinaryPrimitives.WriteUInt32BigEndian(s[16..20], e.DataOffset);
            BinaryPrimitives.WriteUInt32BigEndian(s[20..24], e.DataSize);
        }
        return data;
    }

    public static byte[] BuildDigests(IReadOnlyList<PkgBodyEntry> entries)
    {
        var sorted = entries.OrderBy(x => x.Id).ToList();
        var data = new byte[sorted.Count * 32];
        for (var i = 1; i < sorted.Count; i++)
        {
            var hash = SHA256.HashData(sorted[i].Data);
            hash.CopyTo(data, i * 32);
        }
        return data;
    }

    public static async Task WriteBodyAsync(
        FileStream output,
        PkgBodyLayout layout,
        string contentId,
        string passcode,
        CancellationToken ct)
    {
        foreach (var e in layout.Entries)
        {
            output.Position = e.DataOffset;
            var data = e.Id switch
            {
                Metas => BuildMetas(layout.Entries),
                // DIGESTS must be calculated from the bytes actually stored in
                // the finished body (including encrypted entries). Slot zero is
                // the digest table itself, so write a zero placeholder here and
                // let PkgFinalizer populate it after body serialization.
                Digests => new byte[checked((int)e.DataSize)],
                _ => e.Data
            };
            if (data.Length != e.DataSize)
                throw new InvalidDataException($"PKG entry 0x{e.Id:X8} planned {e.DataSize} bytes but produced {data.Length}.");
            if ((e.Flags1 & 0x80000000u) != 0)
                data = PkgEntryCrypto.Encrypt(e.Id, e.NameOffset, e.Flags1, e.Flags2, e.DataOffset, data, contentId, passcode);
            await output.WriteAsync(data, ct);
        }
        if ((ulong)output.Length < layout.PfsOffset)
            output.SetLength((long)layout.PfsOffset);
    }

    private static void StabilizePlayGoShaSize(List<PkgBodyEntry> entries,ulong outerPfsSize)
    {
        var chunkSha=entries.FirstOrDefault(e=>e.Id==PlayGoChunkSha);
        if(chunkSha is null) return;
        for(var iteration=0;iteration<16;iteration++)
        {
            ulong cursor=PkgHeader.BodyOffset;
            foreach(var e in entries)
            {
                var size=e.Id is Metas or Digests
                    ? checked((uint)(entries.Count*32))
                    : checked((uint)e.Data.Length);
                cursor+=Align(size,16);
            }
            var bodySize=Align(cursor,0x80000)-PkgHeader.BodyOffset;
            var packageSize=PkgHeader.BodyOffset+bodySize+outerPfsSize;
            var next=checked((int)((packageSize/0x10000)*4));
            if(chunkSha.Data.Length==next) return;
            chunkSha.Data=new byte[next];
        }
        throw new InvalidDataException("PlayGo chunk SHA size did not stabilize.");
    }

    private static byte[] BuildNames(IEnumerable<PkgBodyEntry> entries)
    {
        var data = new List<byte> { 0 };
        var offsets = new Dictionary<string,uint>(StringComparer.Ordinal) { [""] = 0 };
        foreach (var e in entries)
        {
            if (string.IsNullOrEmpty(e.Name)) { e.NameOffset = 0; continue; }
            if (!offsets.TryGetValue(e.Name, out var off))
            {
                off = (uint)data.Count;
                offsets[e.Name] = off;
                data.AddRange(Encoding.UTF8.GetBytes(e.Name));
                data.Add(0);
            }
            e.NameOffset = off;
        }
        return data.ToArray();
    }

    private static ulong Align(ulong v, ulong a) => (v + a - 1) & ~(a - 1);
}

using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

public sealed class NpdrmEntryTests
{
    [Fact]
    public void Npbind_preserves_logical_size_and_uses_aligned_encrypted_storage()
    {
        var crypto = new PkgPreparedCrypto(new byte[0x800], new byte[0x100], new byte[0x400], x => x);
        var npbind = new byte[0x214];

        var entries = PkgEntryBuilder.Build("UP0000-SLUS01212_00-0000000000000000", new byte[0x67C], new byte[0x100], 0x10000, crypto, npbind);
        var layout = PkgBodyBuilder.Plan(entries, 0x10000);
        var entry = Assert.Single(layout.Entries, x => x.Id == PkgBodyBuilder.NpBindDat);

        Assert.Equal("npbind.dat", entry.Name);
        Assert.Equal(0x80000000u, entry.Flags1);
        Assert.Equal(0x00003000u, entry.Flags2);
        Assert.Equal(0x214u, entry.DataSize);
        Assert.Equal(0x220u, entry.StoredSize);

        var npbindIndex = layout.Entries.ToList().IndexOf(entry);
        Assert.Equal(layout.Entries.Count - 1, npbindIndex);
        Assert.True(layout.PfsOffset >= entry.DataOffset + entry.StoredSize);
    }

    [Fact]
    public void Reference_sce_sys_entries_keep_expected_ids_names_and_npbind_crypto_flags()
    {
        var crypto = new PkgPreparedCrypto(new byte[0x800], new byte[0x100], new byte[0x400], x => x);
        var npbind = new byte[0x214];
        var pic1 = new byte[] { 1, 2, 3 };
        var share = new byte[] { 4, 5, 6, 7 };
        var save = new byte[] { 8, 9 };

        var entries = PkgEntryBuilder.Build(
            "UP9000-SLUS01212_00-SLUS01212PSXFPKG",
            new byte[0x67C], new byte[0x100], 0x10000, crypto, npbind, pic1, share, save);
        var layout = PkgBodyBuilder.Plan(entries, 0x10000);

        var bind = Assert.Single(layout.Entries, x => x.Id == 0x403);
        Assert.Equal("npbind.dat", bind.Name);
        Assert.Equal(0x80000000u, bind.Flags1);
        Assert.Equal(0x3000u, bind.Flags2);
        Assert.Equal(0x214u, bind.DataSize);
        Assert.Equal(0x220u, bind.StoredSize);

        Assert.Equal("pic1.png", Assert.Single(layout.Entries, x => x.Id == 0x1006).Name);
        Assert.Equal("shareparam.json", Assert.Single(layout.Entries, x => x.Id == 0x100B).Name);
        Assert.Equal("save_data.png", Assert.Single(layout.Entries, x => x.Id == 0x100D).Name);
        Assert.Equal(18, layout.Entries.Count);
    }
}

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

        var next = layout.Entries[layout.Entries.ToList().IndexOf(entry) + 1];
        Assert.True(next.DataOffset >= entry.DataOffset + entry.StoredSize);
    }
}

using System.Buffers.Binary;
using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

/// <summary>
/// Diagnostic baseline for Test19. These tests document known deviations
/// from WORKING.pkg; they do not establish PS4 loader compatibility.
/// </summary>
public sealed class ReferenceCompatibilityTests
{
    [Fact]
    public void Test19_header_pfs_flags_differ_from_working_reference()
    {
        const string id = "UP9000-SLUS01212_00-SLUS01212PSXFPKG";
        var header = PkgHeader.Build(id, 21, 0xFC0, 0x1FE000,
            0x200000, 0x5CD0000, 0x5ED0000);
        var actual = BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(0x408, 8));
        Assert.Equal(0x80000000000003CCUL, actual);
        Assert.NotEqual(0xA0000000000003CCUL, actual);
    }

    [Fact]
    public void Test19_general_digest_flags_and_slots_differ_from_working_reference()
    {
        const string id = "UP9000-SLUS01212_00-SLUS01212PSXFPKG";
        var header = PkgHeader.Build(id, 21, 0xFC0, 0x1FE000,
            0x200000, 0x5CD0000, 0x5ED0000);
        var data = PkgGeneralDigests.Build(header, id, new byte[1660], new byte[32]);
        var flags = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x1C, 4));
        Assert.Equal(0x6Eu, flags);
        Assert.NotEqual(0xFEu, flags);
        Assert.All(data.AsSpan(0x80, 32).ToArray(), b => Assert.Equal((byte)0, b));
        Assert.All(data.AsSpan(0xE0, 32).ToArray(), b => Assert.Equal((byte)0, b));
    }
    [Fact]
    public void General_digest_reference_difference_is_two_flags_and_two_empty_slots()
    {
        const string id = "UP9000-SLUS01212_00-SLUS01212PSXFPKG";
        var header = PkgHeader.Build(id, 21, 0xFC0, 0x1FE000,
            0x200000, 0x5CD0000, 0x5ED0000);
        var data = PkgGeneralDigests.Build(header, id, new byte[1660], new byte[32]);
        var flags = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(0x1C, 4));

        // Observed comparison only: do not assume a one-to-one semantic
        // mapping between flags and digest slots without format evidence.
        Assert.Equal(0x6Eu, flags);
        Assert.Equal(0x90u, 0xFEu ^ flags);
        Assert.All(data.AsSpan(0x80, 32).ToArray(), b => Assert.Equal((byte)0, b));
        Assert.All(data.AsSpan(0xE0, 32).ToArray(), b => Assert.Equal((byte)0, b));
    }
}

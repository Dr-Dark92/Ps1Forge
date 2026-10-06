using Xunit;
using Ps1Forge.Core;

namespace Ps1Forge.Tests;

public sealed class SfoMetadataTests
{
    [Fact]
    public void Ps1ParamSfo_MatchesReferenceGeometry()
    {
        var sfo = Ps4Metadata.BuildParamSfo("SLUS01212", "SLUS01212", 106233856);
        Assert.Equal(1660, sfo.Length);\n        Assert.Equal("UP9000-SLUS01212_00-SLUS01212PSXFPKG", Ps4Metadata.ContentId("SLUS-01212"));

        var ascii = System.Text.Encoding.ASCII.GetString(sfo);
        Assert.Contains("img0_sc_ksize=2048", ascii);
        Assert.DoesNotContain("img0_sc_ksize=512", ascii);
        for (var i = 1; i <= 7; i++)
            Assert.Contains($"SERVICE_ID_ADDCONT_ADD_{i}", ascii);
    }
}

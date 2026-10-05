using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

public sealed class Ps1StagingTests
{
    [Fact]
    public async Task MultiBinCue_NormalizesAndProducesRuntimeInputs()
    {
        var root=Path.Combine(Path.GetTempPath(),"Ps1ForgeTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var a=Path.Combine(root,"track01.bin");
            var b=Path.Combine(root,"track02.bin");
            await File.WriteAllBytesAsync(a,new byte[2352*300]);
            await File.WriteAllBytesAsync(b,new byte[2352*150]);
            var cue=Path.Combine(root,"game.cue");
            await File.WriteAllTextAsync(cue,
                "FILE \"track01.bin\" BINARY\r\n"+
                "  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n"+
                "FILE \"track02.bin\" BINARY\r\n"+
                "  TRACK 02 AUDIO\r\n    INDEX 00 00:00:00\r\n    INDEX 01 00:02:00\r\n");

            var tracks=CueParser.Parse(cue);
            var analysis=new DiscAnalysis(cue,cue,"CUE/BIN","SCUS-94154","NTSC-U",tracks,[]);
            var data=Path.Combine(root,"data");
            var normalized=await Ps1DiscNormalizer.NormalizeAsync(analysis,data,null,CancellationToken.None);

            Assert.Equal(2,normalized.TrackCount);
            Assert.Equal(2352L*450,new FileInfo(normalized.BinPath).Length);
            Assert.Equal("00:04:00",normalized.Tracks[1].Index00);
            Assert.Equal("00:06:00",normalized.Tracks[1].Index01);
            var packageCue=await File.ReadAllTextAsync(normalized.CuePath);
            Assert.Contains("FILE \"disc1.bin\" BINARY",packageCue);
            Assert.DoesNotContain("track01.bin",packageCue);
            Assert.DoesNotContain("track02.bin",packageCue);

            var toc=Ps1TocWriter.Build(normalized.Tracks,new FileInfo(normalized.BinPath).Length);
            Assert.Equal(50,toc.Length);
            await File.WriteAllBytesAsync(Path.Combine(data,"disc1.toc"),toc);

            var config=Ps1ConfigBuilder.Build("SCUS-94154","NTSC-U");
            Assert.Contains("--ps1-title-id=SCUS94154",config);
            Assert.Contains("--region=\"SCEA\"",config);
            Assert.Contains("--image=\"data/disc1.bin\"",config);
            Assert.Contains("--bios-dir=\"bios\"",config);
        }
        finally
        {
            if(Directory.Exists(root)) Directory.Delete(root,true);
        }
    }
}

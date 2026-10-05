using System.Drawing;
using System.Drawing.Imaging;
using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

public sealed class ProductionBackendTests
{
    private sealed class TestCryptoProvider : IPkgCryptoProvider
    {
        public PkgPreparedCrypto Prepare(string contentId,string passcode,byte[] ekpfs) =>
            new(new byte[0x800],new byte[0x100],
                DebugRifSigner.Sign(PkgLicense.BuildUnsignedDebugRif(contentId)),
                digest=>{var w=new byte[0x100];digest.CopyTo(w,0);return w;});
    }

    [Fact]
    public async Task ProductionBackend_BuildsValidatedPkg_FromCueBin()
    {
        var root=Path.Combine(Path.GetTempPath(),"Ps1ForgeTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var runtime=Path.Combine(root,"runtime");
            foreach(var relative in Ps1Runtime.RequiredFiles)
            {
                var p=Path.Combine(runtime,relative.Replace('/',Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                await File.WriteAllBytesAsync(p,Enumerable.Range(0,4096).Select(i=>(byte)(i*19+relative.Length)).ToArray());
            }

            var bios=Path.Combine(runtime,"bios");
            Directory.CreateDirectory(bios);
            await File.WriteAllBytesAsync(Path.Combine(bios,"test.bin"),new byte[4096]);

            var bin=Path.Combine(root,"game.bin");
            await File.WriteAllBytesAsync(bin,new byte[2352*400]);
            var cue=Path.Combine(root,"game.cue");
            await File.WriteAllTextAsync(cue,
                "FILE \"game.bin\" BINARY\r\n  TRACK 01 MODE2/2352\r\n    INDEX 01 00:00:00\r\n");
            var tracks=CueParser.Parse(cue);
            var analysis=new DiscAnalysis(cue,cue,"CUE/BIN","SCUS-94154","NTSC-U",tracks,[]);

            var artwork=Path.Combine(root,"cover.png");
            using(var bitmap=new Bitmap(64,64))
                bitmap.Save(artwork,ImageFormat.Png);

            var staging=Path.Combine(root,"staging");
            var output=Path.Combine(root,"output");
            Directory.CreateDirectory(staging);
            var backend=new Ps4FpkgBackend(runtime,new TestCryptoProvider());
            var pkg=await backend.BuildAsync(analysis,artwork,staging,output,null,CancellationToken.None);

            Assert.True(File.Exists(pkg));
            Assert.Equal("SCUS94154.pkg",Path.GetFileName(pkg));
            var validation=await PkgValidator.ValidateFileAsync(pkg);
            Assert.True(validation.Valid,string.Join(Environment.NewLine,validation.Errors));
        }
        finally
        {
            if(Directory.Exists(root)) Directory.Delete(root,true);
        }
    }
}

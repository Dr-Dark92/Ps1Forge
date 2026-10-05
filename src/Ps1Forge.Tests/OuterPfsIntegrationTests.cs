using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

public sealed class OuterPfsIntegrationTests
{
    [Fact]
    public async Task OuterPfs_CanFeedValidatedPkg()
    {
        var root=Path.Combine(Path.GetTempPath(),"Ps1ForgeTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pfsc=Path.Combine(root,"pfs_image.dat");
            var pfscBytes=new byte[0x18000];
            for(var i=0;i<pfscBytes.Length;i++) pfscBytes[i]=(byte)(i*17+11);
            await File.WriteAllBytesAsync(pfsc,pfscBytes);

            var ekpfs=Enumerable.Range(0,32).Select(i=>(byte)i).ToArray();
            var seed=Enumerable.Range(0,16).Select(i=>(byte)(0xA0+i)).ToArray();
            var outer=Path.Combine(root,"outer.pfs");
            var outerSize=await OuterPfsWriter.BuildAsync(pfsc,outer,ekpfs,seed,0x30000,CancellationToken.None);
            // Reference signed-PFS layout: blocks 0..4 are metadata/reserved,`n            // block 5 is uroot, and the two PFSC data blocks begin at block 6.`n            Assert.Equal(0x80000,outerSize);
            Assert.Equal(0,outerSize%OuterPfsCrypto.BlockSize);

            const string contentId="UP9000-SLUS00000_00-0123456789ABCDEF";
            const string passcode="00000000000000000000000000000000";
            var param=Ps4Metadata.BuildParamSfo("SLUS-00000","SLUS-00000");
            var crypto=new PkgPreparedCrypto(
                new byte[0x800],new byte[0x100],
                DebugRifSigner.Sign(PkgLicense.BuildUnsignedDebugRif(contentId)),
                digest =>
                {
                    var wrapped=new byte[0x100];
                    digest.CopyTo(wrapped,0);
                    return wrapped;
                });
            var entries=PkgEntryBuilder.Build(contentId,param,new byte[]{1,2,3,4},0x30000,crypto);
            var pkg=Path.Combine(root,"outer-pfs-fixture.pkg");
            var validation=await PkgAssembler.AssembleAsync(
                pkg,outer,contentId,passcode,entries,param,
                _=>Ps4Metadata.BuildParamSfo("SLUS-00000","SLUS-00000"),
                crypto.HeaderWrapper);

            Assert.True(validation.Valid,string.Join(Environment.NewLine,validation.Errors));
            var reopened=await PkgValidator.ValidateFileAsync(pkg);
            Assert.True(reopened.Valid,string.Join(Environment.NewLine,reopened.Errors));
        }
        finally
        {
            if(Directory.Exists(root)) Directory.Delete(root,true);
        }
    }
}

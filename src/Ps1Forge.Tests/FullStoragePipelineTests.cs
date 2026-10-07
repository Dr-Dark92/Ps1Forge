using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

public sealed class FullStoragePipelineTests
{
    [Fact]
    public async Task InnerPfs_ThroughPfsc_OuterPfs_AndPkg_Validates()
    {
        var root=Path.Combine(Path.GetTempPath(),"Ps1ForgeTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var app0=Path.Combine(root,"app0");
            Directory.CreateDirectory(Path.Combine(app0,"data"));
            Directory.CreateDirectory(Path.Combine(app0,"sce_sys"));
            await File.WriteAllBytesAsync(Path.Combine(app0,"eboot.bin"),Enumerable.Range(0,8192).Select(i=>(byte)(i*13)).ToArray());
            await File.WriteAllTextAsync(Path.Combine(app0,"data","config-title.txt"),"--image=\"data/disc1.bin\"");
            await File.WriteAllBytesAsync(Path.Combine(app0,"data","disc1.bin"),Enumerable.Range(0,0x24000).Select(i=>(byte)(i*7+3)).ToArray());
            var param=Ps4Metadata.BuildParamSfo("SLUS-00000","SLUS-00000");
            await File.WriteAllBytesAsync(Path.Combine(app0,"sce_sys","param.sfo"),param);

            var inner=Path.Combine(root,"inner.pfs");
            await InnerPfsWriter.BuildAsync(app0,inner,null,CancellationToken.None);
            Assert.True(new FileInfo(inner).Length>0);

            var pfsc=Path.Combine(root,"pfs_image.dat");
            await PfscWriter.WrapAsync(inner,pfsc,false,null,CancellationToken.None);
            Assert.True(new FileInfo(pfsc).Length>new FileInfo(inner).Length);

            var ekpfs=Enumerable.Range(0,32).Select(i=>(byte)i).ToArray();
            var seed=Enumerable.Range(0,16).Select(i=>(byte)(0x40+i)).ToArray();
            var outer=Path.Combine(root,"outer.pfs");
            await OuterPfsWriter.BuildAsync(pfsc,outer,ekpfs,seed,new FileInfo(inner).Length,CancellationToken.None);

            const string contentId="UP9000-SLUS00000_00-0123456789ABCDEF";
            const string passcode="00000000000000000000000000000000";
            var crypto=new PkgPreparedCrypto(
                new byte[0x800],new byte[0x100],
                DebugRifSigner.Sign(PkgLicense.BuildUnsignedDebugRif(contentId)),
                digest=>{var w=new byte[0x100];digest.CopyTo(w,0);return w;});
            var entries=PkgEntryBuilder.Build(contentId,param,new byte[]{1,2,3,4},(ulong)new FileInfo(inner).Length,crypto, Enumerable.Range(0,0x214).Select(i=>(byte)(i*17+3)).ToArray());
            var pkg=Path.Combine(root,"storage-pipeline.pkg");
            var validation=await PkgAssembler.AssembleAsync(
                pkg,outer,contentId,passcode,entries,param,
                _=>Ps4Metadata.BuildParamSfo("SLUS-00000","SLUS-00000"),
                crypto.HeaderWrapper);

            Assert.True(validation.Valid,string.Join(Environment.NewLine,validation.Errors));
            Assert.True((await PkgValidator.ValidateFileAsync(pkg)).Valid);
        }
        finally
        {
            if(Directory.Exists(root)) Directory.Delete(root,true);
        }
    }
}

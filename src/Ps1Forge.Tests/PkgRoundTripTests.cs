using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

public sealed class PkgRoundTripTests
{
    [Fact]
    public async Task Assemble_ThenValidate_RoundTrips()
    {
        var root=Path.Combine(Path.GetTempPath(),"Ps1ForgeTests",Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var pfs=Path.Combine(root,"outer.pfs");
            var pfsBytes=new byte[0x20000];
            for(var i=0;i<pfsBytes.Length;i++) pfsBytes[i]=(byte)(i*31+7);
            await File.WriteAllBytesAsync(pfs,pfsBytes);

            const string contentId="UP9000-SLUS00000_00-0123456789ABCDEF";
            const string passcode="00000000000000000000000000000000";
            var param=Ps4Metadata.BuildParamSfo("SLUS-00000","SLUS-00000");

            var crypto=new PkgPreparedCrypto(
                new byte[0x800],
                new byte[0x100],
                DebugRifSigner.Sign(PkgLicense.BuildUnsignedDebugRif(contentId)),
                digest =>
                {
                    Assert.Equal(32,digest.Length);
                    var wrapped=new byte[0x100];
                    digest.CopyTo(wrapped,0);
                    return wrapped;
                });

            var entries=PkgEntryBuilder.Build(contentId,param,new byte[]{1,2,3,4},0x10000,crypto);
            var planned=PkgBodyBuilder.Plan(entries,(ulong)new FileInfo(pfs).Length);
            var namesEntry=planned.Entries.Single(e=>e.Id==PkgBodyBuilder.EntryNames);
            Assert.NotEmpty(namesEntry.Data);
            Assert.Equal(planned.EntryNames,namesEntry.Data);
            Assert.Contains("param.sfo",System.Text.Encoding.UTF8.GetString(namesEntry.Data));
            Assert.Contains("playgo-chunk.dat",System.Text.Encoding.UTF8.GetString(namesEntry.Data));

            var output=Path.Combine(root,"fixture.pkg");
            var validation=await PkgAssembler.AssembleAsync(
                output,pfs,contentId,passcode,entries,param,
                _=>Ps4Metadata.BuildParamSfo("SLUS-00000","SLUS-00000"),
                crypto.HeaderWrapper);

            Assert.True(validation.Valid,string.Join(Environment.NewLine,validation.Errors));
            Assert.True(new FileInfo(output).Length>pfsBytes.Length);

            var reopened=await PkgValidator.ValidateFileAsync(output);
            Assert.True(reopened.Valid,string.Join(Environment.NewLine,reopened.Errors));
        }
        finally
        {
            if(Directory.Exists(root)) Directory.Delete(root,true);
        }
    }
}

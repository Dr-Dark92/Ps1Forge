using Ps1Forge.Core;
using Xunit;

namespace Ps1Forge.Tests;

public sealed class CryptoTests
{
    [Fact]
    public void DebugRif_IsSignedAndStableSize()
    {
        const string contentId="UP9000-SLUS00000_00-0123456789ABCDEF";
        var rif=DebugRifSigner.Sign(PkgLicense.BuildUnsignedDebugRif(contentId));
        Assert.Equal(0x400,rif.Length);
        Assert.Contains(rif.AsSpan(0x300,0x100).ToArray(),b=>b!=0);
    }

    [Fact]
    public void PkgRsa_IsDeterministic()
    {
        var modulus=Convert.FromHexString("C2D244BCDD843FD9C522AFF7FC888A3380ED8EE2CC81F7ECF81C79BF02BB128E6168291B15B65EC6F8BF5AE03B6A6CD9D6F575ABA06F3481349A5BADED31E3C6EA1AD11322BBB3DAB3B253BD457987AD0A017218102949F4417FD6470C72929EE9BB95A95D79EBE4307690454B9D9CCF9203608C4B6CB37A3A0539A066A935CFB9FAAD9CABEBE46A8CE93BCC72126263BD80C4EE372B3203A309F7A06157AD0DCF15989E4E49F8B5A35C27EE4504EAE44BBC8F87ED191E467563C45BD5BC092F0273193C585549664C11EC0F09FAA5560A5A6356ADA00D8608C1E6B61322492F7CDB4C56970EC2D92E87BC0E67C01B58BC642BC26EE2932EB56B70A4429F64C1");
        var key=new byte[32];
        for(var i=0;i<key.Length;i++) key[i]=(byte)i;
        var a=PkgRsa.EncryptKey(modulus,key);
        var b=PkgRsa.EncryptKey(modulus,key);
        Assert.Equal(256,a.Length);
        Assert.Equal(a,b);
    }
}

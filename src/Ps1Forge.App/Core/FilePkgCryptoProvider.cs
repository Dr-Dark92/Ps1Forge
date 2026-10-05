namespace Ps1Forge.Core;

public sealed class FilePkgCryptoProvider : IPkgCryptoProvider
{
    private readonly string _keyRoot;

    public FilePkgCryptoProvider(string keyRoot) => _keyRoot = keyRoot;

    public PkgPreparedCrypto Prepare(string contentId,string passcode,byte[] ekpfs)
    {
        if(ekpfs.Length!=32) throw new ArgumentException("EKPFS must be exactly 32 bytes.",nameof(ekpfs));

        // Reference fPKG ENTRY_KEYS uses public key slots 0, 1 and 3.
        // Slots 2, 4, 5 and 6 are intentionally zero-filled; silently
        // zeroing a required slot would produce a structurally valid but
        // unusable package.
        var publicKeys=new byte[7][];
        for(var i=0;i<publicKeys.Length;i++) publicKeys[i]=new byte[256];
        publicKeys[0]=ReadRequiredModulus("pkg_public_0.bin");
        publicKeys[1]=ReadRequiredModulus("pkg_public_1.bin");
        publicKeys[3]=ReadRequiredModulus("pkg_public_3.bin");

        var fakeModulus=ReadRequiredModulus("fake_keyset_modulus.bin");
        var headerModulus=publicKeys[3];

        var entryKeys=BuildEntryKeys(contentId,passcode,publicKeys);
        var imageKey=PkgRsa.EncryptKey(fakeModulus,ekpfs);
        var license=DebugRifSigner.Sign(PkgLicense.BuildUnsignedDebugRif(contentId));
        return new(entryKeys,imageKey,license,digest=>PkgRsa.EncryptKey(headerModulus,digest));
    }

    private byte[] ReadRequiredModulus(string name)
    {
        var path=Path.Combine(_keyRoot,name);
        if(!File.Exists(path)) throw new InvalidDataException($"Missing keys/{name}.");
        return ReadModulus(path);
    }

    private static byte[] ReadModulus(string path)
    {
        var data=File.ReadAllBytes(path);
        if(data.Length!=256) throw new InvalidDataException($"{Path.GetFileName(path)} must be exactly 256 bytes.");
        return data;
    }

    private static byte[] BuildEntryKeys(string contentId,string passcode,IReadOnlyList<byte[]> moduli)
    {
        using var ms=new MemoryStream(0x800);
        var paddedId=new byte[48];
        System.Text.Encoding.ASCII.GetBytes(contentId).CopyTo(paddedId,0);
        ms.Write(System.Security.Cryptography.SHA256.HashData(paddedId));

        var digests=new byte[7][];
        var keys=new byte[7][];
        for(uint i=0;i<7;i++)
        {
            var passcodeKey=PackageCrypto.ComputeKey(contentId,passcode,i);
            var digest=System.Security.Cryptography.SHA256.HashData(passcodeKey);
            for(var j=0;j<32;j++) digest[j]^=passcodeKey[j];
            digests[i]=digest;
            keys[i]=IsZero(moduli[(int)i]) ? new byte[256] : PkgRsa.EncryptKey(moduli[(int)i],passcodeKey);
        }
        keys[0]=PkgRsa.EncryptKey(moduli[0],System.Text.Encoding.ASCII.GetBytes(passcode));

        foreach(var d in digests) ms.Write(d);
        foreach(var k in keys) ms.Write(k);
        var result=ms.ToArray();
        if(result.Length!=0x800) throw new InvalidDataException("ENTRY_KEYS generation produced an unexpected size.");
        return result;
    }

    private static bool IsZero(byte[] data) => data.All(static b=>b==0);
}

namespace Ps1Forge.Core;

public interface IPkgCryptoProvider
{
    PkgPreparedCrypto Prepare(string contentId,string passcode,byte[] ekpfs);
}

public sealed class MissingPkgCryptoProvider : IPkgCryptoProvider
{
    public PkgPreparedCrypto Prepare(string contentId,string passcode,byte[] ekpfs) =>
        throw new InvalidOperationException(
            "Final fPKG crypto material is not configured. Provide an IPkgCryptoProvider before building a PS4 package.");
}

using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public static class Keystone
{
    // Fake-package keystone payload generated deterministically from the package
    // passcode. This stays isolated so it can be validated against the package
    // writer before hardware testing.
    public static byte[] Build(string passcode)
    {
        if (passcode.Length != 32)
            throw new ArgumentException("Package passcode must be exactly 32 characters.", nameof(passcode));

        var seed = SHA256.HashData(Encoding.ASCII.GetBytes(passcode));
        var output = new byte[96];

        // Header/magic used by the staging layer; package serializer owns final
        // cryptographic validation and may replace this payload if required.
        Encoding.ASCII.GetBytes("keystone").CopyTo(output, 0);
        seed.CopyTo(output, 16);
        SHA256.HashData(seed).CopyTo(output, 48);
        SHA256.HashData(output.AsSpan(0, 80)).AsSpan(0, 16).CopyTo(output.AsSpan(80));
        return output;
    }
}

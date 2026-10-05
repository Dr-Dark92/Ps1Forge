using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public static class Keystone
{
    // Constants are intentionally supplied by the package layer rather than
    // hiding them in the UI/runtime staging code.
    public static byte[] Build(string passcode, byte[] hmacKey, byte[] macData)
    {
        if (passcode.Length != 32)
            throw new ArgumentException("Package passcode must be exactly 32 characters.", nameof(passcode));

        var header = Convert.FromHexString(
            "6B657973746F6E65020001000000000000000000000000000000000000000000");

        using var fpHmac = new HMACSHA256(hmacKey);
        var fingerprint = fpHmac.ComputeHash(Encoding.ASCII.GetBytes(passcode));

        var first = new byte[header.Length + fingerprint.Length];
        header.CopyTo(first, 0);
        fingerprint.CopyTo(first, header.Length);

        using var finalHmac = new HMACSHA256(macData);
        var final = finalHmac.ComputeHash(first);

        return [.. first, .. final];
    }
}

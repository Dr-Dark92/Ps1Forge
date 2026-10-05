using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public static class Keystone
{
    private static readonly byte[] HmacKey = Convert.FromHexString(
        "C74405F67424BA342BC1276251BBC2F555F16025B6A1B6714780DBAEC852FA2F");
    private static readonly byte[] MacData = Convert.FromHexString(
        "783D6F3AE91C0E0712FCAAB7950BDE06855CF7A22DCDBDE127E9BFCBAD0FF0FE");

    public static byte[] Build(string passcode)
    {
        if (passcode.Length != 32)
            throw new ArgumentException("Package passcode must be exactly 32 characters.", nameof(passcode));

        var header = Convert.FromHexString(
            "6B657973746F6E65020001000000000000000000000000000000000000000000");

        using var fpHmac = new HMACSHA256(HmacKey);
        var fingerprint = fpHmac.ComputeHash(Encoding.ASCII.GetBytes(passcode));

        var first = new byte[header.Length + fingerprint.Length];
        header.CopyTo(first, 0);
        fingerprint.CopyTo(first, header.Length);

        using var finalHmac = new HMACSHA256(MacData);
        var final = finalHmac.ComputeHash(first);

        return [.. first, .. final];
    }
}

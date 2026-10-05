using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public static class PackageCrypto
{
    public static byte[] ComputeKey(string contentId, string passcode, uint index)
    {
        if (contentId.Length != 36) throw new ArgumentException("Content ID must be 36 characters.");
        if (passcode.Length != 32) throw new ArgumentException("Passcode must be 32 characters.");

        Span<byte> idx = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(idx, index);
        var data = new byte[96];
        SHA256.HashData(idx).CopyTo(data, 0);

        var paddedId = new byte[48];
        Encoding.ASCII.GetBytes(contentId).CopyTo(paddedId, 0);
        SHA256.HashData(paddedId).CopyTo(data, 32);
        Encoding.ASCII.GetBytes(passcode).CopyTo(data, 64);
        return SHA256.HashData(data);
    }

    public static byte[] PfsCryptoKey(byte[] ekpfs, byte[] seed, uint index)
    {
        var d = new byte[4 + seed.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(d, index);
        seed.CopyTo(d, 4);
        using var h = new HMACSHA256(ekpfs);
        return h.ComputeHash(d);
    }

    public static (byte[] TweakKey, byte[] DataKey) PfsEncryptionKeys(byte[] ekpfs, byte[] seed)
    {
        var k = PfsCryptoKey(ekpfs, seed, 1);
        return (k[..16], k[16..32]);
    }

    public static byte[] PfsSigningKey(byte[] ekpfs, byte[] seed) =>
        PfsCryptoKey(ekpfs, seed, 2);

    public static byte[] HmacSha256(byte[] key, ReadOnlySpan<byte> data)
    {
        using var h = new HMACSHA256(key);
        return h.ComputeHash(data.ToArray());
    }
}

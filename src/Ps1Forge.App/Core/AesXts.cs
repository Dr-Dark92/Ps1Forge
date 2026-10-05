using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ps1Forge.Core;

public static class AesXts
{
    public static void EncryptInPlace(
        byte[] data,
        byte[] dataKey,
        byte[] tweakKey,
        int sectorSize,
        int startSector)
    {
        using var dataAes = CreateEcb(dataKey);
        using var tweakAes = CreateEcb(tweakKey);
        using var dataEnc = dataAes.CreateEncryptor();
        using var tweakEnc = tweakAes.CreateEncryptor();

        for (var sector = startSector; sector * sectorSize < data.Length; sector++)
        {
            var start = sector * sectorSize;
            var len = Math.Min(sectorSize, data.Length - start);
            if ((len & 15) != 0)
                throw new InvalidDataException("XTS sector data must be AES-block aligned.");

            Span<byte> tweakInput = stackalloc byte[16];
            BinaryPrimitives.WriteUInt64LittleEndian(tweakInput, (ulong)sector);
            var tweak = new byte[16];
            tweakEnc.TransformBlock(tweakInput.ToArray(), 0, 16, tweak, 0);

            var block = new byte[16];
            var encrypted = new byte[16];
            for (var off = 0; off < len; off += 16)
            {
                for (var i = 0; i < 16; i++) block[i] = (byte)(data[start + off + i] ^ tweak[i]);
                dataEnc.TransformBlock(block, 0, 16, encrypted, 0);
                for (var i = 0; i < 16; i++) data[start + off + i] = (byte)(encrypted[i] ^ tweak[i]);
                MultiplyAlpha(tweak);
            }
        }
    }

    private static Aes CreateEcb(byte[] key)
    {
        var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        return aes;
    }

    private static void MultiplyAlpha(byte[] tweak)
    {
        byte feedback = 0;
        for (var i = 0; i < 16; i++)
        {
            var value = tweak[i];
            tweak[i] = (byte)((value << 1) | feedback);
            feedback = (byte)(value >> 7);
        }
        if (feedback != 0) tweak[0] ^= 0x87;
    }
}

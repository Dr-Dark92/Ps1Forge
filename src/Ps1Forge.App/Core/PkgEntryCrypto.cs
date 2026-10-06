using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Ps1Forge.Core;

public static class PkgEntryCrypto
{
    public static byte[] Encrypt(
        uint id,
        uint nameTableOffset,
        uint flags1,
        uint flags2,
        uint dataOffset,
        uint logicalSize,
        byte[] data,
        string contentId,
        string passcode)
    {
        if(data.Length%16!=0) throw new ArgumentException("Encrypted PKG stored size must be 16-byte aligned.",nameof(data));
        if(logicalSize>data.Length) throw new ArgumentOutOfRangeException(nameof(logicalSize));
        var meta=new byte[32];
        BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(0x00,4),id);
        BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(0x04,4),nameTableOffset);
        BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(0x08,4),flags1);
        BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(0x0C,4),flags2);
        BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(0x10,4),dataOffset);
        BinaryPrimitives.WriteUInt32BigEndian(meta.AsSpan(0x14,4),logicalSize);

        var keyIndex=(flags2&0xF000u)>>12;
        var keySeed=PackageCrypto.ComputeKey(contentId,passcode,keyIndex);
        var ivSource=new byte[64];
        meta.CopyTo(ivSource,0);
        keySeed.CopyTo(ivSource,32);
        var ivKey=SHA256.HashData(ivSource);

        using var aes=Aes.Create();
        aes.Key=ivKey[16..32];
        aes.IV=ivKey[..16];
        aes.Mode=CipherMode.CBC;
        aes.Padding=PaddingMode.None;
        using var enc=aes.CreateEncryptor();
        return enc.TransformFinalBlock(data,0,data.Length);
    }
}

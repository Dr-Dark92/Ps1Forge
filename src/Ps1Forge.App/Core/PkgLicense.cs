using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public static class PkgLicense
{
    private static readonly byte[] RifDebugKey=Convert.FromHexString("96C2268D69261C8B1E3B6BFF2FE04E12");

    public static byte[] BuildLicenseInfo(string contentId)
    {
        RequireContentId(contentId);
        var b=new byte[0x200];
        Encoding.ASCII.GetBytes(contentId).CopyTo(b,0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0x40,4),0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0x44,4),0x1A);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0x48,4),0);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0x4C,4),1);
        return b;
    }

    public static byte[] BuildUnsignedDebugRif(string contentId)
    {
        RequireContentId(contentId);
        var b=new byte[0x400];
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0x00,4),0x52494600);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0x04,2),1);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0x06,2),0xFFFF);
        BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(0x10,8),1364222275);
        BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(0x18,8),0x7FFFFFFFFFFFFFFF);
        Encoding.ASCII.GetBytes(contentId).CopyTo(b,0x20);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0x50,2),0x0200);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0x52,2),0x000F);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0x54,2),0x001A);
        BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(0x56,2),3);
        BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(0x64,4),1);

        var padded=new byte[48];
        Encoding.ASCII.GetBytes(contentId).CopyTo(padded,0);
        var seed=SHA256.HashData(padded);
        seed.AsSpan(0,16).CopyTo(b.AsSpan(0x260,16));

        var secret=new byte[144];
        seed.AsSpan(16,16).CopyTo(secret);
        using var aes=Aes.Create();
        aes.Key=RifDebugKey;
        aes.IV=seed[..16];
        aes.Mode=CipherMode.CBC;
        aes.Padding=PaddingMode.None;
        using var enc=aes.CreateEncryptor();
        var encrypted=enc.TransformFinalBlock(secret,0,secret.Length);
        encrypted.CopyTo(b,0x270);
        return b;
    }

    public static byte[] RifSigningDigest(byte[] rif)
    {
        if(rif.Length!=0x400) throw new ArgumentException("Debug RIF must be 0x400 bytes.",nameof(rif));
        return SHA256.HashData(rif.AsSpan(0,0x300));
    }

    private static void RequireContentId(string contentId)
    {
        if(contentId.Length!=36) throw new ArgumentException("Content ID must be 36 ASCII characters.",nameof(contentId));
    }
}

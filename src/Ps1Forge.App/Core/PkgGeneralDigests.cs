using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Ps1Forge.Core;

public static class PkgGeneralDigests
{
    public static byte[] Build(
        byte[] header,
        string contentId,
        byte[] paramSfo,
        byte[] pfsImageDigest,
        uint attribute=0,
        uint? attribute2=0x400,
        string category="gd",
        string format="obs",
        uint pubToolVer=0x03380000)
    {
        if(header.Length<0x480) throw new ArgumentException("PKG header is incomplete.",nameof(header));
        if(pfsImageDigest.Length!=32) throw new ArgumentException("PFS digest must be SHA-256.",nameof(pfsImageDigest));

        var major=ComputeMajorParamDigest(attribute,attribute2,category,format,pubToolVer);
        var data=new byte[0x180];
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0x00,2),0xD256);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(0x02,2),0x0100);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(0x1C,4),(1u<<1)|(1u<<2)|(1u<<3)|(1u<<5)|(1u<<6));

        ComputeContentDigest(contentId,pfsImageDigest,major).CopyTo(data,0x20);
        pfsImageDigest.CopyTo(data,0x40);
        ComputeHeaderDigest(header).CopyTo(data,0x60);
        major.CopyTo(data,0xA0);
        SHA256.HashData(paramSfo).CopyTo(data,0xC0);
        return data;
    }

    public static byte[] ComputeMajorParamDigest(uint attribute,uint? attribute2,string category,string format,uint pubToolVer)
    {
        var s="ATTRIBUTE"+Hex(attribute);
        if(attribute2.HasValue) s+="ATTRIBUTE2"+Hex(attribute2.Value);
        s+="CATEGORY"+category;
        s+="FORMAT"+format;
        s+="PUBTOOLVER"+Hex(pubToolVer);
        return SHA256.HashData(Encoding.UTF8.GetBytes(s));
    }

    private static byte[] ComputeContentDigest(string contentId,byte[] pfsDigest,byte[] major)
    {
        var id=Encoding.ASCII.GetBytes(contentId);
        if(id.Length!=36) throw new ArgumentException("Content ID must be 36 ASCII bytes.",nameof(contentId));
        var data=new byte[36+12+4+4+32+32];
        id.CopyTo(data,0);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(48,4),0xF);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(52,4),0x1A);
        pfsDigest.CopyTo(data,56);
        major.CopyTo(data,88);
        return SHA256.HashData(data);
    }

    private static byte[] ComputeHeaderDigest(byte[] header)
    {
        var data=new byte[64+128];
        header.AsSpan(0,64).CopyTo(data);
        header.AsSpan(0x400,128).CopyTo(data.AsSpan(64));
        return SHA256.HashData(data);
    }

    private static string Hex(uint v)=>$"0x{v:x8}";
}

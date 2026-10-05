using System.Buffers.Binary;
using System.Text;

namespace Ps1Forge.Core;

public sealed record PkgValidation(bool Valid, IReadOnlyList<string> Errors);

public static class PkgValidator
{
    public static PkgValidation ValidateHeader(ReadOnlySpan<byte> pkg)
    {
        var errors = new List<string>();
        if (pkg.Length < PkgHeader.HeaderSize) errors.Add("PKG header is truncated.");
        else
        {
            if (!(pkg[0]==0x7F && pkg[1]=='C' && pkg[2]=='N' && pkg[3]=='T')) errors.Add("Invalid PKG magic.");
            var body=BinaryPrimitives.ReadUInt64BigEndian(pkg.Slice(0x20,8));
            var pfs=BinaryPrimitives.ReadUInt64BigEndian(pkg.Slice(0x410,8));
            var size=BinaryPrimitives.ReadUInt64BigEndian(pkg.Slice(0x430,8));
            if(body!=PkgHeader.BodyOffset) errors.Add("Unexpected body offset.");
            if(pfs<body) errors.Add("PFS overlaps package body.");
            if(size<pfs) errors.Add("Invalid package size.");
        }
        return new(errors.Count==0,errors);
    }
}

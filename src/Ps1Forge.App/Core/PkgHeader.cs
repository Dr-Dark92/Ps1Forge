using System.Buffers.Binary;
using System.Text;

namespace Ps1Forge.Core;

public static class PkgHeader
{
    public const int HeaderSize = 0x1100;
    public const ulong BodyOffset = 0x2000;
    public const uint EntryTableOffset = 0x2A80;

    public static byte[] Build(
        string contentId,
        int entryCount,
        uint mainEntryDataSize,
        ulong bodySize,
        ulong pfsOffset,
        ulong pfsSize,
        ulong packageSize)
    {
        if (contentId.Length != 36) throw new ArgumentException("Content ID must be 36 characters.");

        var h = new byte[HeaderSize];
        h[0] = 0x7F; h[1] = (byte)'C'; h[2] = (byte)'N'; h[3] = (byte)'T';
        BE32(h, 0x04, 0x40000001);
        BE32(h, 0x0C, 0xF);
        BE32(h, 0x10, (uint)entryCount);
        BE16(h, 0x14, 6);
        BE16(h, 0x16, (ushort)entryCount);
        BE32(h, 0x18, EntryTableOffset);
        BE32(h, 0x1C, mainEntryDataSize);
        BE64(h, 0x20, BodyOffset);
        BE64(h, 0x28, bodySize);
        Encoding.ASCII.GetBytes(contentId).CopyTo(h, 0x40);
        BE32(h, 0x70, 0xF);
        BE32(h, 0x74, 0x1A);
        BE32(h, 0x78, 0x0A000000);
        BE32(h, 0x7C, checked((uint)(BodyOffset + bodySize)));
        BE32(h, 0x80, 0x20171106);
        BE32(h, 0x84, 0x01889410);
        BE32(h, 0x9C, 1);
        BE32(h, 0x400, 1);
        BE32(h, 0x404, 1);
        BE64(h, 0x408, 0x80000000000003CC);
        BE64(h, 0x410, pfsOffset);
        BE64(h, 0x418, pfsSize);
        BE64(h, 0x420, 0);
        BE64(h, 0x428, packageSize);
        BE64(h, 0x430, packageSize);
        BE32(h, 0x438, 0x10000);
        BE32(h, 0x43C, 0xE0000);
        return h;
    }

    private static void BE16(byte[] b, int o, ushort v) => BinaryPrimitives.WriteUInt16BigEndian(b.AsSpan(o, 2), v);
    private static void BE32(byte[] b, int o, uint v) => BinaryPrimitives.WriteUInt32BigEndian(b.AsSpan(o, 4), v);
    private static void BE64(byte[] b, int o, ulong v) => BinaryPrimitives.WriteUInt64BigEndian(b.AsSpan(o, 8), v);
}

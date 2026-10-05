using System.Buffers.Binary;
using System.Text;

namespace Ps1Forge.Core;

public static class PlayGo
{
    public static byte[] BuildChunkDat(string contentId, ulong packageSize, ulong innerPfsSize)
    {
        var b = new byte[416];
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x00,4), 0x6F676C70);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x08,2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0A,2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0C,2), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x0E,2), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0x10,4), (uint)b.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x16,2), 1);
        Array.Fill(b, (byte)0xFF, 0x20, 0x20);
        Encoding.ASCII.GetBytes(contentId).CopyTo(b, 0x40);

        (uint Off,uint Size)[] table=[(0x100,0x20),(0x120,2),(0x130,9),(0x140,0x10),(0x160,0x20),(0x180,2),(0x190,0x0C),(0x150,0x10)];
        for(var i=0;i<table.Length;i++){ BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0xC0+i*8,4),table[i].Off); BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(0xC4+i*8,4),table[i].Size); }
        b[0x100]=0x80; b[0x102]=0x03;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x10E,2),1);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x110,8),ulong.MaxValue);
        Encoding.ASCII.GetBytes("Chunk #0").CopyTo(b,0x130);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x148,8),packageSize);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(0x158,8),innerPfsSize);
        b[0x160]=1;
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x174,2),1);
        BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(0x176,2),1);
        Encoding.ASCII.GetBytes("Scenario #0").CopyTo(b,0x190);
        return b;
    }

    public static byte[] Manifest()
    {
        const string xml="<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\r\n<psproject fmt=\"playgo-manifest\" version=\"0990\">\r\n  <volume>\r\n    <chunk_info chunk_count=\"1\" scenario_count=\"1\">\r\n      <scenarios default_id=\"0\">\r\n        <scenario id=\"0\" type=\"sp\" initial_chunk_count=\"1\" label=\"Scenario #0\">0</scenario>\r\n      </scenarios>\r\n    </chunk_info>\r\n  </volume>\r\n</psproject>\r\n";
        return [0xEF,0xBB,0xBF,..Encoding.UTF8.GetBytes(xml)];
    }
}

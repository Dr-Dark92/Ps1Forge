using System.Buffers.Binary;
using System.Text;

namespace Ps1Forge.Core;

public sealed class SfoWriter
{
    private const ushort Utf8 = 0x0204;
    private const ushort Int32 = 0x0404;
    private readonly SortedDictionary<string, SfoValue> _values = new(StringComparer.Ordinal);

    public void AddString(string key, string value, int maxBytes = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(value + "\0");
        var max = maxBytes > 0 ? maxBytes : Align4(bytes.Length);
        _values[key] = new SfoValue(Utf8, bytes, max);
    }

    public void AddInt32(string key, int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        _values[key] = new SfoValue(Int32, bytes, 4);
    }

    public byte[] Build()
    {
        var keys = new MemoryStream();
        var data = new MemoryStream();
        var entries = new List<Entry>();

        foreach (var pair in _values)
        {
            var keyOffset = checked((ushort)keys.Position);
            var keyBytes = Encoding.UTF8.GetBytes(pair.Key + "\0");
            keys.Write(keyBytes);

            AlignStream(data, 4);
            var dataOffset = checked((uint)data.Position);
            data.Write(pair.Value.Data);
            if (pair.Value.MaxLength > pair.Value.Data.Length)
                data.Write(new byte[pair.Value.MaxLength - pair.Value.Data.Length]);

            entries.Add(new Entry(
                keyOffset,
                pair.Value.Format,
                (uint)pair.Value.Data.Length,
                (uint)pair.Value.MaxLength,
                dataOffset));
        }

        var keyTableOffset = 20 + entries.Count * 16;
        var dataTableOffset = Align4(keyTableOffset + checked((int)keys.Length));

        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output, Encoding.UTF8, true);

        writer.Write(0x46535000u); // \0PSF
        writer.Write(0x00000101u);
        writer.Write((uint)keyTableOffset);
        writer.Write((uint)dataTableOffset);
        writer.Write((uint)entries.Count);

        foreach (var entry in entries)
        {
            writer.Write(entry.KeyOffset);
            writer.Write(entry.Format);
            writer.Write(entry.Length);
            writer.Write(entry.MaxLength);
            writer.Write(entry.DataOffset);
        }

        keys.Position = 0;
        keys.CopyTo(output);
        while (output.Position < dataTableOffset)
            output.WriteByte(0);

        data.Position = 0;
        data.CopyTo(output);
        return output.ToArray();
    }

    private static int Align4(int value) => (value + 3) & ~3;

    private static void AlignStream(Stream stream, int alignment)
    {
        while ((stream.Position % alignment) != 0)
            stream.WriteByte(0);
    }

    private sealed record SfoValue(ushort Format, byte[] Data, int MaxLength);
    private sealed record Entry(ushort KeyOffset, ushort Format, uint Length, uint MaxLength, uint DataOffset);
}

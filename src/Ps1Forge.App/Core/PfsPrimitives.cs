using System.Buffers.Binary;
using System.Text;

namespace Ps1Forge.Core;

/// <summary>
/// Foundation for the unsigned inner PFS image. The complete writer is kept
/// separate from PFSC and outer-PFS crypto so each binary layer can be tested.
/// </summary>
public static class PfsPrimitives
{
    public const int BlockSize = 0x10000;
    public const long HeaderMagic = 20130315;
    public const int UnsignedInodeSize = 0xA8;

    public static uint PathHash(string path)
    {
        uint hash = 0;
        foreach (var c in path)
            hash = char.ToUpperInvariant(c) + 31 * hash;
        return hash;
    }

    public static int DirentSize(string name)
    {
        var size = Encoding.UTF8.GetByteCount(name) + 17;
        return (size + 7) & ~7;
    }

    public static long CeilDiv(long a, long b) => (a + b - 1) / b;

    public static void WriteDirent(Stream stream, uint inode, int type, string name)
    {
        var nameBytes = Encoding.UTF8.GetBytes(name);
        var entrySize = DirentSize(name);
        Span<byte> header = stackalloc byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(header[0..4], inode);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..8], type);
        BinaryPrimitives.WriteInt32LittleEndian(header[8..12], nameBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header[12..16], entrySize);
        stream.Write(header);
        stream.Write(nameBytes);
        stream.Write(new byte[entrySize - 16 - nameBytes.Length]);
    }
}

public sealed record PfsInputFile(string VirtualPath, string HostPath);

public static class InnerPfsPlanner
{
    public static IReadOnlyList<PfsInputFile> EnumerateApp0(string app0)
    {
        return Directory.EnumerateFiles(app0, "*", SearchOption.AllDirectories)
            .Select(path => new PfsInputFile(
                Path.GetRelativePath(app0, path).Replace('\\', '/'),
                path))
            .OrderBy(x => x.VirtualPath, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyDictionary<uint, string> BuildFlatPathIndex(IEnumerable<PfsInputFile> files)
    {
        var result = new SortedDictionary<uint, string>();
        foreach (var file in files)
        {
            var absolute = "/" + file.VirtualPath;
            var hash = PfsPrimitives.PathHash(absolute);
            if (!result.TryAdd(hash, absolute))
                throw new InvalidDataException($"PFS path-hash collision: {absolute} and {result[hash]}");
        }
        return result;
    }
}

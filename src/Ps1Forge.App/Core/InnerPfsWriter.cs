using System.Buffers.Binary;
using System.Text;

namespace Ps1Forge.Core;

public static class InnerPfsWriter
{
    private const ushort ReadExec = 1 | 4 | 8 | 32 | 64 | 256;
    private const ushort ModeDir = 16384;
    private const ushort ModeFile = 32768;
    private const uint FlagReadonly = 0x10;
    private const uint FlagInternal = 0x20000;

    private sealed class Node
    {
        public required string Name;
        public required bool IsDir;
        public Node? Parent;
        public List<Node> Children = [];
        public string? HostPath;
        public uint Inode;
        public long Size;
        public uint Blocks;
        public int StartBlock;
        public string FullPath => Parent is null ? "" : Parent.FullPath + "/" + Name;
    }

    public static async Task<string> BuildAsync(
        string app0,
        string outputPath,
        IProgress<string>? progress,
        CancellationToken ct)
    {
        var root = BuildTree(app0);
        var dirs = Walk(root).Where(n => n.IsDir && n != root).OrderBy(n => n.FullPath, StringComparer.Ordinal).ToList();
        var files = Walk(root).Where(n => !n.IsDir).OrderBy(n => n.FullPath, StringComparer.Ordinal).ToList();
        var nodes = dirs.Concat(files).OrderBy(n => n.FullPath, StringComparer.Ordinal).ToList();

        uint nextInode = 0;
        var superRoot = new Node { Name = "super", IsDir = true, Inode = nextInode++ };
        var flat = new Node { Name = "flat_path_table", IsDir = false, Inode = nextInode++ };
        root.Inode = nextInode++;
        foreach (var n in nodes) n.Inode = nextInode++;

        var flatData = BuildFlatTable(nodes);
        flat.Size = flatData.Length;
        flat.Blocks = (uint)Math.Max(1, PfsPrimitives.CeilDiv(flat.Size, PfsPrimitives.BlockSize));

        foreach (var d in dirs.Prepend(root))
        {
            d.Size = Math.Max(PfsPrimitives.BlockSize, DirectoryPayloadSize(d));
            d.Blocks = 1;
        }
        foreach (var f in files)
        {
            f.Size = new FileInfo(f.HostPath!).Length;
            f.Blocks = (uint)Math.Max(1, PfsPrimitives.CeilDiv(f.Size, PfsPrimitives.BlockSize));
        }

        var inodeCount = (int)nextInode;
        var inodesPerBlock = PfsPrimitives.BlockSize / PfsPrimitives.UnsignedInodeSize;
        var inodeBlocks = (int)PfsPrimitives.CeilDiv(inodeCount, inodesPerBlock);

        var block = 1 + inodeBlocks;
        superRoot.StartBlock = block++;
        flat.StartBlock = block;
        block += (int)flat.Blocks;
        block++; // collision-resolver/empty compatibility block

        root.StartBlock = block++;
        foreach (var d in dirs) d.StartBlock = block++;
        foreach (var f in files)
        {
            f.StartBlock = block;
            block += checked((int)f.Blocks);
        }

        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 1024, true);
        output.SetLength((long)block * PfsPrimitives.BlockSize);

        WriteHeader(output, inodeCount, inodeBlocks, block);
        WriteInodeAt(output, superRoot.Inode, inodesPerBlock, (ushort)(ModeDir | ReadExec), 1, FlagInternal | FlagReadonly, PfsPrimitives.BlockSize, 1, superRoot.StartBlock, 1);
        WriteInodeAt(output, flat.Inode, inodesPerBlock, (ushort)(ModeFile | ReadExec), 1, FlagInternal | FlagReadonly, flat.Size, flat.Blocks, flat.StartBlock, 1);
        WriteInodeAt(output, root.Inode, inodesPerBlock, (ushort)(ModeDir | ReadExec), DirLinks(root), FlagReadonly, PfsPrimitives.BlockSize, 1, root.StartBlock, 1);
        foreach (var n in nodes)
            WriteInodeAt(output, n.Inode, inodesPerBlock,
                (ushort)((n.IsDir ? ModeDir : ModeFile) | ReadExec),
                n.IsDir ? DirLinks(n) : (ushort)1,
                FlagReadonly,
                n.IsDir ? PfsPrimitives.BlockSize : n.Size,
                n.Blocks,
                n.StartBlock,
                1);

        output.Position = (long)superRoot.StartBlock * PfsPrimitives.BlockSize;
        PfsPrimitives.WriteDirent(output, flat.Inode, 2, "flat_path_table");
        PfsPrimitives.WriteDirent(output, root.Inode, 3, "uroot");

        output.Position = (long)flat.StartBlock * PfsPrimitives.BlockSize;
        await output.WriteAsync(flatData, ct);

        await WriteDirectory(output, root, root, ct);
        foreach (var d in dirs)
            await WriteDirectory(output, d, d.Parent ?? root, ct);

        var index = 0;
        foreach (var f in files)
        {
            ct.ThrowIfCancellationRequested();
            output.Position = (long)f.StartBlock * PfsPrimitives.BlockSize;
            await using var input = new FileStream(f.HostPath!, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, true);
            await input.CopyToAsync(output, 1024 * 1024, ct);
            progress?.Report($"Inner PFS: {++index}/{files.Count} files");
        }

        await output.FlushAsync(ct);
        return outputPath;
    }

    private static Node BuildTree(string app0)
    {
        var root = new Node { Name = "uroot", IsDir = true };
        foreach (var path in Directory.EnumerateFiles(app0, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal))
        {
            var parts = Path.GetRelativePath(app0, path).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var current = root;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                var child = current.Children.FirstOrDefault(x => x.IsDir && x.Name == parts[i]);
                if (child is null)
                {
                    child = new Node { Name = parts[i], IsDir = true, Parent = current };
                    current.Children.Add(child);
                }
                current = child;
            }
            current.Children.Add(new Node { Name = parts[^1], IsDir = false, Parent = current, HostPath = path });
        }
        return root;
    }

    private static IEnumerable<Node> Walk(Node root)
    {
        foreach (var c in root.Children)
        {
            yield return c;
            if (c.IsDir)
                foreach (var n in Walk(c)) yield return n;
        }
    }

    private static byte[] BuildFlatTable(IEnumerable<Node> nodes)
    {
        var byHash=new Dictionary<uint,string>();
        var entries=new List<(uint Hash,uint Value)>();
        foreach(var n in nodes)
        {
            var hash=PfsPrimitives.PathHash(n.FullPath);
            if(byHash.TryGetValue(hash,out var existing))
                throw new InvalidDataException($"PFS path-hash collision: {n.FullPath} and {existing}");
            byHash.Add(hash,n.FullPath);
            entries.Add((hash,n.Inode | (n.IsDir ? 0x20000000u : 0u)));
        }
        var ordered=entries.OrderBy(x=>x.Hash).ToArray();
        var data = new byte[ordered.Length * 8];
        for (var i = 0; i < entries.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 8, 4), ordered[i].Hash);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 8 + 4, 4), ordered[i].Value);
        }
        return data;
    }

    private static long DirectoryPayloadSize(Node d) =>
        PfsPrimitives.DirentSize(".") + PfsPrimitives.DirentSize("..") +
        d.Children.Sum(c => PfsPrimitives.DirentSize(c.Name));

    private static ushort DirLinks(Node d) => (ushort)(2 + d.Children.Count(c => c.IsDir));

    private static async Task WriteDirectory(FileStream output, Node d, Node parent, CancellationToken ct)
    {
        output.Position = (long)d.StartBlock * PfsPrimitives.BlockSize;
        PfsPrimitives.WriteDirent(output, d.Inode, 4, ".");
        PfsPrimitives.WriteDirent(output, parent.Inode, 5, "..");
        foreach (var c in d.Children)
            PfsPrimitives.WriteDirent(output, c.Inode, c.IsDir ? 3 : 2, c.Name);
        await output.FlushAsync(ct);
    }

    private static void WriteHeader(Stream s, int inodeCount, int inodeBlocks, int totalBlocks)
    {
        Span<byte> h = stackalloc byte[0x380];
        BinaryPrimitives.WriteInt64LittleEndian(h[0x00..], 1);
        BinaryPrimitives.WriteInt64LittleEndian(h[0x08..], PfsPrimitives.HeaderMagic);
        h[0x1A] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(h[0x1C..], 0x8);
        BinaryPrimitives.WriteUInt32LittleEndian(h[0x20..], PfsPrimitives.BlockSize);
        BinaryPrimitives.WriteInt64LittleEndian(h[0x28..], totalBlocks);
        BinaryPrimitives.WriteInt64LittleEndian(h[0x30..], inodeCount);
        BinaryPrimitives.WriteInt64LittleEndian(h[0x38..], totalBlocks);
        BinaryPrimitives.WriteInt64LittleEndian(h[0x40..], inodeBlocks);
        BinaryPrimitives.WriteInt32LittleEndian(h[0x368..], 1);
        s.Position = 0;
        s.Write(h);
    }

    private static void WriteInodeAt(Stream s,uint inode,int inodesPerBlock,ushort mode,ushort nlink,uint flags,long size,uint blocks,int startBlock,long timestamp)
    {
        var inodeBlock=inode/(uint)inodesPerBlock;
        var slot=inode%(uint)inodesPerBlock;
        s.Position=(1L+inodeBlock)*PfsPrimitives.BlockSize+slot*PfsPrimitives.UnsignedInodeSize;
        WriteInode(s,mode,nlink,flags,size,blocks,startBlock,timestamp);
    }

    private static void WriteInode(Stream s, ushort mode, ushort nlink, uint flags, long size, uint blocks, int startBlock, long timestamp)
    {
        Span<byte> d = stackalloc byte[PfsPrimitives.UnsignedInodeSize];
        BinaryPrimitives.WriteUInt16LittleEndian(d[0..], mode);
        BinaryPrimitives.WriteUInt16LittleEndian(d[2..], nlink);
        BinaryPrimitives.WriteUInt32LittleEndian(d[4..], flags);
        BinaryPrimitives.WriteInt64LittleEndian(d[8..], size);
        BinaryPrimitives.WriteInt64LittleEndian(d[16..], size);
        for (var i = 0; i < 4; i++) BinaryPrimitives.WriteInt64LittleEndian(d[(24 + i * 8)..], timestamp);
        BinaryPrimitives.WriteUInt32LittleEndian(d[96..], blocks);
        BinaryPrimitives.WriteInt32LittleEndian(d[100..], startBlock);
        s.Write(d);
    }
}

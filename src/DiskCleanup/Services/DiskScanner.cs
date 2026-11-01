using System.Collections.Concurrent;
using System.IO.Enumeration;
using DiskCleanup.Models;

namespace DiskCleanup.Services;

internal sealed class ExtensionStat
{
    public string Extension { get; set; } = string.Empty;
    public long Bytes;
    public long Count;
}

internal sealed class ScanResult
{
    public ScanResult(FileNode root)
    {
        Root = root;
    }

    public FileNode Root { get; }
    public long FilesSeen { get; set; }
    public long BytesSeen { get; set; }
    public long UnreadableFolders { get; set; }
    public List<ExtensionStat> Extensions { get; set; } = new List<ExtensionStat>();
    public List<FileNode> BiggestFiles { get; set; } = new List<FileNode>();
}

internal readonly struct ScanSnapshot
{
    public ScanSnapshot(long files, long bytes, long skipped, string current)
    {
        Files = files;
        Bytes = bytes;
        Skipped = skipped;
        Current = current;
    }

    public long Files { get; }
    public long Bytes { get; }
    public long Skipped { get; }
    public string Current { get; }
}

/// <summary>
/// Read-only scan. Never changes a file. Skips junctions, symlinks and other
/// reparse points so nothing is counted twice, and keeps only the folders and
/// files that are big enough to matter so the tree stays small.
/// </summary>
internal sealed class DiskScanner
{
    public const long FolderThreshold = 10L * 1024 * 1024;
    public const long FileThreshold = 25L * 1024 * 1024;

    private readonly IReadOnlyDictionary<string, KnownLocation> _known;
    private readonly ConcurrentDictionary<string, ExtensionStat> _ext =
        new ConcurrentDictionary<string, ExtensionStat>(StringComparer.OrdinalIgnoreCase);
    private long _files;
    private long _bytes;
    private long _skipped;
    private volatile string _current = string.Empty;

    public DiskScanner(IReadOnlyDictionary<string, KnownLocation> known)
    {
        _known = known;
    }

    public ScanSnapshot Snapshot() =>
        new ScanSnapshot(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes), Interlocked.Read(ref _skipped), _current);

    public Task<ScanResult> ScanAsync(string driveRoot, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var rootPath = PathSafety.Normalize(driveRoot);
            var root = ScanDirectory(rootPath, rootPath, 0, false, token, true);

            var biggest = root.Descendants()
                .Where(n => !n.IsDirectory)
                .OrderByDescending(n => n.Size)
                .Take(300)
                .ToList();

            return new ScanResult(root)
            {
                FilesSeen = Interlocked.Read(ref _files),
                BytesSeen = Interlocked.Read(ref _bytes),
                UnreadableFolders = Interlocked.Read(ref _skipped),
                Extensions = _ext.Values.OrderByDescending(e => e.Bytes).Take(60).ToList(),
                BiggestFiles = biggest
            };
        }, token);
    }

    private readonly struct Entry
    {
        public Entry(string name, bool isDirectory, long length, FileAttributes attributes)
        {
            Name = name;
            IsDirectory = isDirectory;
            Length = length;
            Attributes = attributes;
        }

        public string Name { get; }
        public bool IsDirectory { get; }
        public long Length { get; }
        public FileAttributes Attributes { get; }
    }

    private static List<Entry> ReadEntries(string path)
    {
        var options = new EnumerationOptions
        {
            // The default skips hidden and system files, which would hide most of the space.
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false
        };
        var enumerable = new FileSystemEnumerable<Entry>(
            path,
            (ref FileSystemEntry e) => new Entry(e.FileName.ToString(), e.IsDirectory, e.IsDirectory ? 0 : e.Length, e.Attributes),
            options);
        return enumerable.ToList();
    }

    private FileNode ScanDirectory(string path, string name, int depth, bool parentProtected, CancellationToken token, bool isRoot)
    {
        token.ThrowIfCancellationRequested();
        _current = path;

        var protectedHere = PathSafety.IsProtected(path);
        var node = new FileNode(isRoot ? path : name, path, true, 0)
        {
            IsProtected = protectedHere,
            WholeBlocked = PathSafety.IsWholeBlocked(path)
        };
        ApplyHint(node);

        List<Entry> entries;
        try
        {
            entries = ReadEntries(path);
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _skipped);
            return node;
        }

        long total = 0;
        var kept = new List<FileNode>();
        var subDirs = new List<Entry>();

        foreach (var e in entries)
        {
            if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;

            if (e.IsDirectory)
            {
                if (e.Name.Equals(".git", StringComparison.OrdinalIgnoreCase)) node.WholeBlocked = true;
                subDirs.Add(e);
                continue;
            }

            total += e.Length;
            Interlocked.Increment(ref _files);
            Interlocked.Add(ref _bytes, e.Length);
            var ext = Path.GetExtension(e.Name);
            if (string.IsNullOrEmpty(ext)) ext = "(none)";
            var stat = _ext.GetOrAdd(ext.ToLowerInvariant(), k => new ExtensionStat { Extension = k });
            Interlocked.Add(ref stat.Bytes, e.Length);
            Interlocked.Increment(ref stat.Count);

            if (e.Length >= FileThreshold)
            {
                var filePath = Path.Combine(path, e.Name);
                var file = new FileNode(e.Name, filePath, false, e.Length)
                {
                    IsProtected = protectedHere || PathSafety.IsProtected(filePath)
                };
                ApplyHint(file);
                kept.Add(file);
            }
        }

        var results = new FileNode?[subDirs.Count];
        void Work(int i)
        {
            var sd = subDirs[i];
            results[i] = ScanDirectory(Path.Combine(path, sd.Name), sd.Name, depth + 1, protectedHere, token, false);
        }

        if (depth < 3 && subDirs.Count > 1)
        {
            Parallel.For(0, subDirs.Count, new ParallelOptions
            {
                CancellationToken = token,
                MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount)
            }, Work);
        }
        else
        {
            for (var i = 0; i < subDirs.Count; i++) Work(i);
        }

        foreach (var child in results)
        {
            if (child == null) continue;
            total += child.Size;
            if (child.Size >= FolderThreshold) kept.Add(child);
        }

        node.Size = total;
        foreach (var child in kept.OrderByDescending(c => c.Size)) node.AddChild(child);
        return node;
    }

    private void ApplyHint(FileNode node)
    {
        if (_known.TryGetValue(PathSafety.Normalize(node.FullPath), out var known))
        {
            node.Advice = known.Advice;
            node.Hint = known.Description;
        }
        else if (PathSafety.IsUnderOneDrive(node.FullPath) &&
                 PathSafety.Normalize(node.FullPath).Equals(
                     PathSafety.Normalize(Environment.GetEnvironmentVariable("OneDrive") ?? node.FullPath + "?"),
                     StringComparison.OrdinalIgnoreCase))
        {
            node.Advice = Advice.Check;
            node.Hint = "OneDrive folder. Removing files here also removes them from OneDrive online.";
        }

        if (node.IsProtected && string.IsNullOrEmpty(node.Hint) && PathSafety.IsProtected(node.FullPath, out var reason))
        {
            node.Hint = "Protected. " + reason;
        }
    }
}

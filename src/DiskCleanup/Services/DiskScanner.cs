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
/// File type totals for one thread. Looks an extension up straight from the file
/// name without allocating, so counting a million files costs almost nothing.
/// </summary>
internal sealed class ExtensionTable
{
    private string?[] _keys = new string?[512];
    private long[] _bytes = new long[512];
    private long[] _counts = new long[512];
    private int _used;

    public void Add(ReadOnlySpan<char> fileName, long length)
    {
        var dot = fileName.LastIndexOf('.');
        var ext = dot < 0 || dot == fileName.Length - 1 ? "(none)".AsSpan() : fileName.Slice(dot);

        var hash = 17;
        foreach (var c in ext) hash = unchecked(hash * 31 + char.ToLowerInvariant(c));

        var mask = _keys.Length - 1;
        var i = hash & mask;
        while (true)
        {
            var key = _keys[i];
            if (key == null) break;
            if (ext.Equals(key, StringComparison.OrdinalIgnoreCase))
            {
                _bytes[i] += length;
                _counts[i]++;
                return;
            }
            i = (i + 1) & mask;
        }

        if ((_used + 1) * 2 > _keys.Length)
        {
            Grow();
            Add(fileName, length);
            return;
        }
        _keys[i] = ext.ToString().ToLowerInvariant();
        _bytes[i] = length;
        _counts[i] = 1;
        _used++;
    }

    private void Grow()
    {
        var oldKeys = _keys;
        var oldBytes = _bytes;
        var oldCounts = _counts;
        _keys = new string?[oldKeys.Length * 2];
        _bytes = new long[oldKeys.Length * 2];
        _counts = new long[oldKeys.Length * 2];
        _used = 0;
        var mask = _keys.Length - 1;
        for (var n = 0; n < oldKeys.Length; n++)
        {
            var key = oldKeys[n];
            if (key == null) continue;
            var hash = 17;
            foreach (var c in key) hash = unchecked(hash * 31 + char.ToLowerInvariant(c));
            var i = hash & mask;
            while (_keys[i] != null) i = (i + 1) & mask;
            _keys[i] = key;
            _bytes[i] = oldBytes[n];
            _counts[i] = oldCounts[n];
            _used++;
        }
    }

    public IEnumerable<(string Extension, long Bytes, long Count)> Entries()
    {
        for (var i = 0; i < _keys.Length; i++)
        {
            if (_keys[i] != null) yield return (_keys[i]!, _bytes[i], _counts[i]);
        }
    }
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

    /// <summary>How deep folders are split across threads (99 = every level), and how many threads (0 = one per core). Measured with ScanBench: splitting every level was 2.7x faster than 3 levels on C:.</summary>
    internal static int ParallelDepth = 99;
    internal static int Degree = 0;

    [ThreadStatic]
    private static ExtensionTable? t_table;

    [ThreadStatic]
    private static DiskScanner? t_owner;

    private readonly IReadOnlyDictionary<string, KnownLocation> _known;
    private readonly ConcurrentBag<ExtensionTable> _tables = new ConcurrentBag<ExtensionTable>();
    private readonly string? _oneDriveRoot;
    private bool _rootIsDrive;
    private long _files;
    private long _bytes;
    private long _skipped;
    private volatile string _current = string.Empty;

    public DiskScanner(IReadOnlyDictionary<string, KnownLocation> known)
    {
        _known = known;
        var od = Environment.GetEnvironmentVariable("OneDrive");
        _oneDriveRoot = string.IsNullOrWhiteSpace(od) ? null : PathSafety.Normalize(od);
    }

    public ScanSnapshot Snapshot() =>
        new ScanSnapshot(Interlocked.Read(ref _files), Interlocked.Read(ref _bytes), Interlocked.Read(ref _skipped), _current);

    public Task<ScanResult> ScanAsync(string driveRoot, CancellationToken token)
    {
        return Task.Run(() =>
        {
            var rootPath = PathSafety.Normalize(driveRoot);
            _rootIsDrive = PathSafety.IsDriveRoot(rootPath);
            var rootResult = ScanDirectory(rootPath, rootPath, 0, false, token, true);
            var root = rootResult.Node!;
            ApplyHint(root);

            var biggest = root.Descendants()
                .Where(n => !n.IsDirectory)
                .OrderByDescending(n => n.Size)
                .Take(300)
                .ToList();

            var merged = new Dictionary<string, ExtensionStat>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in _tables)
            {
                foreach (var (ext, bytes, count) in table.Entries())
                {
                    if (!merged.TryGetValue(ext, out var stat))
                    {
                        stat = new ExtensionStat { Extension = ext };
                        merged[ext] = stat;
                    }
                    stat.Bytes += bytes;
                    stat.Count += count;
                }
            }

            return new ScanResult(root)
            {
                FilesSeen = Interlocked.Read(ref _files),
                BytesSeen = Interlocked.Read(ref _bytes),
                UnreadableFolders = Interlocked.Read(ref _skipped),
                Extensions = merged.Values.OrderByDescending(e => e.Bytes).Take(60).ToList(),
                BiggestFiles = biggest
            };
        }, token);
    }

    /// <summary>The table for the current thread, registered for this scan.</summary>
    private ExtensionTable Table()
    {
        if (!ReferenceEquals(t_owner, this) || t_table == null)
        {
            t_table = new ExtensionTable();
            t_owner = this;
            _tables.Add(t_table);
        }
        return t_table;
    }

    private readonly struct Entry
    {
        public Entry(string? name, bool isDirectory, long length)
        {
            Name = name;
            IsDirectory = isDirectory;
            Length = length;
        }

        /// <summary>Only set for folders and for files big enough to keep.</summary>
        public string? Name { get; }
        public bool IsDirectory { get; }
        public long Length { get; }
    }

    private readonly struct DirResult
    {
        public DirResult(long size, FileNode? node)
        {
            Size = size;
            Node = node;
        }

        public long Size { get; }
        public FileNode? Node { get; }
    }

    private FileSystemEnumerable<Entry> Enumerate(string path)
    {
        var options = new EnumerationOptions
        {
            // The default skips hidden and system files, which would hide most of the space.
            AttributesToSkip = FileAttributes.None,
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            ReturnSpecialDirectories = false,
            BufferSize = 64 * 1024
        };

        var table = Table();
        return new FileSystemEnumerable<Entry>(
            path,
            (ref FileSystemEntry e) =>
            {
                if (e.IsDirectory) return new Entry(e.FileName.ToString(), true, 0);
                var length = e.Length;
                table.Add(e.FileName, length);
                return new Entry(length >= FileThreshold ? e.FileName.ToString() : null, false, length);
            },
            options)
        {
            // Links and junctions are skipped so nothing is counted twice.
            ShouldIncludePredicate = (ref FileSystemEntry e) => (e.Attributes & FileAttributes.ReparsePoint) == 0
        };
    }

    private DirResult ScanDirectory(string path, string name, int depth, bool parentProtected, CancellationToken token, bool isRoot)
    {
        token.ThrowIfCancellationRequested();
        _current = path;

        long total = 0;
        long files = 0;
        var kept = new List<FileNode>();
        List<string>? subDirs = null;
        var hasGit = false;

        // Only the protected flag has to be known before reading the folder, so files can be flagged as they are seen.
        var protectedHere = isRoot ? PathSafety.IsProtected(path) : parentProtected;

        try
        {
            foreach (var e in Enumerate(path))
            {
                if (e.IsDirectory)
                {
                    if (e.Name!.Equals(".git", StringComparison.OrdinalIgnoreCase)) hasGit = true;
                    (subDirs ??= new List<string>()).Add(e.Name!);
                    continue;
                }

                total += e.Length;
                files++;

                if (e.Name != null)
                {
                    var filePath = Path.Combine(path, e.Name);
                    var file = new FileNode(e.Name, filePath, false, e.Length)
                    {
                        IsProtected = PathSafety.IsProtectedChild(filePath, e.Name, protectedHere, isRoot && _rootIsDrive)
                    };
                    ApplyHint(file);
                    kept.Add(file);
                }
            }
        }
        catch (Exception)
        {
            Interlocked.Increment(ref _skipped);
        }

        Interlocked.Add(ref _files, files);
        Interlocked.Add(ref _bytes, total);

        if (subDirs != null)
        {
            var results = new DirResult[subDirs.Count];
            void Work(int i)
            {
                var childPath = Path.Combine(path, subDirs[i]);
                var childProtected = PathSafety.IsProtectedChild(childPath, subDirs[i], protectedHere, isRoot && _rootIsDrive);
                results[i] = ScanDirectory(childPath, subDirs[i], depth + 1, childProtected, token, false);
            }

            if (depth < ParallelDepth && subDirs.Count > 1)
            {
                Parallel.For(0, subDirs.Count, new ParallelOptions
                {
                    CancellationToken = token,
                    MaxDegreeOfParallelism = Degree > 0 ? Degree : Math.Max(2, Environment.ProcessorCount)
                }, Work);
            }
            else
            {
                for (var i = 0; i < subDirs.Count; i++) Work(i);
            }

            foreach (var r in results)
            {
                total += r.Size;
                if (r.Node != null && r.Size >= FolderThreshold)
                {
                    ApplyHint(r.Node);
                    kept.Add(r.Node);
                }
            }
        }

        // Small folders are only counted. A node is built just for the ones that are kept.
        if (!isRoot && total < FolderThreshold) return new DirResult(total, null);

        var node = new FileNode(isRoot ? path : name, path, true, total)
        {
            IsProtected = protectedHere,
            WholeBlocked = hasGit || PathSafety.IsWholeBlockedFast(path, isRoot && _rootIsDrive)
        };
        foreach (var child in kept.OrderByDescending(c => c.Size)) node.AddChild(child);
        return new DirResult(total, node);
    }

    private void ApplyHint(FileNode node)
    {
        if (_known.Count > 0 && _known.TryGetValue(node.FullPath, out var known))
        {
            node.Advice = known.Advice;
            node.Hint = known.Description;
        }
        else if (_oneDriveRoot != null && node.FullPath.Equals(_oneDriveRoot, StringComparison.OrdinalIgnoreCase))
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

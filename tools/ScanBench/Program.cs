using System.Diagnostics;
using DiskCleanup.Models;
using DiskCleanup.Services;

// Times the scanner. Read only. Usage: ScanBench <path> [runs]
// Prints warm timings (the first run warms the Windows file cache) plus the
// totals, so a faster version can be checked for the same answer.
var path = args.Length > 0 ? args[0] : "D:\\";
if (args.Length > 2) DiskScanner.ParallelDepth = int.Parse(args[2]);
if (args.Length > 3) DiskScanner.Degree = int.Parse(args[3]);
Console.WriteLine("cpus=" + Environment.ProcessorCount + " depth=" + DiskScanner.ParallelDepth + " degree=" + DiskScanner.Degree);
var runs = args.Length > 1 ? int.Parse(args[1]) : 3;

Console.WriteLine("Scanning " + path + " (" + runs + " runs, first run is warm-up)");
var times = new List<double>();
ScanResult? last = null;
for (var i = 0; i < runs; i++)
{
    GC.Collect();
    var scanner = new DiskScanner(new Dictionary<string, KnownLocation>());
    var sw = Stopwatch.StartNew();
    last = await scanner.ScanAsync(path, CancellationToken.None);
    sw.Stop();
    var nodes = 1 + last.Root.Descendants().Count();
    Console.WriteLine("run {0}: {1,7:N2}s  files={2:N0}  bytes={3:N0}  nodes={4:N0}  unreadable={5:N0}  gc={6}MB",
        i + 1, sw.Elapsed.TotalSeconds, last.FilesSeen, last.BytesSeen, nodes, last.UnreadableFolders,
        GC.GetTotalMemory(false) / (1024 * 1024));
    if (i > 0) times.Add(sw.Elapsed.TotalSeconds);
}
if (times.Count > 0)
{
    Console.WriteLine("warm average: {0:N2}s  best: {1:N2}s  files/sec: {2:N0}",
        times.Average(), times.Min(), last!.FilesSeen / times.Min());
}

// Fingerprint of everything the UI depends on except sizes (sizes drift on a live drive):
// path, protected flag, whole-blocked flag, advice and kind, for every node kept in the tree.
if (last != null)
{
    var lines = last.Root.Descendants().Prepend(last.Root)
        .Select(n => n.FullPath + "|" + (n.IsProtected ? 1 : 0) + (n.WholeBlocked ? 1 : 0) + "|" + n.Advice + "|" + (n.IsDirectory ? "d" : "f"))
        .OrderBy(s => s, StringComparer.Ordinal).ToList();
    var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", lines)));
    Console.WriteLine("fingerprint: " + Convert.ToHexString(hash).Substring(0, 16) + "  nodes=" + lines.Count
        + "  protected=" + last.Root.Descendants().Count(n => n.IsProtected)
        + "  wholeBlocked=" + last.Root.Descendants().Count(n => n.WholeBlocked)
        + "  extTypes=" + last.Extensions.Count
        + "  extTop=" + string.Join(",", last.Extensions.Take(5).Select(e => e.Extension + ":" + e.Count)));
}

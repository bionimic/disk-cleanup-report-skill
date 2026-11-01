using System.Runtime.InteropServices;
using DiskCleanup.Models;

namespace DiskCleanup.Services;

internal sealed class PlanItem
{
    public PlanItem(FileNode node)
    {
        Node = node;
        Path = PathSafety.Normalize(node.FullPath);
        Size = node.Size;
        IsDirectory = node.IsDirectory;
        ContentsOnly = node.ContentsOnly;
        Advice = node.Advice;
    }

    public FileNode Node { get; }
    public string Path { get; }
    public long Size { get; }
    public bool IsDirectory { get; }
    public bool ContentsOnly { get; }
    public Advice Advice { get; }

    /// <summary>Set when this item must not be moved. The reason is shown to the user.</summary>
    public string? Problem { get; set; }

    public string Drive => Path.Substring(0, Math.Min(3, Path.Length)).ToUpperInvariant();
    public string Description => ContentsOnly ? "Contents of folder" : IsDirectory ? "Folder" : "File";
}

internal sealed class DrivePlan
{
    public string Drive { get; set; } = string.Empty;
    public long Need { get; set; }
    public BinRoom Bin { get; set; }
    public string? Block { get; set; }
}

internal sealed class RecycleResult
{
    public string Path { get; set; } = string.Empty;
    public long MovedBytes { get; set; }
    public long LeftBytes { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public FileNode? Node { get; set; }
    public bool FullyMoved { get; set; }
}

internal static class SelectionPlan
{
    /// <summary>
    /// The exact list of things that would move. A fully ticked folder counts once,
    /// and anything inside another selected item is dropped so nothing is counted twice.
    /// </summary>
    public static List<PlanItem> Build(IEnumerable<FileNode> roots, bool validate = true)
    {
        var raw = new List<PlanItem>();
        foreach (var root in roots) Collect(root, raw);

        var accepted = new List<PlanItem>();
        foreach (var item in raw.OrderBy(i => i.Path.Length))
        {
            if (accepted.Any(a => PathSafety.IsUnder(item.Path, a.Path))) continue;
            accepted.Add(item);
        }
        if (validate)
        {
            foreach (var item in accepted) item.Problem = Validate(item);
        }
        return accepted.OrderByDescending(i => i.Size).ToList();
    }

    private static void Collect(FileNode node, List<PlanItem> into)
    {
        if (node.IsChecked == false) return;
        if (node.IsChecked == true && node.CanSelectWhole)
        {
            into.Add(new PlanItem(node));
            return;
        }
        foreach (var child in node.Children) Collect(child, into);
    }

    public static string? Validate(PlanItem item)
    {
        if (!PathSafety.IsFixedDrive(item.Path)) return "Not on a fixed drive, so it has no Recycle Bin.";
        if (PathSafety.IsProtected(item.Path, out var reason)) return "Protected. " + reason;
        if (item.IsDirectory && !item.ContentsOnly && PathSafety.IsWholeBlocked(item.Path))
            return "This folder cannot be removed as a whole.";
        if (item.Path.Length > PathSafety.MaxRecyclablePath)
            return "The path is too long for the Recycle Bin. Windows would delete it for good.";
        if (!File.Exists(item.Path) && !Directory.Exists(item.Path)) return "No longer exists.";
        return null;
    }

    /// <summary>Checks each drive's Recycle Bin before anything moves.</summary>
    public static List<DrivePlan> Preflight(IReadOnlyList<PlanItem> plan)
    {
        var result = new List<DrivePlan>();
        foreach (var group in plan.Where(p => p.Problem == null).GroupBy(p => p.Drive))
        {
            var need = group.Sum(p => p.Size);
            var bin = BinService.GetBinRoom(group.Key);
            var dp = new DrivePlan { Drive = group.Key, Need = need, Bin = bin };

            if (bin.Kind == BinRoomKind.DeletesImmediately)
            {
                dp.Block = "This drive's Recycle Bin is set to delete files immediately. Nothing was moved from it.";
            }
            else if (bin.Kind == BinRoomKind.Known && need > bin.Room)
            {
                dp.Block = "The Recycle Bin has room for " + Format.Size(bin.Room) + " but these items are " +
                           Format.Size(need) + ". Empty the bin first, then try again.";
            }

            if (dp.Block != null)
            {
                foreach (var item in group) item.Problem = dp.Block;
            }
            result.Add(dp);
        }
        return result;
    }
}

internal static class RecycleService
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ShFileOpStruct
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)] public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)] public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHFileOperation(ref ShFileOpStruct op);

    private const uint FoDelete = 0x3;
    private const ushort FofSilent = 0x4;
    private const ushort FofNoConfirmation = 0x10;
    private const ushort FofAllowUndo = 0x40;
    private const ushort FofNoErrorUi = 0x400;
    private const ushort FofWantNukeWarning = 0x4000;

    // Never touched when moving the contents of a folder.
    private static readonly string[] ExcludedNames = { "DiskCleanup" };

    /// <summary>Sends one path to the Recycle Bin (never a permanent delete). Returns the Windows result code.</summary>
    internal static int SendToRecycleBin(string path, IntPtr hwnd)
    {
        var op = new ShFileOpStruct
        {
            hwnd = hwnd,
            wFunc = FoDelete,
            pFrom = path + "\0\0",
            fFlags = (ushort)(FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi | FofWantNukeWarning)
        };
        return SHFileOperation(ref op);
    }

    public static List<RecycleResult> Execute(
        IReadOnlyList<PlanItem> plan,
        IntPtr hwnd,
        bool allowUnknownRoom,
        IProgress<string>? progress,
        CancellationToken token)
    {
        var results = new List<RecycleResult>();
        var rooms = new Dictionary<string, BinRoom>();
        var remaining = new Dictionary<string, long>();

        foreach (var item in plan)
        {
            token.ThrowIfCancellationRequested();
            var result = new RecycleResult { Path = item.Path, Node = item.Node };
            results.Add(result);

            // Ask again, at the moment of deletion, whether this is still allowed.
            var problem = item.Problem ?? SelectionPlan.Validate(item);
            if (problem != null)
            {
                result.Status = "Skipped";
                result.Message = problem;
                continue;
            }

            if (!rooms.TryGetValue(item.Drive, out var room))
            {
                room = BinService.GetBinRoom(item.Drive);
                rooms[item.Drive] = room;
                remaining[item.Drive] = room.Room;
            }

            if (room.Kind == BinRoomKind.DeletesImmediately)
            {
                result.Status = "Skipped";
                result.Message = "The Recycle Bin on this drive deletes files immediately.";
                continue;
            }
            if (room.Kind == BinRoomKind.Unknown && !allowUnknownRoom)
            {
                result.Status = "Skipped";
                result.Message = "The Recycle Bin size could not be checked.";
                continue;
            }

            progress?.Report(item.Path);
            var targets = item.ContentsOnly ? ExpandContents(item.Path) : new List<string> { item.Path };
            long moved = 0, left = 0;
            var notes = new List<string>();

            foreach (var target in targets)
            {
                token.ThrowIfCancellationRequested();
                var t = MoveOne(target, item.Drive, room, remaining, hwnd, notes);
                moved += t.Moved;
                left += t.Left;
            }

            result.MovedBytes = moved;
            result.LeftBytes = left;
            result.Message = string.Join("; ", notes.Distinct().Take(3));
            if (left == 0 && notes.Count == 0)
            {
                result.Status = "Moved";
                result.FullyMoved = !item.ContentsOnly;
            }
            else if (moved > 0)
            {
                result.Status = "Partly moved";
            }
            else
            {
                result.Status = "Not moved";
            }
        }
        return results;
    }

    private static List<string> ExpandContents(string folder)
    {
        var list = new List<string>();
        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                var name = Path.GetFileName(entry);
                if (ExcludedNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                list.Add(entry);
            }
        }
        catch
        {
            // Unreadable folder: nothing to move.
        }
        return list;
    }

    private static (long Moved, long Left) MoveOne(
        string target,
        string drive,
        BinRoom room,
        Dictionary<string, long> remaining,
        IntPtr hwnd,
        List<string> notes)
    {
        try
        {
            if (PathSafety.IsProtected(target))
            {
                notes.Add("Protected item skipped");
                return (0, 0);
            }
            if (target.Length > PathSafety.MaxRecyclablePath)
            {
                notes.Add("Path too long, skipped");
                return (0, 0);
            }

            var attrs = File.GetAttributes(target);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                notes.Add("Link skipped");
                return (0, 0);
            }

            var size = FileSizing.GetSize(target);
            if (room.Kind == BinRoomKind.Known && size > remaining[drive])
            {
                notes.Add("Too big for the room left in the Recycle Bin");
                return (0, size);
            }

            var rc = SendToRecycleBin(target, hwnd);
            var exists = File.Exists(target) || Directory.Exists(target);
            var rest = exists ? FileSizing.GetSize(target) : 0;
            var moved = Math.Max(0, size - rest);
            if (room.Kind == BinRoomKind.Known) remaining[drive] = Math.Max(0, remaining[drive] - moved);

            if (rc != 0 && rest > 0) notes.Add("Some files are in use");
            else if (rc != 0 && exists) notes.Add("Windows refused (code " + rc + ")");
            return (moved, rest);
        }
        catch (Exception ex)
        {
            notes.Add(ex.Message);
            return (0, 0);
        }
    }
}

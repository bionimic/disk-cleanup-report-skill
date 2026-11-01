using DiskCleanup.Models;

namespace DiskCleanup.Services;

/// <summary>
/// Spots cleanup candidates inside a finished scan, on any drive. It only
/// labels items. Nothing is ticked and nothing moves until the user does it.
/// </summary>
internal static class Suggestions
{
    private static readonly HashSet<string> SafeFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "node_modules", "__pycache__", ".pytest_cache", ".mypy_cache", "ShaderCache", "GPUCache", "Code Cache"
    };

    private static readonly HashSet<string> CheckFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Temp", "tmp"
    };

    private static readonly HashSet<string> SafeFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".dmp", ".mdmp", ".hprof"
    };

    private static readonly HashSet<string> CheckFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".msi", ".exe", ".zip", ".rar", ".7z", ".img", ".bak"
    };

    /// <summary>Files of these types must be at least this big to be suggested.</summary>
    public const long MinFileBytes = 100L * 1024 * 1024;

    /// <summary>Returns the advice and a short reason, or null when the item is not a candidate.</summary>
    public static (Advice Advice, string Hint)? Classify(string name, bool isDirectory, long size)
    {
        if (isDirectory)
        {
            if (SafeFolders.Contains(name)) return (Advice.Safe, "Rebuilds itself when needed.");
            if (CheckFolders.Contains(name)) return (Advice.Check, "Temporary folder. Make sure nothing you need is in it.");
            return null;
        }

        if (size < MinFileBytes) return null;
        var ext = Path.GetExtension(name);
        if (SafeFiles.Contains(ext)) return (Advice.Safe, "Crash or memory dump.");
        if (CheckFiles.Contains(ext)) return (Advice.Check, "Installer, archive or backup. Keep it only if you still need it.");
        return null;
    }

    /// <summary>
    /// Labels candidates in the tree and returns them. A candidate inside another
    /// candidate is left out, so each space is offered once.
    /// </summary>
    public static List<FileNode> Apply(FileNode root)
    {
        var found = new List<FileNode>();
        Walk(root, found);
        return found.OrderByDescending(n => n.Size).ToList();
    }

    private static void Walk(FileNode node, List<FileNode> found)
    {
        foreach (var child in node.Children)
        {
            if (!child.IsProtected && child.Advice == Advice.None)
            {
                var hit = Classify(child.Name, child.IsDirectory, child.Size);
                if (hit != null)
                {
                    child.Advice = hit.Value.Advice;
                    child.Hint = hit.Value.Hint;
                }
            }

            if (!child.IsProtected && child.Advice != Advice.None && child.CanSelectWhole && !string.IsNullOrEmpty(child.Hint)
                && Classify(child.Name, child.IsDirectory, child.Size) != null)
            {
                found.Add(child);
                continue;
            }
            Walk(child, found);
        }
    }
}

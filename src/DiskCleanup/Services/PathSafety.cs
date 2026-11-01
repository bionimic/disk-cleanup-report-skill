namespace DiskCleanup.Services;

/// <summary>
/// The single place that decides what may never be removed. The scanner uses it
/// to disable checkboxes, and the recycle step asks it again for every path
/// right before anything moves.
/// </summary>
internal static class PathSafety
{
    // Names that are protected only directly under a drive root.
    private static readonly HashSet<string> RootNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "System Volume Information", "$Recycle.Bin", "$WinREAgent", "$SysReset", "$Windows.~BT", "$Windows.~WS",
        "Recovery", "Boot", "EFI", "Config.Msi", "Documents and Settings",
        "bootmgr", "BOOTNXT", "pagefile.sys", "hiberfil.sys", "swapfile.sys", "DumpStack.log.tmp"
    };

    // Names that are protected wherever they appear.
    private static readonly HashSet<string> AnywhereNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "WinSxS", ".git", "UsrClass.dat"
    };

    // Safe places inside the protected Windows folder.
    private static List<string> _allowedInsideProtected = BuildAllowed();
    private static List<string> _protectedPaths = BuildProtectedPaths();
    private static List<string> _containerPaths = BuildContainerPaths();

    /// <summary>For tests: rebuild the lists after changing the environment.</summary>
    internal static void Reset()
    {
        _allowedInsideProtected = BuildAllowed();
        _protectedPaths = BuildProtectedPaths();
        _containerPaths = BuildContainerPaths();
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        return full.Length > root.Length ? full.TrimEnd('\\', '/') : full;
    }

    private static List<string> BuildAllowed()
    {
        var list = new List<string>();
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            list.Add(Normalize(Path.Combine(windows, "Temp")));
            list.Add(Normalize(Path.Combine(windows, "SoftwareDistribution", "Download")));
        }
        return list;
    }

    private static List<string> BuildProtectedPaths()
    {
        var list = new List<string>();
        void Add(string? p) { if (!string.IsNullOrWhiteSpace(p)) list.Add(Normalize(p)); }

        Add(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles));
        Add(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        Add(Environment.GetEnvironmentVariable("ProgramW6432"));
        Add(AppContext.BaseDirectory);

        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(local))
        {
            // Never delete a browser profile; clear the cache from the browser instead.
            Add(Path.Combine(local, "Google", "Chrome", "User Data"));
            Add(Path.Combine(local, "Microsoft", "Edge", "User Data"));
            Add(Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"));
        }
        return list;
    }

    private static List<string> BuildContainerPaths()
    {
        var list = new List<string>();
        void Add(string? p) { if (!string.IsNullOrWhiteSpace(p)) list.Add(Normalize(p)); }

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(profile))
        {
            var norm = Normalize(profile);
            Add(norm);
            Add(Path.GetDirectoryName(norm));
            Add(Path.Combine(norm, "AppData"));
            Add(Path.Combine(norm, "AppData", "Local"));
            Add(Path.Combine(norm, "AppData", "LocalLow"));
            Add(Path.Combine(norm, "AppData", "Roaming"));
            Add(Path.Combine(norm, "Documents"));
            Add(Path.Combine(norm, "Desktop"));
            Add(Path.Combine(norm, "Downloads"));
            Add(Path.Combine(norm, "Pictures"));
            Add(Path.Combine(norm, "Videos"));
            Add(Path.Combine(norm, "Music"));
        }
        Add(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            Add(Environment.GetEnvironmentVariable(name));
        }
        return list;
    }

    public static bool IsUnder(string path, string parent)
    {
        if (path.Equals(parent, StringComparison.OrdinalIgnoreCase)) return true;
        var withSep = parent.EndsWith("\\", StringComparison.Ordinal) ? parent : parent + "\\";
        return path.StartsWith(withSep, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsDriveRoot(string path)
    {
        var norm = Normalize(path);
        return string.Equals(norm, Path.GetPathRoot(norm), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when the item (or anything above it) must never be removed.</summary>
    public static bool IsProtected(string path, out string reason)
    {
        var norm = Normalize(path);
        reason = string.Empty;

        var allowed = _allowedInsideProtected.Any(a => IsUnder(norm, a));
        if (!allowed)
        {
            foreach (var p in _protectedPaths)
            {
                if (IsUnder(norm, p))
                {
                    reason = "Inside a protected folder (" + p + ")";
                    return true;
                }
            }
        }

        var root = Path.GetPathRoot(norm) ?? string.Empty;
        var segments = norm.Substring(root.Length).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < segments.Length; i++)
        {
            var s = segments[i];
            if (i == 0 && RootNames.Contains(s))
            {
                reason = "System item (" + s + ")";
                return true;
            }
            if (AnywhereNames.Contains(s) ||
                s.StartsWith("NTUSER.DAT", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("ntuser.ini", StringComparison.OrdinalIgnoreCase))
            {
                reason = "Protected item (" + s + ")";
                return true;
            }
        }
        return false;
    }

    public static bool IsProtected(string path) => IsProtected(path, out _);

    /// <summary>
    /// True when the folder cannot be removed as one piece, because it is a
    /// drive root, a container such as a user profile, or it holds something
    /// protected. Items inside it can still be chosen one by one.
    /// </summary>
    public static bool IsWholeBlocked(string path)
    {
        var norm = Normalize(path);
        if (IsDriveRoot(norm)) return true;
        foreach (var c in _containerPaths)
        {
            if (norm.Equals(c, StringComparison.OrdinalIgnoreCase)) return true;
        }
        foreach (var p in _protectedPaths)
        {
            if (IsUnder(p, norm) && !p.Equals(norm, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public static bool IsUnderOneDrive(string path)
    {
        var norm = Normalize(path);
        foreach (var name in new[] { "OneDrive", "OneDriveConsumer", "OneDriveCommercial" })
        {
            var v = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(v) && IsUnder(norm, Normalize(v))) return true;
        }
        return false;
    }

    public static bool IsFixedDrive(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith("\\\\", StringComparison.Ordinal)) return false;
            return new DriveInfo(root).DriveType == DriveType.Fixed;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>SHFileOperation cannot take longer paths and would remove them for good.</summary>
    public const int MaxRecyclablePath = 259;
}

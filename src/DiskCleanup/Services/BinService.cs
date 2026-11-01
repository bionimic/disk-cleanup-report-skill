using System.IO.Enumeration;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace DiskCleanup.Services;

internal enum BinRoomKind
{
    /// <summary>The bin's size limit is known, so Room is meaningful.</summary>
    Known,

    /// <summary>The limit could not be read. The user must accept the risk to go on.</summary>
    Unknown,

    /// <summary>The bin is set to delete immediately. Nothing can be recycled on this drive.</summary>
    DeletesImmediately
}

internal readonly struct BinRoom
{
    public BinRoom(BinRoomKind kind, long room, long used)
    {
        Kind = kind;
        Room = room;
        Used = used;
    }

    public BinRoomKind Kind { get; }
    public long Room { get; }
    public long Used { get; }
}

internal static class BinService
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, StringBuilder volumeName, uint bufferLength);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? rootPath, uint flags);

    private const uint SherbNoConfirmation = 0x1;
    private const uint SherbNoProgressUi = 0x2;
    private const uint SherbNoSound = 0x4;

    public static long GetBinUsed(string driveRoot)
    {
        var bin = Path.Combine(driveRoot, "$Recycle.Bin");
        try
        {
            if (!Directory.Exists(bin)) return 0;
            return FileSizing.GetSize(bin, followLinks: true);
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>How much more the drive's Recycle Bin can take, read from the same settings Windows uses.</summary>
    public static BinRoom GetBinRoom(string driveRoot)
    {
        var used = GetBinUsed(driveRoot);
        try
        {
            var sb = new StringBuilder(64);
            var mount = driveRoot.EndsWith("\\", StringComparison.Ordinal) ? driveRoot : driveRoot + "\\";
            if (!GetVolumeNameForVolumeMountPoint(mount, sb, (uint)sb.Capacity)) return new BinRoom(BinRoomKind.Unknown, 0, used);

            var name = sb.ToString();
            var start = name.IndexOf('{');
            var end = name.IndexOf('}');
            if (start < 0 || end < start) return new BinRoom(BinRoomKind.Unknown, 0, used);
            var guid = name.Substring(start, end - start + 1);

            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket\Volume\" + guid);
            if (key == null) return new BinRoom(BinRoomKind.Unknown, 0, used);

            var nuke = key.GetValue("NukeOnDelete");
            if (nuke is int n && n == 1) return new BinRoom(BinRoomKind.DeletesImmediately, 0, used);

            var max = key.GetValue("MaxCapacity");
            if (max is int mb && mb > 0)
            {
                var room = (long)mb * 1024 * 1024 - used;
                return new BinRoom(BinRoomKind.Known, Math.Max(0, room), used);
            }
            return new BinRoom(BinRoomKind.Unknown, 0, used);
        }
        catch
        {
            return new BinRoom(BinRoomKind.Unknown, 0, used);
        }
    }

    /// <summary>
    /// Empties the Recycle Bin of one drive. This removes everything in that bin,
    /// including items put there earlier. Only called after the user confirms.
    /// </summary>
    public static bool EmptyDrive(IntPtr hwnd, string driveRoot)
    {
        var rc = SHEmptyRecycleBin(hwnd, driveRoot, SherbNoConfirmation | SherbNoProgressUi | SherbNoSound);
        // 0 = success, 0x80070012 or 1 = bin already empty.
        return rc == 0 || unchecked((uint)rc) == 0x80070012u || rc == 1;
    }
}

internal static class FileSizing
{
    /// <summary>Total size of every file inside a path (or the file itself). Skips links so nothing is counted twice.</summary>
    public static long GetSize(string path, bool followLinks = false)
    {
        try
        {
            if (File.Exists(path)) return new FileInfo(path).Length;
            if (!Directory.Exists(path)) return 0;

            var options = new EnumerationOptions
            {
                AttributesToSkip = followLinks ? FileAttributes.None : FileAttributes.ReparsePoint,
                IgnoreInaccessible = true,
                RecurseSubdirectories = true,
                ReturnSpecialDirectories = false
            };
            var sizes = new FileSystemEnumerable<long>(path, (ref FileSystemEntry e) => e.Length, options)
            {
                ShouldIncludePredicate = (ref FileSystemEntry e) => !e.IsDirectory
            };
            long total = 0;
            foreach (var s in sizes) total += s;
            return total;
        }
        catch
        {
            return 0;
        }
    }
}

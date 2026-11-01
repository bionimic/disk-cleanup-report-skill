using DiskCleanup.Services;

namespace DiskCleanup.Tests;

public class FastSafetyChecksTests
{
    // Walks a path one folder at a time, the way the scanner does, using the fast checks.
    private static bool FastIsProtected(string path)
    {
        var parts = path.Split('\\', StringSplitOptions.RemoveEmptyEntries);
        var current = parts[0] + "\\";
        var isProtected = PathSafety.IsProtected(current);
        var parentIsRoot = true;
        for (var i = 1; i < parts.Length; i++)
        {
            current = Path.Combine(current, parts[i]);
            isProtected = PathSafety.IsProtectedChild(current, parts[i], isProtected, parentIsRoot);
            parentIsRoot = false;
        }
        return isProtected;
    }

    [Fact]
    public void FastCheckAgreesWithTheFullCheck()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var paths = new[]
        {
            windows,
            Path.Combine(windows, "System32"),
            Path.Combine(windows, "System32", "drivers", "etc", "hosts"),
            Path.Combine(windows, "WinSxS", "x"),
            Path.Combine(windows, "Temp"),
            Path.Combine(windows, "Temp", "a", "b"),
            Path.Combine(windows, "SoftwareDistribution"),
            Path.Combine(windows, "SoftwareDistribution", "Download"),
            Path.Combine(windows, "SoftwareDistribution", "Download", "x"),
            Path.Combine(windows, "SoftwareDistribution", "DataStore"),
            programFiles,
            Path.Combine(programFiles, "App", "app.dll"),
            @"C:\hiberfil.sys",
            @"C:\pagefile.sys",
            @"C:\Recovery\x",
            @"C:\System Volume Information",
            @"D:\Games\Old",
            @"D:\Projects\app\.git",
            @"D:\Projects\app\.git\objects",
            @"D:\Projects\app\src",
            @"D:\Windows2\file",
            profile,
            Path.Combine(profile, "Downloads"),
            Path.Combine(profile, "AppData", "Local", "Temp"),
            Path.Combine(profile, "AppData", "Local", "Google", "Chrome", "User Data", "Default"),
            Path.Combine(profile, "NTUSER.DAT"),
            @"D:\hiberfil.sys",
            @"D:\Games\Recovery",
            AppContext.BaseDirectory.TrimEnd('\\'),
        };
        foreach (var p in paths)
        {
            Assert.True(PathSafety.IsProtected(p) == FastIsProtected(p), "Mismatch for " + p);
        }
    }

    [Fact]
    public void FastWholeBlockedAgreesWithTheFullCheck()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        var paths = new[]
        {
            @"C:\",
            @"D:\",
            windows,
            Path.Combine(windows, "Temp"),
            @"C:\Games",
            profile,
            Path.Combine(profile, "AppData"),
            Path.Combine(profile, "AppData", "Local"),
            Path.Combine(profile, "Downloads"),
            Path.Combine(profile, "Documents", "x"),
            Path.GetDirectoryName(profile)!,
            appDir,
            Path.GetDirectoryName(appDir)!,
        };
        foreach (var p in paths)
        {
            var norm = PathSafety.Normalize(p);
            var isRoot = PathSafety.IsDriveRoot(norm);
            Assert.True(PathSafety.IsWholeBlocked(p) == PathSafety.IsWholeBlockedFast(norm, isRoot), "Mismatch for " + p);
        }
    }
}

using DiskCleanup.Models;
using DiskCleanup.Services;

namespace DiskCleanup.Tests;

public class SafetyTests
{
    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string SystemDrive = Path.GetPathRoot(Windows)!;

    [Theory]
    [InlineData("System32")]
    [InlineData("System32\\drivers\\etc\\hosts")]
    [InlineData("WinSxS")]
    public void WindowsFolderIsProtected(string relative)
    {
        Assert.True(PathSafety.IsProtected(Path.Combine(Windows, relative)));
    }

    [Fact]
    public void ProgramFilesIsProtected()
    {
        Assert.True(PathSafety.IsProtected(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)));
        Assert.True(PathSafety.IsProtected(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Any App", "app.dll")));
    }

    [Theory]
    [InlineData("hiberfil.sys")]
    [InlineData("pagefile.sys")]
    [InlineData("swapfile.sys")]
    [InlineData("System Volume Information")]
    [InlineData("$Recycle.Bin")]
    [InlineData("Recovery")]
    public void SystemItemsAtTheDriveRootAreProtected(string name)
    {
        Assert.True(PathSafety.IsProtected(Path.Combine(SystemDrive, name)));
    }

    [Fact]
    public void GitFolderIsProtectedAnywhere()
    {
        Assert.True(PathSafety.IsProtected(Path.Combine(SystemDrive, "Projects", "app", ".git")));
        Assert.True(PathSafety.IsProtected(Path.Combine(SystemDrive, "Projects", "app", ".git", "objects", "ab")));
    }

    [Fact]
    public void WindowsTempAndUpdateDownloadsAreAllowed()
    {
        Assert.False(PathSafety.IsProtected(Path.Combine(Windows, "Temp")));
        Assert.False(PathSafety.IsProtected(Path.Combine(Windows, "Temp", "x.tmp")));
        Assert.False(PathSafety.IsProtected(Path.Combine(Windows, "SoftwareDistribution", "Download")));
        Assert.True(PathSafety.IsProtected(Path.Combine(Windows, "SoftwareDistribution", "DataStore")));
    }

    [Fact]
    public void OrdinaryUserFolderIsNotProtected()
    {
        Assert.False(PathSafety.IsProtected(Path.Combine(SystemDrive, "Games", "OldGame")));
    }

    [Fact]
    public void DriveRootAndUserProfileCannotBeRemovedWhole()
    {
        Assert.True(PathSafety.IsWholeBlocked(SystemDrive));
        Assert.True(PathSafety.IsWholeBlocked(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
        Assert.True(PathSafety.IsWholeBlocked(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)));
        // Ancestor of the Windows folder.
        Assert.True(PathSafety.IsWholeBlocked(SystemDrive));
    }

    [Fact]
    public void ThePrefixCheckDoesNotMatchSiblingFolders()
    {
        // "C:\Windows2" must not count as inside "C:\Windows".
        Assert.False(PathSafety.IsProtected(Windows + "2\\file.txt"));
    }

    [Fact]
    public void ProtectedNodeCannotBeTicked()
    {
        var node = new FileNode("Windows", Windows, true, 1000) { IsProtected = true };
        node.IsChecked = true;
        Assert.False(node.IsChecked);
    }

    [Fact]
    public void FormatSizeUsesBinaryUnits()
    {
        Assert.Equal("1 KB", Format.Size(1024));
        Assert.Equal("1.5 GB", Format.Size((long)(1.5 * 1024 * 1024 * 1024)));
    }
}

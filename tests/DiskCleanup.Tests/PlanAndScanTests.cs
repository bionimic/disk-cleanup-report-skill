using DiskCleanup.Models;
using DiskCleanup.Services;

namespace DiskCleanup.Tests;

public class PlanAndScanTests : IDisposable
{
    private readonly string _root;

    public PlanAndScanTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "DiskCleanupTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        // Only ever removes this test's own dummy folder, with a plain delete.
        try { Directory.Delete(_root, true); } catch { }
    }

    private static FileNode Folder(string path, long size) => new FileNode(Path.GetFileName(path), path, true, size);

    [Fact]
    public void TickingAParentTicksChildrenAndCountsOnce()
    {
        var parent = Folder(Path.Combine(_root, "a"), 300);
        var child1 = Folder(Path.Combine(_root, "a", "b"), 100);
        var child2 = Folder(Path.Combine(_root, "a", "c"), 200);
        parent.AddChild(child1);
        parent.AddChild(child2);

        parent.IsChecked = true;

        Assert.True(child1.IsChecked);
        Assert.True(child2.IsChecked);
        var plan = SelectionPlan.Build(new[] { parent }, validate: false);
        Assert.Single(plan);
        Assert.Equal(300, plan[0].Size);
    }

    [Fact]
    public void UntickingOneChildMakesTheParentPartial()
    {
        var parent = Folder(Path.Combine(_root, "a"), 300);
        var child1 = Folder(Path.Combine(_root, "a", "b"), 100);
        var child2 = Folder(Path.Combine(_root, "a", "c"), 200);
        parent.AddChild(child1);
        parent.AddChild(child2);
        parent.IsChecked = true;

        child1.IsChecked = false;

        Assert.Null(parent.IsChecked);
        var plan = SelectionPlan.Build(new[] { parent }, validate: false);
        Assert.Single(plan);
        Assert.Equal(child2.FullPath, plan[0].Path);
    }

    [Fact]
    public void FolderWithProtectedChildCannotBeRemovedWhole()
    {
        var parent = Folder(Path.Combine(_root, "proj"), 300);
        parent.WholeBlocked = true;
        var ok = Folder(Path.Combine(_root, "proj", "node_modules"), 250);
        var git = Folder(Path.Combine(_root, "proj", ".git"), 50);
        git.IsProtected = true;
        parent.AddChild(ok);
        parent.AddChild(git);

        parent.IsChecked = true;
        Assert.False(parent.IsChecked);
        Assert.False(ok.IsChecked);

        ok.IsChecked = true;
        git.IsChecked = true;

        Assert.Null(parent.IsChecked);
        Assert.False(git.IsChecked);
        var plan = SelectionPlan.Build(new[] { parent }, validate: false);
        Assert.Single(plan);
        Assert.Equal(ok.FullPath, plan[0].Path);
    }

    [Fact]
    public void OverlappingSelectionsFromTwoViewsAreDeduplicated()
    {
        var outer = Folder(Path.Combine(_root, "cache"), 500);
        outer.ContentsOnly = true;
        var inner = Folder(Path.Combine(_root, "cache", "sub"), 100);
        outer.IsChecked = true;
        inner.IsChecked = true;

        var plan = SelectionPlan.Build(new[] { inner, outer }, validate: false);

        Assert.Single(plan);
        Assert.Equal(outer.FullPath, plan[0].Path);
    }

    [Fact]
    public void ValidateRefusesProtectedAndMissingPaths()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var protectedNode = new FileNode("System32", Path.Combine(windows, "System32"), true, 1);
        var item = new PlanItem(protectedNode);
        Assert.StartsWith("Protected", SelectionPlan.Validate(item));

        var missing = new PlanItem(Folder(Path.Combine(_root, "nope"), 1));
        Assert.Equal("No longer exists.", SelectionPlan.Validate(missing));
    }

    [Fact]
    public void ValidateRefusesLongPaths()
    {
        var longPath = Path.Combine(_root, new string('x', 260));
        var item = new PlanItem(Folder(longPath, 1));
        Assert.Contains("too long", SelectionPlan.Validate(item));
    }

    [Fact]
    public async Task ScannerCountsHiddenFilesAndSkipsSmallOnes()
    {
        var big = Path.Combine(_root, "big");
        Directory.CreateDirectory(big);
        var bigFile = Path.Combine(big, "movie.bin");
        File.WriteAllBytes(bigFile, new byte[30 * 1024 * 1024]);
        File.SetAttributes(bigFile, FileAttributes.Hidden);
        File.WriteAllBytes(Path.Combine(_root, "small.txt"), new byte[1024]);

        var scanner = new DiskScanner(new Dictionary<string, KnownLocation>());
        var result = await scanner.ScanAsync(_root, CancellationToken.None);

        Assert.True(result.Root.Size >= 30L * 1024 * 1024);
        var bigNode = result.Root.Children.Single(c => c.Name == "big");
        Assert.True(bigNode.Size >= 30L * 1024 * 1024);
        Assert.Contains(bigNode.Children, f => f.Name == "movie.bin");
        Assert.DoesNotContain(result.Root.Children, f => f.Name == "small.txt");
        Assert.Contains(result.BiggestFiles, f => f.Name == "movie.bin");
    }

    [Fact]
    public async Task ScannerMarksAGitProjectAsNotRemovableWhole()
    {
        var proj = Path.Combine(_root, "proj");
        Directory.CreateDirectory(Path.Combine(proj, ".git"));
        File.WriteAllBytes(Path.Combine(proj, "data.bin"), new byte[30 * 1024 * 1024]);

        var scanner = new DiskScanner(new Dictionary<string, KnownLocation>());
        var result = await scanner.ScanAsync(_root, CancellationToken.None);

        var node = result.Root.Children.Single(c => c.Name == "proj");
        Assert.True(node.WholeBlocked);
        Assert.False(node.CanSelectWhole);
    }

    [Fact]
    public void RecycleSendsADummyFileToTheBinAndRemovesItFromDisk()
    {
        // A tiny dummy file with a recognisable name. It will sit in the Recycle Bin afterwards.
        var dummy = Path.Combine(_root, "DiskCleanup-test-dummy-" + Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllText(dummy, "dummy");
        Assert.True(File.Exists(dummy));

        var node = new FileNode(Path.GetFileName(dummy), dummy, false, 5) { IsChecked = false };
        node.IsChecked = true;
        var plan = SelectionPlan.Build(new[] { node });
        Assert.Single(plan);
        Assert.Null(plan[0].Problem);

        var drives = SelectionPlan.Preflight(plan);
        Assert.Single(drives);

        var results = RecycleService.Execute(plan, IntPtr.Zero, allowUnknownRoom: true, progress: null, token: CancellationToken.None);

        Assert.Single(results);
        Assert.Equal("Moved", results[0].Status);
        Assert.False(File.Exists(dummy));
    }
}

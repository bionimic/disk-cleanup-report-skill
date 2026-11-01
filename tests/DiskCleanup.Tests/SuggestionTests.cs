using DiskCleanup.Models;
using DiskCleanup.Services;

namespace DiskCleanup.Tests;

public class SuggestionTests
{
    private const long Mb = 1024 * 1024;

    [Theory]
    [InlineData("node_modules", true, 50 * Mb, Advice.Safe)]
    [InlineData("__pycache__", true, 20 * Mb, Advice.Safe)]
    [InlineData("Temp", true, 20 * Mb, Advice.Check)]
    [InlineData("crash.dmp", false, 200 * Mb, Advice.Safe)]
    [InlineData("setup.exe", false, 300 * Mb, Advice.Check)]
    [InlineData("game.iso", false, 4000 * Mb, Advice.Check)]
    public void CandidatesAreRecognised(string name, bool isDirectory, long size, Advice expected)
    {
        var hit = Suggestions.Classify(name, isDirectory, size);
        Assert.NotNull(hit);
        Assert.Equal(expected, hit!.Value.Advice);
    }

    [Theory]
    [InlineData("setup.exe", false, 50 * Mb)]       // too small to bother
    [InlineData("movie.mkv", false, 4000 * Mb)]      // personal media is never suggested
    [InlineData("Documents", true, 900 * Mb)]
    [InlineData("src", true, 50 * Mb)]
    public void OrdinaryItemsAreNotSuggested(string name, bool isDirectory, long size)
    {
        Assert.Null(Suggestions.Classify(name, isDirectory, size));
    }

    [Fact]
    public void ACandidateInsideACandidateIsOfferedOnce()
    {
        var root = new FileNode("D:\\", "D:\\", true, 1000 * Mb) { WholeBlocked = true };
        var project = new FileNode("app", "D:\\app", true, 900 * Mb);
        var modules = new FileNode("node_modules", "D:\\app\\node_modules", true, 800 * Mb);
        var nested = new FileNode("node_modules", "D:\\app\\node_modules\\x\\node_modules", true, 100 * Mb);
        root.AddChild(project);
        project.AddChild(modules);
        modules.AddChild(nested);

        var found = Suggestions.Apply(root);

        Assert.Single(found);
        Assert.Same(modules, found[0]);
        Assert.Equal(Advice.Safe, modules.Advice);
    }

    [Fact]
    public void ProtectedItemsAreNeverSuggested()
    {
        var root = new FileNode("C:\\", "C:\\", true, 1000 * Mb) { WholeBlocked = true };
        var temp = new FileNode("Temp", "C:\\Windows\\System32\\Temp", true, 100 * Mb) { IsProtected = true };
        root.AddChild(temp);

        Assert.Empty(Suggestions.Apply(root));
    }
}

using DiskCleanup.Models;

namespace DiskCleanup.Services;

internal sealed class KnownLocation
{
    public KnownLocation(string name, string path, Advice advice, string description, bool contentsOnly, bool needsAdmin = false)
    {
        Name = name;
        Path = path;
        Advice = advice;
        Description = description;
        ContentsOnly = contentsOnly;
        NeedsAdmin = needsAdmin;
    }

    public string Name { get; }
    public string Path { get; }
    public Advice Advice { get; }
    public string Description { get; }
    public bool ContentsOnly { get; }
    public bool NeedsAdmin { get; }
}

/// <summary>Where the usual space users live, ported from the skill's known-locations.md.</summary>
internal static class KnownLocations
{
    public static List<KnownLocation> Build()
    {
        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var list = new List<KnownLocation>();

        void Safe(string name, string path, string why, bool needsAdmin = false) =>
            list.Add(new KnownLocation(name, path, Advice.Safe, why, true, needsAdmin));
        void Check(string name, string path, string why) =>
            list.Add(new KnownLocation(name, path, Advice.Check, why, false));

        if (!string.IsNullOrEmpty(local))
        {
            Safe("Temporary files (your account)", Path.Combine(local, "Temp"), "Temporary files. Files in use stay behind.");
            Safe("NVIDIA shader cache (DX)", Path.Combine(local, "NVIDIA", "DXCache"), "Games rebuild it.");
            Safe("NVIDIA shader cache (GL)", Path.Combine(local, "NVIDIA", "GLCache"), "Games rebuild it.");
            Safe("DirectX shader cache", Path.Combine(local, "D3DSCache"), "Windows rebuilds it.");
            Safe("Crash dumps", Path.Combine(local, "CrashDumps"), "Old crash reports.");
            Safe("npm cache", Path.Combine(local, "npm-cache"), "Re-downloads when needed.");
            Safe("pip cache", Path.Combine(local, "pip", "cache"), "Re-downloads when needed.");
            Safe("Yarn cache", Path.Combine(local, "Yarn"), "Re-downloads when needed.");
            Safe("pnpm store", Path.Combine(local, "pnpm"), "Re-downloads when needed.");
            Safe("Composer cache", Path.Combine(local, "composer"), "Re-downloads when needed.");
            Safe("Dart pub cache", Path.Combine(local, "Pub", "Cache"), "Re-downloads when needed.");
            Check("WSL disks", Path.Combine(local, "wsl"), "WSL disks hold real data. Only remove a distro you no longer use.");
            Check("Docker disks", Path.Combine(local, "Docker", "wsl"), "Holds your images and volumes. Prefer docker system prune.");
        }

        if (!string.IsNullOrEmpty(windows))
        {
            Safe("Windows temp files", Path.Combine(windows, "Temp"), "Needs administrator. Files in use stay behind.", true);
            Safe("Windows Update downloads", Path.Combine(windows, "SoftwareDistribution", "Download"), "Needs administrator. Re-downloaded if needed.", true);
        }

        if (!string.IsNullOrEmpty(user))
        {
            Safe("Gradle cache", Path.Combine(user, ".gradle", "caches"), "Re-downloads on the next build.");
            Safe("NuGet packages", Path.Combine(user, ".nuget", "packages"), "Restored on the next build.");
            Check("Ollama models", Path.Combine(user, ".ollama", "models"), "Local AI models. Use ollama list, then ollama rm.");
            Check("Hugging Face cache", Path.Combine(user, ".cache", "huggingface"), "Downloaded AI models.");
            Check("Torch cache", Path.Combine(user, ".cache", "torch"), "Downloaded AI models.");
            Check("Android emulators", Path.Combine(user, ".android", "avd"), "Delete unused ones from Android Studio Device Manager.");
        }

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrEmpty(programFilesX86))
        {
            // Steam installs here by default; Program Files itself stays protected, so this is hint only.
            list.Add(new KnownLocation("Steam games", Path.Combine(programFilesX86, "Steam", "steamapps", "common"), Advice.Check,
                "Uninstall games from Steam instead.", false));
        }

        return list;
    }

    public static Dictionary<string, KnownLocation> AsLookup(IEnumerable<KnownLocation> locations)
    {
        var map = new Dictionary<string, KnownLocation>(StringComparer.OrdinalIgnoreCase);
        foreach (var k in locations)
        {
            map[PathSafety.Normalize(k.Path)] = k;
        }
        return map;
    }
}

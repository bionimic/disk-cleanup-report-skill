using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using DiskCleanup.Models;
using DiskCleanup.Services;

namespace DiskCleanup.ViewModels;

public sealed class ExtensionRow
{
    public string Extension { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public long Count { get; set; }
    public string SizeText => Format.Size(Bytes);
}

internal sealed class MainViewModel : ViewModelBase
{
    private readonly List<KnownLocation> _known = KnownLocations.Build();
    private readonly Dictionary<string, ScanResult> _scans = new Dictionary<string, ScanResult>(StringComparer.OrdinalIgnoreCase);
    private readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
    private CancellationTokenSource? _cts;
    private DiskScanner? _scanner;
    private bool _summaryQueued;
    private bool _isBusy;
    private string _status = "Pick a drive on the Overview page and press Scan. Scanning never changes a file.";
    private string _selectionText = "Nothing selected";
    private bool _hasSelection;
    private int _page;

    public MainViewModel()
    {
        IsAdmin = Elevation.IsAdministrator();
        ScanCommand = new RelayCommand(p => _ = ScanAsync(p as DriveViewModel), _ => !IsBusy);
        CancelCommand = new RelayCommand(_ => _cts?.Cancel(), _ => IsBusy);
        ReviewCommand = new RelayCommand(_ => RequestReview(), _ => HasSelection && !IsBusy);
        ClearCommand = new RelayCommand(_ => ClearSelection(), _ => HasSelection);
        SelectSafeCommand = new RelayCommand(_ => SelectAllSafe());
        AdminCommand = new RelayCommand(_ => RestartAsAdmin(), _ => !IsAdmin);
        EmptyBinCommand = new RelayCommand(p => EmptyBin(p as DriveViewModel), _ => !IsBusy);
        RefreshQuickCommand = new RelayCommand(_ => _ = LoadQuickAsync(), _ => !IsBusy);

        FileNode.SelectionChanged += QueueSummary;
        _timer.Tick += (_, _) => UpdateProgress();
        LoadDrives();
    }

    public bool IsAdmin { get; }
    public string AdminText => IsAdmin ? "Running as administrator" : "Standard mode";

    public ObservableCollection<DriveViewModel> Drives { get; } = new ObservableCollection<DriveViewModel>();
    public ObservableCollection<FileNode> TreeRoots { get; } = new ObservableCollection<FileNode>();
    public ObservableCollection<FileNode> QuickRoots { get; } = new ObservableCollection<FileNode>();
    public ObservableCollection<FileNode> BiggestFiles { get; } = new ObservableCollection<FileNode>();
    public ObservableCollection<ExtensionRow> Extensions { get; } = new ObservableCollection<ExtensionRow>();

    public ICommand ScanCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ReviewCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SelectSafeCommand { get; }
    public ICommand AdminCommand { get; }
    public ICommand EmptyBinCommand { get; }
    public ICommand RefreshQuickCommand { get; }

    /// <summary>Raised with the validated plan. The window shows the review and returns the results.</summary>
    public Func<List<PlanItem>, List<RecycleResult>?>? ReviewHandler { get; set; }

    public Func<string, bool>? Confirm { get; set; }
    public Func<IntPtr>? WindowHandle { get; set; }

    public int Page
    {
        get => _page;
        set => Set(ref _page, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (Set(ref _isBusy, value))
            {
                Raise(nameof(IsNotBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

    public string Status
    {
        get => _status;
        private set => Set(ref _status, value);
    }

    public string SelectionText
    {
        get => _selectionText;
        private set => Set(ref _selectionText, value);
    }

    public bool HasSelection
    {
        get => _hasSelection;
        private set
        {
            if (Set(ref _hasSelection, value)) CommandManager.InvalidateRequerySuggested();
        }
    }

    // ---- drives -------------------------------------------------------

    private void LoadDrives()
    {
        Drives.Clear();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                // Fixed drives only: removable and network drives have no Recycle Bin.
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                var vm = new DriveViewModel(d.RootDirectory.FullName, d.VolumeLabel, d.TotalSize, d.AvailableFreeSpace);
                Drives.Add(vm);
            }
            catch
            {
                // Skip drives that cannot be read.
            }
        }
        _ = RefreshBinSizesAsync();
    }

    private async Task RefreshBinSizesAsync()
    {
        foreach (var d in Drives.ToList())
        {
            var size = await Task.Run(() => BinService.GetBinUsed(d.Root));
            d.BinSize = size;
        }
    }

    private void RefreshFreeSpace()
    {
        foreach (var d in Drives)
        {
            try
            {
                d.Free = new DriveInfo(d.Root).AvailableFreeSpace;
            }
            catch
            {
                // Ignore.
            }
        }
        _ = RefreshBinSizesAsync();
    }

    // ---- scanning ------------------------------------------------------

    private async Task ScanAsync(DriveViewModel? drive)
    {
        if (drive == null || IsBusy) return;
        IsBusy = true;
        _cts = new CancellationTokenSource();
        _scanner = new DiskScanner(KnownLocations.AsLookup(_known));
        drive.ScanState = "Scanning...";
        Status = "Scanning " + drive.Root + " ...";
        _timer.Start();
        try
        {
            var result = await _scanner.ScanAsync(drive.Root, _cts.Token);

            var old = TreeRoots.FirstOrDefault(r => r.FullPath.Equals(result.Root.FullPath, StringComparison.OrdinalIgnoreCase));
            if (old != null) TreeRoots.Remove(old);
            TreeRoots.Add(result.Root);
            _scans[drive.Root] = result;
            RebuildScanViews();

            var note = result.UnreadableFolders > 0 && !IsAdmin
                ? " " + result.UnreadableFolders + " folders could not be read. Run as administrator to include them."
                : string.Empty;
            drive.ScanState = "Scanned: " + result.FilesSeen.ToString("N0") + " files, " + Format.Size(result.BytesSeen) + "." + note;
            Status = "Finished " + drive.Root + ". Go to Browse, Biggest files or Quick clean and tick what you want to remove." + note;
            Page = 2;
        }
        catch (OperationCanceledException)
        {
            drive.ScanState = "Scan cancelled";
            Status = "Scan cancelled.";
        }
        catch (Exception ex)
        {
            drive.ScanState = "Scan failed";
            Status = "Scan failed: " + ex.Message;
        }
        finally
        {
            _timer.Stop();
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            QueueSummary();
        }
    }

    private void UpdateProgress()
    {
        if (_scanner == null) return;
        var s = _scanner.Snapshot();
        Status = "Scanning: " + s.Files.ToString("N0") + " files, " + Format.Size(s.Bytes) + "   " + Shorten(s.Current, 70);
    }

    private static string Shorten(string text, int max) =>
        text.Length <= max ? text : "..." + text.Substring(text.Length - max + 3);

    private void RebuildScanViews()
    {
        BiggestFiles.Clear();
        foreach (var f in _scans.Values.SelectMany(s => s.BiggestFiles).OrderByDescending(f => f.Size).Take(300))
            BiggestFiles.Add(f);

        Extensions.Clear();
        var merged = _scans.Values.SelectMany(s => s.Extensions)
            .GroupBy(e => e.Extension, StringComparer.OrdinalIgnoreCase)
            .Select(g => new ExtensionRow { Extension = g.Key, Bytes = g.Sum(e => e.Bytes), Count = g.Sum(e => e.Count) })
            .OrderByDescending(e => e.Bytes)
            .Take(60);
        foreach (var e in merged) Extensions.Add(e);
    }

    // ---- quick clean ---------------------------------------------------

    public async Task LoadQuickAsync()
    {
        // Measuring never locks the rest of the app, and no single folder can hold it up.
        if (_measuring) return;
        _measuring = true;
        if (!IsBusy) Status = "Measuring the usual space users...";
        var slow = new List<string>();
        try
        {
            var measured = await Task.WhenAll(_known.Select(MeasureAsync));
            var found = new List<(KnownLocation K, long Size)>();
            foreach (var m in measured)
            {
                if (m.TimedOut) slow.Add(m.K.Name);
                else if (m.Size > 0) found.Add((m.K, m.Size));
            }

            var downloads = await WithTimeout(Task.Run(BuildDownloadsGroup), null);

            var safe = NewHeader("Safe to clear (rebuilds itself)");
            var check = NewHeader("Your call, check first");
            foreach (var (k, size) in found.OrderByDescending(f => f.Size))
            {
                var node = new FileNode(k.Name, PathSafety.Normalize(k.Path), true, size)
                {
                    ContentsOnly = k.ContentsOnly,
                    Advice = k.Advice,
                    Hint = k.Description + (k.NeedsAdmin && !IsAdmin ? " Run as administrator to clear this fully." : string.Empty),
                    IsProtected = PathSafety.IsProtected(k.Path)
                };
                if (node.IsProtected) node.Hint = k.Description;
                (k.Advice == Advice.Safe ? safe : check).AddChild(node);
            }
            if (downloads != null) check.AddChild(downloads);

            QuickRoots.Clear();
            foreach (var header in new[] { safe, check })
            {
                header.Size = header.Children.Sum(c => c.Size);
                if (header.Children.Count > 0) QuickRoots.Add(header);
            }
            if (!IsBusy)
            {
                var note = slow.Count > 0 ? " Skipped (too slow to read): " + string.Join(", ", slow) + "." : string.Empty;
                Status = (QuickRoots.Count == 0 ? "Nothing big found in the usual places." : "Tick what you want to clear, then press Review.") + note;
            }
        }
        finally
        {
            _measuring = false;
            QueueSummary();
        }
    }

    private const int MeasureTimeoutMs = 20000;
    private bool _measuring;

    private async Task<(KnownLocation K, long Size, bool TimedOut)> MeasureAsync(KnownLocation k)
    {
        var work = Task.Run(() => Directory.Exists(k.Path) ? FileSizing.GetSize(k.Path) : 0L);
        var done = await Task.WhenAny(work, Task.Delay(MeasureTimeoutMs));
        // A blocked read cannot be cancelled, so it is abandoned and the app carries on.
        return done == work ? (k, work.Result, false) : (k, 0L, true);
    }

    private static async Task<T> WithTimeout<T>(Task<T> work, T fallback)
    {
        var done = await Task.WhenAny(work, Task.Delay(MeasureTimeoutMs));
        return done == work ? work.Result : fallback;
    }

    private static FileNode NewHeader(string title) =>
        new FileNode(title, string.Empty, true, 0) { WholeBlocked = true };

    private FileNode? BuildDownloadsGroup()
    {
        var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        if (!Directory.Exists(downloads)) return null;
        var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".exe", ".msi", ".zip" };
        var group = new FileNode("Installers and archives in Downloads", PathSafety.Normalize(downloads), true, 0)
        {
            WholeBlocked = true,
            Advice = Advice.Check,
            Hint = "Remove the ones you already installed. Pick them one by one."
        };
        try
        {
            foreach (var file in new DirectoryInfo(downloads).EnumerateFiles())
            {
                if (!exts.Contains(file.Extension)) continue;
                if ((file.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                group.AddChild(new FileNode(file.Name, file.FullName, false, file.Length) { Advice = Advice.Check });
            }
        }
        catch
        {
            return null;
        }
        if (group.Children.Count == 0) return null;
        var sorted = group.Children.OrderByDescending(c => c.Size).ToList();
        group.Children.Clear();
        foreach (var c in sorted) group.Children.Add(c);
        group.Size = group.Children.Sum(c => c.Size);
        return group;
    }

    private void SelectAllSafe()
    {
        foreach (var header in QuickRoots)
        {
            foreach (var node in header.Children)
            {
                if (node.Advice == Advice.Safe && node.CanSelectWhole) node.IsChecked = true;
            }
        }
    }

    // ---- selection -----------------------------------------------------

    private IEnumerable<FileNode> AllRoots() => QuickRoots.Concat(TreeRoots);

    private void QueueSummary()
    {
        if (_summaryQueued) return;
        _summaryQueued = true;
        Application.Current.Dispatcher.BeginInvoke(new Action(() =>
        {
            _summaryQueued = false;
            var plan = SelectionPlan.Build(AllRoots(), validate: false);
            HasSelection = plan.Count > 0;
            SelectionText = plan.Count == 0
                ? "Nothing selected"
                : plan.Count + (plan.Count == 1 ? " item" : " items") + " selected, " + Format.Size(plan.Sum(p => p.Size));
        }), DispatcherPriority.Background);
    }

    private void ClearSelection()
    {
        foreach (var r in AllRoots()) r.ClearSelection();
        foreach (var f in BiggestFiles) f.IsChecked = false;
        QueueSummary();
    }

    // ---- review and cleanup -------------------------------------------

    private void RequestReview()
    {
        var plan = SelectionPlan.Build(AllRoots());
        if (plan.Count == 0 || ReviewHandler == null) return;

        var results = ReviewHandler(plan);
        if (results == null) return;
        ApplyResults(results);
    }

    private void ApplyResults(List<RecycleResult> results)
    {
        var moved = results.Sum(r => r.MovedBytes);
        foreach (var r in results.Where(r => r.MovedBytes > 0 || r.FullyMoved))
        {
            var contentsOnly = r.Node?.ContentsOnly ?? false;
            RemovePath(r.Path, contentsOnly, r.MovedBytes);
        }
        foreach (var f in BiggestFiles.Where(f => !File.Exists(f.FullPath)).ToList()) BiggestFiles.Remove(f);

        ClearSelection();
        RefreshFreeSpace();
        Status = Format.Size(moved) + " moved to the Recycle Bin. Rescan a drive to refresh its numbers exactly.";
    }

    private void RemovePath(string path, bool contentsOnly, long movedBytes)
    {
        var norm = PathSafety.Normalize(path);
        var all = AllRoots().SelectMany(r => new[] { r }.Concat(r.Descendants())).ToList();

        foreach (var node in all)
        {
            if (string.IsNullOrEmpty(node.FullPath)) continue;
            var np = PathSafety.Normalize(node.FullPath);
            if (!np.Equals(norm, StringComparison.OrdinalIgnoreCase)) continue;

            if (contentsOnly)
            {
                Adjust(node, movedBytes, includeSelf: true);
                if (node.Children.Count > 0) node.Children.Clear();
            }
            else if (node.Parent != null)
            {
                Adjust(node, node.Size, includeSelf: false);
                node.Parent.Children.Remove(node);
            }
        }
    }

    private static void Adjust(FileNode node, long delta, bool includeSelf)
    {
        var cur = includeSelf ? node : node.Parent;
        while (cur != null)
        {
            cur.Size = Math.Max(0, cur.Size - delta);
            cur = cur.Parent;
        }
    }

    // ---- admin and bin -------------------------------------------------

    private void RestartAsAdmin()
    {
        if (Confirm != null && !Confirm("Restart Disk Cleanup as administrator?\n\nThis closes the app, asks Windows for permission, and opens it again. Any scan results will be lost.")) return;
        if (Elevation.RestartAsAdministrator()) Application.Current.Shutdown();
        else Status = "Administrator mode was cancelled.";
    }

    private void EmptyBin(DriveViewModel? drive)
    {
        if (drive == null) return;
        var size = BinService.GetBinUsed(drive.Root);
        var text = "Empty the Recycle Bin on " + drive.Root.TrimEnd('\\') + "?\n\n" +
                   "It holds " + Format.Size(size) + ". That includes items that were there before this session, " +
                   "not only what you removed today.\n\nThis cannot be undone.";
        if (Confirm == null || !Confirm(text)) return;
        BinService.EmptyDrive(WindowHandle?.Invoke() ?? IntPtr.Zero, drive.Root);
        RefreshFreeSpace();
    }
}

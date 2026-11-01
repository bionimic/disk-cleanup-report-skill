using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Interop;
using DiskCleanup.Models;
using DiskCleanup.Services;
using DiskCleanup.ViewModels;

namespace DiskCleanup.Views;

internal sealed class ReviewRow
{
    public string Path { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string Note { get; set; } = string.Empty;
}

internal sealed class ResultRow
{
    public string Path { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string MovedText { get; set; } = string.Empty;
    public string LeftText { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

internal sealed class BinRow : ViewModelBase
{
    private string _text = string.Empty;
    private bool _canEmpty = true;
    private string _buttonText = string.Empty;

    public string Drive { get; set; } = string.Empty;

    public string Text
    {
        get => _text;
        set => Set(ref _text, value);
    }

    public bool CanEmpty
    {
        get => _canEmpty;
        set => Set(ref _canEmpty, value);
    }

    public string ButtonText
    {
        get => _buttonText;
        set => Set(ref _buttonText, value);
    }
}

internal partial class ReviewWindow : Window
{
    private readonly List<PlanItem> _plan;
    private readonly List<DrivePlan> _drives;
    private readonly Dictionary<string, long> _freeBefore = new Dictionary<string, long>();
    private readonly ObservableCollection<BinRow> _binRows = new ObservableCollection<BinRow>();
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private bool _working;
    private bool _needsUnknownConsent;

    public ReviewWindow(List<PlanItem> plan, bool isAdmin)
    {
        InitializeComponent();
        _plan = plan;
        _drives = SelectionPlan.Preflight(plan);
        Title = "Review what will move";
        BuildReview(isAdmin);
    }

    /// <summary>What happened, or null if the user cancelled before anything moved.</summary>
    public List<RecycleResult>? Results { get; private set; }

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void BuildReview(bool isAdmin)
    {
        var movable = _plan.Where(p => p.Problem == null).ToList();
        var skipped = _plan.Where(p => p.Problem != null).ToList();

        SummaryText.Text = movable.Count + (movable.Count == 1 ? " item" : " items") + ", " +
                           Format.Size(movable.Sum(p => p.Size)) + " ready to move";

        ReviewGrid.ItemsSource = _plan.Select(p => new ReviewRow
        {
            Path = p.Path,
            Type = p.Description,
            SizeText = Format.Size(p.Size),
            Note = p.Problem != null ? "Will be skipped: " + p.Problem
                 : p.Advice == Advice.Check ? "Check first: make sure you no longer need this"
                 : p.ContentsOnly ? "The folder stays, its contents move"
                 : string.Empty
        }).ToList();

        var warnings = new List<string>();
        if (skipped.Count > 0)
            warnings.Add(skipped.Count + " selected item(s) cannot be moved and will be skipped. See the Note column.");
        if (movable.Any(p => p.Advice == Advice.Check))
            warnings.Add("Some items are marked Check first. Only you know whether you still need them.");
        if (movable.Any(p => PathSafety.IsUnderOneDrive(p.Path)))
            warnings.Add("Some items are in a OneDrive folder. Removing them here also removes them from OneDrive online.");
        warnings.Add("Close apps that use these files first (games, torrent clients, Docker). A torrent may download again if its folder is gone: remove it from the torrent client first.");
        if (!isAdmin && movable.Any(p => p.Path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)))
            warnings.Add("Windows folders may only partly clear without administrator mode.");
        WarningList.ItemsSource = warnings;

        var lines = new List<string>();
        foreach (var d in _drives)
        {
            try { _freeBefore[d.Drive] = new DriveInfo(d.Drive).AvailableFreeSpace; } catch { }

            var drive = d.Drive.TrimEnd('\\');
            switch (d.Bin.Kind)
            {
                case BinRoomKind.Known:
                    lines.Add(drive + " Recycle Bin: room for " + Format.Size(d.Bin.Room) + ", this move needs " + Format.Size(d.Need) + ".");
                    break;
                case BinRoomKind.Unknown:
                    lines.Add(drive + " Recycle Bin: size limit could not be read.");
                    _needsUnknownConsent = true;
                    break;
                default:
                    lines.Add(drive + " Recycle Bin: set to delete immediately. Items on this drive will be skipped.");
                    break;
            }
            if (d.Block != null && d.Bin.Kind == BinRoomKind.Known) lines.Add(d.Block);
        }
        BinInfoText.Text = string.Join("\n", lines);
        UnknownRoomBox.Visibility = _needsUnknownConsent ? Visibility.Visible : Visibility.Collapsed;
        UpdateMoveButton();
    }

    private void UpdateMoveButton()
    {
        var any = _plan.Any(p => p.Problem == null);
        MoveButton.IsEnabled = any && (!_needsUnknownConsent || UnknownRoomBox.IsChecked == true);
    }

    private void OnOptionChanged(object sender, RoutedEventArgs e) => UpdateMoveButton();

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private async void OnMove(object sender, RoutedEventArgs e)
    {
        ReviewPanel.Visibility = Visibility.Collapsed;
        WorkPanel.Visibility = Visibility.Visible;
        _working = true;

        var hwnd = Handle;
        var allowUnknown = UnknownRoomBox.IsChecked == true;
        var progress = new Progress<string>(p => WorkText.Text = p);
        List<RecycleResult> results;
        try
        {
            results = await Task.Run(() => RecycleService.Execute(_plan, hwnd, allowUnknown, progress, _cts.Token));
        }
        catch (OperationCanceledException)
        {
            results = new List<RecycleResult>();
        }
        _working = false;
        Results = results;
        ShowResults(results);
    }

    private void ShowResults(List<RecycleResult> results)
    {
        WorkPanel.Visibility = Visibility.Collapsed;
        ResultPanel.Visibility = Visibility.Visible;
        Title = "Done";

        var moved = results.Sum(r => r.MovedBytes);
        var left = results.Sum(r => r.LeftBytes);
        ResultTitle.Text = Format.Size(moved) + " moved to the Recycle Bin";
        ResultNote.Text = left > 0
            ? Format.Size(left) + " was left behind because it was in use or could not be moved. Close the app using it and try again."
            : "Nothing was left behind.";

        ResultGrid.ItemsSource = results.Select(r => new ResultRow
        {
            Path = r.Path,
            Status = r.Status,
            MovedText = Format.Size(r.MovedBytes),
            LeftText = Format.Size(r.LeftBytes),
            Message = r.Message
        }).ToList();

        foreach (var d in _drives)
        {
            _binRows.Add(new BinRow { Drive = d.Drive, ButtonText = "Empty " + d.Drive.TrimEnd('\\') + "..." });
            RefreshBinRow(_binRows.Last());
        }
        BinList.ItemsSource = _binRows;
    }

    private void RefreshBinRow(BinRow row)
    {
        var size = BinService.GetBinUsed(row.Drive);
        var free = string.Empty;
        try
        {
            var now = new DriveInfo(row.Drive).AvailableFreeSpace;
            free = _freeBefore.TryGetValue(row.Drive, out var before)
                ? "   Free space: " + Format.Size(before) + " to " + Format.Size(now)
                : "   Free space: " + Format.Size(now);
        }
        catch
        {
            // Ignore.
        }
        row.Text = row.Drive.TrimEnd('\\') + "  Recycle Bin holds " + Format.Size(size) + free;
        row.CanEmpty = size > 0;
    }

    private void OnEmptyBin(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement el || el.Tag is not string drive) return;
        var size = BinService.GetBinUsed(drive);
        var text = "Empty the Recycle Bin on " + drive.TrimEnd('\\') + "?\n\n" +
                   "It holds " + Format.Size(size) + " in total. That includes items that were there before this cleanup, " +
                   "not only what you just moved.\n\nThis cannot be undone.";
        if (MessageBox.Show(this, text, "Empty Recycle Bin", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        BinService.EmptyDrive(Handle, drive);
        var row = _binRows.FirstOrDefault(r => r.Drive == drive);
        if (row != null) RefreshBinRow(row);
    }

    private void OnDone(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_working)
        {
            // Do not abandon a move halfway through.
            e.Cancel = true;
            return;
        }
        base.OnClosing(e);
    }
}

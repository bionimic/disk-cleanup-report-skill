using DiskCleanup.Services;

namespace DiskCleanup.ViewModels;

public sealed class DriveViewModel : ViewModelBase
{
    private long _free;
    private long _binSize;
    private string _scanState = "Not scanned yet";

    public DriveViewModel(string root, string label, long total, long free)
    {
        Root = root;
        Label = label;
        Total = total;
        _free = free;
    }

    public string Root { get; }
    public string Label { get; }
    public long Total { get; }

    public long Free
    {
        get => _free;
        set
        {
            if (Set(ref _free, value))
            {
                Raise(nameof(UsedPercent));
                Raise(nameof(FreeText));
                Raise(nameof(UsedText));
            }
        }
    }

    public long BinSize
    {
        get => _binSize;
        set
        {
            if (Set(ref _binSize, value)) Raise(nameof(BinText));
        }
    }

    public string ScanState
    {
        get => _scanState;
        set => Set(ref _scanState, value);
    }

    public double UsedPercent => Total <= 0 ? 0 : 100.0 * (Total - Free) / Total;
    public string Title => Root.TrimEnd('\\') + (string.IsNullOrWhiteSpace(Label) ? string.Empty : "  " + Label);
    public string UsedText => Format.Size(Total - Free) + " used of " + Format.Size(Total);
    public string FreeText => Format.Size(Free) + " free";
    public string BinText => "Recycle Bin: " + Format.Size(BinSize);
}

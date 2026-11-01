using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DiskCleanup.Models;

public enum Advice
{
    None,
    Safe,
    Check
}

/// <summary>
/// One folder or file in the tree. The same instance is shown in the tree, the
/// biggest-files list and the quick clean list, so ticking it in one place
/// ticks it everywhere.
/// </summary>
public sealed class FileNode : INotifyPropertyChanged
{
    private long _size;
    private bool? _isChecked = false;
    private bool _suppress;

    public FileNode(string name, string fullPath, bool isDirectory, long size)
    {
        Name = name;
        FullPath = fullPath;
        IsDirectory = isDirectory;
        _size = size;
    }

    public string Name { get; }
    public string FullPath { get; }
    public bool IsDirectory { get; }
    public FileNode? Parent { get; set; }
    public ObservableCollection<FileNode> Children { get; } = new ObservableCollection<FileNode>();

    /// <summary>Cannot be selected at all (system file, system folder, project data).</summary>
    public bool IsProtected { get; set; }

    /// <summary>The folder as a whole cannot go, but items inside it can.</summary>
    public bool WholeBlocked { get; set; }

    /// <summary>Move what is inside the folder, keep the folder itself.</summary>
    public bool ContentsOnly { get; set; }

    public Advice Advice { get; set; }
    public string Hint { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;

    /// <summary>Drive letter shown next to the name, set only where the list mixes drives.</summary>
    public string DriveBadge { get; set; } = string.Empty;

    public string Drive => string.IsNullOrEmpty(FullPath) ? string.Empty : (System.IO.Path.GetPathRoot(FullPath) ?? string.Empty).TrimEnd((char)92);

    public bool CanSelect => !IsProtected && !WholeBlocked;
    public bool CanSelectWhole => !IsProtected && !WholeBlocked;

    public long Size
    {
        get => _size;
        set
        {
            if (_size == value) return;
            _size = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParentPercent));
            OnPropertyChanged(nameof(SizeText));
        }
    }

    public double ParentPercent
    {
        get
        {
            if (Parent == null || Parent.Size <= 0) return 100;
            return Math.Min(100, 100.0 * Size / Parent.Size);
        }
    }

    public string Kind => IsDirectory ? "Folder" : "File";

    public string SizeText => DiskCleanup.Services.Format.Size(Size);

    public string Folder => string.IsNullOrEmpty(FullPath) ? string.Empty : System.IO.Path.GetDirectoryName(FullPath) ?? string.Empty;

    public string AdviceText => Advice == Advice.Safe ? "Safe to clear" : Advice == Advice.Check ? "Check first" : string.Empty;

    /// <summary>Raised for every change, anywhere. Listeners should coalesce.</summary>
    public static event Action? SelectionChanged;

    /// <summary>true = whole item, null = some children, false = none.</summary>
    public bool? IsChecked
    {
        get => _isChecked;
        set => SetChecked(value, true);
    }

    private void SetChecked(bool? value, bool propagate)
    {
        if (!CanSelect && value == true) value = false;
        if (_isChecked == value) return;
        _isChecked = value;
        OnPropertyChanged(nameof(IsChecked));

        if (propagate && value.HasValue)
        {
            _suppress = true;
            foreach (var child in Children)
            {
                if (child.CanSelect) child.SetChecked(value, true);
            }
            _suppress = false;
            if (!CanSelectWhole) RefreshFromChildren();
        }

        Parent?.RefreshFromChildren();
        SelectionChanged?.Invoke();
    }

    internal void RefreshFromChildren()
    {
        if (_suppress) return;
        var selectable = Children.Where(c => !c.IsProtected).ToList();
        if (selectable.Count == 0) return;

        bool? state;
        if (selectable.All(c => c._isChecked == true)) state = true;
        else if (selectable.All(c => c._isChecked == false)) state = false;
        else state = null;

        if (state == true && !CanSelectWhole) state = null;
        if (_isChecked == state) return;
        _isChecked = state;
        OnPropertyChanged(nameof(IsChecked));
        Parent?.RefreshFromChildren();
    }

    public void ClearSelection()
    {
        _suppress = true;
        foreach (var child in Children) child.ClearSelection();
        _suppress = false;
        if (_isChecked != false)
        {
            _isChecked = false;
            OnPropertyChanged(nameof(IsChecked));
        }
    }

    public IEnumerable<FileNode> Descendants()
    {
        foreach (var child in Children)
        {
            yield return child;
            foreach (var d in child.Descendants()) yield return d;
        }
    }

    public void AddChild(FileNode child)
    {
        child.Parent = this;
        Children.Add(child);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

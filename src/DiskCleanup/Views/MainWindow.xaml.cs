using System.Windows;
using System.Windows.Interop;
using DiskCleanup.Services;
using DiskCleanup.ViewModels;

namespace DiskCleanup.Views;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm = new MainViewModel();

    public MainWindow()
    {
        InitializeComponent();
        DataContext = _vm;

        if (_vm.IsAdmin) Title = "Disk Cleanup (Administrator)";

        _vm.WindowHandle = () => new WindowInteropHelper(this).Handle;
        _vm.Confirm = text =>
            MessageBox.Show(this, text, "Disk Cleanup", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        _vm.ReviewHandler = plan =>
        {
            var review = new ReviewWindow(plan, _vm.IsAdmin) { Owner = this };
            review.ShowDialog();
            return review.Results;
        };

        Loaded += async (_, _) => await _vm.LoadQuickAsync();
    }
}

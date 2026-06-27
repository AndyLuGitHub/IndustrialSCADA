using System.Windows.Controls;
using IndustrialSCADA.Module.LogViewer.ViewModels;

namespace IndustrialSCADA.Module.LogViewer.Views;

/// <summary>
/// Code-behind for the Log Viewer view.
/// Uses constructor injection for the ViewModel.
/// </summary>
public partial class LogViewerView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="LogViewerView"/> class.
    /// </summary>
    /// <param name="viewModel">The log viewer view model, resolved from the DI container.</param>
    public LogViewerView(LogViewerViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

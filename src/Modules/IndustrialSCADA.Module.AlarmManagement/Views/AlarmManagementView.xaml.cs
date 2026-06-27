using System.Windows.Controls;
using IndustrialSCADA.Module.AlarmManagement.ViewModels;

namespace IndustrialSCADA.Module.AlarmManagement.Views;

/// <summary>
/// Code-behind for the Alarm Management view.
/// Uses constructor injection for the ViewModel.
/// </summary>
public partial class AlarmManagementView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AlarmManagementView"/> class.
    /// </summary>
    /// <param name="viewModel">The alarm management view model, resolved from the DI container.</param>
    public AlarmManagementView(AlarmManagementViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

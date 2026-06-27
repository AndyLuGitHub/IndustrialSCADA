using System.Windows.Controls;
using IndustrialSCADA.Module.DeviceControl.ViewModels;

namespace IndustrialSCADA.Module.DeviceControl.Views;

/// <summary>
/// Code-behind for the Device Control view.
/// Uses constructor injection for the ViewModel.
/// </summary>
public partial class DeviceControlView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceControlView"/> class.
    /// </summary>
    /// <param name="viewModel">The device control view model, resolved from the DI container.</param>
    public DeviceControlView(DeviceControlViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

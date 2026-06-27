using System.Windows.Controls;
using IndustrialSCADA.Module.DataAcquisition.ViewModels;

namespace IndustrialSCADA.Module.DataAcquisition.Views;

/// <summary>
/// Code-behind for the Data Acquisition view.
/// Uses constructor injection for the ViewModel.
/// </summary>
public partial class DataAcquisitionView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataAcquisitionView"/> class.
    /// </summary>
    /// <param name="viewModel">The data acquisition view model, resolved from the DI container.</param>
    public DataAcquisitionView(DataAcquisitionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}

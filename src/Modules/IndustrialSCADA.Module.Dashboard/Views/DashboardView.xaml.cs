using System;
using System.Windows.Controls;
using IndustrialSCADA.Module.Dashboard.ViewModels;

namespace IndustrialSCADA.Module.Dashboard.Views;

/// <summary>
/// Code-behind for the Dashboard view.
/// Manually wires the ViewModel via constructor injection (bypasses Prism ViewModelLocator).
/// </summary>
public partial class DashboardView : UserControl
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardView"/> class.
    /// </summary>
    /// <param name="viewModel">The dashboard view model, resolved from the DI container.</param>
    public DashboardView(DashboardViewModel viewModel)
    {
        System.Diagnostics.Debug.WriteLine($"[DashboardView] Constructor BEGIN, vm={viewModel?.GetType().Name ?? "NULL"}");
        try
        {
            InitializeComponent();
            DataContext = viewModel;
            System.Diagnostics.Debug.WriteLine("[DashboardView] Constructor OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[DashboardView] Constructor EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[DashboardView] InnerException: {ex.InnerException?.GetType().Name}: {ex.InnerException?.Message}");
            throw;
        }
    }
}

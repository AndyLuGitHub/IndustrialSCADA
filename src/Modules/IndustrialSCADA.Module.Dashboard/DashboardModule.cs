using IndustrialSCADA.Module.Dashboard.Views;
using IndustrialSCADA.Module.Dashboard.ViewModels;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Navigation.Regions;
using System.Diagnostics;

namespace IndustrialSCADA.Module.Dashboard;

/// <summary>
/// Dashboard Prism module. Registers the DashboardView for region navigation
/// and navigates to it as the default view on initialization.
/// </summary>
public class DashboardModule : IModule
{
    /// <inheritdoc />
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        // Explicitly register ViewModel as singleton so DryIoc can resolve it for constructor injection
        containerRegistry.RegisterSingleton<DashboardViewModel>();

        // Register the view for region navigation (Prism 9: name defaults to typeof(TView).Name)
        containerRegistry.RegisterForNavigation<DashboardView>();
    }

    /// <inheritdoc />
    public void OnInitialized(IContainerProvider containerProvider)
    {
        // Default navigation to DashboardView is triggered from MainWindow.Loaded
        // (in IndustrialSCADA.App), which guarantees MainRegion is registered.
    }
}

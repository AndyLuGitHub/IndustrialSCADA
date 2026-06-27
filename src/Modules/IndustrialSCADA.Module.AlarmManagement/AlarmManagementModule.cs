using IndustrialSCADA.Module.AlarmManagement.Views;
using IndustrialSCADA.Module.AlarmManagement.ViewModels;
using Prism.Ioc;
using Prism.Modularity;

namespace IndustrialSCADA.Module.AlarmManagement;

/// <summary>
/// Alarm Management Prism module. Registers the AlarmManagementView for region navigation.
/// Navigation is triggered from MainWindow.Loaded, not from OnInitialized.
/// </summary>
public class AlarmManagementModule : IModule
{
    /// <inheritdoc />
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<AlarmManagementViewModel>();
        containerRegistry.RegisterForNavigation<AlarmManagementView>();
    }

    /// <inheritdoc />
    public void OnInitialized(IContainerProvider containerProvider)
    {
        // No automatic navigation; user selects this view from the navigation tree.
    }
}

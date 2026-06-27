using System.Diagnostics;
using IndustrialSCADA.Module.DeviceControl.Views;
using IndustrialSCADA.Module.DeviceControl.ViewModels;
using Prism.Ioc;
using Prism.Modularity;
using Prism.Navigation.Regions;

namespace IndustrialSCADA.Module.DeviceControl;

/// <summary>
/// Device Control Prism module. Registers the DeviceControlView for region navigation.
/// </summary>
public class DeviceControlModule : IModule
{
    /// <inheritdoc />
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<DeviceControlViewModel>();
        containerRegistry.RegisterForNavigation<DeviceControlView>();
    }

    /// <inheritdoc />
    public void OnInitialized(IContainerProvider containerProvider)
    {
        // No automatic navigation; user selects this view from the navigation tree.
    }
}

using IndustrialSCADA.Module.DataAcquisition.Views;
using IndustrialSCADA.Module.DataAcquisition.ViewModels;
using Prism.Ioc;
using Prism.Modularity;

namespace IndustrialSCADA.Module.DataAcquisition;

/// <summary>
/// Data Acquisition Prism module. Registers the DataAcquisitionView for region navigation.
/// Navigation is triggered from MainWindow.Loaded, not from OnInitialized.
/// </summary>
public class DataAcquisitionModule : IModule
{
    /// <inheritdoc />
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<DataAcquisitionViewModel>();
        containerRegistry.RegisterForNavigation<DataAcquisitionView>();
    }

    /// <inheritdoc />
    public void OnInitialized(IContainerProvider containerProvider)
    {
        // No automatic navigation; user selects this view from the navigation tree.
    }
}

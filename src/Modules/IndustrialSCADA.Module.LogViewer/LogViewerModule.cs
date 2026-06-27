using IndustrialSCADA.Module.LogViewer.Views;
using IndustrialSCADA.Module.LogViewer.ViewModels;
using Prism.Ioc;
using Prism.Modularity;

namespace IndustrialSCADA.Module.LogViewer;

/// <summary>
/// Log Viewer Prism module. Registers the LogViewerView for region navigation.
/// Navigation is triggered from MainWindow.Loaded, not from OnInitialized.
/// </summary>
public class LogViewerModule : IModule
{
    /// <inheritdoc />
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<LogViewerViewModel>();
        containerRegistry.RegisterForNavigation<LogViewerView>();
    }

    /// <inheritdoc />
    public void OnInitialized(IContainerProvider containerProvider)
    {
        // No automatic navigation; user selects this view from the navigation tree.
    }
}

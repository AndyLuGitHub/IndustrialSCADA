using IndustrialSCADA.Core.Interfaces;
using Prism.Navigation;
using Prism.Navigation.Regions;

namespace IndustrialSCADA.App.Services;

/// <summary>
/// Prism-based navigation service that bridges <see cref="INavigationService"/>
/// with <see cref="IRegionManager"/> for region-based view navigation.
/// </summary>
public class NavigationService : INavigationService
{
    private readonly IRegionManager _regionManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="NavigationService"/> class.
    /// </summary>
    /// <param name="regionManager">The Prism region manager.</param>
    public NavigationService(IRegionManager regionManager)
    {
        _regionManager = regionManager;
    }

    /// <inheritdoc />
    public event EventHandler<string>? Navigated;

    /// <inheritdoc />
    public void Navigate(string viewName, object? parameter = null)
    {
        var navigationParameters = new NavigationParameters();
        if (parameter is not null)
        {
            navigationParameters.Add("parameter", parameter);
        }

        _regionManager.RequestNavigate(
            "MainRegion",
            new Uri(viewName, UriKind.Relative),
            result =>
            {
                if (result.Success)
                {
                    Navigated?.Invoke(this, viewName);
                }
            },
            navigationParameters);
    }
}

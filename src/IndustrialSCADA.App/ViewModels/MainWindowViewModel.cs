using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;

namespace IndustrialSCADA.App.ViewModels;

/// <summary>
/// ViewModel for the main application window.
/// Manages the status bar properties and theme toggling.
/// </summary>
public partial class MainWindowViewModel : ObservableObject
{
    /// <summary>
    /// Gets or sets the window title.
    /// </summary>
    [ObservableProperty]
    private string _title = "Industrial SCADA";

    /// <summary>
    /// Gets or sets the current time displayed in the status bar.
    /// </summary>
    [ObservableProperty]
    private string _currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>
    /// Gets or sets the count of currently online devices.
    /// </summary>
    [ObservableProperty]
    private int _onlineDeviceCount;

    /// <summary>
    /// Gets or sets the count of unhandled alarms.
    /// </summary>
    [ObservableProperty]
    private int _unhandledAlarmCount;

    /// <summary>
    /// Navigates to the specified view by name.
    /// </summary>
    /// <param name="viewName">The registered view name to navigate to.</param>
    [RelayCommand]
    private void NavigateTo(string viewName)
    {
        // Navigation is handled via IRegionManager in code-behind;
        // this command is available for programmatic navigation from ViewModels.
    }

    /// <summary>
    /// Toggles the application theme between Dark and Light modes.
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        var resourceDictionary = new ResourceDictionary();
        var paletteHelper = new PaletteHelper();

        var theme = paletteHelper.GetTheme();
        var isDark = theme.GetBaseTheme() == BaseTheme.Dark;
        theme.SetBaseTheme(isDark ? BaseTheme.Light : BaseTheme.Dark);
        paletteHelper.SetTheme(theme);
    }
}

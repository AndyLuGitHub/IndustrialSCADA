using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using MaterialDesignThemes.Wpf;
using Prism.Navigation.Regions;

namespace IndustrialSCADA.App;

/// <summary>
/// Code-behind for the main application window.
/// Uses standard Prism Region navigation (IRegionManager.RequestNavigate) for view switching.
/// DataContext = this, so all XAML bindings resolve against properties defined here.
/// </summary>
public partial class MainWindow : Window, INotifyPropertyChanged
{
    private readonly DispatcherTimer _statusTimer;
    private readonly IRegionManager _regionManager;
    private string _title = "Industrial SCADA";
    private string _currentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    private int _onlineDeviceCount;
    private int _unhandledAlarmCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// IRegionManager is injected by DryIoc (registered automatically by Prism).
    /// DataContext is set BEFORE InitializeComponent so that bindings resolve immediately.
    /// </summary>
    public MainWindow(IRegionManager regionManager)
    {
        DataContext = this;
        InitializeComponent();

        _regionManager = regionManager;
        ToggleThemeCommand = new RelayCommand(ToggleTheme);

        _statusTimer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Normal,
            OnStatusTimerTick,
            Dispatcher);
        _statusTimer.Start();

        // After window fully loads, navigate to default view.
        // This guarantees MainRegion is registered (Region attached properties are processed
        // when the visual tree is built, which happens before Loaded fires).
        Loaded += (_, _) => NavigateTo("DashboardView");
    }

    // ── Bindable properties ──────────────────────────────────────────────

    /// <summary>Window title shown in the title bar.</summary>
    public new string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    /// <summary>Current time displayed in the status bar (updated every second).</summary>
    public string CurrentTime
    {
        get => _currentTime;
        set { if (_currentTime != value) { _currentTime = value; OnPropertyChanged(); } }
    }

    /// <summary>Count of online devices displayed in the status bar.</summary>
    public int OnlineDeviceCount
    {
        get => _onlineDeviceCount;
        set { if (_onlineDeviceCount != value) { _onlineDeviceCount = value; OnPropertyChanged(); } }
    }

    /// <summary>Count of unhandled alarms displayed in the status bar.</summary>
    public int UnhandledAlarmCount
    {
        get => _unhandledAlarmCount;
        set { if (_unhandledAlarmCount != value) { _unhandledAlarmCount = value; OnPropertyChanged(); } }
    }

    /// <summary>Command to toggle between Dark and Light MaterialDesign themes.</summary>
    public RelayCommand ToggleThemeCommand { get; }

    // ── Navigation ───────────────────────────────────────────────────────

    /// <summary>
    /// Standard Prism Region navigation: RequestNavigate asks Prism to resolve the view
    /// by its registered name and display it in MainRegion.
    /// </summary>
    private void NavigateTo(string viewName)
    {
        try
        {
            _regionManager.RequestNavigate("MainRegion", viewName, result =>
            {
                if (!result.Success)
                    System.Diagnostics.Debug.WriteLine(
                        $"[MainWindow] Navigate to {viewName} failed: {result.Exception?.Message}");
                else
                    System.Diagnostics.Debug.WriteLine($"[MainWindow] Navigate to {viewName} OK");
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[MainWindow] Navigate EXCEPTION: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles TreeView selection changes to navigate the main content region.
    /// </summary>
    private void NavigationTree_SelectedItemChanged(
        object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is TreeViewItem { Tag: string viewName })
        {
            NavigateTo(viewName);
        }
    }

    // ── Timer ────────────────────────────────────────────────────────────

    private void OnStatusTimerTick(object? sender, EventArgs e)
    {
        CurrentTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
    }

    // ── Theme ────────────────────────────────────────────────────────────

    private void ToggleTheme()
    {
        var paletteHelper = new PaletteHelper();
        var theme = paletteHelper.GetTheme();
        var isDark = theme.GetBaseTheme() == BaseTheme.Dark;
        theme.SetBaseTheme(isDark ? BaseTheme.Light : BaseTheme.Dark);
        paletteHelper.SetTheme(theme);
    }

    // ── INotifyPropertyChanged ───────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

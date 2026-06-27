using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace IndustrialSCADA.Module.Dashboard.ViewModels;

/// <summary>
/// ViewModel for the Dashboard view, providing real-time data display
/// with Rx subscriptions and LiveCharts2 trend charts.
/// </summary>
public partial class DashboardViewModel : ObservableObject, IDisposable
{
    private readonly IDataCollector _dataCollector;
    private readonly IDeviceManager _deviceManager;
    private readonly IAlarmService _alarmService;
    private readonly Dispatcher _dispatcher;

    private readonly IDisposable _dataSubscription;
    private readonly IDisposable _deviceStatusSubscription;
    private readonly IDisposable _alarmSubscription;

    // Chart backing collections
    private readonly ObservableCollection<double> _tempValues = new();
    private readonly ObservableCollection<double> _pressureValues = new();

    // ── Observable properties ───────────────────────────────────────────
    [ObservableProperty] private double temperature;
    [ObservableProperty] private double pressure;
    [ObservableProperty] private bool motorState;
    [ObservableProperty] private long counter;
    [ObservableProperty] private int onlineDeviceCount;
    [ObservableProperty] private int unhandledAlarmCount;
    [ObservableProperty] private int totalDeviceCount;
    [ObservableProperty] private bool temperatureQualityGood = true;
    [ObservableProperty] private double temperatureHighLimit = 75.0;

    // ── Chart properties (initialized once) ─────────────────────────────
    public ISeries[] Series { get; }
    public Axis[] XAxes { get; }
    public Axis[] YAxes { get; }

    private const int MaxChartPoints = 300;

    /// <summary>
    /// Initializes a new instance of the <see cref="DashboardViewModel"/> class.
    /// </summary>
    public DashboardViewModel(
        IDataCollector dataCollector,
        IDeviceManager deviceManager,
        IAlarmService alarmService)
    {
        _dataCollector = dataCollector;
        _deviceManager = deviceManager;
        _alarmService = alarmService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // ── Initialize chart series ──────────────────────────────────────
        Series = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = _tempValues,
                Name = "Temperature (\u00b0C)",
                ScalesYAt = 0,
                Stroke = new SolidColorPaint(SKColors.OrangeRed) { StrokeThickness = 2f },
                GeometrySize = 0,
                Fill = null
            },
            new LineSeries<double>
            {
                Values = _pressureValues,
                Name = "Pressure (bar)",
                ScalesYAt = 1,
                Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2f },
                GeometrySize = 0,
                Fill = null
            }
        };

        XAxes = new Axis[]
        {
            new Axis
            {
                Name = "Samples",
                Labeler = value => value.ToString("N0")
            }
        };

        YAxes = new Axis[]
        {
            new Axis
            {
                Name = "Temperature (\u00b0C)",
                NamePaint = new SolidColorPaint(SKColors.OrangeRed),
                MinLimit = 0,
                MaxLimit = 100
            },
            new Axis
            {
                Name = "Pressure (bar)",
                NamePaint = new SolidColorPaint(SKColors.DodgerBlue),
                MinLimit = 0,
                MaxLimit = 12
            }
        };

        // ── Initialize device counts ─────────────────────────────────────
        TotalDeviceCount = deviceManager.Devices.Count;
        OnlineDeviceCount = deviceManager.Devices.Count(d => d.Status == DeviceStatus.Online);

        // ── Subscribe to data stream ─────────────────────────────────────
        _dataSubscription = dataCollector.DataStream.Subscribe(dp =>
        {
            _dispatcher.InvokeAsync(() => OnDataReceived(dp), DispatcherPriority.Background);
        });

        // ── Subscribe to device status changes ───────────────────────────
        _deviceStatusSubscription = deviceManager.DeviceStatusChanged.Subscribe(_ =>
        {
            _dispatcher.InvokeAsync(() =>
            {
                TotalDeviceCount = deviceManager.Devices.Count;
                OnlineDeviceCount = deviceManager.Devices.Count(d => d.Status == DeviceStatus.Online);
            }, DispatcherPriority.Background);
        });

        // ── Subscribe to alarm stream ────────────────────────────────────
        _alarmSubscription = alarmService.AlarmStream.Subscribe(alarm =>
        {
            _dispatcher.InvokeAsync(() => OnAlarmReceived(alarm), DispatcherPriority.Background);
        });
    }

    /// <summary>
    /// Handle incoming data point on the UI thread.
    /// DataCollector pushes TagName in format "DeviceCode.Address" (e.g. "Demo-PLC-001.Temperature"),
    /// but we also accept plain address (e.g. "Temperature") for compatibility.
    /// </summary>
    private void OnDataReceived(DataPoint dp)
    {
        // Extract the address part: if TagName contains '.', take substring after the last '.'
        var tagName = dp.TagName;
        var lastDot = tagName.LastIndexOf('.');
        var address = lastDot >= 0 ? tagName[(lastDot + 1)..] : tagName;

        switch (address)
        {
            case "Temperature":
                Temperature = Convert.ToDouble(dp.CurrentValue ?? 0.0);
                TemperatureQualityGood = dp.Quality >= 192;
                _tempValues.Add(Temperature);
                if (_tempValues.Count > MaxChartPoints)
                    _tempValues.RemoveAt(0);
                break;

            case "Pressure":
                Pressure = Convert.ToDouble(dp.CurrentValue ?? 0.0);
                _pressureValues.Add(Pressure);
                if (_pressureValues.Count > MaxChartPoints)
                    _pressureValues.RemoveAt(0);
                break;

            case "MotorState":
                MotorState = Convert.ToBoolean(dp.CurrentValue ?? false);
                break;

            case "Counter":
                Counter = Convert.ToInt64(dp.CurrentValue ?? 0L);
                break;
        }
    }

    /// <summary>
    /// Handle incoming alarm on the UI thread.
    /// </summary>
    private void OnAlarmReceived(AlarmEntity alarm)
    {
        if (alarm.State == AlarmState.Active)
            UnhandledAlarmCount++;
        else
            UnhandledAlarmCount = Math.Max(0, UnhandledAlarmCount - 1);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _dataSubscription?.Dispose();
        _deviceStatusSubscription?.Dispose();
        _alarmSubscription?.Dispose();
    }
}

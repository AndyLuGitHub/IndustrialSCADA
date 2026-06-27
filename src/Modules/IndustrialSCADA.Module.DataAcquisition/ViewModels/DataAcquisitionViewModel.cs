using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Interfaces;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;

namespace IndustrialSCADA.Module.DataAcquisition.ViewModels;

/// <summary>
/// ViewModel for the Data Acquisition view.
/// Provides real-time data display, historical query, and trend chart.
/// </summary>
public partial class DataAcquisitionViewModel : ObservableObject, IDisposable
{
    private readonly IDataCollector _dataCollector;
    private readonly IDeviceManager _deviceManager;
    private readonly IHistoryRepository _historyRepository;
    private readonly Dispatcher _dispatcher;
    private readonly IDisposable _dataSubscription;

    // ── Real-time data ───────────────────────────────────────────────────
    /// <summary>Current values for all observed data points.</summary>
    public ObservableCollection<DataPoint> RealtimeData { get; } = new();

    /// <summary>Historical query results.</summary>
    public ObservableCollection<HistoryRecord> HistoryData { get; } = new();

    // ── Observable properties ────────────────────────────────────────────
    [ObservableProperty] private string selectedTag = string.Empty;
    [ObservableProperty] private DateTime historyFrom = DateTime.Now.AddHours(-1);
    [ObservableProperty] private DateTime historyTo = DateTime.Now;
    [ObservableProperty] private int realtimeCount;
    [ObservableProperty] private int historyCount;
    [ObservableProperty] private ObservableCollection<string> availableTags = new();

    // ── Chart properties ─────────────────────────────────────────────────
    /// <summary>Line series for historical trend chart.</summary>
    public ISeries[] HistorySeries { get; private set; } = Array.Empty<ISeries>();

    /// <summary>X axis (time) for historical trend chart.</summary>
    public Axis[] HistoryXAxes { get; private set; } = new Axis[]
    {
        new Axis
        {
            Name = "Time",
            Labeler = value =>
            {
                // Values are stored as Unix-timestamp seconds (double)
                var dt = DateTimeOffset.FromUnixTimeSeconds((long)value).LocalDateTime;
                return dt.ToString("HH:mm:ss");
            }
        }
    };

    /// <summary>Y axis (value) for historical trend chart.</summary>
    public Axis[] HistoryYAxes { get; private set; } = new Axis[]
    {
        new Axis
        {
            Name = "Value"
        }
    };

    /// <summary>Display title for the view.</summary>
    public string Title => "Data Acquisition";

    // ── Constructor ──────────────────────────────────────────────────────
    /// <summary>
    /// Initializes a new instance of the <see cref="DataAcquisitionViewModel"/> class.
    /// </summary>
    public DataAcquisitionViewModel(
        IDataCollector dataCollector,
        IDeviceManager deviceManager,
        IHistoryRepository historyRepository)
    {
        _dataCollector = dataCollector;
        _deviceManager = deviceManager;
        _historyRepository = historyRepository;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Seed RealtimeData with all known data points from all devices
        foreach (var device in deviceManager.Devices)
        {
            foreach (var dp in device.DataPoints)
            {
                RealtimeData.Add(dp);
            }
        }

        // Subscribe to the real-time data stream
        _dataSubscription = dataCollector.DataStream.Subscribe(dp =>
        {
            _dispatcher.InvokeAsync(() => OnDataReceived(dp), DispatcherPriority.Background);
        });
    }

    // ── Rx handler ───────────────────────────────────────────────────────
    /// <summary>
    /// Handle an incoming data point on the UI thread.
    /// Updates existing entry or adds a new one, and increments the counter.
    /// </summary>
    private void OnDataReceived(DataPoint dp)
    {
        // Find existing entry by TagName
        var existing = RealtimeData.FirstOrDefault(d => d.TagName == dp.TagName);
        if (existing != null)
        {
            existing.CurrentValue = dp.CurrentValue;
            existing.Quality = dp.Quality;
            existing.UpdatedAt = dp.UpdatedAt;
        }
        else
        {
            RealtimeData.Add(dp);
        }

        RealtimeCount++;
    }

    // ── Commands ─────────────────────────────────────────────────────────
    /// <summary>
    /// Query historical records for the selected tag between the configured time range.
    /// Also populates the trend chart.
    /// </summary>
    [RelayCommand]
    private async Task QueryHistoryAsync()
    {
        if (string.IsNullOrWhiteSpace(SelectedTag))
            return;

        var records = await _historyRepository.QueryAsync(SelectedTag, HistoryFrom, HistoryTo);

        HistoryData.Clear();
        foreach (var r in records)
        {
            HistoryData.Add(r);
        }

        HistoryCount = records.Count;

        // Build chart series from the query results using index-based X axis
        var orderedRecords = records.OrderBy(r => r.Timestamp).ToArray();
        var timestamps = orderedRecords.Select(r => r.Timestamp).ToArray();
        var numericValues = orderedRecords
            .Select(r => Convert.ToDouble(r.Value ?? 0.0))
            .ToArray();

        HistorySeries = new ISeries[]
        {
            new LineSeries<double>
            {
                Values = numericValues,
                Name = SelectedTag,
                Stroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2f },
                GeometrySize = 6,
                GeometryStroke = new SolidColorPaint(SKColors.DodgerBlue) { StrokeThickness = 2f },
                Fill = null
            }
        };

        HistoryXAxes = new Axis[]
        {
            new Axis
            {
                Name = "Time",
                Labeler = value =>
                {
                    try
                    {
                        var idx = (int)value;
                        if (idx >= 0 && idx < timestamps.Length)
                            return timestamps[idx].ToString("HH:mm:ss");
                    }
                    catch { }
                    return string.Empty;
                },
                MinLimit = 0,
                MaxLimit = Math.Max(0, timestamps.Length - 1),
                UnitWidth = 1,
                MinStep = 1
            }
        };

        OnPropertyChanged(nameof(HistorySeries));
        OnPropertyChanged(nameof(HistoryXAxes));
        OnPropertyChanged(nameof(HistoryYAxes));
    }

    /// <summary>
    /// Load the list of available tag names from the history repository.
    /// </summary>
    [RelayCommand]
    private async Task LoadTagsAsync()
    {
        var tags = await _historyRepository.ListTagsAsync();
        AvailableTags = new ObservableCollection<string>(tags);
    }

    // ── IDisposable ──────────────────────────────────────────────────────
    /// <inheritdoc />
    public void Dispose()
    {
        _dataSubscription?.Dispose();
    }
}

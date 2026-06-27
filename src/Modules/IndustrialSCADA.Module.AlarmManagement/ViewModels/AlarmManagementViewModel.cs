using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;

namespace IndustrialSCADA.Module.AlarmManagement.ViewModels;

/// <summary>
/// ViewModel for the Alarm Management view, providing alarm querying,
/// filtering, acknowledgement, handling, and real-time Rx subscriptions.
/// </summary>
public partial class AlarmManagementViewModel : ObservableObject, IDisposable
{
    private readonly IAlarmService _alarmService;
    private readonly Dispatcher _dispatcher;
    private readonly IDisposable _alarmSubscription;

    /// <summary>
    /// The alarm list bound to the DataGrid.
    /// </summary>
    public ObservableCollection<AlarmEntity> Alarms { get; } = new();

    [ObservableProperty]
    private AlarmSeverity? selectedSeverity;

    [ObservableProperty]
    private AlarmState? selectedState;

    [ObservableProperty]
    private int totalCount;

    [ObservableProperty]
    private int activeCount;

    [ObservableProperty]
    private int unhandledCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="AlarmManagementViewModel"/> class.
    /// </summary>
    /// <param name="alarmService">The alarm service for querying and managing alarms.</param>
    public AlarmManagementViewModel(IAlarmService alarmService)
    {
        _alarmService = alarmService;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Subscribe to the real-time alarm stream; auto-refresh on new events
        _alarmSubscription = alarmService.AlarmStream.Subscribe(_ =>
        {
            _dispatcher.InvokeAsync(async () => await RefreshAsync(), DispatcherPriority.Background);
        });

        // Initial data load
        _dispatcher.InvokeAsync(
            () => RefreshCommand.Execute(null),
            DispatcherPriority.Loaded);
    }

    /// <summary>
    /// Queries alarms with the current filter and updates the collection and counts.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var filter = new AlarmFilter
        {
            Severity = SelectedSeverity,
            State = SelectedState,
            PageSize = 200
        };

        var alarms = await _alarmService.QueryAsync(filter);

        Alarms.Clear();
        foreach (var alarm in alarms)
            Alarms.Add(alarm);

        TotalCount = alarms.Count;
        ActiveCount = alarms.Count(a => a.State == AlarmState.Active);
        UnhandledCount = ActiveCount;
    }

    /// <summary>
    /// Acknowledges the specified alarm, then refreshes the list.
    /// </summary>
    [RelayCommand]
    private async Task AcknowledgeAsync(AlarmEntity? alarm)
    {
        if (alarm == null) return;

        var guid = LongToGuid(alarm.Id);
        await _alarmService.AcknowledgeAsync(guid, "Operator");
        await RefreshAsync();
    }

    /// <summary>
    /// Handles (clears) the specified alarm with a remark, then refreshes the list.
    /// </summary>
    [RelayCommand]
    private async Task HandleAsync(AlarmEntity? alarm)
    {
        if (alarm == null) return;

        var guid = LongToGuid(alarm.Id);
        await _alarmService.HandleAsync(guid, "Operator", "Handled via Alarm Management");
        await RefreshAsync();
    }

    /// <summary>
    /// Converts a long Id to a Guid compatible with AlarmService.FindAlarmByGuid.
    /// The service extracts the first 8 bytes via BitConverter.ToInt64, so we
    /// embed the long value in the first 8 bytes of a 16-byte Guid.
    /// </summary>
    private static Guid LongToGuid(long value)
    {
        var longBytes = BitConverter.GetBytes(value);
        var guidBytes = new byte[16];
        Array.Copy(longBytes, guidBytes, 8);
        return new Guid(guidBytes);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _alarmSubscription?.Dispose();
    }
}

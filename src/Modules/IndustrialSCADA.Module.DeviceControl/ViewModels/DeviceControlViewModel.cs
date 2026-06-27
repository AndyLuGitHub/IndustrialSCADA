using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Interfaces;

namespace IndustrialSCADA.Module.DeviceControl.ViewModels;

/// <summary>
/// ViewModel for the Device Control view, providing device management,
/// protocol adapter connection, and manual read/write operations.
/// </summary>
public partial class DeviceControlViewModel : ObservableObject, IDisposable
{
    private readonly IDeviceManager _deviceManager;
    private readonly Dispatcher _dispatcher;
    private readonly IDisposable _statusSubscription;

    // ── Observable properties ───────────────────────────────────────────
    [ObservableProperty] private DeviceEntity? selectedDevice;
    [ObservableProperty] private bool isConnected;
    [ObservableProperty] private string readAddress = string.Empty;
    [ObservableProperty] private string readResult = string.Empty;
    [ObservableProperty] private string writeAddress = string.Empty;
    [ObservableProperty] private string writeValue = string.Empty;

    /// <summary>
    /// Gets the display title for the device control view.
    /// </summary>
    public string Title => "Device Control";

    /// <summary>
    /// Device list for the DataGrid.
    /// </summary>
    public ObservableCollection<DeviceEntity> Devices { get; } = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="DeviceControlViewModel"/> class.
    /// </summary>
    /// <param name="deviceManager">The device manager service.</param>
    public DeviceControlViewModel(IDeviceManager deviceManager)
    {
        _deviceManager = deviceManager;
        _dispatcher = Dispatcher.CurrentDispatcher;

        // Load initial device list
        foreach (var device in deviceManager.Devices)
            Devices.Add(device);

        // Subscribe to device status changes via Rx
        _statusSubscription = deviceManager.DeviceStatusChanged
            .Subscribe(_ =>
            {
                _dispatcher.InvokeAsync(RefreshDeviceList, DispatcherPriority.Background);
            });
    }

    /// <summary>
    /// Reload the device list from IDeviceManager.
    /// </summary>
    [RelayCommand]
    private Task RefreshAsync()
    {
        RefreshDeviceList();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Connect the selected device's protocol adapter.
    /// </summary>
    [RelayCommand]
    private async Task ConnectAsync()
    {
        if (SelectedDevice == null) return;

        try
        {
            var adapter = _deviceManager.GetProtocolAdapter(SelectedDevice.Name);
            var config = BuildConnectionConfig(SelectedDevice);
            await adapter.ConnectAsync(config);
            IsConnected = adapter.IsConnected;
        }
        catch (Exception ex)
        {
            ReadResult = $"Connect error: {ex.Message}";
            IsConnected = false;
        }
    }

    /// <summary>
    /// Disconnect the selected device's protocol adapter.
    /// </summary>
    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (SelectedDevice == null) return;

        try
        {
            var adapter = _deviceManager.GetProtocolAdapter(SelectedDevice.Name);
            await adapter.DisconnectAsync();
            IsConnected = false;
        }
        catch (Exception ex)
        {
            ReadResult = $"Disconnect error: {ex.Message}";
        }
    }

    /// <summary>
    /// Read a value from the specified address on the selected device.
    /// </summary>
    [RelayCommand]
    private async Task ReadAsync()
    {
        if (SelectedDevice == null || string.IsNullOrWhiteSpace(ReadAddress)) return;

        try
        {
            var adapter = _deviceManager.GetProtocolAdapter(SelectedDevice.Name);
            var value = await adapter.ReadAsync<object>(ReadAddress);
            ReadResult = value?.ToString() ?? "(null)";
        }
        catch (Exception ex)
        {
            ReadResult = $"Read error: {ex.Message}";
        }
    }

    /// <summary>
    /// Write a value to the specified address on the selected device.
    /// </summary>
    [RelayCommand]
    private async Task WriteAsync()
    {
        if (SelectedDevice == null || string.IsNullOrWhiteSpace(WriteAddress)) return;

        try
        {
            var adapter = _deviceManager.GetProtocolAdapter(SelectedDevice.Name);
            await adapter.WriteAsync(WriteAddress, WriteValue);
            ReadResult = $"Write OK: {WriteValue} -> {WriteAddress}";
        }
        catch (Exception ex)
        {
            ReadResult = $"Write error: {ex.Message}";
        }
    }

    /// <summary>
    /// Refresh the Devices collection from the manager's current list.
    /// </summary>
    private void RefreshDeviceList()
    {
        Devices.Clear();
        foreach (var device in _deviceManager.Devices)
            Devices.Add(device);
    }

    /// <summary>
    /// Build a ConnectionConfig from the device entity's connection address.
    /// Supports "host:port" or plain "host" format.
    /// </summary>
    private static ConnectionConfig BuildConnectionConfig(DeviceEntity device)
    {
        var host = device.ConnectionAddress;
        var port = 0;

        var colonIndex = device.ConnectionAddress.LastIndexOf(':');
        if (colonIndex > 0 && int.TryParse(device.ConnectionAddress[(colonIndex + 1)..], out var parsedPort))
        {
            host = device.ConnectionAddress[..colonIndex];
            port = parsedPort;
        }

        return new ConnectionConfig
        {
            Host = host,
            Port = port,
            ProtocolType = device.ProtocolType
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _statusSubscription?.Dispose();
    }
}

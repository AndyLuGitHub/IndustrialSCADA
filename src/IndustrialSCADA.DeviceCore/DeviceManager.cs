using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using IndustrialSCADA.Infrastructure.Communication;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.DeviceCore;

/// <summary>
/// 设备管理器实现，负责设备生命周期管理和协议适配器的创建/获取。
/// 内部使用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 保证线程安全。
/// </summary>
public sealed class DeviceManager : IDeviceManager, IDisposable
{
    private readonly IProtocolAdapterFactory _adapterFactory;
    private readonly ILogger<DeviceManager> _logger;

    /// <summary>设备字典，Key = 设备名称（作为设备编码）。</summary>
    private readonly ConcurrentDictionary<string, DeviceEntity> _devices = new();

    /// <summary>协议适配器字典，Key = 设备名称。</summary>
    private readonly ConcurrentDictionary<string, IProtocolAdapter> _adapters = new();

    /// <summary>设备状态变化的 Subject。</summary>
    private readonly Subject<DeviceEntity> _statusSubject = new();

    /// <summary>
    /// 初始化 <see cref="DeviceManager"/> 的新实例。
    /// </summary>
    /// <param name="adapterFactory">协议适配器工厂。</param>
    /// <param name="logger">日志记录器。</param>
    public DeviceManager(IProtocolAdapterFactory adapterFactory, ILogger<DeviceManager> logger)
    {
        _adapterFactory = adapterFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<DeviceEntity> Devices => _devices.Values.ToList();

    /// <inheritdoc />
    public IObservable<DeviceEntity> DeviceStatusChanged => _statusSubject.AsObservable();

    /// <inheritdoc />
    public Task AddDeviceAsync(DeviceEntity device)
    {
        var key = device.Name;

        if (!_devices.TryAdd(key, device))
        {
            _logger.LogWarning("设备 {DeviceCode} 已存在，跳过添加", key);
            return Task.CompletedTask;
        }

        // 为该设备创建协议适配器
        var adapter = _adapterFactory.Create(device.ProtocolType);
        _adapters.TryAdd(key, adapter);

        // 订阅适配器的连接状态变化，联动设备状态
        adapter.ConnectionStateChanged += (_, args) =>
        {
            device.Status = args.IsConnected ? DeviceStatus.Online : DeviceStatus.Offline;
            device.UpdatedAt = DateTime.UtcNow;
            _statusSubject.OnNext(device);
        };

        _logger.LogInformation("已添加设备: {DeviceCode} ({Protocol})", key, device.ProtocolType);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task RemoveDeviceAsync(string deviceCode)
    {
        if (_adapters.TryRemove(deviceCode, out var adapter))
        {
            try
            {
                if (adapter.IsConnected)
                    await adapter.DisconnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "断开设备 {DeviceCode} 连接时出错", deviceCode);
            }
        }

        if (_devices.TryRemove(deviceCode, out var device))
        {
            device.Status = DeviceStatus.Offline;
            device.UpdatedAt = DateTime.UtcNow;
            _statusSubject.OnNext(device);
            _logger.LogInformation("已移除设备: {DeviceCode}", deviceCode);
        }
    }

    /// <inheritdoc />
    public Task<DeviceEntity?> GetDeviceAsync(string deviceCode)
    {
        _devices.TryGetValue(deviceCode, out var device);
        return Task.FromResult(device);
    }

    /// <inheritdoc />
    public IProtocolAdapter GetProtocolAdapter(string deviceCode)
    {
        if (_adapters.TryGetValue(deviceCode, out var adapter))
            return adapter;

        throw new KeyNotFoundException($"未找到设备 {deviceCode} 的协议适配器，请先调用 AddDeviceAsync");
    }

    /// <summary>
    /// 释放资源，断开所有设备连接。
    /// </summary>
    public void Dispose()
    {
        foreach (var kvp in _adapters)
        {
            try
            {
                if (kvp.Value.IsConnected)
                    kvp.Value.DisconnectAsync().GetAwaiter().GetResult();
            }
            catch
            {
                // 静默处理断开连接时的异常
            }
        }

        _adapters.Clear();
        _devices.Clear();
        _statusSubject.Dispose();
    }
}

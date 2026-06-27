using System.Reactive.Linq;
using System.Reactive.Subjects;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.DeviceCore;

/// <summary>
/// 数据采集器实现，负责周期性扫描所有已启用设备并将数据推送到统一数据流。
/// 后台采集循环使用 <see cref="CancellationTokenSource"/> 控制生命周期。
/// </summary>
public sealed class DataCollector : IDataCollector, IDisposable
{
    private readonly IDeviceManager _deviceManager;
    private readonly ILogger<DataCollector> _logger;

    /// <summary>数据流 Subject，所有采集到的数据点通过此推送。</summary>
    private readonly Subject<DataPoint> _dataSubject = new();

    /// <summary>用于控制后台采集循环的取消令牌源。</summary>
    private CancellationTokenSource? _cts;

    /// <summary>后台采集任务。</summary>
    private Task? _collectTask;

    /// <summary>
    /// 初始化 <see cref="DataCollector"/> 的新实例。
    /// </summary>
    /// <param name="deviceManager">设备管理器。</param>
    /// <param name="logger">日志记录器。</param>
    public DataCollector(IDeviceManager deviceManager, ILogger<DataCollector> logger)
    {
        _deviceManager = deviceManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public IObservable<DataPoint> DataStream => _dataSubject.AsObservable();

    /// <inheritdoc />
    public Task StartAsync()
    {
        if (_cts != null && !_cts.IsCancellationRequested)
        {
            _logger.LogWarning("数据采集已在运行中");
            return Task.CompletedTask;
        }

        _cts = new CancellationTokenSource();
        _collectTask = CollectLoopAsync(_cts.Token);
        _logger.LogInformation("数据采集已启动");
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        if (_cts == null)
            return;

        _cts.Cancel();

        if (_collectTask != null)
        {
            try
            {
                await _collectTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 预期的取消异常
            }
        }

        _cts.Dispose();
        _cts = null;
        _collectTask = null;
        _logger.LogInformation("数据采集已停止");
    }

    /// <inheritdoc />
    public async Task<DataPoint> ReadOnceAsync(string deviceCode, string address)
    {
        var adapter = _deviceManager.GetProtocolAdapter(deviceCode);
        var value = await adapter.ReadAsync<object>(address).ConfigureAwait(false);

        return new DataPoint
        {
            TagName = $"{deviceCode}.{address}",
            Address = address,
            CurrentValue = value,
            Quality = 192,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// 后台采集循环，按照各设备配置的扫描间隔周期性读取数据。
    /// </summary>
    private async Task CollectLoopAsync(CancellationToken ct)
    {
        _logger.LogInformation("采集循环开始");

        while (!ct.IsCancellationRequested)
        {
            var devices = _deviceManager.Devices;

            foreach (var device in devices)
            {
                if (ct.IsCancellationRequested) break;
                if (!device.IsEnabled) continue;

                var adapter = _deviceManager.GetProtocolAdapter(device.Name);
                if (!adapter.IsConnected) continue;

                foreach (var dp in device.DataPoints)
                {
                    if (ct.IsCancellationRequested) break;

                    try
                    {
                        var value = await adapter.ReadAsync<object>(dp.Address, ct).ConfigureAwait(false);

                        var point = new DataPoint
                        {
                            Id = dp.Id,
                            DeviceId = device.Id,
                            TagName = dp.TagName,
                            Address = dp.Address,
                            PointType = dp.PointType,
                            Unit = dp.Unit,
                            ScaleFactor = dp.ScaleFactor,
                            Offset = dp.Offset,
                            HighLimit = dp.HighLimit,
                            LowLimit = dp.LowLimit,
                            CurrentValue = value,
                            Quality = 192,
                            CreatedAt = dp.CreatedAt,
                            UpdatedAt = DateTime.UtcNow
                        };

                        _dataSubject.OnNext(point);

                        // 更新设备的最后通信时间
                        device.LastCommunicationAt = DateTime.UtcNow;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "采集设备 {Device} 地址 {Address} 失败", device.Name, dp.Address);

                        // 推送质量标记为 Bad 的数据点
                        var badPoint = new DataPoint
                        {
                            Id = dp.Id,
                            DeviceId = device.Id,
                            TagName = dp.TagName,
                            Address = dp.Address,
                            CurrentValue = null,
                            Quality = 0,
                            CreatedAt = dp.CreatedAt,
                            UpdatedAt = DateTime.UtcNow
                        };
                        _dataSubject.OnNext(badPoint);
                    }
                }
            }

            // 全局最小扫描间隔，避免空转
            try
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("采集循环结束");
    }

    /// <summary>
    /// 释放资源，停止采集并清理。
    /// </summary>
    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _dataSubject.Dispose();
    }
}

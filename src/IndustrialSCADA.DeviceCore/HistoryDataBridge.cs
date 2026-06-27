using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.DeviceCore;

/// <summary>
/// 历史数据桥接服务：订阅 IDataCollector.DataStream，按时间窗口批量缓冲数据点，
/// 并通过 IHistoryRepository 持久化到 SQLite 历史数据库。
/// </summary>
public sealed class HistoryDataBridge : IDisposable
{
    private readonly IDataCollector _dataCollector;
    private readonly IHistoryRepository _historyRepository;
    private readonly ILogger<HistoryDataBridge> _logger;
    private IDisposable? _subscription;

    /// <summary>
    /// 初始化 HistoryDataBridge 实例。
    /// </summary>
    /// <param name="dataCollector">数据采集器，提供实时数据流。</param>
    /// <param name="historyRepository">历史数据存储仓库。</param>
    /// <param name="logger">日志记录器。</param>
    public HistoryDataBridge(
        IDataCollector dataCollector,
        IHistoryRepository historyRepository,
        ILogger<HistoryDataBridge> logger)
    {
        _dataCollector = dataCollector ?? throw new ArgumentNullException(nameof(dataCollector));
        _historyRepository = historyRepository ?? throw new ArgumentNullException(nameof(historyRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 启动数据桥接，订阅数据采集器的 DataStream，每 5 秒缓冲一批数据点并写入历史库。
    /// </summary>
    public void Start()
    {
        if (_subscription is not null)
        {
            _logger.LogWarning("HistoryDataBridge 已在运行，忽略重复启动调用。");
            return;
        }

        _subscription = _dataCollector.DataStream
            .Buffer(TimeSpan.FromSeconds(5))
            .Where(batch => batch.Count > 0)
            .SelectMany(batch => Observable.FromAsync(() => SaveBatchAsync(batch)))
            .Subscribe(
                _ => { },
                ex => _logger.LogError(ex, "HistoryDataBridge 订阅管道发生未处理异常。"));
    }

    /// <summary>
    /// 将一批数据点转换为历史记录并持久化。
    /// </summary>
    private async Task SaveBatchAsync(IList<DataPoint> batch)
    {
        try
        {
            var records = batch.Select(dp => new HistoryRecord
            {
                TagName = dp.TagName,
                Value = Convert.ToDouble(dp.CurrentValue ?? 0.0),
                Quality = dp.Quality,
                Timestamp = dp.UpdatedAt
            }).ToList();

            await _historyRepository.SaveAsync(records).ConfigureAwait(false);

            _logger.LogDebug("HistoryDataBridge 已保存 {BatchSize} 条历史记录。", records.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "HistoryDataBridge 保存历史记录时发生异常（批次大小: {BatchSize}）。", batch.Count);
        }
    }

    /// <summary>
    /// 释放 Rx 订阅资源。
    /// </summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}

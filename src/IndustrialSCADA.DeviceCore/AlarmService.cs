using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.DeviceCore;

/// <summary>
/// 报警服务实现，管理报警的产生、确认、处理及查询。
/// 使用内存字典存储当前活跃的报警，同时通过 <see cref="Subject{T}"/> 推送报警流。
/// </summary>
public sealed class AlarmService : IAlarmService, IDisposable
{
    private readonly ILogger<AlarmService> _logger;

    /// <summary>报警流 Subject。</summary>
    private readonly Subject<AlarmEntity> _alarmSubject = new();

    /// <summary>内存报警存储，Key = 报警 Id (long)。</summary>
    private readonly ConcurrentDictionary<long, AlarmEntity> _alarms = new();

    /// <summary>
    /// 初始化 <see cref="AlarmService"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public AlarmService(ILogger<AlarmService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IObservable<AlarmEntity> AlarmStream => _alarmSubject.AsObservable();

    /// <inheritdoc />
    public Task RaiseAlarmAsync(AlarmEntity alarm)
    {
        // 如果未设置 Id，则生成新的 Guid（使用 long 范围模拟）
        if (alarm.Id == 0)
            alarm.Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        alarm.State = AlarmState.Active;
        alarm.TriggeredAt = DateTime.UtcNow;
        alarm.CreatedAt = DateTime.UtcNow;
        alarm.UpdatedAt = DateTime.UtcNow;

        // 使用 alarm.Id 作为 key（ConcurrentDictionary 需要匹配 TKey）
        // 由于 EntityBase.Id 是 long，我们需要将 Guid 映射到 long
        var key = alarm.Id;
        _alarms[key] = alarm;

        _alarmSubject.OnNext(alarm);
        _logger.LogWarning("报警触发: [{Severity}] {Tag} - {Message}",
            alarm.Severity, alarm.TagName, alarm.Message);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task AcknowledgeAsync(Guid alarmId, string @operator)
    {
        var alarm = FindAlarmByGuid(alarmId);
        if (alarm == null)
        {
            _logger.LogWarning("确认报警失败: 未找到报警 {AlarmId}", alarmId);
            return Task.CompletedTask;
        }

        alarm.State = AlarmState.Acknowledged;
        alarm.AcknowledgedAt = DateTime.UtcNow;
        alarm.UpdatedAt = DateTime.UtcNow;

        _alarmSubject.OnNext(alarm);
        _logger.LogInformation("报警已确认: {AlarmId} by {Operator}", alarmId, @operator);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task HandleAsync(Guid alarmId, string @operator, string remark)
    {
        var alarm = FindAlarmByGuid(alarmId);
        if (alarm == null)
        {
            _logger.LogWarning("处理报警失败: 未找到报警 {AlarmId}", alarmId);
            return Task.CompletedTask;
        }

        alarm.State = AlarmState.Cleared;
        alarm.ClearedAt = DateTime.UtcNow;
        alarm.UpdatedAt = DateTime.UtcNow;
        // 将 remark 附加到 Message
        alarm.Message = $"{alarm.Message} [处理: {@operator} - {remark}]";

        _alarmSubject.OnNext(alarm);
        _logger.LogInformation("报警已处理: {AlarmId} by {Operator}, 备注: {Remark}",
            alarmId, @operator, remark);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AlarmEntity>> QueryAsync(AlarmFilter filter)
    {
        var query = _alarms.Values.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(filter.DeviceCode))
            query = query.Where(a => a.TagName.StartsWith(filter.DeviceCode, StringComparison.OrdinalIgnoreCase));

        if (filter.Severity.HasValue)
            query = query.Where(a => a.Severity == filter.Severity.Value);

        if (filter.State.HasValue)
            query = query.Where(a => a.State == filter.State.Value);

        if (filter.TimeFrom.HasValue)
            query = query.Where(a => a.TriggeredAt >= filter.TimeFrom.Value);

        if (filter.TimeTo.HasValue)
            query = query.Where(a => a.TriggeredAt <= filter.TimeTo.Value);

        var result = query
            .OrderByDescending(a => a.TriggeredAt)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToList();

        return Task.FromResult<IReadOnlyList<AlarmEntity>>(result);
    }

    /// <summary>
    /// 根据 Guid 查找报警实体。由于内部使用 long Id 存储，需要通过遍历匹配。
    /// </summary>
    private AlarmEntity? FindAlarmByGuid(Guid alarmId)
    {
        // 尝试将 Guid 转为 long（如果是从 long 创建的 Guid）
        var bytes = alarmId.ToByteArray();
        var longId = BitConverter.ToInt64(bytes, 0);

        if (_alarms.TryGetValue(longId, out var alarm))
            return alarm;

        // 回退：遍历查找
        return _alarms.Values.FirstOrDefault(a => a.Id == longId);
    }

    /// <summary>
    /// 释放资源。
    /// </summary>
    public void Dispose()
    {
        _alarmSubject.Dispose();
        _alarms.Clear();
    }
}

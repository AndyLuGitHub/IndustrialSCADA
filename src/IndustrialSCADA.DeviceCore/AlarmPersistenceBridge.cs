using System;
using System.Reactive.Disposables;
using System.Threading.Tasks;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using IndustrialSCADA.Infrastructure.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.DeviceCore;

/// <summary>
/// 桥接报警流与 SQLite 持久化的后台服务。
/// 订阅 <see cref="IAlarmService.AlarmStream"/>，将每条报警写入数据库。
/// </summary>
public sealed class AlarmPersistenceBridge : IDisposable
{
    private readonly IAlarmService _alarmService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AlarmPersistenceBridge> _logger;
    private IDisposable? _subscription;

    /// <summary>
    /// 初始化 <see cref="AlarmPersistenceBridge"/> 的新实例。
    /// </summary>
    /// <param name="alarmService">报警服务，提供 Rx 报警流。</param>
    /// <param name="serviceProvider">DI 容器，用于创建 DbContext 作用域。</param>
    /// <param name="logger">日志记录器。</param>
    public AlarmPersistenceBridge(
        IAlarmService alarmService,
        IServiceProvider serviceProvider,
        ILogger<AlarmPersistenceBridge> logger)
    {
        _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 开始订阅报警流，将每条报警持久化到数据库。
    /// </summary>
    public void Start()
    {
        if (_subscription is not null)
        {
            _logger.LogWarning("AlarmPersistenceBridge is already started.");
            return;
        }

        _subscription = _alarmService.AlarmStream.Subscribe(
            onNext: alarm => _ = PersistAlarmAsync(alarm),
            onError: ex => _logger.LogError(ex, "Alarm stream terminated with an error."),
            onCompleted: () => _logger.LogInformation("Alarm stream completed."));

        _logger.LogInformation("AlarmPersistenceBridge started. Subscribed to alarm stream.");
    }

    /// <summary>
    /// 将单条报警写入数据库。为每次写入创建独立的 DbContext 作用域，避免并发问题。
    /// </summary>
    private async Task PersistAlarmAsync(AlarmEntity alarm)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();

            dbContext.Alarms.Add(alarm);
            await dbContext.SaveChangesAsync().ConfigureAwait(false);

            LogAlarm(alarm);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist alarm [{AlarmId}] for tag '{TagName}'.",
                alarm.Id, alarm.TagName);
        }
    }

    /// <summary>
    /// 根据报警严重程度以适当的日志级别记录报警信息。
    /// </summary>
    private void LogAlarm(AlarmEntity alarm)
    {
        switch (alarm.Severity)
        {
            case AlarmSeverity.Info:
                _logger.LogInformation("Alarm persisted [{Id}] [{Severity}] {Tag}: {Message}",
                    alarm.Id, alarm.Severity, alarm.TagName, alarm.Message);
                break;

            case AlarmSeverity.Warning:
                _logger.LogWarning("Alarm persisted [{Id}] [{Severity}] {Tag}: {Message}",
                    alarm.Id, alarm.Severity, alarm.TagName, alarm.Message);
                break;

            case AlarmSeverity.Critical:
            case AlarmSeverity.Emergency:
                _logger.LogError("Alarm persisted [{Id}] [{Severity}] {Tag}: {Message}",
                    alarm.Id, alarm.Severity, alarm.TagName, alarm.Message);
                break;

            default:
                _logger.LogInformation("Alarm persisted [{Id}] [{Severity}] {Tag}: {Message}",
                    alarm.Id, alarm.Severity, alarm.TagName, alarm.Message);
                break;
        }
    }

    /// <summary>
    /// 释放报警流订阅。
    /// </summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
    }
}

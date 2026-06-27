using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using System.Threading.Tasks;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.DeviceCore.Simulator;

/// <summary>
/// 演示报警生成器：订阅 <see cref="IDataCollector.DataStream"/>，
/// 当模拟数据超过配置阈值时自动生成报警，端到端演示报警流水线。
/// </summary>
public sealed class DemoAlarmGenerator : IDisposable
{
    private readonly IDataCollector _dataCollector;
    private readonly IAlarmService _alarmService;
    private readonly ILogger<DemoAlarmGenerator> _logger;

    /// <summary>跟踪当前活跃报警的标签键集合，避免重复触发相同的活跃报警。</summary>
    private readonly HashSet<string> _activeAlarms = new();

    /// <summary>记录各电机标签上一次的状态，用于检测从 false→true 的跳变。</summary>
    private readonly Dictionary<string, bool> _previousMotorState = new();

    private IDisposable? _subscription;

    /// <summary>
    /// 初始化 <see cref="DemoAlarmGenerator"/> 的新实例。
    /// </summary>
    /// <param name="dataCollector">数据采集器。</param>
    /// <param name="alarmService">报警服务。</param>
    /// <param name="logger">日志记录器。</param>
    public DemoAlarmGenerator(
        IDataCollector dataCollector,
        IAlarmService alarmService,
        ILogger<DemoAlarmGenerator> logger)
    {
        _dataCollector = dataCollector ?? throw new ArgumentNullException(nameof(dataCollector));
        _alarmService = alarmService ?? throw new ArgumentNullException(nameof(alarmService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 启动报警生成器，订阅数据采集流。重复调用将被忽略。
    /// </summary>
    public void Start()
    {
        if (_subscription != null)
        {
            _logger.LogWarning("DemoAlarmGenerator 已启动，忽略重复调用。");
            return;
        }

        _subscription = _dataCollector.DataStream
            .Subscribe(
                onNext: dp => _ = ProcessDataPointAsync(dp),
                onError: ex => _logger.LogError(ex, "DataStream 订阅发生错误。"));

        _logger.LogInformation("DemoAlarmGenerator 已启动，已订阅 DataStream。");
    }

    /// <summary>
    /// 处理单个数据点：根据地址部分分发到对应的阈值判断逻辑。
    /// </summary>
    private async Task ProcessDataPointAsync(DataPoint dp)
    {
        try
        {
            var tagName = dp.TagName;
            var lastDot = tagName.LastIndexOf('.');
            var address = lastDot >= 0 ? tagName[(lastDot + 1)..] : tagName;

            switch (address)
            {
                case "Temperature":
                    await ProcessTemperatureAsync(tagName, dp.CurrentValue).ConfigureAwait(false);
                    break;

                case "Pressure":
                    await ProcessPressureAsync(tagName, dp.CurrentValue).ConfigureAwait(false);
                    break;

                case "MotorState":
                    await ProcessMotorStateAsync(tagName, dp.CurrentValue).ConfigureAwait(false);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "处理数据点 {TagName} 时发生异常。", dp.TagName);
        }
    }

    // ─────────────────────────────────────────────
    //  Temperature: >70 Critical, >60 Warning, <=60 Clear
    // ─────────────────────────────────────────────

    private async Task ProcessTemperatureAsync(string tagName, object? value)
    {
        if (!TryConvertToDouble(value, out var temp))
            return;

        var criticalKey = $"{tagName}:TempHigh:Critical";
        var warningKey = $"{tagName}:TempHigh:Warning";

        if (temp > 70.0)
        {
            _activeAlarms.Remove(warningKey);
            if (_activeAlarms.Add(criticalKey))
            {
                _logger.LogWarning("温度 {Value:F1}°C 超过 Critical 阈值 70°C [{Tag}]", temp, tagName);
                await RaiseAlarmAsync(tagName, "Temperature High Alarm",
                    AlarmSeverity.Critical, AlarmState.Active, temp).ConfigureAwait(false);
            }
        }
        else if (temp > 60.0)
        {
            _activeAlarms.Remove(criticalKey);
            if (_activeAlarms.Add(warningKey))
            {
                _logger.LogWarning("温度 {Value:F1}°C 超过 Warning 阈值 60°C [{Tag}]", temp, tagName);
                await RaiseAlarmAsync(tagName, "Temperature High Warning",
                    AlarmSeverity.Warning, AlarmState.Active, temp).ConfigureAwait(false);
            }
        }
        else
        {
            // 温度恢复正常：若此前存在活跃温度报警则发出 Cleared 通知
            var hadCritical = _activeAlarms.Remove(criticalKey);
            var hadWarning = _activeAlarms.Remove(warningKey);
            if (hadCritical || hadWarning)
            {
                _logger.LogInformation("温度 {Value:F1}°C 恢复正常，清除温度报警 [{Tag}]", temp, tagName);
                await RaiseAlarmAsync(tagName, "Temperature High Alarm",
                    AlarmSeverity.Info, AlarmState.Cleared, temp).ConfigureAwait(false);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  Pressure: >8 Critical, >6 Warning, <=6 Clear
    // ─────────────────────────────────────────────

    private async Task ProcessPressureAsync(string tagName, object? value)
    {
        if (!TryConvertToDouble(value, out var pressure))
            return;

        var criticalKey = $"{tagName}:PressureHigh:Critical";
        var warningKey = $"{tagName}:PressureHigh:Warning";

        if (pressure > 8.0)
        {
            _activeAlarms.Remove(warningKey);
            if (_activeAlarms.Add(criticalKey))
            {
                _logger.LogWarning("压力 {Value:F2} bar 超过 Critical 阈值 8 bar [{Tag}]", pressure, tagName);
                await RaiseAlarmAsync(tagName, "Pressure High Alarm",
                    AlarmSeverity.Critical, AlarmState.Active, pressure).ConfigureAwait(false);
            }
        }
        else if (pressure > 6.0)
        {
            _activeAlarms.Remove(criticalKey);
            if (_activeAlarms.Add(warningKey))
            {
                _logger.LogWarning("压力 {Value:F2} bar 超过 Warning 阈值 6 bar [{Tag}]", pressure, tagName);
                await RaiseAlarmAsync(tagName, "Pressure High Warning",
                    AlarmSeverity.Warning, AlarmState.Active, pressure).ConfigureAwait(false);
            }
        }
        else
        {
            var hadCritical = _activeAlarms.Remove(criticalKey);
            var hadWarning = _activeAlarms.Remove(warningKey);
            if (hadCritical || hadWarning)
            {
                _logger.LogInformation("压力 {Value:F2} bar 恢复正常，清除压力报警 [{Tag}]", pressure, tagName);
                await RaiseAlarmAsync(tagName, "Pressure High Alarm",
                    AlarmSeverity.Info, AlarmState.Cleared, pressure).ConfigureAwait(false);
            }
        }
    }

    // ─────────────────────────────────────────────
    //  MotorState: false→true 触发 Info 报警
    // ─────────────────────────────────────────────

    private async Task ProcessMotorStateAsync(string tagName, object? value)
    {
        var motorOn = value is true;

        if (!_previousMotorState.TryGetValue(tagName, out var previousState))
            previousState = false;

        _previousMotorState[tagName] = motorOn;

        if (motorOn && !previousState)
        {
            _logger.LogInformation("电机启动 [{Tag}]", tagName);
            await RaiseAlarmAsync(tagName, "Motor Started",
                AlarmSeverity.Info, AlarmState.Active, value).ConfigureAwait(false);
        }
    }

    // ─────────────────────────────────────────────
    //  公共辅助方法
    // ─────────────────────────────────────────────

    /// <summary>
    /// 构造报警实体并通过 <see cref="IAlarmService"/> 提交，内部包含异常保护。
    /// </summary>
    private async Task RaiseAlarmAsync(
        string tagName,
        string message,
        AlarmSeverity severity,
        AlarmState state,
        object? triggerValue)
    {
        try
        {
            var alarm = new AlarmEntity
            {
                TagName = tagName,
                Message = message,
                Severity = severity,
                State = state,
                TriggerValue = triggerValue,
                TriggeredAt = DateTime.UtcNow
            };

            await _alarmService.RaiseAlarmAsync(alarm).ConfigureAwait(false);

            _logger.LogInformation(
                "报警已提交: {Message} | Tag={TagName} | Severity={Severity} | State={State} | Value={Value}",
                message, tagName, severity, state, triggerValue);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "提交报警失败: {Message} (Tag={TagName})。", message, tagName);
        }
    }

    /// <summary>
    /// 尝试将 <see cref="object"/> 类型的值转换为 <see cref="double"/>。
    /// </summary>
    private static bool TryConvertToDouble(object? value, out double result)
    {
        switch (value)
        {
            case double d:
                result = d;
                return true;
            case float f:
                result = f;
                return true;
            case int i:
                result = i;
                return true;
            case long l:
                result = l;
                return true;
            case short s:
                result = s;
                return true;
            case decimal m:
                result = (double)m;
                return true;
            case string str when double.TryParse(str, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0.0;
                return false;
        }
    }

    /// <summary>
    /// 释放 Rx 订阅资源。
    /// </summary>
    public void Dispose()
    {
        _subscription?.Dispose();
        _subscription = null;
        _logger.LogInformation("DemoAlarmGenerator 已释放。");
    }
}

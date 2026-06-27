using IndustrialSCADA.Core.Enums;

namespace IndustrialSCADA.Core.Entities;

/// <summary>
/// 报警实体
/// </summary>
public class AlarmEntity : EntityBase
{
    /// <summary>关联的标签名</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>报警消息</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>报警严重程度</summary>
    public AlarmSeverity Severity { get; set; }

    /// <summary>当前报警状态</summary>
    public AlarmState State { get; set; } = AlarmState.Active;

    /// <summary>触发时的值</summary>
    public object? TriggerValue { get; set; }

    /// <summary>触发时间</summary>
    public DateTime TriggeredAt { get; set; } = DateTime.UtcNow;

    /// <summary>确认时间</summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>恢复时间</summary>
    public DateTime? ClearedAt { get; set; }
}

namespace IndustrialSCADA.Core.Enums;

/// <summary>
/// 报警状态
/// </summary>
public enum AlarmState
{
    /// <summary>活跃</summary>
    Active = 0,
    /// <summary>已确认</summary>
    Acknowledged = 1,
    /// <summary>已恢复</summary>
    Cleared = 2,
    /// <summary>已抑制</summary>
    Suppressed = 3
}

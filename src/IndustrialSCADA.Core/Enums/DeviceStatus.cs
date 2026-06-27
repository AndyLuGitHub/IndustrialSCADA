namespace IndustrialSCADA.Core.Enums;

/// <summary>
/// 设备运行状态枚举
/// </summary>
public enum DeviceStatus
{
    /// <summary>未知状态</summary>
    Unknown = 0,
    /// <summary>在线</summary>
    Online = 1,
    /// <summary>离线</summary>
    Offline = 2,
    /// <summary>故障</summary>
    Fault = 3,
    /// <summary>维护中</summary>
    Maintenance = 4
}

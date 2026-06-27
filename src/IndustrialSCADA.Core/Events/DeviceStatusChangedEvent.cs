using IndustrialSCADA.Core.Enums;

namespace IndustrialSCADA.Core.Events;

/// <summary>
/// 设备状态变化事件，当某台设备的运行状态发生切换时发布。
/// 在 App/Infrastructure 层可包装为 Prism EventAggregator 事件使用。
/// </summary>
public class DeviceStatusChangedEvent
{
    /// <summary>
    /// 初始化 <see cref="DeviceStatusChangedEvent"/> 的新实例。
    /// </summary>
    /// <param name="deviceCode">设备编码。</param>
    /// <param name="oldStatus">变更前的状态。</param>
    /// <param name="newStatus">变更后的状态。</param>
    public DeviceStatusChangedEvent(string deviceCode, DeviceStatus oldStatus, DeviceStatus newStatus)
    {
        DeviceCode = deviceCode;
        OldStatus = oldStatus;
        NewStatus = newStatus;
    }

    /// <summary>
    /// 发生状态变化的设备编码。
    /// </summary>
    public string DeviceCode { get; }

    /// <summary>
    /// 变更前的设备状态。
    /// </summary>
    public DeviceStatus OldStatus { get; }

    /// <summary>
    /// 变更后的设备状态。
    /// </summary>
    public DeviceStatus NewStatus { get; }
}

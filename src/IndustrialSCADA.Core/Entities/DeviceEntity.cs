using IndustrialSCADA.Core.Enums;

namespace IndustrialSCADA.Core.Entities;

/// <summary>
/// 设备实体，表示一个可通信的工业设备
/// </summary>
public class DeviceEntity : EntityBase
{
    /// <summary>设备名称</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>设备描述</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>使用的通信协议</summary>
    public ProtocolType ProtocolType { get; set; }

    /// <summary>连接地址（IP 或端口号）</summary>
    public string ConnectionAddress { get; set; } = string.Empty;

    /// <summary>当前设备状态</summary>
    public DeviceStatus Status { get; set; } = DeviceStatus.Unknown;

    /// <summary>最后通信时间</summary>
    public DateTime? LastCommunicationAt { get; set; }

    /// <summary>扫描间隔（毫秒）</summary>
    public int ScanIntervalMs { get; set; } = 1000;

    /// <summary>是否启用</summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>该设备下的数据点列表</summary>
    public List<DataPoint> DataPoints { get; set; } = new();
}

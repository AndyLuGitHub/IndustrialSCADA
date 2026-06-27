using IndustrialSCADA.Core.Enums;

namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 连接配置类，封装与设备建立通信连接所需的全部参数。
/// </summary>
public class ConnectionConfig
{
    /// <summary>
    /// 目标主机地址（IP 地址或主机名）。
    /// </summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>
    /// 目标端口号，默认值取决于协议类型。
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// 连接超时时间（毫秒），默认 5000ms。
    /// </summary>
    public int Timeout { get; set; } = 5000;

    /// <summary>
    /// 所使用的通信协议类型。
    /// </summary>
    public ProtocolType ProtocolType { get; set; }

    /// <summary>
    /// 扩展参数字典，用于存放协议特有的配置项
    /// （如 Modbus 从站地址、S7 机架号/槽号等）。
    /// </summary>
    public Dictionary<string, object> Parameters { get; set; } = new();
}

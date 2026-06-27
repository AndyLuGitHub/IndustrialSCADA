using IndustrialSCADA.Core.Enums;

namespace IndustrialSCADA.Core.Entities;

/// <summary>
/// 数据点定义，绑定到某个设备的某个寄存器地址
/// </summary>
public class DataPoint : EntityBase
{
    /// <summary>所属设备 ID</summary>
    public long DeviceId { get; set; }

    /// <summary>标签名（唯一标识）</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>寄存器地址（如 DB1.DBW0、40001）</summary>
    public string Address { get; set; } = string.Empty;

    /// <summary>数据类型</summary>
    public DataPointType PointType { get; set; }

    /// <summary>工程单位（℃、bar 等）</summary>
    public string Unit { get; set; } = string.Empty;

    /// <summary>缩放系数</summary>
    public double ScaleFactor { get; set; } = 1.0;

    /// <summary>偏移量</summary>
    public double Offset { get; set; } = 0.0;

    /// <summary>报警上限</summary>
    public double? HighLimit { get; set; }

    /// <summary>报警下限</summary>
    public double? LowLimit { get; set; }

    /// <summary>当前值（object 以支持多类型）</summary>
    public object? CurrentValue { get; set; }

    /// <summary>数据质量（0=Bad, 192=Good）</summary>
    public byte Quality { get; set; } = 192;
}

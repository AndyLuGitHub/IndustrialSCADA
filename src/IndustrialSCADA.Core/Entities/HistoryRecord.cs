namespace IndustrialSCADA.Core.Entities;

/// <summary>
/// 历史记录，用于持久化采集数据
/// </summary>
public class HistoryRecord : EntityBase
{
    /// <summary>标签名</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>记录值</summary>
    public object? Value { get; set; }

    /// <summary>数据质量</summary>
    public byte Quality { get; set; }

    /// <summary>时间戳</summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

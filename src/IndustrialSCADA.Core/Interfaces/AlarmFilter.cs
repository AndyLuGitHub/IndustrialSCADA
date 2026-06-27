using IndustrialSCADA.Core.Enums;

namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 报警过滤条件，用于在查询报警记录时筛选结果并支持分页。
/// </summary>
public class AlarmFilter
{
    /// <summary>
    /// 按设备编码过滤，为 null 或空表示不过滤。
    /// </summary>
    public string? DeviceCode { get; set; }

    /// <summary>
    /// 按报警严重等级过滤，为 null 表示不过滤。
    /// </summary>
    public AlarmSeverity? Severity { get; set; }

    /// <summary>
    /// 按报警状态过滤，为 null 表示不过滤。
    /// </summary>
    public AlarmState? State { get; set; }

    /// <summary>
    /// 时间范围起始（UTC），为 null 表示不限起始。
    /// </summary>
    public DateTime? TimeFrom { get; set; }

    /// <summary>
    /// 时间范围结束（UTC），为 null 表示不限结束。
    /// </summary>
    public DateTime? TimeTo { get; set; }

    /// <summary>
    /// 页码，从 1 开始，默认第 1 页。
    /// </summary>
    public int Page { get; set; } = 1;

    /// <summary>
    /// 每页大小，默认 50 条。
    /// </summary>
    public int PageSize { get; set; } = 50;
}

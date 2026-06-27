using IndustrialSCADA.Core.Entities;

namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 历史数据存储接口，负责采集数据的持久化和历史查询。
/// </summary>
public interface IHistoryRepository
{
    /// <summary>
    /// 批量保存历史记录。
    /// </summary>
    /// <param name="records">要保存的历史记录集合。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task SaveAsync(IEnumerable<HistoryRecord> records);

    /// <summary>
    /// 按标签名称和时间范围查询历史记录。
    /// </summary>
    /// <param name="tagName">标签名称。</param>
    /// <param name="from">起始时间（UTC，含）。</param>
    /// <param name="to">结束时间（UTC，含）。</param>
    /// <returns>符合条件的历史记录列表。</returns>
    Task<IReadOnlyList<HistoryRecord>> QueryAsync(string tagName, DateTime from, DateTime to);

    /// <summary>
    /// 列出系统中所有已存储的标签名称。
    /// </summary>
    /// <returns>标签名称列表。</returns>
    Task<IReadOnlyList<string>> ListTagsAsync();
}

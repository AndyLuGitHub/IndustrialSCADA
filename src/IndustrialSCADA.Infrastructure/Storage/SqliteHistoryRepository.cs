using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSCADA.Infrastructure.Storage;

/// <summary>
/// 基于 SQLite + EF Core 的历史数据仓储实现。
/// 将 <see cref="HistoryRecord"/> 映射为 <see cref="HistoryRecordEntity"/> 进行持久化。
/// </summary>
public sealed class SqliteHistoryRepository : IHistoryRepository
{
    private readonly ScadaDbContext _dbContext;

    /// <summary>
    /// 初始化 <see cref="SqliteHistoryRepository"/> 的新实例。
    /// </summary>
    /// <param name="dbContext">SCADA 数据库上下文。</param>
    public SqliteHistoryRepository(ScadaDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    /// <inheritdoc />
    public async Task SaveAsync(IEnumerable<HistoryRecord> records)
    {
        var entities = records.Select(r => new HistoryRecordEntity
        {
            TagName = r.TagName,
            Value = Convert.ToDouble(r.Value ?? 0.0),
            Quality = r.Quality >= 192, // 192 = Good
            Timestamp = r.Timestamp,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        }).ToList();

        _dbContext.HistoryRecords.AddRange(entities);
        await _dbContext.SaveChangesAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<HistoryRecord>> QueryAsync(string tagName, DateTime from, DateTime to)
    {
        var entities = await _dbContext.HistoryRecords
            .AsNoTracking()
            .Where(e => e.TagName == tagName && e.Timestamp >= from && e.Timestamp <= to)
            .OrderBy(e => e.Timestamp)
            .ToListAsync()
            .ConfigureAwait(false);

        return entities.Select(e => new HistoryRecord
        {
            Id = e.Id,
            TagName = e.TagName,
            Value = e.Value,
            Quality = e.Quality ? (byte)192 : (byte)0,
            Timestamp = e.Timestamp,
            CreatedAt = e.CreatedAt,
            UpdatedAt = e.UpdatedAt
        }).ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> ListTagsAsync()
    {
        return await _dbContext.HistoryRecords
            .AsNoTracking()
            .Select(e => e.TagName)
            .Distinct()
            .OrderBy(t => t)
            .ToListAsync()
            .ConfigureAwait(false);
    }
}

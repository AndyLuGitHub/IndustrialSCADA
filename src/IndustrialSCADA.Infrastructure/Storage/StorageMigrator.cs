using Microsoft.EntityFrameworkCore;

namespace IndustrialSCADA.Infrastructure.Storage;

/// <summary>
/// 数据库迁移辅助类，负责在应用启动时自动创建或更新数据库结构。
/// </summary>
public static class StorageMigrator
{
    /// <summary>
    /// 执行数据库迁移，确保表结构和索引已创建。
    /// 使用 <see cref="DbContext.Database.EnsureCreatedAsync"/> 方式（适用于 SQLite 开发环境）。
    /// </summary>
    /// <param name="dbContext">SCADA 数据库上下文。</param>
    /// <returns>表示异步操作的任务。</returns>
    public static async Task MigrateAsync(ScadaDbContext dbContext)
    {
        await dbContext.Database.EnsureCreatedAsync().ConfigureAwait(false);
    }
}

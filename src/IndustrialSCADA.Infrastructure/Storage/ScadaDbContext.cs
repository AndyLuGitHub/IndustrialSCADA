using IndustrialSCADA.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace IndustrialSCADA.Infrastructure.Storage;

/// <summary>
/// 历史记录的 EF Core 实体映射类型，用于数据库持久化。
/// 将 <see cref="HistoryRecord"/> 的值类型字段适配为适合 SQLite 存储的形式。
/// </summary>
public class HistoryRecordEntity : EntityBase
{
    /// <summary>标签名。</summary>
    public string TagName { get; set; } = string.Empty;

    /// <summary>记录值（统一用 double 存储）。</summary>
    public double Value { get; set; }

    /// <summary>数据质量标识（true = Good）。</summary>
    public bool Quality { get; set; }

    /// <summary>时间戳（UTC）。</summary>
    public DateTime Timestamp { get; set; }
}

/// <summary>
/// SCADA 系统 EF Core 数据库上下文，管理历史记录、报警和设备实体的持久化。
/// </summary>
public class ScadaDbContext : DbContext
{
    /// <summary>
    /// 初始化 <see cref="ScadaDbContext"/> 的新实例。
    /// </summary>
    /// <param name="options">数据库上下文选项。</param>
    public ScadaDbContext(DbContextOptions<ScadaDbContext> options)
        : base(options)
    {
    }

    /// <summary>历史记录表。</summary>
    public DbSet<HistoryRecordEntity> HistoryRecords => Set<HistoryRecordEntity>();

    /// <summary>报警记录表。</summary>
    public DbSet<AlarmEntity> Alarms => Set<AlarmEntity>();

    /// <summary>设备配置表。</summary>
    public DbSet<DeviceEntity> Devices => Set<DeviceEntity>();

    /// <summary>
    /// 配置模型，创建必要的索引以提升查询性能。
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // HistoryRecordEntity: 按 (TagName, Timestamp) 复合索引，加速历史查询
        modelBuilder.Entity<HistoryRecordEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.TagName, e.Timestamp });
            entity.Property(e => e.Timestamp).IsRequired();
            entity.Property(e => e.TagName).IsRequired().HasMaxLength(256);
        });

        // AlarmEntity: 按 TriggeredAt 索引，加速时间范围查询
        modelBuilder.Entity<AlarmEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.TriggeredAt);
            entity.Property(e => e.TagName).IsRequired().HasMaxLength(256);
            entity.Property(e => e.Message).HasMaxLength(1024);
            // TriggerValue is typed as object?, which EF Core cannot map to a column.
            // No code currently reads/writes this property, so ignoring it is safe.
            entity.Ignore(e => e.TriggerValue);
        });

        // DeviceEntity: 按 Name 建索引
        modelBuilder.Entity<DeviceEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.Name);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(256);
            entity.Ignore(e => e.DataPoints);
        });
    }
}

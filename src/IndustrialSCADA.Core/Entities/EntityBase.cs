namespace IndustrialSCADA.Core.Entities;

/// <summary>
/// 所有实体的抽象基类
/// </summary>
public abstract class EntityBase
{
    /// <summary>唯一标识</summary>
    public long Id { get; set; }

    /// <summary>创建时间</summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>最后更新时间</summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

namespace IndustrialSCADA.Core.Enums;

/// <summary>
/// 数据点类型
/// </summary>
public enum DataPointType
{
    /// <summary>布尔量</summary>
    Bool = 0,
    /// <summary>16位整数</summary>
    Int16 = 1,
    /// <summary>32位整数</summary>
    Int32 = 2,
    /// <summary>32位浮点</summary>
    Float = 3,
    /// <summary>64位浮点</summary>
    Double = 4,
    /// <summary>字符串</summary>
    String = 5
}

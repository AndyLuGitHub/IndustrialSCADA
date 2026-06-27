using System.Collections.Concurrent;

namespace IndustrialSCADA.Infrastructure.Communication.Simulator;

/// <summary>
/// 全局静态模拟数据存储，供所有 <see cref="SimulatorProtocolAdapter"/> 实例共享。
/// 使用 <see cref="ConcurrentDictionary{TKey,TValue}"/> 保证线程安全。
/// </summary>
public static class SimulatorDataStore
{
    /// <summary>全局共享的模拟寄存器字典。</summary>
    private static readonly ConcurrentDictionary<string, object> _registers = new();

    /// <summary>
    /// 获取或设置指定地址的寄存器值。
    /// </summary>
    /// <param name="address">寄存器地址（键名）。</param>
    /// <returns>寄存器值；若不存在返回 null。</returns>
    public static object? GetOrDefault(string address)
    {
        _registers.TryGetValue(address, out var value);
        return value;
    }

    /// <summary>
    /// 设置指定地址的寄存器值。
    /// </summary>
    /// <param name="address">寄存器地址（键名）。</param>
    /// <param name="value">要写入的值。</param>
    public static void Set(string address, object value)
    {
        _registers[address] = value;
    }

    /// <summary>
    /// 尝试获取指定地址的寄存器值。
    /// </summary>
    /// <param name="address">寄存器地址（键名）。</param>
    /// <param name="value">输出值。</param>
    /// <returns>若存在返回 true，否则返回 false。</returns>
    public static bool TryGet(string address, out object? value)
    {
        return _registers.TryGetValue(address, out value);
    }

    /// <summary>
    /// 清除所有寄存器数据。
    /// </summary>
    public static void Clear()
    {
        _registers.Clear();
    }
}

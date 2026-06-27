using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.Infrastructure.Communication.Simulator;

/// <summary>
/// 内置模拟器协议适配器，用内存字典模拟寄存器读写。
/// 适用于开发调试和演示场景，无需连接真实硬件。
/// 使用全局 <see cref="SimulatorDataStore"/> 共享数据，支持跨实例访问。
/// </summary>
public sealed class SimulatorProtocolAdapter : ProtocolAdapterBase
{
    /// <summary>
    /// 初始化 <see cref="SimulatorProtocolAdapter"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public SimulatorProtocolAdapter(ILogger<SimulatorProtocolAdapter> logger)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override string ProtocolName => "Simulator";

    /// <inheritdoc />
    public override ProtocolType ProtocolType => ProtocolType.Simulator;

    /// <summary>
    /// 模拟连接，直接标记为已连接。
    /// </summary>
    protected override Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct)
    {
        Logger.LogInformation("[Simulator] 模拟连接已建立");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 模拟断开连接。
    /// </summary>
    protected override Task DisconnectCoreAsync()
    {
        Logger.LogInformation("[Simulator] 模拟连接已断开");
        return Task.CompletedTask;
    }

    /// <summary>
    /// 从全局模拟寄存器中读取值。
    /// </summary>
    /// <typeparam name="T">期望的数据类型。</typeparam>
    /// <param name="address">寄存器地址（即键名）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>读取到的值，若不存在返回 default。</returns>
    protected override Task<T> ReadCoreAsync<T>(string address, CancellationToken ct)
    {
        try
        {
            if (SimulatorDataStore.TryGet(address, out var value) && value != null)
            {
                if (value is T typed)
                    return Task.FromResult(typed);

                var converted = (T)Convert.ChangeType(value, typeof(T));
                return Task.FromResult(converted);
            }

            return Task.FromResult(default(T)!);
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[Simulator] 读取地址 {Address} 类型转换失败", address);
            return Task.FromResult(default(T)!);
        }
    }

    /// <summary>
    /// 向全局模拟寄存器写入值。
    /// </summary>
    protected override Task WriteCoreAsync<T>(string address, T value, CancellationToken ct)
    {
        try
        {
            SimulatorDataStore.Set(address, value!);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "[Simulator] 写入地址 {Address} 失败", address);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 内部方法：直接设置寄存器值（供 PlcSimulator 等外部组件使用）。
    /// </summary>
    /// <param name="address">寄存器地址。</param>
    /// <param name="value">值。</param>
    public void SetRegister(string address, object value)
    {
        SimulatorDataStore.Set(address, value);
    }
}

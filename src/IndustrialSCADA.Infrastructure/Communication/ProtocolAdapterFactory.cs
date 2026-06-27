using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using IndustrialSCADA.Infrastructure.Communication.DL645;
using IndustrialSCADA.Infrastructure.Communication.EtherCat;
using IndustrialSCADA.Infrastructure.Communication.IEC104;
using IndustrialSCADA.Infrastructure.Communication.Modbus;
using IndustrialSCADA.Infrastructure.Communication.OpcUa;
using IndustrialSCADA.Infrastructure.Communication.S7;
using IndustrialSCADA.Infrastructure.Communication.Simulator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.Infrastructure.Communication;

/// <summary>
/// 协议适配器工厂接口，根据协议类型创建对应的 <see cref="IProtocolAdapter"/> 实例。
/// </summary>
public interface IProtocolAdapterFactory
{
    /// <summary>
    /// 根据指定的协议类型创建协议适配器实例。
    /// </summary>
    /// <param name="type">协议类型枚举值。</param>
    /// <returns>对应的协议适配器。</returns>
    IProtocolAdapter Create(ProtocolType type);
}

/// <summary>
/// 协议适配器工厂默认实现，根据 <see cref="ProtocolType"/> 创建具体的协议适配器。
/// 对于 <see cref="ProtocolType.Simulator"/> 返回 DI 容器中注册的单例实例。
/// </summary>
public sealed class ProtocolAdapterFactory : IProtocolAdapterFactory
{
    private readonly IServiceProvider _serviceProvider;

    /// <summary>
    /// 初始化 <see cref="ProtocolAdapterFactory"/> 的新实例。
    /// </summary>
    /// <param name="serviceProvider">DI 服务提供程序。</param>
    public ProtocolAdapterFactory(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc />
    public IProtocolAdapter Create(ProtocolType type)
    {
        return type switch
        {
            ProtocolType.S7 => new S7ProtocolAdapter(
                _serviceProvider.GetRequiredService<ILogger<S7ProtocolAdapter>>()),

            ProtocolType.ModbusTcp => new ModbusProtocolAdapter(
                _serviceProvider.GetRequiredService<ILogger<ModbusProtocolAdapter>>()),

            ProtocolType.OpcUa => new OpcUaProtocolAdapter(
                _serviceProvider.GetRequiredService<ILogger<OpcUaProtocolAdapter>>()),

            ProtocolType.EtherCat => new EtherCatProtocolAdapter(
                _serviceProvider.GetRequiredService<ILogger<EtherCatProtocolAdapter>>()),

            ProtocolType.IEC104 => new IEC104ProtocolAdapter(
                _serviceProvider.GetRequiredService<ILogger<IEC104ProtocolAdapter>>()),

            ProtocolType.DL645 => new DL645ProtocolAdapter(
                _serviceProvider.GetRequiredService<ILogger<DL645ProtocolAdapter>>()),

            ProtocolType.Simulator => _serviceProvider.GetRequiredService<SimulatorProtocolAdapter>(),

            _ => throw new ArgumentOutOfRangeException(nameof(type), type, $"不支持的协议类型: {type}")
        };
    }
}

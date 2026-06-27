using IndustrialSCADA.Core.Interfaces;
using IndustrialSCADA.DeviceCore.Simulator;
using IndustrialSCADA.Infrastructure.Communication.Simulator;
using Microsoft.Extensions.Logging;
using Prism.Ioc;

namespace IndustrialSCADA.DeviceCore;

/// <summary>
/// DeviceCore 层 DI 注册扩展方法，将设备管理、数据采集、报警服务和模拟器注册到 Prism 容器。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 DeviceCore 层服务（设备管理器、数据采集器、报警服务、PLC 模拟器）到 DI 容器。
    /// </summary>
    /// <param name="containerRegistry">Prism 容器注册器。</param>
    public static void AddDeviceCore(this IContainerRegistry containerRegistry)
    {
        // 注册设备管理器 (Singleton)
        containerRegistry.RegisterSingleton<IDeviceManager, DeviceManager>();

        // 注册数据采集器 (Singleton)
        containerRegistry.RegisterSingleton<IDataCollector, DataCollector>();

        // 注册报警服务 (Singleton)
        containerRegistry.RegisterSingleton<IAlarmService, AlarmService>();

        // SimulatorProtocolAdapter 已在 Infrastructure 层注册为单例，此处无需重复注册

        // 注册 PLC 模拟器 (Singleton)
        containerRegistry.RegisterSingleton<PlcSimulator>();

        // 注册历史数据桥接服务 (Singleton)
        containerRegistry.RegisterSingleton<HistoryDataBridge>();

        // 注册报警持久化桥接 (Singleton) —— 订阅报警流并写入 SQLite
        containerRegistry.RegisterSingleton<AlarmPersistenceBridge>();

        // 注册演示报警生成器 (Singleton) —— 订阅数据流，阈值越限时自动生成报警
        containerRegistry.RegisterSingleton<DemoAlarmGenerator>();
    }
}

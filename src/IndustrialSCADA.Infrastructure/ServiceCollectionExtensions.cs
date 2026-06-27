using IndustrialSCADA.Core.Interfaces;
using IndustrialSCADA.Infrastructure.Communication;
using IndustrialSCADA.Infrastructure.Communication.Simulator;
using IndustrialSCADA.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Prism.Ioc;

namespace IndustrialSCADA.Infrastructure;

/// <summary>
/// Infrastructure 层 DI 注册扩展方法，将协议适配器、存储服务等注册到 Prism 容器。
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// 注册 Infrastructure 层服务（协议适配器、存储、日志、模拟器适配器）到 DI 容器。
    /// </summary>
    /// <param name="containerRegistry">Prism 容器注册器。</param>
    /// <param name="connectionString">SQLite 数据库连接字符串。</param>
    public static void AddInfrastructure(this IContainerRegistry containerRegistry, string connectionString)
    {
        // 注册 EF Core DbContext（使用 SQLite）
        containerRegistry.RegisterInstance<DbContextOptions<ScadaDbContext>>(
            new DbContextOptionsBuilder<ScadaDbContext>()
                .UseSqlite(connectionString)
                .Options);

        containerRegistry.Register<ScadaDbContext>();

        // 注册历史数据仓储
        containerRegistry.RegisterSingleton<IHistoryRepository, SqliteHistoryRepository>();

        // 注册模拟器协议适配器 (Singleton)，供工厂和 PlcSimulator 共享同一实例
        containerRegistry.RegisterSingleton<SimulatorProtocolAdapter>();

        // 注册协议适配器工厂
        containerRegistry.RegisterSingleton<IProtocolAdapterFactory, ProtocolAdapterFactory>();
    }
}

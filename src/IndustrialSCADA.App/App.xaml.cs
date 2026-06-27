using System;
using System.Collections.Generic;
using System.Windows;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using IndustrialSCADA.Infrastructure;
using IndustrialSCADA.Infrastructure.Communication;
using IndustrialSCADA.Infrastructure.Logging;
using IndustrialSCADA.Infrastructure.Storage;
using IndustrialSCADA.DeviceCore;
using IndustrialSCADA.DeviceCore.Simulator;
using IndustrialSCADA.Module.Dashboard;
using IndustrialSCADA.Module.DeviceControl;
using IndustrialSCADA.Module.DataAcquisition;
using IndustrialSCADA.Module.AlarmManagement;
using IndustrialSCADA.Module.LogViewer;
using Microsoft.Extensions.Logging;
using Prism.Ioc;
using Prism.Modularity;
using IndustrialSCADA.App.Services;

namespace IndustrialSCADA.App;

/// <summary>
/// Industrial SCADA application shell, built on Prism + MaterialDesign.
/// </summary>
public partial class App
{
    /// <inheritdoc />
    protected override Window CreateShell()
    {
        return Container.Resolve<MainWindow>();
    }

    /// <inheritdoc />
    protected override void RegisterTypes(IContainerRegistry containerRegistry)
    {
        System.Diagnostics.Debug.WriteLine("[App] RegisterTypes BEGIN");

        // 1. Logging: configure Serilog as the global logger
        SerilogSetup.CreateLogger();

        // 2. Register ILogger<> open generic for Microsoft.Extensions.Logging consumers
        //    (DataCollector, DeviceManager, AlarmService, protocol adapters, etc.)
        containerRegistry.RegisterSingleton(typeof(ILogger<>), typeof(SerilogLoggerAdapter<>));

        // 3. Infrastructure (protocol adapters, EF Core, storage)
        var dbPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scada.db");
        containerRegistry.AddInfrastructure($"Data Source={dbPath}");

        // 4. Device core (device manager, data collector, alarm service, PLC simulator)
        containerRegistry.AddDeviceCore();

        // 5. Navigation service
        containerRegistry.RegisterSingleton<INavigationService, NavigationService>();

        // 6. Main window shell (must be last so all other types are registered first)
        containerRegistry.RegisterSingleton<MainWindow>();

        System.Diagnostics.Debug.WriteLine("[App] RegisterTypes END");
    }

    /// <inheritdoc />
    protected override void ConfigureModuleCatalog(IModuleCatalog moduleCatalog)
    {
        moduleCatalog.AddModule<DashboardModule>(InitializationMode.WhenAvailable);
        moduleCatalog.AddModule<DeviceControlModule>(InitializationMode.WhenAvailable);
        moduleCatalog.AddModule<DataAcquisitionModule>(InitializationMode.WhenAvailable);
        moduleCatalog.AddModule<AlarmManagementModule>(InitializationMode.WhenAvailable);
        moduleCatalog.AddModule<LogViewerModule>(InitializationMode.WhenAvailable);
    }

    /// <inheritdoc />
    protected override void InitializeModules()
    {
        System.Diagnostics.Debug.WriteLine("[App] InitializeModules BEGIN");
        base.InitializeModules();
        System.Diagnostics.Debug.WriteLine("[App] InitializeModules END");
        // 视图导航由 MainWindow.Loaded 事件触发，使用 Prism Region + RequestNavigate。
    }

    /// <inheritdoc />
    protected override async void OnInitialized()
    {
        base.OnInitialized();
        System.Diagnostics.Debug.WriteLine("[App] OnInitialized BEGIN");

        try
        {
            // 0. 确保数据库初始化
            var dbCtx = Container.Resolve<ScadaDbContext>();
            await dbCtx.Database.EnsureCreatedAsync();

            // 1. 注册演示设备（使用 Simulator 协议）
            var deviceManager = Container.Resolve<IDeviceManager>();
            var demoDevice = new DeviceEntity
            {
                Id = 1,
                Name = "Demo-PLC-001",
                Description = "演示用 PLC 模拟器",
                ProtocolType = ProtocolType.Simulator,
                ConnectionAddress = "localhost",
                ScanIntervalMs = 200,
                IsEnabled = true,
                Status = DeviceStatus.Unknown,
                DataPoints = new List<DataPoint>
                {
                    new() { Id = 1, DeviceId = 1, TagName = "Temperature", Address = "Temperature",
                            PointType = DataPointType.Double, Unit = "°C", HighLimit = 75, LowLimit = 25 },
                    new() { Id = 2, DeviceId = 1, TagName = "Pressure", Address = "Pressure",
                            PointType = DataPointType.Double, Unit = "bar", HighLimit = 8 },
                    new() { Id = 3, DeviceId = 1, TagName = "MotorState", Address = "MotorState",
                            PointType = DataPointType.Bool },
                    new() { Id = 4, DeviceId = 1, TagName = "Counter", Address = "Counter",
                            PointType = DataPointType.Int32 }
                }
            };
            await deviceManager.AddDeviceAsync(demoDevice);
            System.Diagnostics.Debug.WriteLine("[App] Demo device registered");

            // 2. 连接模拟器设备
            var adapter = deviceManager.GetProtocolAdapter("Demo-PLC-001");
            await adapter.ConnectAsync(new ConnectionConfig
            {
                Host = "localhost",
                ProtocolType = ProtocolType.Simulator
            });
            System.Diagnostics.Debug.WriteLine("[App] Simulator connected");

            // 3. 启动 PlcSimulator
            var simulator = Container.Resolve<PlcSimulator>();
            simulator.Start();
            System.Diagnostics.Debug.WriteLine("[App] PlcSimulator started");

            // 4. 启动 DataCollector
            var dataCollector = Container.Resolve<IDataCollector>();
            await dataCollector.StartAsync();
            System.Diagnostics.Debug.WriteLine("[App] DataCollector started");

            // 5. 启动历史数据持久化桥接（Rx DataStream → SQLite 批量写入）
            var historyBridge = Container.Resolve<HistoryDataBridge>();
            historyBridge.Start();
            System.Diagnostics.Debug.WriteLine("[App] HistoryDataBridge started");

            // 6. 启动报警持久化桥接（AlarmStream → SQLite）
            var alarmBridge = Container.Resolve<AlarmPersistenceBridge>();
            alarmBridge.Start();
            System.Diagnostics.Debug.WriteLine("[App] AlarmPersistenceBridge started");

            // 7. 启动模拟报警生成器（基于采集数据阈值触发报警）
            var demoAlarms = Container.Resolve<DemoAlarmGenerator>();
            demoAlarms.Start();
            System.Diagnostics.Debug.WriteLine("[App] DemoAlarmGenerator started");

            System.Diagnostics.Debug.WriteLine("[App] OnInitialized OK");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[App.OnInitialized] EXCEPTION: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"[App.OnInitialized] Stack: {ex.StackTrace}");
        }
    }
}

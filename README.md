# IndustrialSCADA - 工业自动化上位机监控系统

基于 WPF + Prism 9 构建的工业 SCADA（数据采集与监视控制）系统，支持多协议设备接入、实时数据采集、报警管理、历史数据存储和可视化监控。

## 技术栈

- **框架**: .NET 8 LTS + WPF + XAML
- **模块化**: Prism 9.0.537 + DryIoc (Region 导航)
- **MVVM**: CommunityToolkit.Mvvm 8.3.2
- **UI 库**: MaterialDesignThemes 5.1.0 (深色主题)
- **图表**: LiveCharts2 (SkiaSharp)
- **持久化**: EF Core 8 + SQLite
- **响应式**: System.Reactive (Rx.NET) 6.0.1
- **日志**: Serilog (文件 + 控制台)

## 项目结构

```
src/
├── IndustrialSCADA.App/                    # 应用入口 (Shell)
│   ├── App.xaml.cs                         # Prism 容器配置、模块注册、启动流程
│   ├── MainWindow.xaml                     # 主窗口 (Region 容器 + 状态栏)
│   └── Services/NavigationService.cs       # 导航服务封装
│
├── IndustrialSCADA.Core/                   # 核心抽象层
│   ├── Entities/                           # 领域实体 (Device, DataPoint, Alarm, HistoryRecord)
│   ├── Enums/                              # 枚举 (ProtocolType, DeviceStatus, AlarmSeverity 等)
│   └── Interfaces/                         # 接口契约 (IProtocolAdapter, IDeviceManager 等)
│
├── IndustrialSCADA.Infrastructure/         # 基础设施层
│   ├── Communication/                      # 7 种协议适配器
│   │   ├── S7/                             # Siemens S7 (S7netplus)
│   │   ├── Modbus/                         # Modbus TCP (FluentModbus)
│   │   ├── OpcUa/                          # OPC UA
│   │   ├── EtherCat/                       # Beckhoff EtherCAT/ADS (TwinCAT.Ads)
│   │   ├── IEC104/                         # IEC 60870-5-104 (电力系统)
│   │   ├── DL645/                          # DL/T 645-2007 (电能表)
│   │   └── Simulator/                      # 内置模拟器
│   ├── Storage/                            # EF Core DbContext + Repository
│   └── Logging/                            # Serilog 适配器
│
├── IndustrialSCADA.DeviceCore/             # 设备核心业务层
│   ├── DeviceManager.cs                    # 设备生命周期管理
│   ├── DataCollector.cs                    # Rx 数据采集引擎
│   ├── AlarmService.cs                     # 报警服务 (Rx AlarmStream)
│   ├── HistoryDataBridge.cs                # 历史数据持久化 (DataCollector → SQLite)
│   ├── AlarmPersistenceBridge.cs           # 报警持久化 (AlarmStream → SQLite)
│   └── Simulator/                          # 模拟器
│       ├── PlcSimulator.cs                 # PLC 数据模拟
│       └── DemoAlarmGenerator.cs           # 阈值报警生成
│
└── Modules/                                # Prism 功能模块
    ├── IndustrialSCADA.Module.Dashboard/   # 仪表盘 (设备概览、统计卡片)
    ├── IndustrialSCADA.Module.DeviceControl/   # 设备控制 (连接、读写、参数配置)
    ├── IndustrialSCADA.Module.DataAcquisition/ # 数据采集 (实时趋势、历史查询)
    ├── IndustrialSCADA.Module.AlarmManagement/ # 报警管理 (确认、处理、过滤)
    └── IndustrialSCADA.Module.LogViewer/       # 日志查看器 (Serilog 文件解析)
```

## 核心功能

### 1. 多协议设备接入
支持 7 种工业通信协议，通过 `IProtocolAdapter` 接口统一抽象：
- **S7**: Siemens S7-300/400/1200/1500 PLC
- **Modbus TCP**: 标准 Modbus 设备
- **OPC UA**: 统一架构设备
- **EtherCAT/ADS**: Beckhoff TwinCAT PLC
- **IEC 104**: 电力系统远动装置
- **DL/T 645**: 多功能电能表
- **Simulator**: 内置模拟器（用于演示和测试）

### 2. 实时数据采集
- 基于 Rx.NET 的响应式数据流 (`IObservable<DataPoint>`)
- 可配置采集间隔 (ScanIntervalMs)
- 支持点位类型: Bool, Int32, Double, String
- 高/低限报警阈值配置

### 3. 报警管理
- 三级报警: Critical (严重), Warning (警告), Info (信息)
- 报警状态流转: Active → Acknowledged → Resolved
- Rx AlarmStream 实时推送
- 自动持久化到 SQLite

### 4. 历史数据存储
- DataCollector → HistoryDataBridge → SQLite 批量写入 (5秒缓冲)
- 支持时间范围查询
- LiveCharts2 趋势图表展示

### 5. 可视化监控
- **仪表盘**: 设备总数、在线/离线/故障统计
- **实时趋势**: 温度、压力等参数动态曲线
- **报警列表**: 按严重等级/状态过滤，支持确认和处理
- **日志查看器**: Serilog 日志文件实时滚动

## 架构设计

### MVVM + Prism 模块化
- 每个功能模块独立项目，通过 `IModule` 动态加载
- Prism Region 导航: `RequestNavigate("MainRegion", viewName)`
- View 构造器注入 ViewModel，手动设置 DataContext

### 模板方法模式
`ProtocolAdapterBase` 定义连接/读写骨架，子类实现 `ConnectCoreAsync` / `ReadCoreAsync` 等：
```csharp
public abstract class ProtocolAdapterBase : IProtocolAdapter
{
    public async Task ConnectAsync(ConnectionConfig config, CancellationToken ct)
    {
        await ConnectCoreAsync(config, ct);
        IsConnected = true;
        OnConnectionStateChanged(true);
    }
    protected abstract Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct);
}
```

### 响应式数据管道
```
PlcSimulator → ProtocolAdapter.ReadAsync()
                ↓
         DataCollector (Rx Subject)
                ↓
    ┌───────────┼───────────┐
    ↓           ↓           ↓
UI 订阅   HistoryBridge  AlarmGenerator
         (批量存 SQLite)  (阈值触发报警)
                              ↓
                        AlarmService (Rx)
                              ↓
                     AlarmPersistenceBridge
                        (存 SQLite)
```

## 开发环境

- **IDE**: Visual Studio 2022 或 JetBrains Rider
- **SDK**: .NET 8.0 SDK
- **OS**: Windows 10/11 (WPF 仅支持 Windows)

## 构建与运行

```bash
# 还原依赖
dotnet restore

# 编译
dotnet build

# 运行
dotnet run --project src/IndustrialSCADA.App
```

首次运行会自动创建 SQLite 数据库 (`scada.db`) 并注册演示设备。

## 演示模式

应用启动后自动：
1. 创建 `Demo-PLC-001` 模拟设备
2. 启动 PlcSimulator 生成模拟数据 (温度 20-80°C, 压力 1-10bar, 电机状态, 计数器)
3. 启动 DataCollector 以 200ms 间隔采集
4. 启动 HistoryDataBridge 每 5 秒批量持久化
5. 启动 DemoAlarmGenerator 根据阈值触发报警 (温度>70°C 严重, >60°C 警告)
6. 启动 AlarmPersistenceBridge 持久化报警记录

## 依赖清单

| 包名 | 版本 | 用途 |
|------|------|------|
| Prism.DryIoc | 9.0.537 | 模块化框架 |
| CommunityToolkit.Mvvm | 8.3.2 | MVVM 工具 |
| MaterialDesignThemes | 5.1.0 | UI 组件 |
| LiveChartsCore.SkiaSharpView.WPF | 2.0.0-rc3.3 | 图表 |
| Microsoft.EntityFrameworkCore.Sqlite | 8.0.x | 持久化 |
| System.Reactive | 6.0.1 | 响应式编程 |
| Serilog | 3.x | 日志 |
| S7netplus | 0.20.0 | Siemens S7 |
| FluentModbus | 5.x | Modbus TCP |
| Beckhoff.TwinCAT.Ads | 6.2.244 | EtherCAT/ADS |

## 许可证

MIT License

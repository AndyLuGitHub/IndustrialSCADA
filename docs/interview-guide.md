# IndustrialSCADA 项目面试技术文档

> 本文档系统梳理项目中使用的核心技术原理和设计方案，用于高级上位机软件工程师面试准备。

---

## 一、整体架构设计

### 1.1 分层架构

项目采用四层架构，每层职责明确、单向依赖：

```
┌─────────────────────────────────────────┐
│  IndustrialSCADA.App (应用层/Shell)      │  Prism 容器、启动流程、MainWindow
├─────────────────────────────────────────┤
│  Modules (功能模块层)                     │  Dashboard, DeviceControl, DataAcquisition,
│                                         │  AlarmManagement, LogViewer
├─────────────────────────────────────────┤
│  IndustrialSCADA.DeviceCore (业务核心层)  │  设备管理、数据采集、报警引擎、桥接服务
├─────────────────────────────────────────┤
│  IndustrialSCADA.Infrastructure (基础设施)│  7种协议适配器、EF Core、Serilog
├─────────────────────────────────────────┤
│  IndustrialSCADA.Core (核心抽象层)        │  接口、实体、枚举（零外部依赖）
└─────────────────────────────────────────┘
```

**设计原则：**
- **依赖倒置（DIP）**：上层通过 Core 层的接口调用下层，不直接依赖实现。例如 DeviceCore 层通过 `IProtocolAdapter` 接口使用协议适配器，不关心具体是 S7 还是 Modbus。
- **单向依赖**：Core ← Infrastructure ← DeviceCore ← Modules ← App，不会出现循环引用。
- **关注点分离**：Core 层只有纯 C# 类型定义，不引用任何第三方库，便于单元测试。

### 1.2 面试要点

> "这个项目采用四层架构，Core 层是纯接口和实体定义，零外部依赖；Infrastructure 层实现具体协议和持久化；DeviceCore 层是业务核心，负责设备管理和数据采集；Modules 层是独立的 Prism 功能模块。整个系统通过接口契约解耦，任何一层的实现都可以独立替换。"

---

## 二、Prism 9 模块化框架

### 2.1 核心概念

Prism 是 .NET 中最成熟的企业级 WPF 模块化框架，提供了模块化、依赖注入、Region 导航三大核心能力。

**生命周期方法（按调用顺序）：**

```csharp
public partial class App  // 继承 PrismApplication
{
    protected override Window CreateShell()          // 1. 创建主窗口
    protected override void RegisterTypes(...)       // 2. 注册 DI 服务
    protected override void ConfigureModuleCatalog(...)  // 3. 注册模块目录
    protected override void InitializeModules()       // 4. 初始化模块
    protected override void OnInitialized()           // 5. 应用启动后（数据库、服务启动等）
}
```

### 2.2 模块注册与加载

每个功能模块实现 `IModule` 接口：

```csharp
public class DashboardModule : IModule
{
    public void RegisterTypes(IContainerRegistry containerRegistry)
    {
        containerRegistry.RegisterSingleton<DashboardViewModel>();
        containerRegistry.RegisterForNavigation<DashboardView>();
    }
    public void OnInitialized(IContainerProvider containerProvider) { }
}
```

**关键设计决策：**
- `RegisterSingleton<ViewModel>()`：DryIoc（Prism 9 默认容器）需要显式注册具体类型才能通过构造函数注入解析。
- `RegisterForNavigation<View>()`：将 View 注册到 Region 导航系统，名称默认为 `typeof(TView).Name`。
- `OnInitialized()` 留空：导航不在模块中触发，因为此时 MainRegion 可能尚未注册到 VisualTree。

### 2.3 Region 导航

Prism Region 是 WPF 中的动态内容占位区域。导航流程：

```xml
<!-- MainWindow.xaml -->
<ContentControl prism:RegionManager.RegionName="MainRegion" />
```

```csharp
// MainWindow.xaml.cs - 在 Loaded 事件中触发导航（确保 Region 已注册）
private void MainWindow_Loaded(object sender, RoutedEventArgs e)
{
    NavigateTo("DashboardView");
}

private void NavigateTo(string viewName)
{
    _regionManager.RequestNavigate("MainRegion", viewName);
}
```

**为什么不用 ViewModelLocator（AutoWire）：**
Prism 9 + DryIoc 的 ViewModelLocator 在解析时存在兼容性问题（XamlParseException）。我们改用显式构造器注入：

```csharp
public DashboardView(DashboardViewModel vm)
{
    DataContext = vm;      // 先设 DataContext
    InitializeComponent(); // 再初始化 XAML
}
```

### 2.4 面试要点

> "我们用 Prism 9 的 Region 导航实现模块间无耦合切换。每个模块是独立的 Class Library，通过 IModule 接口自注册 View 和 ViewModel。导航通过 RequestNavigate 延迟触发，确保 MainWindow 的 Region 已经注册到 VisualTree 中。之所以不用 AutoWire，是因为 Prism 9 默认容器 DryIoc 在 ViewModelLocator 解析时存在兼容性问题，手动构造器注入更可控。"

---

## 三、MVVM 与 CommunityToolkit.Mvvm

### 3.1 源生成器（Source Generator）

CommunityToolkit.Mvvm 使用 C# 源生成器消除 MVVM 样板代码：

```csharp
public partial class DataAcquisitionViewModel : ObservableObject, IDisposable
{
    // [ObservableProperty] 自动生成带 INotifyPropertyChanged 的属性
    [ObservableProperty] private string selectedTag = string.Empty;
    [ObservableProperty] private DateTime historyFrom = DateTime.Now.AddHours(-1);

    // [RelayCommand] 自动生成 ICommand 实现
    [RelayCommand]
    private async Task QueryHistoryAsync()
    {
        var records = await _historyRepository.QueryAsync(SelectedTag, HistoryFrom, HistoryTo);
        // ...
    }
}
```

**编译时自动生成的代码等价于：**
- `SelectedTag` → 完整属性 + `OnPropertyChanged`
- `QueryHistoryCommand` → `AsyncRelayCommand` 包装 + `IsRunning` 状态

### 3.2 面试要点

> "ViewModel 继承 CommunityToolkit.Mvvm 的 ObservableObject，用 [ObservableProperty] 和 [RelayCommand] 两个源生成器消除 INotifyPropertyChanged 和 ICommand 的样板代码。编译时生成的代码与手写等价，运行时零反射开销，性能优于传统的 BindableBase。"

---

## 四、协议适配器——模板方法 + 策略模式

### 4.1 设计动机

工业现场设备协议繁多（S7、Modbus、OPC UA、EtherCAT、IEC 104、DL/T 645），但上层业务（数据采集、设备控制）只关心"连接、读、写、断开"四个操作。

### 4.2 接口定义（策略模式）

```csharp
public interface IProtocolAdapter
{
    string ProtocolName { get; }
    bool IsConnected { get; }
    ProtocolType ProtocolType { get; }
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

    Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default);
    Task DisconnectAsync();
    Task<T> ReadAsync<T>(string address, CancellationToken ct = default);
    Task WriteAsync<T>(string address, T value, CancellationToken ct = default);
    IObservable<DataPoint> SubscribeStream(string address, TimeSpan interval);
}
```

这是一个标准的**策略模式接口**：任何实现此接口的协议都可以被 DataCollector 和 DeviceManager 无差别使用。

### 4.3 模板方法基类

```csharp
public abstract class ProtocolAdapterBase : IProtocolAdapter
{
    // === 骨架方法（定义算法步骤） ===
    public async Task ConnectAsync(ConnectionConfig config, CancellationToken ct)
    {
        _config = config;
        await ConnectCoreAsync(config, ct);   // 步骤1：子类实现
        _isConnected = true;                   // 步骤2：基类处理
        OnConnectionStateChanged(...);          // 步骤3：触发事件
        Logger.LogInformation(...);             // 步骤4：记录日志
    }

    // === 子类必须实现的抽象方法 ===
    protected abstract Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct);
    protected abstract Task DisconnectCoreAsync();
    protected abstract Task<T> ReadCoreAsync<T>(string address, CancellationToken ct);
    protected abstract Task WriteCoreAsync<T>(string address, T value, CancellationToken ct);
}
```

**模板方法的好处：**
- 连接/断开的日志记录、状态管理、异常处理由基类统一实现，7 个协议无需重复编写
- 子类只需关注协议特有的通信逻辑
- `OnConnectionStateChanged` 是 `virtual` 钩子方法，子类可选覆盖

### 4.4 具体协议实现对比

| 协议 | 库 | 重试策略 | 特殊设计 |
|------|-----|---------|---------|
| **S7** | S7netplus | 3次指数退避 (100/200/400ms) | `Task.Run` 包装同步 Open |
| **Modbus TCP** | FluentModbus | 2次线性退避 (200/400ms) | 地址前缀解析 (0x/1x/3x/4x)，自动重连 |
| **EtherCAT/ADS** | TwinCAT.Ads | 3次指数退避 | `ConcurrentDictionary` 句柄缓存 |
| **IEC 104** | 原生 TCP | 连接级重连 | APCI 帧解析，I/S/U 帧，STARTDT 握手 |
| **DL/T 645** | TCP 串口桥 | 重连机制 | FE+68 帧格式，+33/-33 编码，BCD 地址 |
| **OPC UA** | Opc.UaFx | 标准重试 | Session 管理 |
| **Simulator** | 内存字典 | 无需重试 | PlcSimulator 设置寄存器值 |

### 4.5 重试与指数退避（以 S7 为例）

```csharp
for (int attempt = 0; attempt <= MaxRetries; attempt++)
{
    try
    {
        if (attempt > 0)
        {
            var delayMs = (int)Math.Pow(2, attempt - 1) * 100; // 100ms → 200ms → 400ms
            await Task.Delay(delayMs, ct);
        }
        var result = _plc.Read(address);
        return ConvertResult<T>(result);
    }
    catch (PlcException ex)
    {
        lastException = ex;
    }
}
throw lastException!;
```

### 4.6 EtherCAT 句柄缓存

ADS 协议读写需要先创建变量句柄（`CreateVariableHandle`），每次创建有网络开销。使用 `ConcurrentDictionary<string, uint>` 缓存句柄：

```csharp
private readonly ConcurrentDictionary<string, uint> _variableHandles = new();

private async Task<uint> GetOrCreateHandleAsync(string variableName, CancellationToken ct)
{
    if (_variableHandles.TryGetValue(variableName, out var cached))
        return cached;
    var result = await _adsClient.CreateVariableHandleAsync(variableName, ct);
    _variableHandles[variableName] = result.Handle;
    return result.Handle;
}
```

### 4.7 IEC 104 协议实现

IEC 60870-5-104 是电力系统远动通信标准，实现包含：
- **APCI 帧解析**：起始字节 0x68 + 长度 + 4 字节控制域
- **三种帧类型**：I 帧（信息传输）、S 帧（监视）、U 帧（未编号控制）
- **STARTDT/STOPDT 握手**：连接建立后发送 STARTDT 激活帧，等待对端 STARTDT_CON 确认
- **全呼 interrogation**：C_IC_NA_1 类型标识，QOI=20（全呼）
- **15位序列号管理**：发送序号 V(S) 和接收序号 V(R)

### 4.8 DL/T 645 协议实现

DL/T 645-2007 是中国多功能电能表通信标准：
- **帧格式**：FE FE FE FE + 68 + 6字节地址 + 68 + 控制码 + 数据 + CS + 16
- **+33/-33 编码**：地址域和数据域均需加 0x33 编码，接收时减 0x33 解码
- **BCD 地址**：6 字节地址采用 BCD 编码（低字节在前）
- **校验和**：从第一个 68 到 CS 前一字节的算术累加取低字节

### 4.9 面试要点

> "协议适配层用了两个设计模式的组合：策略模式定义统一的 IProtocolAdapter 接口，模板方法模式在 ProtocolAdapterBase 中封装连接管理、日志记录、异常处理等通用逻辑。子类只需实现四个 Core 方法即可接入新协议。每个协议还实现了工业级的容错机制——S7 和 EtherCAT 用 3 次指数退避重试，Modbus 有自动重连机制，EtherCAT 用 ConcurrentDictionary 缓存变量句柄减少网络开销。IEC 104 和 DL/T 645 是原生 TCP 实现，分别处理了 APCI 帧解析和 +33/-33 编码。"

---

## 五、Rx.NET 响应式数据管道

### 5.1 整体数据流架构

```
PlcSimulator (200ms周期)
    ↓ SetRegister()
SimulatorProtocolAdapter (内存字典)
    ↓ ReadAsync<object>()
DataCollector (后台循环, 100ms tick)
    ↓ _dataSubject.OnNext(DataPoint)
    │
    ├──→ DataAcquisitionViewModel (UI 实时更新)
    ├──→ DashboardViewModel (统计卡片更新)
    ├──→ HistoryDataBridge (Buffer 5s → SQLite 批量写入)
    ├──→ DemoAlarmGenerator (阈值检测 → RaiseAlarmAsync)
    │         ↓
    │    AlarmService._alarmSubject.OnNext(AlarmEntity)
    │         │
    │         ├──→ AlarmManagementViewModel (UI 报警列表)
    │         └──→ AlarmPersistenceBridge (SQLite 报警存档)
    │
    └──→ DeviceControlViewModel (设备状态刷新)
```

### 5.2 Subject 发布-订阅模式

`DataCollector` 用 `Subject<DataPoint>` 作为数据发射器：

```csharp
public sealed class DataCollector : IDataCollector, IDisposable
{
    private readonly Subject<DataPoint> _dataSubject = new();

    // 对外暴露 IObservable（隐藏 Subject 的 OnNext 能力）
    public IObservable<DataPoint> DataStream => _dataSubject.AsObservable();

    private async Task CollectLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            foreach (var device in _deviceManager.Devices)
            {
                foreach (var dp in device.DataPoints)
                {
                    var value = await adapter.ReadAsync<object>(dp.Address, ct);
                    var point = new DataPoint { TagName = dp.TagName, CurrentValue = value, Quality = 192 };
                    _dataSubject.OnNext(point);  // 推送给所有订阅者
                }
            }
            await Task.Delay(100, ct);
        }
    }
}
```

**关键设计：**
- `AsObservable()` 将 Subject 包装为只读 `IObservable<T>`，外部只能 Subscribe 不能 OnNext
- 采集失败时推送 `Quality=0` 的坏点，确保订阅者始终知道数据状态

### 5.3 Rx Buffer 操作符——批量持久化

`HistoryDataBridge` 用 `Buffer(TimeSpan)` 将高频数据流转为批量写入：

```csharp
_subscription = _dataCollector.DataStream
    .Buffer(TimeSpan.FromSeconds(5))      // 每5秒收集一批
    .Where(batch => batch.Count > 0)      // 过滤空批
    .SelectMany(batch =>                   // 异步保存
        Observable.FromAsync(() => SaveBatchAsync(batch)))
    .Subscribe(
        _ => { },
        ex => _logger.LogError(ex, "HistoryDataBridge 异常"));
```

**为什么用 Buffer 而不是逐条写入：**
- 采集器每 100ms 一轮、4 个点位 = 每秒 40 条数据
- 逐条写入 SQLite 会产生大量 I/O 和事务开销
- Buffer 5 秒一批（约 200 条），一次 `SaveChangesAsync()` 完成，性能提升一个数量级

### 5.4 UI 线程安全——Dispatcher 调度

Rx 数据流在后台线程产生，但 WPF UI 必须在 UI 线程更新：

```csharp
_dataSubscription = dataCollector.DataStream.Subscribe(dp =>
{
    _dispatcher.InvokeAsync(() => OnDataReceived(dp), DispatcherPriority.Background);
});
```

`DispatcherPriority.Background` 确保数据更新不阻塞用户交互（鼠标点击、滚动等优先级更高）。

### 5.5 面试要点

> "整个数据采集管道基于 Rx.NET 的发布-订阅模型。DataCollector 用 Subject 作为热流发射器，对外只暴露 IObservable 接口防止误操作。下游订阅者各有分工：ViewModel 通过 Dispatcher 更新 UI，HistoryDataBridge 用 Buffer 操作符每 5 秒批量写入 SQLite，DemoAlarmGenerator 做阈值检测触发报警。这种设计的好处是生产者（采集器）和消费者（UI、持久化、报警）完全解耦，新增一个消费者只需要 Subscribe 就行，不改动任何已有代码。"

---

## 六、依赖注入（DI）体系

### 6.1 容器选择

Prism 9 默认使用 **DryIoc** 作为 DI 容器，相比 Unity 性能更优，支持开放泛型注册。

### 6.2 开放泛型注册（关键设计）

项目中大量服务依赖 `Microsoft.Extensions.Logging.ILogger<T>`，但底层用的是 Serilog。通过适配器 + 开放泛型注册解决：

```csharp
// 适配器：将 Serilog 桥接到 ILogger<T>
public class SerilogLoggerAdapter<T> : ILogger<T>
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext(typeof(T));
    // ...实现 Log、IsEnabled、BeginScope
}

// 注册开放泛型（一行代码解决所有 ILogger<T> 的注入）
containerRegistry.RegisterSingleton(typeof(ILogger<>), typeof(SerilogLoggerAdapter<>));
```

这样 `DataCollector` 构造函数注入 `ILogger<DataCollector>` 时，容器自动创建 `SerilogLoggerAdapter<DataCollector>` 实例。

### 6.3 分层注册（扩展方法模式）

每一层通过 `IContainerRegistry` 扩展方法自注册，App 层无需知道实现细节：

```csharp
protected override void RegisterTypes(IContainerRegistry containerRegistry)
{
    SerilogSetup.CreateLogger();                                          // 1. 日志
    containerRegistry.RegisterSingleton(typeof(ILogger<>), typeof(SerilogLoggerAdapter<>));
    containerRegistry.AddInfrastructure($"Data Source={dbPath}");         // 2. 基础设施
    containerRegistry.AddDeviceCore();                                    // 3. 业务核心
    containerRegistry.RegisterSingleton<INavigationService, NavigationService>();
    containerRegistry.RegisterSingleton<MainWindow>();                    // 4. Shell
}
```

### 6.4 服务生命周期

| 生命周期 | 含义 | 项目中的例子 |
|---------|------|-------------|
| **Singleton** | 全局唯一实例 | DataCollector, AlarmService, DeviceManager, 所有 Bridge |
| **Transient** | 每次解析创建新实例 | ScadaDbContext (EF Core 推荐) |
| **Scoped** | 按作用域唯一 | AlarmPersistenceBridge 中用 CreateScope 获取 DbContext |

### 6.5 Singleton 中注入 Transient 的问题

`AlarmPersistenceBridge` 是单例，但 `ScadaDbContext` 是瞬态的。直接注入会导致 DbContext 被永久持有，违反 EF Core 最佳实践。解决方案：注入 `IServiceProvider`，每次保存时创建作用域：

```csharp
public sealed class AlarmPersistenceBridge : IDisposable
{
    private readonly IServiceProvider _serviceProvider;

    private async Task PersistAlarmAsync(AlarmEntity alarm)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ScadaDbContext>();
        dbContext.Alarms.Add(alarm);
        await dbContext.SaveChangesAsync();
    }
}
```

### 6.6 面试要点

> "DI 体系有两个亮点：一是通过开放泛型注册 `typeof(ILogger<>)` → `typeof(SerilogLoggerAdapter<>)`，一行代码解决了 Serilog 和 Microsoft.Extensions.Logging 两套日志体系的桥接问题；二是通过扩展方法分层注册，Infrastructure、DeviceCore 各自管各自的 DI 配置，App 层只调用 `AddInfrastructure()` 和 `AddDeviceCore()`。另外处理了一个经典的 Singleton 注入 Transient 问题——AlarmPersistenceBridge 是单例，但 DbContext 应该短生命周期，所以用 IServiceProvider.CreateScope() 在每次保存时创建新的作用域。"

---

## 七、EF Core + SQLite 持久化

### 7.1 Code-First 模式

```csharp
public class ScadaDbContext : DbContext
{
    public DbSet<AlarmEntity> Alarms { get; set; }
    public DbSet<HistoryRecordEntity> HistoryRecords { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // AlarmEntity.TriggerValue 是 object?，EF Core 无法映射，手动忽略
        modelBuilder.Entity<AlarmEntity>().Ignore(e => e.TriggerValue);
    }
}
```

### 7.2 Repository 模式

```csharp
public class SqliteHistoryRepository : IHistoryRepository
{
    private readonly ScadaDbContext _dbContext;

    public async Task SaveAsync(IEnumerable<HistoryRecord> records)
    {
        var entities = records.Select(r => new HistoryRecordEntity { ... });
        _dbContext.HistoryRecords.AddRange(entities);
        await _dbContext.SaveChangesAsync();
    }

    public async Task<IReadOnlyList<HistoryRecord>> QueryAsync(string tag, DateTime from, DateTime to)
    {
        return await _dbContext.HistoryRecords
            .AsNoTracking()  // 只读查询，关闭变更跟踪提升性能
            .Where(e => e.TagName == tag && e.Timestamp >= from && e.Timestamp <= to)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();
    }
}
```

### 7.3 面试要点

> "持久化用 EF Core Code-First + SQLite，数据库文件在应用启动时通过 `EnsureCreatedAsync()` 自动创建。历史数据查询用 `AsNoTracking()` 关闭 EF Core 的变更跟踪，只读场景下性能提升约 30%。`AlarmEntity.TriggerValue` 是 `object?` 类型，EF Core 无法自动映射，在 `OnModelCreating` 中用 `Ignore()` 排除。"

---

## 八、Serilog 日志适配器（适配器模式）

### 8.1 问题背景

- 项目底层日志引擎是 **Serilog**（支持结构化日志、文件滚动、丰富的 Sink）
- 但 `DataCollector`、`DeviceManager` 等服务使用 `Microsoft.Extensions.Logging.ILogger<T>` 接口
- 两者 API 不兼容，需要桥接

### 8.2 适配器实现

```csharp
public class SerilogLoggerAdapter<T> : ILogger<T>
{
    private readonly Serilog.ILogger _logger = Serilog.Log.ForContext(typeof(T));

    public IDisposable? BeginScope<TState>(TState state) => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) =>
        _logger.IsEnabled(MapLevel(logLevel));

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
        Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel)) return;
        _logger.Write(MapLevel(logLevel), exception, formatter(state, exception));
    }
}
```

### 8.3 命名冲突处理

`Serilog.Log` 静态类和 `ILogger.Log` 方法存在 CS0119 命名冲突。解决方案：

```csharp
// 用完全限定名区分
private readonly Serilog.ILogger _logger = Serilog.Log.ForContext(typeof(T));
```

### 8.4 面试要点

> "日志体系用了适配器模式。底层是 Serilog 提供结构化日志和文件滚动能力，但业务层统一使用 Microsoft.Extensions.Logging 的 ILogger<T> 接口。通过 SerilogLoggerAdapter<T> 桥接两套 API，再用 DI 的开放泛型注册一行代码完成全局配置。这是典型的六角架构中端口和适配器的思路。"

---

## 九、并发与线程安全

### 9.1 ConcurrentDictionary 在报警服务中的使用

```csharp
public sealed class AlarmService : IAlarmService
{
    private readonly ConcurrentDictionary<long, AlarmEntity> _alarms = new();

    public Task RaiseAlarmAsync(AlarmEntity alarm)
    {
        alarm.Id = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _alarms[alarm.Id] = alarm;           // 线程安全写入
        _alarmSubject.OnNext(alarm);          // 推送给订阅者
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AlarmEntity>> QueryAsync(AlarmFilter filter)
    {
        var query = _alarms.Values.AsEnumerable();  // 线程安全读取快照
        // LINQ 过滤、分页...
    }
}
```

**为什么选 ConcurrentDictionary 而不是 Dictionary + lock：**
- `ConcurrentDictionary` 内部使用分段锁（bucket-level locking），并发读写性能远优于全局锁
- 报警触发（写入）和查询（读取）可能同时发生，需要保证线程安全

### 9.2 CancellationToken 协作式取消

后台采集循环通过 `CancellationToken` 实现优雅退出：

```csharp
private async Task CollectLoopAsync(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        // ... 采集逻辑
        await Task.Delay(100, ct);  // ct 传入 Delay，取消时立即返回
    }
}

public async Task StopAsync()
{
    _cts.Cancel();                    // 发出取消信号
    await _collectTask;               // 等待后台任务实际退出
    _cts.Dispose();
}
```

### 9.3 面试要点

> "并发安全主要从三个层面保证：数据存储用 ConcurrentDictionary 的分段锁替代全局锁；后台线程用 CancellationToken 协作式取消确保优雅退出；UI 线程通过 Dispatcher.InvokeAsync 加 DispatcherPriority.Background 保证数据更新不阻塞用户交互。"

---

## 十、设计模式总结

| 设计模式 | 项目中的位置 | 作用 |
|---------|-------------|------|
| **策略模式** | IProtocolAdapter + 7 个实现 | 协议可替换，上层代码不关心具体协议 |
| **模板方法** | ProtocolAdapterBase | 通用逻辑（日志、状态、异常）集中在基类 |
| **观察者模式** | Rx Subject + Subscribe | 数据管道和报警管道的发布-订阅解耦 |
| **桥接模式** | HistoryDataBridge, AlarmPersistenceBridge | 实时域与持久化域解耦 |
| **适配器模式** | SerilogLoggerAdapter | 桥接 Serilog 和 ILogger<T> 两套日志 API |
| **工厂方法** | DI 容器的 Resolve | 根据接口自动创建具体实例 |
| **单例模式** | DataCollector, AlarmService 等 | 全局唯一的运行时服务 |
| **仓储模式** | SqliteHistoryRepository | 数据访问与业务逻辑分离 |

---

## 十一、常见面试问题 Q&A

### Q1: 为什么选 Prism 而不是简单的 MVVM Light 或自己搭？
> Prism 提供了完整的模块化框架（IModule 动态加载）、Region 导航（无需手动管理 View 切换）、以及成熟的 DI 容器集成。对于工业上位机这种需要多模块、可扩展的系统，Prism 的模块化能力是核心价值——新增一个功能模块只需要创建一个 Class Library 实现 IModule，不需要修改任何其他模块的代码，完全符合开闭原则。

### Q2: Rx.NET 和事件（Event）有什么区别？为什么用 Rx？
> 事件是"点对点"的，一个事件只能被一个处理器处理（或者说多播时没有流控制能力）。Rx 的 IObservable 是"流"，支持丰富的操作符——Buffer（批量）、Throttle（节流）、DistinctUntilChanged（去重）、Merge（合并多源）。在这个项目里，采集到的 DataPoint 流需要同时被 UI 显示、历史存储、报警检测三个消费者使用，用 Rx 只需要三个 Subscribe，如果用事件，就需要在事件处理器里手动调用三个方法，耦合度更高。另外 Buffer(5s) 操作符一行代码就实现了批量缓冲，用事件实现需要自己加 Timer 和线程安全队列。

### Q3: DataCollector 的 Subject 和 IObservable 为什么要分开？
> `Subject<T>` 既是 IObserver 又是 IObservable，它拥有 OnNext/OnError/OnCompleted 的推送能力。如果直接暴露 Subject，外部代码可能意外调用 OnNext 向数据流注入伪造数据。通过 `_dataSubject.AsObservable()` 包装，外部只能 Subscribe 不能推送，保证了数据流的单向性——只有 DataCollector 的采集循环才能向流中推送数据。

### Q4: 重试策略为什么用指数退避而不是固定延迟？
> 工业通信中的瞬时故障（网络抖动、PLC 繁忙）通常是短暂的。固定延迟重试（比如每次都等 500ms）在故障持续时会造成资源浪费，在故障已恢复时又不必要地等待。指数退避（100ms → 200ms → 400ms）的策略是：第一次快速重试（大概率故障已恢复），如果仍然失败则逐步延长等待时间，给设备恢复的时间窗口。这在 S7、EtherCAT 等工业协议中是标准做法。

### Q5: 为什么 AlarmService 用内存存储而不是直接写数据库？
> 两个原因：第一是性能——报警触发和查询都是高频操作（报警可能每秒多次触发），ConcurrentDictionary 的读写速度比 SQLite 快几个数量级；第二是解耦——报警产生（业务逻辑）和报警持久化（基础设施关注点）是两个不同的职责。AlarmPersistenceBridge 作为独立服务订阅 AlarmStream 做持久化，这样即使持久化失败（磁盘满、数据库锁），报警服务本身不受影响，UI 仍然能实时显示报警。

### Q6: ConfigureAwait(false) 是什么意思？为什么要到处用？
> `ConfigureAwait(false)` 告诉 await 不要在原始同步上下文（WPF 中就是 UI 线程）上恢复执行。对于后台服务（DataCollector、Bridge），我们不需要回到 UI 线程，加上这个可以避免不必要的线程切换开销，也能防止死锁（当调用方用 `.Result` 或 `.Wait()` 同步等待时）。注意 ViewModel 中不用这个，因为 ViewModel 需要回到 UI 线程更新绑定属性。

---

## 十二、项目启动流程（完整链路）

```
1. App.RegisterTypes()
   ├── Serilog 初始化（文件 + 控制台 Sink）
   ├── ILogger<> 开放泛型注册 → SerilogLoggerAdapter<>
   ├── AddInfrastructure()：协议适配器、ScadaDbContext、HistoryRepository
   ├── AddDeviceCore()：DeviceManager、DataCollector、AlarmService、Bridge 服务
   └── MainWindow 注册

2. App.ConfigureModuleCatalog()
   └── 5 个 IModule 注册（WhenAvailable 模式）

3. App.InitializeModules()
   └── 各模块 RegisterTypes：RegisterSingleton<VM> + RegisterForNavigation<View>

4. App.OnInitialized()
   ├── EnsureCreatedAsync()：创建 SQLite 数据库表
   ├── AddDeviceAsync()：注册 Demo-PLC-001（4 个数据点）
   ├── ConnectAsync()：连接模拟器协议
   ├── PlcSimulator.Start()：开始模拟数据生成（正弦波温度、三角波压力、方波电机、递增计数器）
   ├── DataCollector.StartAsync()：开始后台采集循环（100ms tick）
   ├── HistoryDataBridge.Start()：订阅 DataStream → Buffer 5s → SQLite
   ├── AlarmPersistenceBridge.Start()：订阅 AlarmStream → SQLite
   └── DemoAlarmGenerator.Start()：订阅 DataStream → 阈值报警检测

5. MainWindow.Loaded()
   └── RequestNavigate("MainRegion", "DashboardView")：显示默认页面
```

---

## 十三、性能与可扩展性设计

| 设计点 | 实现 | 效果 |
|--------|------|------|
| 批量写入 | Rx Buffer(5s) | 减少 SQLite I/O 约 20 倍 |
| 句柄缓存 | ConcurrentDictionary<string, uint> | 减少 ADS 网络往返 |
| 无追踪查询 | AsNoTracking() | 只读场景性能提升 ~30% |
| 异步不阻塞 | ConfigureAwait(false) | 避免 UI 线程死锁 |
| 后台优先级 | DispatcherPriority.Background | 数据更新不卡顿用户操作 |
| 模块化 | IModule + Region | 新增功能模块零改动现有代码 |
| 协议扩展 | IProtocolAdapter | 新增协议只需实现 4 个 Core 方法 |

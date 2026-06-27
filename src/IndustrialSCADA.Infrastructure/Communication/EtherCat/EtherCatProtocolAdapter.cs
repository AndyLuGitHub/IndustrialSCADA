using System.Collections.Concurrent;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;
using TwinCAT;
using TwinCAT.Ads;

namespace IndustrialSCADA.Infrastructure.Communication.EtherCat;

/// <summary>
/// Beckhoff EtherCAT/ADS 协议适配器，基于 Beckhoff.TwinCAT.Ads 库实现。
/// 支持通过 ADS 协议读写 TwinCAT PLC 变量（BOOL、INT、DINT、REAL、LREAL、STRING）。
/// 具备异步连接、读取重试（3 次指数退避）和标准 Dispose 模式。
/// </summary>
public sealed class EtherCatProtocolAdapter : ProtocolAdapterBase, IDisposable
{
    private AdsClient? _adsClient;
    private bool _disposed;

    /// <summary>缓存变量句柄，避免每次读写都创建/销毁句柄。</summary>
    private readonly ConcurrentDictionary<string, uint> _variableHandles = new();

    /// <summary>最大读取/写入重试次数。</summary>
    private const int MaxRetries = 3;

    /// <summary>字符串类型的默认最大长度（字节）。</summary>
    private const int DefaultStringMaxLength = 255;

    /// <summary>
    /// 初始化 <see cref="EtherCatProtocolAdapter"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public EtherCatProtocolAdapter(ILogger<EtherCatProtocolAdapter> logger)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override string ProtocolName => "EtherCAT/ADS";

    /// <inheritdoc />
    public override ProtocolType ProtocolType => ProtocolType.EtherCat;

    /// <summary>
    /// 建立与 Beckhoff PLC 的 ADS 连接。
    /// 从 <paramref name="config"/> 的 <see cref="ConnectionConfig.Parameters"/> 中读取
    /// AmsNetId（默认 "127.0.0.1.1.1"）和 AmsPort（默认 851）。
    /// 使用 <see cref="Task.Run(System.Func{object})"/> 包装同步 Connect 调用以避免阻塞。
    /// </summary>
    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct)
    {
        var amsNetIdStr = GetParameter<string>(config, "AmsNetId", "127.0.0.1.1.1");
        var amsPort = GetParameter<int>(config, "AmsPort", 851);

        _adsClient = new AdsClient();
        var amsNetId = new AmsNetId(amsNetIdStr);

        Logger.LogInformation("[EtherCAT] 正在连接 AMS 路由 {AmsNetId}:{AmsPort} (TCP {Host}:{TcpPort})",
            amsNetIdStr, amsPort, config.Host, config.Port);

        // 使用 Task.Run 包装同步 Connect，避免阻塞 UI 线程
        await Task.Run(() => _adsClient.Connect(amsNetId, amsPort), ct).ConfigureAwait(false);

        // 清除上一次连接可能残留的句柄缓存
        _variableHandles.Clear();
    }

    /// <summary>
    /// 断开与 Beckhoff PLC 的 ADS 连接，释放所有变量句柄。
    /// </summary>
    protected override Task DisconnectCoreAsync()
    {
        try
        {
            ReleaseAllHandles();

            _adsClient?.Disconnect();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[EtherCAT] 断开连接时出现异常");
        }
        finally
        {
            _adsClient?.Dispose();
            _adsClient = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 PLC 读取指定变量名的数据，最多重试 3 次（指数退避 100ms、200ms、400ms）。
    /// 支持 BOOL、INT (short)、DINT (int)、REAL (float)、LREAL (double)、STRING 类型。
    /// </summary>
    /// <typeparam name="T">期望的数据类型。</typeparam>
    /// <param name="address">PLC 变量名，如 "MAIN.fTemperature"。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>读取到的值。</returns>
    protected override async Task<T> ReadCoreAsync<T>(string address, CancellationToken ct)
    {
        EnsureConnected();

        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = (int)Math.Pow(2, attempt - 1) * 100; // 100ms, 200ms, 400ms
                    Logger.LogWarning("[EtherCAT] 第 {Attempt} 次重试读取 {Address}，延迟 {Delay}ms",
                        attempt, address, delayMs);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }

                var handle = await GetOrCreateHandleAsync(address, ct).ConfigureAwait(false);
                var adsType = MapToAdsType(typeof(T));

                ResultAnyValue result;

                if (adsType == typeof(string))
                {
                    // 字符串类型需要通过 args 参数指定最大长度
                    result = await _adsClient!.ReadAnyAsync(handle, adsType,
                        new[] { DefaultStringMaxLength }, ct).ConfigureAwait(false);
                }
                else
                {
                    result = await _adsClient!.ReadAnyAsync(handle, adsType, ct).ConfigureAwait(false);
                }

                var value = result.Value;

                if (value is T typed)
                    return typed;

                // 常见类型转换: short -> int, int -> double, float -> double 等
                return ConvertResult<T>(value);
            }
            catch (AdsException ex)
            {
                lastException = ex;
                Logger.LogWarning(ex, "[EtherCAT] 读取变量 {Address} 失败 (尝试 {Attempt}/{Max})",
                    address, attempt + 1, MaxRetries + 1);
            }
        }

        Logger.LogError(lastException, "[EtherCAT] 读取变量 {Address} 在 {MaxRetries} 次重试后仍然失败",
            address, MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// 向 PLC 指定变量名写入数据，最多重试 3 次（指数退避 100ms、200ms、400ms）。
    /// </summary>
    protected override async Task WriteCoreAsync<T>(string address, T value, CancellationToken ct)
    {
        EnsureConnected();

        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = (int)Math.Pow(2, attempt - 1) * 100; // 100ms, 200ms, 400ms
                    Logger.LogWarning("[EtherCAT] 第 {Attempt} 次重试写入 {Address}，延迟 {Delay}ms",
                        attempt, address, delayMs);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }

                var handle = await GetOrCreateHandleAsync(address, ct).ConfigureAwait(false);

                if (typeof(T) == typeof(string))
                {
                    await _adsClient!.WriteAnyAsync(handle, value!,
                        new[] { DefaultStringMaxLength }, ct).ConfigureAwait(false);
                }
                else
                {
                    await _adsClient!.WriteAnyAsync(handle, value!, ct).ConfigureAwait(false);
                }

                return;
            }
            catch (AdsException ex)
            {
                lastException = ex;
                Logger.LogWarning(ex, "[EtherCAT] 写入变量 {Address} 失败 (尝试 {Attempt}/{Max})",
                    address, attempt + 1, MaxRetries + 1);
            }
        }

        Logger.LogError(lastException, "[EtherCAT] 写入变量 {Address} 在 {MaxRetries} 次重试后仍然失败",
            address, MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// 获取或创建变量句柄。使用缓存避免重复创建。
    /// </summary>
    /// <param name="variableName">PLC 变量名。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>变量句柄（uint）。</returns>
    private async Task<uint> GetOrCreateHandleAsync(string variableName, CancellationToken ct)
    {
        if (_variableHandles.TryGetValue(variableName, out var cachedHandle))
            return cachedHandle;

        var resultHandle = await _adsClient!.CreateVariableHandleAsync(variableName, ct).ConfigureAwait(false);
        var handle = resultHandle.Handle;
        _variableHandles[variableName] = handle;
        return handle;
    }

    /// <summary>
    /// 释放所有已缓存的变量句柄。
    /// </summary>
    private void ReleaseAllHandles()
    {
        if (_adsClient == null || _variableHandles.IsEmpty)
        {
            _variableHandles.Clear();
            return;
        }

        foreach (var kvp in _variableHandles)
        {
            try
            {
                _adsClient.DeleteVariableHandle(kvp.Value);
            }
            catch (Exception ex)
            {
                Logger.LogDebug(ex, "[EtherCAT] 释放变量句柄 {Handle} 时出现异常", kvp.Key);
            }
        }

        _variableHandles.Clear();
    }

    /// <summary>
    /// 确保 ADS 客户端已连接。
    /// </summary>
    private void EnsureConnected()
    {
        if (_adsClient == null)
            throw new InvalidOperationException("EtherCAT/ADS 客户端未连接");
    }

    /// <summary>
    /// 将请求的 .NET 类型映射为 ADS 通信使用的类型。
    /// PLC 类型对应关系：BOOL→bool、INT→short、DINT→int、REAL→float、LREAL→double、STRING→string。
    /// </summary>
    /// <param name="requestedType">请求的 .NET 类型。</param>
    /// <returns>ADS 通信使用的 .NET 类型。</returns>
    private static Type MapToAdsType(Type requestedType)
    {
        // 直接映射：这些类型本身就是 ADS ReadAny 接受的 .NET 类型
        if (requestedType == typeof(bool)) return typeof(bool);       // BOOL
        if (requestedType == typeof(short)) return typeof(short);     // INT  (16-bit signed)
        if (requestedType == typeof(int)) return typeof(int);         // DINT (32-bit signed)
        if (requestedType == typeof(float)) return typeof(float);     // REAL (32-bit float)
        if (requestedType == typeof(double)) return typeof(double);   // LREAL (64-bit float)
        if (requestedType == typeof(string)) return typeof(string);   // STRING
        return requestedType;
    }

    /// <summary>
    /// 将 ADS 读取结果转换为目标类型，支持 short→int、int→double、
    /// float→double 等常见数值类型转换。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="result">ADS 读取的原始值。</param>
    /// <returns>转换后的值。</returns>
    private static T ConvertResult<T>(object? result)
    {
        if (result == null)
            return default(T)!;

        var targetType = typeof(T);

        // short (INT) -> int / long / double / float
        if (result is short shortVal)
        {
            if (targetType == typeof(int)) return (T)(object)(int)shortVal;
            if (targetType == typeof(long)) return (T)(object)(long)shortVal;
            if (targetType == typeof(double)) return (T)(object)(double)shortVal;
            if (targetType == typeof(float)) return (T)(object)(float)shortVal;
        }

        // ushort -> int / long / double / float
        if (result is ushort ushortVal)
        {
            if (targetType == typeof(int)) return (T)(object)(int)ushortVal;
            if (targetType == typeof(long)) return (T)(object)(long)ushortVal;
            if (targetType == typeof(double)) return (T)(object)(double)ushortVal;
            if (targetType == typeof(float)) return (T)(object)(float)ushortVal;
        }

        // int (DINT) -> double / float / long
        if (result is int intVal)
        {
            if (targetType == typeof(double)) return (T)(object)(double)intVal;
            if (targetType == typeof(float)) return (T)(object)(float)intVal;
            if (targetType == typeof(long)) return (T)(object)(long)intVal;
        }

        // uint -> long / double
        if (result is uint uintVal)
        {
            if (targetType == typeof(long)) return (T)(object)(long)uintVal;
            if (targetType == typeof(double)) return (T)(object)(double)uintVal;
        }

        // float (REAL) -> double / int
        if (result is float floatVal)
        {
            if (targetType == typeof(double)) return (T)(object)(double)floatVal;
            if (targetType == typeof(int)) return (T)(object)(int)floatVal;
        }

        // double (LREAL) -> float / int
        if (result is double dblVal)
        {
            if (targetType == typeof(float)) return (T)(object)(float)dblVal;
            if (targetType == typeof(int)) return (T)(object)(int)dblVal;
        }

        // 回退：使用 Convert.ChangeType
        return (T)Convert.ChangeType(result, targetType);
    }

    /// <summary>
    /// 从连接配置的扩展参数中获取值，不存在则返回默认值。
    /// </summary>
    private static TV GetParameter<TV>(ConnectionConfig config, string key, TV defaultValue)
    {
        if (config.Parameters.TryGetValue(key, out var raw) && raw is TV typed)
            return typed;

        if (raw != null)
        {
            try
            {
                return (TV)Convert.ChangeType(raw, typeof(TV));
            }
            catch
            {
                return defaultValue;
            }
        }

        return defaultValue;
    }

    /// <summary>
    /// 释放资源，断开 ADS 连接并清理所有变量句柄。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            ReleaseAllHandles();
            _adsClient?.Disconnect();
        }
        catch
        {
            // 静默处理释放时的异常
        }

        _adsClient?.Dispose();
        _adsClient = null;
    }
}

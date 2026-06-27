using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;
using S7.Net;

namespace IndustrialSCADA.Infrastructure.Communication.S7;

/// <summary>
/// 西门子 S7 系列 PLC 协议适配器，基于 S7.Net 库实现。
/// 支持 S7-300/400/1200/1500 等型号的以太网通信。
/// 具备异步连接、读取重试（3 次指数退避）和标准 Dispose 模式。
/// </summary>
public sealed class S7ProtocolAdapter : ProtocolAdapterBase, IDisposable
{
    private Plc? _plc;
    private bool _disposed;

    /// <summary>最大读取重试次数。</summary>
    private const int MaxRetries = 3;

    /// <summary>
    /// 初始化 <see cref="S7ProtocolAdapter"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public S7ProtocolAdapter(ILogger<S7ProtocolAdapter> logger)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override string ProtocolName => "S7";

    /// <inheritdoc />
    public override ProtocolType ProtocolType => ProtocolType.S7;

    /// <summary>
    /// 建立与 S7 PLC 的异步连接。
    /// 从 <paramref name="config"/> 的 <see cref="ConnectionConfig.Parameters"/> 中读取
    /// Rack（默认 0）和 Slot（默认 1），以及 CpuType（默认 S71200）。
    /// 使用 <see cref="Task.Run(System.Func{object})"/> 包装同步 Open 调用以避免阻塞。
    /// </summary>
    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct)
    {
        var rack = GetParameter<short>(config, "Rack", 0);
        var slot = GetParameter<short>(config, "Slot", 1);
        var cpuType = GetParameter<CpuType>(config, "CpuType", CpuType.S71200);

        _plc = new Plc(cpuType, config.Host, rack, slot);

        // 使用 Task.Run 包装同步 Open，避免阻塞 UI 线程
        await Task.Run(() => _plc.Open(), ct).ConfigureAwait(false);

        if (!_plc.IsConnected)
            throw new InvalidOperationException($"S7 连接失败: {_plc.IP} Rack={rack} Slot={slot}");
    }

    /// <summary>
    /// 断开与 S7 PLC 的连接。
    /// </summary>
    protected override Task DisconnectCoreAsync()
    {
        try
        {
            _plc?.Close();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[S7] 断开连接时出现异常");
        }
        finally
        {
            _plc = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 从 PLC 读取指定地址的数据，最多重试 3 次（指数退避）。
    /// 支持 short-&gt;int、short-&gt;double 等常见类型转换。
    /// </summary>
    /// <typeparam name="T">期望的数据类型。</typeparam>
    /// <param name="address">S7 地址，如 "DB1.DBW0"。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>读取到的值。</returns>
    protected override async Task<T> ReadCoreAsync<T>(string address, CancellationToken ct)
    {
        if (_plc == null || !_plc.IsConnected)
            throw new InvalidOperationException("S7 PLC 未连接");

        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = (int)Math.Pow(2, attempt - 1) * 100; // 100ms, 200ms, 400ms
                    Logger.LogWarning("[S7] 第 {Attempt} 次重试读取 {Address}，延迟 {Delay}ms",
                        attempt, address, delayMs);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }

                var result = _plc.Read(address);

                if (result is T typed)
                    return typed;

                // 常见类型转换: short -> int, short -> double, etc.
                return ConvertResult<T>(result);
            }
            catch (PlcException ex)
            {
                lastException = ex;
                Logger.LogWarning(ex, "[S7] 读取地址 {Address} 失败 (尝试 {Attempt}/{Max})",
                    address, attempt + 1, MaxRetries + 1);
            }
        }

        Logger.LogError(lastException, "[S7] 读取地址 {Address} 在 {MaxRetries} 次重试后仍然失败",
            address, MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// 向 PLC 指定地址写入数据，最多重试 3 次（指数退避）。
    /// </summary>
    protected override async Task WriteCoreAsync<T>(string address, T value, CancellationToken ct)
    {
        if (_plc == null || !_plc.IsConnected)
            throw new InvalidOperationException("S7 PLC 未连接");

        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = (int)Math.Pow(2, attempt - 1) * 100;
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                }

                _plc.Write(address, value!);
                return;
            }
            catch (PlcException ex)
            {
                lastException = ex;
                Logger.LogWarning(ex, "[S7] 写入地址 {Address} 失败 (尝试 {Attempt}/{Max})",
                    address, attempt + 1, MaxRetries + 1);
            }
        }

        Logger.LogError(lastException, "[S7] 写入地址 {Address} 在 {MaxRetries} 次重试后仍然失败",
            address, MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// 将 PLC 读取结果转换为目标类型，支持 short-&gt;int、short-&gt;double、
    /// ushort-&gt;int、int-&gt;double 等常见数值类型转换。
    /// </summary>
    /// <typeparam name="T">目标类型。</typeparam>
    /// <param name="result">PLC 读取的原始值。</param>
    /// <returns>转换后的值。</returns>
    private static T ConvertResult<T>(object? result)
    {
        if (result == null)
            return default(T)!;

        var targetType = typeof(T);

        // short -> int / long / double / float
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

        // int -> double / float / long
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

        // double -> float / int
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
    /// 释放资源，断开 PLC 连接。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _plc?.Close();
        }
        catch
        {
            // 静默处理释放时的异常
        }

        _plc = null;
    }
}

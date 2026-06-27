using System.Net;
using FluentModbus;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.Infrastructure.Communication.Modbus;

/// <summary>
/// Modbus TCP 协议适配器，基于 FluentModbus 库实现。
/// 支持通过地址前缀（0x/1x/3x/4x）自动选择对应的功能码。
/// 具备自动重连机制和读取失败重试（最多 2 次）。
/// </summary>
public sealed class ModbusProtocolAdapter : ProtocolAdapterBase, IDisposable
{
    private ModbusTcpClient? _client;
    private ConnectionConfig? _connectionConfig;
    private bool _disposed;

    /// <summary>默认 Modbus 单元标识符（从站地址）。</summary>
    private byte _unitId = 1;

    /// <summary>最大读取重试次数。</summary>
    private const int MaxRetries = 2;

    /// <summary>
    /// 初始化 <see cref="ModbusProtocolAdapter"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public ModbusProtocolAdapter(ILogger<ModbusProtocolAdapter> logger)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override string ProtocolName => "ModbusTcp";

    /// <inheritdoc />
    public override ProtocolType ProtocolType => ProtocolType.ModbusTcp;

    /// <summary>
    /// 建立 Modbus TCP 连接。
    /// 可通过 <see cref="ConnectionConfig.Parameters"/> 指定 UnitId（从站地址，默认 1）。
    /// </summary>
    protected override Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct)
    {
        _connectionConfig = config;

        if (config.Parameters.TryGetValue("UnitId", out var unitIdRaw))
            _unitId = Convert.ToByte(unitIdRaw);

        _client = new ModbusTcpClient();
        var port = config.Port > 0 ? config.Port : 502;
        _client.Connect(new IPEndPoint(IPAddress.Parse(config.Host), port));

        return Task.CompletedTask;
    }

    /// <summary>
    /// 断开 Modbus TCP 连接。
    /// </summary>
    protected override Task DisconnectCoreAsync()
    {
        try
        {
            _client?.Disconnect();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[Modbus] 断开连接时出现异常");
        }
        finally
        {
            _client?.Dispose();
            _client = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据地址前缀读取 Modbus 寄存器数据，支持自动重连和最多 2 次重试。
    /// <list type="bullet">
    ///   <item>0x / 1x — 线圈/离散输入（返回 bool）</item>
    ///   <item>3x — 输入寄存器</item>
    ///   <item>4x — 保持寄存器</item>
    /// </list>
    /// </summary>
    protected override async Task<T> ReadCoreAsync<T>(string address, CancellationToken ct)
    {
        EnsureConnected();

        var (register, offset) = ParseAddress(address);
        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = attempt * 200; // 200ms, 400ms
                    Logger.LogWarning("[Modbus] 第 {Attempt} 次重试读取 {Address}，延迟 {Delay}ms",
                        attempt, address, delayMs);
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);

                    // 重试前尝试重连
                    TryReconnect();
                }

                object result = register switch
                {
                    // 0x — 线圈
                    0 => (object)(_client!.ReadCoils(_unitId, (ushort)offset, 1)[0] != 0),
                    // 1x — 离散输入
                    1 => (object)(_client!.ReadDiscreteInputs(_unitId, (ushort)offset, 1)[0] != 0),
                    // 3x — 输入寄存器
                    3 => (object)_client!.ReadInputRegisters(_unitId, (ushort)offset, 1)[0],
                    // 4x — 保持寄存器
                    _ => (object)_client!.ReadHoldingRegisters(_unitId, (ushort)offset, 1)[0]
                };

                var converted = (T)Convert.ChangeType(result, typeof(T));
                return converted;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                Logger.LogWarning(ex, "[Modbus] 读取地址 {Address} 失败 (尝试 {Attempt}/{Max})",
                    address, attempt + 1, MaxRetries + 1);
            }
        }

        Logger.LogError(lastException, "[Modbus] 读取地址 {Address} 在 {MaxRetries} 次重试后仍然失败",
            address, MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// 根据地址类型写入 Modbus 寄存器，支持自动重连和最多 2 次重试。
    /// </summary>
    protected override async Task WriteCoreAsync<T>(string address, T value, CancellationToken ct)
    {
        EnsureConnected();

        var (register, offset) = ParseAddress(address);
        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    var delayMs = attempt * 200;
                    await Task.Delay(delayMs, ct).ConfigureAwait(false);
                    TryReconnect();
                }

                switch (register)
                {
                    case 0:
                        // 写单个线圈
                        _client!.WriteSingleCoil(_unitId, (ushort)offset, Convert.ToBoolean(value));
                        break;
                    case 4:
                    default:
                        // 写单个保持寄存器
                        _client!.WriteSingleRegister(_unitId, (ushort)offset, Convert.ToUInt16(value));
                        break;
                }

                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                Logger.LogWarning(ex, "[Modbus] 写入地址 {Address} 失败 (尝试 {Attempt}/{Max})",
                    address, attempt + 1, MaxRetries + 1);
            }
        }

        Logger.LogError(lastException, "[Modbus] 写入地址 {Address} 在 {MaxRetries} 次重试后仍然失败",
            address, MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// 确保客户端已连接，若已断开则尝试自动重连。
    /// </summary>
    private void EnsureConnected()
    {
        if (_client == null)
            throw new InvalidOperationException("Modbus TCP 客户端未初始化");

        if (!_client.IsConnected)
        {
            Logger.LogWarning("[Modbus] 连接已断开，尝试自动重连");
            TryReconnect();
        }
    }

    /// <summary>
    /// 尝试重新连接到之前配置的 Modbus TCP 端点。
    /// </summary>
    private void TryReconnect()
    {
        if (_connectionConfig == null)
            throw new InvalidOperationException("Modbus TCP 无可用连接配置，无法重连");

        try
        {
            _client?.Dispose();
            _client = new ModbusTcpClient();
            var port = _connectionConfig.Port > 0 ? _connectionConfig.Port : 502;
            _client.Connect(new IPEndPoint(IPAddress.Parse(_connectionConfig.Host), port));
            Logger.LogInformation("[Modbus] 自动重连成功: {Host}:{Port}",
                _connectionConfig.Host, port);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[Modbus] 自动重连失败: {Host}:{Port}",
                _connectionConfig.Host, _connectionConfig.Port);
            throw new InvalidOperationException("Modbus TCP 自动重连失败", ex);
        }
    }

    /// <summary>
    /// 解析 Modbus 地址字符串，返回寄存器类型和偏移量。
    /// 支持格式：0x00001、1x00001、3x00001、4x00001，或纯数字（默认 4x 保持寄存器）。
    /// </summary>
    private static (int Register, int Offset) ParseAddress(string address)
    {
        if (address.Length >= 2 && address[1] == 'x')
        {
            var register = address[0] - '0';
            var offset = int.Parse(address[2..]) - 1; // Modbus 地址从 1 开始，偏移从 0 开始
            return (register, Math.Max(0, offset));
        }

        // 纯数字默认当作保持寄存器（4x）
        var rawOffset = int.Parse(address) - 1;
        return (4, Math.Max(0, rawOffset));
    }

    /// <summary>
    /// 释放资源，断开 Modbus 连接。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _client?.Disconnect();
        }
        catch
        {
            // 静默处理释放时的异常
        }

        _client?.Dispose();
        _client = null;
    }
}

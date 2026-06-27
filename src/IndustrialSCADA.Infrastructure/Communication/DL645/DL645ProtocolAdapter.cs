using System.IO;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;
using Sockets = System.Net.Sockets;

namespace IndustrialSCADA.Infrastructure.Communication.DL645;

/// <summary>
/// DL/T 645-2007 多功能电能表通信协议适配器。
/// 通过 TCP 串口服务器桥接实现与电能表的通信。
///
/// <para>帧格式：</para>
/// <code>
/// 前导符(FE FE FE FE) + 起始符(68) + 地址域(6字节BCD,低字节在前)
/// + 起始符(68) + 控制码(1字节) + 数据长度(1字节) + 数据域(变长,+33编码)
/// + 校验码(1字节) + 结束符(16)
/// </code>
///
/// <para>常用数据标识 (Data Item Identifiers)：</para>
/// <list type="bullet">
///   <item>00010000 — 正向有功总电能 (Forward active total energy)</item>
///   <item>00020000 — 反向有功总电能 (Reverse active total energy)</item>
///   <item>02010100 — A相电压 (A-phase voltage)</item>
///   <item>02020100 — A相电流 (A-phase current)</item>
///   <item>02030000 — 总有功功率 (Total active power)</item>
/// </list>
/// </summary>
public sealed class DL645ProtocolAdapter : ProtocolAdapterBase, IDisposable
{
    private Sockets.TcpClient? _tcpClient;
    private Sockets.NetworkStream? _stream;
    private byte[] _meterAddress = new byte[6];
    private bool _disposed;

    /// <summary>最大读取重试次数。</summary>
    private const int MaxRetries = 3;

    /// <summary>重试间隔（毫秒）。</summary>
    private const int RetryDelayMs = 500;

    /// <summary>默认串口服务器端口。</summary>
    private const int DefaultPort = 502;

    /// <summary>接收缓冲区大小。</summary>
    private const int BufferSize = 256;

    /// <summary>帧起始符。</summary>
    private const byte FrameStart = 0x68;

    /// <summary>帧结束符。</summary>
    private const byte FrameEnd = 0x16;

    /// <summary>前导符字节。</summary>
    private const byte Preamble = 0xFE;

    /// <summary>DL/T 645 数据编码偏移量（+33）。</summary>
    private const byte DataOffset = 0x33;

    /// <summary>读取数据请求功能码（D4~D0 = 01011）。</summary>
    private const byte ReadFunctionCode = 0x0B;

    /// <summary>
    /// 初始化 <see cref="DL645ProtocolAdapter"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    public DL645ProtocolAdapter(ILogger<DL645ProtocolAdapter> logger)
        : base(logger)
    {
    }

    /// <inheritdoc />
    public override string ProtocolName => "DL/T 645";

    /// <inheritdoc />
    public override ProtocolType ProtocolType => ProtocolType.DL645;

    /// <summary>
    /// 建立 DL/T 645 连接（通过 TCP 串口服务器桥接）。
    /// 可通过 <see cref="ConnectionConfig.Parameters"/> 指定 MeterAddress（表地址，12位BCD字符串）。
    /// </summary>
    protected override async Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct)
    {
        // 解析表地址
        if (!config.Parameters.TryGetValue("MeterAddress", out var addrRaw)
            || addrRaw is not string addrStr
            || addrStr.Length != 12)
        {
            throw new ArgumentException(
                "DL/T 645 需要在 Parameters 中提供 12 位 BCD 表地址 (MeterAddress)，例如 \"000000000001\"");
        }

        _meterAddress = ParseMeterAddress(addrStr);
        Logger.LogInformation("[DL645] 表地址: {Address}", addrStr);

        // 建立 TCP 连接（串口服务器桥接）
        var port = config.Port > 0 ? config.Port : DefaultPort;
        _tcpClient = new Sockets.TcpClient();
        _tcpClient.ReceiveTimeout = config.Timeout;
        _tcpClient.SendTimeout = config.Timeout;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(config.Timeout);

        try
        {
            await _tcpClient.ConnectAsync(config.Host, port, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"连接超时: {config.Host}:{port} (超时 {config.Timeout}ms)");
        }

        _stream = _tcpClient.GetStream();
        Logger.LogInformation("[DL645] 已连接到串口服务器 {Host}:{Port}", config.Host, port);
    }

    /// <summary>
    /// 断开 DL/T 645 连接，释放 TCP 资源。
    /// </summary>
    protected override Task DisconnectCoreAsync()
    {
        try
        {
            _stream?.Close();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[DL645] 关闭 NetworkStream 时出现异常");
        }
        finally
        {
            _stream = null;
        }

        try
        {
            _tcpClient?.Close();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[DL645] 关闭 TcpClient 时出现异常");
        }
        finally
        {
            _tcpClient = null;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 读取 DL/T 645 电能表数据，支持自动重连和最多 3 次重试。
    /// </summary>
    /// <typeparam name="T">返回数据类型（通常为 double 或 string）。</typeparam>
    /// <param name="address">数据标识符（8 位十六进制字符串，如 "00010000"）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>解析后的数据值。</returns>
    protected override async Task<T> ReadCoreAsync<T>(string address, CancellationToken ct)
    {
        EnsureConnected();

        if (string.IsNullOrEmpty(address) || address.Length != 8)
            throw new ArgumentException(
                "DL/T 645 数据标识必须为 8 位十六进制字符串（例如 \"00010000\"）", nameof(address));

        var dataItemId = ParseDataItemId(address);
        var frame = BuildReadFrame(_meterAddress, dataItemId);
        Exception? lastException = null;

        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                if (attempt > 0)
                {
                    Logger.LogWarning("[DL645] 第 {Attempt} 次重试读取数据标识 {DI}，延迟 {Delay}ms",
                        attempt, address, RetryDelayMs);
                    await Task.Delay(RetryDelayMs, ct).ConfigureAwait(false);

                    // 重试前尝试重连
                    TryReconnect();
                }

                // 发送请求帧
                await _stream!.WriteAsync(frame, 0, frame.Length, ct).ConfigureAwait(false);
                await _stream.FlushAsync(ct).ConfigureAwait(false);

                Logger.LogDebug("[DL645] 发送读取请求: DI={DI}, 帧={Frame}",
                    address, BitConverter.ToString(frame));

                // 接收响应
                var response = await ReceiveResponseAsync(ct).ConfigureAwait(false);

                // 解析响应
                var responseData = ParseResponse(response);

                // 转换为请求的类型
                var result = ConvertData<T>(responseData, address);

                Logger.LogDebug("[DL645] 成功读取 DI={DI}, 值={Value}", address, result);
                return result;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                lastException = ex;
                Logger.LogWarning(ex, "[DL645] 读取数据标识 {DI} 失败 (尝试 {Attempt}/{Max})",
                    address, attempt + 1, MaxRetries + 1);
            }
        }

        Logger.LogError(lastException, "[DL645] 读取数据标识 {DI} 在 {MaxRetries} 次重试后仍然失败",
            address, MaxRetries);
        throw lastException!;
    }

    /// <summary>
    /// DL/T 645 是主要用于读取电能表数据的协议，写操作通常不被支持。
    /// </summary>
    protected override Task WriteCoreAsync<T>(string address, T value, CancellationToken ct)
    {
        Logger.LogWarning(
            "[DL645] DL/T 645 协议主要用于读取电能表数据，不支持写操作。地址: {Address}, 值: {Value}",
            address, value);

        throw new NotSupportedException(
            $"DL/T 645 协议是只读的电能表数据采集协议，不支持写入操作。" +
            $"尝试写入地址: {address}，值: {value}。" +
            $"如需设置电能表参数，请使用电表厂商提供的专用工具。");
    }

    #region 帧构建

    /// <summary>
    /// 构建 DL/T 645-2007 读取请求帧。
    /// </summary>
    /// <param name="address">表地址（6 字节，MSB 在前）。</param>
    /// <param name="dataItemId">数据标识（4 字节，DI3 DI2 DI1 DI0 顺序）。</param>
    /// <returns>完整的请求帧字节数组。</returns>
    internal byte[] BuildReadFrame(byte[] address, byte[] dataItemId)
    {
        // 帧结构：前导符(4) + 68 + 地址(6) + 68 + 控制码(1) + 数据长度(1) + 数据(4) + 校验(1) + 16
        // 总长度 = 4 + 1 + 6 + 1 + 1 + 1 + 4 + 1 + 1 = 20
        var frame = new byte[20];
        int idx = 0;

        // 前导符: FE FE FE FE
        frame[idx++] = Preamble;
        frame[idx++] = Preamble;
        frame[idx++] = Preamble;
        frame[idx++] = Preamble;

        // 第一个起始符
        frame[idx++] = FrameStart;

        // 地址域（6 字节，低字节在前 — 需要反转 MSB-first 的存储顺序）
        for (int i = address.Length - 1; i >= 0; i--)
            frame[idx++] = address[i];

        // 第二个起始符
        frame[idx++] = FrameStart;

        // 控制码: 0x0B（读取数据，D7=0 主站发出，D5=0 无错误）
        frame[idx++] = ReadFunctionCode;

        // 数据长度: 4（仅数据标识，无附加数据）
        frame[idx++] = 0x04;

        // 数据域: DI0+33, DI1+33, DI2+33, DI3+33（低字节在前，每个字节加 0x33）
        // dataItemId 存储为 [DI3, DI2, DI1, DI0]，帧中传输顺序为 DI0, DI1, DI2, DI3
        for (int i = dataItemId.Length - 1; i >= 0; i--)
            frame[idx++] = (byte)(dataItemId[i] + DataOffset);

        // 校验码: 从第一个 68 开始到最后一个数据字节的累加和，取低 8 位
        // 第一个 68 的索引为 4
        frame[idx] = CalculateChecksum(frame, 4, idx - 4);
        idx++;

        // 结束符
        frame[idx] = FrameEnd;

        return frame;
    }

    #endregion

    #region 响应解析

    /// <summary>
    /// 接收并组装完整的 DL/T 645 响应帧。
    /// </summary>
    private async Task<byte[]> ReceiveResponseAsync(CancellationToken ct)
    {
        var buffer = new byte[BufferSize];
        int totalRead = 0;

        // 使用超时控制读取
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timeout = _config?.Timeout ?? 5000;
        cts.CancelAfter(timeout);

        try
        {
            // 持续读取直到检测到完整帧
            while (totalRead < BufferSize)
            {
                int bytesRead = await _stream!.ReadAsync(
                    buffer, totalRead, BufferSize - totalRead, cts.Token).ConfigureAwait(false);

                if (bytesRead == 0)
                    throw new IOException("串口服务器连接已关闭，未收到响应数据");

                totalRead += bytesRead;

                // 尝试检测帧是否完整
                if (IsFrameComplete(buffer, totalRead))
                    break;
            }
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new TimeoutException($"接收 DL/T 645 响应超时 (超时 {timeout}ms)");
        }

        var response = new byte[totalRead];
        Array.Copy(buffer, 0, response, 0, totalRead);

        Logger.LogDebug("[DL645] 收到响应: {Response}", BitConverter.ToString(response));
        return response;
    }

    /// <summary>
    /// 检测缓冲区中是否包含完整的 DL/T 645 响应帧。
    /// </summary>
    private static bool IsFrameComplete(byte[] buffer, int length)
    {
        // 至少需要: 68 + 地址(6) + 68 + 控制码(1) + 数据长度(1) + 校验(1) + 结束符(1) = 12 字节
        // 从第一个 68 开始检查
        int startIdx = -1;
        for (int i = 0; i < length; i++)
        {
            if (buffer[i] == FrameStart)
            {
                startIdx = i;
                break;
            }
        }

        if (startIdx < 0 || length - startIdx < 12)
            return false;

        // 读取数据长度（第一个 68 + 地址(6) + 第二个 68 + 控制码(1) + 数据长度）
        int dataLength = buffer[startIdx + 9];

        // 完整帧长度: 68(1) + 地址(6) + 68(1) + 控制码(1) + 数据长度(1) + 数据(N) + 校验(1) + 结束符(1)
        int expectedLength = startIdx + 12 + dataLength;
        return length >= expectedLength && buffer[expectedLength - 1] == FrameEnd;
    }

    /// <summary>
    /// 解析 DL/T 645 响应帧，提取数据域（已去除 +33 编码）。
    /// </summary>
    /// <param name="response">原始响应字节数组。</param>
    /// <returns>解码后的数据字节数组。</returns>
    /// <exception cref="InvalidOperationException">响应格式无效或包含错误标志。</exception>
    internal byte[] ParseResponse(byte[] response)
    {
        // 定位第一个 68
        int startIdx = -1;
        for (int i = 0; i < response.Length; i++)
        {
            if (response[i] == FrameStart)
            {
                startIdx = i;
                break;
            }
        }

        if (startIdx < 0)
            throw new InvalidOperationException("响应帧中未找到起始符 (0x68)");

        int remaining = response.Length - startIdx;

        // 最小帧长度: 68(1) + 地址(6) + 68(1) + 控制码(1) + 数据长度(1) + 校验(1) + 结束符(1) = 12
        if (remaining < 12)
            throw new InvalidOperationException($"响应帧长度不足: 需要至少 12 字节，实际 {remaining} 字节");

        // 验证第二个 68
        if (response[startIdx + 7] != FrameStart)
            throw new InvalidOperationException("响应帧中未找到第二个起始符 (0x68)");

        // 验证地址匹配
        for (int i = 0; i < 6; i++)
        {
            // 帧中地址是低字节在前，_meterAddress 是高字节在前
            if (response[startIdx + 1 + i] != _meterAddress[5 - i])
            {
                throw new InvalidOperationException(
                    $"响应帧地址不匹配: 期望表地址 {BitConverter.ToString(_meterAddress)}，" +
                    $"实际响应地址与预期不符");
            }
        }

        // 读取控制码
        byte controlCode = response[startIdx + 8];

        // 检查错误标志（D5 = bit 5）
        if ((controlCode & 0x20) != 0)
        {
            // 有错误 — 尝试解析错误码
            byte dataLength = response[startIdx + 9];
            if (dataLength >= 1)
            {
                // 错误信息在数据域中（第一个字节是错误码，需要减 33）
                byte errorCode = (byte)(response[startIdx + 10] - DataOffset);
                throw new InvalidOperationException(
                    $"电能表返回错误: 控制码=0x{controlCode:X2}, 错误码=0x{errorCode:X2} ({GetErrorDescription(errorCode)})");
            }

            throw new InvalidOperationException($"电能表返回错误: 控制码=0x{controlCode:X2}");
        }

        // 读取数据长度
        byte length = response[startIdx + 9];

        // 验证帧完整性
        if (remaining < 12 + length)
            throw new InvalidOperationException(
                $"响应帧数据不完整: 声明数据长度 {length}，但帧字节不足");

        // 验证结束符
        if (response[startIdx + 10 + length + 1] != FrameEnd)
            throw new InvalidOperationException(
                $"响应帧结束符错误: 期望 0x16，实际 0x{response[startIdx + 10 + length + 1]:X2}");

        // 验证校验码
        byte expectedChecksum = CalculateChecksum(response, startIdx, 10 + length);
        byte actualChecksum = response[startIdx + 10 + length];
        if (expectedChecksum != actualChecksum)
            throw new InvalidOperationException(
                $"响应帧校验码错误: 期望 0x{expectedChecksum:X2}，实际 0x{actualChecksum:X2}");

        // 提取并解码数据域（去除 +33 编码）
        byte[] rawData = new byte[length];
        Array.Copy(response, startIdx + 10, rawData, 0, length);

        return RemovePrefix(rawData);
    }

    #endregion

    #region 数据转换

    /// <summary>
    /// 将解码后的数据字节转换为请求的类型 <typeparamref name="T"/>。
    /// 数据字节在响应中为低字节在前（LSB first）。
    /// </summary>
    private T ConvertData<T>(byte[] data, string dataItemId)
    {
        var targetType = typeof(T);

        if (targetType == typeof(string))
        {
            // 返回 BCD 或 ASCII 编码的字符串表示
            var hexStr = BitConverter.ToString(data).Replace("-", " ");
            return (T)(object)hexStr;
        }

        if (targetType == typeof(double) || targetType == typeof(float))
        {
            // DL/T 645 数据通常为 BCD 编码，需要根据数据标识解析小数位
            double value = ParseBcdValue(data, dataItemId);
            return (T)(object)value;
        }

        if (targetType == typeof(int) || targetType == typeof(long))
        {
            double numericValue = ParseBcdValue(data, dataItemId);
            return (T)(object)Convert.ToInt64(numericValue);
        }

        if (targetType == typeof(byte[]))
        {
            return (T)(object)data;
        }

        // 通用回退: 尝试解析为 double，再转换为目标类型
        try
        {
            double fallbackValue = ParseBcdValue(data, dataItemId);
            return (T)Convert.ChangeType(fallbackValue, targetType);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"无法将 DL/T 645 数据转换为类型 {targetType.Name}: {BitConverter.ToString(data)}", ex);
        }
    }

    /// <summary>
    /// 将 BCD 编码的数据字节解析为 double 值。
    /// 根据数据标识自动确定小数位数。
    /// </summary>
    private static double ParseBcdValue(byte[] data, string dataItemId)
    {
        if (data.Length == 0)
            return 0.0;

        // 将每个字节视为两位 BCD 码，从最高字节到最低字节拼接
        // 数据在数组中是 LSB first（索引 0 是最低字节）
        var digits = new System.Text.StringBuilder();

        // 从最高有效字节到最低有效字节构建 BCD 数字字符串
        for (int i = data.Length - 1; i >= 0; i--)
        {
            byte b = data[i];
            int highNibble = (b >> 4) & 0x0F;
            int lowNibble = b & 0x0F;
            digits.Append(highNibble);
            digits.Append(lowNibble);
        }

        // 根据数据标识确定隐含小数位数
        int decimalPlaces = GetDecimalPlaces(dataItemId);

        // 解析为整数，再除以 10^decimalPlaces
        if (long.TryParse(digits.ToString(), out long rawValue))
        {
            return rawValue / Math.Pow(10, decimalPlaces);
        }

        return 0.0;
    }

    /// <summary>
    /// 根据数据标识返回隐含小数位数。
    /// </summary>
    private static int GetDecimalPlaces(string dataItemId)
    {
        return dataItemId.ToUpperInvariant() switch
        {
            // 电能类: 2 位小数 (kWh)
            "00010000" => 2, // 正向有功总电能
            "00020000" => 2, // 反向有功总电能
            "00010100" => 2, // 正向有功尖电能
            "00010200" => 2, // 正向有功峰电能
            "00010300" => 2, // 正向有功平电能
            "00010400" => 2, // 正向有功谷电能

            // 电压类: 1 位小数 (V)
            "02010100" => 1, // A相电压
            "02010200" => 1, // B相电压
            "02010300" => 1, // C相电压

            // 电流类: 3 位小数 (A)
            "02020100" => 3, // A相电流
            "02020200" => 3, // B相电流
            "02020300" => 3, // C相电流

            // 功率类: 4 位小数 (kW)
            "02030000" => 4, // 总有功功率
            "02030100" => 4, // A相有功功率
            "02030200" => 4, // B相有功功率
            "02030300" => 4, // C相有功功率

            // 频率: 2 位小数 (Hz)
            "02800002" => 2, // 频率

            // 功率因数: 3 位小数
            "02060000" => 3, // 总功率因数

            // 默认: 0 位小数
            _ => 0
        };
    }

    #endregion

    #region 辅助方法

    /// <summary>
    /// 计算 DL/T 645 校验码（从指定起始位置开始累加，取低 8 位）。
    /// </summary>
    /// <param name="frame">帧数据。</param>
    /// <param name="start">起始索引（通常为第一个 0x68 的位置）。</param>
    /// <param name="length">参与校验的字节数。</param>
    /// <returns>校验码。</returns>
    internal static byte CalculateChecksum(byte[] frame, int start, int length)
    {
        int sum = 0;
        for (int i = start; i < start + length; i++)
            sum += frame[i];
        return (byte)(sum & 0xFF);
    }

    /// <summary>
    /// 对数据字节数组执行 +33 编码（DL/T 645 数据域编码规则）。
    /// </summary>
    /// <param name="data">原始数据。</param>
    /// <param name="value">编码偏移量，默认为 0x33。</param>
    /// <returns>编码后的数据。</returns>
    internal static byte[] AddPrefix(byte[] data, byte value = DataOffset)
    {
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] + value);
        return result;
    }

    /// <summary>
    /// 对数据字节数组执行 -33 解码（DL/T 645 数据域解码规则）。
    /// </summary>
    /// <param name="data">编码后的数据。</param>
    /// <returns>解码后的数据。</returns>
    internal static byte[] RemovePrefix(byte[] data)
    {
        var result = new byte[data.Length];
        for (int i = 0; i < data.Length; i++)
            result[i] = (byte)(data[i] - DataOffset);
        return result;
    }

    /// <summary>
    /// 解析 12 位 BCD 表地址字符串为 6 字节数组（高字节在前存储）。
    /// 例如 "000000000001" => [0x00, 0x00, 0x00, 0x00, 0x00, 0x01]
    /// </summary>
    private static byte[] ParseMeterAddress(string addressStr)
    {
        var address = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            var pair = addressStr.Substring(i * 2, 2);
            address[i] = Convert.ToByte(pair, 16);
        }

        return address;
    }

    /// <summary>
    /// 解析 8 位十六进制数据标识字符串为 4 字节数组 [DI3, DI2, DI1, DI0]。
    /// 例如 "00010000" => [0x00, 0x01, 0x00, 0x00]
    /// </summary>
    private static byte[] ParseDataItemId(string dataItemIdStr)
    {
        if (dataItemIdStr.Length != 8)
            throw new ArgumentException("数据标识必须为 8 位十六进制字符串", nameof(dataItemIdStr));

        var id = new byte[4];
        for (int i = 0; i < 4; i++)
        {
            var pair = dataItemIdStr.Substring(i * 2, 2);
            id[i] = Convert.ToByte(pair, 16);
        }

        return id;
    }

    /// <summary>
    /// 获取 DL/T 645 错误码的中文描述。
    /// </summary>
    private static string GetErrorDescription(byte errorCode)
    {
        return errorCode switch
        {
            0x01 => "其他原因",
            0x02 => "无请求数据",
            0x04 => "密码错误",
            0x08 => "无权限",
            0x10 => "需要更高安全等级",
            _ => $"未知错误 (0x{errorCode:X2})"
        };
    }

    /// <summary>
    /// 确保 TCP 连接处于可用状态，若已断开则尝试自动重连。
    /// </summary>
    private void EnsureConnected()
    {
        if (_tcpClient == null || _stream == null)
            throw new InvalidOperationException("DL/T 645 TCP 连接未初始化");

        if (!_tcpClient.Connected)
        {
            Logger.LogWarning("[DL645] TCP 连接已断开，尝试自动重连");
            TryReconnect();
        }
    }

    /// <summary>
    /// 尝试重新连接到之前配置的串口服务器。
    /// </summary>
    private void TryReconnect()
    {
        if (_config == null)
            throw new InvalidOperationException("DL/T 645 无可用连接配置，无法重连");

        try
        {
            _stream?.Dispose();
            _stream = null;
            _tcpClient?.Dispose();

            var port = _config.Port > 0 ? _config.Port : DefaultPort;
            _tcpClient = new Sockets.TcpClient();
            _tcpClient.ReceiveTimeout = _config.Timeout;
            _tcpClient.SendTimeout = _config.Timeout;
            _tcpClient.Connect(_config.Host, port);
            _stream = _tcpClient.GetStream();

            Logger.LogInformation("[DL645] 自动重连成功: {Host}:{Port}", _config.Host, port);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[DL645] 自动重连失败: {Host}:{Port}", _config.Host, _config.Port);
            throw new InvalidOperationException("DL/T 645 TCP 自动重连失败", ex);
        }
    }

    #endregion

    #region IDisposable

    /// <summary>
    /// 释放资源，关闭 TCP 连接。
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _stream?.Close();
            _stream?.Dispose();
        }
        catch
        {
            // 静默处理释放时的异常
        }

        try
        {
            _tcpClient?.Close();
            _tcpClient?.Dispose();
        }
        catch
        {
            // 静默处理释放时的异常
        }

        _stream = null;
        _tcpClient = null;
    }

    #endregion
}

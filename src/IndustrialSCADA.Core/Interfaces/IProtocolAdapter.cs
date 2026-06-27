using IndustrialSCADA.Core.Enums;

namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 协议适配器接口，定义与工业设备通信的统一抽象。
/// 每种通信协议（S7、Modbus、OPC UA 等）需实现此接口。
/// </summary>
public interface IProtocolAdapter
{
    /// <summary>
    /// 协议名称，用于日志和界面显示（如 "S7-1200"、"ModbusTcp"）。
    /// </summary>
    string ProtocolName { get; }

    /// <summary>
    /// 当前是否已建立连接。
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// 适配器所实现的协议类型。
    /// </summary>
    ProtocolType ProtocolType { get; }

    /// <summary>
    /// 连接状态变化事件，当连接建立或断开时触发。
    /// </summary>
    event EventHandler<ConnectionStateChangedEventArgs> ConnectionStateChanged;

    /// <summary>
    /// 异步建立与设备的通信连接。
    /// </summary>
    /// <param name="config">连接配置参数。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default);

    /// <summary>
    /// 异步断开与设备的通信连接。
    /// </summary>
    /// <returns>表示异步操作的任务。</returns>
    Task DisconnectAsync();

    /// <summary>
    /// 从设备异步读取指定地址的数据。
    /// </summary>
    /// <typeparam name="T">期望返回的数据类型。</typeparam>
    /// <param name="address">设备地址（如 "DB100.DBW0"、"40001"）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>读取到的值。</returns>
    Task<T> ReadAsync<T>(string address, CancellationToken ct = default);

    /// <summary>
    /// 向设备指定地址异步写入数据。
    /// </summary>
    /// <typeparam name="T">写入数据的数据类型。</typeparam>
    /// <param name="address">设备地址。</param>
    /// <param name="value">要写入的值。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task WriteAsync<T>(string address, T value, CancellationToken ct = default);

    /// <summary>
    /// 订阅指定地址的数据流，按照给定间隔周期性推送数据点。
    /// </summary>
    /// <param name="address">设备地址。</param>
    /// <param name="interval">采集间隔。</param>
    /// <returns>数据点的可观察序列。</returns>
    IObservable<Entities.DataPoint> SubscribeStream(string address, TimeSpan interval);
}

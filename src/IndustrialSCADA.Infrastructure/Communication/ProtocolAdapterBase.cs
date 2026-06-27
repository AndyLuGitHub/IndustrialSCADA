using System.Reactive.Linq;
using IndustrialSCADA.Core.Entities;
using IndustrialSCADA.Core.Enums;
using IndustrialSCADA.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace IndustrialSCADA.Infrastructure.Communication;

/// <summary>
/// 协议适配器抽象基类，提供模板方法模式的通信骨架。
/// 子类只需实现 <c>ConnectCoreAsync</c>、<c>DisconnectCoreAsync</c>、
/// <c>ReadCoreAsync</c> 和 <c>WriteCoreAsync</c> 即可接入具体协议。
/// </summary>
public abstract class ProtocolAdapterBase : IProtocolAdapter
{
    /// <summary>当前是否已建立连接。</summary>
    protected bool _isConnected;

    /// <summary>当前连接配置。</summary>
    protected ConnectionConfig? _config;

    /// <summary>日志记录器。</summary>
    protected readonly ILogger Logger;

    /// <summary>
    /// 初始化 <see cref="ProtocolAdapterBase"/> 的新实例。
    /// </summary>
    /// <param name="logger">日志记录器。</param>
    protected ProtocolAdapterBase(ILogger logger)
    {
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string ProtocolName { get; }

    /// <inheritdoc />
    public bool IsConnected => _isConnected;

    /// <inheritdoc />
    public abstract ProtocolType ProtocolType { get; }

    /// <inheritdoc />
    public event EventHandler<ConnectionStateChangedEventArgs>? ConnectionStateChanged;

    /// <inheritdoc />
    public async Task ConnectAsync(ConnectionConfig config, CancellationToken ct = default)
    {
        _config = config;
        try
        {
            await ConnectCoreAsync(config, ct).ConfigureAwait(false);
            _isConnected = true;
            OnConnectionStateChanged(new ConnectionStateChangedEventArgs(true));
            Logger.LogInformation("[{Protocol}] 已连接到 {Host}:{Port}", ProtocolName, config.Host, config.Port);
        }
        catch (Exception ex)
        {
            _isConnected = false;
            OnConnectionStateChanged(new ConnectionStateChangedEventArgs(false, ex.Message));
            Logger.LogError(ex, "[{Protocol}] 连接失败: {Host}:{Port}", ProtocolName, config.Host, config.Port);
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex, "[{Protocol}] 断开连接时出现异常", ProtocolName);
        }
        finally
        {
            _isConnected = false;
            OnConnectionStateChanged(new ConnectionStateChangedEventArgs(false));
            Logger.LogInformation("[{Protocol}] 已断开连接", ProtocolName);
        }
    }

    /// <inheritdoc />
    public async Task<T> ReadAsync<T>(string address, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return await ReadCoreAsync<T>(address, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task WriteAsync<T>(string address, T value, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        await WriteCoreAsync(address, value, ct).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public IObservable<DataPoint> SubscribeStream(string address, TimeSpan interval)
    {
        return Observable
            .Interval(interval)
            .SelectMany(async _ =>
            {
                try
                {
                    var value = await ReadCoreAsync<object>(address, CancellationToken.None).ConfigureAwait(false);
                    return new DataPoint
                    {
                        TagName = address,
                        Address = address,
                        CurrentValue = value,
                        Quality = 192,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                }
                catch (Exception ex)
                {
                    Logger.LogDebug(ex, "[{Protocol}] 周期读取 {Address} 失败", ProtocolName, address);
                    return new DataPoint
                    {
                        TagName = address,
                        Address = address,
                        CurrentValue = null,
                        Quality = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    };
                }
            });
    }

    /// <summary>触发连接状态变化事件。</summary>
    /// <param name="e">事件参数。</param>
    protected virtual void OnConnectionStateChanged(ConnectionStateChangedEventArgs e)
    {
        ConnectionStateChanged?.Invoke(this, e);
    }

    /// <summary>子类实现：建立底层连接。</summary>
    protected abstract Task ConnectCoreAsync(ConnectionConfig config, CancellationToken ct);

    /// <summary>子类实现：断开底层连接。</summary>
    protected abstract Task DisconnectCoreAsync();

    /// <summary>子类实现：从设备读取数据。</summary>
    protected abstract Task<T> ReadCoreAsync<T>(string address, CancellationToken ct);

    /// <summary>子类实现：向设备写入数据。</summary>
    protected abstract Task WriteCoreAsync<T>(string address, T value, CancellationToken ct);
}

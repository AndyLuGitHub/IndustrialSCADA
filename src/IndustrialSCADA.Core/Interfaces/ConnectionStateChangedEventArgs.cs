namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 连接状态变化事件参数，携带连接状态和可选的错误信息。
/// </summary>
public class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化 <see cref="ConnectionStateChangedEventArgs"/> 的新实例。
    /// </summary>
    /// <param name="isConnected">当前连接状态。</param>
    /// <param name="errorMessage">错误信息（可选）。</param>
    public ConnectionStateChangedEventArgs(bool isConnected, string? errorMessage = null)
    {
        IsConnected = isConnected;
        ErrorMessage = errorMessage;
    }

    /// <summary>
    /// 当前是否已建立连接。
    /// </summary>
    public bool IsConnected { get; }

    /// <summary>
    /// 断连时的错误信息，连接成功时为 null。
    /// </summary>
    public string? ErrorMessage { get; }
}

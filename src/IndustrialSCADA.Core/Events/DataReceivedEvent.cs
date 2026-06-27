using IndustrialSCADA.Core.Entities;

namespace IndustrialSCADA.Core.Events;

/// <summary>
/// 数据接收事件，当从设备采集到新的数据点时发布。
/// 在 App/Infrastructure 层可包装为 Prism EventAggregator 事件使用。
/// </summary>
public class DataReceivedEvent
{
    /// <summary>
    /// 初始化 <see cref="DataReceivedEvent"/> 的新实例。
    /// </summary>
    /// <param name="dataPoint">接收到的数据点。</param>
    public DataReceivedEvent(DataPoint dataPoint)
    {
        DataPoint = dataPoint;
    }

    /// <summary>
    /// 接收到的数据点。
    /// </summary>
    public DataPoint DataPoint { get; }
}

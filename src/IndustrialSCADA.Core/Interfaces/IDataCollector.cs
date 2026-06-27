using IndustrialSCADA.Core.Entities;

namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 数据采集器接口，负责管理周期性数据扫描并提供统一的数据流。
/// </summary>
public interface IDataCollector
{
    /// <summary>
    /// 数据流，所有已采集的数据点通过此可观察序列推送。
    /// </summary>
    IObservable<DataPoint> DataStream { get; }

    /// <summary>
    /// 启动数据采集，开始按照各设备配置的扫描间隔周期性轮询。
    /// </summary>
    /// <returns>表示异步操作的任务。</returns>
    Task StartAsync();

    /// <summary>
    /// 停止数据采集，终止所有正在进行的轮询任务。
    /// </summary>
    /// <returns>表示异步操作的任务。</returns>
    Task StopAsync();

    /// <summary>
    /// 对指定设备的指定地址执行单次读取。
    /// </summary>
    /// <param name="deviceCode">设备编码。</param>
    /// <param name="address">数据地址。</param>
    /// <returns>读取到的数据点。</returns>
    Task<DataPoint> ReadOnceAsync(string deviceCode, string address);
}

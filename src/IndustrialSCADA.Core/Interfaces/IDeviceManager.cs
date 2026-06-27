using IndustrialSCADA.Core.Entities;

namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 设备管理器接口，负责设备的生命周期管理以及协议适配器的获取。
/// </summary>
public interface IDeviceManager
{
    /// <summary>
    /// 获取当前已注册的设备列表（只读视图）。
    /// </summary>
    IReadOnlyList<DeviceEntity> Devices { get; }

    /// <summary>
    /// 设备状态变化通知流，当任意设备的状态发生改变时推送该设备实体。
    /// </summary>
    IObservable<DeviceEntity> DeviceStatusChanged { get; }

    /// <summary>
    /// 异步添加一台新设备。
    /// </summary>
    /// <param name="device">设备实体。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task AddDeviceAsync(DeviceEntity device);

    /// <summary>
    /// 根据设备编码异步移除设备。
    /// </summary>
    /// <param name="deviceCode">设备编码。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task RemoveDeviceAsync(string deviceCode);

    /// <summary>
    /// 根据设备编码异步获取设备实体。
    /// </summary>
    /// <param name="deviceCode">设备编码。</param>
    /// <returns>设备实体，若未找到返回 null。</returns>
    Task<DeviceEntity?> GetDeviceAsync(string deviceCode);

    /// <summary>
    /// 获取指定设备对应的协议适配器实例。
    /// </summary>
    /// <param name="deviceCode">设备编码。</param>
    /// <returns>该设备使用的协议适配器。</returns>
    IProtocolAdapter GetProtocolAdapter(string deviceCode);
}

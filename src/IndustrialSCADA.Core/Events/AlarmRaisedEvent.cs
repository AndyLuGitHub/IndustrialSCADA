using IndustrialSCADA.Core.Entities;

namespace IndustrialSCADA.Core.Events;

/// <summary>
/// 报警触发事件，当系统产生新报警时发布。
/// 在 App/Infrastructure 层可包装为 Prism EventAggregator 事件使用。
/// </summary>
public class AlarmRaisedEvent
{
    /// <summary>
    /// 初始化 <see cref="AlarmRaisedEvent"/> 的新实例。
    /// </summary>
    /// <param name="alarm">触发的报警实体。</param>
    public AlarmRaisedEvent(AlarmEntity alarm)
    {
        Alarm = alarm;
    }

    /// <summary>
    /// 触发的报警实体。
    /// </summary>
    public AlarmEntity Alarm { get; }
}

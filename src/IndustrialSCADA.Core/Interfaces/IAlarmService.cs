using IndustrialSCADA.Core.Entities;

namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 报警服务接口，负责报警的产生、确认、处理及查询。
/// </summary>
public interface IAlarmService
{
    /// <summary>
    /// 报警流，当新报警产生或现有报警状态变更时推送。
    /// </summary>
    IObservable<AlarmEntity> AlarmStream { get; }

    /// <summary>
    /// 触发一条新报警。
    /// </summary>
    /// <param name="alarm">报警实体。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task RaiseAlarmAsync(AlarmEntity alarm);

    /// <summary>
    /// 确认指定报警，标记操作员已知晓。
    /// </summary>
    /// <param name="alarmId">报警唯一标识。</param>
    /// <param name="operator">确认操作员名称。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task AcknowledgeAsync(Guid alarmId, string @operator);

    /// <summary>
    /// 处理指定报警并填写处理备注。
    /// </summary>
    /// <param name="alarmId">报警唯一标识。</param>
    /// <param name="operator">处理操作员名称。</param>
    /// <param name="remark">处理备注信息。</param>
    /// <returns>表示异步操作的任务。</returns>
    Task HandleAsync(Guid alarmId, string @operator, string remark);

    /// <summary>
    /// 按照过滤条件查询报警记录。
    /// </summary>
    /// <param name="filter">报警过滤条件。</param>
    /// <returns>符合条件的报警列表。</returns>
    Task<IReadOnlyList<AlarmEntity>> QueryAsync(AlarmFilter filter);
}

namespace IndustrialSCADA.Core.Events;

/// <summary>
/// 导航事件，当用户或系统发起页面导航时发布。
/// 在 App/Infrastructure 层可包装为 Prism EventAggregator 事件使用。
/// </summary>
public class NavigateEvent
{
    /// <summary>
    /// 初始化 <see cref="NavigateEvent"/> 的新实例。
    /// </summary>
    /// <param name="viewName">目标视图名称。</param>
    /// <param name="parameter">可选的导航参数。</param>
    public NavigateEvent(string viewName, object? parameter = null)
    {
        ViewName = viewName;
        Parameter = parameter;
    }

    /// <summary>
    /// 目标视图名称。
    /// </summary>
    public string ViewName { get; }

    /// <summary>
    /// 可选的导航参数，用于向目标视图传递上下文数据。
    /// </summary>
    public object? Parameter { get; }
}

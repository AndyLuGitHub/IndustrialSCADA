namespace IndustrialSCADA.Core.Interfaces;

/// <summary>
/// 导航服务接口（简化版），用于在 ViewModel 层发起页面导航请求。
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 导航到指定视图。
    /// </summary>
    /// <param name="viewName">目标视图名称（通常对应 ViewModel 或 View 的注册键）。</param>
    /// <param name="parameter">可选的导航参数，用于向目标视图传递上下文数据。</param>
    void Navigate(string viewName, object? parameter = null);

    /// <summary>
    /// 导航完成事件，事件参数为目标视图名称。
    /// </summary>
    event EventHandler<string> Navigated;
}

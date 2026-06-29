using System.Threading.Tasks;

namespace CatClawMusic.Core.Interfaces;

/// <summary>
/// 导航服务接口 - 支持 MAUI Shell 导航
/// </summary>
public interface INavigationService
{
    /// <summary>
    /// 导航到指定路由
    /// </summary>
    Task NavigateToAsync(string route, Dictionary<string, object>? parameters = null);
    
    /// <summary>
    /// 返回上一页
    /// </summary>
    Task GoBackAsync();
    
    /// <summary>
    /// 切换 Tab（MAUI Shell TabBar）
    /// </summary>
    void SwitchTab(int tabIndex);
}

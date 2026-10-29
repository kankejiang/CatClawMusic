namespace CatClawMusic.WinUI.Infrastructure;

/// <summary>
/// 壳层导航宿主：由 <see cref="MainWindow"/> 实现，导航服务只依赖这个抽象，
/// 以免 Services 层反向依赖窗口类型。
/// </summary>
public interface INavigationHost
{
    /// <summary>显示指定路由的页面</summary>
    void ShowRoute(string route);

    /// <summary>切换底部/侧栏标签（0 发现，1 歌单，2 音乐库）</summary>
    void SwitchTab(int tabIndex);
}

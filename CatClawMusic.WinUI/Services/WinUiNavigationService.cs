using CatClawMusic.Core.Interfaces;
using CatClawMusic.WinUI.Infrastructure;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// WinUI 端导航服务。契约沿用 Core 的字符串路由（<see cref="INavigationService"/>），
/// 实际页面切换由壳层 <see cref="INavigationHost"/> 完成。
/// </summary>
public sealed class WinUiNavigationService : INavigationService
{
    private INavigationHost? _host;

    internal void Attach(INavigationHost host) => _host = host;

    public Task NavigateToAsync(string route, Dictionary<string, object>? parameters = null)
    {
        _host?.ShowRoute(route);
        return Task.CompletedTask;
    }

    public Task GoBackAsync()
    {
        _host?.GoBack();
        return Task.CompletedTask;
    }

    public void SwitchTab(int tabIndex)
    {
        _host?.SwitchTab(tabIndex);
    }
}

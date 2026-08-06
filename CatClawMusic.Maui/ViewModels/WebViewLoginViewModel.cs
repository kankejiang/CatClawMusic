using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// WebView 登录页 ViewModel：根据路由参数 platform 查找音源插件，
/// 持有其浏览器登录配置，接收 WebView 提取的 Cookie 后回传插件完成登录。
/// </summary>
public partial class WebViewLoginViewModel : ObservableObject, IQueryAttributable
{
    private readonly OnlineMusicAggregator _aggregator;

    /// <summary>当前音源插件（可能为 OnlineMusicAdapter 反射代理）</summary>
    private IOnlineMusicPlugin? _provider;

    /// <summary>插件提供的浏览器登录配置</summary>
    public BrowserLoginInfo? LoginInfo { get; private set; }

    /// <summary>是否已完成登录（避免重复回传）</summary>
    [ObservableProperty]
    private bool _loginCompleted;

    /// <summary>是否已初始化完成（供页面判断是否可加载 WebView）</summary>
    [ObservableProperty]
    private bool _isReady;

    public WebViewLoginViewModel(OnlineMusicAggregator aggregator)
    {
        _aggregator = aggregator;
    }

    /// <summary>路由参数注入：platform=netease 等</summary>
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("platform", out var platObj) && platObj is string platform)
        {
            foreach (var p in _aggregator.GetProviders())
            {
                if (string.Equals(p.PlatformName, platform, StringComparison.OrdinalIgnoreCase))
                {
                    _provider = p;
                    break;
                }
            }
        }
        if (_provider == null) return;

        try
        {
            LoginInfo = _provider.GetBrowserLoginInfoAsync().GetAwaiter().GetResult();
            IsReady = LoginInfo != null;
        }
        catch { IsReady = false; }
    }

    /// <summary>WebView 提取到 Cookie 后调用：回传插件并标记完成</summary>
    public async Task CompleteLoginAsync(string cookie)
    {
        if (LoginCompleted || _provider == null) return;
        try
        {
            await _provider.SetLoginCookieAsync(cookie);
            LoginCompleted = true;
        }
        catch { }
    }

    /// <summary>用户取消登录（返回按钮）</summary>
    public void Cancel()
    {
        // 无需清理；未完成时 provider 保持原登录状态
    }
}

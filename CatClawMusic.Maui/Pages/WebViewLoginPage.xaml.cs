using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 通用 WebView 登录页：根据插件提供的 <see cref="BrowserLoginInfo"/> 打开登录页，
/// 用户在真实网页中登录后，从 WebView 提取指定域名的 Cookie 回传插件。
/// <para>
/// Cookie 提取策略：
/// <list type="bullet">
///   <item>Android：通过 Android.Webkit.CookieManager 同步读取</item>
///   <item>Windows：通过 WebView2 CoreWebView2.CookieManager 异步读取</item>
///   <item>其他平台回退：手动拼接 document.cookie（仅 HttpOnly 失效）</item>
/// </list>
/// </para>
/// </summary>
public partial class WebViewLoginPage : ContentPage
{
    private readonly WebViewLoginViewModel _vm;

    /// <summary>登录 Cookie 轮询定时器：扫码确认后服务端即 Set-Cookie（MUSIC_U），
    /// 即使 SPA hash 路由跳转不触发 WebNavigated，也能检测到登录完成。</summary>
    private IDispatcherTimer? _cookieTimer;

    public WebViewLoginPage(WebViewLoginViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
        // 注意：路由参数（platform）在导航事务中才注入 ViewModel（IQueryAttributable），
        // 构造函数执行时 LoginInfo 尚未就绪。WebView 加载统一放到 OnNavigatedTo 完成。
    }

    /// <summary>
    /// 导航完成：此时路由参数已注入 ViewModel（ApplyQueryAttributes 已执行），
    /// 根据 LoginInfo 加载登录页或提示不支持。
    /// </summary>
    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        var info = _vm.LoginInfo;
        TitleLabel.Text = info?.Title ?? "账号登录";
        if (info?.LoginUrl is string url && !string.IsNullOrWhiteSpace(url))
        {
            if (!string.Equals(LoginWebView.Source?.ToString(), url, StringComparison.OrdinalIgnoreCase))
                LoginWebView.Source = new UrlWebViewSource { Url = url };
            StatusHint.Text = "请在下方页面完成登录，登录成功后自动返回";
            StartCookiePolling();
        }
        else
        {
            StatusHint.Text = "该音源暂不支持登录";
        }
    }

    /// <summary>启动 Cookie 轮询（每 1.5 秒检测一次成功标识 Cookie；不依赖 SPA hash 路由跳转）</summary>
    private void StartCookiePolling()
    {
        if (_cookieTimer != null || _vm.LoginInfo == null) return;
        _cookieTimer = Dispatcher.CreateTimer();
        _cookieTimer.Interval = TimeSpan.FromMilliseconds(1500);
        _cookieTimer.IsRepeating = true;
        _cookieTimer.Tick += async (_, _) => await CheckLoginByCookieAsync();
        _cookieTimer.Start();
    }

    private void StopCookiePolling()
    {
        if (_cookieTimer == null) return;
        _cookieTimer.Stop();
        _cookieTimer.Tick -= async (_, _) => await CheckLoginByCookieAsync();
        _cookieTimer = null;
    }

    /// <summary>轮询检查：指定域名的 Cookie 已包含全部成功标识（如 MUSIC_U）即完成登录</summary>
    private async Task CheckLoginByCookieAsync()
    {
        var info = _vm.LoginInfo;
        if (_vm.LoginCompleted || info == null) { StopCookiePolling(); return; }
        try
        {
            var cookie = await ExtractCookieAsync(info.CookieDomain);
            if (!string.IsNullOrWhiteSpace(cookie)
                && info.SuccessCookieNames.Count > 0
                && info.SuccessCookieNames.All(n => cookie.Contains(n + "=", StringComparison.OrdinalIgnoreCase)))
            {
                StopCookiePolling();
                await TryExtractCookieAndReturnAsync();
            }
        }
        catch { }
    }

    /// <summary>页面离开：停止轮询，避免悬挂定时器</summary>
    protected override void OnNavigatedFrom(NavigatedFromEventArgs args)
    {
        StopCookiePolling();
        base.OnNavigatedFrom(args);
    }

    /// <summary>返回按钮：直接返回上一页</summary>
    private void OnBackTapped(object? sender, EventArgs e)
    {
        _vm.Cancel();
        DesktopNavigation.GoBack();
    }

    /// <summary>"完成"按钮：手动触发 Cookie 提取（用户觉得已登录可主动点）</summary>
    private async void OnDoneTapped(object? sender, EventArgs e)
    {
        await TryExtractCookieAndReturnAsync();
    }

    /// <summary>WebView 导航完成：检查是否登录成功（URL 匹配 或 Cookie 存在）</summary>
    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success) return;

        var info = _vm.LoginInfo;
        if (info == null) return;

        // 1) URL 匹配检查
        var url = e.Url ?? "";
        bool urlMatched = false;
        if (!string.IsNullOrWhiteSpace(info.SuccessUrlPattern) &&
            url.Contains(info.SuccessUrlPattern, StringComparison.OrdinalIgnoreCase))
        {
            urlMatched = true;
        }

        // 2) Cookie 存在检查
        bool cookieReady = false;
        try
        {
            var cookie = await ExtractCookieAsync(info.CookieDomain);
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                // 检查成功标识 Cookie 是否全部存在
                if (info.SuccessCookieNames.Count > 0)
                {
                    cookieReady = info.SuccessCookieNames.All(n => cookie.Contains(n + "=", StringComparison.OrdinalIgnoreCase));
                }
                else
                {
                    // 未指定标识 Cookie：有任意 Cookie 即认为可能已登录
                    cookieReady = true;
                }
            }
        }
        catch { }

        if (urlMatched || cookieReady)
        {
            await TryExtractCookieAndReturnAsync();
        }
    }

    /// <summary>提取 Cookie 并回传插件，然后返回上一页</summary>
    private async Task TryExtractCookieAndReturnAsync()
    {
        var info = _vm.LoginInfo;
        if (info == null) { DesktopNavigation.GoBack(); return; }

        try
        {
            var cookie = await ExtractCookieAsync(info.CookieDomain);
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                StatusHint.Text = "登录成功，正在返回...";
                await _vm.CompleteLoginAsync(cookie);
                DesktopNavigation.GoBack();
                return;
            }
        }
        catch { }
        StatusHint.Text = "尚未检测到登录 Cookie，请先在页面中完成登录";
    }

    /// <summary>跨平台提取指定域名的完整 Cookie 字符串</summary>
    private Task<string> ExtractCookieAsync(string domain)
    {
#if ANDROID
        return Task.FromResult(ExtractCookieAndroid(domain));
#elif WINDOWS
        return ExtractCookieWindowsAsync(domain);
#else
        // 回退：通过 JS 读取 document.cookie（无法获取 HttpOnly Cookie）
        return LoginWebView.EvaluateJavaScriptAsync("document.cookie")
            .ContinueWith(t => t.Result?.ToString()?.Trim('"') ?? "", TaskScheduler.Default);
#endif
    }

#if ANDROID
    /// <summary>Android 平台：通过 CookieManager 同步读取指定域名的所有 Cookie</summary>
    private static string ExtractCookieAndroid(string domain)
    {
        try
        {
            var cm = global::Android.Webkit.CookieManager.Instance;
            var url = domain.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? domain
                : $"https://{domain}/";
            // 注意：GetCookie 是同步的，但可能有内部延迟（确保 cookie 同步）
            global::Android.Webkit.CookieManager.Instance.Flush();
            var raw = cm.GetCookie(url);
            return raw ?? "";
        }
        catch { return ""; }
    }
#endif

#if WINDOWS
    /// <summary>Windows 平台：通过 WebView2 CoreWebView2.CookieManager 异步读取</summary>
    private async Task<string> ExtractCookieWindowsAsync(string domain)
    {
        try
        {
            // 获取 WebView2 底层 CoreWebView2
            var handler = LoginWebView.Handler;
            if (handler == null) return "";
            var platformView = handler.PlatformView;
            if (platformView is not Microsoft.UI.Xaml.Controls.WebView2 wv2) return "";

            var core = wv2.CoreWebView2;
            if (core == null)
            {
                // 等待 CoreWebView2 初始化
                await wv2.EnsureCoreWebView2Async();
                core = wv2.CoreWebView2;
                if (core == null) return "";
            }

            var cookieList = await core.CookieManager.GetCookiesAsync($"https://{domain}/");
            if (cookieList == null || cookieList.Count == 0) return "";

            var parts = new List<string>();
            foreach (var c in cookieList)
            {
                parts.Add($"{c.Name}={c.Value}");
            }
            return string.Join("; ", parts);
        }
        catch { return ""; }
    }
#endif
}

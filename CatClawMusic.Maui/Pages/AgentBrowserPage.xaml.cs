using System.Text.Json;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services.AgentBrowser;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// Agent 控制的浏览器页：加载指定 URL，等待 JS 渲染后注入脚本提取页面正文，
/// 通过协调器回传给 Agent 工具（browser_open）。
/// 用户可见浏览过程，可手动关闭；Agent 请求超时（25s）由协调器兜底。
/// </summary>
public partial class AgentBrowserPage : ContentPage
{
    private readonly AgentBrowserCoordinator _coordinator;
    private readonly TaskCompletionSource<(string, string)>? _tcs;

    public AgentBrowserPage(AgentBrowserCoordinator coordinator)
    {
        InitializeComponent();
        _coordinator = coordinator;
        // 取走当前挂起的浏览器请求，页面加载完成后回传结果
        _tcs = coordinator.Pending;
    }

    /// <summary>导航到指定 URL（由协调器在 Agent 工具调用时触发）</summary>
    public void LoadUrl(string url)
    {
        try
        {
            var navUrl = url.Trim();
            if (!navUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !navUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                navUrl = "https://" + navUrl;
            BrowserWebView.Source = navUrl;
        }
        catch { }
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (_tcs == null) return;
        if (e.Result != WebNavigationResult.Success)
        {
            _tcs.TrySetResult((e.Url ?? "", ""));
            return;
        }

        // 等页面 JS 渲染（动态站点如天气/SPA 需要时间执行脚本）
        await Task.Delay(1200);

        try
        {
            var js = @"(function(){
                var t = document.title || '';
                var body = document.body;
                if (!body) return JSON.stringify({title:t,text:''});
                var clone = body.cloneNode(true);
                ['script','style','noscript','svg','nav','header','footer','aside','iframe','form'].forEach(function(s){
                    var els = clone.querySelectorAll(s);
                    for (var i = els.length - 1; i >= 0; i--) els[i].parentNode.removeChild(els[i]);
                });
                var text = (clone.innerText || clone.textContent || '')
                    .replace(/\u00a0/g,' ').replace(/[ \t]{2,}/g,' ')
                    .replace(/\n{3,}/g,'\n\n').trim().slice(0, 8000);
                return JSON.stringify({title:t,text:text});
            })();";

            var json = await BrowserWebView.EvaluateJavaScriptAsync(js);
            string title = "", text = "";
            if (!string.IsNullOrWhiteSpace(json))
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                title = root.TryGetProperty("title", out var tp) ? tp.GetString() ?? "" : "";
                text = root.TryGetProperty("text", out var sp) ? sp.GetString() ?? "" : "";
            }
            _tcs.TrySetResult((title, text));
        }
        catch
        {
            _tcs.TrySetResult((e.Url ?? "", ""));
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
        }
    }

    private void OnCloseTapped(object? sender, EventArgs e)
    {
        // 关闭页面；若 Agent 仍在等待，其结果由协调器超时兜底
        if (_tcs != null)
            _tcs.TrySetResult(("", ""));
        DesktopNavigation.GoBack();
    }
}

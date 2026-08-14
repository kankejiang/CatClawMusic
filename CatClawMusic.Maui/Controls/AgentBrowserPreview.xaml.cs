using System.Text.Json;
using CatClawMusic.Maui.Services.AgentBrowser;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// Agent 浏览器预览：聊天页面顶部的小窗口（不跳转页面）。
/// Agent 调用 browser_open 时显示并加载 URL，等 JS 渲染后注入脚本提取正文，
/// 通过协调器回传给 Agent 工具。可手动关闭；Agent 请求超时由协调器兜底。
/// 聊天页（SearchPage / DesktopDiscoverPage）在 OnAppearing 时注册为当前宿主。
/// </summary>
public partial class AgentBrowserPreview : ContentView
{
    public AgentBrowserPreview()
    {
        InitializeComponent();
        IsVisible = false;
    }

    /// <summary>显示预览并导航到指定 URL（由协调器在 Agent 工具调用时触发，已在主线程）</summary>
    public void Show(string url)
    {
        IsVisible = true;
        LoadingIndicator.IsRunning = true;
        UrlLabel.Text = url;
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

    /// <summary>手动关闭：隐藏预览，回传空结果（Agent 请求由协调器超时兜底）</summary>
    private void OnCloseTapped(object? sender, EventArgs e)
    {
        CloseAndNotify();
    }

    private void CloseAndNotify()
    {
        IsVisible = false;
        LoadingIndicator.IsRunning = false;
        try { BrowserWebView.Source = "about:blank"; } catch { }
        AgentBrowserCoordinator.Instance.OnPageLoaded("", "");
    }

    private async void OnWebViewNavigated(object? sender, WebNavigatedEventArgs e)
    {
        if (e.Result != WebNavigationResult.Success)
        {
            AgentBrowserCoordinator.Instance.OnPageLoaded("", "");
            LoadingIndicator.IsRunning = false;
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
            AgentBrowserCoordinator.Instance.OnPageLoaded(title, text);
        }
        catch
        {
            AgentBrowserCoordinator.Instance.OnPageLoaded("", "");
        }
        finally
        {
            LoadingIndicator.IsRunning = false;
        }
    }
}

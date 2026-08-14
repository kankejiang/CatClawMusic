using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Pages;

namespace CatClawMusic.Maui.Services.AgentBrowser;

/// <summary>
/// Agent 浏览器协调器：Agent 工具（browser_open）→ 打开内置浏览器页加载 URL →
/// 等待页面 JS 渲染完成 → 读取正文 → 返回给 Agent。
/// 与页面通过 TaskCompletionSource 关联：每次请求新建页面实例，
/// 页面加载完成后用当前请求的 TCS 回传提取结果。
/// </summary>
public class AgentBrowserCoordinator
{
    /// <summary>当前挂起的浏览器请求（页面构造时取走并完成）</summary>
    public TaskCompletionSource<(string title, string text)>? Pending { get; private set; }

    private readonly object _sync = new();

    /// <summary>导航并提取正文（后台线程调用，内部切主线程操作页面）</summary>
    public async Task<string?> NavigateAndExtractAsync(string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            Pending = tcs;
        }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                var page = new AgentBrowserPage(this);
                DesktopNavigation.PushEmbed(page);
                page.LoadUrl(url);
            });

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(25));
            var (title, text) = await tcs.Task.WaitAsync(cts.Token);

            if (string.IsNullOrWhiteSpace(text))
                return null;
            return $"标题: {title}\n正文: {text}";
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            Log.Debug("AgentBrowser", $"[AgentBrowser] 打开失败: {ex.Message}");
            return null;
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(Pending, tcs))
                    Pending = null;
            }
        }
    }

    /// <summary>页面加载完成时调用：把提取结果交给当前请求</summary>
    public void OnPageLoaded(string title, string text)
    {
        TaskCompletionSource<(string, string)>? tcs = null;
        lock (_sync)
        {
            tcs = Pending;
            Pending = null;
        }
        tcs?.TrySetResult((title, text));
    }
}

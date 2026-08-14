using CatClawMusic.Maui.Controls;

namespace CatClawMusic.Maui.Services.AgentBrowser;

/// <summary>
/// Agent 浏览器协调器（静态单例）：Agent 工具（browser_open）→ 聊天页顶部的
/// 浏览器预览小窗口（AgentBrowserPreview）加载 URL → 等待 JS 渲染 → 提取正文回传。
/// 宿主（聊天页）在 OnAppearing/OnDisappearing 时注册/注销；Agent 请求与页面
/// 通过 TaskCompletionSource 关联，25s 超时兜底。
/// </summary>
public class AgentBrowserCoordinator
{
    /// <summary>全局唯一实例（由 MauiProgram 在启动时初始化桥接）</summary>
    public static AgentBrowserCoordinator Instance { get; } = new();

    private readonly object _sync = new();
    private AgentBrowserPreview? _host;
    private TaskCompletionSource<(string title, string text)>? _pending;

    /// <summary>聊天页注册自己为浏览器宿主</summary>
    public void RegisterHost(AgentBrowserPreview host)
    {
        lock (_sync) { _host = host; }
    }

    /// <summary>聊天页离开时注销宿主</summary>
    public void UnregisterHost(AgentBrowserPreview host)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_host, host))
                _host = null;
        }
    }

    /// <summary>导航并提取正文（Agent 工具在后台线程调用，内部切主线程操作预览）</summary>
    public async Task<string?> NavigateAndExtractAsync(string url, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(string, string)>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync) { _pending = tcs; }

        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                AgentBrowserPreview? host;
                lock (_sync) { host = _host; }
                if (host == null)
                {
                    tcs.TrySetResult(("", ""));
                    return;
                }
                host.Show(url);
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
                if (ReferenceEquals(_pending, tcs))
                    _pending = null;
            }
        }
    }

    /// <summary>预览页加载完成时调用：把提取结果交给当前请求</summary>
    public void OnPageLoaded(string title, string text)
    {
        TaskCompletionSource<(string, string)>? tcs;
        lock (_sync)
        {
            tcs = _pending;
            _pending = null;
        }
        tcs?.TrySetResult((title, text));
    }
}

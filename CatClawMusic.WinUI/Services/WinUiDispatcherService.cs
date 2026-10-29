using CatClawMusic.Core.Interfaces;
using Microsoft.UI.Dispatching;

namespace CatClawMusic.WinUI.Services;

/// <summary>主线程调度器（替代 MAUI 的 <c>MainThread</c>）。必须在 UI 线程上构造。</summary>
public sealed class WinUiDispatcherService : IMainThreadDispatcher
{
    private readonly DispatcherQueue _queue =
        DispatcherQueue.GetForCurrentThread()
        ?? throw new InvalidOperationException("WinUiDispatcherService 必须在 UI 线程上创建。");

    public bool IsMainThread => _queue.HasThreadAccess;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (_queue.HasThreadAccess)
        {
            action();
            return;
        }

        _queue.TryEnqueue(() => action());
    }
}

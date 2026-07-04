using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Services;

/// <summary>主线程调度器，封装 MAUI MainThread API</summary>
public class MainThreadDispatcher : IMainThreadDispatcher
{
    /// <summary>获取当前是否在主线程执行</summary>
    public bool IsMainThread => MainThread.IsMainThread;

    /// <summary>将操作投递到主线程执行；若已在主线程则同步执行</summary>
    /// <param name="action">需要在主线程执行的操作</param>
    public void Post(Action action)
    {
        if (MainThread.IsMainThread)
            action();
        else
            MainThread.BeginInvokeOnMainThread(action);
    }
}

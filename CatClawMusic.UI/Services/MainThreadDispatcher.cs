using Android.OS;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Services;

/// <summary>主线程调度器，确保操作在主线程执行</summary>
public class MainThreadDispatcher : IMainThreadDispatcher
{
    private readonly Handler _mainHandler = new(Looper.MainLooper!);

    /// <summary>当前是否在主线程</summary>
    public bool IsMainThread => Looper.MyLooper() == Looper.MainLooper;

    /// <summary>将操作投递到主线程执行</summary>
    public void Post(Action action)
    {
        if (IsMainThread)
            action();
        else
            _mainHandler.Post(action);
    }
}

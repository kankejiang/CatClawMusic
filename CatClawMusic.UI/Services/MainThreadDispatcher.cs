using Android.OS;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.UI.Services;

public class MainThreadDispatcher : IMainThreadDispatcher
{
    private readonly Handler _mainHandler = new(Looper.MainLooper!);

    public bool IsMainThread => Looper.MyLooper() == Looper.MainLooper;

    public void Post(Action action)
    {
        if (IsMainThread)
            action();
        else
            _mainHandler.Post(action);
    }
}

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 播放界面可见性跟踪：正在播放页（NowPlayingPage）或全屏歌词页（FullLyricsPage）
/// 任一可见时视为「歌词表面可见」。AudioPlayerService 据此自适应播放位置更新频率：
/// 可见时 60fps（16ms，逐字歌词着色平滑），不可见时降到 5Hz（200ms，迷你播放器足够），
/// 避免用户浏览音乐库/发现/歌单等页面时，60Hz 的位置事件风暴（PropertyChanged 回投主线程 +
/// 迷你播放器进度条重绘 + 歌词计算）拖慢整个 App。
/// 使用引用计数：分页生命周期（MainPage 反射调用 OnAppearing/OnDisappearing）与
/// Shell 推入的页面叠加出现时计数正确。
/// </summary>
public static class PlayerSurfaceTracker
{
    private static int _refCount;

    /// <summary>可见性变化时触发（页面生命周期所在的主线程）</summary>
    public static event Action? VisibilityChanged;

    /// <summary>当前是否有歌词表面可见</summary>
    public static bool IsVisible => Volatile.Read(ref _refCount) > 0;

    /// <summary>标记一个歌词表面可见，返回释放令牌（与页面 OnDisappearing 配对调用 Dispose）</summary>
    public static IDisposable Acquire()
    {
        if (Interlocked.Increment(ref _refCount) == 1)
            VisibilityChanged?.Invoke();
        return new ReleaseToken();
    }

    /// <summary>引用计数释放令牌：仅第一次 Dispose 生效（页面 OnDisappearing 可能被重复调用）</summary>
    private sealed class ReleaseToken : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0) return;
            if (Interlocked.Decrement(ref PlayerSurfaceTracker._refCount) == 0)
                VisibilityChanged?.Invoke();
        }
    }
}

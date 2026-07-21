namespace CatClawMusic.Maui.Services;

public interface IInteractionStateService
{
    bool IsUserScrolling { get; }
    bool IsUserInteracting { get; }
    event EventHandler<bool>? ScrollStateChanged;
    event EventHandler<bool>? InteractionStateChanged;
    void NotifyScrollStarted();
    void NotifyScrollEnded();
    /// <summary>手指按下（含多指副指针）。由 Activity 的全局触摸分发调用。</summary>
    void NotifyTouchStarted();
    /// <summary>手指抬起/手势取消。最后一个手指抬起后延迟一小段时间才视为交互结束。</summary>
    void NotifyTouchEnded();
    /// <summary>安全网：Activity 进入后台等场景强制清零触摸计数，防止 UP 事件丢失导致交互状态永久卡死。</summary>
    void ResetTouchState();
    IDisposable BeginInteraction(string reason);
}

public class InteractionStateService : IInteractionStateService
{
    private int _scrollRefCount;
    private int _interactionRefCount;
    private int _touchRefCount;
    private readonly System.Timers.Timer _scrollIdleTimer;
    private readonly System.Timers.Timer _touchIdleTimer;
    private readonly object _lock = new();

    public bool IsUserScrolling => Volatile.Read(ref _scrollRefCount) > 0;
    public bool IsUserInteracting => Volatile.Read(ref _interactionRefCount) > 0 || IsUserScrolling
        || Volatile.Read(ref _touchRefCount) > 0;

    public event EventHandler<bool>? ScrollStateChanged;
    public event EventHandler<bool>? InteractionStateChanged;

    public InteractionStateService()
    {
        _scrollIdleTimer = new System.Timers.Timer(180);
        _scrollIdleTimer.AutoReset = false;
        _scrollIdleTimer.Elapsed += (s, e) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                lock (_lock)
                {
                    if (_scrollRefCount > 0)
                    {
                        _scrollRefCount = 0;
                        OnScrollStateChanged(false);
                        if (_interactionRefCount == 0)
                            OnInteractionStateChanged(false);
                    }
                }
            });
        };

        // 触摸结束后的宽限期：手指抬起后短暂停留再宣告交互结束，
        // 避免连续点按之间动画频繁停/启
        _touchIdleTimer = new System.Timers.Timer(300);
        _touchIdleTimer.AutoReset = false;
        _touchIdleTimer.Elapsed += (s, e) =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                lock (_lock)
                {
                    if (_touchRefCount > 0) return;  // 宽限期内又有新触摸
                    if (!IsUserInteracting)
                        OnInteractionStateChanged(false);
                }
            });
        };
    }

    public void NotifyScrollStarted()
    {
        lock (_lock)
        {
            bool wasScrolling = IsUserScrolling;
            bool wasInteracting = IsUserInteracting;
            _scrollRefCount++;
            _scrollIdleTimer.Stop();
            if (!wasScrolling)
                OnScrollStateChanged(true);
            if (!wasInteracting)
                OnInteractionStateChanged(true);
        }
    }

    public void NotifyScrollEnded()
    {
        _scrollIdleTimer.Stop();
        _scrollIdleTimer.Start();
    }

    public void NotifyTouchStarted()
    {
        lock (_lock)
        {
            bool wasInteracting = IsUserInteracting;
            _touchRefCount++;
            _touchIdleTimer.Stop();
            if (!wasInteracting)
                OnInteractionStateChanged(true);
        }
    }

    public void NotifyTouchEnded()
    {
        lock (_lock)
        {
            if (_touchRefCount > 0)
                _touchRefCount--;
            if (_touchRefCount == 0)
            {
                _touchIdleTimer.Stop();
                _touchIdleTimer.Start();
            }
        }
    }

    public void ResetTouchState()
    {
        lock (_lock)
        {
            if (_touchRefCount == 0) return;
            _touchRefCount = 0;
            _touchIdleTimer.Stop();
            if (!IsUserInteracting)
                OnInteractionStateChanged(false);
        }
    }

    public IDisposable BeginInteraction(string reason)
    {
        lock (_lock)
        {
            bool wasInteracting = IsUserInteracting;
            _interactionRefCount++;
            if (!wasInteracting)
                OnInteractionStateChanged(true);
        }
        return new InteractionToken(this);
    }

    private void EndInteraction()
    {
        lock (_lock)
        {
            if (_interactionRefCount > 0)
                _interactionRefCount--;
            if (_interactionRefCount == 0 && !IsUserScrolling)
                OnInteractionStateChanged(false);
        }
    }

    private void OnScrollStateChanged(bool scrolling)
    {
        ScrollStateChanged?.Invoke(this, scrolling);
    }

    private void OnInteractionStateChanged(bool interacting)
    {
        InteractionStateChanged?.Invoke(this, interacting);
    }

    private class InteractionToken : IDisposable
    {
        private InteractionStateService? _owner;
        public InteractionToken(InteractionStateService owner) => _owner = owner;
        public void Dispose()
        {
            _owner?.EndInteraction();
            _owner = null;
        }
    }
}

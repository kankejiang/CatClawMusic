namespace CatClawMusic.Maui.Services;

public interface IInteractionStateService
{
    bool IsUserScrolling { get; }
    bool IsUserInteracting { get; }
    event EventHandler<bool>? ScrollStateChanged;
    event EventHandler<bool>? InteractionStateChanged;
    void NotifyScrollStarted();
    void NotifyScrollEnded();
    IDisposable BeginInteraction(string reason);
}

public class InteractionStateService : IInteractionStateService
{
    private int _scrollRefCount;
    private int _interactionRefCount;
    private readonly System.Timers.Timer _scrollIdleTimer;
    private readonly object _lock = new();

    public bool IsUserScrolling => Volatile.Read(ref _scrollRefCount) > 0;
    public bool IsUserInteracting => Volatile.Read(ref _interactionRefCount) > 0 || IsUserScrolling;

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

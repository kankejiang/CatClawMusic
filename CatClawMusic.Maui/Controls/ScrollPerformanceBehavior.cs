using CatClawMusic.Maui.Services;

namespace CatClawMusic.Maui.Controls;

public class ScrollPerformanceBehavior : Behavior<View>
{
    private View? _associatedView;
    private IInteractionStateService? _stateService;
    private readonly System.Timers.Timer _scrollEndTimer;
    private bool _isScrolling;

    public static readonly BindableProperty OptimizeScrollProperty = BindableProperty.Create(
        nameof(OptimizeScroll), typeof(bool), typeof(ScrollPerformanceBehavior), true);

    public bool OptimizeScroll
    {
        get => (bool)GetValue(OptimizeScrollProperty);
        set => SetValue(OptimizeScrollProperty, value);
    }

    public ScrollPerformanceBehavior()
    {
        _scrollEndTimer = new System.Timers.Timer(180);
        _scrollEndTimer.AutoReset = false;
        _scrollEndTimer.Elapsed += OnScrollEndTimerElapsed;
    }

    private void OnScrollEndTimerElapsed(object? sender, System.Timers.ElapsedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_isScrolling)
            {
                _isScrolling = false;
                _stateService?.NotifyScrollEnded();
            }
        });
    }

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedView = bindable;

        _stateService = IPlatformApplication.Current?.Services
            .GetService<IInteractionStateService>();

        if (bindable is ItemsView itemsView)
        {
            itemsView.Scrolled += OnScrolled;
        }
        else if (bindable is ScrollView scrollView)
        {
            scrollView.Scrolled += OnScrollViewScrolled;
        }
    }

    protected override void OnDetachingFrom(View bindable)
    {
        if (bindable is ItemsView itemsView)
        {
            itemsView.Scrolled -= OnScrolled;
        }
        else if (bindable is ScrollView scrollView)
        {
            scrollView.Scrolled -= OnScrollViewScrolled;
        }

        _scrollEndTimer.Stop();
        _scrollEndTimer.Dispose();

        if (_isScrolling)
        {
            _isScrolling = false;
            _stateService?.NotifyScrollEnded();
        }

        _associatedView = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (!OptimizeScroll) return;
        HandleScroll();
    }

    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (!OptimizeScroll) return;
        HandleScroll();
    }

    private void HandleScroll()
    {
        if (!_isScrolling)
        {
            _isScrolling = true;
            _stateService?.NotifyScrollStarted();
        }
        _scrollEndTimer.Stop();
        _scrollEndTimer.Start();
    }
}

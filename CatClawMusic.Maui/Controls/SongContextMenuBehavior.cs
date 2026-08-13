namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 歌曲行手势行为（仅 Android 长按；Windows 右键由行 Loaded 时代码挂载的 PointerGestureRecognizer 处理）。
/// 触发时以行自身的 BindingContext（Song）为参数，沿逻辑父链找到 <see cref="ISongContextMenuHost"/> 页面弹出菜单。
///
/// 实现要点（Android）：
/// - 不能给行挂原生 LongClick：行变 long-clickable 后会在 ACTION_DOWN 时消费触摸，导致父容器
///   ItemContentView 的原生点击（列表选中）失效；
/// - 不能依赖 MAUI 手势识别器：行上有直接识别器时 MAUI 会装 OnTouchListener 消费 DOWN，
///   绕过 View.onTouchEvent，长按检测不触发；
/// 因此这里用「返回 false 的 OnTouchListener + 按下定时器」自行判定长按：
/// DOWN 不消费（列表点击/滚动不受影响），超时未移动/抬起即视为长按；触发时对父容器
/// PerformLongClick 置位 mHasPerformedLongPress，抑制松手后的点击。
/// 本行为不使用任何 XAML 绑定——MAUI 中 Behavior 不是 Element，RelativeSource 绑定应用到 Behavior
/// 会在 BindingContext 传播时抛异常，导致整行内容空白（此前歌单歌曲不显示的根因）。
/// </summary>
public class SongContextMenuBehavior : Behavior<View>
{
    private View? _view;
    private object? _platformView;

#if ANDROID
    private CancellationTokenSource? _longPressCts;
    private bool _isDown;
    private float _downRawX;
    private float _downRawY;
    private float _touchSlop = 24;
#endif

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _view = bindable;
        bindable.HandlerChanged += OnHandlerChanged;
        bindable.HandlerChanging += OnHandlerChanging;
        AttachIfReady();
    }

    protected override void OnDetachingFrom(View bindable)
    {
        DetachNative();
        bindable.HandlerChanged -= OnHandlerChanged;
        bindable.HandlerChanging -= OnHandlerChanging;
        _view = null;
        base.OnDetachingFrom(bindable);
    }

    private void OnHandlerChanged(object? sender, EventArgs e) => AttachIfReady();

    private void OnHandlerChanging(object? sender, HandlerChangingEventArgs e) => DetachNative();

    private void AttachIfReady()
    {
        if (_view?.Handler?.PlatformView == null) return;
        if (_platformView != null) return;

#if ANDROID
        if (_view.Handler.PlatformView is global::Android.Views.View androidView)
        {
            _platformView = androidView;
            try
            {
                _touchSlop = global::Android.Views.ViewConfiguration.Get(androidView.Context)?.ScaledTouchSlop ?? 24;
            }
            catch { }
            androidView.Touch += OnAndroidTouch;
        }
#endif
    }

    private void DetachNative()
    {
#if ANDROID
        CancelLongPressTimer();
        if (_platformView is global::Android.Views.View androidView)
        {
            androidView.Touch -= OnAndroidTouch;
            _platformView = null;
        }
#endif
    }

#if ANDROID
    private void OnAndroidTouch(object? sender, global::Android.Views.View.TouchEventArgs e)
    {
        var ev = e.Event;
        var view = sender as global::Android.Views.View;
        if (ev == null || view == null) return;

        switch (ev.ActionMasked)
        {
            case global::Android.Views.MotionEventActions.Down:
                _isDown = true;
                _downRawX = ev.RawX;
                _downRawY = ev.RawY;
                StartLongPressTimer(view);
                break;

            case global::Android.Views.MotionEventActions.Move:
                if (_isDown &&
                    (Math.Abs(ev.RawX - _downRawX) > _touchSlop || Math.Abs(ev.RawY - _downRawY) > _touchSlop))
                {
                    // 手指移动超出滑动阈值：视为滚动，取消长按判定
                    CancelLongPressTimer();
                    _isDown = false;
                }
                break;

            case global::Android.Views.MotionEventActions.Up:
            case global::Android.Views.MotionEventActions.Cancel:
            case global::Android.Views.MotionEventActions.PointerUp:
                CancelLongPressTimer();
                _isDown = false;
                break;
        }

        // 不消费任何触摸事件：列表点击、滚动全部照常
        e.Handled = false;
    }

    private void StartLongPressTimer(global::Android.Views.View view)
    {
        CancelLongPressTimer();
        _longPressCts = new CancellationTokenSource();
        var token = _longPressCts.Token;
        var timeout = global::Android.Views.ViewConfiguration.LongPressTimeout;

        _ = Task.Delay(timeout, token).ContinueWith(t =>
        {
            if (t.IsCanceled) return;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_isDown) return;
                _isDown = false;
                CancelLongPressTimer();
                OnLongPressed(view);
            });
        }, TaskScheduler.Default);
    }

    private void CancelLongPressTimer()
    {
        try { _longPressCts?.Cancel(); } catch { }
        try { _longPressCts?.Dispose(); } catch { }
        _longPressCts = null;
    }

    private void OnLongPressed(global::Android.Views.View view)
    {
        try
        {
            view.PerformHapticFeedback(global::Android.Views.FeedbackConstants.LongPress);
        }
        catch { }

        // 对父容器（ItemContentView，已 clickable）执行 PerformLongClick：
        // 内部会先置位 mHasPerformedLongPress，从而抑制松手后的点击（不触发选中/播放）
        try
        {
            (view.Parent as global::Android.Views.View)?.PerformLongClick();
        }
        catch { }

        ShowMenu();
    }
#endif

    private void ShowMenu()
    {
        var song = _view?.BindingContext as Song;
        if (song == null) return;

        var node = _view as Element;
        while (node != null)
        {
            if (node is ISongContextMenuHost host)
            {
                host.ShowSongMenu(song);
                return;
            }
            node = node.Parent;
        }
    }
}

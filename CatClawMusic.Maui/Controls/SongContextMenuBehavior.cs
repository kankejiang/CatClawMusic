namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 歌曲行手势行为（仅 Android 长按；Windows 右键由行上的 PointerGestureRecognizer 事件处理）。
/// 触发时直接以行自身的 BindingContext（Song）为参数，沿逻辑父链找到 <see cref="ISongContextMenuHost"/> 页面弹出菜单。
/// 注意：本行为不使用任何 XAML 绑定——MAUI 中 Behavior 不是 Element，RelativeSource 绑定应用到 Behavior
/// 会在 BindingContext 传播时抛异常，导致整行内容空白（此前歌单歌曲不显示的根因）。
/// </summary>
public class SongContextMenuBehavior : Behavior<View>
{
    private View? _view;
    private object? _platformView;

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
            androidView.LongClick += OnAndroidLongClick;
        }
#endif
    }

    private void DetachNative()
    {
#if ANDROID
        if (_platformView is global::Android.Views.View androidView)
        {
            androidView.LongClick -= OnAndroidLongClick;
            _platformView = null;
        }
#endif
    }

#if ANDROID
    private void OnAndroidLongClick(object? sender, global::Android.Views.View.LongClickEventArgs e)
    {
        try
        {
            (sender as global::Android.Views.View)?.PerformHapticFeedback(
                global::Android.Views.FeedbackConstants.LongPress);
        }
        catch { }
        ShowMenu();
        e.Handled = true;
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

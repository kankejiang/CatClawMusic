using System.Windows.Input;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 歌曲行上下文菜单手势行为：
/// - Android：原生 LongClick（长按）触发；
/// - Windows：原生 PointerPressed（右键）触发；
/// 触发时以行自身的 BindingContext（Song）作为参数执行 <see cref="Command"/>。
/// 用于歌单详情 / 全部歌曲（本地音乐）列表行，实现长按/右键弹出歌曲操作菜单。
/// </summary>
public class SongContextMenuBehavior : Behavior<View>
{
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(SongContextMenuBehavior), null);

    /// <summary>触发菜单时要执行的命令（参数为当前行的 Song）。</summary>
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

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
#elif WINDOWS
        if (_view.Handler.PlatformView is global::Microsoft.UI.Xaml.UIElement uiElement)
        {
            _platformView = uiElement;
            uiElement.PointerPressed += OnWinPointerPressed;
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
#elif WINDOWS
        if (_platformView is global::Microsoft.UI.Xaml.UIElement uiElement)
        {
            uiElement.PointerPressed -= OnWinPointerPressed;
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
        ExecuteCommand();
        e.Handled = true;
    }
#elif WINDOWS
    private void OnWinPointerPressed(object sender, global::Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint((global::Microsoft.UI.Xaml.UIElement)sender);
        if (point.Properties.IsRightButtonPressed)
        {
            ExecuteCommand();
            e.Handled = true;
        }
    }
#endif

    private void ExecuteCommand()
    {
        var command = Command;
        if (command == null) return;
        var parameter = _view?.BindingContext;
        if (command.CanExecute(parameter))
            command.Execute(parameter);
    }
}

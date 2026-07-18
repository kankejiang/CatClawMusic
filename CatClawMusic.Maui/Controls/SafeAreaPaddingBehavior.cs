using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 附加到 Grid 上的行为，自动根据系统栏高度（状态栏）设置顶部 SafeArea padding。
/// 用于通过 Shell 导航的二级页面，让内容避开状态栏区域。
/// 不会覆盖 Grid 原有的水平方向 Padding，仅在顶部叠加系统栏高度。
/// </summary>
public class SafeAreaPaddingBehavior : Behavior<Layout>
{
    private Layout? _associatedLayout;
    private double _originalTopPadding;

    /// <summary>附加到 Layout 时触发，订阅 SafeArea 变化事件并立即应用 padding</summary>
    /// <param name="bindable">目标 Layout</param>
    protected override void OnAttachedTo(Layout bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedLayout = bindable;
        _originalTopPadding = bindable.Padding.Top;
        SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
        ApplyPadding();
    }

    /// <summary>从 Layout 分离时触发，取消订阅并恢复原始 padding</summary>
    /// <param name="bindable">目标 Layout</param>
    protected override void OnDetachingFrom(Layout bindable)
    {
        SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
        _associatedLayout = null;
        base.OnDetachingFrom(bindable);
    }

    /// <summary>系统栏高度变化时触发，重新应用 padding</summary>
    private void OnSafeAreaChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(ApplyPadding);
    }

    /// <summary>应用 SafeArea 顶部 padding（保留原有水平 padding，顶部加上状态栏高度）</summary>
    private void ApplyPadding()
    {
        if (_associatedLayout == null) return;
        var top = SafeAreaHelper.TopInset;
#if ANDROID
        // 兜底：EdgeToEdge 回调可能在首帧之后才触发，TopInset 可能为 0。
        // 状态栏高度通常 >= 24dp，用此值兜底避免顶部控件侵入状态栏。
        if (top < 1) top = 24;
#endif
        _associatedLayout.Padding = new Thickness(
            _associatedLayout.Padding.Left,
            _originalTopPadding + top,
            _associatedLayout.Padding.Right,
            _associatedLayout.Padding.Bottom);
    }
}

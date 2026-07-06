using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 附加到 Grid 上的行为，自动根据系统栏高度（状态栏）设置顶部 SafeArea padding。
/// 用于通过 Shell 导航的二级页面，让内容避开状态栏区域。
/// 不会覆盖 Grid 原有的水平方向 Padding，仅在顶部叠加系统栏高度。
/// </summary>
public class SafeAreaPaddingBehavior : Behavior<Grid>
{
    private Grid? _associatedGrid;
    private double _originalTopPadding;

    /// <summary>附加到 Grid 时触发，订阅 SafeArea 变化事件并立即应用 padding</summary>
    /// <param name="bindable">目标 Grid</param>
    protected override void OnAttachedTo(Grid bindable)
    {
        base.OnAttachedTo(bindable);
        _associatedGrid = bindable;
        _originalTopPadding = bindable.Padding.Top;
        SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
        ApplyPadding();
    }

    /// <summary>从 Grid 分离时触发，取消订阅并恢复原始 padding</summary>
    /// <param name="bindable">目标 Grid</param>
    protected override void OnDetachingFrom(Grid bindable)
    {
        SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
        _associatedGrid = null;
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
        if (_associatedGrid == null) return;
        var top = SafeAreaHelper.TopInset;
        _associatedGrid.Padding = new Thickness(
            _associatedGrid.Padding.Left,
            _originalTopPadding + top,
            _associatedGrid.Padding.Right,
            _associatedGrid.Padding.Bottom);
    }
}

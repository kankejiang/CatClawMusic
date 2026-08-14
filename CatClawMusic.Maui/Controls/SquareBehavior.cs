namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 正方形行为：元素宽度确定后把高度同步为宽度（宽=高）。
/// 用于网格/横滑布局中需要随列宽自适应为正方形的封面（推荐专辑卡等），
/// 无需手动按列宽计算固定高度。
/// </summary>
public class SquareBehavior : Behavior<VisualElement>
{
    protected override void OnAttachedTo(VisualElement bindable)
    {
        base.OnAttachedTo(bindable);
        bindable.SizeChanged += OnSizeChanged;
        // 已布局完成时补一次同步（Behavior 挂载可能晚于首次布局）
        if (bindable.Width > 0)
            bindable.HeightRequest = bindable.Width;
    }

    protected override void OnDetachingFrom(VisualElement bindable)
    {
        bindable.SizeChanged -= OnSizeChanged;
        base.OnDetachingFrom(bindable);
    }

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (sender is not VisualElement v || v.Width <= 0) return;
        // 幂等：宽度不变时 HeightRequest 相同，不会循环触发
        v.HeightRequest = v.Width;
    }
}

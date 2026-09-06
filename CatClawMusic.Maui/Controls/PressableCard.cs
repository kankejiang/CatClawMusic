namespace CatClawMusic.Maui.Controls;

/// <summary>带按压高亮的玻璃卡片：按下时卡面主色淡染 + 主色描边（网易云式按压态），
/// 松开/移出恢复主题卡色（SetDynamicResource 恢复，继续跟随深色模式切换）。
/// 构造时自挂指针手势并用闭包捕获自身——不依赖 GestureRecognizer.Parent
/// （DataTemplate 内 XAML 挂手势时 handler 拿不到所属视图）。</summary>
public class PressableCard : Border
{
    public PressableCard()
    {
        var pointer = new PointerGestureRecognizer();
        pointer.PointerPressed += (_, _) => OnPressed();
        pointer.PointerReleased += (_, _) => OnReleased();
        pointer.PointerExited += (_, _) => OnReleased();
        GestureRecognizers.Add(pointer);
    }

    private void OnPressed()
    {
        var res = Application.Current?.Resources;
        if (res == null) return;
        var primary = (Color)res["PrimaryColor"];
        BackgroundColor = primary.WithAlpha(0.14f);
        Stroke = primary.WithAlpha(0.4f);
    }

    private void OnReleased()
    {
        SetDynamicResource(BackgroundColorProperty, "CardBackgroundColor");
        SetDynamicResource(StrokeProperty, "GlassStrokeColor");
    }
}

namespace CatClawMusic.Maui.Controls;

/// <summary>带按压高亮的玻璃卡片：按下时卡面主色淡染 + 主色描边（网易云式按压态），
/// 松开/移出恢复主题卡色（ClearValue 移除本地值，由 GlassCardStyle 的 DynamicResource
/// setter 重新接管，继续跟随深色模式切换）。
/// 构造时自挂指针手势并用闭包捕获自身——不依赖 GestureRecognizer.Parent
/// （DataTemplate 内 XAML 挂手势时 handler 拿不到所属视图）。</summary>
public class PressableCard : Border
{
    private CancellationTokenSource? _resetCts;

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

        // Android 上卡片同时挂 TapGestureRecognizer 时，快速点击的 PointerReleased
        // 会被 tap 手势竞争吞掉（长按反而正常触发）——Task.Delay 兜底恢复，避免高亮残留
        _ = ResetAfterDelayAsync();
    }

    private async Task ResetAfterDelayAsync()
    {
        _resetCts?.Cancel();
        _resetCts = new CancellationTokenSource();
        var token = _resetCts.Token;
        try
        {
            await Task.Delay(500, token);
            if (token.IsCancellationRequested) return;
            if (MainThread.IsMainThread) OnReleased();
            else MainThread.BeginInvokeOnMainThread(OnReleased);
        }
        catch (TaskCanceledException) { }
    }

    private void OnReleased()
    {
        _resetCts?.Cancel();
        // ClearValue 移除本地覆盖值 → GlassCardStyle 的 DynamicResource setter 重新接管
        ClearValue(BackgroundColorProperty);
        ClearValue(StrokeProperty);
    }
}

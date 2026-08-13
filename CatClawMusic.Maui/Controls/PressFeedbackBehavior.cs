using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 通用点击按压反馈：指针按下时缩小 + 变暗，松开/移出后恢复原始状态。
/// 用于 Border / Grid / Label 等非标准 Button 的可点击控件（挂到 Behaviors 集合）。
/// 标准 Button / ImageButton 已由全局隐式样式（Styles.xaml）提供反馈，无需重复挂载。
/// </summary>
public class PressFeedbackBehavior : Behavior<View>
{
    /// <summary>按压时的目标透明度（0~1）</summary>
    public static readonly BindableProperty PressedOpacityProperty =
        BindableProperty.Create(nameof(PressedOpacity), typeof(double), typeof(PressFeedbackBehavior), 0.62);

    /// <summary>按压时的目标缩放（1=原大）</summary>
    public static readonly BindableProperty PressedScaleProperty =
        BindableProperty.Create(nameof(PressedScale), typeof(double), typeof(PressFeedbackBehavior), 0.96);

    public double PressedOpacity
    {
        get => (double)GetValue(PressedOpacityProperty);
        set => SetValue(PressedOpacityProperty, value);
    }

    public double PressedScale
    {
        get => (double)GetValue(PressedScaleProperty);
        set => SetValue(PressedScaleProperty, value);
    }

    private const string FadeAnimName = "PressFeedbackFade";
    private const string ScaleAnimName = "PressFeedbackScale";

    private readonly PointerGestureRecognizer _pointer = new();
    private View? _view;
    private bool _pressed;
    private double _origOpacity = 1;
    private double _origScale = 1;

    protected override void OnAttachedTo(View bindable)
    {
        base.OnAttachedTo(bindable);
        _view = bindable;
        _pointer.PointerPressed += OnPressed;
        _pointer.PointerReleased += OnReleased;
        _pointer.PointerExited += OnReleased;
        
        bindable.GestureRecognizers.Add(_pointer);
    }

    protected override void OnDetachingFrom(View bindable)
    {
        _pointer.PointerPressed -= OnPressed;
        _pointer.PointerReleased -= OnReleased;
        _pointer.PointerExited -= OnReleased;
        
        bindable.GestureRecognizers.Remove(_pointer);
        _view = null;
        base.OnDetachingFrom(bindable);
    }

    private async void OnPressed(object? sender, PointerEventArgs e)
    {
        if (_pressed || _view == null) return;
        _pressed = true;
        // 记录原始状态：松开时恢复，避免把控件本来的半透明/缩放覆盖成 1.0
        _origOpacity = _view.Opacity;
        _origScale = _view.Scale;

        _view.AbortAnimation(FadeAnimName);
        _view.AbortAnimation(ScaleAnimName);
        await Task.WhenAll(
            _view.ScaleTo(PressedScale, 80, Easing.CubicOut),
            _view.FadeTo(PressedOpacity, 80, Easing.CubicOut));
    }

    private async void OnReleased(object? sender, PointerEventArgs e)
    {
        if (!_pressed || _view == null) return;
        _pressed = false;

        _view.AbortAnimation(FadeAnimName);
        _view.AbortAnimation(ScaleAnimName);
        await Task.WhenAll(
            _view.ScaleTo(_origScale, 140, Easing.CubicOut),
            _view.FadeTo(_origOpacity, 140, Easing.CubicOut));
    }
}

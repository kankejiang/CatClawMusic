using System.Linq;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Platform;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.Graphics.Canvas.Effects;
using CatClawMusic.Maui.Effects;

using WinFrameworkElement = Microsoft.UI.Xaml.FrameworkElement;
using WinSizeChangedEventArgs = Microsoft.UI.Xaml.SizeChangedEventArgs;
using Vector2 = System.Numerics.Vector2;

namespace CatClawMusic.Maui.Platforms.Windows.Effects;

/// <summary>
/// 歌词行高斯模糊的 Windows 平台实现。
///
/// 做法：用 Win2D 的 <see cref="GaussianBlurEffect"/> 配合 Composition 的
/// <see cref="CompositionVisualSurface"/> 把该行（文字 + 翻译）自身内容重定向成一个模糊的
/// SpriteVisual，作为该行所在父容器的**兄弟**节点叠在上面——从而让整行一起失焦，
/// 营造"离当前行越远越模糊"的景深。
///
/// 关键点：sprite 必须是视觉树中该元素（SourceVisual）的**兄弟**而非子节点，
/// 否则 VisualSurface 会把 sprite 自己也采进来源，形成无限反馈（越糊越糊 / 卡死）。
    /// 当前行 BlurAmount=0，隐藏模糊层（sprite 保留复用，仅设 Opacity=0）保持清晰。
    /// </summary>
public class LyricBlurPlatformEffect : PlatformEffect
{
    private WinFrameworkElement? _ctl;
    // 模糊层核心对象：只创建一次并跨帧复用，避免每帧重建 CompositionVisualSurface
    // （重新捕获文字视觉树是闪烁/卡顿的真正来源，会让"变清晰"看起来像跳变）。
    private SpriteVisual? _sprite;
    private CompositionVisualSurface? _surface;
    private CompositionSurfaceBrush? _surfaceBrush;

    protected override void OnAttached()
    {
        _ctl = Control as WinFrameworkElement;
        if (_ctl is not null)
            _ctl.SizeChanged += OnSizeChanged;
        UpdateBlur();
    }

    protected override void OnDetached()
    {
        if (_ctl is not null)
            _ctl.SizeChanged -= OnSizeChanged;
        _ctl = null;
        RemoveSprite();
    }

    protected override void OnElementPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnElementPropertyChanged(e);
        if (e.PropertyName == LyricBlurEffect.BlurAmountProperty.PropertyName)
            UpdateBlur();
    }

    private void OnSizeChanged(object sender, WinSizeChangedEventArgs e) => UpdateBlur();

    private void RemoveSprite()
    {
        if (_sprite is { } sprite && sprite.Parent is ContainerVisual parent)
            parent.Children.Remove(sprite);
        _sprite = null;
        _surface = null;
        _surfaceBrush = null;
    }

    private void UpdateBlur()
    {
        try
        {
            if (_ctl is null) return;

            var amount = LyricBlurEffect.GetBlurAmount(Element);
            var visual = ElementCompositionPreview.GetElementVisual(_ctl);
            var parent = visual.Parent as ContainerVisual;
            if (parent is null) return;

            var w = (float)_ctl.ActualWidth;
            var h = (float)_ctl.ActualHeight;
            if (w <= 0 || h <= 0) return;

            // 首次（或模糊从 0 首度变正）才创建 surface + sprite，之后一直复用，
            // 只有轻量的高斯模糊 effect/brush 会随模糊半径变化重建。
            if (_sprite is null || _surface is null)
            {
                _surface = visual.Compositor.CreateVisualSurface();
                _surface.SourceVisual = visual;
                _surfaceBrush = visual.Compositor.CreateSurfaceBrush(_surface);
                _sprite = visual.Compositor.CreateSpriteVisual();
                parent.Children.InsertAtTop(_sprite); // 作为兄弟节点覆盖在文字之上
            }

            // 同步尺寸与位置：文字字号/行距变化时模糊层要始终贴合文字，否则会错位。
            _surface.SourceSize = new Vector2(w, h);
            _sprite.Size = new Vector2(w, h);
            _sprite.Offset = visual.Offset;

            if (amount <= 0.01)
            {
                // 清晰：隐藏模糊层（保留对象，下一帧若再变糊可直接复用）
                _sprite.Opacity = 0;
                return;
            }
            _sprite.Opacity = 1;

            // 只重建极轻量的模糊 effect + brush（surface 与 sprite 复用，不重新捕获视觉树），
            // 这样每一帧的模糊半径变化都能平滑反映，呈现"缓缓变清晰 / 缓缓变糊"的渐变。
            var blur = new GaussianBlurEffect
            {
                Name = "LyricBlur",
                BlurAmount = (float)amount,
                BorderMode = EffectBorderMode.Soft,
                Source = new CompositionEffectSourceParameter("src"),
            };
            var factory = visual.Compositor.CreateEffectFactory(blur);
            var brush = factory.CreateBrush();
            brush.SetSourceParameter("src", _surfaceBrush);
            _sprite.Brush = brush;
        }
        catch
        {
            // 模糊仅为视觉增强，任何异常都不应影响歌词功能
        }
    }
}

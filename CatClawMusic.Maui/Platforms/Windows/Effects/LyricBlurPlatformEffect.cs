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
using Vector3 = System.Numerics.Vector3;

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
    // 模糊画刷同样跨帧复用：BlurAmount 注册为可动画参数，逐帧只写标量，
    // 不再每帧 CreateEffectFactory/CreateBrush（那在 380ms×60fps 的过渡里是明显的浪费）。
    private CompositionEffectBrush? _blurBrush;

    /// <summary>
    /// 模糊向外溢出的留白（每边 px）。
    ///
    /// 高斯模糊会让笔画往外扩散，若采样区域与 sprite 都严格等于控件尺寸，
    /// 扩散出去的部分会被硬裁在行边界上——边缘反而显得锐利，整行看着"没糊"。
    /// 这里把采样源与 sprite 都向四周扩 <c>BlurPadding</c>，让柔边有地方生长。
    /// 取固定值（≥ 最大模糊半径的约 2 倍）而非随半径动态变化，
    /// 这样逐帧动画只需写一个标量，不必反复改 surface / sprite 尺寸。
    /// </summary>
    private const float BlurPadding = 24f;

    /// <summary>
    /// 模糊副本的亮度增益。
    ///
    /// 模糊 sprite 是叠在清晰原文**之上**的兄弟节点，二者亮度若 1:1，
    /// 清晰笔画会完全压过模糊副本 —— 肉眼看到的仍是一行清楚的字。
    /// 把模糊层整体提亮，让它成为视觉主体，清晰原文退化为隐约的内核，
    /// 呈现出"边缘发虚、笔画柔化"的失焦感。
    ///
    /// premultiplied alpha 下 RGB 与 A 必须同倍放大，否则颜色会失真。
    /// </summary>
    private const float BlurGain = 1.7f;

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
        _blurBrush = null;
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
            // 向四周各扩 BlurPadding，给高斯模糊的柔边留出生长空间（见该常量注释）。
            const float pad = BlurPadding;
            _surface.SourceOffset = new Vector2(-pad, -pad);
            _surface.SourceSize = new Vector2(w + pad * 2, h + pad * 2);
            _sprite.Size = new Vector2(w + pad * 2, h + pad * 2);
            _sprite.Offset = visual.Offset - new Vector3(pad, pad, 0);

            if (amount <= 0.01)
            {
                // 清晰：隐藏模糊层（保留对象，下一帧若再变糊可直接复用）
                _sprite.Opacity = 0;
                return;
            }
            _sprite.Opacity = 1;

            // 模糊画刷只创建一次：BlurAmount 声明为可动画参数，之后逐帧仅写入标量，
            // 因此"缓缓变清晰 / 缓缓变糊"的每一帧都极轻量（无 effect 图重编译）。
            if (_blurBrush is null)
            {
                var blur = new GaussianBlurEffect
                {
                    Name = "LyricBlur",
                    BlurAmount = (float)amount,
                    BorderMode = EffectBorderMode.Soft,
                    Source = new CompositionEffectSourceParameter("src"),
                };

                // 提亮模糊副本，使其压过下方的清晰原文（见 BlurGain 注释）。
                // premultiplied alpha：RGB 与 A 同倍放大，超出部分由合成器 clamp。
                var boosted = new ColorMatrixEffect
                {
                    Source = blur,
                    ColorMatrix = new Matrix5x4
                    {
                        M11 = BlurGain,
                        M22 = BlurGain,
                        M33 = BlurGain,
                        M44 = BlurGain,
                    },
                };

                var factory = visual.Compositor.CreateEffectFactory(boosted, new[] { "LyricBlur.BlurAmount" });
                _blurBrush = factory.CreateBrush();
                _blurBrush.SetSourceParameter("src", _surfaceBrush);
                _sprite.Brush = _blurBrush;
            }

            _blurBrush.Properties.InsertScalar("LyricBlur.BlurAmount", (float)amount);
        }
        catch
        {
            // 模糊仅为视觉增强，任何异常都不应影响歌词功能
        }
    }
}

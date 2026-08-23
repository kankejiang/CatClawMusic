using System.Numerics;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

using WSizeChangedEventArgs = Microsoft.UI.Xaml.SizeChangedEventArgs;
using Vector2 = System.Numerics.Vector2;
using WFrameworkElement = Microsoft.UI.Xaml.FrameworkElement;

namespace Vitrum.Windows.Handlers;

/// <summary>
/// 毛玻璃的 Windows 平台实现，支持两种捕获源：
/// <list type="bullet">
/// <item><b>无 <see cref="BlurConsumerView.ContentSource"/>（侧栏 / 顶部）</b>：用
/// <see cref="CompositionBackdropBrush"/> 捕获窗口背后的内容（配合窗口透明/DWM 扩展
/// = 桌面背景），模糊后填充圆角 <see cref="ShapeVisual"/>。用于「侧栏/顶部透出桌面背景」。</item>
/// <item><b>有 <see cref="BlurConsumerView.ContentSource"/>（播放条）</b>：用
/// <see cref="CompositionVisualSurface"/> 捕获被覆盖的内容视图（主内容区 MainArea）
/// 的底部横带，模糊后填充到播放条——实时呈现「下层控件」。</item>
/// </list>
/// 两种模式都会叠加一层 <see cref="TintColor"/> 半透明着色。Sprite 是该控件平台面板
/// 视觉树的子节点，跟随面板尺寸；XAML 中本控件必须是内容的背景层（sibling）。
/// </summary>
public class BlurConsumerViewHandler : ContentViewHandler
{
    /// <summary>高斯模糊半径（DIP）。越大雾感越强。</summary>
    private const float BlurRadius = 32f;

    public static new readonly IPropertyMapper<BlurConsumerView, BlurConsumerViewHandler> Mapper =
        new PropertyMapper<BlurConsumerView, BlurConsumerViewHandler>(ContentViewHandler.Mapper)
        {
            [nameof(BlurConsumerView.TintColor)] = MapTintColor,
            [nameof(BlurConsumerView.ClipCornerRadius)] = MapClipCornerRadius,
            [nameof(BlurConsumerView.BlurEnabled)] = MapBlurEnabled,
            [nameof(BlurConsumerView.LiquidGlass)] = MapBlurEnabled,
            [nameof(BlurConsumerView.ContentSource)] = MapContentSource,
        };

    public BlurConsumerViewHandler() : base(Mapper) { }

    /// <summary>基类把 VirtualView 面成 <see cref="IContentView"/>，这里还原为具体类型。</summary>
    private BlurConsumerView BlurView => (BlurConsumerView)VirtualView;

    private CompositionEffectBrush? _blurBrush;
    private CompositionColorBrush? _tintBrush;

    // 用 SpriteVisual 承载模糊/着色（CompositionSpriteShape.FillBrush 不接受含
    // Backdrop/Surface 源的效果画刷——会抛"Unsupported source brush type"，故不用 ShapeVisual）
    private SpriteVisual? _blurSprite;
    private SpriteVisual? _tintSprite;

    // ContentSource 捕获模式：捕获被覆盖的内容视图底带
    private CompositionVisualSurface? _surface;
    private CompositionSurfaceBrush? _surfaceBrush;
    private WFrameworkElement? _sourcePanel;
    private VisualElement? _sourceElement; // 关联的 MAUI 元素，用于反检查

    private float _radius;
    private bool _blurVisible = true;
    private bool _built;

    protected override ContentPanel CreatePlatformView() => new ContentPanel();

    public new ContentPanel PlatformView => (ContentPanel)base.PlatformView;

    protected override void ConnectHandler(ContentPanel platformView)
    {
        base.ConnectHandler(platformView);
        platformView.Loaded += OnLoaded;
        platformView.SizeChanged += OnSizeChanged;
        if (BlurView.ContentSource is { } src)
            AttachSource(src);
    }

    protected override void DisconnectHandler(ContentPanel platformView)
    {
        platformView.Loaded -= OnLoaded;
        platformView.SizeChanged -= OnSizeChanged;
        DetachSource();
        RemoveVisuals();
        base.DisconnectHandler(platformView);
    }

    private void OnLoaded(object sender, RoutedEventArgs e) => Build();

    private void OnSizeChanged(object sender, WSizeChangedEventArgs e)
        => Layout((float)e.NewSize.Width, (float)e.NewSize.Height);

    private void AttachSource(VisualElement source)
    {
        _sourceElement = source;
        if (source.Handler?.PlatformView is WFrameworkElement fe)
        {
            _sourcePanel = fe;
            _sourcePanel.SizeChanged += OnSourceSizeChanged;
        }
        // Handler 可能尚未连接：等 Loaded 后 ResolveSource 再补
    }

    private void DetachSource()
    {
        if (_sourcePanel != null)
            _sourcePanel.SizeChanged -= OnSourceSizeChanged;
        _sourcePanel = null;
        _sourceElement = null;
    }

    private void ResolveSource()
    {
        if (_sourcePanel != null) return;
        var src = _sourceElement ?? BlurView.ContentSource;
        if (src?.Handler?.PlatformView is WFrameworkElement fe)
        {
            _sourcePanel = fe;
            _sourcePanel.SizeChanged += OnSourceSizeChanged;
        }
    }

    private void OnSourceSizeChanged(object sender, WSizeChangedEventArgs e)
        => Layout((float)PlatformView.ActualWidth, (float)PlatformView.ActualHeight);

    private void Build()
    {
        var panel = PlatformView;
        var width = (float)panel.ActualWidth;
        var height = (float)panel.ActualHeight;
        if (width <= 0 || height <= 0) return;

        // ContentSource 捕获源（若指定）
        ResolveSource();

        var host = ElementCompositionPreview.GetElementVisual(panel);
        var compositor = host.Compositor;

        CompositionBrush srcBrush;
        if (_sourcePanel != null)
        {
            _surface = compositor.CreateVisualSurface();
            _surface.SourceVisual = ElementCompositionPreview.GetElementVisual(_sourcePanel);
            _surfaceBrush = compositor.CreateSurfaceBrush(_surface);
            _surfaceBrush.Stretch = CompositionStretch.Fill;
            _surfaceBrush.HorizontalAlignmentRatio = 0.5f;
            _surfaceBrush.VerticalAlignmentRatio = 0.5f;
            srcBrush = _surfaceBrush;
        }
        else
        {
            // 无来源：捕获窗口背后（配合窗口透明 = 桌面背景）
            srcBrush = compositor.CreateBackdropBrush();
        }

        var blur = new GaussianBlurEffect
        {
            Name = "VitrumBlur",
            BlurAmount = BlurRadius,
            BorderMode = EffectBorderMode.Soft,
            Source = new CompositionEffectSourceParameter("src"),
        };
        var factory = compositor.CreateEffectFactory(blur);
        _blurBrush = factory.CreateBrush();
        _blurBrush.SetSourceParameter("src", srcBrush);

        _tintBrush = compositor.CreateColorBrush(BlurView.TintColor.ToWindowsColor());

        // 模糊层：SpriteVisual.Brush 支持含 Backdrop/Surface 源的效果画刷。
        _blurSprite = compositor.CreateSpriteVisual();
        _blurSprite.Brush = _blurBrush;

        // 着色层：叠加在模糊层之上（均位于内容 sibling 之下）
        _tintSprite = compositor.CreateSpriteVisual();
        _tintSprite.Brush = _tintBrush;

        if (host is ContainerVisual container)
        {
            container.Children.InsertAtBottom(_blurSprite);
            container.Children.InsertAtTop(_tintSprite);
        }

        _built = true;
        _radius = BlurView.ClipCornerRadius;
        _blurVisible = BlurView.BlurEnabled;
        Layout(width, height);
    }

    private void RemoveVisuals()
    {
        if (!_built) return;
        if (_blurSprite != null && _blurSprite.Parent is ContainerVisual p1) p1.Children.Remove(_blurSprite);
        if (_tintSprite != null && _tintSprite.Parent is ContainerVisual p2) p2.Children.Remove(_tintSprite);
        _built = false;
        _surface = null;
        _surfaceBrush = null;
        _blurBrush = null;
        _tintBrush = null;
        _blurSprite = null;
        _tintSprite = null;
    }

    private void Layout(float width, float height)
    {
        if (!_built) return;
        if (width <= 0 || height <= 0) return;

        // 内容捕获模式：更新采集横带（底部与播放条等高的内容带）
        if (_surface != null && _surfaceBrush != null && _sourcePanel != null)
        {
            var srcW = (float)_sourcePanel.ActualWidth;
            var srcH = (float)_sourcePanel.ActualHeight;
            if (srcW > 0 && srcH > 0)
            {
                var band = Math.Min(height, srcH);
                _surface.SourceOffset = new Vector2(0f, srcH - band);
                _surface.SourceSize = new Vector2(srcW, band);
            }
        }

        var size = new Vector2(width, height);

        if (_blurSprite != null)
        {
            _blurSprite.Size = size;
            _blurSprite.IsVisible = _blurVisible;
        }
        if (_tintSprite != null)
        {
            _tintSprite.Size = size;
        }
    }

    private static void MapTintColor(BlurConsumerViewHandler handler, BlurConsumerView view)
        => handler._tintBrush?.UpdateColor(view.TintColor);

    private static void MapClipCornerRadius(BlurConsumerViewHandler handler, BlurConsumerView view)
    {
        handler._radius = view.ClipCornerRadius;
        handler.Layout((float)handler.PlatformView.ActualWidth, (float)handler.PlatformView.ActualHeight);
    }

    private static void MapBlurEnabled(BlurConsumerViewHandler handler, BlurConsumerView view)
    {
        handler._blurVisible = view.BlurEnabled;
        handler.Layout((float)handler.PlatformView.ActualWidth, (float)handler.PlatformView.ActualHeight);
    }

    private static void MapContentSource(BlurConsumerViewHandler handler, BlurConsumerView view)
    {
        // 捕获源变化需重建模糊层
        handler.DetachSource();
        if (view.ContentSource is { } src)
            handler.AttachSource(src);
        handler.RemoveVisuals();
        handler.Build();
    }
}
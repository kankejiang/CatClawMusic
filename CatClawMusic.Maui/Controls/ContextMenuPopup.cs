using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 上下文菜单下拉弹层：全窗透明遮罩 + 在指定坐标处弹出的圆角卡片。
/// 用于歌曲行长按（Android）/右键（Windows）的右键菜单式下拉菜单，与 AppPopup 的居中弹窗不同：
/// 菜单位置跟随手指/鼠标按下点，超出边界时自动收缩进可视区域。
/// </summary>
public class ContextMenuPopup : ContentView
{
    private readonly Grid _root;
    private readonly BoxView _mask;
    private readonly Border _card;
    private readonly VerticalStackLayout _cardContent;
    private bool _isOpen;
    private double _maxWidth = 400;
    private double _maxHeight = 600;
    private const double CardWidth = 248;
    private const double EdgeMargin = 8;

    /// <summary>关闭完成事件（宿主据此移除本弹层并释放）。</summary>
    public event EventHandler? Closed;

    public ContextMenuPopup()
    {
        _cardContent = new VerticalStackLayout { Spacing = 0 };

        _card = new Border
        {
            WidthRequest = CardWidth,
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Start,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            StrokeThickness = 1,
            Padding = new Thickness(6),
            AnchorX = 0,
            AnchorY = 0,
            TranslationX = -1000,
            TranslationY = -1000,
            Content = _cardContent
        };
        _card.SetDynamicResource(BackgroundColorProperty, "CardBackgroundStrongColor");
        _card.SetDynamicResource(Border.StrokeProperty, "GlassStrokeStrongColor");
#if !ANDROID
        // Windows 端 Shadow 渲染正常（提供下拉阴影）；Android 端 Border + Shadow 在 MAUI 已知
        // 会导致整张卡片（背景+内容）渲染失效（被 Shadow 视图遮挡/绘制管线冲突），
        // 仅显示透明遮罩——表现为"弹窗不可见但拦截触摸"。临时去除以恢复可见性。
        // 待 MAUI 官方修复后可统一恢复。
        _card.Shadow = new Shadow
        {
            Brush = Colors.Black,
            Radius = 24,
            Offset = new Point(0, 6),
            Opacity = 0.35f
        };
#endif

        _mask = new BoxView { Color = Colors.Transparent };
        _mask.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { _ = CloseAsync(); })
        });

        _root = new Grid { Children = { _mask, _card } };
        Content = _root;
        IsVisible = false;
        Opacity = 0;
    }

    /// <summary>清空菜单内容。</summary>
    public void ClearContent() => _cardContent.Children.Clear();

    /// <summary>添加一行菜单内容。</summary>
    public void AddContent(View view) => _cardContent.Children.Add(view);

    /// <summary>
    /// 在指定坐标（相对宿主左上角，DIP）处弹出菜单；坐标越界时自动收缩进 <paramref name="maxWidth"/>/<paramref name="maxHeight"/> 范围内。
    /// </summary>
    public void ShowAt(double x, double y, double maxWidth, double maxHeight)
    {
        if (_isOpen) return;
        _isOpen = true;

        if (maxWidth > 0) _maxWidth = maxWidth;
        if (maxHeight > 0) _maxHeight = maxHeight;

        ClampAndPosition(x, y);

        // 立即可见，不依赖动画：弹层是打开时新建的，首次布局前可能尚无平台 Handler，
        // FadeTo/ScaleTo 此时会抛异常（被吞掉后卡片停留在 Opacity=0 永久不可见）。
        _card.Opacity = 1;
        _card.Scale = 1;
        this.Opacity = 1;
        this.InputTransparent = false;
        this.IsVisible = true;
    }

    /// <summary>内容变化（如进入"添加到歌单"子视图）后重新测量并按原边界收缩定位。</summary>
    public void Relayout()
    {
        if (!_isOpen) return;
        ClampAndPosition(_card.TranslationX, _card.TranslationY);
    }

    private void ClampAndPosition(double x, double y)
    {
        double w = CardWidth;
        double h = 280; // 估算兜底：主菜单 5 项
        try
        {
            _card.Measure(double.PositiveInfinity, double.PositiveInfinity);
            if (_card.DesiredSize.Width > 0) w = Math.Min(CardWidth, _card.DesiredSize.Width);
            if (_card.DesiredSize.Height > 0) h = _card.DesiredSize.Height;
        }
        catch { }

        if (double.IsNaN(x) || double.IsInfinity(x)) x = EdgeMargin;
        if (double.IsNaN(y) || double.IsInfinity(y)) y = EdgeMargin;

        var maxX = Math.Max(EdgeMargin, _maxWidth - w - EdgeMargin);
        var maxY = Math.Max(EdgeMargin, _maxHeight - h - EdgeMargin);
        _card.TranslationX = Math.Clamp(x + 4, EdgeMargin, maxX);
        _card.TranslationY = Math.Clamp(y + 8, EdgeMargin, maxY);
    }

    /// <summary>关闭菜单（淡出后隐藏并触发 <see cref="Closed"/>）。</summary>
    public async Task CloseAsync()
    {
        if (!_isOpen) return;
        _isOpen = false;

        try
        {
            await Task.WhenAll(
                _card.FadeTo(0, 120, Easing.CubicIn),
                _card.ScaleTo(0.92, 120, Easing.CubicIn));
        }
        catch { }

        this.IsVisible = false;
        this.InputTransparent = true;
        Closed?.Invoke(this, EventArgs.Empty);
    }
}

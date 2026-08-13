using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 上下文菜单下拉弹层：全窗透明遮罩 + 在指定坐标处弹出的圆角卡片（带轻微缩放淡入）。
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
        _card.Shadow = new Shadow
        {
            Brush = Colors.Black,
            Radius = 24,
            Offset = new Point(0, 6),
            Opacity = 0.35f
        };

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

        this.InputTransparent = false;
        this.IsVisible = true;
        this.Opacity = 1;

        _mask.Opacity = 0;
        _card.Opacity = 0;
        _card.Scale = 0.92;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await Task.WhenAll(
                    _card.FadeTo(1, 140, Easing.CubicOut),
                    _card.ScaleTo(1, 140, Easing.CubicOut));
            }
            catch { }
        });
    }

    /// <summary>内容变化（如进入"添加到歌单"子视图）后重新测量并按原边界收缩定位。</summary>
    public void Relayout()
    {
        if (!_isOpen) return;
        ClampAndPosition(_card.TranslationX, _card.TranslationY);
    }

    private void ClampAndPosition(double x, double y)
    {
        try
        {
            _card.Measure(double.PositiveInfinity, double.PositiveInfinity);
            var w = Math.Min(CardWidth, _card.DesiredSize.Width);
            var h = _card.DesiredSize.Height;
            var maxX = Math.Max(EdgeMargin, _maxWidth - w - EdgeMargin);
            var maxY = Math.Max(EdgeMargin, _maxHeight - h - EdgeMargin);
            _card.TranslationX = Math.Clamp(x + 4, EdgeMargin, maxX);
            _card.TranslationY = Math.Clamp(y + 8, EdgeMargin, maxY);
        }
        catch
        {
            _card.TranslationX = x;
            _card.TranslationY = y;
        }
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

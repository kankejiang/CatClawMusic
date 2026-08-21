using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 歌曲上下文菜单弹层：
/// - Android（长按）：网易云风格「底部抽屉」——半透明遮罩 + 背景高斯模糊，卡片从屏幕底部滑入/滑出，
///   始终贴底弹出（不跟随手指位置）；内容超高时内部滚动，不会顶出屏幕。
/// - Windows（右键）：保留桌面下拉卡片，跟随鼠标按下点，超出边界自动收缩进可视区域。
/// 供歌曲行长按/右键菜单（SongContextMenu）与各页面 ISongContextMenuHost 复用。
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
    private double _pendingX;
    private double _pendingY;
    private const double CardWidth = 248;
    private const double EdgeMargin = 8;

    /// <summary>Android 使用底部抽屉形态；桌面平台保留下拉卡片。</summary>
    private static bool IsDrawer =>
#if ANDROID
        true;
#else
        false;
#endif

    /// <summary>关闭完成事件（宿主据此移除本弹层并释放）。</summary>
    public event EventHandler? Closed;

    public ContextMenuPopup()
    {
        _cardContent = new VerticalStackLayout { Spacing = 0 };

        _card = new Border
        {
            StrokeShape = new RoundRectangle
            {
                CornerRadius = IsDrawer ? new CornerRadius(22, 22, 0, 0) : new CornerRadius(14)
            },
            StrokeThickness = 1,
            Padding = new Thickness(6),
            AnchorX = 0,
            AnchorY = 0,
            Content = IsDrawer ? BuildDrawerContent() : _cardContent
        };
        _card.SetDynamicResource(BackgroundColorProperty, "CardBackgroundStrongColor");
        _card.SetDynamicResource(Border.StrokeProperty, "GlassStrokeStrongColor");
#if !ANDROID
        // Windows 端 Shadow 渲染正常（提供下拉阴影）；Android 端 Border + Shadow 在 MAUI 已知
        // 会导致整张卡片（背景+内容）渲染失效（被 Shadow 视图遮挡/绘制管线冲突），
        // 仅显示透明遮罩——表现为"弹窗不可见但拦截触摸"。临时去除以恢复可见性。
        _card.Shadow = new Shadow
        {
            Brush = Colors.Black,
            Radius = 24,
            Offset = new Point(0, 6),
            Opacity = 0.35f
        };
#endif

        if (IsDrawer)
        {
            // 底部抽屉：贴底、近全宽、顶部圆角；初始置于屏外（滑入动画起点）
            _card.HorizontalOptions = LayoutOptions.Fill;
            _card.VerticalOptions = LayoutOptions.End;
            _card.Margin = new Thickness(8, 0, 8, 8);
            _card.TranslationY = 1200;
            _mask = new BoxView { Color = Color.FromArgb("#66000000") };
        }
        else
        {
            // 桌面下拉卡片：固定宽度、左上对齐，坐标由 ClampAndPosition 定位
            _card.WidthRequest = CardWidth;
            _card.HorizontalOptions = LayoutOptions.Start;
            _card.VerticalOptions = LayoutOptions.Start;
            _card.TranslationX = -1000;
            _card.TranslationY = -1000;
            _mask = new BoxView { Color = Colors.Transparent };
        }

        _mask.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() => { _ = CloseAsync(); })
        });

        _root = new Grid { Children = { _mask, _card } };
        Content = _root;
        IsVisible = false;
        Opacity = 0;
    }

    /// <summary>抽屉模式：顶部 drag handle（拖动手势下滑关闭）+ 内容区 ScrollView（超高内部滚动）。</summary>
    private View BuildDrawerContent()
    {
        // 顶部 40 DIP 高的抓握区；居中放一条 36×4 圆角半透明小条作为可视提示。
        var handleBar = new Border
        {
            WidthRequest = 36,
            HeightRequest = 4,
            BackgroundColor = Color.FromArgb("#50000000"),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(2) },
            StrokeThickness = 0,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Center
        };
        var handleArea = new Grid
        {
            HeightRequest = 40,
            Children = { handleBar }
        };

        // PanGestureRecognizer 仅挂在拖拽把手上，不与 ScrollView 竞争触摸事件
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += OnHandlePan;
        handleArea.GestureRecognizers.Add(pan);

        var contentScroll = new ScrollView
        {
            VerticalOptions = LayoutOptions.Fill,
            Content = _cardContent
        };

        var root = new Grid
        {
            RowDefinitions = new RowDefinitionCollection
            {
                new() { Height = GridLength.Auto },  // drag handle
                new() { Height = GridLength.Star }   // 内容区
            }
        };
        root.Add(handleArea, 0, 0);
        root.Add(contentScroll, 0, 1);

        return root;
    }

    /// <summary>抽屉拖拽处理：PanGestureRecognizer 仅挂在拖拽把手上（40dp 高），不与 ScrollView 竞争触摸事件，
    /// 拖动时直接更新卡片 TranslationY，松开后基于阈值关闭或回弹。</summary>
    private async void OnHandlePan(object? sender, PanUpdatedEventArgs e)
    {
        if (!IsDrawer || !_isOpen) return;

        switch (e.StatusType)
        {
            case GestureStatus.Running:
                var ty = Math.Max(0, e.TotalY);
                _card.TranslationY = ty;
                var ratio = _card.Height > 0 ? Math.Min(1, ty / (_card.Height * 0.3)) : 0;
                _mask.Opacity = 1 - ratio * 0.5;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                if (_card.Height > 0 && _card.TranslationY > _card.Height * 0.25)
                {
                    await CloseAsync();
                }
                else
                {
                    try
                    {
                        await Task.WhenAll(
                            _card.TranslateTo(0, 0, 220, Easing.CubicOut),
                            _mask.FadeTo(1, 200));
                    }
                    catch { }
                }
                break;
        }
    }

    /// <summary>清空菜单内容。</summary>
    public void ClearContent() => _cardContent.Children.Clear();

    /// <summary>添加一行菜单内容。</summary>
    public void AddContent(View view) => _cardContent.Children.Add(view);

    /// <summary>
    /// 弹出菜单。Android 抽屉模式下忽略锚点坐标（贴底滑入），<paramref name="maxWidth"/>/<paramref name="maxHeight"/>
    /// 用于限制最大高度与桌面下拉模式的收缩边界。
    /// </summary>
    public void ShowAt(double x, double y, double maxWidth, double maxHeight)
    {
        if (_isOpen) return;
        _isOpen = true;

        if (maxWidth > 0) _maxWidth = maxWidth;
        if (maxHeight > 0) _maxHeight = maxHeight;

        if (IsDrawer)
        {
            // 抽屉最大高度=屏高一半；内容自然高度低于该值时按内容显示，超出时 ScrollView 内部滚动
            _card.MaximumHeightRequest = Math.Max(160, _maxHeight * 0.5);
            _card.TranslationX = 0;
            _card.TranslationY = _maxHeight; // 屏外起点，等首次布局后按实际高度滑入
            _card.Opacity = 1;
            _card.Scale = 1;
            _mask.Opacity = 0;
            this.Opacity = 1;
            this.InputTransparent = false;
            this.IsVisible = true;
            ScheduleDrawerSlideIn();
        }
        else
        {
            _pendingX = x;
            _pendingY = y;
            ClampAndPosition(x, y);

            // 立即可见，不依赖动画：弹层是打开时新建的，首次布局前可能尚无平台 Handler，
            // FadeTo/ScaleTo 此时会抛异常（被吞掉后卡片停留在 Opacity=0 永久不可见）。
            _card.Opacity = 1;
            _card.Scale = 1;
            this.Opacity = 1;
            this.InputTransparent = false;
            this.IsVisible = true;

            // 卡片首次渲染完成前 Measure 会低估高度（未挂载布局树，且不含系统字体缩放），
            // 若歌曲行位于屏幕底部，菜单底部会超出屏幕；等真实布局（SizeChanged）后按实际尺寸重新约束。
            ScheduleReclamp(x, y);
        }

#if ANDROID
        // 与播放页弹窗一致的背景高斯模糊（遮罩下透出模糊内容）
        ApplyBlurToSiblings();
#endif
    }

    /// <summary>内容变化（如进入"添加到歌单"子视图）后重新适配：抽屉贴底由内部滚动消化，下拉模式按原始锚点重定位。</summary>
    public void Relayout()
    {
        if (!_isOpen) return;
        if (IsDrawer) return;
        ClampAndPosition(_pendingX, _pendingY);
    }

    /// <summary>等待抽屉首次真实布局后播放滑入动画（此时卡片高度已知，滑入距离=卡片高度）。</summary>
    private void ScheduleDrawerSlideIn()
    {
        _card.SizeChanged += OnDrawerCardSized;
    }

    private async void OnDrawerCardSized(object? sender, EventArgs e)
    {
        _card.SizeChanged -= OnDrawerCardSized;
        if (!_isOpen) return;
        try
        {
            _card.TranslationY = _card.Height + 16;
            await Task.WhenAll(
                _card.TranslateTo(0, 0, 260, Easing.CubicOut),
                _mask.FadeTo(1, 200));
        }
        catch { }
    }

    /// <summary>记录锚点并等待卡片首次真实布局后重新约束位置（修正 Measure 低估导致的越界）。</summary>
    private void ScheduleReclamp(double x, double y)
    {
        _pendingX = x;
        _pendingY = y;

        // 弹层每次新建，首次 ShowAt 时卡片必然未布局；若已有尺寸（极端复用场景）则直接修正。
        if (_card.Width > 0 && _card.Height > 0)
        {
            ClampAndPosition(x, y);
            return;
        }
        _card.SizeChanged += OnCardSizedForReclamp;
    }

    private void OnCardSizedForReclamp(object? sender, EventArgs e)
    {
        _card.SizeChanged -= OnCardSizedForReclamp;
        if (!_isOpen) return;
        ClampAndPosition(_pendingX, _pendingY);
    }

    /// <summary>桌面下拉模式：按卡片实际尺寸把位置夹紧进可视区域（抽屉模式不使用）。</summary>
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

        // 已真实布局后优先用实际尺寸（比 Measure 更可靠，涵盖系统字体缩放等）
        if (_card.Width > 0) w = Math.Min(CardWidth, _card.Width);
        if (_card.Height > 0) h = _card.Height;

        if (double.IsNaN(x) || double.IsInfinity(x)) x = EdgeMargin;
        if (double.IsNaN(y) || double.IsInfinity(y)) y = EdgeMargin;

        var maxX = Math.Max(EdgeMargin, _maxWidth - w - EdgeMargin);
        var maxY = Math.Max(EdgeMargin, _maxHeight - h - EdgeMargin);
        _card.TranslationX = Math.Clamp(x + 4, EdgeMargin, maxX);
        _card.TranslationY = Math.Clamp(y + 8, EdgeMargin, maxY);
    }

    /// <summary>关闭菜单：抽屉滑出/下拉淡出后隐藏并触发 <see cref="Closed"/>。</summary>
    public async Task CloseAsync()
    {
        if (!_isOpen) return;
        _isOpen = false;
        _card.SizeChanged -= OnCardSizedForReclamp;
        _card.SizeChanged -= OnDrawerCardSized;

        if (IsDrawer)
        {
            try
            {
                await Task.WhenAll(
                    _card.TranslateTo(0, _card.Height + 16, 220, Easing.CubicIn),
                    _mask.FadeTo(0, 180));
            }
            catch { }
        }
        else
        {
            try
            {
                await Task.WhenAll(
                    _card.FadeTo(0, 120, Easing.CubicIn),
                    _card.ScaleTo(0.92, 120, Easing.CubicIn));
            }
            catch { }
        }

#if ANDROID
        RemoveBlurFromSiblings();
#endif

        this.IsVisible = false;
        this.InputTransparent = true;
        Closed?.Invoke(this, EventArgs.Empty);
    }

#if ANDROID
    private readonly List<global::Android.Views.View> _blurredViews = new();

    /// <summary>对弹层背后的兄弟视图应用高斯模糊 RenderEffect（与播放页弹窗一致，minSdk=31 无需 API 防护）。</summary>
    private void ApplyBlurToSiblings()
    {
        _blurredViews.Clear();

        if (this.Parent is Microsoft.Maui.Controls.Layout layout)
        {
            foreach (var child in layout.Children)
            {
                if (child == this) continue;
                if (child is Microsoft.Maui.Controls.View view &&
                    view.Handler?.PlatformView is global::Android.Views.View nativeView)
                {
                    nativeView.SetRenderEffect(
                        global::Android.Graphics.RenderEffect.CreateBlurEffect(
                            24, 24, global::Android.Graphics.Shader.TileMode.Clamp));
                    _blurredViews.Add(nativeView);
                }
            }
        }
    }

    /// <summary>移除兄弟视图上的模糊效果。</summary>
    private void RemoveBlurFromSiblings()
    {
        foreach (var view in _blurredViews)
        {
            try { view.SetRenderEffect(null); } catch { }
        }
        _blurredViews.Clear();
    }
#endif
}

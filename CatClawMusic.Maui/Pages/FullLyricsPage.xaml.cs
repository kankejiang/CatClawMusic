using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>全屏歌词页面，支持横竖屏两种布局。竖屏：顶部HeroCard + 全宽歌词；横屏：左封面 + 右歌词。</summary>
public partial class FullLyricsPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private readonly List<KaraokeLabel> _lyricLabels = new();
    private readonly List<KaraokeLabel> _lyricTransLabels = new();      // 与 _lyricLabels 索引对齐，无译文行为 null
    private readonly List<Border> _lyricBorders = new();
    private readonly List<KaraokeLabel> _landscapeLyricLabels = new();
    private readonly List<KaraokeLabel> _landscapeLyricTransLabels = new(); // 与 _landscapeLyricLabels 索引对齐
    private readonly List<Border> _landscapeLyricBorders = new();
    private bool _userScrolling = false;
    private int _lastHighlightIndex = -1;
    private int _landscapeLastHighlight = -1;
    private bool _isLandscape;
    private int _lastCoverSize;
    private readonly LyricsSettingsService _settings = LyricsSettingsService.Instance;

    // 滚动歌词（复刻 Windows）：自绘静态堆叠 + 整体平移 TranslationY。
    // 行视图/锚点/裁剪区高度按横竖屏各一套，Active* 抽象统一访问。
    private readonly List<View> _lyricRowViews = new();
    private readonly List<View> _landscapeLyricRowViews = new();
    private double[] _lyricRowTops = Array.Empty<double>();
    private double[] _landscapeLyricRowTops = Array.Empty<double>();
    private double[] _lastMeasuredTops = Array.Empty<double>();
    private double[] _lastLandscapeMeasuredTops = Array.Empty<double>();
    private double _lyricClipHeight;
    private double _landscapeLyricClipHeight;
    private int _lyricMeasureRetries;
    private int _landscapeLyricMeasureRetries;
    private double _panStartY;
    private bool _panWired;

    // 歌词行视觉层次（与播放页一致）：所有行统一字号（锚点依赖行高恒定），当前行用 Scale 放大 + 行距呼吸。
    private const double LyricCurrentScale = 1.25;
    private const double LyricGapExtra = 6;
    private const uint LyricAnimMs = 380;

    /// <summary>当前活跃的行视图列表（横屏/竖屏）</summary>
    private List<View> ActiveLyricRowViews => _isLandscape ? _landscapeLyricRowViews : _lyricRowViews;

    /// <summary>当前活跃的行锚点表</summary>
    private double[] ActiveLyricRowTops => _isLandscape ? _landscapeLyricRowTops : _lyricRowTops;

    /// <summary>当前活跃的裁剪容器</summary>
    private Grid ActiveLyricClip => _isLandscape ? LandscapeLyricClip : LyricClip;

    /// <summary>当前活跃的裁剪区高度</summary>
    private double ActiveLyricClipHeight => _isLandscape ? _landscapeLyricClipHeight : _lyricClipHeight;

    /// <summary>当前活跃的测量重试计数（引用返回）</summary>
    private ref int ActiveMeasureRetries => ref _isLandscape ? ref _landscapeLyricMeasureRetries : ref _lyricMeasureRetries;

    /// <summary>当前活跃的 LyricStack（横屏/竖屏）</summary>
    private VerticalStackLayout ActiveLyricStack => _isLandscape ? LandscapeLyricStack : LyricStack;

    /// <summary>当前活跃的歌词标签列表</summary>
    private List<KaraokeLabel> ActiveLyricLabels => _isLandscape ? _landscapeLyricLabels : _lyricLabels;

    /// <summary>当前活跃的译文标签列表（与主歌词索引对齐，无译文行为 null）</summary>
    private List<KaraokeLabel> ActiveLyricTransLabels => _isLandscape ? _landscapeLyricTransLabels : _lyricTransLabels;

    /// <summary>当前活跃的最后高亮索引</summary>
    private ref int ActiveLastHighlight => ref _isLandscape ? ref _landscapeLastHighlight : ref _lastHighlightIndex;

    /// <summary>竖屏裁剪容器 Handler 变化时触发</summary>
    private void OnCollectionViewHandlerChanged(object? sender, EventArgs e)
    {
        if (_isLandscape) return;
        TriggerScrollIfReady(LyricClip, _lyricLabels);
    }

    /// <summary>横屏裁剪容器 Handler 变化时触发</summary>
    private void OnLandscapeCollectionViewHandlerChanged(object? sender, EventArgs e)
    {
        if (!_isLandscape) return;
        TriggerScrollIfReady(LandscapeLyricClip, _landscapeLyricLabels);
    }

    private void TriggerScrollIfReady(Grid clip, List<KaraokeLabel> labels)
    {
        if (clip.Handler != null && labels.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
#if ANDROID
            Android.Util.Log.Info("FLP", "[FullLyricsPage] LyricClip Handler 就绪，延迟滚动到 idx={0}", _viewModel.CurrentLyricIndexObservable);
#endif
            _ = Task.Delay(300).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (clip.Handler != null)
                    {
                        HighlightLineWithoutScroll(_viewModel.CurrentLyricIndexObservable);
                        MeasureLyricRows();
                        ScrollToLine(_viewModel.CurrentLyricIndexObservable);
                    }
                }));
        }
    }

    /// <summary>初始化 <see cref="FullLyricsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    public FullLyricsPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

#if ANDROID
        Android.Util.Log.Info("FLP", "[FullLyricsPage] 构造 #{0}", GetHashCode());
#endif

        // 控件级事件：在构造函数中订阅一次，永不取消
        LyricClip.HandlerChanged += OnCollectionViewHandlerChanged;
        LandscapeLyricClip.HandlerChanged += OnLandscapeCollectionViewHandlerChanged;

        // 歌词拖动浏览（暂停跟随 3 秒）
        WireLyricPanGesture();

        // 封面图片打标签确保高分辨率解码
        LandscapeCoverImage.HandlerChanged += (_, _) => TagPlayerCoverViews();

        // 监听尺寸变化以切换横竖屏布局
        SizeChanged += OnPageSizeChanged;

        // 锁屏解锁/回前台：立即重测 + 钉当前行（安卓 Handler 重建会导致旧锚点表过时）
        App.Resumed += OnAppResumed;

        // 静态/单例事件：通过 HandlerChanged 管理订阅生命周期
        HandlerChanged += (_, _) =>
        {
            if (Handler == null)
            {
#if ANDROID
                Android.Util.Log.Info("FLP", "[FullLyricsPage] Handler=null(取消订阅) #{0}", GetHashCode());
#endif
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
            }
            else
            {
                _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
                _viewModel.PropertyChanged += OnViewModelPropertyChanged;
                SafeAreaHelper.SafeAreaChanged -= OnSafeAreaChanged;
                SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
            }
        };
    }

    private void OnAppResumed(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (Handler == null) return;
            ForcePinActiveLine();
            ScheduleRetriedPinActive(3);
        });
    }

    /// <summary>
    /// 自检当前锚点表是否与真实布局一致（适配横竖屏双布局）：
    /// - 锚点表数量与行数一致
    /// - 所有行高度 > 0.5
    /// - 逐行累加结果与 ActiveLyricRowTops 误差在 1dp 内
    /// 锁屏解锁 / 从后台回前台时，Handler/PlatformView 可能重建导致行高变化，
    /// 旧锚点表依然被 ScrollToLine 引用 → 越滚越偏。调用前校验保证定位基准可靠。
    /// </summary>
    private bool ValidateActiveTops()
    {
        var rows = ActiveLyricRowViews;
        var tops = ActiveLyricRowTops;
        if (tops.Length != rows.Count) return false;
        var spacing = ActiveLyricStack.Spacing;
        double y = ActiveLyricStack.Padding.Top;
        for (int i = 0; i < rows.Count; i++)
        {
            var h = rows[i].Height;
            if (h <= 0.5) return false;
            if (Math.Abs(tops[i] - y) > 1.0) return false;
            y += h + spacing;
        }
        return true;
    }

    /// <summary>锁屏解锁/回前台的"强制钉线"操作：重启测量 + 取消动画后直接跳目标位（横竖屏通用）。</summary>
    private void ForcePinActiveLine()
    {
        var rows = ActiveLyricRowViews;
        if (rows.Count == 0) return;
        var idx = Math.Max(0, _viewModel.CurrentLyricIndexObservable);
        if (idx >= rows.Count) return;

        // 清空对应方向的锚点快照，强制 MeasureLyricRows 重新测量
        if (_isLandscape) _lastLandscapeMeasuredTops = Array.Empty<double>();
        else _lastMeasuredTops = Array.Empty<double>();

        // 重启一轮测量
        ref int retries = ref ActiveMeasureRetries;
        retries = 0;
        MeasureLyricRows();

        if (ActiveLyricRowTops.Length != rows.Count)
        {
            // 测量尚未就绪，稍后再试 4 次（最多 1200ms）
            ScheduleRetriedPinActive(4);
            return;
        }

        PinActiveLineNow(idx);
    }

    /// <summary>取消动画，立即把目标行钉在 1/3 处（不走缓动，避免前台视觉上还在滑）。横竖屏通用。</summary>
    private void PinActiveLineNow(int index)
    {
        var rows = ActiveLyricRowViews;
        if (index < 0 || index >= rows.Count) return;
        if (ActiveLyricRowTops.Length != rows.Count) return;
        try
        {
            var clipH = ActiveLyricClip.Bounds.Height;
            if (clipH <= 0) clipH = ActiveLyricClipHeight;
            if (clipH <= 0) return;

            // 把最新裁剪高度回写到缓存（后续 ScrollToLine 也会用）
            if (_isLandscape) _landscapeLyricClipHeight = clipH;
            else _lyricClipHeight = clipH;

            double targetY = ComputePinnedTargetY(index, clipH);
            ActiveLyricStack.CancelAnimations();
            // 直接赋值 TranslationY，避免 380ms 缓动的视觉滑动
            ActiveLyricStack.TranslationY = targetY;
        }
        catch { }
    }

    private void ScheduleRetriedPinActive(int remaining)
    {
        if (remaining <= 0) return;
        _ = Task.Delay(300).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            var rows = ActiveLyricRowViews;
            if (rows.Count == 0) return;
            // 先自检一次，失效就重测
            if (!ValidateActiveTops())
            {
                ref int retries = ref ActiveMeasureRetries;
                retries = 0;
                if (_isLandscape) _lastLandscapeMeasuredTops = Array.Empty<double>();
                else _lastMeasuredTops = Array.Empty<double>();
                MeasureLyricRows();
            }
            var idx = Math.Max(0, _viewModel.CurrentLyricIndexObservable);
            if (ActiveLyricRowTops.Length == rows.Count && idx < rows.Count)
                PinActiveLineNow(idx);
            else
                ScheduleRetriedPinActive(remaining - 1);
        }));
    }

    private void OnPageSizeChanged(object? sender, EventArgs e)
    {
        var w = Width;
        var h = Height;
        if (w > 0 && h > 0)
            ApplyLayoutForOrientation(w, h);
    }

    /// <summary>根据屏幕方向切换横竖屏布局</summary>
    private void ApplyLayoutForOrientation(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        var isLandscape = width > height;
        var topInset = SafeAreaHelper.TopInset;
        var bottomInset = SafeAreaHelper.BottomInset;

        // 封面尺寸计算（与 NowPlayingPage 完全一致）
        int coverSize;
        if (isLandscape)
        {
            // 可用高度 = height - 顶部预留 - 底部预留
            // 顶部预留 = topInset + 5，底部预留 = max(topInset, bottomInset) + 5
            double effectiveBottomInset = Math.Max(topInset, bottomInset);
            double availableHeight = height - topInset - effectiveBottomInset - 10;
            // 左栏宽度：LandscapeContent 内容宽度 = width - 48*2(LandscapeContent左右) - 32(列间距)
            // 左栏占比 1 / (1+1.15) = 1/2.15
            double landscapeContentWidth = width - 96 - 32;
            double leftColumnWidth = landscapeContentWidth / 2.15;
            // 封面左边距(相对于左栏Grid) = 目标左边距 - LandscapeContent左padding
            // 目标左边距 = topInset + 5（相对于屏幕左边）
            double leftMarginInGrid = (topInset + 5) - 48;
            // 左栏可用宽度 = 左栏宽度 - 左边距（左边距为负时增加可用宽度）
            double availableWidth = leftColumnWidth - leftMarginInGrid;
            // 封面尺寸 = min(可用高度, 可用宽度)，保留 240-800 的合理范围
            coverSize = Math.Clamp((int)Math.Min(availableHeight, availableWidth), 240, 800);
        }
        else
        {
            coverSize = Math.Clamp((int)(width - 60), 280, 560);
        }

        if (_isLandscape == isLandscape && _lastCoverSize == coverSize)
            return;

        var orientationChanged = _isLandscape != isLandscape;
        _isLandscape = isLandscape;

        if (isLandscape)
        {
            // 横屏：左封面 + 右歌词，删除顶部卡片
            if (orientationChanged)
            {
                PortraitContent.IsVisible = false;
                LandscapeContent.IsVisible = true;
            }

            // 横屏 SafeArea：内容间距由 LandscapeContent 的 Padding 处理，
            // RootGrid 不设 Padding，确保雾面背景和暗色遮罩铺满全屏（沉浸式）
            RootGrid.Padding = new Thickness(0);
            LandscapeContent.Padding = new Thickness(48, topInset + 5, 48, Math.Max(topInset, bottomInset) + 5);

            _lastCoverSize = coverSize;

            // 封面尺寸同步（辉光 + 本体）
            LandscapeCoverGlow.WidthRequest = coverSize;
            LandscapeCoverGlow.HeightRequest = coverSize;
            LandscapeCover.WidthRequest = coverSize;
            LandscapeCover.HeightRequest = coverSize;
            LandscapeCoverImage.WidthRequest = coverSize;
            LandscapeCoverImage.HeightRequest = coverSize;

            // 封面位置：顶部贴齐（VerticalOptions=Start，顶部=0即对齐状态栏+5），
            // 左边距 = topInset + 5 - LandscapeContent左padding
            double leftMargin = topInset + 5 - 48;
            LandscapeCover.Margin = new Thickness(leftMargin, 0, 0, 0);
            LandscapeCoverGlow.Margin = new Thickness(leftMargin, 0, 0, 0);

            // 重载封面确保高分辨率
            _ = ReloadCoverHighResAsync(LandscapeCoverImage);

            // 如果横屏歌词还没构建，立即构建
            if (_landscapeLyricLabels.Count == 0 && _viewModel.AllLyricLines != null && _viewModel.AllLyricLines.Count > 0)
            {
                BuildLandscapeLyricViews();
            }

            if (orientationChanged)
            {
                // 方向切换：先同步当前行高亮样式，再强制重测锚点并钉回 1/3 处（无缓动），
                // 追加 3 次重试兜底 Handler/二次布局晚于本次回调的情况。
                var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                if (_landscapeLyricLabels.Count > 0)
                    HighlightLineWithoutScroll(idx);
                ForcePinActiveLine();
                ScheduleRetriedPinActive(3);
            }
        }
        else
        {
            // 竖屏：恢复原始布局
            LandscapeContent.IsVisible = false;
            PortraitContent.IsVisible = true;
            ApplySafeArea();

            if (orientationChanged)
            {
                // 方向切换：同步当前行高亮样式 + 强制钉行（同横屏逻辑）
                var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                if (_lyricLabels.Count > 0)
                    HighlightLineWithoutScroll(idx);
                ForcePinActiveLine();
                ScheduleRetriedPinActive(3);
            }
        }
    }

    /// <summary>给封面图片打标签确保高分辨率解码</summary>
    private void TagPlayerCoverViews()
    {
#if ANDROID
        try
        {
            if (LandscapeCoverImage.Handler?.PlatformView is Android.Widget.ImageView iv)
                iv.Tag = CatClawMusic.Maui.Platforms.Android.CachingFileImageSourceService.PlayerCoverTag;
        }
        catch { }
#endif
    }

    /// <summary>强制封面使用高分辨率桶重新加载（与 NowPlayingPage 实现一致）</summary>
    private async Task ReloadCoverHighResAsync(Image image)
    {
#if ANDROID
        try
        {
            await Task.Delay(50);
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                try
                {
                    if (image.Handler?.PlatformView is Android.Widget.ImageView iv)
                        iv.Tag = CatClawMusic.Maui.Platforms.Android.CachingFileImageSourceService.PlayerCoverTag;

                    // 强制重新解码：先清空 Source（会破坏 XAML 的 OneWay 绑定），
                    // 再用 SetBinding 重新建立绑定。绑定立即求值，将 Source 设回
                    // ViewModel.CoverImage 的当前值，触发带 Tag 的高分辨率重新解码。
                    image.Source = null;
                    image.SetBinding(Image.SourceProperty, new Binding(nameof(NowPlayingViewModel.CoverImage)));
                }
                catch { }
            });
        }
        catch { }
#endif
    }

    /// <summary>系统栏高度变化时触发</summary>
    private void OnSafeAreaChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            ApplySafeArea();
            if (_isLandscape && Width > 0 && Height > 0)
                ApplyLayoutForOrientation(Width, Height);
        });
    }

    /// <summary>应用 SafeArea</summary>
    private void ApplySafeArea()
    {
        var top = SafeAreaHelper.TopInset;
#if ANDROID || WINDOWS
        var bottom = SafeAreaHelper.BottomInset;
#else
        var bottom = 0;
#endif
        if (!_isLandscape)
        {
            PortraitContent.Padding = new Thickness(0, top, 0, bottom);
        }
    }

    /// <summary>当视图模型属性变更时触发</summary>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        try
        {
#if ANDROID
            if (Handler?.PlatformView is Android.Views.View av && !av.IsAttachedToWindow)
            {
                Android.Util.Log.Info("FLP", "[FullLyricsPage] PropertyChanged({0}) 跳过：PlatformView未附加", e.PropertyName);
                return;
            }
#endif
            if (e.PropertyName == nameof(NowPlayingViewModel.AllLyricLines) ||
                e.PropertyName == nameof(NowPlayingViewModel.HasLyrics))
            {
#if ANDROID
                Android.Util.Log.Info("FLP", "[FullLyricsPage] PropertyChanged: 重建歌词视图, AllLyricLines.Count={0}", _viewModel.AllLyricLines?.Count ?? -1);
#endif
                MainThread.BeginInvokeOnMainThread(BuildLyricViews);
                return;
            }

            if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLyricIndexObservable))
            {
#if ANDROID
                Android.Util.Log.Info("FLP", "[FullLyricsPage] PropertyChanged: CurrentLyricIndex={0}, Labels={1}, HandlerAttached={2}",
                    _viewModel.CurrentLyricIndexObservable, ActiveLyricLabels.Count, Handler?.PlatformView != null);
#endif
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    HighlightLine(_viewModel.CurrentLyricIndexObservable);
                });
                return;
            }

            if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLineFillProgress))
            {
                var idx = _viewModel.CurrentLyricIndexObservable;
                if (idx >= 0 && idx < ActiveLyricLabels.Count)
                    ActiveLyricLabels[idx].FillProgress = _viewModel.CurrentLineFillProgress;
            }
        }
        catch { }
    }

    /// <summary>页面显示时触发</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();
#if WINDOWS
        Shell.SetNavBarIsVisible(this, false);
#endif
        ApplySafeArea();
        Application.Current!.RequestedThemeChanged += OnThemeChanged;

        if (_viewModel.AllLyricLines != null && _viewModel.AllLyricLines.Count > 0)
        {
            var activeLabels = ActiveLyricLabels;
            if (activeLabels.Count != _viewModel.AllLyricLines.Count)
                BuildLyricViews();
            else
            {
                var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                HighlightLineWithoutScroll(idx);
            }

            // 进入全屏歌词页：立即钉一次当前行（可能是后台期间切歌 / 横竖屏切换了，位置不准），
            // 随后每隔 300ms 再补钉 3 次，覆盖 Handler/PlatformView 在 OnAppearing 之后才就绪的场景。
            ForcePinActiveLine();
            ScheduleRetriedPinActive(3);
        }
    }

    /// <summary>页面消失时触发</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Application.Current!.RequestedThemeChanged -= OnThemeChanged;
    }

    /// <summary>主题变更时重建歌词视图</summary>
    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(BuildLyricViews);
    }

    /// <summary>构建所有布局的歌词视图（竖屏+横屏）</summary>
    private void BuildLyricViews()
    {
        BuildPortraitLyricViews();
        BuildLandscapeLyricViews();
    }

    /// <summary>构建竖屏歌词视图</summary>
    private void BuildPortraitLyricViews()
    {
        LyricStack.Children.Clear();
        _lyricLabels.Clear();
        _lyricTransLabels.Clear();
        _lyricBorders.Clear();
        _lyricRowViews.Clear();
        _lastHighlightIndex = -1;
        _lyricRowTops = Array.Empty<double>();

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
        {
            var label = new KaraokeLabel
            {
                Text = _viewModel.NoLyricsText,
                FontSize = 16,
                FontFamily = "OpenSansSemibold",
                TextColor = (Color)Application.Current!.Resources["TextHintColor"],
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 1,
                FillProgress = 1,
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions()
            };
            LyricStack.Children.Add(label);
            return;
        }

        BuildLyricStack(LyricStack, lines, _lyricLabels, _lyricBorders, _lyricRowViews, _lyricTransLabels);

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        if (!_isLandscape)
            HighlightLineWithoutScroll(idx);

        // 安卓端二次布局（OnSizeChanged 确定 _realWidth）可能在 720ms 之后才改变行高，
        // SizeChanged 触发时重启一轮测量即可覆盖。
        LyricClip.SizeChanged -= OnPortraitLyricClipSizeChanged;
        LyricClip.SizeChanged += OnPortraitLyricClipSizeChanged;

        // 布局完成后实测行高 + 钉当前行（恒钉 1/3 处）
        _lyricMeasureRetries = 0;
        _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (LyricClip.Handler == null) return;
            MeasureLyricRows();
            ScrollToLine(idx);
        }));
    }

    private void OnPortraitLyricClipSizeChanged(object? sender, EventArgs e)
    {
        if (_isLandscape || _lyricRowViews.Count == 0) return;
        _lyricMeasureRetries = 0;
        MeasureLyricRows();
        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        ScrollToLine(idx);
    }

    /// <summary>构建横屏歌词视图</summary>
    private void BuildLandscapeLyricViews()
    {
        LandscapeLyricStack.Children.Clear();
        _landscapeLyricLabels.Clear();
        _landscapeLyricTransLabels.Clear();
        _landscapeLyricBorders.Clear();
        _landscapeLyricRowViews.Clear();
        _landscapeLastHighlight = -1;
        _landscapeLyricRowTops = Array.Empty<double>();

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
        {
            var label = new KaraokeLabel
            {
                Text = _viewModel.NoLyricsText,
                FontSize = 16,
                FontFamily = "OpenSansSemibold",
                TextColor = (Color)Application.Current!.Resources["TextHintColor"],
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 1,
                FillProgress = 1,
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions()
            };
            LandscapeLyricStack.Children.Add(label);
            return;
        }

        BuildLyricStack(LandscapeLyricStack, lines, _landscapeLyricLabels, _landscapeLyricBorders, _landscapeLyricRowViews, _landscapeLyricTransLabels);

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        if (_isLandscape)
            HighlightLineWithoutScroll(idx);

        // 横屏歌词区尺寸变化时重启一轮测量（覆盖安卓二次布局/横竖屏切换）
        LandscapeLyricClip.SizeChanged -= OnLandscapeLyricClipSizeChanged;
        LandscapeLyricClip.SizeChanged += OnLandscapeLyricClipSizeChanged;

        // 布局完成后实测行高 + 钉当前行
        _landscapeLyricMeasureRetries = 0;
        _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (LandscapeLyricClip.Handler == null) return;
            MeasureLyricRows();
            ScrollToLine(idx);
        }));
    }

    private void OnLandscapeLyricClipSizeChanged(object? sender, EventArgs e)
    {
        if (!_isLandscape || _landscapeLyricRowViews.Count == 0) return;
        _landscapeLyricMeasureRetries = 0;
        MeasureLyricRows();
        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        ScrollToLine(idx);
    }

    /// <summary>通用歌词栈构建方法（字号在构建时按设置固定 → 行高恒定，滚动锚点稳定）。
    /// 正确原理：所有行（未唱/当前/已唱）统一按同一宽度提前分行 → 当前行自动分行、
    /// 放大 1.5x 后每一行视觉宽度 = 满屏宽，非当前行与当前行分行完全一致、不会因为变宽度跳动。
    /// - 外层 host 永远满屏宽 Margin=0（不剪裁不缩容器）
    /// - 所有 Label WidthRequest 统一 = 屏宽 / 倍率 - 6字安全宽度 → StaticLayout 在倒数第 4~6 个字提前断行
    /// - 当前行放大 1.5x：(屏宽/1.5 - 6字) × 1.5 = 屏宽 - 9字视觉宽 ＜ 屏宽 → 末尾不溢出，自动分行生效</summary>
    private void BuildLyricStack(VerticalStackLayout stack, IReadOnlyList<LrcLyricLine> lines,
        List<KaraokeLabel> labelList, List<Border> borderList, List<View> rowViews,
        List<KaraokeLabel> transLabelList)
    {
        var baseSize = _settings.FontSize;
        var transSize = Math.Max(10, baseSize - 2);
        var align = _settings.ToLayoutOptions().Alignment;
        // host 永远满屏宽
        var hostMargin = new Thickness(0);
        // 构建期默认都是非当前行，所以用满屏宽公式（不拆短行）。成为当前行时由 ApplyLabelWidthRole 动态改。
        double WrappedLabelWidth(double parentW)
            => parentW > 0 ? Math.Max(40, parentW - 1) : -1;

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = baseSize,
                FontFamily = "OpenSansSemibold",
                FontAttributes = FontAttributes.Bold,
                TextColor = Colors.White,
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 2,
                FillProgress = 0,
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions(),
                LineBreakMode = LineBreakMode.WordWrap,
                Opacity = 0.2,
                Padding = new Thickness(4, 6, 4, 6) // 左右各 +4：兜住 StrokeWidth=2 的外描边不被 Grid Clip 裁掉
            };
            // 缩放锚点：左对齐从左边缘向右生长，居中从中心生长，右对齐从右边缘向左生长
            label.AnchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);
            label.AnchorY = 0.5;

            var border = new Border
            {
                // 透明容器不要圆角：StrokeShape 同时是裁剪形状，会裁掉放大后歌词的四角
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(0),
                HorizontalOptions = LayoutOptions.Fill
            };
            var host = new ContentView { Content = label, HorizontalOptions = LayoutOptions.Fill, Margin = hostMargin };
            host.LayoutChanged += (s, _) =>
            {
                if (s is View v && v.Width > 0)
                    label.WidthRequest = WrappedLabelWidth(v.Width);
            };
            border.Content = host;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var vStack = new VerticalStackLayout { Spacing = 10, HorizontalOptions = LayoutOptions.Fill };
                vStack.Children.Add(border);

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = transSize,
                    FontFamily = "OpenSansSemibold",
                    FontAttributes = FontAttributes.Bold,
                    TextColor = Colors.White,
                    OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = _settings.ToTextAlignment(),
                    HorizontalOptions = _settings.ToLayoutOptions(),
                    LineBreakMode = LineBreakMode.WordWrap,
                    Opacity = 0.2,
                    Padding = new Thickness(4, 6, 4, 6) // 译文左右描边也留空间
                };
                transLabel.AnchorX = align == LayoutAlignment.Center ? 0.5 : (align == LayoutAlignment.End ? 1.0 : 0.0);
                transLabel.AnchorY = 0.5;
                var transBorder = new Border
                {
                    StrokeThickness = 0,
                    BackgroundColor = Colors.Transparent,
                    Padding = new Thickness(0),
                    HorizontalOptions = LayoutOptions.Fill
                };
                var transHost = new ContentView { Content = transLabel, HorizontalOptions = LayoutOptions.Fill, Margin = hostMargin };
                transHost.LayoutChanged += (s, _) =>
                {
                    if (s is View v && v.Width > 0)
                        transLabel.WidthRequest = WrappedLabelWidth(v.Width);
                };
                transBorder.Content = transHost;
                vStack.Children.Add(transBorder);
                stack.Children.Add(vStack);
                rowViews.Add(vStack);
                transLabelList.Add(transLabel);
            }
            else
            {
                stack.Children.Add(border);
                rowViews.Add(border);
                transLabelList.Add(null); // 索引对齐，无译文行占位
            }

            labelList.Add(label);
            borderList.Add(border);
        }
    }

    /// <summary>
    /// 按与当前行的距离设置行级高斯模糊（Android 12+ RenderEffect）：
    /// 当前行清晰（blur=0），距离 1 行轻微虚化 1dp，≥2 行 2dp（更远不再加深，避免糊成一团）。
    /// 低版本 Android 自动无操作，保持透明度分层兜底。
    /// </summary>
    private void ApplyRowBlur(KaraokeLabel lbl, int dist)
    {
#if ANDROID
        if (lbl.Handler?.PlatformView is CatClawMusic.Maui.Platforms.Android.KaraokePlatformView pv)
        {
            float r = dist <= 0 ? 0f : dist == 1 ? 1f : 2f;
            pv.SetRowBlur(r);
        }
#endif
    }

    /// <summary>切换某标签的自动分行宽度：
    /// - 非当前行：host.Width（满屏宽，短歌词不强行分行，长歌词按正常屏宽分）
    /// - 当前行：host.Width / LyricCurrentScale（按倍率缩宽度，放大 1.5x 后视觉 = 满屏宽，自动分行不切字）</summary>
    private void ApplyLabelWidthRole(KaraokeLabel lbl, bool isActive)
    {
        if (lbl == null) return;
        // 向上找外层 host ContentView（Border.Content → ContentView），拿父容器可用宽度
        if (lbl.Parent is not ContentView host || host.Width <= 0)
            host = lbl.Parent?.Parent as ContentView; // 兜底
        if (host == null || host.Width <= 0) return;

        double w;
        if (isActive)
            w = host.Width / LyricCurrentScale - 1;      // 当前行：屏宽/倍率 → 放大后视觉 = 满屏宽（自动分行）
        else
            w = host.Width - 1;                          // 非当前行：满屏宽，短行不拆
        lbl.WidthRequest = Math.Max(40, w);
    }

    /// <summary>仅高亮不滚动（构建期/初始化用）</summary>
    private void HighlightLineWithoutScroll(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;
        var transLabels = ActiveLyricTransLabels;

        for (int i = 0; i < labels.Count; i++)
        {
            var lbl = labels[i];
            if (i == index)
            {
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
                lbl.Scale = LyricCurrentScale;
            }
            else
            {
                lbl.FillProgress = 0;
                lbl.Opacity = 0.35;
                lbl.Scale = 1.0;
            }
            ApplyLabelWidthRole(lbl, i == index); // 当前行：满屏/倍率宽；非当前行：窄提前换行
            ApplyRowBlur(lbl, Math.Abs(i - index));
            if (transLabels.Count > i && transLabels[i] is KaraokeLabel tl)
                ApplyRowBlur(tl, Math.Abs(i - index)); // 译文与主歌词同步失焦
        }

        ApplyLyricRowGap(index, animate: false);
        ActiveLastHighlight = index;

        // ⚠ 当前行变窄（WidthRequest 从满宽→屏宽/1.5）导致 StaticLayout 多分一行、行高变大。
        // 紧接着的 ScrollToLine/PinActiveLine 仍用旧锚点表 tops + 旧 rowH，计算的位置比实际偏高
        // → 布局完成后（下一帧）强制重测锚点 + 再钉一次，保证不顶出裁剪区
        RelayoutAndRepinAfterHighlightChange(index);
    }

    /// <summary>高亮切换导致行高/宽度变化：下一帧强制重排 + 重测锚点 + 重新钉行（保证锚点与真实行高一致）。</summary>
    private void RelayoutAndRepinAfterHighlightChange(int index)
    {
        // 1) 先行高扩展：把动态 Margin 设好（必须先设，后面 MeasureLyricRows 才能读到正确行高）
        ApplyLyricRowGap(index, animate: false);
        var stack = ActiveLyricStack;
        stack.InvalidateMeasure(); // 强制 MAUI 重新测量+布局（让行高更新到扩展后的真实高度）
        Dispatcher.Dispatch(() =>
        {
            if (ActiveLyricClip.Handler == null) return;
            // 2) 清空锚点快照，强制 MeasureLyricRows 不做 stable 快速跳过
            if (_isLandscape) _lastLandscapeMeasuredTops = Array.Empty<double>();
            else _lastMeasuredTops = Array.Empty<double>();
            ref int retries = ref ActiveMeasureRetries;
            retries = 0;
            MeasureLyricRows();
            if (ActiveLyricRowTops.Length == ActiveLyricRowViews.Count && index >= 0 && index < ActiveLyricRowViews.Count)
                PinActiveLineNow(index); // 3) 以新锚点 + 新行高正确钉行
        });
    }

    /// <summary>高亮并滚动（当前行 Scale 放大 + 行距呼吸 + 整体平移钉 1/3 处）</summary>
    private void HighlightLine(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;

        // 每次高亮当前行之前：自检锚点表是否与真实行高匹配。
        // 锁屏解锁、前后台切换时，Activity/Handler 可能重建，
        // 旧锚点表依然被 ScrollToLine 使用 → 当前行越滚越偏。
        // 这里先校验一次，保证基准是最新实测行高。
        var rows = ActiveLyricRowViews;
        if (ActiveLyricRowTops.Length != rows.Count || !ValidateActiveTops())
        {
            ref int retries = ref ActiveMeasureRetries;
            retries = 0;
            if (_isLandscape) _lastLandscapeMeasuredTops = Array.Empty<double>();
            else _lastMeasuredTops = Array.Empty<double>();
            MeasureLyricRows();
        }

        ref int lastHl = ref ActiveLastHighlight;
        var affectedMin = Math.Max(0, Math.Min(index, lastHl) - 5);
        var affectedMax = Math.Min(labels.Count - 1, Math.Max(index, lastHl) + 5);
        var prev = lastHl;
        var transLabels = ActiveLyricTransLabels;

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = labels[i];
            if (i == index)
            {
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FillProgress = 0;
                lbl.Opacity = 0.35;
            }

            // Scale 走缓动动画（跳过当前/旧行，交给 AnimateLyricRowScale 处理）
            if (i != index && i != prev)
                lbl.Scale = i == index ? LyricCurrentScale : 1.0;

            ApplyLabelWidthRole(lbl, i == index); // 当前行：满屏/倍率宽（分行少）；非当前行：窄提前换行
            ApplyRowBlur(lbl, Math.Abs(i - index));
            if (transLabels.Count > i && transLabels[i] is KaraokeLabel tl)
                ApplyRowBlur(tl, Math.Abs(i - index)); // 译文与主歌词同步失焦
        }

        AnimateLyricRowScale(index, LyricCurrentScale);
        if (prev >= 0 && prev != index)
            AnimateLyricRowScale(prev, 1.0);

        ApplyLyricRowGap(index, animate: true);

        lastHl = index;

        if (!_userScrolling)
            ScrollToLine(index);

        // 当前行变窄 → StaticLayout 多分一行，行高变大，上一步 ScrollToLine 用的还是旧锚点+旧rowH（位置偏上被切）。
        // 下一帧强制重排、重测锚点、再钉一次，保证正确显示。
        RelayoutAndRepinAfterHighlightChange(index);
    }

    /// <summary>动态行高：给当前行的**容器**加真实高度（Scale 是渲染变换，视觉高 = labelH×1.5，
    /// 容器高度不变就会上下溢出被裁）。三件套：
    /// 1) 容器 HeightRequest = 原高 + 主歌词label高×(LyricCurrentScale−1) —— 补上放大溢出的高度；
    /// 2) 主歌词 Border 垂直 Fill —— 吸收新增空间，自身长到 1.5×labelH；
    /// 3) 主歌词 label 垂直居中 —— 放大内容以容器中心对称，上下都完整贴合不溢出。
    /// 邻居行 ±1 只留小呼吸 Margin；非当前行恢复自动高度 + 顶部对齐。</summary>
    private void ApplyLyricRowGap(int index, bool animate)
    {
        var rows = ActiveLyricRowViews;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            // 定位主歌词 Border（无译文：row 就是 Border；有译文：row 是 VStack，第 0 个是主歌词 Border）
            var mainBorder = row as Border
                             ?? (row as VerticalStackLayout)?.Children.FirstOrDefault() as Border;

            if (i == index && mainBorder != null)
            {
                // 主歌词 label 未放大布局高度（fallback 字号）
                double mainLblH = 0;
                if (mainBorder.Content is ContentView host && host.Content is KaraokeLabel lbl)
                    mainLblH = lbl.Height > 0 ? lbl.Height : _settings.FontSize;
                if (mainLblH <= 0) mainLblH = _settings.FontSize;

                double oldRowH = row.Height > 0 ? row.Height : mainLblH;
                // 容器高补上放大溢出 + 上下各 6px 呼吸，兜住 descender/外描边不被裁
                double newRowH = oldRowH + mainLblH * (LyricCurrentScale - 1.0) + LyricGapExtra * 2;

                // 1) 容器加高（动画插值）
                if (Math.Abs(row.HeightRequest - newRowH) > 0.5)
                {
                    row.AbortAnimation("GrowRowH");
                    if (animate)
                    {
                        double startH = row.HeightRequest > 0 ? row.HeightRequest : oldRowH;
                        row.Animate("GrowRowH", t =>
                        {
                            row.HeightRequest = startH + (newRowH - startH) * t;
                        }, 16, LyricAnimMs, Easing.CubicInOut);
                    }
                    else row.HeightRequest = newRowH;
                }
                // 2) 主歌词 Border 吸收新增空间
                mainBorder.VerticalOptions = LayoutOptions.Fill;
                // 3) host 也填满容器 + label 居中 → 放大内容以容器中心对称，上下留出呼吸空间
                if (mainBorder.Content is ContentView hostC)
                {
                    hostC.VerticalOptions = LayoutOptions.Fill;
                    if (hostC.Content is KaraokeLabel lblC)
                        lblC.VerticalOptions = LayoutOptions.Center;
                }
            }
            else if (mainBorder != null)
            {
                // 非当前行：恢复自动高度 + 顶部对齐
                if (row.HeightRequest > 0) row.HeightRequest = -1;
                mainBorder.VerticalOptions = LayoutOptions.Start;
                if (mainBorder.Content is ContentView hostN)
                {
                    hostN.VerticalOptions = LayoutOptions.Start;
                    if (hostN.Content is KaraokeLabel lblN)
                        lblN.VerticalOptions = LayoutOptions.Start;
                }
            }

            // 邻居行小呼吸 Margin
            double top = 0, bottom = 0;
            if (i == index - 1) bottom = LyricGapExtra;
            else if (i == index + 1) top = LyricGapExtra;

            var targetMargin = new Thickness(0, top, 0, bottom);
            if (Math.Abs(row.Margin.Top - targetMargin.Top) < 0.5 &&
                Math.Abs(row.Margin.Bottom - targetMargin.Bottom) < 0.5) continue;

            row.AbortAnimation("ApplyLyricRowGap");
            if (animate)
            {
                double sTop = row.Margin.Top, sBot = row.Margin.Bottom;
                double tTop = targetMargin.Top, tBot = targetMargin.Bottom;
                row.Animate("ApplyLyricRowGap", t =>
                {
                    row.Margin = new Thickness(0, sTop + (tTop - sTop) * t, 0, sBot + (tBot - sBot) * t);
                }, 16, LyricAnimMs, Easing.CubicInOut);
            }
            else
            {
                row.Margin = targetMargin;
            }
        }
    }

    /// <summary>缓动把第 i 行缩放到目标倍率（Scale 渲染变换，不影响行高与滚动锚点）。</summary>
    private void AnimateLyricRowScale(int i, double target)
    {
        var labels = ActiveLyricLabels;
        if (i < 0 || i >= labels.Count) return;
        var lbl = labels[i];
        if (Math.Abs(lbl.Scale - target) < 0.01) return;

        lbl.AbortAnimation("ScaleTo");
        _ = lbl.ScaleTo(target, LyricAnimMs, Easing.CubicInOut);
    }

    /// <summary>
    /// 实测各行顶部 Y（由实测行高累加）与裁剪区高度，供"当前行恒钉 1/3 处"锚点计算。
    /// 关键约束：所有行高度必须都 > 0.5 才写 tops 表，避免回退猜测造成累积残差 → 越滚越偏。
    /// 未就绪按 150ms 间隔重试，最多 20 次（3 秒覆盖安卓 OnSizeChanged 二次布局）。
    /// 新增：_lastMeasuredTops 快照 → 布局未变化时快速跳过，减少重复累加开销。
    /// </summary>
    private void MeasureLyricRows()
    {
        var rows = ActiveLyricRowViews;
        if (rows.Count == 0) return;

        var clip = ActiveLyricClip;
        double clipH = clip.Bounds.Height;
        if (clipH <= 0)
        {
            clipH = _isLandscape ? _landscapeLyricClipHeight : _lyricClipHeight;
            if (clipH <= 0) return;
        }

        var stack = ActiveLyricStack;
        double spacing = stack.Spacing;
        double startY = stack.Padding.Top;

        // 前置：若保留了上一次的合法锚点表且布局未变，直接跳过（避免不必要的重试）
        var lastTops = _isLandscape ? _lastLandscapeMeasuredTops : _lastMeasuredTops;
        if (lastTops.Length == rows.Count)
        {
            double yy = startY;
            bool stable = true;
            for (int i = 0; i < rows.Count; i++)
            {
                var h = rows[i].Height;
                if (h <= 0.5) { stable = false; break; }
                if (Math.Abs(lastTops[i] - yy) > 0.5) { stable = false; break; }
                yy += h + spacing;
            }
            if (stable)
            {
                if (_isLandscape) _landscapeLyricClipHeight = clipH;
                else _lyricClipHeight = clipH;
                return;
            }
        }

        // 预扫：整表全干净才提交
        bool allReady = true;
        for (int i = 0; i < rows.Count; i++)
        {
            if (rows[i].Height <= 0.5) { allReady = false; break; }
        }
        if (!allReady)
        {
            ref int retries = ref ActiveMeasureRetries;
            if (retries < 20)
            {
                retries++;
                _ = Task.Delay(150).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (ActiveLyricClip.Handler == null) return;
                    MeasureLyricRows();
                    var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                    if (ActiveLyricRowTops.Length == rows.Count)
                        PinActiveLineNow(idx);
                }));
            }
            return;
        }

        if (_isLandscape)
            _landscapeLyricClipHeight = clipH;
        else
            _lyricClipHeight = clipH;

        var tops = new double[rows.Count];
        // 起点含 Stack 的 Top Padding，避免每行中心恒定偏下
        double y = startY;
        for (int i = 0; i < rows.Count; i++)
        {
            tops[i] = y;
            y += rows[i].Height + spacing;
        }

        if (_isLandscape)
        {
            _landscapeLyricRowTops = tops;
            _lastLandscapeMeasuredTops = (double[])tops.Clone();
        }
        else
        {
            _lyricRowTops = tops;
            _lastMeasuredTops = (double[])tops.Clone();
        }
    }

    /// <summary>
    /// 计算目标行应钉住的平移量 TranslationY（在 LyricStack 内部坐标）。
    /// 常规：当前行中心钉裁剪区 30% 处；但当前行放大（Scale 1.5x）后视觉高度变大，
    /// 若按 30% 钉会把超出行顶出裁剪区被剪裁 → 用"视觉高度"做上下夹紧：
    /// - 放得下：中心在 [minC, maxC] 区间内贴近 30% 处（保证整行完整可见）
    /// - 行太高放不下（视觉高 ≥ 裁剪区）：顶格显示，至少头部完整可见
    /// </summary>
    private double ComputePinnedTargetY(int index, double clipH)
    {
        var rows = ActiveLyricRowViews;
        var rowH = rows[index].Height > 0 ? rows[index].Height : 40;
        double visualH = rowH * LyricCurrentScale;            // 当前行放大后的视觉高度
        double topMargin = ActiveLyricStack.Padding.Top + 4;  // 顶部/底部安全边距
        double minC = topMargin + visualH / 2.0;              // 顶部不溢出的最小中心
        double maxC = clipH - topMargin - visualH / 2.0;      // 底部不溢出的最大中心
        double center = clipH * 0.30;                         // 理想：1/3 处
        if (minC <= maxC)
            center = Math.Clamp(center, minC, maxC);          // 放得下 → 夹紧保证完整显示
        else
            center = minC;                                    // 超高 → 顶格，头部优先可见
        return center - (ActiveLyricRowTops[index] + rowH / 2.0);
    }

    /// <summary>
    /// 把歌词缓动钉到指定行：当前行中心**恒定**落在裁剪区 1/3 处。
    /// 滚动 = 整体平移 LyricStack.TranslationY（合成线程变换，不重排），380ms CubicInOut，
    /// 首尾不夹紧 → 位置永远一致。用户拖动（_userScrolling）期间不自动滚动。
    /// </summary>
    private void ScrollToLine(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;

        var rows = ActiveLyricRowViews;
        if (ActiveLyricRowTops.Length != rows.Count) return;

        try
        {
            var clipH = ActiveLyricClipHeight;
            if (clipH <= 0) return;

            double targetY = ComputePinnedTargetY(index, clipH);

            ActiveLyricStack.CancelAnimations();
            ActiveLyricStack.TranslateTo(0, targetY, LyricAnimMs, Easing.CubicInOut);
        }
        catch { }
    }

    /// <summary>给歌词裁剪区挂平移手势：用户拖动即暂停跟随 3 秒（期间不自动滚动）。</summary>
    private void WireLyricPanGesture()
    {
        if (_panWired) return;
        _panWired = true;

        WirePan(LyricClip, LyricStack);
        WirePan(LandscapeLyricClip, LandscapeLyricStack);
    }

    private void WirePan(Grid clip, VerticalStackLayout stack)
    {
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (e.StatusType == GestureStatus.Started)
            {
                stack.CancelAnimations();
                _panStartY = stack.TranslationY;
                OnUserScrolled(); // 拖动 → 3 秒内不自动跟随
            }
            else if (e.StatusType == GestureStatus.Running)
            {
                stack.TranslationY = _panStartY + e.TotalY;
            }
        };
        clip.GestureRecognizers.Add(pan);
    }

    private void OnUserScrolled()
    {
        _userScrolling = true;
        _ = ResetUserScrollingAsync();
    }

    private async Task ResetUserScrollingAsync()
    {
        await Task.Delay(3000);
        _userScrolling = false;
    }

    /// <summary>轻击歌词区域返回播放页</summary>
    private void OnLyricsAreaTapped(object? sender, TappedEventArgs e)
    {
        OnBackClicked(sender, e);
    }

    /// <summary>返回播放页</summary>
    private void OnBackClicked(object? sender, EventArgs e)
    {
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
        {
            _ = Shell.Current.Navigation.PopAsync();
            return;
        }
#if WINDOWS
        _ = Shell.Current.GoToAsync("//main");
#else
        MainPage.Instance?.SwitchToTab(0);
#endif
    }

    /// <summary>点击歌词设置按钮</summary>
    private void OnLyricsSettingsClicked(object? sender, EventArgs e)
    {
        if (LyricsSettingsPopup.PopupContent.Children.Count <= 1)
        {
            var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
            var inactiveColor = (Color)Application.Current.Resources["ChipInactiveColor"];
            var textSecondary = (Color)Application.Current.Resources["TextSecondaryColor"];
            var textHint = (Color)Application.Current.Resources["TextHintColor"];

            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词模式", textHint));
            LyricsSettingsPopup.AddContent(BuildSegmentedControl(
                ("逐行", LyricsSettingsService.Mode.Line),
                ("逐字", LyricsSettingsService.Mode.Word),
                _settings.LyricsMode,
                value =>
                {
                    _settings.LyricsMode = value;
                    RebuildLyricsView();
                },
                primaryColor, inactiveColor, Colors.White, textSecondary));

            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词位置显示", textHint));
            LyricsSettingsPopup.AddContent(BuildSegmentedControl(
                ("居左", LyricsSettingsService.Alignment.Left),
                ("居中", LyricsSettingsService.Alignment.Center),
                ("居右", LyricsSettingsService.Alignment.Right),
                _settings.LyricsAlignment,
                value =>
                {
                    _settings.LyricsAlignment = value;
                    RebuildLyricsView();
                },
                primaryColor, inactiveColor, Colors.White, textSecondary));

            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词字体大小", textHint));
            LyricsSettingsPopup.AddContent(BuildFontSizeSlider(primaryColor, textSecondary, textHint));

            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("智能删除空行", textHint));
            LyricsSettingsPopup.AddContent(BuildToggleSwitch(
                "紧凑显示，移除歌词中的空行",
                _settings.RemoveEmptyLines,
                value =>
                {
                    _settings.RemoveEmptyLines = value;
                    _viewModel.RefreshFilteredLines();
                    RebuildLyricsView();
                },
                primaryColor, textSecondary, textHint));
        }

        LyricsSettingsPopup.Open();
    }

    /// <summary>重建所有歌词视图</summary>
    private void RebuildLyricsView()
    {
        _viewModel.RefreshFillProgress();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BuildLyricViews();
        });
    }

    private Label BuildSectionLabel(string text, Color color)
    {
        return new Label
        {
            Text = text,
            FontSize = 13,
            TextColor = color,
            Margin = new Thickness(0, 0, 0, 8),
            FontAttributes = FontAttributes.None
        };
    }

    private View BuildSpacer(double height)
    {
        return new BoxView { HeightRequest = height, BackgroundColor = Colors.Transparent };
    }

    private View BuildSegmentedControl<T>(
        (string Label, T Value) option1,
        (string Label, T Value) option2,
        T currentValue,
        Action<T> onSelected,
        Color activeColor, Color inactiveColor,
        Color activeTextColor, Color inactiveTextColor) where T : Enum
    {
        return BuildSegmentedControlCore(new[] { option1, option2 }, currentValue, onSelected, activeColor, inactiveColor, activeTextColor, inactiveTextColor);
    }

    private View BuildSegmentedControl<T>(
        (string Label, T Value) option1,
        (string Label, T Value) option2,
        (string Label, T Value) option3,
        T currentValue,
        Action<T> onSelected,
        Color activeColor, Color inactiveColor,
        Color activeTextColor, Color inactiveTextColor) where T : Enum
    {
        return BuildSegmentedControlCore(new[] { option1, option2, option3 }, currentValue, onSelected, activeColor, inactiveColor, activeTextColor, inactiveTextColor);
    }

    private View BuildSegmentedControlCore<T>(
        (string Label, T Value)[] options,
        T currentValue,
        Action<T> onSelected,
        Color activeColor, Color inactiveColor,
        Color activeTextColor, Color inactiveTextColor) where T : Enum
    {
        var colCount = options.Length;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                Enumerable.Range(0, colCount)
                    .Select(_ => new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) })
                    .ToArray()),
            ColumnSpacing = 6,
            HeightRequest = 44
        };

        var buttons = new List<Button>();

        for (int i = 0; i < colCount; i++)
        {
            var opt = options[i];
            var isActive = EqualityComparer<T>.Default.Equals(opt.Value, currentValue);

            var btn = new Button
            {
                Text = opt.Label,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = isActive ? activeTextColor : inactiveTextColor,
                BackgroundColor = isActive ? activeColor : inactiveColor,
                CornerRadius = 22,
                HeightRequest = 44,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalOptions = LayoutOptions.Fill
            };

            var captured = opt.Value;
            btn.Clicked += (_, _) =>
            {
                onSelected(captured);
                for (int j = 0; j < options.Length; j++)
                {
                    var sel = EqualityComparer<T>.Default.Equals(options[j].Value, captured);
                    buttons[j].BackgroundColor = sel ? activeColor : inactiveColor;
                    buttons[j].TextColor = sel ? activeTextColor : inactiveTextColor;
                }
            };

            buttons.Add(btn);
            grid.Add(btn, i);
        }

        return grid;
    }

    private View BuildFontSizeSlider(Color primaryColor, Color textSecondary, Color textHint)
    {
        var minSize = LyricsSettingsService.MinFontSize;
        var maxSize = LyricsSettingsService.MaxFontSize;
        var currentSize = _settings.FontSize;

        var valueLabel = new Label
        {
            Text = $"{currentSize:F0}",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = primaryColor,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var slider = new Slider
        {
            Minimum = minSize,
            Maximum = maxSize,
            Value = currentSize,
            ThumbColor = primaryColor,
            MinimumTrackColor = primaryColor,
            MaximumTrackColor = (Color)Application.Current!.Resources["GlassStrokeColor"],
            HeightRequest = 40
        };
        slider.ValueChanged += (_, e) =>
        {
            var newSize = Math.Round(e.NewValue);
            _settings.FontSize = newSize;
            valueLabel.Text = $"{newSize:F0}";
            RebuildLyricsView();
        };

        var rangeGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            }
        };
        rangeGrid.Add(new Label { Text = "A", FontSize = 11, TextColor = textHint }, 0);
        rangeGrid.Add(new Label { Text = $"{maxSize:F0}", FontSize = 11, TextColor = textHint, HorizontalOptions = LayoutOptions.End }, 2);

        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                valueLabel,
                slider,
                rangeGrid
            }
        };
    }

    private View BuildToggleSwitch(
        string description, bool currentValue,
        Action<bool> onToggled,
        Color primaryColor, Color textSecondary, Color textHint)
    {
        var descLabel = new Label
        {
            Text = description,
            FontSize = 13,
            TextColor = textSecondary,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        var toggle = new Switch
        {
            IsToggled = currentValue,
            OnColor = primaryColor,
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        toggle.Toggled += (_, e) => onToggled(e.Value);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            },
            HeightRequest = 44,
            Children = { descLabel, toggle }
        };
    }
}

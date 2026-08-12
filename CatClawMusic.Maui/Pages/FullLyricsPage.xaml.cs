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
                App.Resumed -= OnAppResumed;
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
    private void OnLyricsAreaTapped(object? sender, TappedEventArgs e)
    {
        OnBackClicked(sender, e);
    }

    /// <summary>返回播放页</summary>
    private void OnBackClicked(object? sender, EventArgs e)
    {
        var shell = DesktopNavigation.TryGetShell();
        if (shell != null && shell.Navigation.NavigationStack.Count > 1)
        {
            _ = shell.Navigation.PopAsync();
            return;
        }
        if (shell != null)
        {
#if WINDOWS
            _ = shell.GoToAsync("//main");
#else
            MainPage.Instance?.SwitchToTab(0);
#endif
            return;
        }
        // 桌面无 Shell：关闭嵌入恢复原 tab
        DesktopNavigation.CloseEmbedded();
    }
}

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
    private readonly List<Border> _lyricBorders = new();
    private readonly List<KaraokeLabel> _landscapeLyricLabels = new();
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
    private double _lyricClipHeight;
    private double _landscapeLyricClipHeight;
    private int _lyricMeasureRetries;
    private int _landscapeLyricMeasureRetries;
    private double _panStartY;
    private bool _panWired;

    // 歌词行视觉层次（与播放页一致）：所有行统一字号（锚点依赖行高恒定），当前行用 Scale 放大 + 行距呼吸。
    private const double LyricCurrentScale = 1.5;
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

            // 切换到横屏后滚动到当前歌词行
            var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
            _ = Task.Delay(100).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_landscapeLyricLabels.Count > 0)
                    {
                        HighlightLineWithoutScroll(idx);
                        ScrollToLine(idx);
                    }
                }));
        }
        else
        {
            // 竖屏：恢复原始布局
            LandscapeContent.IsVisible = false;
            PortraitContent.IsVisible = true;
            ApplySafeArea();

            // 切换到竖屏后滚动到当前歌词行
            var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
            _ = Task.Delay(100).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (_lyricLabels.Count > 0)
                    {
                        HighlightLineWithoutScroll(idx);
                        ScrollToLine(idx);
                    }
                }));
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

            _ = Task.Delay(100).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                    HighlightLine(idx);
                }));
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

        BuildLyricStack(LyricStack, lines, _lyricLabels, _lyricBorders, _lyricRowViews);

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        if (!_isLandscape)
            HighlightLineWithoutScroll(idx);

        // 布局完成后实测行高 + 钉当前行（恒钉 1/3 处）
        _lyricMeasureRetries = 0;
        _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (LyricClip.Handler == null) return;
            MeasureLyricRows();
            ScrollToLine(idx);
        }));
    }

    /// <summary>构建横屏歌词视图</summary>
    private void BuildLandscapeLyricViews()
    {
        LandscapeLyricStack.Children.Clear();
        _landscapeLyricLabels.Clear();
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

        BuildLyricStack(LandscapeLyricStack, lines, _landscapeLyricLabels, _landscapeLyricBorders, _landscapeLyricRowViews);

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        if (_isLandscape)
            HighlightLineWithoutScroll(idx);

        // 布局完成后实测行高 + 钉当前行
        _landscapeLyricMeasureRetries = 0;
        _ = Task.Delay(60).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
        {
            if (LandscapeLyricClip.Handler == null) return;
            MeasureLyricRows();
            ScrollToLine(idx);
        }));
    }

    /// <summary>通用歌词栈构建方法（字号在构建时按设置固定 → 行高恒定，滚动锚点稳定）</summary>
    private void BuildLyricStack(VerticalStackLayout stack, IReadOnlyList<LrcLyricLine> lines,
        List<KaraokeLabel> labelList, List<Border> borderList, List<View> rowViews)
    {
        var baseSize = _settings.FontSize;
        var transSize = Math.Max(10, baseSize - 2);

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = baseSize,
                FontFamily = "OpenSansRegular",
                FontAttributes = FontAttributes.None,
                TextColor = Colors.White,
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 2,
                FillProgress = 0,
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions(),
                LineBreakMode = LineBreakMode.WordWrap,
                Opacity = 0.2,
                Padding = new Thickness(16, 6)
            };
            // 缩放锚点（当前行 Scale 放大用）：居中从中心生长，左对齐向右生长
            label.AnchorX = _settings.ToLayoutOptions().Alignment == LayoutAlignment.Center ? 0.5 : 0;
            label.AnchorY = 0.5;

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(22, 0),
                // 必须 Fill：让 KaraokeLabel 拿到父级宽度约束 → StaticLayout 正常换行（长歌词分行显示）
                HorizontalOptions = LayoutOptions.Fill
            };
            border.Content = label;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var vStack = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Fill };
                vStack.Children.Add(border);

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = transSize,
                    FontFamily = "OpenSansRegular",
                    FontAttributes = FontAttributes.None,
                    TextColor = Colors.White,
                    OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = _settings.ToTextAlignment(),
                    HorizontalOptions = _settings.ToLayoutOptions(),
                    LineBreakMode = LineBreakMode.WordWrap,
                    Opacity = 0.2,
                    Padding = new Thickness(16, 6)
                };
                var transBorder = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                    StrokeThickness = 0,
                    BackgroundColor = Colors.Transparent,
                    Padding = new Thickness(22, 0),
                    HorizontalOptions = LayoutOptions.Fill
                };
                transBorder.Content = transLabel;
                vStack.Children.Add(transBorder);
                stack.Children.Add(vStack);
                rowViews.Add(vStack);
            }
            else
            {
                stack.Children.Add(border);
                rowViews.Add(border);
            }

            labelList.Add(label);
            borderList.Add(border);
        }
    }

    /// <summary>
    /// 按与当前行的距离设置行级高斯模糊（Android 12+ RenderEffect）：
    /// 当前行清晰（blur=0），距离 1 行轻微虚化 3.5dp，≥2 行 6.5dp（参照 Windows 景深档位）。
    /// 低版本 Android 自动无操作，保持透明度分层兜底。
    /// </summary>
    private void ApplyRowBlur(KaraokeLabel lbl, int dist)
    {
#if ANDROID
        if (lbl.Handler?.PlatformView is CatClawMusic.Maui.Platforms.Android.KaraokePlatformView pv)
        {
            float r = dist <= 0 ? 0f : dist == 1 ? 3.5f : 6.5f;
            pv.SetRowBlur(r);
        }
#endif
    }

    /// <summary>仅高亮不滚动（构建期/初始化用）</summary>
    private void HighlightLineWithoutScroll(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;

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
            ApplyRowBlur(lbl, Math.Abs(i - index));
        }

        ApplyLyricRowGap(index, animate: false);
        ActiveLastHighlight = index;
    }

    /// <summary>高亮并滚动（当前行 Scale 放大 + 行距呼吸 + 整体平移钉 1/3 处）</summary>
    private void HighlightLine(int index)
    {
        var labels = ActiveLyricLabels;
        if (index < 0 || index >= labels.Count) return;

        ref int lastHl = ref ActiveLastHighlight;
        var affectedMin = Math.Max(0, Math.Min(index, lastHl) - 5);
        var affectedMax = Math.Min(labels.Count - 1, Math.Max(index, lastHl) + 5);
        var prev = lastHl;

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

            ApplyRowBlur(lbl, Math.Abs(i - index));
        }

        AnimateLyricRowScale(index, LyricCurrentScale);
        if (prev >= 0 && prev != index)
            AnimateLyricRowScale(prev, 1.0);

        ApplyLyricRowGap(index, animate: true);

        lastHl = index;

        if (!_userScrolling)
            ScrollToLine(index);
    }

    /// <summary>只把当前行上下两条缝隙撑开 <see cref="LyricGapExtra"/>（行容器 TranslationY，不重排）。</summary>
    private void ApplyLyricRowGap(int index, bool animate)
    {
        var rows = ActiveLyricRowViews;
        for (int i = 0; i < rows.Count; i++)
        {
            double target = i < index ? -LyricGapExtra
                          : i > index ? LyricGapExtra
                          : 0;

            var row = rows[i];
            if (Math.Abs(row.TranslationY - target) < 0.5) continue;

            row.AbortAnimation("TranslateTo");
            if (animate)
                _ = row.TranslateTo(0, target, LyricAnimMs, Easing.CubicInOut);
            else
                row.TranslationY = target;
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
    /// 行高未就绪时自动重试，最多 6 次。
    /// </summary>
    private void MeasureLyricRows()
    {
        var rows = ActiveLyricRowViews;
        if (rows.Count == 0) return;

        if (_isLandscape)
        {
            _landscapeLyricClipHeight = LandscapeLyricClip.Bounds.Height;
            _landscapeLyricRowTops = new double[rows.Count];
        }
        else
        {
            _lyricClipHeight = LyricClip.Bounds.Height;
            _lyricRowTops = new double[rows.Count];
        }

        double y = 0;
        double spacing = ActiveLyricStack.Spacing;   // ⚠ 行间距必须计入锚点累加，否则每行偏差 → 越滚越偏
        bool needsRetry = false;
        for (int i = 0; i < rows.Count; i++)
        {
            ActiveLyricRowTops[i] = y;               // 由前序行高累加，不读 Bounds.Y
            var h = rows[i].Height;
            if (h <= 0.5)
            {
                h = 40;                              // 未就绪先用回退值，重试会校正
                needsRetry = true;
            }
            y += h + spacing;
        }

        if (needsRetry && ActiveMeasureRetries < 6)
        {
            ActiveMeasureRetries++;
            _ = Task.Delay(120).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ActiveLyricClip.Handler == null) return;
                MeasureLyricRows();
                var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                ScrollToLine(idx);
            }));
        }
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

            var rowH = rows[index].Height > 0 ? rows[index].Height : 40;
            double targetY = clipH * 0.30 - (ActiveLyricRowTops[index] + rowH / 2.0);

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

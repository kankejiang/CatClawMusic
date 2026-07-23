using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>正在播放页面，展示当前播放歌曲的封面、进度、歌词及播放控制。</summary>
public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private readonly SleepTimerService _sleepTimer;
    private readonly IMusicLibraryService _musicLibrary;
    private readonly AudioPlayerService _audioPlayer;
    private bool _isDragging;
    private readonly List<KaraokeLabel> _lyricLabels = new();
    private readonly List<Border> _lyricBorders = new();
    private int _lastHighlightIndex = -1;
    private bool _isLandscape;
    private int _lastCoverSize;
    private bool _landscapeLyricsMode;
    private readonly List<KaraokeLabel> _landscapeLyricLabels = new();
    private readonly List<Border> _landscapeLyricBorders = new();
    private int _landscapeLastHighlight = -1;

    /// <summary>初始化 <see cref="NowPlayingPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">当前播放视图模型，提供歌曲、进度与歌词数据。</param>
    /// <param name="sleepTimer">睡眠定时服务。</param>
    /// <param name="musicLibrary">音乐库服务（歌单操作）。</param>
    /// <param name="audioPlayer">音频播放服务（均衡器应用）。</param>
    public NowPlayingPage(NowPlayingViewModel viewModel, SleepTimerService sleepTimer,
        IMusicLibraryService musicLibrary, AudioPlayerService audioPlayer)
    {
        InitializeComponent();
        _viewModel = viewModel;
        _sleepTimer = sleepTimer;
        _musicLibrary = musicLibrary;
        _audioPlayer = audioPlayer;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
        Loaded += OnPageLoaded;

        // 监听 RootGrid 尺寸变化（Content 被 MainPage 提取后 OnSizeAllocated 不会触发）
        RootGrid.SizeChanged += OnRootSizeChanged;

        // 设置收起图标（使用 ImageSourceHelper 确保 Windows 端正确加载）
        CollapseIcon.Source = ImageSourceHelper.FromNameOriginal("ic_collapse");
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        var w = RootGrid.Width;
        var h = RootGrid.Height;
        if (w > 0 && h > 0)
            ApplyLayoutForOrientation(w, h);
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
        UpdateTimerButtonState();
#if WINDOWS
        UpdateMaximizeIcon();
#endif
    }

    /// <summary>系统栏高度变化时触发，更新内容区域的顶部 padding 以避开状态栏</summary>
    private void OnSafeAreaChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() => ApplySafeArea());
    }

    /// <summary>给 RootGrid 应用 SafeArea 顶部 padding（雾面背景不应用，保持延伸到状态栏）</summary>
    private void ApplySafeArea()
    {
        // RootGrid 原始 Padding 为 (20,12,20,16)，顶部加上状态栏高度
        var top = SafeAreaHelper.TopInset;
        RootGrid.Padding = new Thickness(20, top + 12, 20, 16);
    }

    /// <summary>页面尺寸分配时触发，根据宽高比切换横屏/竖屏布局。</summary>
    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        ApplyLayoutForOrientation(width, height);
    }

    /// <summary>根据屏幕方向动态切换主体内容与底部控件的布局。
    /// 横屏（宽>高）：封面在左、歌词在右，底部使用三栏控件（等同 PC 布局）。
    /// 竖屏（高>=宽）：封面在上、歌词在下，底部使用 5 列等分控件。</summary>
    private void ApplyLayoutForOrientation(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        var isLandscape = width > height;
        // 封面尺寸：竖屏根据较短边计算；横屏尽量占满左栏（取左栏宽与可用高的最小值），上限与竖屏一致 560
        var coverSize = isLandscape
            ? Math.Clamp((int)Math.Min(height - 32, (width - 148) / 2.15), 240, 560)
            : Math.Clamp((int)(width - 60), 280, 560);

        if (_isLandscape == isLandscape && _lastCoverSize == coverSize)
            return;

        var orientationChanged = _isLandscape != isLandscape;
        _isLandscape = isLandscape;

        if (isLandscape)
        {
            // 横屏：使用独立 LandscapeRoot 布局（左封面 + 右信息/控制），隐藏竖屏与三栏控件
            if (orientationChanged)
            {
                LandscapeRoot.IsVisible = true;
                MainContent.IsVisible = false;
                BottomControlsRoot.IsVisible = false;
                TopNavBar.IsVisible = false;
                PhoneControls.IsVisible = false;
                DesktopControls.IsVisible = false;
                BottomActionBar.IsVisible = false;
            }
            RightHalf.ClearValue(HeightRequestProperty);
            MainContent.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition { Height = GridLength.Auto },
                new RowDefinition { Height = GridLength.Star }
            };
        }
        else
        {
            // 竖屏：封面上、歌词下，歌词区域限制为5行高度；恢复竖屏控件并隐藏横屏布局
            if (orientationChanged)
            {
                LandscapeRoot.IsVisible = false;
                MainContent.IsVisible = true;
                BottomControlsRoot.IsVisible = true;
                TopNavBar.IsVisible = true;
                PhoneControls.IsVisible = true;
                DesktopControls.IsVisible = false;
                BottomActionBar.IsVisible = true;
                // 退出横屏时复位歌词模式，恢复信息区显示
                LandscapeTitleBlock.IsVisible = true;
                LandscapeToolsRow.IsVisible = true;
                LandscapeCurrentLyric.IsVisible = true;
                LandscapeLyricsScroll.IsVisible = false;
                _landscapeLyricsMode = false;
            }
            // 5行歌词高度估算：约 200-220px
            RightHalf.HeightRequest = 200;
            MainContent.RowDefinitions = new RowDefinitionCollection
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Auto }
            };
        }

        if (_lastCoverSize != coverSize)
        {
            _lastCoverSize = coverSize;
            CoverGlow.WidthRequest = coverSize;
            CoverGlow.HeightRequest = coverSize;
            CoverArea.WidthRequest = coverSize;
            CoverArea.HeightRequest = coverSize;
            ArtworkImage.WidthRequest = coverSize;
            ArtworkImage.HeightRequest = coverSize;
            // 横屏布局封面同步尺寸（LandscapeRoot 隐藏时设置也无害）
            LandscapeCover.WidthRequest = coverSize;
            LandscapeCover.HeightRequest = coverSize;
            LandscapeCoverImage.WidthRequest = coverSize;
            LandscapeCoverImage.HeightRequest = coverSize;
        }
    }

    /// <summary>当页面显示在屏幕上时触发，加载当前歌曲、构建歌词视图并启动进度定时器。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        CrashReporter.MarkStage("NowPlayingPage.OnAppearing: 开始");
#if WINDOWS
        Shell.SetNavBarIsVisible(this, false);
#endif
        ApplySafeArea();
        await _viewModel.LoadCurrentSongAsync();
        CrashReporter.MarkStage("NowPlayingPage.OnAppearing: LoadCurrentSongAsync 完成");

        if (_viewModel.Duration > 0)
            ProgressSlider.Maximum = _viewModel.Duration;

        // 切页重建或滑块被重置后，立即把当前进度同步到滑块：
        // 若 Progress 值恰与前值相同（未变化），不会触发 PropertyChanged，
        // 滑块会停在 XAML 初始值 0，表现为「进度条归零」。这里强制同步一次。
        if (_viewModel.Progress > 0)
            ProgressSlider.Value = _viewModel.Progress;

        // 横屏布局滑块同步（与竖屏 ProgressSlider 保持一致）
        if (_viewModel.Duration > 0)
            LandscapeProgressSlider.Maximum = _viewModel.Duration;
        if (_viewModel.Progress > 0)
            LandscapeProgressSlider.Value = _viewModel.Progress;

        Application.Current!.RequestedThemeChanged += OnThemeChanged;

        // 仅在歌词行数变化时重建视图，避免切页时大量控件销毁/重建
        var allLines = _viewModel.AllLyricLines;
        if (allLines == null || _lyricLabels.Count != allLines.Count)
            BuildLyricViews();
        else if (_viewModel.CurrentLyricIndexObservable >= 0 && _lyricLabels.Count > 0)
            HighlightLine(_viewModel.CurrentLyricIndexObservable);
        CrashReporter.MarkStage("NowPlayingPage.OnAppearing: 歌词视图构建完成");

        // 延迟滚动到当前歌词行，确保布局完成后再定位
        if (_lyricLabels.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
            _ = Task.Delay(100).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                    HighlightLine(_viewModel.CurrentLyricIndexObservable)));
        }

        // 整个进入播放页流程无异常完成，清除阶段标记（若此后再崩，说明是后续交互，非进入阶段）
        CrashReporter.ClearStage();
    }

    /// <summary>当页面从屏幕上消失时触发，取消订阅主题变更事件。</summary>
    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Application.Current!.RequestedThemeChanged -= OnThemeChanged;
    }

    /// <summary>当系统主题发生变更时触发，在主线程上重建歌词视图以应用新主题颜色。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">主题变更事件参数。</param>
    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(BuildLyricViews);
    }

    /// <summary>当视图模型属性变更时触发，根据变更的属性重建歌词视图或更新高亮行。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">属性变更事件参数。</param>
    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NowPlayingViewModel.AllLyricLines) ||
            e.PropertyName == nameof(NowPlayingViewModel.HasLyrics))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isLandscape && _landscapeLyricsMode)
                    BuildLandscapeLyricViews();
                else
                    BuildLyricViews();
            });
            return;
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLyricIndexObservable))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (_isLandscape && _landscapeLyricsMode)
                    HighlightLandscapeLine(_viewModel.CurrentLyricIndexObservable);
                else
                    HighlightLine(_viewModel.CurrentLyricIndexObservable);
            });
            return;
        }

        // 逐字填充进度变化：直接更新当前行 KaraokeLabel 的 FillProgress
        // PropertyChanged 已在主线程触发，无需额外 dispatch
        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLineFillProgress))
        {
            var idx = _viewModel.CurrentLyricIndexObservable;
            if (_isLandscape && _landscapeLyricsMode)
            {
                if (idx >= 0 && idx < _landscapeLyricLabels.Count)
                    _landscapeLyricLabels[idx].FillProgress = _viewModel.CurrentLineFillProgress;
            }
            else if (idx >= 0 && idx < _lyricLabels.Count)
                _lyricLabels[idx].FillProgress = _viewModel.CurrentLineFillProgress;
            return;
        }

        // 直接响应 ViewModel 的 Progress/Duration 变化，替代冗余的 500ms UI 定时器
        if (e.PropertyName == nameof(NowPlayingViewModel.Duration))
        {
            var duration = _viewModel.Duration;
            if (duration > 1 && ProgressSlider.Maximum != duration)
            {
                ProgressSlider.Maximum = duration;
                LandscapeProgressSlider.Maximum = duration;
            }
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.Progress) && !_isDragging)
        {
            var progress = _viewModel.Progress;
            var duration = _viewModel.Duration;
            if (duration > 1 && Math.Abs(ProgressSlider.Value - progress) > 0.5)
            {
                ProgressSlider.Value = progress;
                LandscapeProgressSlider.Value = progress;
            }
        }
    }

    private void BuildLyricViews()
    {
        LyricStack.Children.Clear();
        _lyricLabels.Clear();
        _lyricBorders.Clear();
        _lastHighlightIndex = -1;

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
            return;

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = 12,
                FontFamily = "OpenSansRegular",
                FontAttributes = FontAttributes.None,
                TextColor = Colors.White,
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 2,
                FillProgress = 0,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Fill,
                LineBreakMode = LineBreakMode.WordWrap,
                Padding = new Thickness(16, 4)
            };

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(18, 0),
                HorizontalOptions = LayoutOptions.Fill
            };
            border.Content = label;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.Fill };
                stack.Children.Add(border);

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = 11,
                    FontFamily = "OpenSansRegular",
                    FontAttributes = FontAttributes.None,
                    TextColor = Colors.White,
                    OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Fill,
                    LineBreakMode = LineBreakMode.WordWrap,
                    Padding = new Thickness(16, 4)
                };
                // 用与主歌词相同结构的 Border 包裹，确保翻译文本与主歌词居中对齐
                var transBorder = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                    StrokeThickness = 0,
                    BackgroundColor = Colors.Transparent,
                    Padding = new Thickness(18, 0),
                    HorizontalOptions = LayoutOptions.Fill
                };
                transBorder.Content = transLabel;
                stack.Children.Add(transBorder);
                LyricStack.Children.Add(stack);
            }
            else
            {
                LyricStack.Children.Add(border);
            }

            _lyricLabels.Add(label);
            _lyricBorders.Add(border);
        }

        if (_lyricLabels.Count > 0)
        {
            var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
            HighlightLineWithoutScroll(idx);
        }
    }

    private void HighlightLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            if (i == index)
            {
                // 当前行：实心填充，进度由 ViewModel 逐字计算（逐行模式为 1.0）
                lbl.FontSize = 18;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
            }
            else
            {
                // 非当前行：浅色实心，统一字号和不透明度
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                lbl.FontSize = 15;
                lbl.Opacity = 0.35;
            }
        }

        _lastHighlightIndex = index;
    }

    private void HighlightLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        var affectedMin = Math.Max(0, Math.Min(index, _lastHighlightIndex) - 4);
        var affectedMax = Math.Min(_lyricLabels.Count - 1, Math.Max(index, _lastHighlightIndex) + 4);

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            if (i == index)
            {
                lbl.FontSize = 18;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                // 非当前行：浅色实心，统一字号和不透明度
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                lbl.FontSize = 15;
                lbl.Opacity = 0.35;
            }
        }

        _lastHighlightIndex = index;

        ScrollToLine(index);
    }

    private async void ScrollToLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        try
        {
            var label = _lyricLabels[index];

#if ANDROID
            if (LyricCollectionView.Handler?.PlatformView is global::AndroidX.RecyclerView.Widget.RecyclerView recyclerView
                && label.Handler?.PlatformView is global::Android.Views.View nativeLabel)
            {
                int[] labelLocation = new int[2];
                nativeLabel.GetLocationOnScreen(labelLocation);
                int[] recyclerLocation = new int[2];
                recyclerView.GetLocationOnScreen(recyclerLocation);

                int labelCenterY = labelLocation[1] + nativeLabel.Height / 2;
                int targetY = recyclerLocation[1] + (int)(recyclerView.Height * 0.25);
                int dy = labelCenterY - targetY;

                if (Math.Abs(dy) > 2)
                {
                    recyclerView.SmoothScrollBy(0, dy);
                }
            }
            else if (LyricCollectionView.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                var y = GetRelativeY(label);
                var targetScrollY = y - LyricCollectionView.Height * 0.25;
                targetScrollY = Math.Max(0, targetScrollY);
                nativeView.ScrollY = (int)targetScrollY;
            }
#elif WINDOWS
            if (LyricCollectionView.Handler?.PlatformView is Microsoft.UI.Xaml.Controls.ListViewBase listView
                && label.Handler?.PlatformView is Microsoft.UI.Xaml.FrameworkElement nativeLabel)
            {
                var scrollViewer = FindScrollViewer(listView);
                if (scrollViewer != null && scrollViewer.Content is Microsoft.UI.Xaml.UIElement content)
                {
                    // 相对于 ScrollViewer 内容原点计算标签绝对位置（更精确，避免增量误差）
                    var transform = nativeLabel.TransformToVisual(content);
                    var point = transform.TransformPoint(new Windows.Foundation.Point(0, 0));
                    var labelCenter = point.Y + nativeLabel.ActualHeight / 2;
                    // 目标：让标签中心位于视口 25% 处
                    var targetOffset = labelCenter - scrollViewer.ViewportHeight * 0.25;
                    targetOffset = Math.Max(0, Math.Min(targetOffset, scrollViewer.ScrollableHeight));
                    // 仅当偏差较大时才滚动，避免逐字更新时频繁打断动画
                    if (Math.Abs(scrollViewer.VerticalOffset - targetOffset) > 4)
                    {
                        scrollViewer.ChangeView(null, targetOffset, null, disableAnimation: false);
                    }
                }
            }
#else
            var y = GetRelativeY(label);
            var targetScrollY = y - LyricCollectionView.Height * 0.25;
            targetScrollY = Math.Max(0, targetScrollY);
            if (LyricCollectionView.ItemsSource is System.Collections.IEnumerable items && items.Cast<object>().Any())
            {
                LyricCollectionView.ScrollTo(items.Cast<object>().First(), position: ScrollToPosition.Start, animate: true);
            }
#endif
        }
        catch { }
    }

    /// <summary>获取元素相对于 LyricStack 的 Y 坐标（累加所有父容器的 Y）</summary>
    private double GetRelativeY(VisualElement element)
    {
        double y = element.Y + element.Height / 2;
        var parent = element.Parent as VisualElement;
        while (parent != null && parent != LyricStack)
        {
            y += parent.Y;
            parent = parent.Parent as VisualElement;
        }
        return y;
    }

#if WINDOWS
    /// <summary>在 WinUI 可视树中查找 ScrollViewer（用于 CollectionView 手动定位歌词行）</summary>
    private static Microsoft.UI.Xaml.Controls.ScrollViewer? FindScrollViewer(Microsoft.UI.Xaml.DependencyObject obj)
    {
        for (int i = 0; i < Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(obj); i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(obj, i);
            if (child is Microsoft.UI.Xaml.Controls.ScrollViewer sv)
                return sv;
            var result = FindScrollViewer(child);
            if (result != null)
                return result;
        }
        return null;
    }
#endif

    /// <summary>当用户开始拖动进度条时触发，标记拖动状态并通知视图模型开始定位。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        _isDragging = true;
        _viewModel.OnSeekStarted();
    }

    /// <summary>当用户结束拖动进度条时触发，通知视图模型定位到拖动结束位置。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        _isDragging = false;
        // 读取触发拖拽的滑块自身的值（竖屏/横屏共用同一处理程序）
        var slider = sender as Slider ?? ProgressSlider;
        await _viewModel.OnSeekCompleted(slider.Value);
    }

    private static string FormatTime(double seconds)
    {
        if (seconds < 0) seconds = 0;
        var ts = TimeSpan.FromSeconds(seconds);
        return $"{(int)ts.TotalMinutes}:{ts.Seconds:D2}";
    }

    /// <summary>点击页面空白区域时触发，若点击位于封面、歌词或无歌词提示区域则切换到全屏歌词页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">点击事件参数。</param>
    private void OnPageTapped(object? sender, TappedEventArgs e)
    {
        var ptCover = e.GetPosition(CoverArea);
        if (ptCover.HasValue && ptCover.Value.X >= -10 && ptCover.Value.X <= CoverArea.Width + 10
            && ptCover.Value.Y >= -10 && ptCover.Value.Y <= CoverArea.Height + 10)
        {
            GoToFullLyrics();
            return;
        }

        if (LyricsContainer.IsVisible)
        {
            var pt = e.GetPosition(LyricsContainer);
            if (pt.HasValue && pt.Value.X >= 0 && pt.Value.X <= LyricsContainer.Width
                && pt.Value.Y >= 0 && pt.Value.Y <= LyricsContainer.Height)
            {
                GoToFullLyrics();
                return;
            }
        }

        if (NoLyricsLabel.IsVisible)
        {
            var pt = e.GetPosition(NoLyricsLabel);
            if (pt.HasValue && pt.Value.X >= -20 && pt.Value.X <= NoLyricsLabel.Width + 20
                && pt.Value.Y >= -10 && pt.Value.Y <= NoLyricsLabel.Height + 10)
            {
                GoToFullLyrics();
            }
        }
    }

    /// <summary>跳转到全屏歌词页：移动端走 ViewPager 切换，桌面端走 Shell 路由</summary>
    private static void GoToFullLyrics()
    {
#if WINDOWS
        _ = Shell.Current.GoToAsync("//fullyrics");
#else
        MainPage.Instance?.SwitchToFullLyrics();
#endif
    }

    /// <summary>点击左上角收起按钮：播放页向下平移收起，露出发现页</summary>
    private void OnCollapseButtonTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
            _ = Shell.Current.Navigation.PopAsync();
        else
            _ = Shell.Current.GoToAsync("//main");
#else
        MainPage.Instance?.CollapseNowPlaying();
#endif
    }

    /// <summary>点击歌词按钮：横屏下就地切换歌词模式（收起右侧按钮显示多行歌词），竖屏下打开全屏歌词页</summary>
    private void OnOpenLyricsClicked(object? sender, EventArgs e)
    {
        if (_isLandscape)
            ToggleLandscapeLyricsMode();
        else
            GoToFullLyrics();
    }

    // ═══════════════════════════════════════
    // 横屏歌词模式（就地显示多行歌词，不跳独立页面）
    // ═══════════════════════════════════════

    /// <summary>切换横屏歌词模式：开 → 收起信息区、显示多行歌词、封面加大；关 → 恢复信息区</summary>
    private void ToggleLandscapeLyricsMode()
    {
        _landscapeLyricsMode = !_landscapeLyricsMode;
        ApplyLandscapeLyricsMode();
        if (_landscapeLyricsMode)
        {
            BuildLandscapeLyricViews();
            var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
            HighlightLandscapeLineWithoutScroll(idx);
            _ = Task.Delay(100).ContinueWith(_ => MainThread.BeginInvokeOnMainThread(() => HighlightLandscapeLine(idx)));
        }
    }

    /// <summary>应用横屏歌词模式的可见性与封面尺寸</summary>
    private void ApplyLandscapeLyricsMode()
    {
        LandscapeTitleBlock.IsVisible = !_landscapeLyricsMode;
        LandscapeToolsRow.IsVisible = !_landscapeLyricsMode;
        LandscapeCurrentLyric.IsVisible = !_landscapeLyricsMode;
        LandscapeLyricsScroll.IsVisible = _landscapeLyricsMode;
        // 歌词模式下隐藏进度条与播放控件，呈现纯净歌词页；再次点击歌词恢复横屏模式
        LandscapeProgressRow.IsVisible = !_landscapeLyricsMode;
        LandscapeControlsRow.IsVisible = !_landscapeLyricsMode;
        // 重算封面尺寸（歌词模式封面更大，能多大就多大）
        ApplyLayoutForOrientation(this.Width, this.Height);
    }

    /// <summary>构建横屏多行歌词视图（目标为 LandscapeLyricStack）</summary>
    private void BuildLandscapeLyricViews()
    {
        LandscapeLyricStack.Children.Clear();
        _landscapeLyricLabels.Clear();
        _landscapeLyricBorders.Clear();
        _landscapeLastHighlight = -1;

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
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            LandscapeLyricStack.Children.Add(label);
            return;
        }

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = 16,
                FontFamily = "OpenSansRegular",
                FontAttributes = FontAttributes.None,
                TextColor = Colors.White,
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 2,
                FillProgress = 0,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Fill,
                LineBreakMode = LineBreakMode.WordWrap,
                Opacity = 0.2,
                Padding = new Thickness(16, 6)
            };

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(22, 0),
                HorizontalOptions = LayoutOptions.Fill
            };
            border.Content = label;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 4, HorizontalOptions = LayoutOptions.Fill };
                stack.Children.Add(border);

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = 14,
                    FontFamily = "OpenSansRegular",
                    FontAttributes = FontAttributes.None,
                    TextColor = Colors.White,
                    OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Fill,
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
                stack.Children.Add(transBorder);
                LandscapeLyricStack.Children.Add(stack);
            }
            else
            {
                LandscapeLyricStack.Children.Add(border);
            }

            _landscapeLyricLabels.Add(label);
            _landscapeLyricBorders.Add(border);
        }

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        HighlightLandscapeLineWithoutScroll(idx);
    }

    private void HighlightLandscapeLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _landscapeLyricLabels.Count) return;

        for (int i = 0; i < _landscapeLyricLabels.Count; i++)
        {
            var lbl = _landscapeLyricLabels[i];

            if (i == index)
            {
                lbl.FontSize = 19;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                lbl.FontSize = 16;
                lbl.Opacity = 0.35;
            }
        }

        _landscapeLastHighlight = index;
    }

    private void HighlightLandscapeLine(int index)
    {
        if (index < 0 || index >= _landscapeLyricLabels.Count) return;

        var affectedMin = Math.Max(0, Math.Min(index, _landscapeLastHighlight) - 5);
        var affectedMax = Math.Min(_landscapeLyricLabels.Count - 1, Math.Max(index, _landscapeLastHighlight) + 5);

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = _landscapeLyricLabels[i];

            if (i == index)
            {
                lbl.FontSize = 19;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                lbl.FontSize = 16;
                lbl.Opacity = 0.35;
            }
        }

        _landscapeLastHighlight = index;

        ScrollToLandscapeLine(index);
    }

    private void ScrollToLandscapeLine(int index)
    {
        if (index < 0 || index >= _landscapeLyricLabels.Count) return;

        try
        {
            var label = _landscapeLyricLabels[index];
            var y = GetRelativeYLandscape(label);
            var targetScrollY = Math.Max(0, y - LandscapeLyricsScroll.Height * 0.3);
            _ = LandscapeLyricsScroll.ScrollToAsync(0, targetScrollY, false);
        }
        catch { }
    }

    private double GetRelativeYLandscape(VisualElement element)
    {
        double y = element.Y + element.Height / 2;
        var parent = element.Parent as VisualElement;
        while (parent != null && parent != LandscapeLyricStack)
        {
            y += parent.Y;
            parent = parent.Parent as VisualElement;
        }
        return y;
    }

    /// <summary>点击歌曲详情入口：跳转到歌曲详情页</summary>
    private void OnSongDetailTapped(object? sender, EventArgs e)
    {
        var song = _viewModel.CurrentSong;
        if (song == null || song.Id <= 0) return;

        _ = Shell.Current.GoToAsync($"songdetail?songId={song.Id}");
    }
    /// <summary>点击播放列表按钮：弹出播放队列弹窗</summary>
    private void OnOpenPlaylistClicked(object? sender, EventArgs e)
    {
        BuildPlaylistPopupContent();
        PlaylistPopup.Open();

        // 延迟滚动到当前歌曲
        _ = Task.Delay(300).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() =>
            {
                try
                {
                    var currentSong = _viewModel.CurrentSong;
                    if (currentSong != null && _playlistCollectionView != null)
                    {
                        var songs = _viewModel.GetQueueSongs();
                        var idx = songs.ToList().FindIndex(s => s.Id == currentSong.Id);
                        if (idx >= 0)
                            _playlistCollectionView.ScrollTo(idx, position: ScrollToPosition.Center, animate: false);
                    }
                }
                catch { }
            }));
    }

    private CollectionView? _playlistCollectionView;

    /// <summary>构建播放列表弹窗内容：歌曲列表 + 每项可点击播放/滑动删除</summary>
    private void BuildPlaylistPopupContent()
    {
        PlaylistPopup.ClearContent();

        var songs = _viewModel.GetQueueSongs();
        var currentSong = _viewModel.CurrentSong;
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];

        // 歌曲数量标签
        var countLabel = new Label
        {
            Text = $"{songs.Count} 首歌曲",
            FontSize = 13,
            TextColor = textHint,
            Margin = new Thickness(0, 0, 0, 12)
        };
        PlaylistPopup.AddContent(countLabel);

        // 歌曲列表 CollectionView（高度限制 400，可滚动）
        _playlistCollectionView = new CollectionView
        {
            SelectionMode = SelectionMode.None,
            HeightRequest = Math.Min(songs.Count * 56, 400),
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            ItemsSource = songs.ToList(),
            ItemTemplate = new DataTemplate(() =>
            {
                var grid = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitionCollection
                    {
                        new() { Width = GridLength.Auto },       // 播放指示器
                        new() { Width = new GridLength(1, GridUnitType.Star) }, // 歌曲信息
                        new() { Width = GridLength.Auto }         // 删除按钮
                    },
                    HeightRequest = 52,
                    Padding = new Thickness(0, 4),
                    ColumnSpacing = 10
                };

                // 播放指示器（当前歌曲显示小图标）
                var indicator = new Image
                {
                    WidthRequest = 16,
                    HeightRequest = 16,
                    Aspect = Aspect.AspectFit,
                    Source = ImageSourceHelper.FromNameOriginal("ic_play_dark"),
                    IsVisible = false,
                    VerticalOptions = LayoutOptions.Center
                };
                grid.Add(indicator, 0);

                // 歌曲信息
                var infoStack = new VerticalStackLayout
                {
                    Spacing = 2,
                    VerticalOptions = LayoutOptions.Center
                };
                var titleLabel = new Label
                {
                    FontSize = 14,
                    FontFamily = "OpenSansSemibold",
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation,
                    VerticalOptions = LayoutOptions.Center
                };
                titleLabel.SetBinding(Label.TextProperty, "Title");
                var artistLabel = new Label
                {
                    FontSize = 12,
                    TextColor = textSecondary,
                    MaxLines = 1,
                    LineBreakMode = LineBreakMode.TailTruncation
                };
                artistLabel.SetBinding(Label.TextProperty, "Artist");
                infoStack.Children.Add(titleLabel);
                infoStack.Children.Add(artistLabel);
                grid.Add(infoStack, 1);

                // 删除按钮
                var removeBtn = new ImageButton
                {
                    WidthRequest = 32,
                    HeightRequest = 32,
                    CornerRadius = 16,
                    Padding = 6,
                    Aspect = Aspect.AspectFit,
                    BackgroundColor = Colors.Transparent,
                    Source = ImageSourceHelper.FromNameOriginal("ic_close"),
                    VerticalOptions = LayoutOptions.Center
                };
                grid.Add(removeBtn, 2);

                // 绑定上下文加载后设置当前歌曲高亮
                grid.BindingContextChanged += (s, _) =>
                {
                    if (s is Grid g && g.BindingContext is Song song)
                    {
                        var isCurrent = currentSong != null && song.Id == currentSong.Id;
                        titleLabel.TextColor = isCurrent ? primaryColor : textPrimary;
                        indicator.IsVisible = isCurrent;
                        if (isCurrent)
                            titleLabel.FontAttributes = FontAttributes.Bold;
                    }
                };

                // 点击播放
                var tapGesture = new TapGestureRecognizer();
                tapGesture.Tapped += (_, _) =>
                {
                    if (grid.BindingContext is Song song)
                    {
                        PlaylistPopup.Close();
                        _ = _viewModel.PlaySongFromQueueCommand.ExecuteAsync(song);
                    }
                };
                grid.GestureRecognizers.Add(tapGesture);

                // 删除按钮点击
                removeBtn.Clicked += (_, _) =>
                {
                    if (grid.BindingContext is Song song)
                    {
                        _ = _viewModel.RemoveSongFromQueueCommand.ExecuteAsync(song);
                        // 刷新列表
                        BuildPlaylistPopupContent();
                    }
                };

                return grid;
            })
        };
        _playlistCollectionView.Behaviors.Add(new Controls.ScrollPerformanceBehavior());
        PlaylistPopup.AddContent(_playlistCollectionView);
    }

    // ─── 窗口控制按钮 ──

    private void OnMinimizeTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        if (App.CurrentAppWindow == null) return;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);
        _ = ShowWindow(hwnd, SW_MINIMIZE);
#endif
    }

    private void OnMaximizeTapped(object? sender, TappedEventArgs e)
    {
        ToggleMaximize();
    }

    private void OnCloseTapped(object? sender, TappedEventArgs e)
    {
#if WINDOWS
        if (App.CurrentAppWindow == null) return;
        var hwnd = Microsoft.UI.Win32Interop.GetWindowFromWindowId(App.CurrentAppWindow.Id);
        _ = PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
#endif
    }

    private void ToggleMaximize()
    {
#if WINDOWS
        if (App.CurrentAppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            if (presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized)
                presenter.Restore();
            else
                presenter.Maximize();

            UpdateMaximizeIcon();
        }
#endif
    }

    private void UpdateMaximizeIcon()
    {
#if WINDOWS
        if (App.CurrentAppWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
            return;

        var isMaximized = presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
        MaximizeIcon.IsVisible = !isMaximized;
        RestoreIcon.IsVisible = isMaximized;
#endif
    }

#if WINDOWS
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const int SW_MINIMIZE = 6;
    private const uint WM_CLOSE = 0x0010;
#endif
}

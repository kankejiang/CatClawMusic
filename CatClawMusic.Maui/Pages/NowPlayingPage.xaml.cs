using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>正在播放页面，展示当前播放歌曲的封面、进度、歌词及播放控制。</summary>
public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private bool _isDragging;
    private readonly List<KaraokeLabel> _lyricLabels = new();
    private readonly List<Border> _lyricBorders = new();
    private int _lastHighlightIndex = -1;
    private bool _isLandscape;
    private int _lastCoverSize;

    /// <summary>初始化 <see cref="NowPlayingPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">当前播放视图模型，提供歌曲、进度与歌词数据。</param>
    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
        Loaded += OnPageLoaded;

        // 设置收起图标（使用 ImageSourceHelper 确保 Windows 端正确加载）
        CollapseIcon.Source = ImageSourceHelper.FromNameOriginal("ic_collapse");
    }

    private void OnPageLoaded(object? sender, EventArgs e)
    {
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
        // 封面尺寸：横屏根据高度计算（避免超出窗口），竖屏根据较短边计算
        var coverSize = isLandscape
            ? Math.Clamp((int)(height * 0.6), 260, 480)
            : Math.Clamp((int)(Math.Min(width, height) * 0.55), 240, 360);

        if (_isLandscape == isLandscape && _lastCoverSize == coverSize)
            return;

        var orientationChanged = _isLandscape != isLandscape;
        _isLandscape = isLandscape;

        if (orientationChanged)
        {
            if (isLandscape)
            {
                // 横屏：封面左、歌词右
                Grid.SetRow(LeftHalf, 0);
                Grid.SetColumn(LeftHalf, 0);
                Grid.SetRowSpan(LeftHalf, 2);
                Grid.SetColumnSpan(LeftHalf, 1);
                LeftHalf.RowSpacing = 22;

                Grid.SetRow(RightHalf, 0);
                Grid.SetColumn(RightHalf, 2);
                Grid.SetRowSpan(RightHalf, 2);
                Grid.SetColumnSpan(RightHalf, 1);

                PhoneControls.IsVisible = false;
                DesktopControls.IsVisible = true;
            }
            else
            {
                // 竖屏：封面上、歌词下
                Grid.SetRow(LeftHalf, 0);
                Grid.SetColumn(LeftHalf, 0);
                Grid.SetRowSpan(LeftHalf, 1);
                Grid.SetColumnSpan(LeftHalf, 3);
                LeftHalf.RowSpacing = 16;

                Grid.SetRow(RightHalf, 1);
                Grid.SetColumn(RightHalf, 0);
                Grid.SetRowSpan(RightHalf, 1);
                Grid.SetColumnSpan(RightHalf, 3);

                PhoneControls.IsVisible = true;
                DesktopControls.IsVisible = false;
            }
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
        }
    }

    /// <summary>当页面显示在屏幕上时触发，加载当前歌曲、构建歌词视图并启动进度定时器。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
#if WINDOWS
        Shell.SetNavBarIsVisible(this, false);
#endif
        ApplySafeArea();
        await _viewModel.LoadCurrentSongAsync();

        if (_viewModel.Duration > 0)
            ProgressSlider.Maximum = _viewModel.Duration;

        Application.Current!.RequestedThemeChanged += OnThemeChanged;

        // 仅在歌词行数变化时重建视图，避免切页时大量控件销毁/重建
        var allLines = _viewModel.AllLyricLines;
        if (allLines == null || _lyricLabels.Count != allLines.Count)
            BuildLyricViews();
        else if (_viewModel.CurrentLyricIndexObservable >= 0 && _lyricLabels.Count > 0)
            HighlightLine(_viewModel.CurrentLyricIndexObservable);

        // 延迟滚动到当前歌词行，确保布局完成后再定位
        if (_lyricLabels.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
            _ = Task.Delay(100).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                    HighlightLine(_viewModel.CurrentLyricIndexObservable)));
        }
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
            MainThread.BeginInvokeOnMainThread(BuildLyricViews);
            return;
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLyricIndexObservable))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                HighlightLine(_viewModel.CurrentLyricIndexObservable);
            });
            return;
        }

        // 逐字填充进度变化：直接更新当前行 KaraokeLabel 的 FillProgress
        // PropertyChanged 已在主线程触发，无需额外 dispatch
        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLineFillProgress))
        {
            var idx = _viewModel.CurrentLyricIndexObservable;
            if (idx >= 0 && idx < _lyricLabels.Count)
                _lyricLabels[idx].FillProgress = _viewModel.CurrentLineFillProgress;
            return;
        }

        // 直接响应 ViewModel 的 Progress/Duration 变化，替代冗余的 500ms UI 定时器
        if (e.PropertyName == nameof(NowPlayingViewModel.Duration))
        {
            var duration = _viewModel.Duration;
            if (duration > 1 && ProgressSlider.Maximum != duration)
                ProgressSlider.Maximum = duration;
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.Progress) && !_isDragging)
        {
            var progress = _viewModel.Progress;
            var duration = _viewModel.Duration;
            if (duration > 1 && Math.Abs(ProgressSlider.Value - progress) > 0.5)
                ProgressSlider.Value = progress;
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
        await _viewModel.OnSeekCompleted(ProgressSlider.Value);
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

    /// <summary>点击歌词按钮：打开全屏歌词页</summary>
    private void OnOpenLyricsClicked(object? sender, EventArgs e)
    {
        GoToFullLyrics();
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

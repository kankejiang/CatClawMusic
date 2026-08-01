#if WINDOWS
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using Microsoft.Maui.Controls.Shapes;
using DispatcherTimer = Microsoft.UI.Xaml.DispatcherTimer;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// NowPlayingPage 的 Windows 桌面播放布局逻辑（基于 Aurora 原型）。
/// 仅编译进 Windows 目标，安卓端完全不受影响。
/// 负责亚克力标题栏 EQ 动画、3D 封面、歌词遮罩淡入与跟随、
/// 底部三栏控制坞（播放/进度/音量）等桌面专属交互。
/// </summary>
public partial class NowPlayingPage
{
    // Windows 歌词视图
    private readonly List<KaraokeLabel> _winLyricLabels = new();
    private readonly List<Border> _winLyricBorders = new();
    private int _winLastHighlight = -1;
    private bool _winFollow = true;

    // Windows 封面尺寸缓存
    private int _winCoverSize;

    // Windows 播放状态（用于 EQ 动画与播放图标切换）
    private bool _winIsPlaying;

    // 音量与静音
    private double _winLastVolume = 1.0;
    private bool _winIsMuted;

    // EQ 频谱动画定时器（WinUI DispatcherTimer，已在 FrostedBackgroundHandler 中验证可用）
    private DispatcherTimer? _winEqTimer;
    private double _winEqTime;

    // ═══════════════════════════════════════
    // 布局接入
    // ═══════════════════════════════════════

    /// <summary>Windows 端布局：显示 WindowsStage，隐藏竖屏与横屏布局，并按窗口尺寸计算封面大小。</summary>
    private void ApplyWindowsLayout(double width, double height)
    {
        if (width <= 0 || height <= 0) return;

        // 首次进入：切换可见性
        if (!WindowsStage.IsVisible)
        {
            WindowsStage.IsVisible = true;
            MainContent.IsVisible = false;
            BottomControlsRoot.IsVisible = false;
            TopNavBar.IsVisible = false;
            PhoneControls.IsVisible = false;
            DesktopControls.IsVisible = false;
            BottomActionBar.IsVisible = false;
            LandscapeRoot.IsVisible = false;
            ApplyWindowsSafeArea();
        }

        // 封面尺寸：左栏宽 ≈ 0.42 * 舞台宽；可用高 = 窗口高 - 标题栏48 - 控制坞92 - 上下留白
        double stageWidth = width;
        double leftColWidth = stageWidth * 0.42;
        double availableHeight = height - 48 - 92 - 48;
        int coverSize = Math.Clamp((int)Math.Min(availableHeight, leftColWidth - 96), 240, 520);

        if (_winCoverSize != coverSize)
        {
            _winCoverSize = coverSize;
            WinCover.WidthRequest = coverSize;
            WinCover.HeightRequest = coverSize;
            WinCoverImage.WidthRequest = coverSize;
            WinCoverImage.HeightRequest = coverSize;
        }
    }

    /// <summary>Windows 安全区：标题栏顶部留出状态栏空间。</summary>
    private void ApplyWindowsSafeArea()
    {
        var topInset = SafeAreaHelper.TopInset;
        WindowsStage.Padding = new Thickness(0, topInset, 0, 0);
    }

    /// <summary>WindowsStage 就绪初始化：构建歌词、同步滑块、初始化音量与图标状态。</summary>
    private void OnWindowsStageReady()
    {
        ApplyWindowsSafeArea();

        // 播放图标
        _winIsPlaying = _viewModel.IsPlaying;
        UpdateWinPlayIcon();

        // 收藏图标
        UpdateWinLikeIcon();

        // 进度滑块
        if (_viewModel.Duration > 0)
            WinProgressSlider.Maximum = _viewModel.Duration;
        if (_viewModel.Progress > 0)
            WinProgressSlider.Value = _viewModel.Progress;

        // 音量：从音频服务读取当前值
        try
        {
            var v = _audioPlayer.Volume;
            _winLastVolume = v > 0 ? v : 1.0;
            _winIsMuted = v <= 0;
            WinVolumeSlider.Value = v;
            UpdateWinMuteIcon();
        }
        catch { }

        // 歌词
        BuildWindowsLyricViews();
        if (_winLyricLabels.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
            _ = Task.Delay(120).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                    HighlightWindowsLine(_viewModel.CurrentLyricIndexObservable)));
        }

        // EQ 动画
        UpdateWinEqAnimation();
    }

    // ═══════════════════════════════════════
    // 歌词
    // ═══════════════════════════════════════

    /// <summary>构建 Windows 歌词视图到 WinLyricStack。</summary>
    private void BuildWindowsLyricViews()
    {
        WinLyricStack.Children.Clear();
        _winLyricLabels.Clear();
        _winLyricBorders.Clear();
        _winLastHighlight = -1;

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
        {
            WinNoLyricsLabel.IsVisible = true;
            return;
        }
        WinNoLyricsLabel.IsVisible = false;

        var currentSize = _settings.FontSize;
        var inactiveSize = currentSize * 0.8;

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = inactiveSize,
                FontFamily = "OpenSansRegular",
                FontAttributes = FontAttributes.None,
                TextColor = Colors.White,
                OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                StrokeWidth = 2,
                FillProgress = 0,
                HorizontalTextAlignment = TextAlignment.Start,
                HorizontalOptions = LayoutOptions.Start,
                LineBreakMode = LineBreakMode.WordWrap,
                Opacity = 0.32,
                Padding = new Thickness(0, 4)
            };

            var border = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                StrokeThickness = 0,
                BackgroundColor = Colors.Transparent,
                Padding = new Thickness(0, 0),
                HorizontalOptions = LayoutOptions.Start
            };
            border.Content = label;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 3, HorizontalOptions = LayoutOptions.Start };
                stack.Children.Add(border);

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = inactiveSize - 2,
                    FontFamily = "OpenSansRegular",
                    FontAttributes = FontAttributes.None,
                    TextColor = Colors.White,
                    OutlineColor = Color.FromRgba(1f, 1f, 1f, 0.5f),
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = TextAlignment.Start,
                    HorizontalOptions = LayoutOptions.Start,
                    LineBreakMode = LineBreakMode.WordWrap,
                    Opacity = 0.32,
                    Padding = new Thickness(0, 2)
                };
                var transBorder = new Border
                {
                    StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                    StrokeThickness = 0,
                    BackgroundColor = Colors.Transparent,
                    Padding = new Thickness(0, 0),
                    HorizontalOptions = LayoutOptions.Start
                };
                transBorder.Content = transLabel;
                stack.Children.Add(transBorder);
                WinLyricStack.Children.Add(stack);
            }
            else
            {
                WinLyricStack.Children.Add(border);
            }

            _winLyricLabels.Add(label);
            _winLyricBorders.Add(border);
        }

        if (_winLyricLabels.Count > 0)
        {
            var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
            HighlightWindowsLineWithoutScroll(idx);
        }
    }

    private void HighlightWindowsLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _winLyricLabels.Count) return;

        var currentSize = _settings.FontSize;
        var inactiveSize = currentSize * 0.8;

        for (int i = 0; i < _winLyricLabels.Count; i++)
        {
            var lbl = _winLyricLabels[i];
            if (i == index)
            {
                lbl.FontSize = currentSize;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FillProgress = 0;
                lbl.FontSize = inactiveSize;
                lbl.Opacity = 0.32;
            }
        }
        _winLastHighlight = index;
    }

    private void HighlightWindowsLine(int index)
    {
        if (index < 0 || index >= _winLyricLabels.Count) return;

        var currentSize = _settings.FontSize;
        var inactiveSize = currentSize * 0.8;

        var affectedMin = Math.Max(0, Math.Min(index, _winLastHighlight) - 4);
        var affectedMax = Math.Min(_winLyricLabels.Count - 1, Math.Max(index, _winLastHighlight) + 4);

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = _winLyricLabels[i];
            if (i == index)
            {
                lbl.FontSize = currentSize;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FillProgress = 0;
                lbl.FontSize = inactiveSize;
                lbl.Opacity = 0.32;
            }
        }
        _winLastHighlight = index;
        ScrollToWindowsLine(index);
    }

    private async void ScrollToWindowsLine(int index)
    {
        if (index < 0 || index >= _winLyricLabels.Count) return;
        try
        {
            var label = _winLyricLabels[index];
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (label.Height > 0)
                {
                    var targetY = label.Y - WinLyricsScroll.Height * 0.32;
                    if (Math.Abs(WinLyricsScroll.ScrollY - targetY) > 4)
                        await WinLyricsScroll.ScrollToAsync(0, Math.Max(0, targetY), true);
                    return;
                }
                await Task.Delay(120);
            }
        }
        catch { }
    }

    // ═══════════════════════════════════════
    // 播放控制
    // ═══════════════════════════════════════

    private void OnWinPlayTapped(object? sender, TappedEventArgs e)
    {
        _viewModel.TogglePlayPauseCommand.Execute(null);
    }

    /// <summary>刷新播放按钮图标（白色圆按钮 + 深色图标）与 EQ 动画状态。</summary>
    private void UpdateWinPlayIcon()
    {
        WinPlayIcon.Source = _winIsPlaying
            ? ImageSourceHelper.FromNameOriginal("ic_pause_light")
            : ImageSourceHelper.FromNameOriginal("ic_play_light");
    }

    /// <summary>播放状态变化：更新图标与 EQ 动画。</summary>
    private void OnWinPlayingChanged()
    {
        _winIsPlaying = _viewModel.IsPlaying;
        UpdateWinPlayIcon();
        UpdateWinEqAnimation();
    }

    // ═══════════════════════════════════════
    // 收藏
    // ═══════════════════════════════════════

    private void OnWinLikeTapped(object? sender, TappedEventArgs e)
    {
        _viewModel.ToggleLikeCommand.Execute(null);
    }

    /// <summary>刷新收藏 chip 图标与文字（已收藏时显示爱心 + 主题色）。</summary>
    private void UpdateWinLikeIcon()
    {
        var liked = _viewModel.IsLiked;
        WinLikeIcon.Source = liked
            ? ImageSourceHelper.FromNameOriginal("ic_favorite_white")
            : ImageSourceHelper.FromNameOriginal("ic_favorite_border_white");
        WinLikeLabel.Text = liked ? "已收藏" : "收藏";
        WinLikeLabel.TextColor = liked
            ? (Color)Application.Current!.Resources["LikeColor"]
            : Colors.White;
    }

    // ═══════════════════════════════════════
    // 跟随开关
    // ═══════════════════════════════════════

    private void OnWinFollowTapped(object? sender, TappedEventArgs e)
    {
        _winFollow = !_winFollow;
        WinFollowLabel.Text = _winFollow ? "跟随" : "手动";
        WinFollowBtn.BackgroundColor = _winFollow
            ? (Color)Application.Current!.Resources["PrimaryColor"]
            : Color.FromArgb("#10FFFFFF");
        if (_winFollow && _winLyricLabels.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
            HighlightWindowsLine(_viewModel.CurrentLyricIndexObservable);
    }

    // ═══════════════════════════════════════
    // 音量与静音
    // ═══════════════════════════════════════

    private void OnWinVolumeChanged(object? sender, ValueChangedEventArgs e)
    {
        var v = Math.Clamp(e.NewValue, 0.0, 1.0);
        try
        {
            _audioPlayer.Volume = v;
        }
        catch { }
        _winIsMuted = v <= 0;
        if (v > 0) _winLastVolume = v;
        UpdateWinMuteIcon();
    }

    private void OnWinMuteTapped(object? sender, EventArgs e)
    {
        if (_winIsMuted)
        {
            var restore = _winLastVolume > 0 ? _winLastVolume : 0.7;
            WinVolumeSlider.Value = restore;
        }
        else
        {
            _winLastVolume = WinVolumeSlider.Value;
            WinVolumeSlider.Value = 0;
        }
    }

    private void UpdateWinMuteIcon()
    {
        WinMuteBtn.Source = _winIsMuted ? "ic_volume_mute" : "ic_volume";
        WinMuteBtn.Opacity = _winIsMuted ? 0.5 : 1.0;
    }

    // ═══════════════════════════════════════
    // EQ 频谱动画
    // ═══════════════════════════════════════

    private void UpdateWinEqAnimation()
    {
        if (_winIsPlaying)
            StartWinEq();
        else
            StopWinEq();
    }

    private void StartWinEq()
    {
        if (_winEqTimer != null) return;
        _winEqTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
        _winEqTimer.Tick += OnWinEqTick;
        _winEqTimer.Start();
    }

    private void StopWinEq()
    {
        if (_winEqTimer == null) return;
        _winEqTimer.Stop();
        _winEqTimer.Tick -= OnWinEqTick;
        _winEqTimer = null;
        // 复位为低条
        WinEq1.HeightRequest = 4;
        WinEq2.HeightRequest = 4;
        WinEq3.HeightRequest = 4;
        WinEq4.HeightRequest = 4;
    }

    private void OnWinEqTick(object? sender, object e)
    {
        _winEqTime += 0.08;
        WinEq1.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 3.1));
        WinEq2.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 4.3 + 1.0));
        WinEq3.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 3.7 + 2.0));
        WinEq4.HeightRequest = 3 + 9 * (0.5 + 0.5 * Math.Sin(_winEqTime * 5.0 + 0.5));
    }

    // ═══════════════════════════════════════
    // 最大化图标
    // ═══════════════════════════════════════

    private void UpdateWinMaximizeIcon()
    {
        if (App.CurrentAppWindow?.Presenter is not Microsoft.UI.Windowing.OverlappedPresenter presenter)
            return;
        var isMaximized = presenter.State == Microsoft.UI.Windowing.OverlappedPresenterState.Maximized;
        WinMaximizeIcon.IsVisible = !isMaximized;
        WinRestoreIcon.IsVisible = isMaximized;
    }
}
#endif

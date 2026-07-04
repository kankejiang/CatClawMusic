using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

/// <summary>正在播放页面，展示当前播放歌曲的封面、进度、歌词及播放控制。</summary>
public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private IDispatcherTimer? _progressTimer;
    private bool _isDragging;
    private readonly List<Label> _lyricLabels = new();
    private int _lastHighlightIndex = -1;

    /// <summary>初始化 <see cref="NowPlayingPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">当前播放视图模型，提供歌曲、进度与歌词数据。</param>
    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        _progressTimer = Application.Current!.Dispatcher.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _progressTimer.Tick += OnProgressTimerTick;
    }

    /// <summary>当页面显示在屏幕上时触发，加载当前歌曲、构建歌词视图并启动进度定时器。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCurrentSongAsync();

        if (_viewModel.Duration > 0)
            ProgressSlider.Maximum = _viewModel.Duration;

        Application.Current!.RequestedThemeChanged += OnThemeChanged;

        BuildLyricViews();

        // 延迟滚动到当前歌词行，确保布局完成后再定位
        if (_lyricLabels.Count > 0 && _viewModel.CurrentLyricIndexObservable >= 0)
        {
            _ = Task.Delay(100).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                    HighlightLine(_viewModel.CurrentLyricIndexObservable)));
        }

        if (!_progressTimer.IsRunning)
            _progressTimer.Start();
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
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLyricIndexObservable))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                HighlightLine(_viewModel.CurrentLyricIndexObservable);
            });
        }
    }

    private void BuildLyricViews()
    {
        LyricStack.Children.Clear();
        _lyricLabels.Clear();
        _lastHighlightIndex = -1;

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
            return;

        var hintColor = (Color)Application.Current!.Resources["TextHintColor"];

        foreach (var line in lines)
        {
            var label = new Label
            {
                Text = line.Text,
                FontSize = 14,
                TextColor = hintColor,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                FontAttributes = FontAttributes.None,
                Padding = new Thickness(16, 2)
            };

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 1, HorizontalOptions = LayoutOptions.Center };
                stack.Children.Add(label);

                var transLabel = new Label
                {
                    Text = line.Translation,
                    FontSize = 11,
                    TextColor = hintColor.WithAlpha(0.6f),
                    HorizontalTextAlignment = TextAlignment.Center,
                    HorizontalOptions = LayoutOptions.Center
                };
                stack.Children.Add(transLabel);
                LyricStack.Children.Add(stack);
            }
            else
            {
                LyricStack.Children.Add(label);
            }

            _lyricLabels.Add(label);
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

        var hintColor = (Color)Application.Current!.Resources["TextHintColor"];
        var secondaryColor = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];

        var current = _lyricLabels[index];
        current.FontSize = 16;
        current.FontAttributes = FontAttributes.Bold;
        current.TextColor = primaryColor;

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            if (i == index) continue;
            var dist = Math.Abs(i - index);
            var lbl = _lyricLabels[i];
            if (dist == 1)
            {
                lbl.FontSize = 14;
                lbl.TextColor = secondaryColor;
            }
            else if (dist == 2)
            {
                lbl.FontSize = 13;
                lbl.TextColor = hintColor;
            }
            else if (dist == 3)
            {
                lbl.FontSize = 12;
                lbl.TextColor = hintColor.WithAlpha(0.7f);
            }
            else
            {
                lbl.FontSize = 11;
                lbl.TextColor = hintColor.WithAlpha(0.5f);
            }
        }

        _lastHighlightIndex = index;
    }

    private void HighlightLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        var hintColor = (Color)Application.Current!.Resources["TextHintColor"];
        var secondaryColor = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];

        if (_lastHighlightIndex >= 0 && _lastHighlightIndex < _lyricLabels.Count)
        {
            var prev = _lyricLabels[_lastHighlightIndex];
            prev.FontSize = 14;
            prev.FontAttributes = FontAttributes.None;

            var dist = Math.Abs(_lastHighlightIndex - index);
            if (dist == 1)
                prev.TextColor = secondaryColor;
            else if (dist == 2)
                prev.TextColor = hintColor;
            else
                prev.TextColor = hintColor.WithAlpha(0.55f);
        }

        var current = _lyricLabels[index];
        current.FontSize = 16;
        current.FontAttributes = FontAttributes.Bold;
        current.TextColor = primaryColor;

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            if (i == index) continue;
            var dist = Math.Abs(i - index);
            var lbl = _lyricLabels[i];
            if (dist == 1)
            {
                lbl.FontSize = 14;
                lbl.TextColor = secondaryColor;
            }
            else if (dist == 2)
            {
                lbl.FontSize = 13;
                lbl.TextColor = hintColor;
            }
            else if (dist == 3)
            {
                lbl.FontSize = 12;
                lbl.TextColor = hintColor.WithAlpha(0.7f);
            }
            else
            {
                lbl.FontSize = 11;
                lbl.TextColor = hintColor.WithAlpha(0.5f);
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
            var y = label.Y + label.Height / 2;
            var scrollY = y - LyricScrollView.Height / 2;
            scrollY = Math.Max(0, scrollY);

            await LyricScrollView.ScrollToAsync(0, scrollY, true);
        }
        catch { }
    }

    /// <summary>进度定时器每次触发时调用，在用户未拖动时同步进度条与播放时长显示。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (_isDragging) return;

        var duration = _viewModel.Duration;
        var progress = _viewModel.Progress;

        if (duration < 1)
        {
            duration = _viewModel.AudioServiceDuration;
            if (duration > 1)
            {
                _viewModel.Duration = duration;
                _viewModel.TotalTimeDisplay = FormatTime(duration);
            }
        }

        if (duration > 1)
        {
            if (ProgressSlider.Maximum != duration)
                ProgressSlider.Maximum = duration;

            if (Math.Abs(ProgressSlider.Value - progress) > 0.5)
                ProgressSlider.Value = progress;
        }
    }

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
            MainPage.Instance?.SwitchToFullLyrics();
            return;
        }

        if (LyricsContainer.IsVisible)
        {
            var pt = e.GetPosition(LyricsContainer);
            if (pt.HasValue && pt.Value.X >= 0 && pt.Value.X <= LyricsContainer.Width
                && pt.Value.Y >= 0 && pt.Value.Y <= LyricsContainer.Height)
            {
                MainPage.Instance?.SwitchToFullLyrics();
                return;
            }
        }

        if (NoLyricsLabel.IsVisible)
        {
            var pt = e.GetPosition(NoLyricsLabel);
            if (pt.HasValue && pt.Value.X >= -20 && pt.Value.X <= NoLyricsLabel.Width + 20
                && pt.Value.Y >= -10 && pt.Value.Y <= NoLyricsLabel.Height + 10)
            {
                MainPage.Instance?.SwitchToFullLyrics();
            }
        }
    }
}

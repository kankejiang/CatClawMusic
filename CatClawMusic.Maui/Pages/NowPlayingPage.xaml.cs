using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private IDispatcherTimer? _progressTimer;
    private bool _isDragging;

    public NowPlayingPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = _viewModel;

        // 使用 Application 级别的 Dispatcher 创建 timer，确保在 Singleton 构造时可用
        _progressTimer = Application.Current!.Dispatcher.CreateTimer();
        _progressTimer.Interval = TimeSpan.FromMilliseconds(500);
        _progressTimer.Tick += OnProgressTimerTick;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCurrentSongAsync();

        // 确保 Slider Maximum 同步
        if (_viewModel.Duration > 0)
            ProgressSlider.Maximum = _viewModel.Duration;

        // 启动 timer（每次页面出现时确保 timer 在运行）
        if (!_progressTimer.IsRunning)
            _progressTimer.Start();
    }

    private void OnProgressTimerTick(object? sender, EventArgs e)
    {
        if (_isDragging) return;

        // 直接设置 Maximum 和 Value，绕过 MAUI 绑定的不可靠性
        var duration = _viewModel.Duration;
        var progress = _viewModel.Progress;

        // Duration 小于 1 秒视为无效，从 AudioService 拉取
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

            // 只在差异较大时更新，避免不必要的 UI 刷新
            if (Math.Abs(ProgressSlider.Value - progress) > 0.5)
                ProgressSlider.Value = progress;
        }
    }

    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        _isDragging = true;
        _viewModel.OnSeekStarted();
    }

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

    /// <summary>收起按钮：切换到发现页</summary>
    private void OnCollapseClicked(object? sender, EventArgs e)
    {
        MainPage.Instance?.SwitchToTab(1);
    }

    /// <summary>根Grid点击检测：判断是否点击在歌曲/歌词区域，若是则进入全屏歌词</summary>
    private void OnPageTapped(object? sender, TappedEventArgs e)
    {
        // 点击封面区域 → 进入全屏歌词
        var ptCover = e.GetPosition(CoverArea);
        if (ptCover.HasValue && ptCover.Value.X >= -10 && ptCover.Value.X <= CoverArea.Width + 10
            && ptCover.Value.Y >= -10 && ptCover.Value.Y <= CoverArea.Height + 10)
        {
            MainPage.Instance?.SwitchToFullLyrics();
            return;
        }

        // 点击歌词容器区域 → 进入全屏歌词
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

        // 点击"暂无歌词"标签区域
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

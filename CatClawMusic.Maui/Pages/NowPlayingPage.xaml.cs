using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls;

namespace CatClawMusic.Maui.Pages;

public partial class NowPlayingPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private IDispatcherTimer? _progressTimer;
    private bool _isDragging;
    private readonly List<Label> _lyricLabels = new();
    private int _lastHighlightIndex = -1;

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

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadCurrentSongAsync();

        if (_viewModel.Duration > 0)
            ProgressSlider.Maximum = _viewModel.Duration;

        Application.Current!.RequestedThemeChanged += OnThemeChanged;

        BuildLyricViews();

        if (!_progressTimer.IsRunning)
            _progressTimer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Application.Current!.RequestedThemeChanged -= OnThemeChanged;
    }

    private void OnThemeChanged(object? sender, AppThemeChangedEventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(BuildLyricViews);
    }

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
            HighlightLine(_viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0);
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

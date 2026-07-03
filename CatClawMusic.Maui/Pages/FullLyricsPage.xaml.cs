using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

public partial class FullLyricsPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private readonly List<Label> _lyricLabels = new();
    private bool _userScrolling = false;
    private int _lastHighlightIndex = -1;

    public FullLyricsPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildLyricViews();
    }

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
        }
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        Application.Current!.RequestedThemeChanged += OnThemeChanged;
        if (_viewModel.AllLyricLines != null && _viewModel.AllLyricLines.Count > 0)
        {
            if (_lyricLabels.Count != _viewModel.AllLyricLines.Count)
                BuildLyricViews();
            else
            {
                var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                HighlightLineWithoutScroll(idx);
            }

            // 延迟滚动到当前歌词行，确保布局完成后再定位
            _ = Task.Delay(100).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
                    HighlightLine(idx);
                }));
        }
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

    private void BuildLyricViews()
    {
        LyricStack.Children.Clear();
        _lyricLabels.Clear();
        _lastHighlightIndex = -1;

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
        {
            var label = new Label
            {
                Text = _viewModel.NoLyricsText,
                FontSize = 16,
                TextColor = (Color)Application.Current!.Resources["TextHintColor"],
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            LyricStack.Children.Add(label);
            return;
        }

        var hintColor = (Color)Application.Current!.Resources["TextHintColor"];

        foreach (var line in lines)
        {
            var label = new Label
            {
                Text = line.Text,
                FontSize = 18,
                TextColor = hintColor,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                Padding = new Thickness(16, 4)
            };

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.Center };
                stack.Children.Add(label);

                var transLabel = new Label
                {
                    Text = line.Translation,
                    FontSize = 13,
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

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        HighlightLineWithoutScroll(idx);
    }

    private void HighlightLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        var hintColor = (Color)Application.Current!.Resources["TextHintColor"];
        var secondaryColor = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            if (i == index)
            {
                lbl.FontSize = 22;
                lbl.FontAttributes = FontAttributes.Bold;
                lbl.TextColor = primaryColor;
            }
            else if (dist <= 1)
            {
                lbl.FontSize = 18;
                lbl.FontAttributes = FontAttributes.None;
                lbl.TextColor = secondaryColor;
            }
            else if (dist <= 3)
            {
                lbl.FontSize = 16;
                lbl.FontAttributes = FontAttributes.None;
                lbl.TextColor = hintColor;
            }
            else
            {
                lbl.FontSize = 14;
                lbl.FontAttributes = FontAttributes.None;
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

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            if (i == index)
            {
                lbl.FontSize = 22;
                lbl.FontAttributes = FontAttributes.Bold;
                lbl.TextColor = primaryColor;
            }
            else if (dist <= 1)
            {
                lbl.FontSize = 18;
                lbl.FontAttributes = FontAttributes.None;
                lbl.TextColor = secondaryColor;
            }
            else if (dist <= 3)
            {
                lbl.FontSize = 16;
                lbl.FontAttributes = FontAttributes.None;
                lbl.TextColor = hintColor;
            }
            else
            {
                lbl.FontSize = 14;
                lbl.FontAttributes = FontAttributes.None;
                lbl.TextColor = hintColor.WithAlpha(0.5f);
            }
        }

        _lastHighlightIndex = index;

        if (!_userScrolling)
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

    private void OnLyricScrolled(object? sender, ScrolledEventArgs e)
    {
        _userScrolling = true;
        _ = ResetUserScrollingAsync();
    }

    private async Task ResetUserScrollingAsync()
    {
        await Task.Delay(3000);
        _userScrolling = false;
    }

    private void OnBackClicked(object? sender, EventArgs e)
    {
        MainPage.Instance?.SwitchToTab(0);
    }
}

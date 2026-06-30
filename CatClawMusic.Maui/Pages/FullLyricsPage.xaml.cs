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
        }

        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLyricIndexObservable))
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                HighlightLine(_viewModel.CurrentLyricIndexObservable);
            });
        }
    }

    /// <summary>动态构建所有歌词行标签</summary>
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

        foreach (var line in lines)
        {
            var label = new Label
            {
                Text = line.Text,
                FontSize = 18,
                TextColor = Color.FromArgb("#80FFFFFF"), // Inactive color
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center,
                LineBreakMode = LineBreakMode.WordWrap,
                Padding = new Thickness(16, 4)
            };

            // If line has translation, add a sub-label
            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 2, HorizontalOptions = LayoutOptions.Center };
                stack.Children.Add(label);

                var transLabel = new Label
                {
                    Text = line.Translation,
                    FontSize = 13,
                    TextColor = Color.FromArgb("#50FFFFFF"),
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

        // 首行高亮
        HighlightLine(0);
    }

    /// <summary>高亮指定行并自动滚动</summary>
    private void HighlightLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        // 取消上一行的高亮
        if (_lastHighlightIndex >= 0 && _lastHighlightIndex < _lyricLabels.Count)
        {
            var prev = _lyricLabels[_lastHighlightIndex];
            prev.FontSize = 18;
            prev.FontAttributes = FontAttributes.None;
            prev.TextColor = Color.FromArgb("#80FFFFFF");
        }

        // 高亮当前行
        var current = _lyricLabels[index];
        current.FontSize = 22;
        current.FontAttributes = FontAttributes.Bold;
        current.TextColor = (Color)Application.Current!.Resources["PrimaryLightColor"];

        _lastHighlightIndex = index;

        // 自动滚动到当前行
        if (!_userScrolling)
            ScrollToLine(index);
    }

    /// <summary>将指定行滚动到视图中央</summary>
    private async void ScrollToLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        try
        {
            // 获取标签在父容器中的位置
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
        // 检测用户手动滚动（3秒无操作后恢复自动滚动）
        _userScrolling = true;
        _ = ResetUserScrollingAsync();
    }

    private async Task ResetUserScrollingAsync()
    {
        await Task.Delay(3000);
        _userScrolling = false;
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
    }
}

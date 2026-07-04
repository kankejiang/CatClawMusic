using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>全屏歌词页面，以全屏方式展示当前播放歌曲的完整歌词并支持自动滚动与高亮。</summary>
public partial class FullLyricsPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private readonly List<Label> _lyricLabels = new();
    private bool _userScrolling = false;
    private int _lastHighlightIndex = -1;

    /// <summary>初始化 <see cref="FullLyricsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">当前播放视图模型，提供歌词与播放状态数据。</param>
    public FullLyricsPage(NowPlayingViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildLyricViews();
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
        }
    }

    /// <summary>当页面显示在屏幕上时触发，订阅主题变更事件并重建或恢复歌词高亮状态。</summary>
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

    /// <summary>当用户手动滚动歌词视图时触发，标记用户正在滚动以暂停自动滚动定位。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">滚动事件参数。</param>
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

    /// <summary>点击返回按钮时触发，切换回主页面的第一个标签页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnBackClicked(object? sender, EventArgs e)
    {
        MainPage.Instance?.SwitchToTab(0);
    }
}

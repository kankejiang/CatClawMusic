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
        SafeAreaHelper.SafeAreaChanged += OnSafeAreaChanged;
        BuildLyricViews();
    }

    /// <summary>系统栏高度变化时触发，更新内容区域的顶部 padding 以避开状态栏</summary>
    private void OnSafeAreaChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(ApplySafeArea);
    }

    /// <summary>给 ContentGrid 应用 SafeArea 顶部 padding（雾面背景不应用，保持延伸到状态栏）</summary>
    private void ApplySafeArea()
    {
        var top = SafeAreaHelper.TopInset;
        ContentGrid.Padding = new Thickness(0, top, 0, 0);
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
        ApplySafeArea();
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

        var nonCurrentColor = Colors.White;
        var translationColor = Colors.White.WithAlpha(0.7f);

        foreach (var line in lines)
        {
            var label = new Label
            {
                Text = line.Text,
                FontSize = 18,
                TextColor = nonCurrentColor,
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
                    TextColor = translationColor,
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

        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var nonCurrentColor = Colors.White;

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            // 非当前行颜色统一，仅字号递减以保持层次感
            if (i == index)
            {
                lbl.FontSize = 22;
                lbl.FontAttributes = FontAttributes.Bold;
                lbl.TextColor = primaryColor;
            }
            else
            {
                lbl.FontAttributes = FontAttributes.None;
                lbl.TextColor = nonCurrentColor;
                if (dist <= 1)
                    lbl.FontSize = 18;
                else if (dist <= 3)
                    lbl.FontSize = 16;
                else
                    lbl.FontSize = 14;
            }
        }

        _lastHighlightIndex = index;
    }

    private void HighlightLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        var nonCurrentColor = Colors.White;

        // 仅更新新旧索引附近 ±4 范围内的行（避免全量遍历所有 Label）
        var affectedMin = Math.Max(0, Math.Min(index, _lastHighlightIndex) - 4);
        var affectedMax = Math.Min(_lyricLabels.Count - 1, Math.Max(index, _lastHighlightIndex) + 4);

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            // 非当前行颜色统一，仅字号递减以保持层次感
            if (i == index)
            {
                lbl.FontSize = 22;
                lbl.FontAttributes = FontAttributes.Bold;
                lbl.TextColor = primaryColor;
            }
            else
            {
                lbl.FontAttributes = FontAttributes.None;
                lbl.TextColor = nonCurrentColor;
                if (dist <= 1)
                    lbl.FontSize = 18;
                else if (dist <= 3)
                    lbl.FontSize = 16;
                else
                    lbl.FontSize = 14;
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

#if ANDROID
            if (LyricCollectionView.Handler?.PlatformView is global::AndroidX.RecyclerView.Widget.RecyclerView recyclerView
                && label.Handler?.PlatformView is global::Android.Views.View nativeLabel)
            {
                int[] labelLocation = new int[2];
                nativeLabel.GetLocationOnScreen(labelLocation);
                int[] recyclerLocation = new int[2];
                recyclerView.GetLocationOnScreen(recyclerLocation);

                int labelCenterY = labelLocation[1] + nativeLabel.Height / 2;
                int recyclerCenterY = recyclerLocation[1] + recyclerView.Height / 2;
                int dy = labelCenterY - recyclerCenterY;

                if (Math.Abs(dy) > 2)
                {
                    recyclerView.SmoothScrollBy(0, dy);
                }
            }
            else if (LyricCollectionView.Handler?.PlatformView is global::Android.Views.View nativeView)
            {
                var y = GetRelativeY(label);
                var targetScrollY = y - LyricCollectionView.Height / 2;
                targetScrollY = Math.Max(0, targetScrollY);
                nativeView.ScrollY = (int)targetScrollY;
            }
#else
            var y = GetRelativeY(label);
            var targetScrollY = y - LyricCollectionView.Height / 2;
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

    /// <summary>用户手动滚动歌词时标记用户滚动状态（通过平台事件监听）</summary>
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

    /// <summary>点击返回按钮时触发，切换回主页面的第一个标签页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnBackClicked(object? sender, EventArgs e)
    {
        MainPage.Instance?.SwitchToTab(0);
    }
}

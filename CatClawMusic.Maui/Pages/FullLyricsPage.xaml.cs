using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>全屏歌词页面，以全屏方式展示当前播放歌曲的完整歌词并支持自动滚动与高亮。</summary>
public partial class FullLyricsPage : ContentPage
{
    private readonly NowPlayingViewModel _viewModel;
    private readonly List<KaraokeLabel> _lyricLabels = new();
    private readonly List<Border> _lyricBorders = new();
    private bool _userScrolling = false;
    private int _lastHighlightIndex = -1;
    private readonly LyricsSettingsService _settings = LyricsSettingsService.Instance;

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
            return;
        }

        // 逐字填充进度变化：直接更新当前行 KaraokeLabel 的 FillProgress（无需重建视图）
        if (e.PropertyName == nameof(NowPlayingViewModel.CurrentLineFillProgress))
        {
            var idx = _viewModel.CurrentLyricIndexObservable;
            if (idx >= 0 && idx < _lyricLabels.Count)
            {
                var progress = _viewModel.CurrentLineFillProgress;
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (idx >= 0 && idx < _lyricLabels.Count)
                        _lyricLabels[idx].FillProgress = progress;
                });
            }
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
        _lyricBorders.Clear();
        _lastHighlightIndex = -1;

        var lines = _viewModel.AllLyricLines;
        if (lines == null || lines.Count == 0)
        {
            var label = new KaraokeLabel
            {
                Text = _viewModel.NoLyricsText,
                FontSize = 16,
                FontFamily = "OpenSansSemibold",
                TextColor = (Color)Application.Current!.Resources["TextHintColor"],
                OutlineColor = (Color)Application.Current!.Resources["TextHintColor"],
                StrokeWidth = 1,
                FillProgress = 1,
                HorizontalTextAlignment = TextAlignment.Center,
                HorizontalOptions = LayoutOptions.Center
            };
            LyricStack.Children.Add(label);
            return;
        }

        foreach (var line in lines)
        {
            var label = new KaraokeLabel
            {
                Text = line.Text,
                FontSize = 15,
                FontFamily = "OpenSansRegular",
                FontAttributes = FontAttributes.None,
                TextColor = Colors.White,
                OutlineColor = Colors.White,
                StrokeWidth = 2,
                FillProgress = 0,
                HorizontalTextAlignment = _settings.ToTextAlignment(),
                HorizontalOptions = _settings.ToLayoutOptions(),
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
                HorizontalOptions = _settings.ToLayoutOptions()
            };
            border.Content = label;

            if (!string.IsNullOrEmpty(line.Translation))
            {
                var stack = new VerticalStackLayout { Spacing = 4, HorizontalOptions = _settings.ToLayoutOptions() };
                stack.Children.Add(border);

                var transLabel = new KaraokeLabel
                {
                    Text = line.Translation,
                    FontSize = 13,
                    FontFamily = "OpenSansRegular",
                    FontAttributes = FontAttributes.None,
                    TextColor = Colors.White,
                    OutlineColor = Colors.White,
                    StrokeWidth = 1.5,
                    FillProgress = 0,
                    HorizontalTextAlignment = _settings.ToTextAlignment(),
                    HorizontalOptions = _settings.ToLayoutOptions(),
                    Opacity = 0.2
                };
                stack.Children.Add(transLabel);
                LyricStack.Children.Add(stack);
            }
            else
            {
                LyricStack.Children.Add(border);
            }

            _lyricLabels.Add(label);
            _lyricBorders.Add(border);
        }

        var idx = _viewModel.CurrentLyricIndexObservable >= 0 ? _viewModel.CurrentLyricIndexObservable : 0;
        HighlightLineWithoutScroll(idx);
    }

    private void HighlightLineWithoutScroll(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        var baseSize = _settings.FontSize;

        for (int i = 0; i < _lyricLabels.Count; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            if (i == index)
            {
                // 当前行：实心填充，进度由 ViewModel 逐字计算（逐行模式为 1.0）
                lbl.FontSize = baseSize;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                // 非当前行：空心描边，按距离递减透明度
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                switch (dist)
                {
                    case 1:
                        lbl.FontSize = baseSize - 6;
                        lbl.Opacity = 0.5;
                        break;
                    case 2:
                        lbl.FontSize = baseSize - 9;
                        lbl.Opacity = 0.28;
                        break;
                    case 3:
                        lbl.FontSize = baseSize - 11;
                        lbl.Opacity = 0.16;
                        break;
                    default:
                        lbl.FontSize = baseSize - 11;
                        lbl.Opacity = 0.1;
                        break;
                }
            }
        }

        _lastHighlightIndex = index;
    }

    private void HighlightLine(int index)
    {
        if (index < 0 || index >= _lyricLabels.Count) return;

        var baseSize = _settings.FontSize;
        var affectedMin = Math.Max(0, Math.Min(index, _lastHighlightIndex) - 5);
        var affectedMax = Math.Min(_lyricLabels.Count - 1, Math.Max(index, _lastHighlightIndex) + 5);

        for (int i = affectedMin; i <= affectedMax; i++)
        {
            var lbl = _lyricLabels[i];
            var dist = Math.Abs(i - index);

            if (i == index)
            {
                // 当前行：实心填充，进度由 ViewModel 逐字计算（逐行模式为 1.0）
                lbl.FontSize = baseSize;
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = _viewModel.CurrentLineFillProgress;
                lbl.Opacity = 1.0;
            }
            else
            {
                lbl.FontAttributes = FontAttributes.None;
                lbl.FillProgress = 0;
                switch (dist)
                {
                    case 1:
                        lbl.FontSize = baseSize - 6;
                        lbl.Opacity = 0.5;
                        break;
                    case 2:
                        lbl.FontSize = baseSize - 9;
                        lbl.Opacity = 0.28;
                        break;
                    case 3:
                        lbl.FontSize = baseSize - 11;
                        lbl.Opacity = 0.16;
                        break;
                    default:
                        lbl.FontSize = baseSize - 11;
                        lbl.Opacity = 0.1;
                        break;
                }
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

    /// <summary>点击返回按钮时触发：移动端切换回主页全屏歌词 tab，桌面端通过 Shell 导航返回上一页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private void OnBackClicked(object? sender, EventArgs e)
    {
#if WINDOWS
        // 桌面端：FullLyricsPage 通过 Shell.GoToAsync("//fullyrics") 推入，返回上一页即可
        if (Shell.Current.Navigation.NavigationStack.Count > 1)
        {
            _ = Shell.Current.Navigation.PopAsync();
        }
        else
        {
            _ = Shell.Current.GoToAsync("//main");
        }
#else
        // 移动端：切回主页 ViewPager 的全屏歌词 tab（index 0）
        MainPage.Instance?.SwitchToTab(0);
#endif
    }

    /// <summary>点击右上角歌词设置按钮，弹出歌词设置面板</summary>
    private void OnLyricsSettingsClicked(object? sender, EventArgs e)
    {
        // 第一次点击时构建弹窗内容
        if (LyricsSettingsPopup.PopupContent.Children.Count <= 1)
        {
            var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
            var inactiveColor = (Color)Application.Current.Resources["ChipInactiveColor"];
            var textSecondary = (Color)Application.Current.Resources["TextSecondaryColor"];
            var textHint = (Color)Application.Current.Resources["TextHintColor"];

            // ─── 歌词模式 ───
            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词模式", textHint));
            LyricsSettingsPopup.AddContent(BuildSegmentedControl(
                ("逐行", LyricsSettingsService.Mode.Line),
                ("逐字", LyricsSettingsService.Mode.Word),
                _settings.LyricsMode,
                value =>
                {
                    _settings.LyricsMode = value;
                    RebuildLyricsView();
                },
                primaryColor, inactiveColor, Colors.White, textSecondary));

            // ─── 歌词位置显示 ───
            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词位置显示", textHint));
            LyricsSettingsPopup.AddContent(BuildSegmentedControl(
                ("居左", LyricsSettingsService.Alignment.Left),
                ("居中", LyricsSettingsService.Alignment.Center),
                ("居右", LyricsSettingsService.Alignment.Right),
                _settings.LyricsAlignment,
                value =>
                {
                    _settings.LyricsAlignment = value;
                    RebuildLyricsView();
                },
                primaryColor, inactiveColor, Colors.White, textSecondary));

            // ─── 歌词字体大小 ───
            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词字体大小", textHint));
            LyricsSettingsPopup.AddContent(BuildFontSizeSlider(primaryColor, textSecondary, textHint));
        }

        LyricsSettingsPopup.Open();
    }

    /// <summary>重建歌词视图以应用新设置</summary>
    private void RebuildLyricsView()
    {
        // 先刷新 ViewModel 的逐字进度（切换逐行/逐字模式后立即生效）
        _viewModel.RefreshFillProgress();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            BuildLyricViews();
        });
    }

    private Label BuildSectionLabel(string text, Color color)
    {
        return new Label
        {
            Text = text,
            FontSize = 13,
            TextColor = color,
            Margin = new Thickness(0, 0, 0, 8),
            FontAttributes = FontAttributes.None
        };
    }

    private View BuildSpacer(double height)
    {
        return new BoxView { HeightRequest = height, BackgroundColor = Colors.Transparent };
    }

    /// <summary>构建分段选择器（两选项或三选项的胶囊式单选）</summary>
    private View BuildSegmentedControl<T>(
        (string Label, T Value) option1,
        (string Label, T Value) option2,
        T currentValue,
        Action<T> onSelected,
        Color activeColor, Color inactiveColor,
        Color activeTextColor, Color inactiveTextColor) where T : Enum
    {
        return BuildSegmentedControlCore(new[] { option1, option2 }, currentValue, onSelected, activeColor, inactiveColor, activeTextColor, inactiveTextColor);
    }

    private View BuildSegmentedControl<T>(
        (string Label, T Value) option1,
        (string Label, T Value) option2,
        (string Label, T Value) option3,
        T currentValue,
        Action<T> onSelected,
        Color activeColor, Color inactiveColor,
        Color activeTextColor, Color inactiveTextColor) where T : Enum
    {
        return BuildSegmentedControlCore(new[] { option1, option2, option3 }, currentValue, onSelected, activeColor, inactiveColor, activeTextColor, inactiveTextColor);
    }

    private View BuildSegmentedControlCore<T>(
        (string Label, T Value)[] options,
        T currentValue,
        Action<T> onSelected,
        Color activeColor, Color inactiveColor,
        Color activeTextColor, Color inactiveTextColor) where T : Enum
    {
        var colCount = options.Length;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection(
                Enumerable.Range(0, colCount)
                    .Select(_ => new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) })
                    .ToArray()),
            ColumnSpacing = 6,
            HeightRequest = 44
        };

        // 持有按钮引用以便切换状态
        var borders = new List<Border>();
        var labels = new List<Label>();

        for (int i = 0; i < colCount; i++)
        {
            var opt = options[i];
            var isActive = EqualityComparer<T>.Default.Equals(opt.Value, currentValue);

            var label = new Label
            {
                Text = opt.Label,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = isActive ? activeTextColor : inactiveTextColor,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            };
            labels.Add(label);

            var border = new Border
            {
                BackgroundColor = isActive ? activeColor : inactiveColor,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(18) },
                StrokeThickness = 0,
                Padding = new Thickness(0),
                Content = label
            };
            borders.Add(border);

            // 点击切换
            var captured = opt.Value;
            var tap = new TapGestureRecognizer();
            tap.Tapped += (_, _) =>
            {
                onSelected(captured);
                // 更新所有按钮状态
                for (int j = 0; j < options.Length; j++)
                {
                    var sel = EqualityComparer<T>.Default.Equals(options[j].Value, captured);
                    borders[j].BackgroundColor = sel ? activeColor : inactiveColor;
                    labels[j].TextColor = sel ? activeTextColor : inactiveTextColor;
                }
            };
            border.GestureRecognizers.Add(tap);

            grid.Add(border, i);
        }

        return grid;
    }

    /// <summary>构建字体大小拖动条</summary>
    private View BuildFontSizeSlider(Color primaryColor, Color textSecondary, Color textHint)
    {
        var minSize = LyricsSettingsService.MinFontSize;
        var maxSize = LyricsSettingsService.MaxFontSize;
        var currentSize = _settings.FontSize;

        var valueLabel = new Label
        {
            Text = $"{currentSize:F0}",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = primaryColor,
            HorizontalOptions = LayoutOptions.End,
            Margin = new Thickness(0, 0, 0, 6)
        };

        var slider = new Slider
        {
            Minimum = minSize,
            Maximum = maxSize,
            Value = currentSize,
            ThumbColor = primaryColor,
            MinimumTrackColor = primaryColor,
            MaximumTrackColor = (Color)Application.Current!.Resources["GlassStrokeColor"],
            HeightRequest = 40
        };
        slider.ValueChanged += (_, e) =>
        {
            var newSize = Math.Round(e.NewValue);
            _settings.FontSize = newSize;
            valueLabel.Text = $"{newSize:F0}";
            RebuildLyricsView();
        };

        var rangeGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            }
        };
        rangeGrid.Add(new Label { Text = "A", FontSize = 11, TextColor = textHint }, 0);
        rangeGrid.Add(new Label { Text = $"{maxSize:F0}", FontSize = 11, TextColor = textHint, HorizontalOptions = LayoutOptions.End }, 2);

        return new VerticalStackLayout
        {
            Spacing = 4,
            Children =
            {
                valueLabel,
                slider,
                rangeGrid
            }
        };
    }
}

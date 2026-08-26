using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;

namespace CatClawMusic.Maui.Pages;

/// <summary>全屏歌词页 —— partial 分域文件。</summary>
public partial class FullLyricsPage
{
    /// <summary>上次构建设置弹窗时扩展歌词能力是否可用（插件加载状态变化时重建弹窗内容）</summary>
    private bool _extendedLyricsSectionBuilt;

    private void OnLyricsSettingsClicked(object? sender, EventArgs e)
    {
        // 扩展歌词功能由插件提供：检测到已启用的扩展歌词插件时才构建该分区，
        // 插件安装/卸载/启停后再次打开弹窗会自动重建（与上次构建状态不同时清空重建）
        var extendedAvailable = LyricsSettingsService.ExtendedLyricsAvailable;
        if (LyricsSettingsPopup.PopupContent.Children.Count <= 1 || _extendedLyricsSectionBuilt != extendedAvailable)
        {
            if (LyricsSettingsPopup.PopupContent.Children.Count > 1)
                LyricsSettingsPopup.ClearContent();
            _extendedLyricsSectionBuilt = extendedAvailable;
            var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
            var inactiveColor = (Color)Application.Current.Resources["ChipInactiveColor"];
            var textSecondary = (Color)Application.Current.Resources["TextSecondaryColor"];
            var textHint = (Color)Application.Current.Resources["TextHintColor"];

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

            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词来源模式", textHint));
            LyricsSettingsPopup.AddContent(BuildSegmentedControl(
                ("自动", LyricsSourceMode.Auto),
                ("内嵌", LyricsSourceMode.Embedded),
                ("外挂", LyricsSourceMode.External),
                _settings.LyricsSourceMode,
                value =>
                {
                    _settings.LyricsSourceMode = value;
                    _viewModel.ReloadLyricsForCurrentSong();
                },
                primaryColor, inactiveColor, Colors.White, textSecondary));

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

            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("歌词字体大小", textHint));
            LyricsSettingsPopup.AddContent(BuildFontSizeSlider(primaryColor, textSecondary, textHint));

            LyricsSettingsPopup.AddContent(BuildSpacer(16));
            LyricsSettingsPopup.AddContent(BuildSectionLabel("智能删除空行", textHint));
            LyricsSettingsPopup.AddContent(BuildToggleSwitch(
                "紧凑显示，移除歌词中的空行",
                _settings.RemoveEmptyLines,
                value =>
                {
                    _settings.RemoveEmptyLines = value;
                    _viewModel.RefreshFilteredLines();
                    RebuildLyricsView();
                },
                primaryColor, textSecondary, textHint));

            if (extendedAvailable)
            {
                var extendedTitle = LyricsSettingsService.ExtendedLyricsPlugin?.ExtensionTitle ?? "扩展歌词";
                LyricsSettingsPopup.AddContent(BuildSpacer(16));
                LyricsSettingsPopup.AddContent(BuildSectionLabel(extendedTitle, textHint));
                LyricsSettingsPopup.AddContent(BuildToggleSwitch(
                    "显示歌词译文",
                    _settings.ShowTranslation,
                    value =>
                    {
                        _settings.ShowTranslation = value;
                        RebuildLyricsView();
                    },
                    primaryColor, textSecondary, textHint));
                LyricsSettingsPopup.AddContent(BuildToggleSwitch(
                    "显示罗马音",
                    _settings.ShowRoma,
                    value =>
                    {
                        _settings.ShowRoma = value;
                        RebuildLyricsView();
                    },
                    primaryColor, textSecondary, textHint));
            }
        }

        LyricsSettingsPopup.Open();
    }

    /// <summary>重建所有歌词视图</summary>
    private void RebuildLyricsView()
    {
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

    private View BuildSegmentedControl<T>(
        (string Label, T Value) option1,
        (string Label, T Value) option2,
        (string Label, T Value) option3,
        (string Label, T Value) option4,
        T currentValue,
        Action<T> onSelected,
        Color activeColor, Color inactiveColor,
        Color activeTextColor, Color inactiveTextColor) where T : Enum
    {
        return BuildSegmentedControlCore(new[] { option1, option2, option3, option4 }, currentValue, onSelected, activeColor, inactiveColor, activeTextColor, inactiveTextColor);
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

        var buttons = new List<Button>();

        for (int i = 0; i < colCount; i++)
        {
            var opt = options[i];
            var isActive = EqualityComparer<T>.Default.Equals(opt.Value, currentValue);

            var btn = new Button
            {
                Text = opt.Label,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = isActive ? activeTextColor : inactiveTextColor,
                BackgroundColor = isActive ? activeColor : inactiveColor,
                CornerRadius = 22,
                HeightRequest = 44,
                Padding = new Thickness(0),
                Margin = new Thickness(0),
                VerticalOptions = LayoutOptions.Fill
            };

            var captured = opt.Value;
            btn.Clicked += (_, _) =>
            {
                onSelected(captured);
                for (int j = 0; j < options.Length; j++)
                {
                    var sel = EqualityComparer<T>.Default.Equals(options[j].Value, captured);
                    buttons[j].BackgroundColor = sel ? activeColor : inactiveColor;
                    buttons[j].TextColor = sel ? activeTextColor : inactiveTextColor;
                }
            };

            buttons.Add(btn);
            grid.Add(btn, i);
        }

        return grid;
    }

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

    private View BuildToggleSwitch(
        string description, bool currentValue,
        Action<bool> onToggled,
        Color primaryColor, Color textSecondary, Color textHint)
    {
        var descLabel = new Label
        {
            Text = description,
            FontSize = 13,
            TextColor = textSecondary,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Start
        };

        var toggle = new Switch
        {
            IsToggled = currentValue,
            OnColor = primaryColor,
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        toggle.Toggled += (_, e) => onToggled(e.Value);

        return new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = new GridLength(1, GridUnitType.Star) },
                new() { Width = GridLength.Auto }
            },
            HeightRequest = 44,
            Children = { descLabel, toggle }
        };
    }
}

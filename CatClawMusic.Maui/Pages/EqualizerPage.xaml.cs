using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Controls.EqBands;
using CatClawMusic.Maui.Helpers;
using System.Collections.ObjectModel;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.Services.Equalizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using static CatClawMusic.Maui.Controls.PopupUiHelpers;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 图形均衡器独立全屏页面。
/// 打开：Shell.Current.Navigation.PushModalAsync（从下往上滑入）；关闭 PopModalAsync。
/// 作为窗口级模态页面，不嵌套在滚动页内，彻底避开自定义手势与父滚动冲突。
/// </summary>
public partial class EqualizerPage : ContentPage
{
    private readonly AudioPlayerService _audioPlayer;
    private ObservableCollection<EqBand> _bands = new();
    private readonly List<(string Key, Border View)> _presetChips = new();
    private VerticalStackLayout? _eqBandsArea;
    private Label? _modeDescLabel;
    private Border? _modeAutoChip;
    private Border? _modeAlwaysChip;

    public EqualizerPage()
    {
        InitializeComponent();
        _audioPlayer = IPlatformApplication.Current!.Services.GetRequiredService<AudioPlayerService>();
        BuildHeader();
        BuildEqualizerContent();
    }

    private void AddContent(View v) => BodyStack.Add(v);

    private async Task CloseAsync() => await Navigation.PopModalAsync();

    // ═══════════════════════════════════════
    // 头部
    // ═══════════════════════════════════════

    private void BuildHeader()
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];

        var backBtn = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17) },
            StrokeThickness = 0,
            WidthRequest = 36, HeightRequest = 36,
            HorizontalOptions = LayoutOptions.Start, VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "←", FontSize = 18, TextColor = textPrimary,
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, -2, 0, 0)
            }
        };
        backBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await CloseAsync()) });
        HeaderGrid.Add(backBtn, 0);

        HeaderGrid.Add(new Label
        {
            Text = "图形均衡器",
            FontSize = 24,
            FontAttributes = FontAttributes.Bold,
            TextColor = textPrimary,
            VerticalOptions = LayoutOptions.Center,
            Margin = new Thickness(12, 0, 0, 0)
        }, 1);

        var resetLabel = new Label
        {
            Text = "重置",
            FontSize = 14,
            FontAttributes = FontAttributes.Bold,
            TextColor = primary,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End
        };
        resetLabel.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                EqualizerSettings.ApplyPreset("flat");
                EqualizerSettings.BassBoost = 0;
                EqualizerSettings.Loudness = 0;
                EqualizerSettings.Balance = 0;
                EqualizerSettings.SpatialAudioEnabled = false;
                EqualizerSettings.CrossfadeEnabled = false;
                EqualizerSettings.CrossfadeDuration = 4;
                BuildEqualizerContent();
                ApplyEqSettingsLive();
            })
        });
        HeaderGrid.Add(resetLabel, 2);
    }

    // ═══════════════════════════════════════
    // 主体内容
    // ═══════════════════════════════════════

    private void BuildEqualizerContent()
    {
        BodyStack.Clear();
        _presetChips.Clear();
        _bands.Clear();

        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];

        // 总开关
        AddContent(CreateToggleRow("均衡器开关", "开启后按下方频段调节", EqualizerSettings.Enabled, v =>
        {
            EqualizerSettings.Enabled = v;
            ApplyEqSettingsLive();
            if (EqualizerSettings.UseFFmpegEq && _audioPlayer.IsPlaying)
                _audioPlayer.ReapplyEqualizerLive();
        }, margin: new Thickness(0, 0, 0, 22)));

        // 常见模式（横向按钮条）
        AddContent(SectionLabel("常见模式"));
        var presetScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Margin = new Thickness(0, 0, 0, 22)
        };
        var presetStack = new HorizontalStackLayout { Spacing = 8 };
        var currentPreset = EqualizerSettings.CurrentPreset;
        for (int i = 0; i < EqualizerSettings.PresetList.Length; i++)
        {
            var (key, name) = EqualizerSettings.PresetList[i];
            var chip = CreateChip(name, key == currentPreset, compact: true);
            _presetChips.Add((key, chip));
            var capturedKey = key;
            chip.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => SelectPreset(capturedKey))
            });
            presetStack.Add(chip);
        }
        // 若当前预设不在列表里（如旧版“原声”），默认高亮“自定义”
        if (!_presetChips.Any(c => c.Key == currentPreset))
            HighlightPresetChip("custom");
        presetScroll.Content = presetStack;
        AddContent(presetScroll);

        // 工作模式
        AddContent(SectionLabel("均衡器模式"));
        var modeStack = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(), new() },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 0, 0, 6)
        };
        bool isAlways = EqualizerSettings.FfmpegMode == EqualizerSettings.FfmpegModeAlways;
        _modeAutoChip = CreateModeChip("自动", !isAlways);
        _modeAlwaysChip = CreateModeChip("精确", isAlways);
        _modeAutoChip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectMode(EqualizerSettings.FfmpegModeAuto)) });
        _modeAlwaysChip.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SelectMode(EqualizerSettings.FfmpegModeAlways)) });
        modeStack.Add(_modeAutoChip, 0);
        modeStack.Add(_modeAlwaysChip, 1);
        AddContent(modeStack);
        _modeDescLabel = new Label
        {
            FontSize = 11, TextColor = textHint,
            Text = isAlways
                ? "所有音频经 FFmpeg 烘焙 10 段均衡，效果最精确"
                : "仅不兼容格式走 FFmpeg 软解，使用原生 5 段实时均衡",
            Margin = new Thickness(2, 4, 0, 22)
        };
        AddContent(_modeDescLabel);

        // 均衡器滑块
        AddContent(SectionLabel("均衡器"));
        _eqBandsArea = new VerticalStackLayout { Spacing = 0 };
        AddContent(_eqBandsArea);
        BuildBandArea(textSecondary, primary);

        // 完成按钮
        var doneBtn = CreatePopupButton("完成", true);
        doneBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await CloseAsync())
        });
        AddContent(doneBtn);
    }

    private static Label SectionLabel(string text) => new()
    {
        Text = text,
        FontSize = 13,
        FontAttributes = FontAttributes.Bold,
        TextColor = (Color)Application.Current!.Resources["TextSecondaryColor"],
        Margin = new Thickness(2, 0, 0, 10)
    };

    private void HighlightPresetChip(string key)
    {
        foreach (var (k, view) in _presetChips)
            SetChipActive(view, k == key);
    }

    private void SelectPreset(string key)
    {
        if (key == "custom")
        {
            EqualizerSettings.CurrentPreset = "custom";
        }
        else
        {
            EqualizerSettings.ApplyPreset(key);
            RefreshEqBandUI();
        }
        HighlightPresetChip(key);
        ApplyEqSettingsLive();
    }

    private Border CreateModeChip(string text, bool active)
    {
        var chip = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(0, 12),
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = text,
                FontSize = 13,
                FontAttributes = FontAttributes.Bold,
                TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };
        SetChipActive(chip, active);
        return chip;
    }

    private void SelectMode(string mode)
    {
        if (EqualizerSettings.FfmpegMode == mode) return;
        EqualizerSettings.FfmpegMode = mode;
        var isAlways = mode == EqualizerSettings.FfmpegModeAlways;
        if (_modeAutoChip != null) SetChipActive(_modeAutoChip, !isAlways);
        if (_modeAlwaysChip != null) SetChipActive(_modeAlwaysChip, isAlways);
        if (_modeDescLabel != null)
            _modeDescLabel.Text = isAlways
                ? "所有音频经 FFmpeg 烘焙 10 段均衡，效果最精确"
                : "仅不兼容格式走 FFmpeg 软解，使用原生 5 段实时均衡";
        RebuildBandArea();
        ApplyEqSettingsLive();
        if (_audioPlayer.IsPlaying)
            _audioPlayer.ReapplyEqualizerLive();
    }

    /// <summary>在圆角面板内重建频段滑块（原生 Slider 竖向）。</summary>
    private void BuildBandArea(Color labelColor, Color accentColor)
    {
        var gains = EqualizerSettings.GetBandGains();
        var freqs = EqualizerSettings.BandFrequencies;
        var labels = EqualizerSettings.BandLabels;

        _bands = new ObservableCollection<EqBand>();
        for (int i = 0; i < labels.Length; i++)
            _bands.Add(new EqBand(freqs[i], labels[i], gains[i]));

        var view = new EqBandsView
        {
            MinGain = EqualizerSettings.MinGainDb,
            MaxGain = EqualizerSettings.MaxGainDb,
            Accent = accentColor,
            LabelColor = labelColor,
        };
        view.Bands = _bands;
        view.LiveValueChanged += ApplyEqSettingsLive;
        view.DragCompleted += () =>
        {
            EqualizerSettings.SetBandGains(_bands.Select(b => b.Value).ToArray());
            EqualizerSettings.CurrentPreset = "custom";
            HighlightPresetChip("custom");
            ApplyEqSettingsLive();
        };

        var scaleGrid = new Grid
        {
            RowDefinitions =
            {
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Star },
                new RowDefinition { Height = GridLength.Star }
            },
            WidthRequest = 28,
            HeightRequest = EqBandsView.TrackLength,
            VerticalOptions = LayoutOptions.Center
        };
        string[] scaleLabels =
        {
            $"+{EqualizerSettings.MaxGainDb:0}",
            "+6",
            "0",
            "-6",
            $"-{EqualizerSettings.MaxGainDb:0}"
        };
        for (int i = 0; i < scaleLabels.Length; i++)
        {
            scaleGrid.Add(new Label
            {
                Text = scaleLabels[i],
                FontSize = 9,
                TextColor = labelColor,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalTextAlignment = TextAlignment.Center
            }, 0, i);
        }

        var panel = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.05f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(20) },
            StrokeThickness = 0,
            Padding = new Thickness(10, 14, 10, 14),
            Margin = new Thickness(0, 0, 0, 16),
            Content = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Auto }, new() { Width = GridLength.Star } },
                ColumnSpacing = 4,
                VerticalOptions = LayoutOptions.Center,
                Children = { scaleGrid }
            }
        };
        ((Grid)panel.Content).Add(view, 1);

        _eqBandsArea!.Clear();
        _eqBandsArea.Add(panel);
    }

    private void RebuildBandArea()
    {
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];
        BuildBandArea(textSecondary, primary);
    }

    private void RefreshEqBandUI()
    {
        var gains = EqualizerSettings.GetBandGains();
        for (int i = 0; i < _bands.Count && i < gains.Length; i++)
            _bands[i].Value = gains[i];
    }

    private void ApplyEqSettingsLive()
    {
        EqualizerSettings.SetBandGains(_bands.Select(b => b.Value).ToArray());
        _audioPlayer.ApplyEqualizer();
        if (EqualizerSettings.UseFFmpegEq && EqualizerSettings.Enabled && _audioPlayer.IsPlaying)
            _audioPlayer.ReapplyEqualizerLive();
    }
}

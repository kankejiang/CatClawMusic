using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
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
/// 音效中心（均衡器）独立全屏页面。
/// 打开：Shell.Current.Navigation.PushModalAsync —— Android 原生转场即从下往上滑入。
/// 关闭：Navigation.PopModalAsync —— 从上往下滑出。
/// 作为窗口级模态页面，不受 NowPlayingPage 内部 Grid 行约束裁切，可铺满整屏。
/// </summary>
public partial class EqualizerPage : ContentPage
{
    private readonly AudioPlayerService _audioPlayer;
    private double[] _eqLiveGains = EqualizerSettings.GetBandGains();
    private EqCurveDrawable? _eqCurveDrawable;
    private GraphicsView? _eqCurveView;
    private readonly List<(Label ValueLabel, BoxView Fill, Border Handle)> _eqBandControls = new();
    private ScrollView? _eqPresetScroll;
    private readonly List<Border> _eqPresetChips = new();
    private Grid? _eqBandsArea;

    public EqualizerPage()
    {
        InitializeComponent();
        _audioPlayer = IPlatformApplication.Current!.Services.GetRequiredService<AudioPlayerService>();
        BuildHeader();
        BuildEqualizerContent();
    }

    // ═══════════════════════════════════════
    // 头部：标题 + 关闭按钮
    // ═══════════════════════════════════════

    private void BuildHeader()
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];

        var titleStack = new VerticalStackLayout { Spacing = 2 };
        titleStack.Add(new Label { Text = "均衡器", FontSize = 20, FontAttributes = FontAttributes.Bold, TextColor = textPrimary });
        titleStack.Add(new Label { Text = "音效中心", FontSize = 12, TextColor = textHint });
        HeaderGrid.Add(titleStack, 0);

        var closeBtn = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(17) },
            StrokeThickness = 0,
            WidthRequest = 34, HeightRequest = 34,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "✕", FontSize = 15, TextColor = textSecondary,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        closeBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(async () => await CloseAsync()) });
        HeaderGrid.Add(closeBtn, 1);
    }

    private void AddContent(View v) => BodyStack.Add(v);
    private void ClearContent() => BodyStack.Clear();

    private async Task CloseAsync() => await Navigation.PopModalAsync();

    // ═══════════════════════════════════════
    // 均衡器主体
    // ═══════════════════════════════════════

    private void BuildEqualizerContent()
    {
        ClearContent();
        _eqBandControls.Clear();
        _eqPresetChips.Clear();

        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];

        // 主开关
        var masterEnabled = EqualizerSettings.Enabled;
        AddContent(CreateToggleRow("图形均衡器", "开启后按下方频段调节", masterEnabled, v =>
        {
            EqualizerSettings.Enabled = v;
            ApplyEqSettingsLive();
#if WINDOWS
            // Windows 端 EQ 切换需要更换播放管线（MediaPlayer ↔ AudioGraph），
            // 正在播放时从当前位置重载当前歌曲使其立即生效
            _ = RestartPlaybackForEqSwitchAsync();
#endif
        }, margin: new Thickness(0, 0, 0, 12)));

        // FFmpeg 精确均衡（10 段）：开启后绕过原生 5 段限制，由 FFmpeg 把 10 段烘焙进音频
        AddContent(CreateToggleRow("FFmpeg 精确均衡", "开启后使用 10 段精细调节（经 FFmpeg 烘焙）",
            EqualizerSettings.UseFFmpegEq, v =>
        {
            EqualizerSettings.UseFFmpegEq = v; // 自动在 5/10 段间重采样增益
            RebuildBandArea();                 // 重建频段滑块（数量变化）
            ApplyEqSettingsLive();             // 切换 原生EQ ↔ FFmpeg 烘焙 路径
        }, margin: new Thickness(0, 0, 0, 12)));

        // 预设横向滚动
        AddContent(new Label
        {
            Text = "预设", FontSize = 12, TextColor = textHint, Margin = new Thickness(2, 0, 0, 8)
        });
        _eqPresetScroll = new ScrollView
        {
            Orientation = ScrollOrientation.Horizontal,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Never,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var presetStack = new HorizontalStackLayout { Spacing = 8 };
        var currentPreset = EqualizerSettings.CurrentPreset;
        foreach (var (key, name) in EqualizerSettings.PresetList)
        {
            var chip = CreateChip(name, key == currentPreset, compact: true);
            _eqPresetChips.Add(chip);
            var capturedKey = key;
            chip.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() =>
                {
                    EqualizerSettings.ApplyPreset(capturedKey);
                    var gains = EqualizerSettings.GetBandGains();
                    Array.Copy(gains, _eqLiveGains, gains.Length);
                    RefreshEqBandUI();
                    HighlightPresetChip(_eqPresetChips.IndexOf(chip));
                    ApplyEqSettingsLive();
                })
            });
            presetStack.Add(chip);
        }
        _eqPresetScroll.Content = presetStack;
        AddContent(_eqPresetScroll);

        // 频段增益区域（曲线 + 滑块）
        AddContent(new Label
        {
            Text = "频段增益    −12 ~ +12 dB", FontSize = 12, TextColor = textHint,
            Margin = new Thickness(2, 0, 0, 6)
        });

        _eqBandsArea = new Grid
        {
            HeightRequest = 190,
            Margin = new Thickness(0, 0, 0, 8),
            IsClippedToBounds = true // 防止自绘曲线/节点溢出到预设区
        };
        AddContent(_eqBandsArea);
        BuildBandArea(textSecondary, primaryColor);

        // 音效增强
        AddContent(new Label
        {
            Text = "音效增强", FontSize = 12, TextColor = textHint, Margin = new Thickness(2, 4, 0, 8)
        });
        var enhFrame = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            StrokeThickness = 0,
            Padding = new Thickness(14, 6),
            Margin = new Thickness(0, 0, 0, 14)
        };
        var enhStack = new VerticalStackLayout { Spacing = 0 };

        enhStack.Add(CreateHSliderRow("低音增强", 0, 100, EqualizerSettings.BassBoost,
            v => { EqualizerSettings.BassBoost = (int)v; ApplyEqSettingsLive(); }, v => $"{(int)v}"));
        enhStack.Add(CreateHSliderRow("响度增益", 0, 100, EqualizerSettings.Loudness,
            v => { EqualizerSettings.Loudness = (int)v; ApplyEqSettingsLive(); }, v => $"{(int)v}"));
        // 空间音频（3D 环绕，Android 原生 Virtualizer 实现）
        enhStack.Add(CreateToggleRow("空间音频", "3D 环绕声场", EqualizerSettings.SpatialAudioEnabled,
            v => { EqualizerSettings.SpatialAudioEnabled = v; ApplyEqSettingsLive(); },
            margin: new Thickness(0, 8, 0, 0)));

        // 淡入淡出（曲目间交叉淡变）
        enhStack.Add(CreateToggleRow("淡入淡出", "曲目间交叉淡变", EqualizerSettings.CrossfadeEnabled,
            v => { EqualizerSettings.CrossfadeEnabled = v; },
            margin: new Thickness(0, 8, 0, 0)));

        // 淡入淡出时长
        enhStack.Add(CreateHSliderRow("淡入淡出时长", 0, 12, EqualizerSettings.CrossfadeDuration,
            v => { EqualizerSettings.CrossfadeDuration = (int)v; },
            v => $"{(int)v}s"));

        enhFrame.Content = enhStack;
        AddContent(enhFrame);

        // 底部按钮：重置 / 应用
        var footGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = 110 }, new() { Width = GridLength.Star } },
            ColumnSpacing = 10
        };
        var resetBtn = CreatePopupButton("重置", false);
        resetBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                EqualizerSettings.ApplyPreset("flat");
                var flatGains = EqualizerSettings.GetBandGains();
                Array.Copy(flatGains, _eqLiveGains, flatGains.Length);
                EqualizerSettings.BassBoost = 0;
                EqualizerSettings.Loudness = 0;
                EqualizerSettings.Balance = 0;
                RefreshEqBandUI();
                HighlightPresetChip(0);
                ApplyEqSettingsLive();
                BuildEqualizerContent(); // 刷新增强控件
            })
        });
        footGrid.Add(resetBtn, 0);

        var applyBtn = CreatePopupButton("应用", true);
        applyBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () =>
            {
                ApplyEqSettingsLive();
                await CloseAsync();
            })
        });
        footGrid.Add(applyBtn, 1);
        AddContent(footGrid);
    }

    /// <summary>在 _eqBandsArea 容器内（重建）频段曲线 + 滑块</summary>
    private void BuildBandArea(Color labelColor, Color accentColor)
    {
        _eqLiveGains = EqualizerSettings.GetBandGains();
        _eqBandControls.Clear();

        _eqCurveDrawable = new EqCurveDrawable(_eqLiveGains);
        _eqCurveView = new GraphicsView
        {
            Drawable = _eqCurveDrawable,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };

        var bandsGrid = new Grid { ColumnSpacing = 2 };
        for (int i = 0; i < EqualizerSettings.BandFrequencies.Length; i++)
            bandsGrid.ColumnDefinitions.Add(new ColumnDefinition());

        for (int i = 0; i < EqualizerSettings.BandFrequencies.Length; i++)
        {
            var bandView = CreateEqBandSlider(i, _eqLiveGains[i], labelColor, accentColor);
            bandsGrid.Add(bandView, i);
        }

        // 先加曲线作为背景，再加 bandsGrid 覆盖在上面，避免曲线节点溢出
        _eqBandsArea!.Clear();
        _eqBandsArea.Add(_eqCurveView);
        _eqBandsArea.Add(bandsGrid);
    }

    /// <summary>切换 5/10 段后重建频段区（保持当前增益曲线形状）</summary>
    private void RebuildBandArea()
    {
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];
        BuildBandArea(textSecondary, primaryColor);
    }

    /// <summary>创建单个频段竖向滑块</summary>
    private View CreateEqBandSlider(int bandIndex, double initialGain, Color labelColor, Color accentColor)
    {
        const double sliderHeight = 140;
        var min = EqualizerSettings.MinGainDb;
        var max = EqualizerSettings.MaxGainDb;

        var stack = new VerticalStackLayout { Spacing = 0, HorizontalOptions = LayoutOptions.Center };

        var sliderArea = new Grid
        {
            HeightRequest = sliderHeight,
            HorizontalOptions = LayoutOptions.Fill // 列宽内居中，避免固定 WidthRequest 被压缩后 thumb 偏左
        };

        // 轨道
        var track = new BoxView
        {
            WidthRequest = 5, CornerRadius = 3,
            Color = new Color(1, 1, 1, 0.10f),
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Fill
        };
        sliderArea.Add(track);

        // 填充（从底部向上）
        var fill = new BoxView
        {
            WidthRequest = 5, CornerRadius = 3,
            Color = accentColor,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End
        };
        sliderArea.Add(fill);

        // 手柄
        var handle = new Border
        {
            WidthRequest = 20, HeightRequest = 20,
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            BackgroundColor = Colors.White,
            StrokeThickness = 0,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.End,
            Shadow = new Shadow { Brush = new SolidColorBrush(Colors.Black), Radius = 6, Opacity = 0.4f, Offset = new Point(0, 2) }
        };
        sliderArea.Add(handle);

        // 数值标签
        var valLabel = new Label
        {
            FontSize = 10, FontAttributes = FontAttributes.Bold,
            TextColor = accentColor,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 0)
        };
        // 频率标签
        var hzLabel = new Label
        {
            Text = EqualizerSettings.BandLabels[bandIndex],
            FontSize = 10, TextColor = labelColor,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 1, 0, 0)
        };

        void UpdateVisual(double gain)
        {
            var frac = (gain - min) / (max - min);
            fill.HeightRequest = frac * sliderHeight;
            handle.TranslationY = -(frac * sliderHeight);
            valLabel.Text = (gain > 0 ? "+" : "") + gain.ToString("0");
        }

        UpdateVisual(initialGain);
        _eqBandControls.Add((valLabel, fill, handle));

        // 拖拽手势
        double gainAtStart = initialGain;
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    gainAtStart = _eqLiveGains[bandIndex];
                    break;
                case GestureStatus.Running:
                    var delta = -e.TotalY / sliderHeight * (max - min);
                    var newGain = Math.Clamp(Math.Round(gainAtStart + delta), min, max);
                    _eqLiveGains[bandIndex] = newGain;
                    UpdateVisual(newGain);
                    _eqCurveDrawable?.UpdateGains(_eqLiveGains);
                    _eqCurveView?.Invalidate();
                    break;
                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    // 手动调整 → 标记为自定义预设
                    EqualizerSettings.SetBandGains(_eqLiveGains);
                    EqualizerSettings.CurrentPreset = "custom";
                    HighlightPresetChip(-1);
                    ApplyEqSettingsLive();
                    break;
            }
        };
        sliderArea.GestureRecognizers.Add(pan);

        stack.Add(sliderArea);
        stack.Add(valLabel);
        stack.Add(hzLabel);
        return stack;
    }

    /// <summary>刷新所有频段滑块 UI（预设切换时）</summary>
    private void RefreshEqBandUI()
    {
        var min = EqualizerSettings.MinGainDb;
        var max = EqualizerSettings.MaxGainDb;
        const double sliderHeight = 140;

        for (int i = 0; i < _eqBandControls.Count && i < _eqLiveGains.Length; i++)
        {
            var (valLabel, fill, handle) = _eqBandControls[i];
            var gain = _eqLiveGains[i];
            var frac = (gain - min) / (max - min);
            fill.HeightRequest = frac * sliderHeight;
            handle.TranslationY = -(frac * sliderHeight);
            valLabel.Text = (gain > 0 ? "+" : "") + gain.ToString("0");
        }
        _eqCurveDrawable?.UpdateGains(_eqLiveGains);
        _eqCurveView?.Invalidate();
    }

    private void HighlightPresetChip(int activeIndex)
    {
        for (int i = 0; i < _eqPresetChips.Count; i++)
            SetChipActive(_eqPresetChips[i], i == activeIndex);
    }

    /// <summary>将当前设置应用到播放引擎（实时生效）</summary>
    private void ApplyEqSettingsLive()
    {
        EqualizerSettings.SetBandGains(_eqLiveGains);
        _audioPlayer.ApplyEqualizer();
    }

#if WINDOWS
    /// <summary>Windows 端切换 EQ 开关后，从当前位置重载播放以切换音频管线</summary>
    private async Task RestartPlaybackForEqSwitchAsync()
    {
        try
        {
            if (!_audioPlayer.IsPlaying) return;
            var path = _audioPlayer.CurrentSongFilePath;
            if (string.IsNullOrEmpty(path)) return;

            var position = _audioPlayer.CurrentPosition;
            await _audioPlayer.PlayAsync(path);
            if (position > 1)
                await _audioPlayer.SeekAsync(TimeSpan.FromSeconds(position));
        }
        catch (Exception ex)
        {
            Log.Debug("EqualizerPage", $"[EQ] Windows 管线切换失败: {ex.Message}");
        }
    }
#endif
}

// ═══════════════════════════════════════
// 自绘图形
// ═══════════════════════════════════════

/// <summary>均衡器频段曲线</summary>
public class EqCurveDrawable : IDrawable
{
    private readonly double[] _gains;

    public EqCurveDrawable(double[] gains)
    {
        _gains = gains;
    }

    public void UpdateGains(double[] gains)
    {
        Array.Copy(gains, _gains, Math.Min(gains.Length, _gains.Length));
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var w = dirtyRect.Width;
        var h = dirtyRect.Height - 44; // 底部留出标签空间
        var n = _gains.Length;
        if (n == 0 || w <= 0) return;

        var min = EqualizerSettings.MinGainDb;
        var max = EqualizerSettings.MaxGainDb;

        var points = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            var x = (i + 0.5f) / n * w;
            var frac = (float)((_gains[i] - min) / (max - min));
            var y = 8 + (1f - frac) * (h - 16);
            points[i] = new PointF(x, y);
        }

        // 渐变曲线
        canvas.StrokeSize = 2.5f;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeLineJoin = LineJoin.Round;
        canvas.StrokeColor = Color.FromArgb("#8C7BFF");
        for (int i = 0; i < n - 1; i++)
            canvas.DrawLine(points[i], points[i + 1]);

        // 不再画节点圆点：避免在 MAUI GraphicsView 上产生小白点溢出到预设区/滑轨左侧
    }
}

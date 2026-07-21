using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Services;
using CatClawMusic.Maui.Services.Equalizer;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

namespace CatClawMusic.Maui.Pages;

/// <summary>NowPlayingPage 底部操作栏功能：定时关闭 / 均衡器 / 切换横屏 / 更多</summary>
public partial class NowPlayingPage
{
    // ═══════════════════════════════════════
    // 定时关闭
    // ═══════════════════════════════════════

    private int _selectedTimerMinutes = 30;
    private bool _timerStopAfterSong = true;
    private bool _timerFadeOut;
    private Label? _timerCountdownLabel;
    private TimerRingDrawable? _timerRingDrawable;
    private GraphicsView? _timerRingView;

    /// <summary>点击定时关闭按钮</summary>
    private void OnSleepTimerClicked(object? sender, EventArgs e)
    {
        BuildSleepTimerContent();
        SleepTimerPopup.Open();
    }

    private void BuildSleepTimerContent()
    {
        SleepTimerPopup.ClearContent();
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];

        if (_sleepTimer.IsRunning)
        {
            BuildTimerActiveView(textPrimary, textSecondary, textHint);
            return;
        }

        // ─── 选择态 ───
        // 当前播放歌曲条
        var song = _viewModel.CurrentSong;
        if (song != null)
        {
            var nowGrid = new Grid
            {
                ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = 38 }, new() { Width = GridLength.Star } },
                ColumnSpacing = 10
            };
            var coverBorder = new Border
            {
                WidthRequest = 38, HeightRequest = 38,
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                StrokeThickness = 0,
                BackgroundColor = (Color)Application.Current!.Resources["ChipInactiveColor"]
            };
            if (_viewModel.CoverImage != null)
                coverBorder.Content = new Image { Source = _viewModel.CoverImage, Aspect = Aspect.AspectFill };
            nowGrid.Add(coverBorder, 0);

            var metaStack = new VerticalStackLayout
            {
                Spacing = 2,
                VerticalOptions = LayoutOptions.Center
            };
            metaStack.Add(new Label
            {
                Text = song.Title ?? "", FontSize = 13, FontAttributes = FontAttributes.Bold,
                TextColor = textPrimary, MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation
            });
            metaStack.Add(new Label
            {
                Text = song.Artist ?? "", FontSize = 11, TextColor = textSecondary,
                MaxLines = 1, LineBreakMode = LineBreakMode.TailTruncation
            });
            nowGrid.Add(metaStack, 1);

            var nowCard = new Border
            {
                BackgroundColor = new Color(1, 1, 1, 0.06f),
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
                StrokeThickness = 0,
                Padding = new Thickness(10),
                Margin = new Thickness(0, 0, 0, 14),
                Content = nowGrid
            };
            SleepTimerPopup.AddContent(nowCard);
        }

        // 停止时间标签
        SleepTimerPopup.AddContent(new Label
        {
            Text = "停止时间", FontSize = 12, TextColor = textHint,
            Margin = new Thickness(2, 0, 0, 8)
        });

        // 时间 Chips（3列网格）
        var chipsGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new(), new(), new() },
            RowDefinitions = new RowDefinitionCollection { new(), new() },
            ColumnSpacing = 8, RowSpacing = 8,
            Margin = new Thickness(0, 0, 0, 14)
        };
        var options = new (string Label, int Minutes)[]
        {
            ("15 分钟", 15), ("30 分钟", 30), ("45 分钟", 45),
            ("60 分钟", 60), ("90 分钟", 90), ("自定义…", 0)
        };
        var chipBorders = new List<Border>();
        for (int i = 0; i < options.Length; i++)
        {
            var (label, minutes) = options[i];
            var chip = CreateChip(label, minutes == _selectedTimerMinutes);
            chipBorders.Add(chip);
            var tap = new TapGestureRecognizer();
            var capturedMinutes = minutes;
            tap.Tapped += async (_, _) =>
            {
                if (capturedMinutes == 0)
                {
                    // 自定义时长
                    var input = await DisplayPromptAsync("自定义时长", "请输入分钟数（1-480）",
                        initialValue: "30", keyboard: Keyboard.Numeric, accept: "确定", cancel: "取消");
                    if (input != null && int.TryParse(input, out var custom) && custom is > 0 and <= 480)
                    {
                        _selectedTimerMinutes = custom;
                        UpdateChipStates(chipBorders, -1); // 全部取消高亮
                    }
                    return;
                }
                _selectedTimerMinutes = capturedMinutes;
                UpdateChipStates(chipBorders, chipBorders.IndexOf(chip));
            };
            chip.GestureRecognizers.Add(tap);
            chipsGrid.Add(chip, i % 3, i / 3);
        }
        SleepTimerPopup.AddContent(chipsGrid);

        // 选项开关
        SleepTimerPopup.AddContent(CreateToggleRow("播完当前歌曲后停止", "时间到后等当前曲目播完", _timerStopAfterSong,
            v => _timerStopAfterSong = v));
        SleepTimerPopup.AddContent(CreateToggleRow("结束时淡出音量", "最后 20 秒渐弱", _timerFadeOut,
            v => _timerFadeOut = v));

        // 底部按钮
        var footGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = 96 }, new() { Width = GridLength.Star } },
            ColumnSpacing = 10,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var cancelBtn = CreatePopupButton("取消", false);
        cancelBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SleepTimerPopup.Close()) });
        footGrid.Add(cancelBtn, 0);

        var startBtn = CreatePopupButton("开始定时", true);
        startBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                _sleepTimer.Start(_selectedTimerMinutes, _timerStopAfterSong, _timerFadeOut);
                BuildSleepTimerContent(); // 切换到进行中视图
            })
        });
        footGrid.Add(startBtn, 1);
        SleepTimerPopup.AddContent(footGrid);
    }

    /// <summary>定时进行中视图：倒计时环 + 剩余时间</summary>
    private void BuildTimerActiveView(Color textPrimary, Color textSecondary, Color textHint)
    {
        var stack = new VerticalStackLayout { Spacing = 0 };

        // 倒计时环（中心叠加剩余时间）
        _timerRingDrawable = new TimerRingDrawable();
        _timerRingView = new GraphicsView
        {
            Drawable = _timerRingDrawable,
            WidthRequest = 170, HeightRequest = 170,
            HorizontalOptions = LayoutOptions.Center
        };
        _timerCountdownLabel = new Label
        {
            Text = FormatTimerText(_sleepTimer.RemainingSeconds),
            FontSize = 30, FontAttributes = FontAttributes.Bold,
            TextColor = textPrimary,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        };
        var ringGrid = new Grid { WidthRequest = 170, HeightRequest = 170, HorizontalOptions = LayoutOptions.Center };
        ringGrid.Add(_timerRingView);
        ringGrid.Add(_timerCountdownLabel);
        UpdateTimerRing();
        stack.Add(ringGrid);

        stack.Add(new Label
        {
            Text = "后停止", FontSize = 12, TextColor = textHint,
            HorizontalTextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 4, 0, 12)
        });

        // 状态说明
        var summary = new List<string> { $"已设置 {_sleepTimer.TotalSeconds / 60} 分钟后停止" };
        if (_sleepTimer.StopAfterCurrentSong) summary.Add("· 播完当前歌曲");
        if (_sleepTimer.FadeOutEnabled) summary.Add("· 结束时淡出");
        if (_sleepTimer.IsWaitingForSongEnd) summary.Add("· 等待当前歌曲播完…");
        stack.Add(new Label
        {
            Text = string.Join("\n", summary),
            FontSize = 12, TextColor = textSecondary,
            HorizontalTextAlignment = TextAlignment.Center,
            LineHeight = 1.6,
            Margin = new Thickness(0, 0, 0, 14)
        });

        // 按钮：取消定时 / 完成
        var footGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = 110 }, new() { Width = GridLength.Star } },
            ColumnSpacing = 10
        };
        var cancelTimerBtn = CreatePopupButton("取消定时", false);
        cancelTimerBtn.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(() =>
            {
                _sleepTimer.Cancel();
                SleepTimerPopup.Close();
                UpdateTimerButtonState();
            })
        });
        footGrid.Add(cancelTimerBtn, 0);

        var doneBtn = CreatePopupButton("完成", true);
        doneBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => SleepTimerPopup.Close()) });
        footGrid.Add(doneBtn, 1);
        stack.Add(footGrid);

        SleepTimerPopup.AddContent(stack);

        // 订阅 Tick 更新
        _sleepTimer.Tick -= OnSleepTimerTick;
        _sleepTimer.Tick += OnSleepTimerTick;
        _sleepTimer.StateChanged -= OnSleepTimerStateChanged;
        _sleepTimer.StateChanged += OnSleepTimerStateChanged;
    }

    private void OnSleepTimerTick(object? sender, int remaining)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_timerCountdownLabel != null)
                _timerCountdownLabel.Text = FormatTimerText(remaining);
            UpdateTimerRing();
        });
    }

    private void OnSleepTimerStateChanged(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            UpdateTimerButtonState();
            if (!_sleepTimer.IsRunning)
            {
                _sleepTimer.Tick -= OnSleepTimerTick;
                _sleepTimer.StateChanged -= OnSleepTimerStateChanged;
                _timerCountdownLabel = null;
                _timerRingDrawable = null;
            }
        });
    }

    private void UpdateTimerRing()
    {
        if (_timerRingDrawable == null || _timerRingView == null) return;
        var total = _sleepTimer.TotalSeconds;
        _timerRingDrawable.Progress = total > 0 ? (float)_sleepTimer.RemainingSeconds / total : 0;
        _timerRingView.Invalidate();
    }

    private static string FormatTimerText(int seconds)
    {
        if (seconds < 0) seconds = 0;
        return $"{seconds / 60:D2}:{seconds % 60:D2}";
    }

    /// <summary>定时按钮激活状态（运行中显示主题色底）</summary>
    private void UpdateTimerButtonState()
    {
        try
        {
            var btn = BottomActionBar.Children.OfType<ImageButton>()
                .FirstOrDefault(b => Grid.GetColumn(b) == 1);
            if (btn != null)
                btn.BackgroundColor = _sleepTimer.IsRunning
                    ? ((Color)Application.Current!.Resources["PrimaryColor"]).WithAlpha(0.25f)
                    : Colors.Transparent;
        }
        catch { }
    }

    // ═══════════════════════════════════════
    // 均衡器
    // ═══════════════════════════════════════

    private readonly double[] _eqLiveGains = new double[EqualizerSettings.BandFrequencies.Length];
    private EqCurveDrawable? _eqCurveDrawable;
    private GraphicsView? _eqCurveView;
    private readonly List<(Label ValueLabel, BoxView Fill, Border Handle)> _eqBandControls = new();
    private ScrollView? _eqPresetScroll;
    private readonly List<Border> _eqPresetChips = new();

    /// <summary>点击均衡器按钮</summary>
    private void OnEqualizerClicked(object? sender, EventArgs e)
    {
        BuildEqualizerContent();
        EqualizerSheet.Open();
    }

    private void BuildEqualizerContent()
    {
        EqualizerSheet.ClearContent();
        _eqBandControls.Clear();
        _eqPresetChips.Clear();

        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];
        var textHint = (Color)Application.Current!.Resources["TextHintColor"];
        var primaryColor = (Color)Application.Current!.Resources["PrimaryColor"];

        // 标题行
        var headGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Star }, new() { Width = GridLength.Auto } },
            Margin = new Thickness(0, 0, 0, 10)
        };
        var titleStack = new VerticalStackLayout { Spacing = 1 };
        titleStack.Add(new Label { Text = "均衡器", FontSize = 17, FontAttributes = FontAttributes.Bold, TextColor = textPrimary });
        titleStack.Add(new Label { Text = "音效中心", FontSize = 11, TextColor = textHint });
        headGrid.Add(titleStack, 0);

        var closeBtn = new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(16) },
            StrokeThickness = 0,
            WidthRequest = 30, HeightRequest = 30,
            HorizontalOptions = LayoutOptions.End, VerticalOptions = LayoutOptions.Center,
            Content = new Label
            {
                Text = "\u2715", FontSize = 14, TextColor = textSecondary,
                HorizontalTextAlignment = TextAlignment.Center, VerticalTextAlignment = TextAlignment.Center
            }
        };
        closeBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => EqualizerSheet.Close()) });
        headGrid.Add(closeBtn, 1);
        EqualizerSheet.AddContent(headGrid);

        // 主开关
        var masterEnabled = EqualizerSettings.Enabled;
        EqualizerSheet.AddContent(CreateToggleRow("图形均衡器", "开启后按下方频段调节", masterEnabled, v =>
        {
            EqualizerSettings.Enabled = v;
            ApplyEqSettingsLive();
#if WINDOWS
            // Windows 端 EQ 切换需要更换播放管线（MediaPlayer ↔ AudioGraph），
            // 正在播放时从当前位置重载当前歌曲使其立即生效
            _ = RestartPlaybackForEqSwitchAsync();
#endif
        }, margin: new Thickness(0, 0, 0, 12)));

        // 预设横向滚动
        EqualizerSheet.AddContent(new Label
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
        EqualizerSheet.AddContent(_eqPresetScroll);

        // 频段增益区域（曲线 + 滑块）
        EqualizerSheet.AddContent(new Label
        {
            Text = "频段增益    −12 ~ +12 dB", FontSize = 12, TextColor = textHint,
            Margin = new Thickness(2, 0, 0, 6)
        });

        var gains = EqualizerSettings.GetBandGains();
        Array.Copy(gains, _eqLiveGains, gains.Length);

        _eqCurveDrawable = new EqCurveDrawable(_eqLiveGains);
        var eqArea = new Grid { HeightRequest = 190, Margin = new Thickness(0, 0, 0, 8) };
        _eqCurveView = new GraphicsView
        {
            Drawable = _eqCurveDrawable,
            HorizontalOptions = LayoutOptions.Fill,
            VerticalOptions = LayoutOptions.Fill,
            InputTransparent = true
        };
        eqArea.Add(_eqCurveView);

        var bandsGrid = new Grid { ColumnSpacing = 2 };
        for (int i = 0; i < EqualizerSettings.BandFrequencies.Length; i++)
            bandsGrid.ColumnDefinitions.Add(new ColumnDefinition());

        for (int i = 0; i < EqualizerSettings.BandFrequencies.Length; i++)
        {
            var bandView = CreateEqBandSlider(i, _eqLiveGains[i], textSecondary, primaryColor);
            bandsGrid.Add(bandView, i);
        }
        eqArea.Add(bandsGrid);
        EqualizerSheet.AddContent(eqArea);

        // 音效增强
        EqualizerSheet.AddContent(new Label
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
        enhStack.Add(CreateHSliderRow("左右平衡", -100, 100, EqualizerSettings.Balance,
            v => { EqualizerSettings.Balance = (int)v; },
            v => v == 0 ? "居中" : (v < 0 ? $"L{-(int)v}" : $"R{(int)v}")));

        enhFrame.Content = enhStack;
        EqualizerSheet.AddContent(enhFrame);

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
            Command = new Command(() =>
            {
                ApplyEqSettingsLive();
                EqualizerSheet.Close();
            })
        });
        footGrid.Add(applyBtn, 1);
        EqualizerSheet.AddContent(footGrid);
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
            WidthRequest = 36,
            HorizontalOptions = LayoutOptions.Center
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
            Log.Debug("NowPlayingPage", $"[EQ] Windows 管线切换失败: {ex.Message}");
        }
    }
#endif

    // ═══════════════════════════════════════
    // 切换横屏
    // ═══════════════════════════════════════

    private void OnRotateClicked(object? sender, EventArgs e)
    {
#if ANDROID
        var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
        if (activity == null) return;

        var current = activity.RequestedOrientation;
        if (current == global::Android.Content.PM.ScreenOrientation.Landscape ||
            current == global::Android.Content.PM.ScreenOrientation.SensorLandscape)
        {
            // 恢复为跟随传感器（竖屏优先）
            activity.RequestedOrientation = global::Android.Content.PM.ScreenOrientation.Unspecified;
        }
        else
        {
            activity.RequestedOrientation = global::Android.Content.PM.ScreenOrientation.SensorLandscape;
        }
#else
        // Windows/桌面端无横屏切换需求
        DisplayAlert("提示", "桌面端窗口可自由调整宽高，页面会自动适配布局", "知道了");
#endif
    }

    // ═══════════════════════════════════════
    // 更多
    // ═══════════════════════════════════════

    private async void OnMoreClicked(object? sender, EventArgs e)
    {
        var song = _viewModel.CurrentSong;
        if (song == null) return;

        var action = await DisplayActionSheet(song.Title ?? "歌曲操作", "取消", null,
            "加入歌单", "分享", "查看歌手", "查看专辑");

        switch (action)
        {
            case "加入歌单":
                await AddCurrentSongToPlaylistAsync(song);
                break;
            case "分享":
                await ShareSongAsync(song);
                break;
            case "查看歌手":
                if (!string.IsNullOrEmpty(song.Artist))
                    await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(song.Artist)}");
                break;
            case "查看专辑":
                if (!string.IsNullOrEmpty(song.Album))
                    await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(song.Album)}");
                break;
        }
    }

    /// <summary>选择歌单并添加当前歌曲</summary>
    private async Task AddCurrentSongToPlaylistAsync(Core.Models.Song song)
    {
        try
        {
            var playlists = await _musicLibrary.GetAllPlaylistsAsync();
            if (playlists == null || playlists.Count == 0)
            {
                await DisplayAlert("提示", "还没有歌单，请先在歌单页创建", "知道了");
                return;
            }

            var names = playlists.Select(p => p.Name ?? "未命名歌单").ToArray();
            var choice = await DisplayActionSheet("加入歌单", "取消", null, names);
            if (string.IsNullOrEmpty(choice) || choice == "取消") return;

            var playlist = playlists.FirstOrDefault(p => (p.Name ?? "未命名歌单") == choice);
            if (playlist == null) return;

            await _musicLibrary.AddSongToPlaylistAsync(playlist.Id, song.Id);
            await DisplayAlert("成功", $"已添加到「{choice}」", "好的");
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 加入歌单失败: {ex.Message}");
        }
    }

    /// <summary>分享当前歌曲</summary>
    private async Task ShareSongAsync(Core.Models.Song song)
    {
        try
        {
            var text = $"{song.Title} - {song.Artist}";
            if (!string.IsNullOrEmpty(song.Album))
                text += $"（{song.Album}）";
            text += " | 来自猫爪音乐";

            await Share.Default.RequestAsync(new ShareTextRequest
            {
                Text = text,
                Title = "分享歌曲"
            });
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 分享失败: {ex.Message}");
        }
    }

    // ═══════════════════════════════════════
    // 通用 UI 构建辅助
    // ═══════════════════════════════════════

    /// <summary>创建 Chip 按钮（圆角标签）</summary>
    private static Border CreateChip(string text, bool active, bool compact = false)
    {
        var chip = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            Padding = new Thickness(compact ? 13 : 0, compact ? 8 : 11),
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

    private static void SetChipActive(Border chip, bool active)
    {
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];
        if (active)
        {
            chip.Background = new LinearGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new(primary.WithAlpha(0.35f), 0f),
                    new(Color.FromArgb("#55D6FF").WithAlpha(0.25f), 1f)
                }
            };
            chip.Stroke = new SolidColorBrush(primary.WithAlpha(0.6f));
            chip.StrokeThickness = 1;
        }
        else
        {
            chip.BackgroundColor = new Color(1, 1, 1, 0.06f);
            chip.Stroke = new SolidColorBrush(new Color(1, 1, 1, 0.08f));
            chip.StrokeThickness = 1;
        }
    }

    private static void UpdateChipStates(List<Border> chips, int activeIndex)
    {
        for (int i = 0; i < chips.Count; i++)
            SetChipActive(chips[i], i == activeIndex);
    }

    /// <summary>创建开关选项行</summary>
    private static View CreateToggleRow(string title, string description, bool initial, Action<bool> onChanged, Thickness? margin = null)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Star }, new() { Width = GridLength.Auto } },
            ColumnSpacing = 10
        };

        var textStack = new VerticalStackLayout { Spacing = 1, VerticalOptions = LayoutOptions.Center };
        textStack.Add(new Label
        {
            Text = title, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"]
        });
        textStack.Add(new Label
        {
            Text = description, FontSize = 11,
            TextColor = (Color)Application.Current!.Resources["TextSecondaryColor"]
        });
        grid.Add(textStack, 0);

        var toggle = new Switch
        {
            IsToggled = initial,
            VerticalOptions = LayoutOptions.Center,
            OnColor = (Color)Application.Current!.Resources["PrimaryColor"]
        };
        toggle.Toggled += (_, e) => onChanged(e.Value);
        grid.Add(toggle, 1);

        return new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.06f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            StrokeThickness = 0,
            Padding = new Thickness(12, 11),
            Margin = margin ?? new Thickness(0, 0, 0, 8),
            Content = grid
        };
    }

    /// <summary>创建弹窗底部按钮（渐变主按钮 / 玻璃幽灵按钮）</summary>
    private static Border CreatePopupButton(string text, bool primary)
    {
        var btn = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            HeightRequest = 48,
            HorizontalOptions = LayoutOptions.Fill,
            Content = new Label
            {
                Text = text,
                FontSize = 14,
                FontAttributes = FontAttributes.Bold,
                TextColor = primary ? Color.FromArgb("#080B1A") : (Color)Application.Current!.Resources["TextPrimaryColor"],
                HorizontalTextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center
            }
        };

        if (primary)
        {
            var c1 = (Color)Application.Current!.Resources["PrimaryColor"];
            btn.Background = new LinearGradientBrush
            {
                GradientStops = new GradientStopCollection
                {
                    new(c1, 0f),
                    new(Color.FromArgb("#55D6FF"), 1f)
                }
            };
            btn.StrokeThickness = 0;
        }
        else
        {
            btn.BackgroundColor = new Color(1, 1, 1, 0.06f);
            btn.Stroke = new SolidColorBrush(new Color(1, 1, 1, 0.14f));
            btn.StrokeThickness = 1;
        }
        return btn;
    }

    /// <summary>创建横向滑块行（音效增强用）</summary>
    private static View CreateHSliderRow(string label, double min, double max, double initial,
        Action<double> onChanged, Func<double, string> format)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = 70 }, new() { Width = GridLength.Star }, new() { Width = 42 }
            },
            ColumnSpacing = 10,
            Padding = new Thickness(0, 7)
        };

        grid.Add(new Label
        {
            Text = label, FontSize = 13, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
            VerticalTextAlignment = TextAlignment.Center
        }, 0);

        var valueLabel = new Label
        {
            Text = format(initial), FontSize = 12, FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["PrimaryColor"],
            HorizontalTextAlignment = TextAlignment.End,
            VerticalTextAlignment = TextAlignment.Center
        };

        var slider = new Slider
        {
            Minimum = min, Maximum = max, Value = initial,
            MinimumTrackColor = (Color)Application.Current!.Resources["PrimaryColor"],
            MaximumTrackColor = new Color(1, 1, 1, 0.12f),
            ThumbColor = Colors.White,
            VerticalOptions = LayoutOptions.Center
        };
        slider.ValueChanged += (_, e) =>
        {
            valueLabel.Text = format(e.NewValue);
            onChanged(e.NewValue);
        };
        grid.Add(slider, 1);
        grid.Add(valueLabel, 2);
        return grid;
    }
}

// ═══════════════════════════════════════
// 自绘图形
// ═══════════════════════════════════════

/// <summary>定时关闭倒计时环</summary>
public class TimerRingDrawable : IDrawable
{
    /// <summary>剩余比例 0~1</summary>
    public float Progress { get; set; } = 1f;

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        var cx = dirtyRect.Width / 2;
        var cy = dirtyRect.Height / 2;
        var r = Math.Min(cx, cy) - 10;

        // 轨道
        canvas.StrokeColor = new Color(1, 1, 1, 0.12f);
        canvas.StrokeSize = 9;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.DrawEllipse(cx - r, cy - r, r * 2, r * 2);

        // 进度弧（从12点方向顺时针）
        if (Progress > 0.001f)
        {
            var sweep = Progress * 360f;
            canvas.StrokeColor = Color.FromArgb("#8C7BFF");
            canvas.StrokeSize = 9;
            canvas.StrokeLineCap = LineCap.Round;
            // DrawArc: 角度0=3点钟方向，逆时针为正 → 从90°(12点)开始，顺时针扫过 sweep
            canvas.DrawArc(cx - r, cy - r, r * 2, r * 2, 90f, 90f - sweep, true, false);
        }
    }
}

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

        // 节点圆点
        canvas.FillColor = Color.FromArgb("#55D6FF");
        foreach (var p in points)
            canvas.FillEllipse(p.X - 3, p.Y - 3, 6, 6);
    }
}

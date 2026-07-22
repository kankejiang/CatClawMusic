using CatClawMusic.Core.Interfaces;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Services;
using static CatClawMusic.Maui.Controls.PopupUiHelpers;
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
    // 自定义时长内联输入面板（替代 DisplayPromptAsync，避免 ViewPager2 承载页上弹模态崩溃）
    private View? _timerCustomPanel;
    private Entry? _timerCustomEntry;
    private List<Border>? _timerChipBorders;

    /// <summary>点击定时关闭按钮</summary>
    private void OnSleepTimerClicked(object? sender, EventArgs e)
    {
        BuildSleepTimerContent();
        SleepTimerPopup.Open();
    }

    /// <summary>点击均衡器按钮：弹出独立全屏音效中心页面（从下往上滑入，关闭时从上往下滑出）。</summary>
    private async void OnEqualizerClicked(object? sender, EventArgs e)
    {
        var eqPage = new EqualizerPage();
        if (Shell.Current?.Navigation is { } nav)
            await nav.PushModalAsync(eqPage);
        else
            await Navigation.PushModalAsync(eqPage);
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
        _timerChipBorders = chipBorders;
        for (int i = 0; i < options.Length; i++)
        {
            var (label, minutes) = options[i];
            var chip = CreateChip(label, minutes == _selectedTimerMinutes);
            chipBorders.Add(chip);
            var tap = new TapGestureRecognizer();
            var capturedMinutes = minutes;
            tap.Tapped += (_, _) =>
            {
                if (capturedMinutes == 0)
                {
                    // 自定义时长：展开底部弹层内的内联输入（不再用 DisplayPromptAsync，
                    // 避免 NowPlayingPage 被 ViewPager2 承载时弹模态找不到宿主窗口而崩溃）
                    ShowCustomDurationPanel();
                    return;
                }
                // 选择预设：取消其他按钮高亮并收起自定义面板
                if (_timerCustomPanel != null) _timerCustomPanel.IsVisible = false;
                _selectedTimerMinutes = capturedMinutes;
                UpdateChipStates(chipBorders, chipBorders.IndexOf(chip));
            };
            chip.GestureRecognizers.Add(tap);
            chipsGrid.Add(chip, i % 3, i / 3);
        }
        SleepTimerPopup.AddContent(chipsGrid);

        // 自定义时长内联面板（默认隐藏，点击“自定义…”展开）
        _timerCustomPanel = BuildCustomDurationPanel();
        SleepTimerPopup.AddContent(_timerCustomPanel);

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

    /// <summary>展开“自定义时长”内联面板，预填当前/默认分钟数，并取消预设高亮。</summary>
    private void ShowCustomDurationPanel()
    {
        if (_timerChipBorders != null)
            UpdateChipStates(_timerChipBorders, -1); // 进入自定义态：取消所有预设高亮
        if (_timerCustomEntry != null)
            _timerCustomEntry.Text = _selectedTimerMinutes > 0 ? _selectedTimerMinutes.ToString() : "30";
        if (_timerCustomPanel != null)
        {
            _timerCustomPanel.IsVisible = true;
            _timerCustomEntry?.Focus();
        }
    }

    /// <summary>构建“自定义时长”内联输入面板（隐藏态）：用弹层内 Entry 替代 DisplayPromptAsync 模态弹窗。</summary>
    private View BuildCustomDurationPanel()
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];

        var panel = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(14) },
            StrokeThickness = 0,
            Background = new SolidColorBrush(new Color(1, 1, 1, 0.06f)),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 14),
            IsVisible = false
        };
        var stack = new VerticalStackLayout { Spacing = 10 };

        stack.Add(new Label
        {
            Text = "自定义时长（1 - 480 分钟）",
            FontSize = 12, TextColor = textSecondary
        });

        _timerCustomEntry = new Entry
        {
            Keyboard = Keyboard.Numeric,
            Text = _selectedTimerMinutes > 0 ? _selectedTimerMinutes.ToString() : "30",
            TextColor = textPrimary,
            Background = new SolidColorBrush(new Color(0, 0, 0, 0.28f)),
            HorizontalTextAlignment = TextAlignment.Center
        };
        stack.Add(_timerCustomEntry);

        var btnGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection { new() { Width = GridLength.Star }, new() { Width = 10 }, new() { Width = GridLength.Star } }
        };
        var confirm = CreatePopupButton("确定", true);
        confirm.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(ConfirmCustomDuration) });
        var cancel = CreatePopupButton("取消", false);
        cancel.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() =>
        {
            if (_timerCustomPanel != null) _timerCustomPanel.IsVisible = false;
        }) });
        btnGrid.Add(confirm, 0);
        btnGrid.Add(cancel, 2);
        stack.Add(btnGrid);

        panel.Content = stack;
        return panel;
    }

    /// <summary>确认自定义时长：校验 1-480，写入选中值并高亮“自定义…”芯片；非法输入仅收起面板保留原选择。</summary>
    private void ConfirmCustomDuration()
    {
        var raw = _timerCustomEntry?.Text?.Trim();
        if (!int.TryParse(raw, out var custom) || custom is <= 0 or > 480)
        {
            if (_timerCustomPanel != null) _timerCustomPanel.IsVisible = false;
            if (_timerChipBorders != null)
                UpdateChipStates(_timerChipBorders, IndexOfMinutes(_selectedTimerMinutes));
            return;
        }
        _selectedTimerMinutes = custom;
        if (_timerCustomPanel != null) _timerCustomPanel.IsVisible = false;
        // 高亮最后一个（“自定义…”）芯片，表示当前为自定义时长
        if (_timerChipBorders != null)
            UpdateChipStates(_timerChipBorders, _timerChipBorders.Count - 1);
    }

    /// <summary>将分钟数映射回 options 数组下标（15,30,45,60,90,0）；自定义值返回 -1。</summary>
    private static int IndexOfMinutes(int minutes) => minutes switch
    {
        15 => 0, 30 => 1, 45 => 2, 60 => 3, 90 => 4, 0 => 5, _ => -1
    };

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
            Controls.NativeTabPager.SetSwipeEnabled(true);
        }
        else
        {
            activity.RequestedOrientation = global::Android.Content.PM.ScreenOrientation.SensorLandscape;
            Controls.NativeTabPager.SetSwipeEnabled(false);
        }
#else
        // Windows/桌面端无横屏切换需求
        DisplayAlert("提示", "桌面端窗口可自由调整宽高，页面会自动适配布局", "知道了");
#endif
    }

    // ═══════════════════════════════════════
    // 更多
    // ═══════════════════════════════════════

    private void OnMoreClicked(object? sender, EventArgs e)
    {
        var song = _viewModel.CurrentSong;
        if (song == null) return;

        var items = new List<VirtualizedSelectItem>();

        if (!string.IsNullOrEmpty(song.Artist))
            items.Add(new VirtualizedSelectItem
            {
                Icon = "♪",
                Text = "查看歌手",
                OnSelected = _ => NavigateToArtist(song.Artist!)
            });
        if (!string.IsNullOrEmpty(song.Album))
            items.Add(new VirtualizedSelectItem
            {
                Icon = "◉",
                Text = "查看专辑",
                OnSelected = _ => NavigateToAlbum(song.Album!)
            });

        // 加入歌单：进入子列表（KeepOpen 不关闭，可点返回）
        items.Add(new VirtualizedSelectItem
        {
            Icon = "＋",
            Text = "加入歌单",
            TrailingIcon = "›",
            KeepOpen = true,
            OnSelected = _ => OpenPlaylistPickerFor(song)
        });

        items.Add(new VirtualizedSelectItem
        {
            Icon = "↗",
            Text = "分享",
            OnSelected = _ => ShareCurrent(song)
        });

        VsMore.Show(items, song.Title ?? "歌曲操作");
    }

    private void NavigateToArtist(string artist)
        => _ = Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist)}");

    private void NavigateToAlbum(string album)
        => _ = Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album)}");

    private void OpenPlaylistPickerFor(Core.Models.Song song)
        => _ = ShowPlaylistPickerAsync(song);

    private void ShareCurrent(Core.Models.Song song)
        => _ = ShareSongAsync(song);

    /// <summary>在虚拟化选择器中进入歌单子列表（保留主菜单可返回）。</summary>
    private async Task ShowPlaylistPickerAsync(Core.Models.Song song)
    {
        try
        {
            var playlists = await _musicLibrary.GetAllPlaylistsAsync();

            if (playlists == null || playlists.Count == 0)
            {
                VsMore.ReplaceItems(new List<VirtualizedSelectItem>
                {
                    new()
                    {
                        Icon = "♫",
                        Text = "还没有歌单",
                        Subtitle = "请先在歌单页创建歌单后再添加",
                        KeepOpen = true
                    }
                }, "加入歌单");
                return;
            }

            var items = playlists.Select(p => new VirtualizedSelectItem
            {
                Icon = "♫",
                Text = p.Name ?? "未命名歌单",
                TrailingIcon = "＋",
                KeepOpen = true,
                OnSelected = _ => AddSongToPlaylist(p.Id, p.Name ?? "未命名歌单", song)
            }).ToList();

            VsMore.PushItems(items, "加入歌单");
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 加载歌单失败: {ex.Message}");
            VsMore.Close();
        }
    }

    /// <summary>将当前歌曲加入指定歌单，并在选择器内显示成功态后自动收起。</summary>
    private async void AddSongToPlaylist(int playlistId, string playlistName, Core.Models.Song song)
    {
        try
        {
            await _musicLibrary.AddSongToPlaylistAsync(playlistId, song.Id);
            VsMore.ReplaceItems(new List<VirtualizedSelectItem>
            {
                new()
                {
                    Icon = "✓",
                    Text = $"已添加到「{playlistName}」",
                    KeepOpen = true
                }
            }, "添加成功");

            MainThread.BeginInvokeOnMainThread(async () =>
            {
                await Task.Delay(900);
                VsMore.Close();
            });
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 加入歌单失败: {ex.Message}");
            VsMore.Close();
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


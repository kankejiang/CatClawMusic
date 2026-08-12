using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.ApplicationModel.DataTransfer;
using Microsoft.Maui.Storage;
using static CatClawMusic.Maui.Controls.PopupUiHelpers;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;
using System.Collections.Generic;
using System.IO;

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

    /// <summary>点击音效按钮：弹出独立全屏音效页面（虚拟环绕声/低音/响度/淡入淡出等，内含均衡器入口）。</summary>
    private async void OnEqualizerClicked(object? sender, EventArgs e)
    {
        var fxPage = new SoundEffectsPage();
        if (DesktopNavigation.TryGetShell()?.Navigation is { } nav)
            await nav.PushModalAsync(fxPage);
        else
            await Navigation.PushModalAsync(fxPage);
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
        cancelBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { _ = SleepTimerPopup.CloseAsync(); }) });
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
                _ = SleepTimerPopup.CloseAsync();
                UpdateTimerButtonState();
            })
        });
        footGrid.Add(cancelTimerBtn, 0);

        var doneBtn = CreatePopupButton("完成", true);
        doneBtn.GestureRecognizers.Add(new TapGestureRecognizer { Command = new Command(() => { _ = SleepTimerPopup.CloseAsync(); }) });
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
        if (Application.Current is not App app) return;

        bool goingToLandscape = !app.ManualLandscape;
        app.ToggleManualLandscape();

        if (goingToLandscape)
        {
            // ForceLandscape 内部通过 BeginInvokeOnMainThread 延迟切 Shell 根页面，
            // 这里再排一帧：Shell 切完 DesktopMainPage 后把播放页推到导航栈顶，
            // 用户不会跳到发现页。
            MainThread.BeginInvokeOnMainThread(() =>
            {
                var page = MauiProgram.Services.GetRequiredService<NowPlayingPage>();
                if (DesktopNavigation.TryGetShell()?.Navigation is { } nav)
                    _ = nav.PushAsync(page);
            });
        }
#endif
    }

    // ═══════════════════════════════════════
    // 更多
    // ═══════════════════════════════════════

    private void OnMoreClicked(object? sender, EventArgs e)
    {
        var song = _viewModel.CurrentSong;
        if (song == null) return;

        BuildMoreMenu(song);
        MorePopup.Open();
    }

    /// <summary>构建「更多」主菜单（查看歌手/专辑、加入歌单、分享）。</summary>
    private void BuildMoreMenu(Core.Models.Song song)
    {
        MorePopup.Title = "歌曲操作";
        MorePopup.ClearContent();

        var menu = new VerticalStackLayout { Spacing = 0 };

        if (!string.IsNullOrEmpty(song.Artist))
            menu.Add(CreateMenuRow("♪", "查看歌手", false, () =>
            {
                _ = MorePopup.CloseAsync();
                NavigateToArtist(song.Artist!);
            }));

        if (!string.IsNullOrEmpty(song.Album))
            menu.Add(CreateMenuRow("◉", "查看专辑", false, () =>
            {
                _ = MorePopup.CloseAsync();
                NavigateToAlbum(song.Album!);
            }));

        menu.Add(CreateMenuRow("＋", "加入歌单", true, () => BuildPlaylistPicker(song)));
        menu.Add(CreateMenuRow("↗", "分享", false, () =>
        {
            _ = MorePopup.CloseAsync();
                _ = ShareSongAsync(song);
        }));

        MorePopup.AddContent(menu);
    }

    /// <summary>创建一行可点击菜单项（图标 + 文字 + 可选右侧箭头）。</summary>
    private View CreateMenuRow(string icon, string text, bool showChevron, Action onTap)
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = 32 },
                new() { Width = GridLength.Star },
                new() { Width = GridLength.Auto }
            },
            ColumnSpacing = 12,
            HeightRequest = 52,
            Padding = new Thickness(4, 0)
        };

        row.Add(new Label
        {
            Text = icon,
            FontSize = 20,
            TextColor = (Color)Application.Current!.Resources["PrimaryColor"],
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }, 0);

        row.Add(new Label
        {
            Text = text,
            FontSize = 15,
            TextColor = textPrimary,
            VerticalTextAlignment = TextAlignment.Center
        }, 1);

        if (showChevron)
        {
            row.Add(new Label
            {
                Text = "›",
                FontSize = 22,
                TextColor = textSecondary,
                HorizontalTextAlignment = TextAlignment.End,
                VerticalTextAlignment = TextAlignment.Center
            }, 2);
        }

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => onTap();
        row.GestureRecognizers.Add(tap);

        return new Border
        {
            BackgroundColor = new Color(1, 1, 1, 0.04f),
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(12) },
            StrokeThickness = 0,
            Padding = new Thickness(12, 0),
            Margin = new Thickness(0, 0, 0, 8),
            Content = row
        };
    }

    /// <summary>进入「加入歌单」子视图：列出歌单，点击即加入。</summary>
    private async void BuildPlaylistPicker(Core.Models.Song song)
    {
        MorePopup.Title = "加入歌单";
        MorePopup.ClearContent();

        // 返回头部
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = GridLength.Star }
            },
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 12)
        };
        var back = new Label
        {
            Text = "‹ 返回",
            FontSize = 14,
            TextColor = (Color)Application.Current!.Resources["PrimaryColor"],
            VerticalTextAlignment = TextAlignment.Center
        };
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (_, _) => BuildMoreMenu(song);
        back.GestureRecognizers.Add(backTap);
        header.Add(back, 0);
        header.Add(new Label
        {
            Text = "加入歌单",
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
            VerticalTextAlignment = TextAlignment.Center
        }, 1);
        MorePopup.AddContent(header);

        try
        {
            var playlists = await _musicLibrary.GetAllPlaylistsAsync();
            if (playlists == null || playlists.Count == 0)
            {
                MorePopup.AddContent(new Label
                {
                    Text = "还没有歌单，请先在歌单页创建歌单",
                    FontSize = 13,
                    TextColor = (Color)Application.Current!.Resources["TextSecondaryColor"],
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            var list = new VerticalStackLayout { Spacing = 0 };
            foreach (var p in playlists)
            {
                var capturedName = p.Name ?? "未命名歌单";
                list.Add(CreateMenuRow("♫", capturedName, true,
                    () => AddSongToPlaylist(p.Id, capturedName, song)));
            }

            MorePopup.AddContent(new ScrollView
            {
                MaximumHeightRequest = 360,
                Content = list
            });
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 加载歌单失败: {ex.Message}");
            await MorePopup.CloseAsync();
        }
    }

    /// <summary>将当前歌曲加入指定歌单，并切换为成功态。</summary>
    private async void AddSongToPlaylist(int playlistId, string playlistName, Core.Models.Song song)
    {
        try
        {
            await _musicLibrary.AddSongToPlaylistAsync(playlistId, song.Id);

            MorePopup.ClearContent();
            MorePopup.AddContent(new Label
            {
                Text = $"✓ 已添加到「{playlistName}」",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
                Margin = new Thickness(0, 6, 0, 14)
            });

            var okBtn = CreatePopupButton("完成", true);
            okBtn.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => { _ = MorePopup.CloseAsync(); })
            });
            MorePopup.AddContent(okBtn);
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 加入歌单失败: {ex.Message}");
            await MorePopup.CloseAsync();
        }
    }

    private void NavigateToArtist(string artist)
    {
        if (DesktopNavigation.TryGoToShell($"artistdetail?artistName={Uri.EscapeDataString(artist)}")) return;
        DesktopNavigation.OpenArtistDetail(artist);
    }

    private void NavigateToAlbum(string album)
    {
        if (DesktopNavigation.TryGoToShell($"albumdetail?title={Uri.EscapeDataString(album)}")) return;
        DesktopNavigation.OpenAlbumDetail(album);
    }

    /// <summary>分享音频文件：本地歌曲直接分享文件；网络歌曲先下载到本地缓存再以文件分享。</summary>
    private async Task ShareSongAsync(Song song)
    {
        try
        {
            // 清理上一次分享遗留的 MAUI 中转缓存（只删上次的，不会动本次即将创建的文件）。
            // 注意：绝不能等 RequestAsync 返回后再清——接收端 App 是异步读取 content URI 的，
            // 那样会误删正在被读取的文件，导致接收端报"文件不存在"。
            CleanupShareStagingCache();

            string? filePath = await GetShareFilePathAsync(song);
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                // 兜底：无法取到文件时，仅分享文字信息
                var text = $"{song.Title} - {song.Artist}";
                if (!string.IsNullOrEmpty(song.Album)) text += $"（{song.Album}）";
                text += " | 来自猫爪音乐";
                await Share.Default.RequestAsync(new ShareTextRequest { Text = text, Title = "分享歌曲" });
                return;
            }

            var shareName = System.IO.Path.GetFileName(filePath);
            Log.Debug("NowPlayingPage", $"[More] 分享文件名='{shareName}' 源='{song.FilePath}' Source={song.Source} Title='{song.Title}'");
            ShowToast($"发送端文件名：{shareName}");

            await Share.Default.RequestAsync(new ShareFileRequest
            {
                Title = "分享音频文件",
                File = new ShareFile(filePath)
            });
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 分享失败: {ex.Message}");
        }
    }

    /// <summary>清理 MAUI Share 框架遗留的中转缓存子目录。
    /// MAUI Essentials 的 FileProvider 在执行 ShareFileRequest 时会将文件复制到
    /// &lt;cache&gt;/&lt;固定provider-uuid(32位hex)&gt;/&lt;每次随机uuid&gt;/ 下，且不会自动清理。
    /// 注意：MAUI 实际把中转文件放在 <b>external cache</b>
    /// （/storage/emulated/0/Android/data/包名/cache/），而不是 FileSystem.CacheDirectory
    /// 返回的 internal cache（/data/data/包名/cache/）。两者是不同的目录，
    /// 早期版本只扫 internal cache 导致清理从未命中，这里两个目录都扫。</summary>
    internal static void CleanupShareStagingCache()
    {
        try
        {
            // 候选中转根：internal cache + external cache（MAUI 真实所在）
            var roots = new List<string> { FileSystem.CacheDirectory };
#if ANDROID
            try
            {
                // 本绑定版本无 GetExternalCacheDir()，改用已可用的 GetExternalFilesDir 推导：
                // 外部文件根 .../Android/data/包名/files → 外部缓存根 .../Android/data/包名/cache
                var extFiles = Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath;
                if (!string.IsNullOrEmpty(extFiles))
                {
                    var extCache = System.IO.Path.Combine(
                        System.IO.Path.GetDirectoryName(extFiles)!, "cache");
                    if (!string.IsNullOrEmpty(extCache)) roots.Add(extCache);
                }
            }
            catch { /* 拿不到 external cache 就只用 internal */ }
#endif
            foreach (var root in roots)
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                ScanAndPurgeStaging(root);
            }
        }
        catch { /* 清理失败静默忽略 */ }
    }

    /// <summary>扫描某个 cache 根目录，删除所有 MAUI provider staging 子目录下的随机子目录。</summary>
    private static void ScanAndPurgeStaging(string cacheRoot)
    {
        foreach (var dir in Directory.GetDirectories(cacheRoot))
        {
            var name = System.IO.Path.GetFileName(dir);
            // MAUI provider staging 根目录特征：32位纯十六进制字符串
            if (name.Length == 32 && IsHexString(name))
            {
                foreach (var sub in Directory.GetDirectories(dir))
                {
                    try { Directory.Delete(sub, recursive: true); }
                    catch { /* 个别文件被占用就跳过 */ }
                }
            }
        }
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        return true;
    }

    /// <summary>解析可分享的本地文件路径：本地歌曲返回其文件；网络歌曲返回（已缓存或下载得到的）本地副本。</summary>
    private async Task<string?> GetShareFilePathAsync(Song song)
    {
        // 本地歌曲：复制成"标题+扩展名"的干净文件名再分享（源文件名可能带 " (1)" 等后缀）
        if (song.Source == SongSource.Local)
        {
            if (string.IsNullOrEmpty(song.FilePath) || !File.Exists(song.FilePath))
                return null;
            return CopyToShareDir(song, song.FilePath);
        }

        var networkSvc = MauiProgram.Services.GetService<INetworkMusicService>();
        if (networkSvc == null) return null;

        // 已缓存的网络音频：复制成带正确文件名的本地副本（缓存文件名为哈希，不直接分享）
        var cached = AudioCacheService.Instance.GetCachedPath(song.FilePath);
        if (cached != null && File.Exists(cached))
        {
            var bytes = await File.ReadAllBytesAsync(cached);
            return WriteShareFile(song, bytes);
        }

        // 未缓存：按协议下载
        var profiles = await networkSvc.GetProfilesAsync();
        var profile = profiles.FirstOrDefault(p =>
            (p.Protocol == ProtocolType.SMB && song.Source == SongSource.SMB) ||
            (p.Protocol == ProtocolType.WebDAV && song.Source == SongSource.WebDAV) ||
            ((p.Protocol == ProtocolType.Navidrome) && (song.Source == SongSource.WebDAV || song.Source == SongSource.Cache)));

        if (profile == null) return null;

        ShowToast("正在准备分享文件…");
        using var stream = await networkSvc.OpenAudioStreamAsync(song, profile);
        if (stream == null) return null;

        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms);
        return WriteShareFile(song, ms.ToArray());
    }

    /// <summary>把音频字节写入分享缓存目录，文件名取歌曲标题 + 原扩展名（清理非法字符）。</summary>
    private static string? WriteShareFile(Song song, byte[] data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var path = BuildSharePath(song, song.FilePath);
            if (path == null) return null;
            File.WriteAllBytes(path, data);
            return path;
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 写入分享文件失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>把本地源文件复制到分享缓存目录（重命名为干净的"标题+扩展名"）。</summary>
    private static string? CopyToShareDir(Song song, string sourcePath)
    {
        try
        {
            var path = BuildSharePath(song, sourcePath);
            if (path == null) return null;
            File.Copy(sourcePath, path, overwrite: true);
            return path;
        }
        catch (Exception ex)
        {
            Log.Debug("NowPlayingPage", $"[More] 复制分享文件失败: {ex.Message}");
            // 复制失败（如空间不足）时退回直接分享源文件
            return sourcePath;
        }
    }

    /// <summary>生成分享文件的目标路径：每次用一个全新的空子目录，返回"标题+原扩展名"（清理非法字符）。
    /// 用全新空目录可从根本上杜绝"同目录已存在同名文件 → 被系统去重加 (1)(2)(3)"。</summary>
    private static string? BuildSharePath(Song song, string? sourcePath)
    {
        var root = System.IO.Path.Combine(FileSystem.AppDataDirectory, "share_audio");

        // 彻底清掉旧的分享根目录（含所有历史子目录/文件），防堆积；失败则忽略
        try { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); } catch { }

        // 每次用一个全新的空子目录（GUID），里面绝不可能有同名文件可供去重
        var dir = System.IO.Path.Combine(root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        var ext = System.IO.Path.GetExtension(sourcePath);
        if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".audio";

        var name = string.IsNullOrWhiteSpace(song.Title) ? "audio" : song.Title.Trim();
        // 去掉标题末尾的 " (1)" / "（1）" / "(1 )" 等重复序号后缀（半/全角、允许内部空格）
        name = System.Text.RegularExpressions.Regex.Replace(name, @"\s*[\(（]\s*\d+\s*[\)）]\s*$", "");
        if (string.IsNullOrWhiteSpace(name)) name = "audio";
        foreach (var c in System.IO.Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        if (name.Length > 60) name = name[..60];

        return System.IO.Path.Combine(dir, $"{name}{ext}");
    }

    /// <summary>简短提示（Android 原生 Toast；其它平台无操作）。</summary>
    private static void ShowToast(string message)
    {
#if ANDROID
        try
        {
            var ctx = Android.App.Application.Context;
            var toast = Android.Widget.Toast.MakeText(ctx, message, Android.Widget.ToastLength.Short);
            toast.Show();
        }
        catch { }
#endif
    }

    // ═══════════════════════════════════════
    // 通用 UI 构建辅助
    // ═══════════════════════════════════════

}

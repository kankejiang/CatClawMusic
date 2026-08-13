using CatClawMusic.Core.Models;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 歌曲上下文菜单宿主接口：由歌曲行手势（Android 长按 / Windows 右键）的目标页面实现，
/// 用于在长按/右键时弹出歌曲操作菜单。<paramref name="position"/> 为相对行左上角的 DIP 坐标。
/// </summary>
public interface ISongContextMenuHost
{
    void ShowSongMenu(Song song, View row, Point position);
}

/// <summary>
/// 歌曲上下文菜单操作回调集合。由歌单详情页 / 全部歌曲页（本地音乐）提供具体实现，
/// 通过 <see cref="SongContextMenu.ShowAt"/> 统一渲染"播放 / 下一首播放 / 收藏 / 添加到歌单 / 歌曲信息"
/// 下拉菜单（跟随长按/右键位置弹出）。
/// </summary>
public sealed record SongMenuActions
{
    /// <summary>播放该歌曲</summary>
    public Func<Task>? Play { get; init; }

    /// <summary>下一首播放（插入播放队列当前曲之后）</summary>
    public Func<Task>? PlayNext { get; init; }

    /// <summary>切换收藏状态</summary>
    public Func<Task>? ToggleFavorite { get; init; }

    /// <summary>收藏菜单项文案（收藏 / 取消收藏）</summary>
    public string? FavoriteText { get; init; } = "收藏";

    /// <summary>查看歌曲信息（跳转歌曲详情）</summary>
    public Func<Task>? SongInfo { get; init; }

    /// <summary>获取用户歌单列表（"添加到歌单"使用）；为 null 时隐藏该项</summary>
    public Func<Task<List<Playlist>>>? GetPlaylists { get; init; }

    /// <summary>把歌曲加入指定歌单（参数为歌单 Id）</summary>
    public Func<int, Task>? AddSongToPlaylist { get; init; }

    /// <summary>下载到本地（网络歌曲）；为 null 时隐藏该项</summary>
    public Func<Task>? Download { get; init; }
}

/// <summary>
/// 歌曲长按/右键下拉菜单：统一弹出"播放 / 下一首播放 / 收藏 / 添加到歌单 / 歌曲信息"。
/// 供 PlaylistDetailPage / AllSongsPage / DesktopAllSongsPage 的歌曲行复用（ContextMenuPopup 承载，
/// 菜单位置跟随按下点；弹层挂到窗口根网格以处于最上层）。
/// </summary>
public static class SongContextMenu
{
    /// <summary>
    /// 在歌曲行按下位置弹出下拉菜单。<paramref name="position"/> 为相对行左上角的 DIP 坐标。
    /// </summary>
    public static void ShowAt(Song song, View row, Point position, SongMenuActions actions)
    {
        try
        {
            var host = ResolveWindowRoot() ?? FindPageRoot(row);
            if (host == null) return;

            var popup = new ContextMenuPopup();
            BuildMainMenu(popup, song, actions);

            // 全窗覆盖：铺满宿主网格（避免落入左上角单元格）
            if (host is Grid grid)
            {
                Grid.SetRow(popup, 0);
                Grid.SetRowSpan(popup, Math.Max(1, grid.RowDefinitions.Count));
                Grid.SetColumn(popup, 0);
                Grid.SetColumnSpan(popup, Math.Max(1, grid.ColumnDefinitions.Count));
            }
            host.Children.Add(popup);

            EventHandler? closed = null;
            closed = (_, _) =>
            {
                popup.Closed -= closed;
                try { if (popup.Parent is Layout parent) parent.Children.Remove(popup); } catch { }
            };
            popup.Closed += closed;

            var pos = ToHostPosition(row, position, host);
            popup.ShowAt(pos.X, pos.Y, host.Width, host.Height);
        }
        catch (Exception ex)
        {
            Log.Debug("SongContextMenu", $"[SongContextMenu] ShowAt 失败: {ex}");
            Toast($"菜单打开失败：{ex.Message}");
        }
    }

    /// <summary>构建主菜单（播放 / 下一首播放 / 收藏 / 添加到歌单 / [下载到本地] / 歌曲信息）。</summary>
    private static void BuildMainMenu(ContextMenuPopup popup, Song song, SongMenuActions actions)
    {
        popup.ClearContent();
        popup.AddContent(CreateRow("▶", "播放", async () =>
        {
            await popup.CloseAsync();
            if (actions.Play != null) await actions.Play();
        }));
        popup.AddContent(CreateRow("⏭", "下一首播放", async () =>
        {
            await popup.CloseAsync();
            if (actions.PlayNext != null) await actions.PlayNext();
        }));
        popup.AddContent(CreateRow("♥", actions.FavoriteText ?? "收藏", async () =>
        {
            await popup.CloseAsync();
            if (actions.ToggleFavorite != null) await actions.ToggleFavorite();
        }));
        popup.AddContent(CreateRow("＋", "添加到歌单", () => ShowPlaylistPicker(popup, song, actions)));
        if (actions.Download != null)
        {
            popup.AddContent(CreateRow("↓", "下载到本地", async () =>
            {
                await popup.CloseAsync();
                await actions.Download();
            }));
        }
        popup.AddContent(CreateRow("ℹ", "歌曲信息", async () =>
        {
            await popup.CloseAsync();
            if (actions.SongInfo != null) await actions.SongInfo();
        }));
    }

    /// <summary>"添加到歌单"子视图：列出用户歌单，点击即加入；支持返回主菜单。</summary>
    private static async Task ShowPlaylistPicker(ContextMenuPopup popup, Song song, SongMenuActions actions)
    {
        if (actions.GetPlaylists == null || actions.AddSongToPlaylist == null) return;

        popup.ClearContent();

        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var textSecondary = (Color)Application.Current!.Resources["TextSecondaryColor"];

        // 返回头部
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = GridLength.Auto },
                new() { Width = GridLength.Star }
            },
            ColumnSpacing = 8,
            Padding = new Thickness(8, 4, 8, 6)
        };
        var back = new Label
        {
            Text = "‹ 返回",
            FontSize = 14,
            TextColor = (Color)Application.Current!.Resources["PrimaryColor"],
            VerticalTextAlignment = TextAlignment.Center
        };
        var backTap = new TapGestureRecognizer();
        backTap.Tapped += (_, _) =>
        {
            BuildMainMenu(popup, song, actions);
            popup.Relayout();
        };
        back.GestureRecognizers.Add(backTap);
        header.Add(back, 0);
        header.Add(new Label
        {
            Text = "添加到歌单",
            FontSize = 15,
            FontAttributes = FontAttributes.Bold,
            TextColor = textPrimary,
            VerticalTextAlignment = TextAlignment.Center
        }, 1);
        popup.AddContent(header);

        try
        {
            var playlists = await actions.GetPlaylists();
            if (playlists == null || playlists.Count == 0)
            {
                popup.AddContent(new Label
                {
                    Text = "还没有歌单，请先在歌单页创建歌单",
                    FontSize = 13,
                    TextColor = textSecondary,
                    Margin = new Thickness(10, 6, 10, 10)
                });
                popup.Relayout();
                return;
            }

            var list = new VerticalStackLayout { Spacing = 0 };
            foreach (var p in playlists)
            {
                var captured = p;
                list.Add(CreateRow("♫", p.Name ?? "未命名歌单",
                    () => AddToPlaylistAsync(popup, captured.Name ?? "未命名歌单", captured.Id, actions.AddSongToPlaylist!)));
            }

            popup.AddContent(new ScrollView
            {
                MaximumHeightRequest = 360,
                Content = list
            });
            popup.Relayout();
        }
        catch (Exception ex)
        {
            Log.Debug("SongContextMenu", $"[SongContextMenu] 加载歌单失败: {ex.Message}");
            await popup.CloseAsync();
        }
    }

    /// <summary>把歌曲加入歌单，并切换为成功态。</summary>
    private static async Task AddToPlaylistAsync(ContextMenuPopup popup, string playlistName, int playlistId, Func<int, Task> addSong)
    {
        try
        {
            await addSong(playlistId);

            popup.ClearContent();
            popup.AddContent(new Label
            {
                Text = $"✓ 已添加到「{playlistName}」",
                FontSize = 15,
                FontAttributes = FontAttributes.Bold,
                TextColor = (Color)Application.Current!.Resources["TextPrimaryColor"],
                Margin = new Thickness(10, 8, 10, 14)
            });

            var okBtn = PopupUiHelpers.CreatePopupButton("完成", true);
            okBtn.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => { _ = popup.CloseAsync(); })
            });
            popup.AddContent(okBtn);
            popup.Relayout();
        }
        catch (Exception ex)
        {
            Log.Debug("SongContextMenu", $"[SongContextMenu] 加入歌单失败: {ex.Message}");
            await popup.CloseAsync();
        }
    }

    /// <summary>解析窗口级根网格宿主；竖屏 Shell 模式（页面即顶层）返回 null。</summary>
    private static Layout? ResolveWindowRoot()
    {
#if WINDOWS
        if (Pages.DesktopBlankPage.Instance?.WindowRoot is { } blankRoot)
            return blankRoot;
#endif
        if (App.IsLandscapeMode() && Pages.DesktopMainPage.Instance?.WindowRoot is { } desktopRoot)
            return desktopRoot;
        return null;
    }

    /// <summary>竖屏 Shell 模式：取行所在页面的根布局作为弹层宿主。</summary>
    private static Layout? FindPageRoot(View row)
    {
        var node = row as Element;
        while (node != null)
        {
            if (node is ContentPage page && page.Content is Layout layout)
                return layout;
            node = node.Parent;
        }
        return null;
    }

    /// <summary>把相对行的坐标换算为相对宿主（弹层覆盖的网格）的 DIP 坐标。</summary>
    private static Point ToHostPosition(View row, Point position, Layout host)
    {
        try
        {
#if ANDROID
            var rowNative = row.Handler?.PlatformView as global::Android.Views.View;
            var hostNative = host.Handler?.PlatformView as global::Android.Views.View;
            if (rowNative != null && hostNative != null)
            {
                var rowLoc = new int[2];
                var hostLoc = new int[2];
                rowNative.GetLocationOnScreen(rowLoc);
                hostNative.GetLocationOnScreen(hostLoc);
                var density = rowNative.Context?.Resources?.DisplayMetrics?.Density ?? 1;
                if (density <= 0) density = 1;
                return new Point((rowLoc[0] - hostLoc[0]) / density + position.X,
                                 (rowLoc[1] - hostLoc[1]) / density + position.Y);
            }
#elif WINDOWS
            var rowEl = row.Handler?.PlatformView as global::Microsoft.UI.Xaml.FrameworkElement;
            var hostEl = host.Handler?.PlatformView as global::Microsoft.UI.Xaml.FrameworkElement;
            if (rowEl != null && hostEl != null)
            {
                var p = rowEl.TransformToVisual(hostEl)
                    .TransformPoint(new global::Windows.Foundation.Point(position.X, position.Y));
                return new Point(p.X, p.Y);
            }
#endif
        }
        catch { }
        return new Point(8, 8);
    }

    /// <summary>创建一行可点击菜单项（图标 + 文字，下拉菜单紧凑样式）。</summary>
    private static View CreateRow(string icon, string text, Func<Task> onTap)
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = 28 },
                new() { Width = GridLength.Star }
            },
            ColumnSpacing = 10
        };
        row.Add(new Label
        {
            Text = icon,
            FontSize = 14,
            TextColor = primary,
            HorizontalTextAlignment = TextAlignment.Center,
            VerticalTextAlignment = TextAlignment.Center
        }, 0);
        row.Add(new Label
        {
            Text = text,
            FontSize = 14,
            TextColor = textPrimary,
            VerticalTextAlignment = TextAlignment.Center
        }, 1);

        var border = new Border
        {
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(9) },
            StrokeThickness = 0,
            BackgroundColor = Colors.Transparent,
            Padding = new Thickness(10, 9),
            Margin = new Thickness(2, 2),
            HorizontalOptions = LayoutOptions.Fill,
            Content = row
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await onTap())
        });
        return border;
    }

    /// <summary>简短提示（Android 原生 Toast；Windows 暂无原生提示，静默）。</summary>
    public static void Toast(string message)
    {
#if ANDROID
        try
        {
            var ctx = global::Android.App.Application.Context;
            global::Android.Widget.Toast.MakeText(ctx, message, global::Android.Widget.ToastLength.Short)?.Show();
        }
        catch { }
#endif
    }
}

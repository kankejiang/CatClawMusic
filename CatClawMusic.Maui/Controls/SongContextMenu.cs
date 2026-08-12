using CatClawMusic.Core.Models;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Controls;

/// <summary>
/// 歌曲上下文菜单宿主接口：由歌曲行手势（Android 长按 / Windows 右键）的目标页面实现，
/// 用于在长按/右键时弹出歌曲操作菜单。
/// </summary>
public interface ISongContextMenuHost
{
    void ShowSongMenu(Song song);
}

/// <summary>
/// 歌曲上下文菜单操作回调集合。由歌单详情页 / 全部歌曲页（本地音乐）提供具体实现，
/// 通过 <see cref="SongContextMenu.Show"/> 统一渲染"播放 / 下一首播放 / 收藏 / 添加到歌单 / 歌曲信息"菜单。
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
}

/// <summary>
/// 歌曲长按/右键上下文菜单：统一弹出"播放 / 下一首播放 / 收藏 / 添加到歌单 / 歌曲信息"。
/// 供 PlaylistDetailPage / AllSongsPage / DesktopAllSongsPage 的歌曲行复用（AppPopup 承载）。
/// </summary>
public static class SongContextMenu
{
    /// <summary>在主菜单中展示歌曲操作菜单。</summary>
    public static void Show(AppPopup popup, Song song, SongMenuActions actions)
    {
        popup.Title = song.Title ?? "";
        popup.ClearContent();

        var menu = new VerticalStackLayout { Spacing = 0 };
        menu.Add(CreateRow("▶", "播放", async () =>
        {
            await CloseAsync(popup);
            if (actions.Play != null) await actions.Play();
        }));
        menu.Add(CreateRow("⏭", "下一首播放", async () =>
        {
            await CloseAsync(popup);
            if (actions.PlayNext != null) await actions.PlayNext();
        }));
        menu.Add(CreateRow("♥", actions.FavoriteText ?? "收藏", async () =>
        {
            await CloseAsync(popup);
            if (actions.ToggleFavorite != null) await actions.ToggleFavorite();
        }));
        menu.Add(CreateRow("＋", "添加到歌单", () => ShowPlaylistPicker(popup, song, actions)));
        menu.Add(CreateRow("ℹ", "歌曲信息", async () =>
        {
            await CloseAsync(popup);
            if (actions.SongInfo != null) await actions.SongInfo();
        }));

        popup.AddContent(menu);
        popup.Open();
    }

    /// <summary>"添加到歌单"子视图：列出用户歌单，点击即加入；支持返回主菜单。</summary>
    private static async Task ShowPlaylistPicker(AppPopup popup, Song song, SongMenuActions actions)
    {
        if (actions.GetPlaylists == null || actions.AddSongToPlaylist == null) return;

        popup.Title = "";
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
        backTap.Tapped += (_, _) => Show(popup, song, actions);
        back.GestureRecognizers.Add(backTap);
        header.Add(back, 0);
        header.Add(new Label
        {
            Text = "添加到歌单",
            FontSize = 16,
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
                    Margin = new Thickness(0, 4, 0, 0)
                });
                return;
            }

            var list = new VerticalStackLayout { Spacing = 0 };
            foreach (var p in playlists)
            {
                var captured = p;
                var row = CreateRow("♫", p.Name ?? "未命名歌单",
                    () => AddToPlaylistAsync(popup, captured.Name ?? "未命名歌单", captured.Id, actions.AddSongToPlaylist!));
                list.Add(row);
            }

            popup.AddContent(new ScrollView
            {
                MaximumHeightRequest = 380,
                Content = list
            });
        }
        catch (Exception ex)
        {
            Log.Debug("SongContextMenu", $"[SongContextMenu] 加载歌单失败: {ex.Message}");
            await popup.CloseAsync();
        }
    }

    /// <summary>把歌曲加入歌单，并切换为成功态。</summary>
    private static async Task AddToPlaylistAsync(AppPopup popup, string playlistName, int playlistId, Func<int, Task> addSong)
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
                Margin = new Thickness(0, 6, 0, 14)
            });

            var okBtn = PopupUiHelpers.CreatePopupButton("完成", true);
            okBtn.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(() => { _ = popup.CloseAsync(); })
            });
            popup.AddContent(okBtn);
        }
        catch (Exception ex)
        {
            Log.Debug("SongContextMenu", $"[SongContextMenu] 加入歌单失败: {ex.Message}");
            await popup.CloseAsync();
        }
    }

    /// <summary>创建一行可点击菜单项（图标 + 文字）。</summary>
    private static View CreateRow(string icon, string text, Func<Task> onTap)
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];
        var primary = (Color)Application.Current!.Resources["PrimaryColor"];

        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitionCollection
            {
                new() { Width = 30 },
                new() { Width = GridLength.Star }
            },
            ColumnSpacing = 12
        };
        row.Add(new Label
        {
            Text = icon,
            FontSize = 16,
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
            StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
            Stroke = cardBg,
            StrokeThickness = 0,
            BackgroundColor = cardBg,
            Padding = new Thickness(14, 11),
            Margin = new Thickness(0, 0, 0, 6),
            HorizontalOptions = LayoutOptions.Fill,
            Content = row
        };
        border.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await onTap())
        });
        return border;
    }

    private static async Task CloseAsync(AppPopup popup)
    {
        try { await popup.CloseAsync(); } catch { }
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

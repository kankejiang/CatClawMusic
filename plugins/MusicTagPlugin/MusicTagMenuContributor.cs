using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.MusicTagPlugin;

public class MusicTagMenuContributor : IMenuContributorPlugin
{
    private const int MenuEditMetadata = 10001;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };

    public string PluginId => "musictag.menu";
    public string Name => "MusicTag 元数据编辑";
    public string Version => "1.0.0";
    public string Author => "CatClawMusic";
    public string Description => "提供歌曲元数据编辑功能，支持编辑标题、艺术家、专辑，以及联网搜索歌词和封面图片。参考 music-tag-web 设计。";

    public List<string> Capabilities => new()
    {
        "元数据编辑: 修改歌曲标题、艺术家、专辑信息",
        "歌词搜索: 一键搜索并嵌入歌词到文件",
        "封面搜索: 一键搜索并嵌入封面到文件",
        "TagLibSharp 写入: 直接写入音频文件 ID3/Vorbis 标签"
    };

    public Task InitializeAsync() => Task.CompletedTask;

    public Task ShutdownAsync()
    {
        _httpClient.Dispose();
        return Task.CompletedTask;
    }

    public List<MenuItemEntry> GetMenuItems(Song song)
    {
        return new List<MenuItemEntry>
        {
            new(MenuEditMetadata, "编辑元数据")
        };
    }

    public Task OnMenuItemClicked(int itemId, Song song, object fragment)
    {
        if (itemId == MenuEditMetadata && fragment is global::Android.App.Fragment frag)
        {
            var activity = frag.Activity;
            if (activity != null)
            {
                activity.RunOnUiThread(() => ShowEditMetadataDialog(frag, song));
            }
        }

        return Task.CompletedTask;
    }

    private void ShowEditMetadataDialog(global::Android.App.Fragment fragment, Song song)
    {
        var ctx = fragment.Context;
        if (ctx == null) return;

        var isLocalFile = song.Source == SongSource.Local
            && !string.IsNullOrEmpty(song.FilePath)
            && System.IO.File.Exists(song.FilePath);

        var scrollView = new global::Android.Widget.ScrollView(ctx);
        var layout = new global::Android.Widget.LinearLayout(ctx)
        {
            Orientation = global::Android.Widget.Orientation.Vertical
        };
        layout.SetPadding(40, 20, 40, 10);

        var etTitle = CreateEditField(ctx, song.Title ?? "");
        var etArtist = CreateEditField(ctx, song.Artist ?? "");
        var etAlbum = CreateEditField(ctx, song.Album ?? "");

        layout.AddView(CreateLabel(ctx, "标题"));
        layout.AddView(etTitle);
        layout.AddView(CreateLabel(ctx, "艺术家"));
        layout.AddView(etArtist);
        layout.AddView(CreateLabel(ctx, "专辑"));
        layout.AddView(etAlbum);

        var btnSearchLyrics = new global::Android.Widget.Button(ctx)
        {
            Text = "🔍 搜索歌词"
        };
        btnSearchLyrics.SetTextColor(global::Android.Graphics.Color.ParseColor("#9B7ED8"));
        btnSearchLyrics.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#209B7ED8"));
        var lyricsLayout = new global::Android.Widget.LinearLayout(ctx)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        lyricsLayout.SetPadding(0, 12, 0, 0);
        lyricsLayout.AddView(btnSearchLyrics);
        layout.AddView(lyricsLayout);

        var tvLyricsResult = new global::Android.Widget.TextView(ctx)
        {
            Visibility = global::Android.Views.ViewStates.Gone
        };
        tvLyricsResult.SetTextColor(global::Android.Graphics.Color.ParseColor("#2D7A50"));
        tvLyricsResult.TextSize = 12;
        tvLyricsResult.SetPadding(0, 4, 0, 4);
        layout.AddView(tvLyricsResult);

        btnSearchLyrics.Click += async (s, e) =>
        {
            btnSearchLyrics.Text = "搜索中...";
            btnSearchLyrics.Enabled = false;

            try
            {
                var lyricsPlugin = new MusicTagLyricsPlugin();
                var lrc = await lyricsPlugin.GetLyricsAsync(song);

                fragment.Activity?.RunOnUiThread(() =>
                {
                    btnSearchLyrics.Text = "🔍 搜索歌词";
                    btnSearchLyrics.Enabled = true;
                    if (lrc != null && lrc.Lines.Count > 0)
                    {
                        tvLyricsResult.Text = $"✅ 已找到 {lrc.Lines.Count} 行歌词";
                        tvLyricsResult.Visibility = global::Android.Views.ViewStates.Visible;
                    }
                    else
                    {
                        tvLyricsResult.Text = "❌ 未找到歌词";
                        tvLyricsResult.Visibility = global::Android.Views.ViewStates.Visible;
                    }
                });
            }
            catch
            {
                fragment.Activity?.RunOnUiThread(() =>
                {
                    btnSearchLyrics.Text = "🔍 搜索歌词";
                    btnSearchLyrics.Enabled = true;
                    tvLyricsResult.Text = "❌ 搜索失败";
                    tvLyricsResult.Visibility = global::Android.Views.ViewStates.Visible;
                });
            }
        };

        var btnSearchCover = new global::Android.Widget.Button(ctx)
        {
            Text = "🖼️ 搜索封面"
        };
        btnSearchCover.SetTextColor(global::Android.Graphics.Color.ParseColor("#D87E9B"));
        btnSearchCover.SetBackgroundColor(global::Android.Graphics.Color.ParseColor("#20D87E9B"));
        var coverLayout = new global::Android.Widget.LinearLayout(ctx)
        {
            Orientation = global::Android.Widget.Orientation.Horizontal
        };
        coverLayout.SetPadding(0, 8, 0, 0);
        coverLayout.AddView(btnSearchCover);
        layout.AddView(coverLayout);

        var tvCoverResult = new global::Android.Widget.TextView(ctx)
        {
            Visibility = global::Android.Views.ViewStates.Gone
        };
        tvCoverResult.SetTextColor(global::Android.Graphics.Color.ParseColor("#2D7A50"));
        tvCoverResult.TextSize = 12;
        tvCoverResult.SetPadding(0, 4, 0, 4);
        layout.AddView(tvCoverResult);

        byte[]? foundCoverBytes = null;

        btnSearchCover.Click += async (s, e) =>
        {
            btnSearchCover.Text = "搜索中...";
            btnSearchCover.Enabled = false;

            try
            {
                var coverPlugin = new MusicTagCoverPlugin();
                var coverBytes = coverPlugin.IsAvailable
                    ? await coverPlugin.GetCoverAsync(song)
                    : null;

                fragment.Activity?.RunOnUiThread(() =>
                {
                    btnSearchCover.Text = "🖼️ 搜索封面";
                    btnSearchCover.Enabled = true;
                    if (coverBytes != null)
                    {
                        foundCoverBytes = coverBytes;
                        tvCoverResult.Text = $"✅ 已找到封面 ({coverBytes.Length / 1024}KB)";
                        tvCoverResult.Visibility = global::Android.Views.ViewStates.Visible;
                    }
                    else
                    {
                        tvCoverResult.Text = "❌ 未找到封面";
                        tvCoverResult.Visibility = global::Android.Views.ViewStates.Visible;
                    }
                });
            }
            catch
            {
                fragment.Activity?.RunOnUiThread(() =>
                {
                    btnSearchCover.Text = "🖼️ 搜索封面";
                    btnSearchCover.Enabled = true;
                    tvCoverResult.Text = "❌ 搜索失败";
                    tvCoverResult.Visibility = global::Android.Views.ViewStates.Visible;
                });
            }
        };

        scrollView.AddView(layout);

        new global::Android.App.AlertDialog.Builder(ctx)
            .SetTitle($"编辑元数据 - {song.Title}")
            .SetView(scrollView)
            .SetPositiveButton("保存", async (d, args) =>
            {
                var title = etTitle.Text?.Trim();
                var artist = etArtist.Text?.Trim();
                var album = etAlbum.Text?.Trim();

                if (isLocalFile)
                {
                    var saved = CatClawMusic.Core.Services.TagReader.WriteMetadata(
                        song.FilePath, title, artist, album, null, null, null);
                    if (saved && foundCoverBytes != null)
                    {
                        CatClawMusic.Core.Services.TagReader.WriteCoverToFile(song.FilePath, foundCoverBytes);
                    }

                    var msg = saved ? "元数据已保存" : "保存失败（文件可能被占用）";
                    fragment.Activity?.RunOnUiThread(() =>
                        global::Android.Widget.Toast.MakeText(ctx, msg, global::Android.Widget.ToastLength.Short)?.Show());
                }
                else
                {
                    fragment.Activity?.RunOnUiThread(() =>
                        global::Android.Widget.Toast.MakeText(ctx, "网络歌曲暂不支持编辑标签，已应用缓存信息",
                            global::Android.Widget.ToastLength.Short)?.Show());
                }

                if (foundCoverBytes != null)
                {
                    try
                    {
                        var coverDir = System.IO.Path.Combine(
                            global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
                        System.IO.Directory.CreateDirectory(coverDir);
                        var coverPath = System.IO.Path.Combine(coverDir, $"cover_{song.Id}.jpg");
                        await System.IO.File.WriteAllBytesAsync(coverPath, foundCoverBytes);
                    }
                    catch { }
                }
            })
            .SetNegativeButton("取消", (d, args) => { })
            .Show();
    }

    private static global::Android.Widget.TextView CreateLabel(global::Android.Content.Context ctx, string text)
    {
        var tv = new global::Android.Widget.TextView(ctx);
        tv.Text = text;
        tv.SetTextColor(global::Android.Graphics.Color.ParseColor("#B0A8BA"));
        tv.TextSize = 12;
        tv.SetPadding(0, 8, 0, 4);
        return tv;
    }

    private static global::Android.Widget.EditText CreateEditField(global::Android.Content.Context ctx, string value)
    {
        var et = new global::Android.Widget.EditText(ctx);
        et.Text = value;
        et.SetTextColor(global::Android.Graphics.Color.ParseColor("#2D2438"));
        et.TextSize = 15;
        return et;
    }
}

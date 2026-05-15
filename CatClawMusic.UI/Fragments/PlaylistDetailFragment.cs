using Android.App;
using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class PlaylistDetailFragment : Fragment
{
    private PlaylistDetailViewModel _viewModel = null!;
    private INavigationService _navigationService = null!;
    private IAudioPlayerService? _audioPlayer = null!;
    private PlayQueue? _playQueue = null!;
    private RecyclerView _songList = null!;
    private TextView _titleText = null!, _statusText = null!;
    private SongAdapter _adapter = null!;

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_playlist_detail, container, false)!;

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<PlaylistDetailViewModel>();
        _navigationService = MainApplication.Services.GetRequiredService<INavigationService>();
        _audioPlayer = MainApplication.Services.GetService<IAudioPlayerService>();
        _playQueue = MainApplication.Services.GetService<PlayQueue>();
        _songList = view.FindViewById<RecyclerView>(Resource.Id.song_list)!;
        _songList.SetLayoutManager(new LinearLayoutManager(Context));
        _titleText = view.FindViewById<TextView>(Resource.Id.title_text)!;
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back)!;
        btnBack.Click += (s, e) => _navigationService.GoBack();

        _adapter = MainApplication.Services.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += (s, song) => _ = _viewModel.PlaySongAsync(song);
        _adapter.SongLongClicked += (s, song) => ShowSongContextMenu(_adapter.LastLongClickedView ?? _songList, song);
        _songList.SetAdapter(_adapter);

        // 监听播放状态变化
        if (_audioPlayer != null)
            _audioPlayer.StateChanged += OnAudioPlayerStateChanged;

        // 初始化播放状态显示
        UpdatePlayState();

        var args = Arguments;
        if (args != null)
        {
            int playlistId = args.GetInt("playlistId", 0);
            string name = args.GetString("playlistName") ?? "歌单";
            _titleText.Text = name;
            _ = _viewModel.LoadAsync(playlistId, name);
        }

        _viewModel.Songs.CollectionChanged += (s, e) =>
        {
            var a = Activity;
            if (a != null) a.RunOnUiThread(() =>
            {
                _adapter.UpdateSongs(_viewModel.Songs);
                _statusText.Text = _viewModel.StatusText;
                _statusText.Visibility = _viewModel.Songs.Count == 0
                    ? ViewStates.Visible : ViewStates.Gone;
            });
        };
    }

    private void ShowSongContextMenu(View anchor, Song song)
    {
        var popup = new AndroidX.AppCompat.Widget.PopupMenu(Context!, anchor);
        popup.MenuInflater!.Inflate(Resource.Menu.menu_song_context, popup.Menu!);
        popup.MenuItemClick += (s, e) => HandleContextMenuClick(e.Item!.ItemId, song);
        popup.Show();
    }

    private async void HandleContextMenuClick(int itemId, Song song)
    {
        switch (itemId)
        {
            case Resource.Id.action_play:
                await _viewModel.PlaySongAsync(song);
                break;
            case Resource.Id.action_play_next:
                var queue = MainApplication.Services.GetRequiredService<Core.Services.PlayQueue>();
                queue.AddNext(song);
                var a = Activity;
                if (a != null) a.RunOnUiThread(() =>
                    Toast.MakeText(Context, $"已添加: {song.Title}", ToastLength.Short)!.Show());
                break;
            case Resource.Id.action_add_to_playlist:
                ShowAddToPlaylistDialog(song);
                break;
            case Resource.Id.action_favorite:
                bool isFav = await _viewModel.IsFavoriteAsync(song.Id);
                await _viewModel.ToggleFavoriteAsync(song.Id, !isFav);
                var favA = Activity;
                if (favA != null) favA.RunOnUiThread(() =>
                    Toast.MakeText(Context, isFav ? "已取消收藏" : "已收藏", ToastLength.Short)!.Show());
                break;
            case Resource.Id.action_edit_metadata:
                ShowEditMetadataDialog(song);
                break;
            case Resource.Id.action_song_info:
                ShowSongInfoDialog(song);
                break;
        }
    }

    private async void ShowAddToPlaylistDialog(Song song)
    {
        var musicLibrary = MainApplication.Services.GetRequiredService<Core.Interfaces.IMusicLibraryService>();
        var playlists = await musicLibrary.GetAllPlaylistsAsync();
        if (playlists.Count == 0)
        {
            Toast.MakeText(Context, "暂无歌单，请先创建歌单", ToastLength.Short)!.Show();
            return;
        }

        var names = playlists.Select(p => p.Name).ToArray();
        new Android.App.AlertDialog.Builder(Context!)
            .SetTitle($"添加到歌单")
            .SetItems(names, async (s, e) =>
            {
                var selected = playlists[e.Which];
                await _viewModel.AddSongToPlaylistAsync(selected.Id, song.Id);
                Toast.MakeText(Context, $"已添加到「{selected.Name}」", ToastLength.Short)!.Show();
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }

    private void ShowSongInfoDialog(Song song)
    {
        var durationStr = TimeSpan.FromMilliseconds(song.Duration).ToString(@"mm\:ss");
        var sourceStr = song.Source switch
        {
            SongSource.Local => "本地",
            SongSource.WebDAV => "网络",
            SongSource.Cache => "缓存",
            _ => "未知"
        };
        var info = $"标题：{song.Title}\n"
            + $"艺术家：{song.Artist}\n"
            + $"专辑：{song.Album}\n"
            + $"时长：{durationStr}\n"
            + $"来源：{sourceStr}\n"
            + $"比特率：{(song.Bitrate > 0 ? $"{song.Bitrate / 1000}kbps" : "未知")}\n"
            + $"文件大小：{FormatFileSize(song.FileSize)}\n"
            + $"路径：{song.FilePath}";

        new Android.App.AlertDialog.Builder(Context!)
            .SetTitle("歌曲详情")
            .SetMessage(info)
            .SetPositiveButton("确定", (s, e) => { })
            .Show();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    private TextView CreateLabel(string text)
    {
        var tv = new TextView(Context);
        tv.Text = text;
        tv.SetTextColor(Android.Graphics.Color.ParseColor("#B0A8BA"));
        tv.TextSize = 12;
        tv.SetPadding(0, 8, 0, 4);
        return tv;
    }

    private EditText CreateEditField(string value)
    {
        var et = new EditText(Context);
        et.Text = value;
        et.SetTextColor(Android.Graphics.Color.ParseColor("#2D2438"));
        et.TextSize = 15;
        return et;
    }

    private void ShowEditMetadataDialog(Song song)
    {
        var ctx = Context;
        if (ctx == null) return;

        var isLocalFile = song.Source == SongSource.Local
            && !string.IsNullOrEmpty(song.FilePath)
            && System.IO.File.Exists(song.FilePath);

        var scrollView = new ScrollView(ctx);
        var layout = new LinearLayout(ctx)
        {
            Orientation = Orientation.Vertical
        };
        layout.SetPadding(40, 20, 40, 10);

        var etTitle = CreateEditField(song.Title ?? "");
        var etArtist = CreateEditField(song.Artist ?? "");
        var etAlbum = CreateEditField(song.Album ?? "");

        layout.AddView(CreateLabel("标题"));
        layout.AddView(etTitle);
        layout.AddView(CreateLabel("艺术家"));
        layout.AddView(etArtist);
        layout.AddView(CreateLabel("专辑"));
        layout.AddView(etAlbum);

        var btnSearchLyrics = new Android.Widget.Button(ctx)
        {
            Text = "🔍 搜索歌词"
        };
        btnSearchLyrics.SetTextColor(Android.Graphics.Color.ParseColor("#9B7ED8"));
        btnSearchLyrics.SetBackgroundColor(Android.Graphics.Color.ParseColor("#209B7ED8"));
        var lyricsLayout = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        lyricsLayout.SetPadding(0, 12, 0, 0);
        lyricsLayout.AddView(btnSearchLyrics);
        layout.AddView(lyricsLayout);

        var tvLyricsResult = new TextView(ctx)
        {
            Visibility = ViewStates.Gone
        };
        tvLyricsResult.SetTextColor(Android.Graphics.Color.ParseColor("#2D7A50"));
        tvLyricsResult.TextSize = 12;
        tvLyricsResult.SetPadding(0, 4, 0, 4);
        layout.AddView(tvLyricsResult);

        btnSearchLyrics.Click += async (s, e) =>
        {
            btnSearchLyrics.Text = "搜索中...";
            btnSearchLyrics.Enabled = false;

            var lyricsService = MainApplication.Services.GetRequiredService<ILyricsService>();
            var lrc = await lyricsService.GetLyricsAsync(song);

            Activity?.RunOnUiThread(() =>
            {
                btnSearchLyrics.Text = "🔍 搜索歌词";
                btnSearchLyrics.Enabled = true;
                if (lrc != null && lrc.Lines.Count > 0)
                {
                    tvLyricsResult.Text = $"✅ 已找到 {lrc.Lines.Count} 行歌词";
                    tvLyricsResult.Visibility = ViewStates.Visible;
                }
                else
                {
                    tvLyricsResult.Text = "❌ 未找到歌词";
                    tvLyricsResult.Visibility = ViewStates.Visible;
                }
            });
        };

        var btnSearchCover = new Android.Widget.Button(ctx)
        {
            Text = "🖼️ 搜索封面"
        };
        btnSearchCover.SetTextColor(Android.Graphics.Color.ParseColor("#D87E9B"));
        btnSearchCover.SetBackgroundColor(Android.Graphics.Color.ParseColor("#20D87E9B"));
        var coverLayout = new LinearLayout(ctx) { Orientation = Orientation.Horizontal };
        coverLayout.SetPadding(0, 8, 0, 0);
        coverLayout.AddView(btnSearchCover);
        layout.AddView(coverLayout);

        var tvCoverResult = new TextView(ctx)
        {
            Visibility = ViewStates.Gone
        };
        tvCoverResult.SetTextColor(Android.Graphics.Color.ParseColor("#2D7A50"));
        tvCoverResult.TextSize = 12;
        tvCoverResult.SetPadding(0, 4, 0, 4);
        layout.AddView(tvCoverResult);

        byte[]? foundCoverBytes = null;

        btnSearchCover.Click += async (s, e) =>
        {
            btnSearchCover.Text = "搜索中...";
            btnSearchCover.Enabled = false;

            var pluginManager = MainApplication.Services.GetService<IPluginManager>();
            byte[]? coverBytes = null;

            if (pluginManager != null)
            {
                var providers = pluginManager.GetEnabledPlugins<ICoverProviderPlugin>();
                foreach (var provider in providers)
                {
                    try
                    {
                        if (!provider.IsAvailable) continue;
                        coverBytes = await provider.GetCoverAsync(song);
                        if (coverBytes != null) break;
                    }
                    catch { }
                }
            }

            Activity?.RunOnUiThread(() =>
            {
                btnSearchCover.Text = "🖼️ 搜索封面";
                btnSearchCover.Enabled = true;
                if (coverBytes != null)
                {
                    foundCoverBytes = coverBytes;
                    tvCoverResult.Text = $"✅ 已找到封面 ({coverBytes.Length / 1024}KB)";
                    tvCoverResult.Visibility = ViewStates.Visible;
                }
                else
                {
                    tvCoverResult.Text = "❌ 未找到封面";
                    tvCoverResult.Visibility = ViewStates.Visible;
                }
            });
        };

        scrollView.AddView(layout);

        new AlertDialog.Builder(ctx)
            .SetTitle($"编辑元数据 - {song.Title}")
            .SetView(scrollView)
            .SetPositiveButton("保存", async (s, e) =>
            {
                var title = etTitle.Text?.Trim();
                var artist = etArtist.Text?.Trim();
                var album = etAlbum.Text?.Trim();

                if (isLocalFile)
                {
                    var saved = TagReader.WriteMetadata(song.FilePath, title, artist, album, null, null, null);
                    if (saved && foundCoverBytes != null)
                    {
                        TagReader.WriteCoverToFile(song.FilePath, foundCoverBytes);
                    }

                    var msg = saved ? "元数据已保存" : "保存失败（文件可能被占用）";
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(ctx, msg, ToastLength.Short)?.Show());
                }
                else
                {
                    Activity?.RunOnUiThread(() =>
                        Toast.MakeText(ctx, "网络歌曲暂不支持编辑标签，已应用缓存信息", ToastLength.Short)?.Show());
                }

                if (foundCoverBytes != null)
                {
                    var coverDir = System.IO.Path.Combine(
                        global::Android.App.Application.Context.CacheDir!.AbsolutePath, "covers");
                    System.IO.Directory.CreateDirectory(coverDir);
                    var coverPath = System.IO.Path.Combine(coverDir, $"cover_{song.Id}.jpg");
                    await System.IO.File.WriteAllBytesAsync(coverPath, foundCoverBytes);
                }
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }

    private void OnAudioPlayerStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        Activity?.RunOnUiThread(UpdatePlayState);
    }

    private void UpdatePlayState()
    {
        var currentSong = _playQueue?.CurrentSong;
        int currentSongId = currentSong?.Id ?? -1;
        bool isPlaying = _audioPlayer?.IsPlaying ?? false;
        _adapter.UpdatePlayState(currentSongId, isPlaying);
    }

    public override void OnDestroyView()
    {
        base.OnDestroyView();
        if (_audioPlayer != null)
            _audioPlayer.StateChanged -= OnAudioPlayerStateChanged;
    }
}

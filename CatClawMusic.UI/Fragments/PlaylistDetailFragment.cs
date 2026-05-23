using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.Helpers;
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
    private int _playlistId;
    private bool _isUserPlaylist;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionChangedHandler;

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
        _adapter.SongLongClicked += (s, song) => ShowSongContextMenu(song);
        _songList.SetAdapter(_adapter);

        if (_audioPlayer != null)
            _audioPlayer.StateChanged += OnAudioPlayerStateChanged;

        UpdatePlayState();

        var args = Arguments;
        if (args != null)
        {
            _playlistId = args.GetInt("playlistId", 0);
            _isUserPlaylist = _playlistId > 0;
            string name = args.GetString("playlistName") ?? "歌单";
            _titleText.Text = name;
            _ = _viewModel.LoadAsync(_playlistId, name);
        }

        _collectionChangedHandler = (s, e) =>
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
        _viewModel.Songs.CollectionChanged += _collectionChangedHandler;
    }

    private void ShowSongContextMenu(Song song)
    {
        var ctx = Context;
        if (ctx == null) return;

        var dialog = new GlassDialog(ctx)
            .SetTitle(song.Title ?? "未知歌曲", song.Artist ?? "未知艺术家");

        dialog.AddItem("▶  播放", () => _ = _viewModel.PlaySongAsync(song));
        dialog.AddItem("⏭  下一首播放", () =>
        {
            var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
            queue.AddNext(song);
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(ctx, $"已添加: {song.Title}", ToastLength.Short)?.Show());
        });
        dialog.AddItem("📋  添加到歌单", () => ShowAddToPlaylistDialog(song));

        if (_isUserPlaylist)
        {
            dialog.AddItem("🗑  从歌单移除", async () =>
            {
                await _viewModel.RemoveSongFromPlaylistAsync(song.Id);
                Activity?.RunOnUiThread(() =>
                {
                    Toast.MakeText(ctx, "已从歌单移除", ToastLength.Short)?.Show();
                    _ = _viewModel.LoadAsync(_playlistId, _viewModel.PlaylistName);
                });
            });
        }

        dialog.AddItem("❤  收藏", async () =>
        {
            bool isFav = await _viewModel.IsFavoriteAsync(song.Id);
            await _viewModel.ToggleFavoriteAsync(song.Id, !isFav);
            Activity?.RunOnUiThread(() =>
                Toast.MakeText(ctx, isFav ? "已取消收藏" : "已收藏", ToastLength.Short)?.Show());
        });
        dialog.AddItem("ℹ  歌曲详情", () => ShowSongInfoDialog(song));

        dialog.Show();
    }

    private async void ShowAddToPlaylistDialog(Song song)
    {
        var ctx = Context;
        if (ctx == null) return;

        var musicLibrary = MainApplication.Services.GetRequiredService<IMusicLibraryService>();
        var playlists = await musicLibrary.GetAllPlaylistsAsync();
        if (playlists.Count == 0)
        {
            Toast.MakeText(ctx, "暂无歌单，请先创建歌单", ToastLength.Short)!.Show();
            return;
        }

        var dialog = new GlassDialog(ctx).SetTitle("添加到歌单");
        foreach (var p in playlists)
        {
            var playlistId = p.Id;
            dialog.AddItem(p.Name, async () =>
            {
                await _viewModel.AddSongToPlaylistAsync(playlistId, song.Id);
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(ctx, $"已添加到「{p.Name}」", ToastLength.Short)?.Show());
            });
        }
        dialog.AddNegativeButton("取消");
        dialog.Show();
    }

    private void ShowSongInfoDialog(Song song)
    {
        var ctx = Context;
        if (ctx == null) return;

        var durationStr = TimeSpan.FromMilliseconds(song.Duration).ToString(@"mm\:ss");
        var sourceStr = song.Source switch
        {
            SongSource.Local => "本地",
            SongSource.WebDAV => "网络",
            SongSource.Cache => "缓存",
            _ => "未知"
        };
        var info = $"艺术家：{song.Artist}\n"
            + $"专辑：{song.Album}\n"
            + $"时长：{durationStr}\n"
            + $"来源：{sourceStr}\n"
            + $"比特率：{(song.Bitrate > 0 ? $"{song.Bitrate / 1000}kbps" : "未知")}\n"
            + $"文件大小：{FormatFileSize(song.FileSize)}\n"
            + $"路径：{song.FilePath}";

        new GlassDialog(ctx)
            .SetTitle(song.Title ?? "未知歌曲")
            .AddMessage(info)
            .AddNegativeButton("确定")
            .Show();
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
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

    public override void OnResume()
    {
        base.OnResume();
        if (_viewModel.Songs.Count > 0 && _isUserPlaylist)
            _ = _viewModel.LoadAsync(_playlistId, _viewModel.PlaylistName);
    }

    public override void OnDestroyView()
    {
        if (_collectionChangedHandler != null)
            _viewModel.Songs.CollectionChanged -= _collectionChangedHandler;
        if (_audioPlayer != null)
            _audioPlayer.StateChanged -= OnAudioPlayerStateChanged;
        base.OnDestroyView();
    }
}

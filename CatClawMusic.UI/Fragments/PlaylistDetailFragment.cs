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

        if (_audioPlayer != null)
            _audioPlayer.StateChanged += OnAudioPlayerStateChanged;

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

        var pluginMenuItems = new List<(int MenuItemId, IMenuContributorPlugin Plugin)>();
        var pluginManager = MainApplication.Services.GetService<IPluginManager>();
        if (pluginManager != null)
        {
            var contributors = pluginManager.GetEnabledPlugins<IMenuContributorPlugin>();
            foreach (var contributor in contributors)
            {
                try
                {
                    foreach (var entry in contributor.GetMenuItems(song))
                    {
                        if (string.IsNullOrEmpty(entry.Title)) continue;
                        var menuItem = popup.Menu!.Add(entry.Title);
                        menuItem.SetShowAsAction(Android.Views.ShowAsAction.Never);
                        pluginMenuItems.Add((menuItem.ItemId, contributor));
                    }
                }
                catch { }
            }
        }

        popup.MenuItemClick += (s, e) => HandleContextMenuClick(e.Item!.ItemId, song, pluginMenuItems);
        popup.Show();
    }

    private async void HandleContextMenuClick(int itemId, Song song, List<(int MenuItemId, IMenuContributorPlugin Plugin)> pluginItems)
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
            case Resource.Id.action_song_info:
                ShowSongInfoDialog(song);
                break;
            default:
                foreach (var (menuItemId, plugin) in pluginItems)
                {
                    if (itemId == menuItemId)
                    {
                        await plugin.OnMenuItemClicked(0, song, this);
                        return;
                    }
                }
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

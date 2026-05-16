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

/// <summary>
/// 歌单详情Fragment，显示歌单中的歌曲列表，支持播放、上下文菜单等操作
/// </summary>
public class PlaylistDetailFragment : Fragment
{
    private PlaylistDetailViewModel _viewModel = null!;
    private INavigationService _navigationService = null!;
    private IAudioPlayerService? _audioPlayer = null!;
    private PlayQueue? _playQueue = null!;
    private RecyclerView _songList = null!;
    private TextView _titleText = null!, _statusText = null!;
    private SongAdapter _adapter = null!;

    /// <summary>
    /// 创建歌单详情视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_playlist_detail, container, false)!;

    /// <summary>
    /// 视图创建完成后初始化控件，加载歌单数据并绑定事件
    /// </summary>
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

    /// <summary>
    /// 显示歌曲右键上下文菜单，包含播放、添加、收藏等选项
    /// </summary>
    private void ShowSongContextMenu(View anchor, Song song)
    {
        var popup = new AndroidX.AppCompat.Widget.PopupMenu(Context!, anchor);
        popup.MenuInflater!.Inflate(Resource.Menu.menu_song_context, popup.Menu!);

        popup.MenuItemClick += (s, e) => HandleContextMenuClick(e.Item!.ItemId, song);
        popup.Show();
    }

    /// <summary>
    /// 处理上下文菜单点击事件，分发到不同的操作逻辑
    /// </summary>
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
            case Resource.Id.action_song_info:
                ShowSongInfoDialog(song);
                break;
        }
    }

    /// <summary>
    /// 显示"添加到歌单"对话框，列出所有可用歌单供用户选择
    /// </summary>
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

    /// <summary>
    /// 显示歌曲详情对话框，包含标题、艺术家、专辑、时长等信息
    /// </summary>
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

    /// <summary>
    /// 格式化文件大小为可读字符串（B/KB/MB）
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }

    /// <summary>
    /// 音频播放器状态变化时刷新播放状态UI
    /// </summary>
    private void OnAudioPlayerStateChanged(object? sender, PlaybackStateChangedEventArgs e)
    {
        Activity?.RunOnUiThread(UpdatePlayState);
    }

    /// <summary>
    /// 更新当前播放状态，高亮当前播放歌曲
    /// </summary>
    private void UpdatePlayState()
    {
        var currentSong = _playQueue?.CurrentSong;
        int currentSongId = currentSong?.Id ?? -1;
        bool isPlaying = _audioPlayer?.IsPlaying ?? false;
        _adapter.UpdatePlayState(currentSongId, isPlaying);
    }

    /// <summary>
    /// Fragment销毁时解绑播放器状态变化事件
    /// </summary>
    public override void OnDestroyView()
    {
        base.OnDestroyView();
        if (_audioPlayer != null)
            _audioPlayer.StateChanged -= OnAudioPlayerStateChanged;
    }
}

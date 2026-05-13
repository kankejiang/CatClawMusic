using Android.Graphics;
using Android.OS;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.Widget;
using AndroidX.RecyclerView.Widget;
using CatClawMusic.Core.Models;
using CatClawMusic.UI.Adapters;
using CatClawMusic.UI.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CatClawMusic.UI.Fragments;

public class PlaylistFragment : Fragment
{
    private PlaylistViewModel _viewModel = null!;
    private RecyclerView _songList = null!;
    private SongAdapter _adapter = null!;
    private TextView _tabAll = null!, _tabFav = null!, _tabRecent = null!;
    private TextView _statusText = null!;
    private TextView _sortTitle = null!, _sortArtist = null!, _sortAlbum = null!;
    private LinearLayout _playlistStripContainer = null!;
    private View _btnNewPlaylist = null!;

    private readonly Color _colorActive = Color.ParseColor("#9B7ED8");
    private readonly Color _colorInactive = Color.ParseColor("#2D2438");
    private readonly Color _colorSortActive = Color.ParseColor("#9B7ED8");
    private readonly Color _colorSortInactive = Color.ParseColor("#C0B8CA");

    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
    {
        return inflater.Inflate(Resource.Layout.fragment_playlist, container, false)!;
    }

    public override void OnViewCreated(View view, Bundle? state)
    {
        base.OnViewCreated(view, state);
        _viewModel = MainApplication.Services.GetRequiredService<PlaylistViewModel>();
        _songList = view.FindViewById<RecyclerView>(Resource.Id.song_list)!;
        _songList.SetLayoutManager(new LinearLayoutManager(Context));
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        _tabAll = view.FindViewById<TextView>(Resource.Id.tab_all)!;
        _tabFav = view.FindViewById<TextView>(Resource.Id.tab_fav)!;
        _tabRecent = view.FindViewById<TextView>(Resource.Id.tab_recent)!;

        _tabAll.Click += (s, e) => _ = _viewModel.LoadAllSongsAsync();
        _tabFav.Click += (s, e) => _ = _viewModel.LoadFavoritesAsync();
        _tabRecent.Click += (s, e) => _ = _viewModel.LoadRecentAsync();

        _sortTitle = view.FindViewById<TextView>(Resource.Id.sort_title)!;
        _sortArtist = view.FindViewById<TextView>(Resource.Id.sort_artist)!;
        _sortAlbum = view.FindViewById<TextView>(Resource.Id.sort_album)!;

        _sortTitle.Click += (s, e) => _viewModel.SetSort("title");
        _sortArtist.Click += (s, e) => _viewModel.SetSort("artist");
        _sortAlbum.Click += (s, e) => _viewModel.SetSort("album");

        _adapter = MainApplication.Services.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += (s, song) => _ = _viewModel.PlaySongAsync(song);
        _adapter.SongLongClicked += (s, song) => ShowSongContextMenu((View?)s ?? view, song);
        _songList.SetAdapter(_adapter);

        _viewModel.Songs.CollectionChanged += (s, e) =>
        {
            var a = Activity;
            if (a != null) a.RunOnUiThread(() =>
            {
                _adapter.UpdateSongs(_viewModel.Songs);
                _statusText.Visibility = _viewModel.Songs.Count == 0 ? ViewStates.Visible : ViewStates.Gone;
                _statusText.Text = _viewModel.StatusText;
            });
        };

        _viewModel.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(_viewModel.ActiveTab))
            {
                var a = Activity;
                if (a != null) a.RunOnUiThread(UpdateTabStyles);
            }
            else if (e.PropertyName == nameof(_viewModel.SortKey) || e.PropertyName == nameof(_viewModel.SortDescending))
            {
                var a = Activity;
                if (a != null) a.RunOnUiThread(UpdateSortStyles);
            }
        };

        // 歌单条
        _playlistStripContainer = view.FindViewById<LinearLayout>(Resource.Id.playlist_strip_container)!;
        _btnNewPlaylist = view.FindViewById(Resource.Id.btn_new_playlist)!;
        _btnNewPlaylist.Click += (s, e) => ShowCreatePlaylistDialog();

        _viewModel.Playlists.CollectionChanged += (s, e) =>
        {
            var a = Activity;
            if (a != null) a.RunOnUiThread(RefreshPlaylistStrip);
        };

        _ = _viewModel.LoadPlaylistsAsync();
        _ = _viewModel.LoadAllSongsAsync();
    }

    private void UpdateTabStyles()
    {
        var active = _viewModel.ActiveTab;
        _tabAll.SetTextColor(active == "all" ? _colorActive : _colorInactive);
        _tabAll.Typeface = active == "all" ? Typeface.DefaultBold : Typeface.Default;
        _tabFav.SetTextColor(active == "fav" ? _colorActive : _colorInactive);
        _tabFav.Typeface = active == "fav" ? Typeface.DefaultBold : Typeface.Default;
        _tabRecent.SetTextColor(active == "recent" ? _colorActive : _colorInactive);
        _tabRecent.Typeface = active == "recent" ? Typeface.DefaultBold : Typeface.Default;
    }

    private void UpdateSortStyles()
    {
        var isDesc = _viewModel.SortDescending;
        var arrow = isDesc ? " ↓" : " ↑";

        _sortTitle.Text = "标题" + (_viewModel.SortKey == "title" ? arrow : "");
        _sortTitle.SetTextColor(_viewModel.SortKey == "title" ? _colorSortActive : _colorSortInactive);
        _sortTitle.Typeface = _viewModel.SortKey == "title" ? Typeface.DefaultBold : Typeface.Default;

        _sortArtist.Text = "艺术家" + (_viewModel.SortKey == "artist" ? arrow : "");
        _sortArtist.SetTextColor(_viewModel.SortKey == "artist" ? _colorSortActive : _colorSortInactive);
        _sortArtist.Typeface = _viewModel.SortKey == "artist" ? Typeface.DefaultBold : Typeface.Default;

        _sortAlbum.Text = "专辑" + (_viewModel.SortKey == "album" ? arrow : "");
        _sortAlbum.SetTextColor(_viewModel.SortKey == "album" ? _colorSortActive : _colorSortInactive);
        _sortAlbum.Typeface = _viewModel.SortKey == "album" ? Typeface.DefaultBold : Typeface.Default;
    }

    private void RefreshPlaylistStrip()
    {
        _playlistStripContainer.RemoveAllViews();
        foreach (var playlist in _viewModel.Playlists)
        {
            var card = LayoutInflater.From(Context)!.Inflate(Resource.Layout.item_playlist_strip, _playlistStripContainer, false)!;
            var name = card.FindViewById<TextView>(Resource.Id.playlist_strip_name)!;
            var count = card.FindViewById<TextView>(Resource.Id.playlist_strip_count)!;
            name.Text = playlist.Name;
            count.Text = $"{playlist.SongCount}首";

            var captured = playlist;
            card.Click += (s, e) => _viewModel.NavigateToPlaylist(captured.Id, captured.Name);
            card.LongClick += (s, e) => ShowDeletePlaylistDialog(captured);

            _playlistStripContainer.AddView(card);
        }
    }

    private void ShowSongContextMenu(View anchor, Song song)
    {
        var popup = new PopupMenu(Context!, anchor);
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
            case Resource.Id.action_song_info:
                ShowSongInfoDialog(song);
                break;
        }
    }

    private void ShowCreatePlaylistDialog()
    {
        var editText = new EditText(Context!)
        {
            Hint = "请输入歌单名称",
            InputType = Android.Text.InputTypes.TextFlagCapSentences
        };

        new Android.App.AlertDialog.Builder(Context!)
            .SetTitle("新建歌单")
            .SetView(editText)
            .SetPositiveButton("创建", async (s, e) =>
            {
                var name = editText.Text?.Trim();
                if (!string.IsNullOrEmpty(name))
                    await _viewModel.CreatePlaylistAsync(name);
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }

    private void ShowDeletePlaylistDialog(Playlist playlist)
    {
        new Android.App.AlertDialog.Builder(Context!)
            .SetTitle("删除歌单")
            .SetMessage($"确定要删除歌单「{playlist.Name}」吗？\n歌单中的歌曲不会被删除。")
            .SetPositiveButton("删除", async (s, e) =>
            {
                await _viewModel.DeletePlaylistAsync(playlist.Id);
            })
            .SetNegativeButton("取消", (s, e) => { })
            .Show();
    }

    private void ShowAddToPlaylistDialog(Song song)
    {
        var playlists = _viewModel.Playlists.ToList();
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

    public override void OnResume()
    {
        base.OnResume();
        if (_viewModel.ActiveTab == "all") _ = _viewModel.LoadAllSongsAsync();
        else if (_viewModel.ActiveTab == "fav") _ = _viewModel.LoadFavoritesAsync();
        else _ = _viewModel.LoadRecentAsync();
    }
}

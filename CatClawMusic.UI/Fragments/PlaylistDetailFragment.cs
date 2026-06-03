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
using System.Linq;

namespace CatClawMusic.UI.Fragments;

/// <summary>
/// 歌单详情Fragment，显示歌单内的歌曲列表，支持播放、移除、收藏等操作
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
    private int _playlistId;
    private bool _isUserPlaylist;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _collectionChangedHandler;
    private ImageButton _btnShuffle = null!;
    private ImageButton _btnSort = null!;
    private ImageButton _btnFilter = null!;
    private ImageButton _btnMultiSelect = null!;
    private bool _isMultiSelectMode;
    private string _currentSourceFilter = "all"; // all, local, network
    private readonly HashSet<int> _selectedSongIds = new();
    private LinearLayout? _multiSelectBar;

    /// <summary>
    /// 创建歌单详情视图
    /// </summary>
    public override View OnCreateView(LayoutInflater inflater, ViewGroup? container, Bundle? state)
        => inflater.Inflate(Resource.Layout.fragment_playlist_detail, container, false)!;

    /// <summary>
    /// 视图创建完成后初始化控件，从Arguments获取歌单ID，加载歌曲列表
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
        _songList.SetItemViewCacheSize(20);
        _songList.GetRecycledViewPool().SetMaxRecycledViews(0, 30);
        _titleText = view.FindViewById<TextView>(Resource.Id.title_text)!;
        _statusText = view.FindViewById<TextView>(Resource.Id.status_text)!;

        var btnBack = view.FindViewById<ImageButton>(Resource.Id.btn_back)!;
        btnBack.Click += (s, e) => _navigationService.GoBack();

        _btnShuffle = view.FindViewById<ImageButton>(Resource.Id.btn_shuffle)!;
        _btnSort = view.FindViewById<ImageButton>(Resource.Id.btn_sort)!;
        _btnFilter = view.FindViewById<ImageButton>(Resource.Id.btn_filter)!;
        _btnMultiSelect = view.FindViewById<ImageButton>(Resource.Id.btn_multi_select)!;

        _btnShuffle.Click += OnShuffleClicked;
        _btnSort.Click += OnSortClicked;
        _btnFilter.Click += OnFilterClicked;
        _btnMultiSelect.Click += OnMultiSelectClicked;

        _adapter = MainApplication.Services.GetRequiredService<SongAdapter>();
        _adapter.SongClicked += (s, song) => _ = _viewModel.PlaySongAsync(song);
        _adapter.SongLongClicked += (s, song) => ShowSongContextMenu(song);
        _songList.SetAdapter(_adapter);
        _songList.AddOnScrollListener(new SongAdapter.ScrollListener(_adapter));

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
            
            if (_playlistId == -1)
            {
                _btnFilter.Visibility = ViewStates.Visible;
            }
            
            _ = _viewModel.LoadAsync(_playlistId, name).ContinueWith(_ =>
            {
                ApplySavedSort();
                ApplySavedSourceFilter();
            });
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
                    _ = _viewModel.LoadAsync(_playlistId, _viewModel.PlaylistName).ContinueWith(_ =>
                    {
                        ApplySavedSort();
                    });
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

    private void OnShuffleClicked(object? sender, EventArgs e)
    {
        if (_viewModel.Songs.Count == 0) return;
        var random = new Random();
        var song = _viewModel.Songs[random.Next(_viewModel.Songs.Count)];
        _ = _viewModel.PlaySongAsync(song);
    }

    private void OnFilterClicked(object? sender, EventArgs e)
    {
        var ctx = Context;
        if (ctx == null) return;

        var dialog = new GlassDialog(ctx).SetTitle("来源筛选");

        dialog.AddItemWithHighlight("全部音乐", _currentSourceFilter == "all", () =>
        {
            _currentSourceFilter = "all";
            SaveSourceFilter("all");
            _viewModel.ApplySourceFilter("all");
        });
        dialog.AddItemWithHighlight("仅本地音乐", _currentSourceFilter == "local", () =>
        {
            _currentSourceFilter = "local";
            SaveSourceFilter("local");
            _viewModel.ApplySourceFilter("local");
        });
        dialog.AddItemWithHighlight("仅网络音乐", _currentSourceFilter == "network", () =>
        {
            _currentSourceFilter = "network";
            SaveSourceFilter("network");
            _viewModel.ApplySourceFilter("network");
        });

        dialog.Show();
    }

    private void OnSortClicked(object? sender, EventArgs e)
    {
        var ctx = Context;
        if (ctx == null) return;

        var prefs = Activity?.GetSharedPreferences("playlist_sort", Android.Content.FileCreationMode.Private);
        var currentSort = prefs?.GetString($"sort_{_playlistId}", "title") ?? "title";

        var dialog = new GlassDialog(ctx).SetTitle("排序");

        dialog.AddItemWithHighlight("标题", currentSort == "title", () => ApplySort("title", s => s.Title ?? "", false));
        dialog.AddItemWithHighlight("文件名", currentSort == "filename", () => ApplySort("filename", s => System.IO.Path.GetFileNameWithoutExtension(s.FilePath ?? ""), false));
        dialog.AddItemWithHighlight("专辑", currentSort == "album", () => ApplySort("album", s => s.Album ?? "", false));
        dialog.AddItemWithHighlight("艺术家", currentSort == "artist", () => ApplySort("artist", s => s.Artist ?? "", false));
        dialog.AddItemWithHighlight("大小", currentSort == "size", () => ApplySort("size", s => s.FileSize.ToString(), false));
        dialog.AddItemWithHighlight("年份", currentSort == "year", () => ApplySort("year", s => s.Year.ToString(), false));
        dialog.AddItemWithHighlight("文件夹", currentSort == "folder", () => ApplySort("folder", s => System.IO.Path.GetDirectoryName(s.FilePath ?? "") ?? "", false));
        dialog.AddItemWithHighlight("播放次数", currentSort == "playcount", () => ApplySort("playcount", s => s.PlayCount.ToString(), true));
        dialog.AddItemWithHighlight("时长（短→长）", currentSort == "duration_asc", () => ApplySort("duration_asc", s => s.Duration.ToString(), false));
        dialog.AddItemWithHighlight("时长（长→短）", currentSort == "duration_desc", () => ApplySort("duration_desc", s => s.Duration.ToString(), true));
        dialog.AddItemWithHighlight("修改时间", currentSort == "modified", () => ApplySort("modified", s => s.DateModified.ToString(), false));
        dialog.AddItemWithHighlight("添加时间", currentSort == "added", () => ApplySort("added", s => s.DateAdded.ToString(), false));

        dialog.Show();
    }

    private void ApplySavedSourceFilter()
    {
        if (_playlistId != -1) return;
        var prefs = Activity?.GetSharedPreferences("playlist_sort", Android.Content.FileCreationMode.Private);
        var savedFilter = prefs?.GetString($"source_filter_{_playlistId}", "all") ?? "all";
        if (savedFilter != "all")
        {
            _currentSourceFilter = savedFilter;
            _viewModel.ApplySourceFilter(savedFilter);
        }
    }

    private void SaveSourceFilter(string filter)
    {
        var prefs = Activity?.GetSharedPreferences("playlist_sort", Android.Content.FileCreationMode.Private);
        prefs?.Edit().PutString($"source_filter_{_playlistId}", filter).Apply();
    }

    private void ApplySavedSort()
    {
        var prefs = Activity?.GetSharedPreferences("playlist_sort", Android.Content.FileCreationMode.Private);
        if (prefs == null) return;
        var sortKey = prefs.GetString($"sort_{_playlistId}", null);
        if (string.IsNullOrEmpty(sortKey) || _viewModel.Songs.Count == 0) return;

        var desc = prefs.GetBoolean($"sort_desc_{_playlistId}", false);

        var sortMap = new Dictionary<string, Func<Song, string>>
        {
            ["title"] = s => s.Title ?? "",
            ["filename"] = s => System.IO.Path.GetFileNameWithoutExtension(s.FilePath ?? ""),
            ["album"] = s => s.Album ?? "",
            ["artist"] = s => s.Artist ?? "",
            ["size"] = s => s.FileSize.ToString(),
            ["year"] = s => s.Year.ToString(),
            ["folder"] = s => System.IO.Path.GetDirectoryName(s.FilePath ?? "") ?? "",
            ["playcount"] = s => s.PlayCount.ToString(),
            ["duration_asc"] = s => s.Duration.ToString(),
            ["duration_desc"] = s => s.Duration.ToString(),
            ["modified"] = s => s.DateModified.ToString(),
            ["added"] = s => s.DateAdded.ToString(),
        };

        if (!sortMap.TryGetValue(sortKey, out var selector)) return;

        var songsCopy = _viewModel.Songs.ToList();
        _ = Task.Run(() =>
        {
            var sorted = desc
                ? songsCopy.OrderByDescending(selector).ToList()
                : songsCopy.OrderBy(selector).ToList();
            Activity?.RunOnUiThread(() =>
            {
                _viewModel.Songs.ReplaceAll(sorted);
            });
        });
    }

    private void ApplySort(string sortKey, Func<Song, string> keySelector, bool descending)
    {
        var songsCopy = _viewModel.Songs.ToList();
        _ = Task.Run(() =>
        {
            var sorted = descending
                ? songsCopy.OrderByDescending(keySelector).ToList()
                : songsCopy.OrderBy(keySelector).ToList();
            Activity?.RunOnUiThread(() =>
            {
                _viewModel.Songs.ReplaceAll(sorted);
            });
        });

        var prefs = Activity?.GetSharedPreferences("playlist_sort", Android.Content.FileCreationMode.Private);
        if (prefs != null)
        {
            prefs.Edit().PutString($"sort_{_playlistId}", sortKey)
                      .PutBoolean($"sort_desc_{_playlistId}", descending)
                      .Apply();
        }
    }

    private void OnMultiSelectClicked(object? sender, EventArgs e)
    {
        _isMultiSelectMode = !_isMultiSelectMode;
        _selectedSongIds.Clear();

        if (_isMultiSelectMode)
        {
            _btnMultiSelect.SetImageResource(Resource.Drawable.ic_check_box);
            _adapter.SetMultiSelectMode(true);
            ShowMultiSelectBar();
        }
        else
        {
            _btnMultiSelect.SetImageResource(Resource.Drawable.ic_check_box_outline);
            _adapter.SetMultiSelectMode(false);
            HideMultiSelectBar();
        }
    }

    private void ShowMultiSelectBar()
    {
        if (_multiSelectBar != null) { _multiSelectBar.Visibility = ViewStates.Visible; return; }

        var view = View;
        if (view == null) return;

        _multiSelectBar = new LinearLayout(Context) { Orientation = Orientation.Horizontal };
        _multiSelectBar.SetGravity(GravityFlags.CenterVertical);
        _multiSelectBar.SetBackgroundColor(Android.Graphics.Color.Argb(0xE0, 0x1A, 0x0E, 0x28));
        _multiSelectBar.SetPadding(24, 16, 24, 16);

        var lp = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.WrapContent)
        {
            Gravity = GravityFlags.Bottom
        };
        _multiSelectBar.LayoutParameters = lp;

        AddMultiSelectButton("移出歌单", () => RemoveSelectedFromPlaylist());
        AddMultiSelectButton("添加到歌单", () => AddSelectedToPlaylist());
        AddMultiSelectButton("添加到播放列表", () => AddSelectedToPlayQueue());

        (view as ViewGroup)?.AddView(_multiSelectBar);
    }

    private void AddMultiSelectButton(string text, Action action)
    {
        var btn = new TextView(Context) { Text = text };
        btn.SetTextSize(Android.Util.ComplexUnitType.Sp, 13);
        btn.SetTextColor(Android.Graphics.Color.White);
        btn.SetPadding(24, 12, 24, 12);
        btn.Click += (s, e) => action();
        var lp = new LinearLayout.LayoutParams(0, ViewGroup.LayoutParams.WrapContent, 1f);
        btn.LayoutParameters = lp;
        btn.Gravity = GravityFlags.Center;
        _multiSelectBar?.AddView(btn);
    }

    private void HideMultiSelectBar()
    {
        if (_multiSelectBar != null) _multiSelectBar.Visibility = ViewStates.Gone;
    }

    private async void RemoveSelectedFromPlaylist()
    {
        var adapterSelected = _adapter.GetSelectedSongIds();
        if (adapterSelected.Count == 0) return;
        foreach (var id in adapterSelected.ToList())
            await _viewModel.RemoveSongFromPlaylistAsync(id);
        _selectedSongIds.Clear();
        ExitMultiSelectMode();
        _ = _viewModel.LoadAsync(_playlistId, _viewModel.PlaylistName).ContinueWith(_ =>
        {
            ApplySavedSort();
        });
    }

    private void AddSelectedToPlaylist()
    {
        if (_adapter.GetSelectedSongIds().Count == 0) return;
        var selected = _viewModel.Songs.Where(s => _adapter.GetSelectedSongIds().Contains(s.Id)).ToList();
        ShowAddToPlaylistDialogForSongs(selected);
    }

    private void AddSelectedToPlayQueue()
    {
        if (_adapter.GetSelectedSongIds().Count == 0) return;
        var queue = MainApplication.Services.GetRequiredService<PlayQueue>();
        var selected = _viewModel.Songs.Where(s => _adapter.GetSelectedSongIds().Contains(s.Id)).ToList();
        foreach (var song in selected)
            queue.AddToEnd(song);
        Toast.MakeText(Context!, $"已添加{selected.Count}首到播放列表", ToastLength.Short)?.Show();
        ExitMultiSelectMode();
    }

    private void ExitMultiSelectMode()
    {
        _isMultiSelectMode = false;
        _selectedSongIds.Clear();
        _btnMultiSelect.SetImageResource(Resource.Drawable.ic_check_box_outline);
        _adapter.SetMultiSelectMode(false);
        HideMultiSelectBar();
    }

    private async void ShowAddToPlaylistDialogForSongs(List<Song> songs)
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
                foreach (var song in songs)
                    await _viewModel.AddSongToPlaylistAsync(playlistId, song.Id);
                Activity?.RunOnUiThread(() =>
                    Toast.MakeText(ctx, $"已添加{songs.Count}首到「{p.Name}」", ToastLength.Short)?.Show());
            });
        }
        dialog.AddNegativeButton("取消");
        dialog.Show();
    }

    /// <summary>
    /// Fragment恢复可见时重新加载歌单数据（如果是用户歌单）
    /// </summary>
    public override void OnResume()
    {
        base.OnResume();
        if (_playlistId != 0)
        {
            _ = _viewModel.LoadAsync(_playlistId, _viewModel.PlaylistName).ContinueWith(_ =>
            {
                ApplySavedSort();
                ApplySavedSourceFilter();
            });
        }
    }

    /// <summary>
    /// Fragment销毁时解绑事件处理器
    /// </summary>
    public override void OnDestroyView()
    {
        if (_collectionChangedHandler != null)
            _viewModel.Songs.CollectionChanged -= _collectionChangedHandler;
        if (_audioPlayer != null)
            _audioPlayer.StateChanged -= OnAudioPlayerStateChanged;
        base.OnDestroyView();
    }
}

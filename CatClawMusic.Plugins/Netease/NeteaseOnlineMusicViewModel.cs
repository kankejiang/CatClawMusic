using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.ApplicationModel;

namespace CatClawMusic.Plugins.Netease;

/// <summary>
/// 网易云在线音乐 ViewModel：歌单分类/列表 → 歌单内歌曲/搜索 → 在线播放。
/// <para>
/// 这是原客户端 OnlineMusicViewModel 的插件化版本，简化为单一音源（网易云），
/// 不需要多音源切换。数据全部通过 <see cref="NetEaseMusicPlugin"/> 获取。
/// </para>
/// </summary>
public partial class NeteaseOnlineMusicViewModel : ObservableObject
{
    private readonly IOnlineMusicPlugin _plugin;
    private readonly PlayQueue _queue;
    private readonly IAudioPlayerService _audioPlayer;

    /// <summary>当前是否在浏览排行榜（返回时回歌单广场）</summary>
    private bool _browsingToplists;

    // ── 私人漫游（FM）无限播放 ──
    /// <summary>是否处于私人漫游电台模式（播完自动续播并持续拉新歌补充缓冲）</summary>
    private bool _isFmMode;
    /// <summary>已进入全局播放队列的 FM 歌曲的网易云 id（用于去重，避免重复追加）</summary>
    private readonly HashSet<string> _fmSongIds = new(StringComparer.Ordinal);
    /// <summary>防止并发追加缓冲</summary>
    private bool _fmAppending;
    private const int FmBatchSize = 8;       // 每次补充拉取的数量
    private const int FmBufferThreshold = 2; // 队列中剩余 FM 歌曲 ≤ 此值时开始补充

    // ── 账号登录状态 ──

    [ObservableProperty]
    private bool _isLoggedIn;

    [ObservableProperty]
    private string? _accountName;

    /// <summary>账号按钮文案</summary>
    public string AccountButtonText => IsLoggedIn ? $"👤 {AccountName ?? "已登录"}" : "👤 登录";

    /// <summary>是否支持登录</summary>
    [ObservableProperty]
    private bool _supportsLogin;

    /// <summary>当前音源的浏览器登录配置</summary>
    public BrowserLoginInfo? CurrentLoginInfo { get; private set; }

    // ── 页面状态 ──

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusText = "加载中...";

    // ── 歌单浏览模式 ──

    [ObservableProperty]
    private bool _showPlaylists = true;

    /// <summary>歌单分类 chips</summary>
    public ObservableCollection<CategoryChipItem> Categories { get; } = new()
    {
        new("全部", true), new("华语", false), new("欧美", false), new("日韩", false),
        new("流行", false), new("摇滚", false), new("民谣", false), new("电子", false),
        new("轻音乐", false), new("ACG", false), new("怀旧", false), new("治愈", false),
        new("运动", false), new("夜晚", false),
    };

    [ObservableProperty]
    private string _selectedCategory = "全部";

    /// <summary>歌单列表</summary>
    public ObservableCollection<OnlinePlaylist> Playlists { get; } = new();

    [ObservableProperty]
    private string _playlistStatus = "";

    // ── 歌曲列表模式 ──

    [ObservableProperty]
    private bool _showSongs;

    [ObservableProperty]
    private string _currentListTitle = "";

    /// <summary>歌曲列表</summary>
    public ObservableCollection<OnlineSong> Songs { get; } = new();

    [ObservableProperty]
    private string _songsStatus = "";

    public bool HasPlaylistSongs => Songs.Count > 0;

    // ── 搜索 ──

    [ObservableProperty]
    private string _searchQuery = "";

    public NeteaseOnlineMusicViewModel(IOnlineMusicPlugin plugin, PlayQueue queue, IAudioPlayerService audioPlayer)
    {
        _plugin = plugin;
        _queue = queue;
        _audioPlayer = audioPlayer;
        Songs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPlaylistSongs));
        _audioPlayer.PlaybackCompleted += OnAudioPlaybackCompleted;
    }

    /// <summary>页面出现时加载初始数据</summary>
    public async Task OnAppearingAsync()
    {
        await LoadLoginStateAsync();
        await LoadPlaylistsAsync();
    }

    // ── 歌单 ──

    [RelayCommand]
    public async Task SelectCategoryAsync(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || category == SelectedCategory) return;
        SelectedCategory = category;
        foreach (var c in Categories) c.IsSelected = c.Name == category;
        await LoadPlaylistsAsync();
    }

    /// <summary>加载当前分类的歌单列表</summary>
    public async Task LoadPlaylistsAsync()
    {
        IsLoading = true;
        PlaylistStatus = "正在加载歌单...";
        Playlists.Clear();
        try
        {
            var category = SelectedCategory == "全部" ? null : SelectedCategory;
            var pls = await _plugin.GetPlaylistsAsync(category);
            foreach (var pl in pls ?? new List<OnlinePlaylist>())
                Playlists.Add(pl);
            PlaylistStatus = Playlists.Count == 0 ? "该分类暂无歌单" : "";
            StatusText = "";
        }
        catch (Exception ex)
        {
            PlaylistStatus = $"歌单加载失败：{ex.Message}";
            Log.Debug("NeteasePlugin", $"[Netease] LoadPlaylists failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    public async Task OpenPlaylistAsync(OnlinePlaylist? playlist)
    {
        if (playlist == null) return;
        _isFmMode = false; // 离开私人漫游电台
        IsLoading = true;
        SongsStatus = "正在加载歌曲...";
        CurrentListTitle = playlist.Name;
        Songs.Clear();
        try
        {
            var pageSize = playlist.SongCount > 0 ? playlist.SongCount : 200;
            var songs = await _plugin.GetPlaylistSongsAsync(playlist, 1, pageSize);
            foreach (var s in songs ?? new List<OnlineSong>())
                Songs.Add(s);
            SongsStatus = Songs.Count == 0 ? "歌单为空" : "";
        }
        catch (Exception ex)
        {
            SongsStatus = $"歌曲加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
        ShowPlaylists = false;
        ShowSongs = true;
    }

    [RelayCommand]
    public async Task BackToPlaylistsAsync()
    {
        ShowSongs = false;
        ShowPlaylists = true;
        if (_browsingToplists)
        {
            _browsingToplists = false;
            await LoadPlaylistsAsync();
        }
    }

    [RelayCommand]
    public async Task LoadToplistsAsync()
    {
        IsLoading = true;
        PlaylistStatus = "正在加载排行榜...";
        Playlists.Clear();
        try
        {
            var lists = await _plugin.GetToplistsAsync();
            foreach (var pl in lists) Playlists.Add(pl);
            _browsingToplists = true;
            PlaylistStatus = Playlists.Count == 0 ? "排行榜加载失败" : "";
        }
        catch (Exception ex)
        {
            PlaylistStatus = $"排行榜加载失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
        ShowSongs = false;
        ShowPlaylists = true;
    }

    [RelayCommand]
    public async Task LoadPrivateFmAsync()
    {
        IsLoading = true;
        SongsStatus = "正在获取私人漫游...";
        CurrentListTitle = "🎧 私人漫游";
        Songs.Clear();
        try
        {
            var songs = await _plugin.GetPrivateFmAsync(15);
            foreach (var s in songs ?? new List<OnlineSong>())
                Songs.Add(s);
            SongsStatus = Songs.Count == 0 ? "私人漫游暂无歌曲，稍后再试" : "";
        }
        catch (Exception ex)
        {
            SongsStatus = $"私人漫游获取失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
        _isFmMode = Songs.Count > 0; // 进入私人漫游电台模式
        ShowPlaylists = false;
        ShowSongs = true;
    }

    [RelayCommand]
    public async Task LoadDailyRecommendAsync()
    {
        _isFmMode = false; // 离开私人漫游电台
        IsLoading = true;
        SongsStatus = "正在获取每日推荐...";
        CurrentListTitle = "📅 每日推荐";
        Songs.Clear();
        try
        {
            var songs = await _plugin.GetDailyRecommendAsync(20);
            foreach (var s in songs ?? new List<OnlineSong>())
                Songs.Add(s);
            SongsStatus = Songs.Count == 0 ? "每日推荐暂无歌曲" : "";
        }
        catch (Exception ex)
        {
            SongsStatus = $"每日推荐获取失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
        ShowPlaylists = false;
        ShowSongs = true;
    }

    [RelayCommand]
    public async Task SearchSongsAsync()
    {
        var q = SearchQuery?.Trim();
        if (string.IsNullOrWhiteSpace(q)) return;
        _isFmMode = false; // 离开私人漫游电台
        IsLoading = true;
        SongsStatus = "正在搜索...";
        CurrentListTitle = $"搜索：{q}";
        Songs.Clear();
        try
        {
            var songs = await _plugin.SearchAsync(q, 1, 20);
            foreach (var s in songs ?? new List<OnlineSong>())
                Songs.Add(s);
            SongsStatus = Songs.Count == 0 ? "没有找到相关歌曲，换个关键词试试" : "";
        }
        catch (Exception ex)
        {
            SongsStatus = $"搜索失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
        ShowPlaylists = false;
        ShowSongs = true;
    }

    /// <summary>全部播放：取所有歌曲播放直链，构造临时 Song 入队</summary>
    public async Task PlayAllAsync()
    {
        if (_isFmMode)
        {
            var first = Songs.FirstOrDefault();
            if (first != null) await PlayFmSongAsync(first);
            return;
        }
        _isFmMode = false;
        var views = Songs.ToList();
        if (views.Count == 0) return;
        try
        {
            var tempSongs = new List<Song>();
            foreach (var song in views)
            {
                string? url = null;
                try { url = await _plugin.GetPlayUrlAsync(song); }
                catch { }
                if (string.IsNullOrWhiteSpace(url)) continue;
                tempSongs.Add(new Song
                {
                    Id = -1,
                    Title = song.Title,
                    Artist = song.Artist,
                    Album = song.Album,
                    Duration = (int)(song.DurationMs / 1000),
                    FilePath = url,
                    RemoteId = $"{song.Platform}:{song.Id}",
                    Source = SongSource.Local,
                    AllArtists = song.Artist
                });
            }
            if (tempSongs.Count == 0) return;
            _queue.SetSongs(tempSongs);
            _queue.SelectSong(tempSongs[0].Id);
            await _audioPlayer.PlayAsync(tempSongs[0].FilePath);
        }
        catch { }
    }

    /// <summary>播放单首在线歌曲</summary>
    public async Task PlaySongAsync(OnlineSong song)
    {
        if (_isFmMode)
        {
            await PlayFmSongAsync(song);
            return;
        }
        _isFmMode = false;
        string? playUrl = null;
        try { playUrl = await _plugin.GetPlayUrlAsync(song); }
        catch { }
        if (string.IsNullOrWhiteSpace(playUrl)) return;

        var tmp = new Song
        {
            Id = -1,
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            Duration = (int)(song.DurationMs / 1000),
            FilePath = playUrl,
            RemoteId = $"{song.Platform}:{song.Id}",
            Source = SongSource.Local,
            AllArtists = song.Artist
        };
        try
        {
            _queue.SetSongs(new List<Song> { tmp });
            _queue.SelectSong(tmp.Id);
            await _audioPlayer.PlayAsync(playUrl);
        }
        catch { }
    }

    // ── 私人漫游（FM）无限电台 ──

    /// <summary>
    /// 私人漫游播放：把当前 FM 列表整批入队（每首赋予唯一负 Id 以便队列索引/去重），
    /// 播放被点击的那首，并开启"无限电台"续播——播完自动拉新歌追加到队尾。
    /// 对齐网易云官方私人 FM：进入即一批歌，播完不断续播、边播边拉新。
    /// </summary>
    private async Task PlayFmSongAsync(OnlineSong clicked)
    {
        var list = Songs.ToList();
        if (list.Count == 0) return;

        // 整批解析播放直链，构造带唯一 Id 的临时 Song 入队
        var temp = new List<Song>();
        for (int i = 0; i < list.Count; i++)
        {
            var os = list[i];
            string? url = null;
            try { url = await _plugin.GetPlayUrlAsync(os); }
            catch { }
            if (string.IsNullOrWhiteSpace(url)) continue;
            temp.Add(ToFmSong(os, url, i));
        }
        if (temp.Count == 0) return;

        _queue.SetSongs(temp);
        _fmSongIds.Clear();
        foreach (var os in list)
            if (!string.IsNullOrWhiteSpace(os.Id)) _fmSongIds.Add(os.Id);
        _isFmMode = true;
        _queue.PlayMode = PlayMode.Sequential; // FM 为顺序无限电台，不循环早期歌曲

        var target = temp.FirstOrDefault(s => s.RemoteId == $"{clicked.Platform}:{clicked.Id}") ?? temp[0];
        _queue.SelectSong(target.Id);
        try { await _audioPlayer.PlayAsync(target.FilePath); }
        catch { }
    }

    /// <summary>把在线歌曲转成带唯一负 Id 的临时 Song（FM 用，便于队列索引/去重）</summary>
    private static Song ToFmSong(OnlineSong os, string url, int index) => new()
    {
        Id = -1000000 - index,
        Title = os.Title,
        Artist = os.Artist,
        Album = os.Album,
        Duration = (int)(os.DurationMs / 1000),
        FilePath = url,
        RemoteId = $"{os.Platform}:{os.Id}",
        Source = SongSource.Local,
        AllArtists = os.Artist,
    };

    /// <summary>播放完成事件：仅 FM 模式下补充缓冲，实现无限续播</summary>
    private void OnAudioPlaybackCompleted(object? sender, EventArgs e)
    {
        if (!_isFmMode) return;
        MainThread.BeginInvokeOnMainThread(async () => await EnsureFmBufferAsync());
    }

    /// <summary>若队列中剩余 FM 歌曲不足阈值，则拉取新一批追加到队尾</summary>
    private async Task EnsureFmBufferAsync()
    {
        if (!_isFmMode || _fmAppending) return;
        var songs = _queue.GetSongs();
        var current = _queue.CurrentSong;
        if (current == null || songs.Count == 0) return;
        int idx = -1;
        for (int i = 0; i < songs.Count; i++)
        {
            if (ReferenceEquals(songs[i], current)) { idx = i; break; } // 入队实例与 CurrentSong 为同一对象
        }
        if (idx < 0) return;
        int remaining = songs.Count - idx - 1;
        if (remaining > FmBufferThreshold) return;
        await AppendFmBatchAsync();
    }

    /// <summary>向私人漫游队列追加一批新歌（去重后入队尾）</summary>
    private async Task AppendFmBatchAsync()
    {
        _fmAppending = true;
        try
        {
            var batch = await _plugin.GetPrivateFmAsync(FmBatchSize);
            if (batch == null) return;
            foreach (var os in batch)
            {
                if (os == null || string.IsNullOrWhiteSpace(os.Id) || _fmSongIds.Contains(os.Id)) continue;
                string? url = null;
                try { url = await _plugin.GetPlayUrlAsync(os); } catch { }
                if (string.IsNullOrWhiteSpace(url)) continue;
                _queue.AddToEnd(ToFmSong(os, url, _fmSongIds.Count));
                _fmSongIds.Add(os.Id);
            }
        }
        catch { }
        finally { _fmAppending = false; }
    }

    /// <summary>页面消失时解绑事件、退出 FM 模式，避免悬挂的事件处理器持续追加歌曲</summary>
    public void Detach()
    {
        _audioPlayer.PlaybackCompleted -= OnAudioPlaybackCompleted;
        _isFmMode = false;
        _fmSongIds.Clear();
    }

    // ── 登录 ──

    partial void OnIsLoggedInChanged(bool value) => OnPropertyChanged(nameof(AccountButtonText));
    partial void OnAccountNameChanged(string? value) => OnPropertyChanged(nameof(AccountButtonText));

    public async Task LoadLoginStateAsync()
    {
        try
        {
            IsLoggedIn = await _plugin.IsLoggedInAsync();
            AccountName = IsLoggedIn ? await _plugin.GetAccountNameAsync() ?? "已登录" : null;
            CurrentLoginInfo = await _plugin.GetBrowserLoginInfoAsync();
            SupportsLogin = CurrentLoginInfo != null;
        }
        catch
        {
            IsLoggedIn = false;
            AccountName = null;
            SupportsLogin = false;
            CurrentLoginInfo = null;
        }
    }

    [RelayCommand]
    public async Task LogoutAsync()
    {
        try
        {
            await _plugin.LogoutAsync();
            IsLoggedIn = false;
            AccountName = null;
        }
        catch { }
    }
}

/// <summary>歌单分类 chip 项</summary>
public partial class CategoryChipItem : ObservableObject
{
    public string Name { get; }
    [ObservableProperty]
    private bool _isSelected;

    public CategoryChipItem(string name, bool isSelected)
    {
        Name = name;
        IsSelected = isSelected;
    }
}

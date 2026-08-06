using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 在线音乐中心 ViewModel：音源切换 → 歌单分类/歌单列表 → 歌单内歌曲/搜索 → 播放。
/// 歌单能力由音源插件提供（<see cref="IOnlineMusicPlugin.GetPlaylistsAsync"/> /
/// <see cref="IOnlineMusicPlugin.GetPlaylistSongsAsync"/>），未实现的音源显示空态提示。
/// </summary>
public partial class OnlineMusicViewModel : ObservableObject
{
    private readonly OnlineMusicAggregator _onlineMusic;

    private IOnlineMusicPlugin? _currentProvider;

    /// <summary>已启用的在线音源（chips 切换）</summary>
    public ObservableCollection<OnlineProviderItem> Providers { get; } = new();

    /// <summary>是否有已启用音源</summary>
    [ObservableProperty]
    private bool _hasProviders;

    /// <summary>页面级状态文本（无音源提示等）</summary>
    [ObservableProperty]
    private string _statusText = "加载中...";

    /// <summary>是否正在加载</summary>
    [ObservableProperty]
    private bool _isLoading;

    // ── 歌单浏览模式 ──

    /// <summary>是否显示歌单浏览模式</summary>
    [ObservableProperty]
    private bool _showPlaylists;

    /// <summary>歌单分类 chips（对象化，带选中态）</summary>
    public ObservableCollection<CategoryChipItem> Categories { get; } = new()
    {
        new("全部", true), new("华语", false), new("欧美", false), new("日韩", false),
        new("流行", false), new("摇滚", false), new("民谣", false), new("电子", false),
        new("轻音乐", false), new("ACG", false), new("怀旧", false), new("治愈", false),
        new("运动", false), new("夜晚", false),
    };

    /// <summary>当前选中的歌单分类</summary>
    [ObservableProperty]
    private string _selectedCategory = "全部";

    /// <summary>歌单列表</summary>
    public ObservableCollection<OnlinePlaylist> Playlists { get; } = new();

    /// <summary>歌单加载状态文本</summary>
    [ObservableProperty]
    private string _playlistStatus = "";

    // ── 歌曲列表模式（歌单内歌曲 / 搜索） ──

    /// <summary>是否显示歌曲列表模式</summary>
    [ObservableProperty]
    private bool _showSongs;

    /// <summary>当前列表标题（歌单名 或 "搜索：xxx"）</summary>
    [ObservableProperty]
    private string _currentListTitle = "";

    /// <summary>歌曲列表（包装 OnlineSong 展示）</summary>
    public ObservableCollection<OnlineSongView> Songs { get; } = new();

    /// <summary>歌曲加载状态文本</summary>
    [ObservableProperty]
    private string _songsStatus = "";

    /// <summary>当前歌曲列表是否有歌曲（控制"全部播放"按钮可见性）</summary>
    public bool HasPlaylistSongs => Songs.Count > 0;

    /// <summary>搜索关键词（音源内搜索）</summary>
    [ObservableProperty]
    private string _searchQuery = "";

    /// <summary>
    /// 初始化 <see cref="OnlineMusicViewModel"/> 实例。
    /// </summary>
    /// <param name="onlineMusic">在线音乐聚合器，提供已启用音源列表与播放路由</param>
    public OnlineMusicViewModel(OnlineMusicAggregator onlineMusic)
    {
        _onlineMusic = onlineMusic;
        Songs.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasPlaylistSongs));
    }

    /// <summary>刷新已启用音源并默认选中第一个（页面出现/插件变化后调用）</summary>
    public async Task LoadProvidersAsync()
    {
        var providers = _onlineMusic.GetProviders();
        Providers.Clear();
        foreach (var p in providers)
        {
            var name = string.IsNullOrWhiteSpace(p.PlatformName) ? "在线音源" : p.PlatformName;
            Providers.Add(new OnlineProviderItem { Platform = p.PlatformName, Name = name, Icon = ProviderIcon(name) });
        }
        HasProviders = Providers.Count > 0;
        StatusText = HasProviders ? "" : "还没有可用的在线音源插件\n请到「设置 → 插件管理」添加插件后重试";

        if (HasProviders && _currentProvider == null)
            await SelectProviderAsync(Providers[0]);
    }

    /// <summary>切换音源</summary>
    [RelayCommand]
    public async Task SelectProviderAsync(OnlineProviderItem? item)
    {
        if (item == null) return;
        IOnlineMusicPlugin? provider = null;
        foreach (var p in _onlineMusic.GetProviders())
        {
            if (string.Equals(p.PlatformName, item.Platform, StringComparison.OrdinalIgnoreCase))
            {
                provider = p;
                break;
            }
        }
        if (provider == null) return;

        _currentProvider = provider;
        SelectedCategory = "全部";
        foreach (var c in Categories) c.IsSelected = c.Name == "全部";
        ShowSongs = false;
        ShowPlaylists = true;
        await LoadPlaylistsAsync();
    }

    /// <summary>选择歌单分类</summary>
    [RelayCommand]
    public async Task SelectCategoryAsync(string? category)
    {
        if (string.IsNullOrWhiteSpace(category) || category == SelectedCategory) return;
        SelectedCategory = category;
        foreach (var c in Categories) c.IsSelected = c.Name == category;
        await LoadPlaylistsAsync();
    }

    /// <summary>加载当前音源 + 分类的歌单列表</summary>
    private async Task LoadPlaylistsAsync()
    {
        if (_currentProvider == null) return;
        IsLoading = true;
        PlaylistStatus = "正在加载歌单...";
        Playlists.Clear();
        try
        {
            var category = SelectedCategory == "全部" ? null : SelectedCategory;
            var pls = await _currentProvider.GetPlaylistsAsync(category);
            foreach (var pl in pls ?? new List<OnlinePlaylist>())
                Playlists.Add(pl);
            PlaylistStatus = Playlists.Count == 0
                ? "该音源暂未提供歌单，或该分类暂无歌单"
                : "";
        }
        catch (Exception ex)
        {
            PlaylistStatus = $"歌单加载失败：{ex.Message}";
            Log.Debug("OnlineMusicViewModel", $"[OnlineMusic] LoadPlaylists failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>打开歌单：加载歌单内歌曲并切换到歌曲列表模式</summary>
    [RelayCommand]
    public async Task OpenPlaylistAsync(OnlinePlaylist? playlist)
    {
        if (playlist == null || _currentProvider == null) return;
        IsLoading = true;
        SongsStatus = "正在加载歌曲...";
        CurrentListTitle = playlist.Name;
        Songs.Clear();
        try
        {
            // 外面显示多少首（SongCount），点进去就拉多少首；无 SongCount 时兜底 200
            var pageSize = playlist.SongCount > 0 ? playlist.SongCount : 200;
            var songs = await _currentProvider.GetPlaylistSongsAsync(playlist, 1, pageSize);
            foreach (var s in songs ?? new List<OnlineSong>())
                Songs.Add(new OnlineSongView(s));
            SongsStatus = Songs.Count == 0 ? "歌单为空，或该音源暂无法获取歌单歌曲" : "";
        }
        catch (Exception ex)
        {
            SongsStatus = $"歌曲加载失败：{ex.Message}";
            Log.Debug("OnlineMusicViewModel", $"[OnlineMusic] LoadPlaylistSongs failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
        ShowPlaylists = false;
        ShowSongs = true;
    }

    /// <summary>返回歌单列表模式</summary>
    [RelayCommand]
    public void BackToPlaylists()
    {
        ShowSongs = false;
        ShowPlaylists = true;
    }

    /// <summary>音源内搜索：结果展示在歌曲列表模式</summary>
    [RelayCommand]
    public async Task SearchSongsAsync()
    {
        var q = SearchQuery?.Trim();
        if (string.IsNullOrWhiteSpace(q) || _currentProvider == null) return;
        IsLoading = true;
        SongsStatus = "正在搜索...";
        CurrentListTitle = $"搜索：{q}";
        Songs.Clear();
        try
        {
            var songs = await _currentProvider.SearchAsync(q, 1, 20);
            foreach (var s in songs ?? new List<OnlineSong>())
                Songs.Add(new OnlineSongView(s));
            SongsStatus = Songs.Count == 0 ? "没有找到相关歌曲，换个关键词试试" : "";
        }
        catch (Exception ex)
        {
            SongsStatus = $"搜索失败：{ex.Message}";
            Log.Debug("OnlineMusicViewModel", $"[OnlineMusic] Search failed: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
        ShowPlaylists = false;
        ShowSongs = true;
    }

    private static string ProviderIcon(string name)
    {
        if (name.Contains("Apple", StringComparison.OrdinalIgnoreCase)) return "🍎";
        if (name.Contains("酷狗", StringComparison.OrdinalIgnoreCase)) return "🐶";
        if (name.Contains("网易", StringComparison.OrdinalIgnoreCase)) return "🎵";
        if (name.Contains("QQ", StringComparison.OrdinalIgnoreCase)) return "🐧";
        if (name.Contains("Soda", StringComparison.OrdinalIgnoreCase)) return "🧃";
        return "🎧";
    }
}

/// <summary>在线音源入口展示项</summary>
public class OnlineProviderItem
{
    /// <summary>来源平台标识（netease / qq / kugou / soda / apple）</summary>
    public string Platform { get; set; } = "";
    /// <summary>音源展示名</summary>
    public string Name { get; set; } = "";
    /// <summary>音源图标 Emoji</summary>
    public string Icon { get; set; } = "🎧";
}

/// <summary>歌单分类 chip 项（带选中态）</summary>
public partial class CategoryChipItem : ObservableObject
{
    /// <summary>分类名</summary>
    public string Name { get; }
    /// <summary>是否选中</summary>
    [ObservableProperty]
    private bool _isSelected;

    public CategoryChipItem(string name, bool isSelected)
    {
        Name = name;
        IsSelected = isSelected;
    }
}

/// <summary>在线歌曲展示项（包装 OnlineSong + 展示辅助）</summary>
public class OnlineSongView
{
    /// <summary>原始歌曲数据（播放时使用）</summary>
    public OnlineSong Song { get; }

    public string Title => Song.Title;
    public string Artist => Song.Artist;
    public string Album => Song.Album;
    public string PlatformName => string.IsNullOrWhiteSpace(Song.PlatformName) ? Song.Platform : Song.PlatformName;
    public string? CoverUrl => Song.CoverUrl;
    /// <summary>时长文本（m:ss）</summary>
    public string DurationText
    {
        get
        {
            var sec = Song.DurationMs / 1000;
            if (sec <= 0) return "";
            return $"{(sec / 60)}:{sec % 60:00}";
        }
    }

    public OnlineSongView(OnlineSong song)
    {
        Song = song;
    }
}

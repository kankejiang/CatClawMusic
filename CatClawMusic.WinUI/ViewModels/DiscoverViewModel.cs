using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 发现页（照 Android 端 `LandscapeDiscover`）：
/// 左「每日推荐」英雄卡 + 右「5 个彩色入口 + 货架面板（歌曲/专辑/艺人 分段）」+ 下方「本周精选」榜单。
///
/// 数据全部来自复用的 <see cref="IMusicLibraryService"/>：
/// 每日推荐 = 最近添加的第一首（Android 也是从 fresh 里挑，「根据口味生成」目前是文案）；
/// 本周精选 = 播放次数最高的几首（PlayCount 由数据库查询回填）。
/// </summary>
public sealed partial class DiscoverViewModel : ObservableObject
{
    private readonly IMusicLibraryService _library;
    private readonly PlaybackService _playback;

    public DiscoverViewModel(IMusicLibraryService library, PlaybackService playback)
    {
        _library = library;
        _playback = playback;
    }

    // ── 货架数据 ──
    public ObservableCollection<SongRow> RecSongs { get; } = [];
    public ObservableCollection<AlbumRow> RecAlbums { get; } = [];
    public ObservableCollection<ArtistRow> RecArtists { get; } = [];

    /// <summary>本周精选（播放次数 Top 5，无播放记录时退回最近添加）</summary>
    public ObservableCollection<SongRow> TopSongs { get; } = [];

    /// <summary>全部曲库（「随机播放」用；避免每次点都重新查库）</summary>
    private List<Song> _allSongs = [];

    [ObservableProperty]
    public partial SongRow? HeroSong { get; set; }

    [ObservableProperty]
    public partial string Greeting { get; set; } = "晚上好，为你精选今日好歌";

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    /// <summary>「收藏」入口的计数（Android 那颗心上有数字）</summary>
    [ObservableProperty]
    public partial int FavoriteCount { get; set; }

    /// <summary>货架当前分段：0=歌曲 / 1=专辑 / 2=艺人</summary>
    [ObservableProperty]
    public partial int ShelfTab { get; set; }

    /// <summary>分段切换后由页面调 UpdateShelfVisibility（XAML 里三个 ListView 显隐交替）。</summary>
    public event EventHandler? ShelfTabChanged;

    partial void OnShelfTabChanged(int value) => ShelfTabChanged?.Invoke(this, EventArgs.Empty);

    /// <summary>页面首次显示时调用。空库时给出可操作提示，而不是一片空白。</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        if (IsBusy)
            return;

        IsBusy = true;
        try
        {
            _allSongs = await _library.GetAllSongsAsync();
            FavoriteCount = await _library.GetFavoriteSongCountAsync();

            BuildHero();
            BuildShelves();
            BuildTopSongs();

            StatusText = _allSongs.Count == 0
                ? "曲库是空的，先到「音乐库」选一个音乐文件夹扫描"
                : string.Empty;
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败：{ex.Message}";
            Log.Error("discover", $"发现页加载失败：{ex}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void BuildHero()
    {
        // 每日推荐：优先播放次数最高的那首（「根据口味」至少在数据上说得通），
        // 没有播放记录时退回最近入库的一首。
        var hero = _allSongs
            .OrderByDescending(s => s.PlayCount)
            .ThenByDescending(s => s.DateAdded)
            .FirstOrDefault();

        HeroSong = hero is null ? null : new SongRow(hero);
    }

    private void BuildShelves()
    {
        RecSongs.Clear();
        foreach (var song in _allSongs.OrderByDescending(s => s.DateAdded).Take(10))
            RecSongs.Add(new SongRow(song));

        RecAlbums.Clear();
        foreach (var group in _allSongs
                     .Where(s => !string.IsNullOrWhiteSpace(s.Album))
                     .GroupBy(s => s.Album)
                     .OrderByDescending(g => g.Max(s => s.DateAdded))
                     .Take(8))
        {
            var first = group.First();
            RecAlbums.Add(new AlbumRow(group.Key, first.Artist, group.Count()));
        }

        RecArtists.Clear();
        foreach (var group in _allSongs
                     .Where(s => !string.IsNullOrWhiteSpace(s.Artist))
                     .GroupBy(s => s.Artist)
                     .OrderByDescending(g => g.Count())
                     .Take(10))
        {
            RecArtists.Add(new ArtistRow(group.Key, group.Count()));
        }
    }

    private void BuildTopSongs()
    {
        TopSongs.Clear();

        // 有播放记录按次数排；一首都没播过时退回最近添加，避免榜单是空的。
        var ranked = _allSongs.Where(s => s.PlayCount > 0).OrderByDescending(s => s.PlayCount).Take(5).ToList();
        if (ranked.Count == 0)
            ranked = _allSongs.OrderByDescending(s => s.DateAdded).Take(5).ToList();

        foreach (var song in ranked)
            TopSongs.Add(new SongRow(song));
    }

    // ── 入口动作 ──

    /// <summary>播放全部每周精选 / 或整个曲库</summary>
    [RelayCommand]
    private async Task PlayHeroAsync()
    {
        if (HeroSong is not null)
            await _playback.PlayAsync(_allSongs, HeroSong.Song);
    }

    /// <summary>换一批：从曲库里随机换一首当英雄卡。</summary>
    [RelayCommand]
    private void RefreshHero()
    {
        if (_allSongs.Count <= 1)
            return;

        Song picked;
        do
        {
            picked = _allSongs[Random.Shared.Next(_allSongs.Count)];
        }
        while (HeroSong is not null && picked.Id == HeroSong.Song.Id);

        HeroSong = new SongRow(picked);
    }

    /// <summary>随机播放整个曲库</summary>
    [RelayCommand]
    private async Task ShuffleAllAsync()
    {
        if (_allSongs.Count == 0)
            return;

        var shuffled = _allSongs.OrderBy(_ => Random.Shared.Next()).ToList();
        await _playback.PlayAsync(shuffled, shuffled[0]);
    }

    /// <summary>播放最多播放（本周精选）</summary>
    [RelayCommand]
    private async Task PlayTopAsync()
    {
        if (TopSongs.Count > 0)
            await _playback.PlayAsync(TopSongs.Select(r => r.Song).ToList(), TopSongs[0].Song);
    }

    /// <summary>播放收藏</summary>
    [RelayCommand]
    private async Task PlayFavoritesAsync()
    {
        var favorites = await _library.GetFavoriteSongsAsync();
        if (favorites.Count > 0)
            await _playback.PlayAsync(favorites, favorites[0]);
    }

    [RelayCommand]
    private void PlaySong(SongRow? row)
    {
        if (row is null)
            return;

        _ = _playback.PlayAsync(RecSongs.Select(r => r.Song).ToList(), row.Song);
    }
}
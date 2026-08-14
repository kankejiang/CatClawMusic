using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using Microsoft.Maui.ApplicationModel;

using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>探索域：探索数据加载 / 搜索用全量库 / 扫描后重载 / 示例封面解析。</summary>
public partial class SearchViewModel
{
    public async Task LoadDataAsync()
    {
        // 重入保护：启动时构造函数/PreloadTabData/OnAppearing 可能并发触发同一加载，
        // 只让第一个真正执行，避免重复整库加载与万级历史聚合叠加拖垮主线程。
        if (System.Threading.Interlocked.CompareExchange(ref _loadInProgress, 1, 0) != 0)
            return;
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        var savedDate = Preferences.Default.Get("explore_last_load_date", "");
        var isSameDay = savedDate == today;

        // 同一天且已有数据：只跳过每日推荐生成，但仍刷新常听/最近添加（这些数据会随播放变化）
        if (isSameDay && _allDailyRecommendSongs.Count > 0)
        {
            try
            {
                IsLoading = true;
                var topPlayedTask = _exploreDataService.GetTopPlayedSongsAsync(20);
                var recentTask = _exploreDataService.GetRecentlyAddedSongsAsync(20);
                await Task.WhenAll(topPlayedTask, recentTask);

                _allTopPlayedSongs = topPlayedTask.Result;
                _allRecentAddedSongs = recentTask.Result;

                var artistsTask = _exploreDataService.GetArtistsWithSongCountAsync();
                var albumsTask = _exploreDataService.GetAlbumsWithSongCountAsync();
                await Task.WhenAll(artistsTask, albumsTask);

                var artistsResult = artistsTask.Result;
                var albumsResult = albumsTask.Result;

                // 先以占位（无封面）构建并显示，不阻塞等待封面提取
                var newSongs = _allTopPlayedSongs.Concat(_allRecentAddedSongs).ToList();
                _allArtists = artistsResult.Select(a => new SearchArtistItem { Id = a.Id, Name = a.Name, Subtitle = $"{a.SongCount} 首歌曲", CoverSource = null }).ToList();
                _allAlbums = albumsResult.Select(a => new SearchAlbumItem { Id = a.Id, Title = a.Title, ArtistName = a.ArtistName, Subtitle = $"{a.SongCount} 首歌曲", CoverSource = null }).ToList();

                ApplyFilters();
                _ = LoadFavoritesAndGenerateHeroCards();

                // 后台加载全部艺术家/专辑用于搜索栏匹配（不阻塞主流程）
                _ = LoadAllLibraryForSearchAsync();

                // 后台分块解析封面 + 采样封面，完成后刷新 UI（画质零损失，提取逻辑不变）
                if (newSongs.Count > 0)
                {
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await Services.CoverHelper.BatchResolveCoversAsync(newSongs);
                            await ResolveSampleCoversAsync(artistsResult, albumsResult, newSongs);
                            await MainThread.InvokeOnMainThreadAsync(() => RefreshSearchCovers(artistsResult, albumsResult, newSongs));
                        }
                        catch (Exception ex)
                        {
                            Log.Debug("SearchViewModel", $"[SearchVM] 后台封面解析失败: {ex.Message}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Log.Debug("SearchViewModel", $"[SearchVM] LoadDataAsync(refresh) failed: {ex.Message}");
            }
            finally { IsLoading = false; System.Threading.Interlocked.Exchange(ref _loadInProgress, 0); }
            return;
        }


        try
        {
            IsLoading = true;

            var dailyTask = _exploreDataService.GetDailyRecommendAsync();
            var artistsTask = _exploreDataService.GetArtistsWithSongCountAsync();
            var albumsTask = _exploreDataService.GetAlbumsWithSongCountAsync();
            var topPlayedTask = _exploreDataService.GetTopPlayedSongsAsync(20);
            var recentTask = _exploreDataService.GetRecentlyAddedSongsAsync(20);

            await Task.WhenAll(dailyTask, artistsTask, albumsTask, topPlayedTask, recentTask);

            _allDailyRecommendSongs = dailyTask.Result;
            var artistsResult = artistsTask.Result;
            var albumsResult = albumsTask.Result;
            _allTopPlayedSongs = topPlayedTask.Result;
            _allRecentAddedSongs = recentTask.Result;

            // 先以占位（无封面）构建并显示，不阻塞等待封面提取
            var allSongs = _allDailyRecommendSongs
                .Concat(_allTopPlayedSongs)
                .Concat(_allRecentAddedSongs)
                .ToList();
            _allArtists = artistsResult
                .Select(a => new SearchArtistItem
                {
                    Id = a.Id,
                    Name = a.Name,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = null
                })
                .ToList();
            _allAlbums = albumsResult
                .Select(a => new SearchAlbumItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    ArtistName = a.ArtistName,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = null
                })
                .ToList();

            // 设置今日推荐英雄卡片（封面稍后后台回填）
            if (_allDailyRecommendSongs.Count > 0)
            {
                var featured = _allDailyRecommendSongs[0];
                _featuredSong = featured;
                HasFeaturedSong = true;
                FeaturedSongTitle = featured.Title ?? "";
                FeaturedSongArtist = featured.Artist ?? "";
            }
            else
            {
                HasFeaturedSong = false;
            }

            // 保存日期到 Preferences（跨重启持久化）
            Preferences.Default.Set("explore_last_load_date", today);

            ApplyFilters();
            _ = LoadFavoritesAndGenerateHeroCards();

            // 后台加载全部艺术家/专辑用于搜索栏匹配（不阻塞主流程）
            _ = LoadAllLibraryForSearchAsync();

            // 后台分块解析封面 + 采样封面，完成后刷新 UI（画质零损失，提取逻辑不变）
            _ = Task.Run(async () =>
            {
                try
                {
                    await Services.CoverHelper.BatchResolveCoversAsync(allSongs);
                    await ResolveSampleCoversAsync(artistsResult, albumsResult, allSongs);
                    await MainThread.InvokeOnMainThreadAsync(() => RefreshSearchCovers(artistsResult, albumsResult, allSongs));
                }
                catch (Exception ex)
                {
                    Log.Debug("SearchViewModel", $"[SearchVM] 后台封面解析失败: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            EmptyStateText = $"加载失败：{ex.Message}";
            IsCurrentTabEmpty = true;
            Log.Debug("SearchViewModel", $"[SearchViewModel] 加载探索数据失败: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
            System.Threading.Interlocked.Exchange(ref _loadInProgress, 0);
        }
    }

    /// <summary>加载探索数据（与 <see cref="LoadDataAsync"/> 等价）</summary>
    public async Task LoadExploreDataAsync()
    {
        await LoadDataAsync();
        // 后台准备当天 AI 歌单（每天一次；未开启智能推荐/未配置模型时自动跳过）
        _ = EnsureDailyAiPlaylistsAsync();
    }

    /// <summary>
    /// 后台加载全部艺术家/专辑用于搜索栏匹配。
    /// 每日推荐只随机展示 10 个艺术家/专辑，但搜索栏需要能匹配到库内任意艺术家/专辑，
    /// 因此单独加载全量列表供 <see cref="UpdateSearchDropdownAsync"/> 使用。
    /// </summary>
    private async Task LoadAllLibraryForSearchAsync()
    {
        try
        {
            var allArtistsTask = _exploreDataService.GetAllArtistsAsync();
            var allAlbumsTask = _exploreDataService.GetAllAlbumsAsync();
            var allSongsTask = _libraryService.GetMergedSongsAsync();
            // ConfigureAwait(false)：全量艺术家/专辑/歌曲列表的 materialize（含每条 PathToImageSource）
            // 改在后台线程执行，避免在主线程构建上千条项造成进入发现页时的卡顿。
            await Task.WhenAll(allArtistsTask, allAlbumsTask, allSongsTask).ConfigureAwait(false);

            _allArtistsForSearch = allArtistsTask.Result
                .Select(a => new SearchArtistItem
                {
                    Id = a.Id,
                    Name = a.Name,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.Cover))
                })
                .ToList();
            _allAlbumsForSearch = allAlbumsTask.Result
                .Select(a => new SearchAlbumItem
                {
                    Id = a.Id,
                    Title = a.Title,
                    ArtistName = a.ArtistName,
                    Subtitle = $"{a.SongCount} 首歌曲",
                    CoverSource = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.CoverArtPath, a.Cover))
                })
                .ToList();
            // 全量歌曲（本地+网络，已按启用协议过滤、含艺术家/专辑名），供搜索匹配库内任意歌曲
            _allSongsForSearch = allSongsTask.Result;
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] LoadAllLibraryForSearch failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 扫描完成后重新加载探索数据：清除所有缓存并强制全量刷新。
    /// 在 LocalScanService.NeedsReload 标记为 true 时由页面 OnAppearing 调用。
    /// </summary>
    public async Task ReloadAfterScanAsync()
    {
        try
        {
            _exploreDataService.InvalidateDailyRecommendCache();
            InvalidateAiCache();
            InvalidateAiPlaylistsCache();
            Services.CoverHelper.ClearCache();
            Preferences.Default.Remove("explore_last_load_date");

            _allDailyRecommendSongs = [];
            _allTopPlayedSongs = [];
            _allArtists = [];
            _allAlbums = [];
            _allArtistsForSearch = [];
            _allAlbumsForSearch = [];
            _allSongsForSearch = [];
            _allRecentAddedSongs = [];
            ApplyFilters();

            await LoadDataAsync();

            // 扫描后数据已刷新：重新准备当天 AI 歌单（缓存已失效，重新生成）
            _ = EnsureDailyAiPlaylistsAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] ReloadAfterScan failed: {ex.Message}");
        }
    }

    /// <summary>
    /// 为艺人/专辑解析采样歌曲的封面。
    /// ExploreDataService 返回的 SampleCoverPath 在扫描后为 null（DB 中 CoverArtPath 未写入），
    /// 此方法根据 SampleSongId 和 SampleFilePath 从音频文件提取嵌入封面并回填 SampleCoverPath。
    /// </summary>
    /// <param name="artists">艺人列表（会被修改 SampleCoverPath）</param>
    /// <param name="albums">专辑列表（会被修改 SampleCoverPath）</param>
    /// <param name="alreadyResolved">已解析封面的歌曲集合，用于跳过重复解析</param>
    private async Task ResolveSampleCoversAsync(
        List<Data.ArtistWithCount> artists,
        List<Data.AlbumWithCount> albums,
        List<Song> alreadyResolved)
    {
        // 从已解析歌曲中建立 songId → CoverArtPath 映射，避免重复提取
        var resolvedMap = new Dictionary<int, string?>();
        foreach (var s in alreadyResolved)
        {
            if (s.Id > 0 && !resolvedMap.ContainsKey(s.Id))
                resolvedMap[s.Id] = s.CoverArtPath;
        }

        // 收集需要解析封面的采样歌曲（去重）
        var pending = new Dictionary<int, Song>();
        foreach (var a in artists)
        {
            if (a.SampleSongId > 0 && !string.IsNullOrEmpty(a.SampleFilePath)
                && !resolvedMap.ContainsKey(a.SampleSongId) && !pending.ContainsKey(a.SampleSongId))
            {
                pending[a.SampleSongId] = new Song { Id = a.SampleSongId, FilePath = a.SampleFilePath };
            }
        }
        foreach (var a in albums)
        {
            if (a.SampleSongId > 0 && !string.IsNullOrEmpty(a.SampleFilePath)
                && !resolvedMap.ContainsKey(a.SampleSongId) && !pending.ContainsKey(a.SampleSongId))
            {
                pending[a.SampleSongId] = new Song { Id = a.SampleSongId, FilePath = a.SampleFilePath };
            }
        }

        if (pending.Count > 0)
        {
            // 缩略图（默认 300）供列表/卡片秒显；发现页卡片需更大尺寸（800）单独再解析一次
            await Services.CoverHelper.BatchResolveCoversAsync(pending.Values);
            foreach (var kv in pending)
            {
                var discoverPath = Services.CoverHelper.ResolveSingleCover(kv.Value, Services.CoverHelper.DiscoverSize);
                resolvedMap[kv.Key] = discoverPath ?? kv.Value.CoverArtPath;
            }
        }

        // 回填 SampleCoverPath
        foreach (var a in artists)
        {
            if (string.IsNullOrEmpty(a.SampleCoverPath)
                && resolvedMap.TryGetValue(a.SampleSongId, out var path)
                && !string.IsNullOrEmpty(path))
                a.SampleCoverPath = path;
        }
        foreach (var a in albums)
        {
            if (string.IsNullOrEmpty(a.SampleCoverPath)
                && resolvedMap.TryGetValue(a.SampleSongId, out var path)
                && !string.IsNullOrEmpty(path))
                a.SampleCoverPath = path;
        }
    }

    /// <summary>
    /// 后台封面解析完成后在主线程刷新搜索页封面：艺术家/专辑卡片 + 今日推荐大图。
    /// 提取逻辑不变（音频内嵌封面 → 1024px 下采样缓存），画质零损失。
    /// </summary>

    public IReadOnlyList<Song> GetSongsForTab(int tabIndex)
    {
        return tabIndex switch
        {
            0 => DailyRecommendSongs.ToList(),
            3 => TopPlayedSongs.ToList(),
            4 => RecentAddedSongs.ToList(),
            _ => []
        };
    }

    /// <summary>搜索防抖令牌，避免每次按键都触发过滤</summary>
}

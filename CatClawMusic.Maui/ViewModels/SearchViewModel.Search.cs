using CatClawMusic.Core.Models;
using CatClawMusic.Data;

using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>搜索域：搜索下拉刷新 / 防抖 / 在线搜索 / 结果过滤。</summary>
public partial class SearchViewModel
{
    private void RefreshSearchCovers(List<Data.ArtistWithCount> artists, List<Data.AlbumWithCount> albums, List<Song> allSongs)
    {
        var artistMap = artists.ToDictionary(a => a.Id, a => a);
        foreach (var item in _allArtists)
            if (artistMap.TryGetValue(item.Id, out var a))
                item.CoverSource = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.Cover));

        var albumMap = albums.ToDictionary(a => a.Id, a => a);
        foreach (var item in _allAlbums)
        {
            if (!albumMap.TryGetValue(item.Id, out var a)) continue;
            var cover = PathToImageSource(FirstNonEmpty(a.SampleCoverPath, a.CoverArtPath, a.Cover));
            if (cover == null)
            {
                // 回退：用专辑内第一首已解析封面的歌曲
                var sampleSong = allSongs.FirstOrDefault(s =>
                    s.Album?.Equals(a.Title, StringComparison.OrdinalIgnoreCase) == true
                    && !string.IsNullOrWhiteSpace(s.CoverArtPath));
                if (sampleSong != null)
                    cover = ImageSource.FromFile(sampleSong.CoverArtPath);
            }
            item.CoverSource = cover;
        }

        // 今日推荐大图封面回填
        if (HasFeaturedSong && _featuredSong != null && !string.IsNullOrEmpty(_featuredSong.CoverArtPath))
            FeaturedSongCover = ImageSource.FromFile(_featuredSong.CoverArtPath);
    }

    /// <summary>获取指定 Tab 下的歌曲列表（用于列表页播放交互）</summary>
    /// <param name="tabIndex">Tab 索引</param>
    /// <returns>该 Tab 下的歌曲只读列表</returns>

    private CancellationTokenSource? _searchDebounceCts;

    partial void OnSearchQueryChanged(string value)
    {
        // 防抖 250ms，避免连续按键重复过滤
        _searchDebounceCts?.Cancel();
        _searchDebounceCts?.Dispose();
        _searchDebounceCts = new CancellationTokenSource();
        _ = UpdateSearchDropdownAsync(value, _searchDebounceCts.Token);
    }

    private async Task UpdateSearchDropdownAsync(string query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            ShowSearchResults = false;
            HasNoSearchResults = false;
            SearchResults.Clear();
            SearchArtistResults.Clear();
            SearchAlbumResults.Clear();
            OnlineSearchResults.Clear();
            HasOnlineSearchResults = false;
            return;
        }

        try
        {
            // 等待防抖窗口（250ms 内若再次按键则取消此次）
            await Task.Delay(250, ct).ConfigureAwait(false);

            var q = query.Trim();

            // 将 LINQ 过滤放到线程池线程执行
            // 搜索使用全量艺术家/专辑列表（_allArtistsForSearch/_allAlbumsForSearch），
            // 而非每日推荐的 10 个，确保能匹配到库内任意艺术家/专辑
            var (songs, artists, albums) = await Task.Run(() =>
            {
                // 歌曲搜索用全量库（_allSongsForSearch）；尚未加载完成时回退到发现页子集，保证尽早可用。
                // 修复原只搜每日推荐/最多播放/最近添加三个子集（共约60首）、库内大多数歌曲搜不到的问题。
                IEnumerable<Song> songSource = _allSongsForSearch.Count > 0
                    ? _allSongsForSearch
                    : _allDailyRecommendSongs.Concat(_allTopPlayedSongs).Concat(_allRecentAddedSongs);
                var songs = songSource
                    .Where(s =>
                        (s.Title?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (s.Artist?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (s.Album?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                    .GroupBy(s => s.Id)
                    .Select(g => g.First())
                    .Take(10)
                    .ToList();

                var searchArtists = _allArtistsForSearch.Count > 0 ? _allArtistsForSearch : _allArtists;
                var artists = searchArtists
                    .Where(a => a.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Take(6)
                    .ToList();

                var searchAlbums = _allAlbumsForSearch.Count > 0 ? _allAlbumsForSearch : _allAlbums;
                var albums = searchAlbums
                    .Where(a =>
                        a.Title.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                        a.ArtistName.Contains(q, StringComparison.OrdinalIgnoreCase))
                    .Take(6)
                    .ToList();

                return (songs, artists, albums);
            }, ct).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();

            // 回到主线程更新 ObservableCollection
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                SearchResults = new ObservableCollection<Song>(songs);
                SearchArtistResults = new ObservableCollection<SearchArtistItem>(artists);
                SearchAlbumResults = new ObservableCollection<SearchAlbumItem>(albums);
                var hasResults = songs.Count > 0 || artists.Count > 0 || albums.Count > 0;
                ShowSearchResults = hasResults;
                HasNoSearchResults = !hasResults;
            });

            // 在线搜索：聚合已启用的音源插件（不阻塞本地结果展示）
            _ = SearchOnlineAsync(q, ct);
        }
        catch (OperationCanceledException)
        {
            // 防抖正常行为，忽略
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[Search] UpdateSearchDropdown failed: {ex.Message}");
        }
    }

    /// <summary>清空搜索下拉结果</summary>
    public void ClearSearchDropdown()
    {
        ShowSearchResults = false;
        HasNoSearchResults = false;
        SearchResults.Clear();
        SearchArtistResults.Clear();
        SearchAlbumResults.Clear();
        OnlineSearchResults.Clear();
        HasOnlineSearchResults = false;
    }

    /// <summary>在线搜索：并行请求所有已启用的音源插件并合并结果（失败静默，不阻塞本地搜索）</summary>
    private async Task SearchOnlineAsync(string keyword, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword)) return;
        try
        {
            MainThread.BeginInvokeOnMainThread(() => IsSearchingOnline = true);
            var results = await _onlineMusic.SearchAllAsync(keyword).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (ct.IsCancellationRequested) return;
                OnlineSearchResults = new ObservableCollection<OnlineSong>(results);
                HasOnlineSearchResults = OnlineSearchResults.Count > 0;
                IsSearchingOnline = false;
            });
        }
        catch (OperationCanceledException)
        {
            MainThread.BeginInvokeOnMainThread(() => IsSearchingOnline = false);
        }
        catch
        {
            MainThread.BeginInvokeOnMainThread(() => IsSearchingOnline = false);
        }
    }

    /// <summary>从搜索入口直接发送消息（自动进入聊天模式）</summary>
    /// <param name="message">要发送的消息</param>
}

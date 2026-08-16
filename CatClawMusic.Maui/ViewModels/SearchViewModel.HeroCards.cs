using CatClawMusic.Core.Models;
using CatClawMusic.Data;

using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>Hero 卡域：问候语 / 英雄卡生成 / 收藏加载与每日洗牌。</summary>
public partial class SearchViewModel
{
    private string CalculateGreeting()
    {
        var hour = DateTime.Now.Hour;
        return hour switch
        {
            >= 0 and < 6 => "凌晨好，为你精选深夜好歌",
            >= 6 and < 12 => "早上好，为你精选晨间好歌",
            >= 12 and < 18 => "下午好，为你精选午后好歌",
            _ => "晚上好，为你精选今日好歌"
        };
    }

    /// <summary>生成英雄卡片</summary>
    private void GenerateHeroCards()
    {
        var cards = new List<HeroCardItem>();
        var gradients = new (Color Start, Color End)[]
        {
            (Color.FromArgb("#667eea"), Color.FromArgb("#764ba2")),
            (Color.FromArgb("#f093fb"), Color.FromArgb("#f5576c")),
            (Color.FromArgb("#4facfe"), Color.FromArgb("#00f2fe")),
            (Color.FromArgb("#43e97b"), Color.FromArgb("#38f9d7")),
            (Color.FromArgb("#fa709a"), Color.FromArgb("#fee140"))
        };

        // AI 智能推荐卡（首位）
        if (IsAiRecommendationEnabled)
        {
            var today = DateTime.Today.ToString("yyyy-MM-dd");
            if (_aiRecommendBatchDate == today && _aiRecommendBatch.Count > 0)
            {
                // 命中当天缓存：直接从批次中轮换取一首展示，不再调用 AI（零 token 消耗）
                AiRecItem? item = null;
                Song? aiSong = null;
                var start = _aiHeroIndex % _aiRecommendBatch.Count;
                for (int i = 0; i < _aiRecommendBatch.Count; i++)
                {
                    var cand = _aiRecommendBatch[(start + i) % _aiRecommendBatch.Count];
                    var s = ResolveSongById(cand.SongId);
                    if (s != null) { item = cand; aiSong = s; break; }
                }
                if (aiSong != null)
                {
                    _aiRecommendedSong = aiSong;
                    cards.Add(new HeroCardItem
                    {
                        Tag = "✨ AI 智能推荐",
                        Title = aiSong.Title ?? "未知歌曲",
                        Description = string.IsNullOrWhiteSpace(item?.Reason) ? AiRecommendReason : item!.Reason,
                        Song = aiSong,
                        GradientStart = gradients[4].Start,
                        GradientEnd = gradients[4].End
                    });
                }
            }
            else
            {
                // 当天尚无缓存：先用本地挑一首占位，并在后台向 AI 获取「当天全部推荐」（每天仅一次）
                var aiSong = PickAiRecommendedSong();
                _aiRecommendedSong = aiSong;
                if (aiSong != null)
                {
                    cards.Add(new HeroCardItem
                    {
                        Tag = "✨ AI 智能推荐",
                        Title = aiSong.Title ?? "未知歌曲",
                        Description = IsAiRecommending ? "AI 正在分析你的口味…" : AiRecommendReason,
                        Song = aiSong,
                        GradientStart = gradients[4].Start,
                        GradientEnd = gradients[4].End
                    });
                }

                if (_agentService.IsConfigured && !_aiFetchInProgress && _aiAttemptDate != today)
                {
                    _ = EnsureDailyAiRecommendationsAsync(regenerateAfter: true);
                }
            }
        }

        var tags = new[] { "每日推荐", "最多播放", "我的最爱", "随机播放" };

        if (_allDailyRecommendSongs.Count > 0)
        {
            var song = _allDailyRecommendSongs[0];
            cards.Add(new HeroCardItem
            {
                Tag = tags[0],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[0].Start,
                GradientEnd = gradients[0].End
            });
        }

        if (_allTopPlayedSongs.Count > 0)
        {
            var song = _allTopPlayedSongs[0];
            cards.Add(new HeroCardItem
            {
                Tag = tags[1],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[1].Start,
                GradientEnd = gradients[1].End
            });
        }

        if (FavoriteSongs.Count > 0)
        {
            var song = FavoriteSongs[0];
            cards.Add(new HeroCardItem
            {
                Tag = tags[2],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[2].Start,
                GradientEnd = gradients[2].End
            });
        }

        if (_allDailyRecommendSongs.Count > 0)
        {
            var random = new Random();
            var index = random.Next(_allDailyRecommendSongs.Count);
            var song = _allDailyRecommendSongs[index];
            cards.Add(new HeroCardItem
            {
                Tag = tags[3],
                Title = song.Title ?? "未知歌曲",
                Description = $"{song.Artist ?? "未知艺术家"} · {song.Album ?? "未知专辑"}",
                Song = song,
                GradientStart = gradients[3].Start,
                GradientEnd = gradients[3].End
            });
        }

        // 统一设播放图标（WinUI 上 XAML 字面量 Source="ic_xxx" 不渲染，必须代码赋 ImageSource）
        // 用深色图标：播放按钮背景是半透明白底 (#50FFFFFF)，浅色图标会看不见
        // 必须在赋值 HeroCards 之前设好——HeroCardItem 无属性变更通知，赋值后再改绑定不会刷新
        var playIcon = Helpers.ImageSourceHelper.FromNameOriginal("ic_play_dark");
        foreach (var c in cards) c.PlayIcon = playIcon;

        HeroCards = new ObservableCollection<HeroCardItem>(cards.Take(4));
    }

    /// <summary>
    /// 基于听歌数据智能挑选一首 AI 推荐歌曲。
    /// 策略：优先从「常听但非榜首」中随机选择，避免永远推荐同一首。
    /// </summary>

    private async Task RefreshAsync()
    {
        try
        {
            _exploreDataService.InvalidateDailyRecommendCache();
            InvalidateAiCache();
            Services.CoverHelper.ClearCache();
            Preferences.Default.Remove("explore_last_load_date");

            _allDailyRecommendSongs = [];
            _allTopPlayedSongs = [];
            _allArtists = [];
            _allAlbums = [];
            _allRecentAddedSongs = [];
            ApplyFilters();

            // 手动刷新：重新生成当天 AI 歌单（清内存+磁盘缓存 → Ensure 强制重新调用 AI）
            InvalidateAiPlaylistsCache(clearDisk: true);
            _ = EnsureDailyAiPlaylistsAsync();

            await LoadDataAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] RefreshAsync failed: {ex.Message}");
        }
    }

    /// <summary>随机重新排序每日推荐</summary>
    private void ShuffleDaily()
    {
        if (_allDailyRecommendSongs.Count == 0) return;

        var random = new Random();
        _allDailyRecommendSongs = _allDailyRecommendSongs.OrderBy(_ => random.Next()).ToList();
        var shuffled = _allDailyRecommendSongs.Take(20).ToList();
        DailyRecommendSongs = new ObservableCollection<Song>(shuffled);
        _aiHeroIndex++; // 轮换到当天缓存里的下一首 AI 推荐（不重新调用 AI）
        GenerateHeroCards();
    }

    /// <summary>加载收藏歌曲并生成英雄卡片（渲染优先：先显示收藏，封面后台分块解析）</summary>
    private async Task LoadFavoritesAndGenerateHeroCards()
    {
        try
        {
            var favoriteSongs = await _libraryService.GetFavoriteSongsAsync();
            // 发现页"我的最爱"是预览区块（"查看全部"跳独立歌单页 playlistId=-2），只展示最近 N 首。
            // FavoriteList 是嵌在 VerticalStackLayout 里的 CollectionView，失去虚拟化，
            // 无界列表会一次性物化全部行 + 封面 Image 拖垮主线程；限量后与"最多播放"(20)保持一致。
            const int favoritePreviewCount = 20;
            var previewFavorites = favoriteSongs.Count > favoritePreviewCount
                ? favoriteSongs.Take(favoritePreviewCount).ToList()
                : favoriteSongs;
            // 先显示收藏（封面占位）；Song.CoverArtPath 已实现 INPC，封面就绪自动刷新
            FavoriteSongs = new ObservableCollection<Song>(previewFavorites);
            GenerateHeroCards();
            if (previewFavorites.Count > 0)
            {
                _ = Task.Run(async () =>
                {
                    try { await Services.CoverHelper.BatchResolveCoversAsync(previewFavorites); }
                    catch (Exception ex)
                    {
                        Log.Debug("SearchViewModel", $"[SearchVM] 后台收藏封面解析失败: {ex.Message}");
                    }
                });
            }
        }
        catch (Exception ex)
        {
            Log.Debug("SearchViewModel", $"[SearchVM] LoadFavorites failed: {ex.Message}");
            GenerateHeroCards();
        }
    }
}

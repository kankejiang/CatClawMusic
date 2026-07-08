using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 艺术家列表页 ViewModel：从本地数据库加载所有艺术家（含歌曲数量统计），
/// 并为每位艺术家解析示例封面。
/// </summary>
public partial class ArtistsViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreData;

    /// <summary>艺术家集合（含每位艺术家的歌曲数量与示例封面）</summary>
    [ObservableProperty]
    private ObservableCollection<ArtistWithCount> _artists = new();

    /// <summary>是否正在加载艺术家数据</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>状态文本（用于向用户展示加载进度或结果）</summary>
    [ObservableProperty]
    private string _statusText = "加载中...";

    /// <summary>
    /// 初始化 <see cref="ArtistsViewModel"/> 实例。
    /// </summary>
    /// <param name="exploreData">探索页数据服务，用于读取艺术家聚合数据</param>
    public ArtistsViewModel(ExploreDataService exploreData)
    {
        _exploreData = exploreData;
    }

    /// <summary>异步加载所有艺术家：拉取艺术家列表并为每位艺术家解析示例封面路径</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载艺术家...";

            var artists = await _exploreData.GetAllArtistsAsync();

            // 用 SampleSongId + SampleFilePath 构造临时 Song，通过 CoverHelper 从音频文件提取嵌入封面
            await Task.Run(() =>
            {
                var pending = new Dictionary<int, Song>();
                foreach (var artist in artists)
                {
                    // 先检查磁盘缓存是否已有（之前会话可能已提取过）
                    if (artist.SampleSongId > 0)
                    {
                        var cachedPath = Services.CoverHelper.GetCachedPath(artist.SampleSongId);
                        if (File.Exists(cachedPath))
                        {
                            artist.Cover = cachedPath;
                            continue;
                        }
                    }

                    // 缓存未命中，收集需要提取封面的采样歌曲
                    if (artist.SampleSongId > 0 && !string.IsNullOrEmpty(artist.SampleFilePath)
                        && !pending.ContainsKey(artist.SampleSongId))
                    {
                        pending[artist.SampleSongId] = new Song { Id = artist.SampleSongId, FilePath = artist.SampleFilePath };
                    }
                }

                if (pending.Count > 0)
                {
                    Services.CoverHelper.BatchResolveCovers(pending.Values);
                    // 回填解析结果到艺术家
                    foreach (var artist in artists)
                    {
                        if (string.IsNullOrEmpty(artist.Cover) && artist.SampleSongId > 0
                            && pending.TryGetValue(artist.SampleSongId, out var s)
                            && !string.IsNullOrEmpty(s.CoverArtPath))
                        {
                            artist.Cover = s.CoverArtPath;
                        }
                    }
                }
            });

            Artists = new ObservableCollection<ArtistWithCount>(artists);
            StatusText = $"共 {Artists.Count} 位艺术家";
        }
        catch (Exception ex)
        {
            StatusText = $"加载失败: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}

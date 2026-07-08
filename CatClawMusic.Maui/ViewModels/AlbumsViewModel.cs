using CatClawMusic.Core.Models;
using CatClawMusic.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>
/// 专辑列表页 ViewModel：从本地数据库加载所有专辑（含歌曲数量统计），
/// 并为每个专辑解析示例封面。
/// </summary>
public partial class AlbumsViewModel : ObservableObject
{
    private readonly ExploreDataService _exploreData;

    /// <summary>专辑集合（含每张专辑的歌曲数量与示例封面）</summary>
    [ObservableProperty]
    private ObservableCollection<AlbumWithCount> _albums = new();

    /// <summary>是否正在加载专辑数据</summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>状态文本（用于向用户展示加载进度或结果）</summary>
    [ObservableProperty]
    private string _statusText = "加载中...";

    /// <summary>
    /// 初始化 <see cref="AlbumsViewModel"/> 实例。
    /// </summary>
    /// <param name="exploreData">探索页数据服务，用于读取专辑聚合数据</param>
    public AlbumsViewModel(ExploreDataService exploreData)
    {
        _exploreData = exploreData;
    }

    /// <summary>异步加载所有专辑：拉取专辑列表并为每个专辑解析示例封面路径</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载专辑...";

            var albums = await _exploreData.GetAllAlbumsAsync();

            // 用 SampleSongId + SampleFilePath 构造临时 Song，通过 CoverHelper 从音频文件提取嵌入封面
            await Task.Run(() =>
            {
                var pending = new Dictionary<int, Song>();
                foreach (var album in albums)
                {
                    // 先检查磁盘缓存是否已有（之前会话可能已提取过）
                    if (album.SampleSongId > 0)
                    {
                        var cachedPath = Services.CoverHelper.GetCachedPath(album.SampleSongId);
                        if (File.Exists(cachedPath))
                        {
                            album.CoverArtPath = cachedPath;
                            continue;
                        }
                    }

                    // 缓存未命中，收集需要提取封面的采样歌曲
                    if (album.SampleSongId > 0 && !string.IsNullOrEmpty(album.SampleFilePath)
                        && !pending.ContainsKey(album.SampleSongId))
                    {
                        pending[album.SampleSongId] = new Song { Id = album.SampleSongId, FilePath = album.SampleFilePath };
                    }
                }

                if (pending.Count > 0)
                {
                    Services.CoverHelper.BatchResolveCovers(pending.Values);
                    // 回填解析结果到专辑
                    foreach (var album in albums)
                    {
                        if (string.IsNullOrEmpty(album.CoverArtPath) && album.SampleSongId > 0
                            && pending.TryGetValue(album.SampleSongId, out var s)
                            && !string.IsNullOrEmpty(s.CoverArtPath))
                        {
                            album.CoverArtPath = s.CoverArtPath;
                        }
                    }
                }
            });

            Albums = new ObservableCollection<AlbumWithCount>(albums);
            StatusText = $"共 {Albums.Count} 张专辑";
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

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

    /// <summary>异步加载所有艺术家：拉取艺术家列表并为每位艺术家回填示例封面路径</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载艺术家...";

            var artists = await _exploreData.GetAllArtistsAsync();

            await Task.Run(() =>
            {
                foreach (var artist in artists)
                {
                    if (!string.IsNullOrEmpty(artist.SampleCoverPath) && File.Exists(artist.SampleCoverPath))
                    {
                        artist.Cover = artist.SampleCoverPath;
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

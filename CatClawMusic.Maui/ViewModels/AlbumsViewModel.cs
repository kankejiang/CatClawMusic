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

    /// <summary>异步加载所有专辑：拉取专辑列表并为每个专辑回填示例封面路径</summary>
    [RelayCommand]
    public async Task LoadAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "正在加载专辑...";

            var albums = await _exploreData.GetAllAlbumsAsync();

            await Task.Run(() =>
            {
                foreach (var album in albums)
                {
                    if (!string.IsNullOrEmpty(album.SampleCoverPath) && File.Exists(album.SampleCoverPath))
                    {
                        album.CoverArtPath = album.SampleCoverPath;
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

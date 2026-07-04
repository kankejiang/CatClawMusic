using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>艺术家详情页面，展示指定艺术家的专辑与歌曲列表。</summary>
[QueryProperty(nameof(ArtistName), "artistName")]
public partial class ArtistDetailPage : ContentPage
{
    private readonly ArtistDetailViewModel _viewModel;

    /// <summary>获取或设置艺术家名称，作为导航查询参数传入，用于加载对应艺术家的数据。</summary>
    public string ArtistName
    {
        set => _ = _viewModel.LoadArtistCommand.ExecuteAsync(value);
    }

    /// <summary>初始化 <see cref="ArtistDetailPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">艺术家详情页面对应的视图模型。</param>
    public ArtistDetailPage(ArtistDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>在专辑列表中选中某个专辑时触发，清除选中状态并导航到该专辑的详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Album album)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }

            await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title ?? string.Empty)}");
        }
    }

    /// <summary>在歌曲列表中选中某首歌曲时触发，清除选中状态并播放所选歌曲。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }
            await _viewModel.PlaySongCommand.ExecuteAsync(song);
        }
    }
}

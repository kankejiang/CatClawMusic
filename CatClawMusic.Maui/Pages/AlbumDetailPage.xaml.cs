using CatClawMusic.Core.Models;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>专辑详情页面，展示指定专辑中的歌曲列表及相关信息。</summary>
[QueryProperty(nameof(AlbumTitle), "title")]
public partial class AlbumDetailPage : ContentPage
{
    private readonly AlbumDetailViewModel _viewModel;

    /// <summary>获取或设置专辑标题，作为导航查询参数传入，用于加载对应专辑的数据。</summary>
    public string AlbumTitle
    {
        set => _ = _viewModel.LoadAsync(Uri.UnescapeDataString(value ?? string.Empty));
    }

    /// <summary>初始化 <see cref="AlbumDetailPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">专辑详情页面对应的视图模型。</param>
    public AlbumDetailPage(AlbumDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>点击返回按钮时触发，返回到上一级页面。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">事件参数。</param>
    private async void OnBackClicked(object? sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

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

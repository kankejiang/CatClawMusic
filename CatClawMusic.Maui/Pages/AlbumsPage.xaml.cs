using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>专辑列表页面，展示本地音乐库中的所有专辑。</summary>
public partial class AlbumsPage : ContentPage
{
    private readonly AlbumsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    /// <summary>初始化 <see cref="AlbumsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">专辑列表页面对应的视图模型。</param>
    public AlbumsPage(AlbumsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，首次出现时加载专辑列表数据。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            await _viewModel.LoadAsync();
        }
    }

    /// <summary>在专辑列表中选中某个专辑时触发，清除选中状态并导航到该专辑的详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is AlbumWithCount album)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }

            await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title ?? string.Empty)}");
        }
    }
}

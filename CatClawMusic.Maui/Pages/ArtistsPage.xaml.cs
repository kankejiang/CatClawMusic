using CatClawMusic.Data;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>艺术家列表页面，展示本地音乐库中的所有艺术家。</summary>
public partial class ArtistsPage : ContentPage
{
    private readonly ArtistsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    /// <summary>初始化 <see cref="ArtistsPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    /// <param name="viewModel">艺术家列表页面对应的视图模型。</param>
    public ArtistsPage(ArtistsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>当页面显示在屏幕上时触发，首次出现时加载艺术家列表数据。</summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            await _viewModel.LoadAsync();
        }
    }

    /// <summary>在艺术家列表中选中某个艺术家时触发，清除选中状态并导航到该艺术家的详情页。</summary>
    /// <param name="sender">事件源。</param>
    /// <param name="e">选择变更事件参数。</param>
    private async void OnArtistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is ArtistWithCount artist)
        {
            if (sender is CollectionView collectionView)
            {
                collectionView.SelectedItem = null;
            }

            await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name ?? string.Empty)}");
        }
    }
}

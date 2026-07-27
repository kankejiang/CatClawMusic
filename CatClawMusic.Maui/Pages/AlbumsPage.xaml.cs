using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>专辑列表页面（竖屏），展示本地音乐库中的所有专辑，支持搜索、筛选、排序和网格/列表视图切换。
/// 横屏下使用 <see cref="DesktopAlbumsPage"/>。公共 UI 组件见 <see cref="Controls"/> 命名空间。</summary>
public partial class AlbumsPage : ContentPage
{
    private readonly AlbumsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    public AlbumsPage(AlbumsViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_isFirstAppearing)
        {
            _isFirstAppearing = false;
            _ = _viewModel.LoadAsync();
        }
    }

    // === 事件处理 ===

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }
        await Shell.Current.GoToAsync("..");
    }

    private void OnSearchToggled(object? sender, EventArgs e)
        => _viewModel.IsSearchVisible = !_viewModel.IsSearchVisible;

    // ChipList.ChipTapped 事件签名：EventHandler<object?>
    private void OnFilterChipTapped(object? sender, object? item)
    {
        if (item is AlbumsViewModel.FilterChip chip)
            _viewModel.SelectFilter(chip.FilterKey);
    }

    private void OnSortChipTapped(object? sender, object? item)
    {
        if (item is AlbumsViewModel.SortOption option)
            _viewModel.SelectSort(option.Key);
    }

    private async void OnAlbumTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is AlbumWithCount album)
        {
            _viewModel.SelectedAlbum = album;
            await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title ?? string.Empty)}");
        }
    }

    private void OnEraTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is AlbumsViewModel.EraRailItem era)
            _viewModel.SelectEra(era.Key);
    }
}

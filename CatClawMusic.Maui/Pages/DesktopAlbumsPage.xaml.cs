using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式专辑页：5 列网格布局，复用 AlbumsViewModel。</summary>
public partial class DesktopAlbumsPage : ContentPage
{
    private readonly AlbumsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    public DesktopAlbumsPage(AlbumsViewModel viewModel)
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

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay())
            return;
        // 横屏嵌入模式：返回到音乐库 tab
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }
        await Shell.Current.GoToAsync("..");
    }

    private void OnSearchTapped(object? sender, EventArgs e)
    {
        _viewModel.IsSearchVisible = !_viewModel.IsSearchVisible;
    }

    private void OnFilterChipTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumsViewModel.FilterChip chip)
        {
            _viewModel.SelectFilter(chip.FilterKey);
        }
    }

    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumsViewModel.SortOption option)
        {
            _viewModel.SelectSort(option.Key);
        }
    }

    private void OnViewToggleTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.Parent is Grid viewToggle)
        {
            var column = Grid.GetColumn(border);
            _viewModel.IsGridView = column == 0;
        }
    }

    private void OnEraTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumsViewModel.EraRailItem era)
        {
            _viewModel.SelectEra(era.Key);
        }
    }

    private async void OnAlbumTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is AlbumWithCount album)
        {
            _viewModel.SelectedAlbum = album;
            await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title ?? string.Empty)}");
        }
    }
}

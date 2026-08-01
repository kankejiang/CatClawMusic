using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式专辑页：8 列网格布局，复用 AlbumsViewModel。
/// 公共 UI 组件见 <see cref="Controls"/> 命名空间。</summary>
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

    // === 事件处理 ===

    private void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }
        _ = Shell.Current.GoToAsync("..");
    }

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

    private void OnLetterTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is AlbumsViewModel.LetterRailItem letter)
            _viewModel.SelectLetter(letter.Key);
    }
}

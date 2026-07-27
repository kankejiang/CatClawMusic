using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>艺术家列表页面（竖屏），展示本地音乐库中的所有艺术家，支持搜索、筛选、排序和网格/列表视图切换。
/// 横屏下使用 <see cref="DesktopArtistsPage"/>。公共 UI 组件见 <see cref="Controls"/> 命名空间。</summary>
public partial class ArtistsPage : ContentPage
{
    private readonly ArtistsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    public ArtistsPage(ArtistsViewModel viewModel)
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
        if (item is ArtistsViewModel.FilterChip chip)
            _viewModel.SelectFilter(chip.FilterKey);
    }

    private void OnSortChipTapped(object? sender, object? item)
    {
        if (item is ArtistsViewModel.SortOption option)
            _viewModel.SelectSort(option.Key);
    }

    private async void OnArtistTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is ArtistWithCount artist)
        {
            _viewModel.SelectedArtist = artist;
            await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name ?? string.Empty)}");
        }
    }

    private void OnLetterTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is ArtistsViewModel.LetterRailItem letter)
            _viewModel.SelectLetter(letter.Key);
    }

    private async void OnMostPlayedTapped(object? sender, EventArgs e)
    {
        if (_viewModel.MostPlayedArtist != null)
            await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(_viewModel.MostPlayedArtist.Name ?? string.Empty)}");
    }
}

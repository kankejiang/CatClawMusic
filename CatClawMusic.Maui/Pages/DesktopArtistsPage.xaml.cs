using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式艺术家页：8 列网格布局，复用 ArtistsViewModel。
/// 公共 UI 组件见 <see cref="Controls"/> 命名空间。</summary>
public partial class DesktopArtistsPage : ContentPage
{
    private readonly ArtistsViewModel _viewModel;
    private bool _isFirstAppearing = true;

    public DesktopArtistsPage(ArtistsViewModel viewModel)
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

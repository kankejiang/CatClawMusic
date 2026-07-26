using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式艺术家页：5 列网格布局，复用 ArtistsViewModel。</summary>
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
        if (sender is Border border && border.BindingContext is ArtistsViewModel.FilterChip chip)
        {
            _viewModel.SelectFilter(chip.FilterKey);
        }
    }

    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ArtistsViewModel.SortOption option)
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

    private void OnLetterTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ArtistsViewModel.LetterRailItem letter)
        {
            _viewModel.SelectLetter(letter.Key);
        }
    }

    private async void OnArtistTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ArtistWithCount artist)
        {
            _viewModel.SelectedArtist = artist;
            await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name ?? string.Empty)}");
        }
    }

    private async void OnMostPlayedTapped(object? sender, EventArgs e)
    {
        if (_viewModel.MostPlayedArtist != null)
        {
            await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(_viewModel.MostPlayedArtist.Name ?? string.Empty)}");
        }
    }
}

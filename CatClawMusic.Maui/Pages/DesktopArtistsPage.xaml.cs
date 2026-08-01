using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式艺术家页：4 列网格布局，复用 ArtistsViewModel。
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

    // FlexLayout 筛选项点击：从 sender 的 BindingContext 中获取 FilterChip
    private void OnFilterChipTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is FilterChip chip)
            _viewModel.SelectFilter(chip.FilterKey);
    }

    // FlexLayout 排序项点击：从 sender 的 BindingContext 中获取 SortOption
    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is ArtistsViewModel.SortOption option)
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

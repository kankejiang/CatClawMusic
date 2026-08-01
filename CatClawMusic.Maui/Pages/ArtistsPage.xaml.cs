using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

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

    private void OnArtistMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not ArtistWithCount artist) return;
        ShowArtistMorePopup(artist);
    }

    // === AppPopup 弹窗 ===

    private void ShowArtistMorePopup(ArtistWithCount artist)
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        ArtistMorePopup.Title = artist.Name;
        ArtistMorePopup.ClearContent();

        string[] actions = { "查看详情", "播放全部歌曲", "分享" };
        foreach (var action in actions)
        {
            var captured = action;
            var row = new Border
            {
                StrokeShape = new RoundRectangle { CornerRadius = new CornerRadius(10) },
                Stroke = cardBg,
                StrokeThickness = 0,
                BackgroundColor = cardBg,
                Padding = new Thickness(14, 11),
                Margin = new Thickness(0, 0, 0, 6),
                HorizontalOptions = LayoutOptions.Fill
            };
            row.GestureRecognizers.Add(new TapGestureRecognizer
            {
                Command = new Command(async () =>
                {
                    await ArtistMorePopup.CloseAsync();
                    await HandleArtistAction(captured, artist);
                })
            });
            row.Content = new Label
            {
                Text = action,
                FontSize = 14,
                TextColor = textPrimary,
                VerticalOptions = LayoutOptions.Center
            };
            ArtistMorePopup.AddContent(row);
        }

        ArtistMorePopup.Open();
    }

    private async Task HandleArtistAction(string? action, ArtistWithCount artist)
    {
        switch (action)
        {
            case "查看详情":
                await Shell.Current.GoToAsync($"artistdetail?artistName={Uri.EscapeDataString(artist.Name ?? string.Empty)}");
                break;
            case "播放全部歌曲":
                // TODO: 播放该艺术家全部歌曲
                break;
        }
    }
}

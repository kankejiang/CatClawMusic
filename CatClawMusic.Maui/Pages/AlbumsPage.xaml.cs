using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>专辑列表页面（竖屏），展示本地音乐库中的所有专辑，支持搜索、筛选、A-Z 索引、排序和网格/列表视图切换。
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

    private void OnAlbumMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not AlbumWithCount album) return;
        ShowAlbumMorePopup(album);
    }

    // === AppPopup 弹窗 ===

    private void ShowAlbumMorePopup(AlbumWithCount album)
    {
        var textPrimary = (Color)Application.Current!.Resources["TextPrimaryColor"];
        var cardBg = (Color)Application.Current!.Resources["CardBackgroundStrongColor"];

        AlbumMorePopup.Title = album.Title;
        AlbumMorePopup.ClearContent();

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
                    await AlbumMorePopup.CloseAsync();
                    await HandleAlbumAction(captured, album);
                })
            });
            row.Content = new Label
            {
                Text = action,
                FontSize = 14,
                TextColor = textPrimary,
                VerticalOptions = LayoutOptions.Center
            };
            AlbumMorePopup.AddContent(row);
        }

        AlbumMorePopup.Open();
    }

    private async Task HandleAlbumAction(string? action, AlbumWithCount album)
    {
        switch (action)
        {
            case "查看详情":
                await Shell.Current.GoToAsync($"albumdetail?title={Uri.EscapeDataString(album.Title ?? string.Empty)}");
                break;
            case "播放全部歌曲":
                // TODO: 播放该专辑全部歌曲
                break;
        }
    }
}

using CatClawMusic.Data;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;
using Microsoft.Maui.Controls.Shapes;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式专辑页：4 列网格布局，复用 AlbumsViewModel。
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
        // Shell 环境（Android 竖屏）GoToAsync；桌面无 Shell 关闭嵌入恢复原 tab
        if (DesktopNavigation.TryGoToShell("..")) return;
        DesktopNavigation.CloseEmbedded();
    }

    // FlexLayout 筛选项点击：从 sender 的 BindingContext 中获取 FilterChip
    private void OnFilterChipTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is AlbumsViewModel.FilterChip chip)
            _viewModel.SelectFilter(chip.FilterKey);
    }

    // FlexLayout 排序项点击：从 sender 的 BindingContext 中获取 SortOption
    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is AlbumsViewModel.SortOption option)
            _viewModel.SelectSort(option.Key);
    }

    private async void OnAlbumTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is AlbumWithCount album)
        {
            _viewModel.SelectedAlbum = album;
            var title = album.Title ?? string.Empty;
            if (DesktopNavigation.TryGoToShell($"albumdetail?title={Uri.EscapeDataString(title)}")) return;
            DesktopNavigation.OpenAlbumDetail(title);
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
                {
                    var title = album.Title ?? string.Empty;
                    if (DesktopNavigation.TryGoToShell($"albumdetail?title={Uri.EscapeDataString(title)}")) break;
                    DesktopNavigation.OpenAlbumDetail(title);
                    break;
                }
            case "播放全部歌曲":
                // TODO: 播放该专辑全部歌曲
                break;
        }
    }
}

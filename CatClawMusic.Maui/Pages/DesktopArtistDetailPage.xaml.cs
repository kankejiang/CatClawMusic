using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式艺术家详情页：左侧控制面板 + 右侧专辑/歌曲列表（Tab 切换），复用 ArtistDetailViewModel。</summary>
public partial class DesktopArtistDetailPage : ContentPage
{
    private readonly ArtistDetailViewModel _viewModel;

    /// <summary>艺术家名称，设置时触发加载对应艺术家数据。</summary>
    public string ArtistName
    {
        set => _ = _viewModel.LoadArtistCommand.ExecuteAsync(value);
    }

    public DesktopArtistDetailPage(ArtistDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    /// <summary>返回：横屏嵌入模式关嵌入回音乐库 tab，保持侧边栏可见。</summary>
    private void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;

        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }

        if (DesktopNavigation.TryGoToShell("..")) return;
        DesktopNavigation.CloseEmbedded();
    }

    /// <summary>点击专辑：打开该专辑详情（横屏下走 DesktopAlbumDetailPage 嵌入）。</summary>
    private async void OnAlbumSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Album album)
        {
            if (sender is CollectionView collectionView)
                collectionView.SelectedItem = null;

            var title = album.Title ?? string.Empty;
            if (DesktopNavigation.TryGoToShell($"albumdetail?title={Uri.EscapeDataString(title)}")) return;
            DesktopNavigation.OpenAlbumDetail(title);
        }
    }

    /// <summary>点击歌曲：清除选中状态并播放所选歌曲。</summary>
    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (sender is CollectionView collectionView)
                collectionView.SelectedItem = null;
            await _viewModel.PlaySongCommand.ExecuteAsync(song);
        }
    }
}
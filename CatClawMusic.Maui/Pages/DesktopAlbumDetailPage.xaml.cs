using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式专辑详情页：左侧控制面板 + 右侧歌曲列表，复用 AlbumDetailViewModel。</summary>
public partial class DesktopAlbumDetailPage : ContentPage
{
    private readonly AlbumDetailViewModel _viewModel;

    /// <summary>专辑标题，设置时触发加载对应专辑数据。</summary>
    public string AlbumTitle
    {
        set => _ = _viewModel.LoadAsync(Uri.UnescapeDataString(value ?? string.Empty));
    }

    public DesktopAlbumDetailPage(AlbumDetailViewModel viewModel)
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

    /// <summary>在歌曲列表中选中某首歌曲时触发，清除选中状态并播放所选歌曲。</summary>
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
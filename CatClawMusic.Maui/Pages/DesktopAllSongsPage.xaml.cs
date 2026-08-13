using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式全部歌曲页：双列网格歌曲列表，复用 AllSongsViewModel。
/// 公共 UI 组件见 <see cref="Controls"/> 命名空间；本类仅保留横屏专属的网格列数自适应与 AppPopup 弹窗。</summary>
[QueryProperty(nameof(Source), "source")]
public partial class DesktopAllSongsPage : ContentPage, ISongContextMenuHost
{
    private readonly AllSongsViewModel _vm;

    public string Source { get; set; } = "local";

    public DesktopAllSongsPage(AllSongsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _vm.LoadAsync(Source);
    }

    // === 事件处理 ===

    /// <summary>弹出歌曲上下文菜单（由 ⋮ 更多按钮触发）。</summary>
    public void ShowSongMenu(Song song, View row, Point position)
    {
        SongContextMenu.ShowAt(song, row, position, new SongMenuActions
        {
            Play = () => _vm.PlaySongCommand.ExecuteAsync(song),
            PlayNext = () => PlaySongNextAsync(song),
            ToggleFavorite = () => ToggleFavoriteAsync(song),
            SongInfo = () => OpenSongInfoAsync(song),
            GetPlaylists = () => _vm.GetPlaylistsAsync(),
            AddSongToPlaylist = playlistId => _vm.AddSongToPlaylistAsync(playlistId, song.Id)
        });
    }

    private async Task PlaySongNextAsync(Song song)
    {
        if (await _vm.PlaySongNextAsync(song))
            SongContextMenu.Toast("已加入播放队列");
    }

    private async Task ToggleFavoriteAsync(Song song)
    {
        var isFavorite = await _vm.ToggleFavoriteForSongAsync(song);
        SongContextMenu.Toast(isFavorite ? "已收藏" : "已取消收藏");
    }

    private async Task OpenSongInfoAsync(Song song)
    {
        var songId = song.Id.ToString();
        if (DesktopNavigation.TryGoToShell($"songdetail?songId={songId}")) return;
        DesktopNavigation.OpenSongDetail(songId);
        await Task.CompletedTask;
    }

    private void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;

        // 横屏嵌入模式：返回到音乐库 tab，保持侧边栏可见
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }

        // Shell 环境（Android 竖屏）GoToAsync；桌面无 Shell 关闭嵌入恢复原 tab
        if (DesktopNavigation.TryGoToShell("..")) return;
        DesktopNavigation.CloseEmbedded();
    }

    // FlexLayout 排序项点击：从 sender 的 BindingContext 中获取 SortOption
    private void OnSortChipTapped(object? sender, EventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is SortOption option)
            _vm.ToggleSort(option.Key);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            ((CollectionView)sender!).SelectedItem = null;
            await _vm.PlaySongCommand.ExecuteAsync(song);
        }
    }

    private async void OnGridSongTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is Song song)
            await _vm.PlaySongCommand.ExecuteAsync(song);
    }

    /// <summary>点击 ⋮ 更多按钮：以按钮位置为锚点弹出下拉菜单。</summary>
    private void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;
        if (sender is Element recognizer && recognizer.Parent is View moreLabel && moreLabel.Parent is View row)
            ShowSongMenu(song, row, new Point(moreLabel.X + moreLabel.Width / 2, moreLabel.Y + moreLabel.Height / 2));
    }

    private void OnIndexTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int index && index >= 0 && index < _vm.Songs.Count)
        {
            var targetSong = _vm.Songs[index];
            SongListView?.ScrollTo(targetSong, position: ScrollToPosition.MakeVisible);
        }
    }
}

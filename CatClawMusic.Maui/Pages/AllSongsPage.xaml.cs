using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// "全部歌曲"二级页面（竖屏）：支持搜索、多维度排序、A-Z 索引、播放/随机。
/// 横屏下使用 <see cref="DesktopAllSongsPage"/>。公共 UI 组件见 <see cref="Controls"/> 命名空间。
/// </summary>
[QueryProperty(nameof(Source), "source")]
public partial class AllSongsPage : ContentPage, ISongContextMenuHost
{
    private readonly AllSongsViewModel _vm;

    public string Source { get; set; } = "local";

    public AllSongsPage(AllSongsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 立即开始加载（DB 查询在后台线程，页面外壳先渲染；命中缓存则首屏秒出，
        // 封面由 BatchResolveCoversAsync 后台分块解析、经 INPC 自动刷新可见 cell）。
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
        if (DesktopNavigation.TryGoToShell($"songdetail?songId={song.Id}")) return;
        DesktopNavigation.OpenSongDetail(song.Id.ToString());
        await Task.CompletedTask;
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;

        // 横屏嵌入模式：返回到音乐库 tab，保持侧边栏可见
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("library");
            return;
        }

        if (Navigation.NavigationStack.Count > 1)
            await Navigation.PopAsync();
        else
            DesktopNavigation.GoBack();
    }

    // ChipList.ChipTapped 事件签名：EventHandler<object?>
    private void OnSortChipTapped(object? sender, object? item)
    {
        if (item is SortOption option)
            _vm.ToggleSort(option.Key);
    }

    private async void OnSongClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: Song song })
            await _vm.PlaySongCommand.ExecuteAsync(song);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            ((CollectionView)sender!).SelectedItem = null;
            await _vm.PlaySongCommand.ExecuteAsync(song);
        }
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

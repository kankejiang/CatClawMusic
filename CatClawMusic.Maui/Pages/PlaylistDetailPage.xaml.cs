using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 歌单详情页面：展示指定歌单中的歌曲列表，支持搜索、排序、随机播放、A-Z 索引。
/// 样式与 <see cref="AllSongsPage"/> 保持一致。
/// </summary>
[QueryProperty(nameof(PlaylistId), "playlistId")]
[QueryProperty(nameof(PlaylistName), "name")]
public partial class PlaylistDetailPage : ContentPage, ISongContextMenuHost
{
    private readonly PlaylistDetailViewModel _viewModel;
    private int _playlistId;
    private string _playlistName = "";

    /// <summary>获取或设置歌单标识，作为导航查询参数传入，用于加载对应歌单数据。</summary>
    public int PlaylistId
    {
        get => _playlistId;
        set
        {
            _playlistId = value;
            _ = LoadPlaylistIfReady();
        }
    }

    /// <summary>获取或设置歌单名称，作为导航查询参数传入，用于加载对应歌单数据。</summary>
    public string PlaylistName
    {
        get => _playlistName;
        set
        {
            _playlistName = Uri.UnescapeDataString(value ?? "");
            _ = LoadPlaylistIfReady();
        }
    }

    /// <summary>初始化 <see cref="PlaylistDetailPage"/> 类的新实例，并绑定对应的视图模型。</summary>
    public PlaylistDetailPage(PlaylistDetailViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    private async Task LoadPlaylistIfReady()
    {
        if (_playlistId != 0 && !string.IsNullOrEmpty(_playlistName))
            await _viewModel.LoadPlaylistAsync(_playlistId, _playlistName);
    }

    // === 事件处理 ===

    /// <summary>弹出歌曲上下文菜单（由 ⋮ 更多按钮触发）。</summary>
    public void ShowSongMenu(Song song, View row, Point position)
    {
        SongContextMenu.ShowAt(song, row, position, new SongMenuActions
        {
            Play = () => _viewModel.PlaySongCommand.ExecuteAsync(song),
            PlayNext = () => PlaySongNextAsync(song),
            ToggleFavorite = () => ToggleFavoriteAsync(song),
            SongInfo = () => OpenSongInfoAsync(song),
            GetPlaylists = () => _viewModel.GetPlaylistsAsync(),
            AddSongToPlaylist = playlistId => _viewModel.AddSongToPlaylistAsync(playlistId, song.Id)
        });
    }

    /// <summary>
    /// 从 ⋮ 按钮触发下拉菜单：以按钮在行内的位置为锚点弹出。
    /// sender 为 TapGestureRecognizer，其 Parent 为 ⋮ Label，再上一级为行 Grid。
    /// </summary>
    private void ShowSongMenuAtMoreButton(object? sender, Song song)
    {
        if (sender is Element recognizer && recognizer.Parent is View moreLabel && moreLabel.Parent is View row)
            ShowSongMenu(song, row, new Point(moreLabel.X + moreLabel.Width / 2, moreLabel.Y + moreLabel.Height / 2));
    }

    private async Task PlaySongNextAsync(Song song)
    {
        if (await _viewModel.PlaySongNextAsync(song))
            SongContextMenu.Toast("已加入播放队列");
    }

    private async Task ToggleFavoriteAsync(Song song)
    {
        var isFavorite = await _viewModel.ToggleFavoriteForSongAsync(song);
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

        // 横屏嵌入模式：返回到歌单 tab，保持侧边栏可见
        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("playlists");
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
            _viewModel.ToggleSort(option.Key);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is Song song)
        {
            if (sender is CollectionView cv) cv.SelectedItem = null;
            await _viewModel.PlaySongCommand.ExecuteAsync(song);
        }
    }

    private void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;
        ShowSongMenuAtMoreButton(sender, song);
    }

    private void OnIndexTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is int index && index >= 0 && index < _viewModel.Songs.Count)
        {
            var targetSong = _viewModel.Songs[index];
            SongListView?.ScrollTo(targetSong, position: ScrollToPosition.MakeVisible);
        }
    }
}

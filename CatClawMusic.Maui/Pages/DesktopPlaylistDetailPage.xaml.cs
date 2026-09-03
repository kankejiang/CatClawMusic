using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>横屏桌面模式歌单详情页：左侧控制面板 + 右侧排序/搜索/A-Z 歌曲列表，复用 PlaylistDetailViewModel。</summary>
public partial class DesktopPlaylistDetailPage : ContentPage, ISongContextMenuHost
{
    private readonly PlaylistDetailViewModel _viewModel;
    private int _playlistId;
    private string _playlistName = "";

    /// <summary>歌单标识，设置时触发加载对应歌单数据。</summary>
    public int PlaylistId
    {
        get => _playlistId;
        set
        {
            _playlistId = value;
            _ = LoadPlaylistIfReady();
        }
    }

    /// <summary>歌单名称，设置时触发加载对应歌单数据。</summary>
    public string PlaylistName
    {
        get => _playlistName;
        set
        {
            _playlistName = Uri.UnescapeDataString(value ?? "");
            _ = LoadPlaylistIfReady();
        }
    }

    public DesktopPlaylistDetailPage(PlaylistDetailViewModel viewModel)
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

    /// <summary>从 ⋮ 按钮触发下拉菜单：以按钮在行内的位置为锚点弹出。</summary>
    private void ShowSongMenuAtMoreButton(object? sender, Song song)
    {
        if (sender is Element recognizer && recognizer.Parent is View moreLabel && moreLabel.Parent is View row)
            ShowSongMenu(song, row, new Point(moreLabel.X + moreLabel.Width / 2, moreLabel.Y + moreLabel.Height / 2));
    }

    private async void OnSongClicked(object? sender, EventArgs e)
    {
        if (sender is Button { BindingContext: Song song })
            await _viewModel.PlaySongCommand.ExecuteAsync(song);
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
        var songId = song.Id.ToString();
        if (DesktopNavigation.TryGoToShell($"songdetail?songId={songId}")) return;
        DesktopNavigation.OpenSongDetail(songId);
        await Task.CompletedTask;
    }

    private void OnMoreTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not Song song) return;
        ShowSongMenuAtMoreButton(sender, song);
    }

    /// <summary>返回：横屏嵌入模式关嵌入回歌单 tab，保持侧边栏可见。</summary>
    private void OnBackTapped(object? sender, EventArgs e)
    {
        if (PagerNavigator.TryPopOverlay()) return;

        if (App.IsLandscapeMode() && DesktopMainPage.Instance != null)
        {
            DesktopMainPage.Instance.CloseEmbeddedSubPage("playlists");
            return;
        }

        if (DesktopNavigation.TryGoToShell("..")) return;
        DesktopNavigation.CloseEmbedded();
    }

    private void OnSortChipTapped(object? sender, object? item)
    {
        if (item is SortOption option)
            _viewModel.ToggleSort(option.Key);
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
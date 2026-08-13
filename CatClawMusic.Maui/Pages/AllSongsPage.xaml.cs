using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Maui.Controls;
using CatClawMusic.Maui.Helpers;
using CatClawMusic.Maui.Services;
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
    private readonly DownloadManager? _downloadManager;
    private readonly INetworkMusicService? _networkMusicService;

    public string Source { get; set; } = "local";

    public AllSongsPage(AllSongsViewModel vm, DownloadManager? downloadManager = null, INetworkMusicService? networkMusicService = null)
    {
        InitializeComponent();
        _vm = vm;
        _downloadManager = downloadManager;
        _networkMusicService = networkMusicService;
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
            AddSongToPlaylist = playlistId => _vm.AddSongToPlaylistAsync(playlistId, song.Id),
            // 网络歌曲支持下载到本地（下载管理页可查看进度）
            Download = song.Source == SongSource.Local ? null : () => DownloadSongAsync(song)
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

    /// <summary>网络歌曲下载到本地：按协议定位连接配置，复用现有音频流通道写入下载目录</summary>
    private async Task DownloadSongAsync(Song song)
    {
        if (_downloadManager == null || _networkMusicService == null) return;

        try
        {
            var profiles = await _networkMusicService.GetProfilesAsync();
            var profile = profiles.FirstOrDefault(p =>
                (p.Protocol == ProtocolType.SMB && song.Source == SongSource.SMB) ||
                (p.Protocol == ProtocolType.WebDAV && song.Source == SongSource.WebDAV) ||
                (p.Protocol == ProtocolType.Navidrome && (song.Source == SongSource.WebDAV || song.Source == SongSource.Cache)));
            if (profile == null)
            {
                await DisplayAlert("无法下载", "未找到该歌曲对应的网络连接配置，请先在「设置 → 网络音乐」中配置。", "确定");
                return;
            }

            var ext = Path.GetExtension(song.FilePath ?? string.Empty);
            if (string.IsNullOrEmpty(ext) || ext.Length > 10) ext = ".mp3";
            var fileName = $"{SanitizeFileName(song.Title ?? "audio")}{ext}";

            _downloadManager.EnqueueStream(
                song.Title ?? Path.GetFileNameWithoutExtension(song.FilePath ?? "音频"),
                song.RemoteId ?? song.FilePath,
                fileName,
                ct => _networkMusicService.OpenAudioStreamAsync(song, profile));

            await DisplayAlert("已加入下载", $"《{song.Title}》正在下载到：\n{DownloadManager.GetDownloadFolderPath()}\n\n可在「音乐库 → 下载管理」查看进度。", "确定");
        }
        catch (Exception ex)
        {
            await DisplayAlert("下载失败", ex.Message, "确定");
        }
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Where(c => !invalid.Contains(c) && c != '/' && c != '\\').ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "audio" : cleaned;
    }
}

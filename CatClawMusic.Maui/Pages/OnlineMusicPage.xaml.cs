using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Maui.ViewModels;

namespace CatClawMusic.Maui.Pages;

/// <summary>
/// 在线音乐中心页面：音源切换 → 歌单分类/歌单列表 → 歌单内歌曲/搜索 → 在线播放。
/// 歌单能力由音源插件提供，未实现歌单的音源显示空态。
/// </summary>
public partial class OnlineMusicPage : ContentPage
{
    private readonly OnlineMusicViewModel _vm;
    private readonly OnlineMusicAggregator _onlineMusic;
    private readonly PlayQueue _queue;
    private readonly IAudioPlayerService _audioPlayer;

    public OnlineMusicPage(
        OnlineMusicViewModel vm,
        OnlineMusicAggregator onlineMusic,
        PlayQueue queue,
        IAudioPlayerService audioPlayer)
    {
        InitializeComponent();
        _vm = vm;
        _onlineMusic = onlineMusic;
        _queue = queue;
        _audioPlayer = audioPlayer;
        BindingContext = _vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        AdjustPlaylistSpan(); // 先按当前宽度设列数，再加载数据（避免首次布局跳变）
        await _vm.LoadProvidersAsync();
    }

    /// <summary>页面尺寸变化时按宽度自适应歌单列数（窄屏 2/3 列，宽屏 4-6 列）。</summary>
    private void OnPageSizeChanged(object? sender, EventArgs e) => AdjustPlaylistSpan();

    private void AdjustPlaylistSpan()
    {
        var w = Content?.Width ?? 0;
        if (w <= 0) return;
        int span = w switch
        {
            < 500 => 2,    // 手机竖屏
            < 800 => 3,    // 手机横屏 / 小平板
            < 1200 => 4,   // 平板横屏 / 中等窗口
            < 1600 => 5,   // 桌面端标准宽度
            _ => 6,        // 桌面端大屏 / 4K
        };
        if (PlaylistsLayout.Span != span) PlaylistsLayout.Span = span;
    }

    private async void OnBackTapped(object? sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync(".."); } catch { }
    }

    private async void OnPlaylistSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not OnlinePlaylist playlist) return;
        await _vm.OpenPlaylistAsync(playlist);
    }

    private async void OnSongSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is CollectionView cv) cv.SelectedItem = null;
        if (e.CurrentSelection.FirstOrDefault() is not OnlineSongView view) return;
        await PlayOnlineSongAsync(view.Song);
    }

    private async void OnSearchClicked(object? sender, EventArgs e) => await _vm.SearchSongsAsync();

    private async void OnSearchCompleted(object? sender, EventArgs e) => await _vm.SearchSongsAsync();

    /// <summary>在线播放：取播放直链 → 构造临时 Song → 接入现有播放链路（不落库）。</summary>
    private async Task PlayOnlineSongAsync(OnlineSong song)
    {
        string? playUrl = null;
        try
        {
            playUrl = await _onlineMusic.GetPlayUrlAsync(song);
        }
        catch (Exception ex)
        {
            Log.Debug("OnlineMusicPage", $"[OnlineMusic] GetPlayUrl failed: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(playUrl))
        {
            await DisplayAlert("暂不可播放", $"{song.PlatformName} 当前未接入播放直链，可尝试其他音源", "确定");
            return;
        }

        // 构造临时 Song 接入现有播放链路（FilePath 为播放直链，播放页正常显示标题/进度）
        var tmp = new Song
        {
            Id = -1,
            Title = song.Title,
            Artist = song.Artist,
            Album = song.Album,
            Duration = (int)(song.DurationMs / 1000),
            FilePath = playUrl,
            RemoteId = $"{song.Platform}:{song.Id}",
            Source = SongSource.Local,
            AllArtists = song.Artist
        };
        try
        {
            _queue.SetSongs(new List<Song> { tmp });
            _queue.SelectSong(tmp.Id);
            await _audioPlayer.PlayAsync(playUrl);
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }
}

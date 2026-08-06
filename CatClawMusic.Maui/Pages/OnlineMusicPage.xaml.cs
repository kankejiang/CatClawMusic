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

    /// <summary>WinUI handler 附加后触发一次（首次 Layout 时机），确保初始列数正确。</summary>
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        AdjustPlaylistSpan();
    }

    private void OnPageLoaded(object? sender, EventArgs e) => AdjustPlaylistSpan();

    /// <summary>页面尺寸变化时按宽度自适应歌单列数（窄屏 2/3 列，宽屏 4-6 列）。</summary>
    private void OnPageSizeChanged(object? sender, EventArgs e) => AdjustPlaylistSpan();

    private void AdjustPlaylistSpan()
    {
        // 用页面自身的 Width（ContentPage.Width），更准确地反映实际可用宽度；
        // 退而求其次用 Content.Width；都没有就跳过
        var w = Width > 0 ? Width : (Content?.Width ?? 0);
        if (w <= 0) return;
        int span = w switch
        {
            < 600 => 2,    // 手机竖屏
            < 900 => 3,    // 手机横屏 / 小平板
            < 1200 => 4,   // 平板横屏 / 中等窗口
            < 1500 => 5,   // 桌面端常见尺寸（~1280）
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

    /// <summary>歌单/搜索结果"全部播放"：按当前顺序取所有歌曲播放直链，构造临时 Song 入队并从第一首开始播。</summary>
    private async void OnPlayAllTapped(object? sender, TappedEventArgs e)
    {
        var views = _vm.Songs.ToList();
        if (views.Count == 0) return;
        try
        {
            var tempSongs = new List<Song>();
            var failed = new List<string>();
            foreach (var view in views)
            {
                string? url = null;
                try { url = await _onlineMusic.GetPlayUrlAsync(view.Song); }
                catch { }
                if (string.IsNullOrWhiteSpace(url)) { failed.Add(view.Title); continue; }
                tempSongs.Add(new Song
                {
                    Id = -1,
                    Title = view.Song.Title,
                    Artist = view.Song.Artist,
                    Album = view.Song.Album,
                    Duration = (int)(view.Song.DurationMs / 1000),
                    FilePath = url,
                    RemoteId = $"{view.Song.Platform}:{view.Song.Id}",
                    Source = SongSource.Local,
                    AllArtists = view.Song.Artist
                });
            }

            if (tempSongs.Count == 0)
            {
                await DisplayAlert("暂不可播放", $"全部 {views.Count} 首歌曲都无法获取播放直链", "确定");
                return;
            }

            _queue.SetSongs(tempSongs);
            _queue.SelectSong(tempSongs[0].Id);
            await _audioPlayer.PlayAsync(tempSongs[0].FilePath);

            if (failed.Count > 0)
            {
                await DisplayAlert("部分失败", $"已添加 {tempSongs.Count} 首到队列，{failed.Count} 首获取播放直链失败（可换其他音源）", "确定");
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("播放失败", ex.Message, "确定");
        }
    }

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

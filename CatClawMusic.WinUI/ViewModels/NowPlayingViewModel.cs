using System.Collections.ObjectModel;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.Streams;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 播放页：当前曲信息 + 封面 + 滚动歌词。数据仍来自 <see cref="PlaybackService"/>，
/// 歌词走 Core 的 <see cref="ILyricsService"/>（内嵌/外挂/插件自动链路）。
/// </summary>
public sealed partial class NowPlayingViewModel : ObservableObject
{
    private readonly PlaybackService _playback;
    private readonly ILyricsService _lyrics;
    private readonly CoverService _covers;
    private readonly IDesktopLyricService _desktopLyrics;
    private readonly MusicDatabase _db;

    private LrcLyrics? _currentLyrics;
    private int _lyricRequestId;
    private int _currentLineIndex = -1;
    private Song? _loadedSong;

    public NowPlayingViewModel(
        PlaybackService playback,
        ILyricsService lyrics,
        CoverService covers,
        IDesktopLyricService desktopLyrics,
        MusicDatabase db)
    {
        _playback = playback;
        _lyrics = lyrics;
        _covers = covers;
        _desktopLyrics = desktopLyrics;
        _db = db;

        _playback.StateChanged += (_, _) => RefreshFromPlayback();
        _playback.PositionChanged += (_, position) => OnPositionChanged(position);

        RefreshFromPlayback();
    }

    /// <summary>当前行变化（视图据此把这一行滚到中间）</summary>
    public event EventHandler<int>? CurrentLineChanged;

    public ObservableCollection<LyricLineRow> LyricLines { get; } = [];

    [ObservableProperty]
    public partial string Title { get; set; } = "未在播放";

    [ObservableProperty]
    public partial string Artist { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string Album { get; set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasSong { get; set; }

    [ObservableProperty]
    public partial bool IsPlaying { get; set; }

    [ObservableProperty]
    public partial string PlayGlyph { get; set; } = "\uE768";

    [ObservableProperty]
    public partial string ModeGlyph { get; set; } = "\uE8EE";

    [ObservableProperty]
    public partial string ModeTooltip { get; set; } = "列表循环";

    [ObservableProperty]
    public partial string PositionText { get; set; } = "0:00";

    [ObservableProperty]
    public partial string DurationText { get; set; } = "0:00";

    [ObservableProperty]
    public partial double PositionSeconds { get; set; }

    [ObservableProperty]
    public partial double DurationSeconds { get; set; } = 1;

    [ObservableProperty]
    public partial BitmapImage? CoverSource { get; set; }

    [ObservableProperty]
    public partial bool HasLyrics { get; set; }

    [ObservableProperty]
    public partial string LyricsPlaceholder { get; set; } = "选一首歌开始";

    [ObservableProperty]
    public partial double Volume { get; set; } = 1.0;

    [ObservableProperty]
    public partial bool IsDesktopLyricsOn { get; set; }

    [ObservableProperty]
    public partial bool IsFavorite { get; set; }

    public string FavoriteGlyph => IsFavorite ? "\uEB52" : "\uEB51";

    public string FavoriteTooltip => IsFavorite ? "取消收藏" : "收藏";

    partial void OnIsFavoriteChanged(bool value)
    {
        OnPropertyChanged(nameof(FavoriteGlyph));
        OnPropertyChanged(nameof(FavoriteTooltip));
    }

    [RelayCommand]
    private async Task ToggleFavoriteAsync()
    {
        if (_loadedSong is null)
            return;

        var target = !IsFavorite;
        try
        {
            await _db.SetFavoriteAsync(_loadedSong.Id, target);
            IsFavorite = target;
        }
        catch (Exception ex)
        {
            Log.Warn("favorite", $"切换收藏失败：{ex.Message}");
        }
    }

    [RelayCommand]
    private Task TogglePlayAsync() => _playback.TogglePlayPauseAsync();

    [RelayCommand]
    private Task NextAsync() => _playback.NextAsync();

    [RelayCommand]
    private Task PreviousAsync() => _playback.PreviousAsync();

    [RelayCommand]
    private void CyclePlayMode()
    {
        _playback.CyclePlayMode();
        RefreshMode();
    }

    [RelayCommand]
    private void ToggleDesktopLyrics()
    {
        if (_desktopLyrics.IsShowing)
        {
            _desktopLyrics.Hide();
            IsDesktopLyricsOn = false;
        }
        else
        {
            _desktopLyrics.Show();
            IsDesktopLyricsOn = true;
            PushDesktopLyric(TimeSpan.FromSeconds(PositionSeconds));
        }
    }

    public Task SeekAsync(double seconds) => _playback.SeekAsync(TimeSpan.FromSeconds(seconds));

    /// <summary>点歌词行跳转</summary>
    public Task SeekToLineAsync(LyricLineRow? row) =>
        row is null ? Task.CompletedTask : _playback.SeekAsync(row.Timestamp);

    partial void OnVolumeChanged(double value) => _playback.Volume = value;

    private void RefreshFromPlayback()
    {
        var song = _playback.CurrentSong;

        HasSong = song is not null;
        Title = song?.Title ?? "未在播放";
        Artist = song?.Artist ?? string.Empty;
        Album = song?.Album ?? string.Empty;
        IsPlaying = _playback.IsPlaying;
        PlayGlyph = IsPlaying ? "\uE769" : "\uE768";
        Volume = _playback.Volume;

        RefreshMode();
        RefreshProgress(_playback.PositionSeconds, _playback.DurationSeconds);

        if (song?.Id != _loadedSong?.Id)
        {
            _loadedSong = song;
            _ = LoadSongAsync(song);
        }
    }

    private void RefreshMode()
    {
        (ModeGlyph, ModeTooltip) = _playback.PlayMode switch
        {
            PlayMode.Sequential => ("\uE8EE", "顺序播放"),
            PlayMode.SingleRepeat => ("\uE8ED", "单曲循环"),
            PlayMode.Shuffle => ("\uE8B1", "随机播放"),
            _ => ("\uE8EE", "列表循环"),
        };
    }

    private async Task LoadSongAsync(Song? song)
    {
        var requestId = ++_lyricRequestId;

        CoverSource = null;
        _currentLyrics = null;
        _currentLineIndex = -1;
        LyricLines.Clear();
        HasLyrics = false;
        LyricsPlaceholder = song is null ? "选一首歌开始" : "正在加载歌词…";

        if (song is null)
            return;

        try
        {
            IsFavorite = await _db.IsFavoriteAsync(song.Id);
        }
        catch (Exception ex)
        {
            Log.Warn("favorite", $"读取收藏状态失败：{ex.Message}");
        }

        var coverPath = await _covers.GetCoverPathAsync(song);
        if (requestId != _lyricRequestId)
            return;

        if (coverPath is not null)
            CoverSource = await LoadBitmapAsync(coverPath);

        var lyrics = await _lyrics.GetLyricsAsync(song, LyricsSourceMode.Auto);
        if (requestId != _lyricRequestId)
            return;

        if (lyrics is null || lyrics.Lines.Count == 0)
        {
            LyricsPlaceholder = "暂无歌词";
            return;
        }

        _currentLyrics = lyrics;
        for (var i = 0; i < lyrics.Lines.Count; i++)
            LyricLines.Add(new LyricLineRow(lyrics.Lines[i], i));

        HasLyrics = true;
        LyricsPlaceholder = string.Empty;
        UpdateCurrentLine(TimeSpan.FromSeconds(PositionSeconds));
    }

    private void OnPositionChanged(TimeSpan position)
    {
        if (_playback.CurrentSong is null)
            return;

        RefreshProgress(position.TotalSeconds, _playback.DurationSeconds);
        UpdateCurrentLine(position);
    }

    private void RefreshProgress(double position, double duration)
    {
        PositionSeconds = position;
        DurationSeconds = duration > 0 ? duration : Math.Max(1, _playback.CurrentSong?.Duration ?? 0);
        PositionText = FormatTime(position);
        DurationText = FormatTime(duration);
    }

    private void UpdateCurrentLine(TimeSpan position)
    {
        if (_currentLyrics is null || LyricLines.Count == 0)
            return;

        var index = _lyrics.GetCurrentLyricIndex(_currentLyrics, position);
        if (index != _currentLineIndex)
        {
            if (_currentLineIndex >= 0 && _currentLineIndex < LyricLines.Count)
                LyricLines[_currentLineIndex].IsCurrent = false;

            _currentLineIndex = index;

            if (index >= 0 && index < LyricLines.Count)
            {
                LyricLines[index].IsCurrent = true;
                CurrentLineChanged?.Invoke(this, index);
            }
        }

        PushDesktopLyric(position);
    }

    private void PushDesktopLyric(TimeSpan position)
    {
        if (!_desktopLyrics.IsShowing || _currentLyrics is null || LyricLines.Count == 0)
            return;

        string? current = null;
        string? next = null;

        if (_currentLineIndex >= 0 && _currentLineIndex < LyricLines.Count)
            current = LyricLines[_currentLineIndex].Text;
        if (_currentLineIndex + 1 >= 0 && _currentLineIndex + 1 < LyricLines.Count)
            next = LyricLines[_currentLineIndex + 1].Text;

        var progress = _currentLineIndex >= 0 && _currentLineIndex < LyricLines.Count
            ? LyricFillCalculator.ComputeFillProgress(
                LyricLines[_currentLineIndex].Line,
                _currentLineIndex,
                _currentLyrics.Lines,
                position,
                lineMode: LyricLines[_currentLineIndex].Line.WordTimestamps is null)
            : -1;

        _desktopLyrics.UpdateLyricLines(current, next, progress);
    }

    private static async Task<BitmapImage?> LoadBitmapAsync(string path)
    {
        try
        {
            var bitmap = new BitmapImage();
            using IRandomAccessStream stream = await FileRandomAccessStream.OpenAsync(path, FileAccessMode.Read);
            await bitmap.SetSourceAsync(stream);
            return bitmap;
        }
        catch (Exception ex)
        {
            Log.Debug("cover", $"加载封面失败：{path} — {ex.Message}");
            return null;
        }
    }

    private static string FormatTime(double seconds)
    {
        if (seconds <= 0 || double.IsNaN(seconds))
            return "0:00";

        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{time.Minutes}:{time.Seconds:00}";
    }
}

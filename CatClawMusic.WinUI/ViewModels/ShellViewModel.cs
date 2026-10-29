using CatClawMusic.Core.Services;
using CatClawMusic.WinUI.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CatClawMusic.WinUI.ViewModels;

/// <summary>
/// 底部播放条的状态与命令（后续播放页复用同一份）。
/// 只读属性全部由 <see cref="PlaybackService"/> 的事件驱动刷新。
/// </summary>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly PlaybackService _playback;

    public ShellViewModel(PlaybackService playback)
    {
        _playback = playback;
        _playback.StateChanged += (_, _) => RefreshAll();
        _playback.PositionChanged += (_, _) => RefreshProgress();
    }

    public string Title => _playback.CurrentSong?.Title ?? "未在播放";

    public string Subtitle => _playback.CurrentSong is { } song
        ? $"{song.Artist} · {song.Album}"
        : "从音乐库选一首歌开始";

    public bool HasSong => _playback.CurrentSong is not null;

    public bool IsPlaying => _playback.IsPlaying;

    /// <summary>Segoe Fluent Icons：E768 播放 / E769 暂停</summary>
    public string PlayGlyph => IsPlaying ? "\uE769" : "\uE768";

    public string PlayTooltip => IsPlaying ? "暂停" : "播放";

    /// <summary>顺序与列表循环共用 repeat 图标，靠 Tooltip 区分（与 MAUI 的 ic_repeat_all 一致）</summary>
    public string PlayModeGlyph => _playback.PlayMode switch
    {
        PlayMode.SingleRepeat => "\uE8ED",
        PlayMode.Shuffle => "\uE8B1",
        _ => "\uE8EE",
    };

    public string PlayModeTooltip => _playback.PlayMode switch
    {
        PlayMode.Sequential => "顺序播放",
        PlayMode.SingleRepeat => "单曲循环",
        PlayMode.Shuffle => "随机播放",
        _ => "列表循环",
    };

    public string PositionText => FormatTime(_playback.PositionSeconds);

    public string DurationText => FormatTime(_playback.DurationSeconds);

    public double PositionSeconds => _playback.PositionSeconds;

    public double DurationSeconds => _playback.DurationSeconds;

    public double Progress => _playback.Progress;

    [RelayCommand]
    private Task TogglePlayAsync() => _playback.TogglePlayPauseAsync();

    [RelayCommand]
    private Task NextAsync() => _playback.NextAsync();

    [RelayCommand]
    private Task PreviousAsync() => _playback.PreviousAsync();

    [RelayCommand]
    private void CyclePlayMode() => _playback.CyclePlayMode();

    /// <summary>进度条拖动结束后跳转（单位：秒）</summary>
    public Task SeekAsync(double seconds) => _playback.SeekAsync(TimeSpan.FromSeconds(seconds));

    private void RefreshAll()
    {
        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(Subtitle));
        OnPropertyChanged(nameof(HasSong));
        OnPropertyChanged(nameof(IsPlaying));
        OnPropertyChanged(nameof(PlayGlyph));
        OnPropertyChanged(nameof(PlayTooltip));
        OnPropertyChanged(nameof(PlayModeGlyph));
        OnPropertyChanged(nameof(PlayModeTooltip));
        RefreshProgress();
    }

    private void RefreshProgress()
    {
        OnPropertyChanged(nameof(PositionText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(PositionSeconds));
        OnPropertyChanged(nameof(DurationSeconds));
        OnPropertyChanged(nameof(Progress));
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

using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.WinUI.Infrastructure;

namespace CatClawMusic.WinUI.Services;

/// <summary>
/// 播放编排：队列（复用 Core 的 <see cref="PlayQueue"/>）+ 播放内核 + 状态持久化。
/// 界面（底部播放条 / 播放页）只跟这个服务打交道，不直接碰 MediaPlayer。
/// </summary>
public sealed class PlaybackService
{
    private const string KeyLastSongId = "playback_last_song_id";
    private const string KeyMode = "playback_mode";

    private readonly IAudioPlayerService _player;
    private readonly IMusicLibraryService _library;
    private readonly JsonSettingsStore _settings;
    private readonly PlayQueue _queue = new();

    public PlaybackService(IAudioPlayerService player, IMusicLibraryService library, JsonSettingsStore settings)
    {
        _player = player;
        _library = library;
        _settings = settings;

        // 队列模式跨重启恢复（对照 MAUI 的 RecordPlayMode / 启动恢复）。
        _queue.PlayMode = (PlayMode)_settings.Get(KeyMode, (int)PlayMode.ListRepeat);

        _player.PlaybackStateChanged += (_, _) => RaiseState();
        _player.DurationChanged += (_, _) => RaiseState();
        _player.PlaybackCompleted += (_, _) => HandleCompleted();
        _player.PositionChanged += (_, position) => PositionChanged?.Invoke(this, position);
    }

    /// <summary>切歌 / 播放状态 / 模式 / 时长 变化（进度另见 <see cref="PositionChanged"/>）</summary>
    public event EventHandler? StateChanged;

    /// <summary>播放进度（约 300ms 一次）</summary>
    public event EventHandler<TimeSpan>? PositionChanged;

    public Song? CurrentSong => _queue.CurrentSong;

    public bool IsPlaying => _player.IsPlaying;

    public PlayMode PlayMode => _queue.PlayMode;

    public double PositionSeconds => _player.CurrentPosition;

    public double DurationSeconds
    {
        get
        {
            var duration = _player.Duration;
            return duration > 0 ? duration : CurrentSong?.Duration ?? 0;
        }
    }

    public double Progress =>
        DurationSeconds > 0 ? Math.Clamp(PositionSeconds / DurationSeconds, 0, 1) : 0;

    public double Volume
    {
        get => _player.Volume;
        set => _player.Volume = value;
    }

    /// <summary>以指定列表作为队列并从某首开始播</summary>
    public async Task PlayAsync(IReadOnlyList<Song> songs, Song song)
    {
        var list = songs.Count > 0 ? songs : [song];
        _queue.SetSongs(list);
        _queue.SelectSong(song.Id);

        if (_queue.CurrentSong is null)
        {
            // 目标歌曲不在传入列表里（例如列表刚被替换）：退化为播列表首曲。
            _queue.SelectSong(list[0].Id);
        }

        await PlayCurrentAsync();
    }

    /// <summary>整队播放（从第一首开始）</summary>
    public async Task PlayAllAsync(IReadOnlyList<Song> songs)
    {
        if (songs.Count == 0)
            return;

        _queue.SetSongs(songs);
        _queue.SelectSong(songs[0].Id);
        await PlayCurrentAsync();
    }

    public async Task TogglePlayPauseAsync()
    {
        if (CurrentSong is null)
            return;

        if (_player.IsPlaying)
            await _player.PauseAsync();
        else if (_player.CurrentSongFilePath is not null)
            await _player.ResumeAsync();
        else
            await PlayCurrentAsync();
    }

    public async Task NextAsync()
    {
        var song = _queue.Next();
        if (song is null)
        {
            await _player.StopAsync();
            RaiseState();
            return;
        }

        await PlayCurrentAsync();
    }

    /// <summary>上一曲：没有历史时回到本曲开头（与主流播放器一致）</summary>
    public async Task PreviousAsync()
    {
        if (_queue.Previous() is null)
        {
            await _player.SeekAsync(TimeSpan.Zero);
            return;
        }

        await PlayCurrentAsync();
    }

    /// <summary>列表循环 → 单曲循环 → 随机 → 列表循环（与 MAUI 一致）</summary>
    public void CyclePlayMode()
    {
        _queue.PlayMode = PlayMode switch
        {
            PlayMode.ListRepeat => PlayMode.SingleRepeat,
            PlayMode.SingleRepeat => PlayMode.Shuffle,
            PlayMode.Shuffle => PlayMode.ListRepeat,
            _ => PlayMode.ListRepeat,
        };

        _settings.Set(KeyMode, (int)_queue.PlayMode);
        RaiseState();
    }

    public Task SeekAsync(TimeSpan position) => _player.SeekAsync(position);

    /// <summary>把一首歌插到当前播放曲之后（「下一首播放」）</summary>
    public void AddNext(Song song)
    {
        _queue.AddNext(song);
        RaiseState();
    }

    /// <summary>启动时恢复上次的队列与当前曲（不自动开播）</summary>
    public async Task RestoreAsync()
    {
        var songs = await _library.GetAllSongsAsync();
        if (songs.Count == 0)
            return;

        _queue.SetSongs(songs);

        var lastSongId = _settings.Get(KeyLastSongId, 0);
        if (lastSongId > 0 && songs.Any(s => s.Id == lastSongId))
            _queue.SelectSong(lastSongId);

        RaiseState();
    }

    private async Task PlayCurrentAsync()
    {
        var song = _queue.CurrentSong;
        if (song is null)
            return;

        _settings.Set(KeyLastSongId, song.Id);
        RaiseState();

        await _player.PlayAsync(song.FilePath);
        RaiseState();
    }

    private async void HandleCompleted()
    {
        try
        {
            if (CurrentSong is null)
                return;

            if (PlayMode == PlayMode.SingleRepeat)
            {
                await _player.SeekAsync(TimeSpan.Zero);
                await _player.ResumeAsync();
                return;
            }

            await NextAsync();
        }
        catch (Exception ex)
        {
            Log.Error("playback", $"播放完成处理失败：{ex.Message}");
        }
    }

    private void RaiseState() => StateChanged?.Invoke(this, EventArgs.Empty);
}

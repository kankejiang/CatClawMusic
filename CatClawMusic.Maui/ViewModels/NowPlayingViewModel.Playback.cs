using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

using CatClawMusic.Maui.Helpers;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>正在播放 ViewModel —— partial 分域文件。</summary>
public partial class NowPlayingViewModel
{
    private void OnPlaybackStateChanged(object? sender, bool isPlaying)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            IsPlaying = isPlaying;
            PlayPauseIcon = isPlaying ? "\u23f8" : "\u25b6"; // ⏸ or ▶
            PlayPauseIconSource = ImageSourceHelper.FromNamePlayerCtrl(isPlaying ? "ic_notif_pause" : "ic_notif_play", isPlaying ? "ic_notif_pause" : "ic_notif_play");
            PlayPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal(isPlaying ? "ic_notif_pause" : "ic_notif_play");

            // 听歌时长追踪：播放开始时记录起点，暂停时累积时长
            lock (_listenRecordLock)
            {
                if (isPlaying)
                {
                    var currentSongId = _queue.CurrentSong?.Id ?? 0;
                    if (currentSongId > 0)
                    {
                        if (_trackedSongId != currentSongId)
                        {
                            // 换歌后首次播放：重置追踪状态，并为本次聆听建一行播放会话（趋势/时长统计用）
                            _trackedSongId = currentSongId;
                            _pendingListenMs = 0;
                            _currentSessionId = -1;
                            var sid = currentSongId;
                            _ = Task.Run(async () =>
                            {
                                try { _currentSessionId = await _db.LogListenSessionAsync(sid, 0); }
                                catch { }
                            });
                        }
                        _listeningStartUtc = DateTime.UtcNow;
                    }
                }
                else
                {
                    // 暂停：把已播放时长累加到 pending
                    if (_trackedSongId > 0 && _listeningStartUtc != DateTime.MinValue)
                    {
                        var elapsed = (long)(DateTime.UtcNow - _listeningStartUtc).TotalMilliseconds;
                        if (elapsed > 0)
                        {
                            _pendingListenMs += elapsed;
                        }
                        _listeningStartUtc = DateTime.MinValue;
                    }
                }
            }

            // 检测队列当前歌曲是否变化（外部页面播放时触发）
            // 此时 _loadedSongId 还是旧值，需要加载新歌信息更新迷你播放器
            var queueSong = _queue.CurrentSong;
            if (isPlaying && queueSong != null && queueSong.Id != _loadedSongId)
            {
                await LoadCurrentSongAsync(autoPlay: false);
            }
        });
    }

    private void OnDurationChanged(object? sender, double duration)
    {
        // 媒体打开后由平台播放器推送准确总时长
        if (duration > 1 && Math.Abs(Duration - duration) > 0.5)
        {
            Duration = duration;
            TotalTimeDisplay = FormatTime(Duration);
        }

        // 本地歌时长回填：扫描时跳过 duration（避免全文件 IO 卡顿），
        // 播放时拿到真实时长后补写 Song 与数据库，列表时长从 "--:--" 恢复
        if (duration > 1 && _queue.CurrentSong is { Duration: 0, Source: SongSource.Local } song)
        {
            song.Duration = (int)duration;
            _ = Task.Run(async () =>
            {
                try { await _db.UpdateSongDurationAsync(song.Id, (int)duration); }
                catch (Exception ex) { Log.Debug("NowPlaying", $"[NowPlaying] 回填时长失败: {ex.Message}"); }
            });
        }
    }

    private void OnPositionChanged(object? sender, TimeSpan position)
    {
        if (Duration < 1 && _audioService.Duration > 1)
        {
            Duration = _audioService.Duration;
            TotalTimeDisplay = FormatTime(Duration);
        }

        // 滑动列表时跳过非必要 UI 更新（Progress、CurrentTimeDisplay、歌词），
        // 减少 PropertyChanged 绑定开销，让主线程专注处理列表渲染。
        // 滑动停止后由 OnInteractionStateChanged 补一次 UpdateLyricPosition 同步歌词，
        // Progress/CurrentTimeDisplay 会在下一个 tick 自动恢复。
        bool isUserInteracting = _interactionState?.IsUserInteracting ?? false;

        // 进度条与时间显示必须实时更新，不能因列表滚动 / Tab 滑动等交互状态被冻结。
        // 早期实现用「交互中直接 return」会卡死 Progress：一旦交互 refCount 没归零，
        // Progress 永久停止推进，进度条彻底不走（只有切歌/切页等事件偶然解卡才恢复）。
        if (!_isSeeking)
        {
            Progress = position.TotalSeconds;
        }
        else if ((DateTime.UtcNow - _seekStartTime).TotalSeconds >= 10)
        {
            _isSeeking = false;
            Progress = position.TotalSeconds;
        }

        // 仅在整数秒变化时才格式化时间显示，避免每 tick 分配新字符串
        var currentSecond = (int)position.TotalSeconds;
        if (currentSecond != _lastDisplayedSecond)
        {
            _lastDisplayedSecond = currentSecond;
            CurrentTimeDisplay = FormatTime(currentSecond);

            // 每 30 秒定时 flush 聆听时长，防止应用被系统杀死时数据丢失
            if (_lastFlushSecond < 0 || (currentSecond - _lastFlushSecond) >= 30)
            {
                _lastFlushSecond = currentSecond;
                _ = FlushListeningAsync(isFinalFlush: false);
            }
        }

        // 仅在不交互（未滚动列表 / 未滑动 Tab）时更新歌词逐字定位，避免滑动时歌词抖动；
        // 滑动停止后会由 OnInteractionStateChanged 用播放器实时位置补一次同步。
        if (!isUserInteracting)
        {
            UpdateLyricPosition(position);
        }
        else
        {
            // 交互期间（含自动滚动跟随）仍持续更新当前行的逐字着色进度，
            // 避免滚动冻结着色、结束后跳字；只是不切换行索引，避免滚动期间
            // 触发新的 HighlightLine → ScrollToLine 循环。
            UpdateFillProgressOnly(position);
        }

        // 预缓冲：距歌曲结束 PreBufferSeconds 秒时，开始缓冲下一首 + 预取元数据
        if (Duration > 0 && (Duration - position.TotalSeconds) <= AudioCacheService.PreBufferSeconds
            && _preBufferedSongId != _loadedSongId)
        {
            _preBufferedSongId = _loadedSongId;
            var nextSong = _queue.PeekNextSong();
            if (nextSong != null)
            {
                _ = Task.Run(() => PreBufferNextSongAsync(nextSong));
            }
        }
    }

    private void OnPlaybackCompleted(object? sender, EventArgs e)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // 睡眠定时：若处于“播完当前歌曲后停止”等待阶段，则暂停且不自动切下一首
            if (_sleepTimer != null && _sleepTimer.IsWaitingForSongEnd)
            {
                await FlushListeningAsync(isFinalFlush: true);
                _sleepTimer.StopOnSongCompleted();
                return;
            }

            // 播放完成：先 flush 当前歌曲的聆听时长，再切下一首
            await FlushListeningAsync(isFinalFlush: true);
            var next = _queue.PeekNextSong();
            if (next == null)
            {
                // 顺序播放（PlayMode.Sequential）播到队尾：停止，不循环。
                // 避免 FM/顺序队列耗尽后宿主无条件 Next() 导致的"从头重播"。
                await _audioService.StopAsync();
                return;
            }
            _queue.Next();
            await LoadCurrentSongAsync();
        });
    }

    // === Play/Pause ===

    private async Task TogglePlayPauseAsync()
    {
        if (IsPlaying)
        {
            await _audioService.PauseAsync();
            return;
        }

        var song = _queue.CurrentSong;
        if (song == null || string.IsNullOrEmpty(song.FilePath)) return;

        // 同一首歌已加载：使用 Resume 从暂停位置恢复，避免 PlayAsync 重新加载媒体导致从头播放
        if (_audioService.CurrentSongFilePath == song.FilePath)
        {
            await _audioService.ResumeAsync();
        }
        else
        {
            await _audioService.PlayAsync(song.FilePath);
        }
    }

    // === Next / Previous ===

    private async Task PlayNextAsync()
    {
        _queue.Next();
        await LoadCurrentSongAsync();
    }

    private async Task PlayPreviousAsync()
    {
        _queue.Previous();
        await LoadCurrentSongAsync();
    }

    /// <summary>从播放队列中点选一首歌播放</summary>
    private async Task PlaySongFromQueueAsync(Song? song)
    {
        if (song == null) return;
        _queue.SelectSong(song.Id);
        await LoadCurrentSongAsync();
    }

    /// <summary>从播放队列中移除一首歌。若移除的是当前歌曲则切换到下一首</summary>
    private async Task RemoveSongFromQueueAsync(Song? song)
    {
        if (song == null) return;
        var wasCurrent = _queue.CurrentSong?.Id == song.Id;
        _queue.RemoveSong(song.Id);
        if (wasCurrent)
            await LoadCurrentSongAsync();
    }

    /// <summary>获取当前播放队列歌曲列表（供播放列表弹窗使用）</summary>
    public IReadOnlyList<Song> GetQueueSongs() => _queue.GetSongs();

    // === Play Mode Cycling: ListRepeat → SingleRepeat → Shuffle → ListRepeat ===

    private void CyclePlayMode()
    {
        if (_queue.IsFmMode)
        {
            // 私人漫游（FM）电台模式：仅在「单曲循环 ↔ 无限（Sequential）」之间切换，禁用随机/列表循环
            _queue.PlayMode = _queue.PlayMode == PlayMode.SingleRepeat
                ? PlayMode.Sequential
                : PlayMode.SingleRepeat;
        }
        else
        {
            _queue.PlayMode = _queue.PlayMode switch
            {
                PlayMode.ListRepeat => PlayMode.SingleRepeat,
                PlayMode.SingleRepeat => PlayMode.Shuffle,
                PlayMode.Shuffle => PlayMode.ListRepeat,
                _ => PlayMode.ListRepeat
            };
        }

        if (_queue.PlayMode == PlayMode.Shuffle)
            _queue.EnableShuffle();

        RefreshPlayModeDisplay();
    }

    /// <summary>FM 模式切换时刷新模式按钮（无限 ↔ 单曲循环 显示）</summary>
    private void OnIsFmModeChanged(object? sender, EventArgs e)
    {
        RefreshPlayModeDisplay();
    }

    private void RefreshPlayModeDisplay()
    {
        bool fm = _queue.IsFmMode;
        // Windows 上播放模式图标用 SVG 矢量渲染（FromNameVectorThemed），任意尺寸无锯齿
        (PlayModeIcon, PlayModeLabel, PlayModeIconSource) = _queue.PlayMode switch
        {
            PlayMode.ListRepeat => ("\U0001f501", "列表循环", ImageSourceHelper.FromNameVectorThemed("ic_repeat_all")),
            PlayMode.SingleRepeat => ("\U0001f502", "单曲循环", ImageSourceHelper.FromNameVectorThemed("ic_repeat_one")),
            PlayMode.Shuffle => ("\U0001f500", "随机播放", ImageSourceHelper.FromNameVectorThemed("ic_shuffle")),
            PlayMode.Sequential when fm => ("\u221e", "无限", ImageSourceHelper.FromNameVectorThemed("ic_infinite")),
            PlayMode.Sequential => ("\u27a1", "顺序播放", ImageSourceHelper.FromNameVectorThemed("ic_repeat_all")),
            _ => ("\U0001f501", "列表循环", ImageSourceHelper.FromNameVectorThemed("ic_repeat_all"))
        };
        PlayModeIconSourceWhite = _queue.PlayMode switch
        {
            PlayMode.ListRepeat => ImageSourceHelper.FromNameOriginal("ic_repeat_all"),
            PlayMode.SingleRepeat => ImageSourceHelper.FromNameOriginal("ic_repeat_one"),
            PlayMode.Shuffle => ImageSourceHelper.FromNameOriginal("ic_shuffle"),
            PlayMode.Sequential when fm => ImageSourceHelper.FromNameOriginal("ic_infinite"),
            PlayMode.Sequential => ImageSourceHelper.FromNameOriginal("ic_repeat_all"),
            _ => ImageSourceHelper.FromNameOriginal("ic_repeat_all")
        };
    }

    // === Like / Favorite ===

    private async Task ToggleLikeAsync()
    {
        var song = _queue.CurrentSong;
        if (song == null) return;

        var newFav = !IsLiked;
        await _db.SetFavoriteAsync(song.Id, newFav);
        IsLiked = newFav;
        LikeIcon = newFav ? "\u2665" : "\u2661"; // ♥ or ♡
        LikeIconSource = ImageSourceHelper.FromNamePlayerCtrl(newFav ? "ic_notif_favorite" : "ic_notif_favorite_border", newFav ? "ic_notif_favorite" : "ic_notif_favorite_border");
        LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(newFav ? "ic_notif_favorite" : "ic_notif_favorite_border");

        // 同步到在线音源插件（如网易云服务器红心）：RemoteId 形如 "netease:{onlineId}"。
        // 本地收藏是权威，插件同步失败（未登录/接口拒绝）静默，不影响本地红心状态。
        if (!string.IsNullOrEmpty(song.RemoteId) && song.RemoteId.Contains(':'))
        {
            try
            {
                var sep = song.RemoteId.IndexOf(':');
                var platform = song.RemoteId[..sep];
                var onlineId = song.RemoteId[(sep + 1)..];
                var plugin = _pluginManager?.GetEnabledPlugins<IOnlineMusicPlugin>()
                    .FirstOrDefault(p => string.Equals(p.PlatformName, platform, StringComparison.OrdinalIgnoreCase));
                if (plugin != null) await plugin.LikeSongAsync(onlineId, newFav);
            }
            catch { }
        }

#if ANDROID || WINDOWS
        try { (_audioService as Services.AudioPlayerService)?.UpdateFavoriteState(newFav); }
        catch { }
#endif
    }

    // === Seek ===

    private async void OnSeek(double positionSeconds)
    {
        _isSeeking = false;
        try { await _audioService.SeekAsync(TimeSpan.FromSeconds(positionSeconds)); }
        catch { }
    }

    /// <summary>Called from UI when user starts dragging the slider</summary>
    public void OnSeekStarted()
    {
        _isSeeking = true;
        _seekStartTime = DateTime.UtcNow;
    }

    /// <summary>Called from UI when user releases the slider</summary>
    public async Task OnSeekCompleted(double positionSeconds)
    {
        _isSeeking = false;
        await _audioService.SeekAsync(TimeSpan.FromSeconds(positionSeconds));
    }

    // === Load Song (called when page appears or song changes) ===

    /// <summary>
    /// 加载播放队列中的当前歌曲：刷新基础信息、封面、歌词、播放模式与即将播放列表，
    /// 并在 <paramref name="autoPlay"/> 为 true 时自动播放（启动恢复除外）。
    /// </summary>
    /// <param name="autoPlay">是否在切换歌曲后自动播放</param>
}

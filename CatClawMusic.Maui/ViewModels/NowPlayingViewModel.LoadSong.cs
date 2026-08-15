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
    public async Task LoadCurrentSongAsync(bool autoPlay = true)
    {
        var song = _queue.CurrentSong;
        var oldSongId = _loadedSongId;
        // 换歌前先 flush 上一首的聆听时长（后台执行，不阻塞切歌）
        if (oldSongId > 0 && song != null && song.Id != oldSongId)
        {
            _ = Task.Run(() => FlushListeningAsync(isFinalFlush: true));
        }

        // 启动恢复：如果队列为空，尝试从 Preferences 恢复上次的整个播放队列
        if (song == null)
        {
            try
            {
                await _db.EnsureInitializedAsync();
                var (restoredSongs, restoredIndex, restoredMode) = await RestoreQueueStateAsync();
                if (restoredSongs.Count > 0 && restoredIndex >= 0)
                {
                    // 直接恢复持久化的播放顺序（含洗牌顺序），不再重新洗牌
                    _queue.RestorePersisted(restoredSongs, restoredIndex, restoredMode);
                    song = _queue.CurrentSong;
                    // 标记启动恢复，避免自动播放
                    _isStartupRestore = true;
                    Log.Debug("AppViewModels", $"[NowPlaying] 恢复播放队列: {restoredSongs.Count} 首, 索引={restoredIndex}, 模式={restoredMode}");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("AppViewModels", $"[NowPlaying] 恢复播放队列失败: {ex.Message}");
            }
        }

        if (song == null)
        {
            Title = "";
            Artist = "";
            Album = "";
            HasAlbum = false;
            CoverImage = ImageSource.FromFile(DefaultCoverService.GetDefaultCoverPath());
            HasCover = false;
            HasLyrics = false;
            ClearLyrics();
            _lastRecordedSongId = -1;
            _loadedSongId = -1;
            Duration = 0;
            Progress = 0;
            IsPlaying = false;
#if ANDROID
            // 队列已无歌曲可播放：停止播放并移除前台通知（自然播放结束分支）。
            // 正常切歌不走此分支，前台服务/MediaSession 会被复用（见 AudioPlayerService 的 STATE_ENDED 处理）。
            try
            {
                if (_audioService is Services.AudioPlayerService androidAudio)
                    androidAudio.StopAndHideNotification();
                else
                    await _audioService.StopAsync();
            }
            catch { }
#else
            try { await _audioService.StopAsync(); } catch { }
#endif
            return;
        }

        // 同一首歌已经在播放，只需要同步显示信息，不重新播放
        var isSameSong = _loadedSongId == song.Id;
        _loadedSongId = song.Id;

        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();
        var ct = _loadCts.Token;

        // Basic info (always update — artist/title might change for WebDAV etc.)
        Title = song.Title ?? "未知歌曲";
        Artist = song.Artist ?? "未知艺术家";
        Album = song.Album ?? "未知专辑";
        HasAlbum = !string.IsNullOrEmpty(song.Album) && song.Album != "未知专辑";

        if (!isSameSong)
        {
            // 切歌：重置进度、封面和歌词
            // Song.Duration 单位是毫秒，Slider 需要秒
            // 新歌曲还没加载完成，_audioService.Duration 可能是旧值或 0，
            // 因此只使用数据库中的时长；若无效则等待 DurationChanged 事件修正。
            var songDurationSec = song.Duration > 1000 ? song.Duration / 1000.0 : 0;
            Duration = songDurationSec;
            TotalTimeDisplay = Duration > 0 ? FormatTime(Duration) : "--:--";
            Progress = 0;
            CurrentTimeDisplay = "0:00";
            _lastDisplayedSecond = 0;
            ClearLyrics();

            // 关键修复：切歌时先重置封面为默认图，防止异步加载失败时显示旧封面
            CoverImage = ImageSource.FromFile(DefaultCoverService.GetDefaultCoverPath());
            HasCover = false;
            CurrentCoverPath = null;

            // 持久化当前歌曲 ID，下次启动可恢复
            Preferences.Default.Set("last_playing_song_id", song.Id);
        }

        // Check favorite (与播放并行执行，不阻塞切歌)
        var favoriteTask = Task.Run(async () =>
        {
            try { return await _db.IsFavoriteAsync(song.Id); }
            catch { return false; }
        }, ct);

        // 预加载封面（与播放并行，提前显示）
        var coverTask = Task.Run(async () =>
        {
            try { await LoadCoverAsync(song, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Debug("AppViewModels", $"[CoverArt] 预加载封面失败: {ex.Message}"); }
        }, ct);

#if ANDROID || WINDOWS
        // 更新前台播放通知 / Windows SMTC 显示
#if ANDROID
        // 仅在实际切歌时重置进度缓存：确保通知栏/锁屏立即显示新歌的 0 进度而非上一首旧进度。
        // 同一首歌（如进入播放页/响应式重载）绝不能调用——它会把已 STATE_READY 的 _isPrepared
        // 清成 false 且无后续 READY 事件恢复，导致进度条永远卡在 0（歌单播放进度不走的元凶）。
        if (!isSameSong)
        {
            try { (_audioService as Services.AudioPlayerService)?.NotifySongSwitching(); }
            catch { }
            // 把数据库已知时长传给通知栏兜底：ExoPlayer Prepare 完成前 Duration=0，
            // 没有它通知栏进度条会在切歌间隙显示 00:00（Song.Duration 单位毫秒）
            try { (_audioService as Services.AudioPlayerService)?.UpdateKnownDuration(song.Duration > 1000 ? song.Duration / 1000.0 : 0); }
            catch { }
        }
#endif
        try { (_audioService as Services.AudioPlayerService)?.UpdateSongInfo(Title, Artist); }
        catch { }
        try { (_audioService as Services.AudioPlayerService)?.UpdateFavoriteState(IsLiked); }
        catch { }
#endif

        // Update play mode display (read current state, don't cycle)
        RefreshPlayModeDisplay();

        // Update upcoming songs
        RefreshUpcomingSongs();

        if (!isSameSong && autoPlay && !_isStartupRestore)
        {
            // 换歌时且允许自动播放才启动播放（启动恢复除外）
            if (!string.IsNullOrEmpty(song.FilePath))
            {
                // 播放与收藏查询并行执行
                var playTask = _audioService.PlayAsync(song.FilePath);
                await Task.WhenAll(playTask, favoriteTask);
                IsLiked = favoriteTask.Result;
                if (_lastRecordedSongId != song.Id)
                {
                    _lastRecordedSongId = song.Id;
                    _ = RecordPlayAsync(song.Id);
                }
            }
            else
            {
                IsLiked = await favoriteTask;
            }

            // 更新收藏图标
            LikeIcon = IsLiked ? "\u2665" : "\u2661";
            LikeIconSource = ImageSourceHelper.FromNamePlayerCtrl(IsLiked ? "ic_notif_favorite" : "ic_notif_favorite_border", IsLiked ? "ic_notif_favorite" : "ic_notif_favorite_border");
            LikeIconSourceWhite = ImageSourceHelper.FromNameOriginal(IsLiked ? "ic_notif_favorite" : "ic_notif_favorite_border");

            // 换歌时加载歌词（封面已在上方预加载），网络歌曲先缓存到本地再处理
            _ = Task.Run(async () =>
            {
                try
                {
                    // 网络歌曲：先缓存到本地，然后用本地文件方式处理
                    if (song.Source != SongSource.Local)
                    {
                        var localPath = await ResolveToLocalPathAsync(song, ct);
                        if (localPath != null)
                        {
                            await Task.WhenAll(
                                LoadMetadataFromLocalFileAsync(song, localPath),
                                LoadLyricsFromLocalFileAsync(song, localPath, ct)
                            );
                            // 封面已在预加载阶段处理，仅在网络缓存成功后补充加载
                            if (string.IsNullOrEmpty(song.CoverArtPath) || !File.Exists(song.CoverArtPath))
                                await LoadCoverFromLocalFileAsync(song, localPath, ct);
                            return;
                        }
                    }
                    // 本地歌曲：封面已预加载，仅加载歌词
                    await LoadLyricsAsync(song, ct);
                    // 如果封面预加载失败，补充加载
                    if (!HasCover)
                        await LoadCoverAsync(song, ct);
                    if (song.Source != SongSource.Local && _networkMusic != null)
                        await FetchAndUpdateSongMetadataAsync(song);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Debug("AppViewModels", $"Load cover/lyrics error: {ex.Message}");
                }
            }, ct);
        }
        else if (!isSameSong && (!autoPlay || _isStartupRestore))
        {
            // 首次加载或启动恢复：加载封面、歌词和元数据，但不播放
            // 外部页面（音乐库、发现页）触发播放时 autoPlay=false，走此分支；
            // 启动恢复时不记录（避免记录上次未完成的播放），其他情况记录播放会话
            if (!_isStartupRestore && _lastRecordedSongId != song.Id)
            {
                _lastRecordedSongId = song.Id;
                _ = RecordPlayAsync(song.Id);
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    if (song.Source != SongSource.Local)
                    {
                        var localPath = await ResolveToLocalPathAsync(song, ct);
                        if (localPath != null)
                        {
                            await Task.WhenAll(
                                LoadMetadataFromLocalFileAsync(song, localPath),
                                LoadCoverFromLocalFileAsync(song, localPath, ct),
                                LoadLyricsFromLocalFileAsync(song, localPath, ct)
                            );
                            return;
                        }
                    }
                    await Task.WhenAll(
                        LoadCoverAsync(song, ct),
                        LoadLyricsAsync(song, ct)
                    );
                    if (song.Source != SongSource.Local && _networkMusic != null)
                        await FetchAndUpdateSongMetadataAsync(song);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Log.Debug("AppViewModels", $"Load cover/lyrics error: {ex.Message}");
                }
            }, ct);
        }
        else
        {
            // 同一首歌回到播放页：恢复正确的播放/暂停状态图标
            PlayPauseIcon = _audioService.IsPlaying ? "\u23f8" : "\u25b6";
            PlayPauseIconSource = ImageSourceHelper.FromNamePlayerCtrl(_audioService.IsPlaying ? "ic_notif_pause" : "ic_notif_play", _audioService.IsPlaying ? "ic_notif_pause" : "ic_notif_play");
            PlayPauseIconSourceWhite = ImageSourceHelper.FromNameOriginal(_audioService.IsPlaying ? "ic_notif_pause" : "ic_notif_play");
        }

        // 重置启动恢复标志
        _isStartupRestore = false;

        // 保存队列状态（后台执行，不阻塞切歌）
        _ = Task.Run(SaveQueueState);
    }

    /// <summary>
    /// 启动页预加载当前歌曲的封面与歌词（不触碰播放器，不自动播放）。
    /// 主界面/歌词页进入时直接显示已就绪数据，消除「启动页结束但封面歌词
    /// 还在加载」的空白/占位闪烁。幂等：重复调用只重载一次结果。
    /// </summary>
    public async Task PreloadMediaAsync()
    {
        var song = _queue.CurrentSong;
        if (song == null) return;
        try
        {
            var ct = CancellationToken.None;
            await Task.WhenAll(
                LoadCoverAsync(song, ct),
                LoadLyricsAsync(song, ct));
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Preload] 封面/歌词预加载失败: {ex.Message}");
        }
    }

    // === 队列状态持久化 ===

    /// <summary>保存当前播放队列状态到 Preferences</summary>
}

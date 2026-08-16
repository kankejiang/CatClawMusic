using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.Maui.Services;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace CatClawMusic.Maui.ViewModels;

/// <summary>正在播放 ViewModel —— partial 分域文件。</summary>
public partial class NowPlayingViewModel
{
    private async Task LoadLyricsAsync(Song song, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        LrcLyrics? lyrics = null;
        try
        {
            Log.Debug("AppViewModels", $"[Lyrics] 开始加载歌词: {song.Title} (Id={song.Id}, Path={song.FilePath?.Substring(0, Math.Min(60, song.FilePath?.Length ?? 0))}...)");
            Log.Debug("AppViewModels", $"[Lyrics] LyricsPath={song.LyricsPath ?? "null"}");

            lyrics = await _lyrics.GetLyricsAsync(song);

            Log.Debug("AppViewModels", $"[Lyrics] 结果: {(lyrics != null ? $"{lyrics.Lines.Count} 行" : "null")}");
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[Lyrics] 加载异常: {ex.GetType().Name}: {ex.Message}");
        }

        ct.ThrowIfCancellationRequested();

        if (lyrics != null && lyrics.Lines.Count > 0)
        {
            _currentLyrics = lyrics;
            _currentLyricIndex = -1;
            BuildFilteredLines();

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLyrics = true;
                NoLyricsText = "";
                CurrentLyricIndexObservable = -1;
                OnPropertyChanged(nameof(AllLyricLines));
            });
            _desktopLyricManager?.SetLyrics(lyrics);
            Log.Debug("AppViewModels", $"[Lyrics] 歌词已加载，首行: {lyrics.Lines[0].Text}");
        }
        else
        {
            _currentLyrics = null;
            _currentLyricIndex = -1;
            _filteredLines = null;
            _originalToFilteredMap = null;
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                HasLyrics = false;
                NoLyricsText = "暂无歌词";
                ClearLyrics();
                OnPropertyChanged(nameof(AllLyricLines));
            });
            _desktopLyricManager?.SetLyrics(null);
            Log.Debug("AppViewModels", "[Lyrics] 未找到歌词");
        }
    }

    private void UpdateLyricPosition(TimeSpan position)
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0)
            return;

        var newIndex = _lyrics.GetCurrentLyricIndex(_currentLyrics, position);

        // 即使行索引不变，也要更新逐字填充进度（Apple Music 风格逐字渐进填充）
        UpdateFillProgress(newIndex, position);

        if (newIndex == _currentLyricIndex)
            return;
        _currentLyricIndex = newIndex;

        // 智能删除空行开启时，若当前行为空行（被过滤），不更新 UI 显示索引，
        // 保持上一句歌词的高亮位置不变，避免歌词向下回滚。
        // 空行代表间奏/停顿，UI 应停留在上一句歌词等待下一句出现。
        if (_originalToFilteredMap != null
            && newIndex >= 0
            && newIndex < _originalToFilteredMap.Length
            && _originalToFilteredMap[newIndex] == -1)
        {
            return;
        }

        // UI 显示使用过滤后列表和索引；预览行也基于过滤后列表，跳过空行
        var displayLines = _filteredLines ?? _currentLyrics.Lines;
        var displayIndex = MapOriginalToFiltered(newIndex);

        CurrentLyricIndexObservable = displayIndex;

        LyricCurrent = GetLineText(displayLines, displayIndex);
        LyricLine0 = GetLineText(displayLines, displayIndex - 4);
        LyricLine1 = GetLineText(displayLines, displayIndex - 3);
        LyricLine2 = GetLineText(displayLines, displayIndex - 2);
        LyricLine3 = GetLineText(displayLines, displayIndex - 1);
        LyricLine4 = GetLineText(displayLines, displayIndex + 1);
        LyricLine5 = GetLineText(displayLines, displayIndex + 2);
        LyricLine6 = GetLineText(displayLines, displayIndex + 3);
        LyricLine7 = GetLineText(displayLines, displayIndex + 4);
    }

    /// <summary>
    /// 横竖屏根切换（shell.Items.Clear + 重建 MainPage）后强制同步歌词显示：
    /// 新 NowPlayingPage 的 _lastHighlightIndex 初始为 -1，而 Singleton VM 的
    /// _currentLyricIndex 保持旧值——UpdateLyricPosition 在 newIndex==_currentLyricIndex
    /// 时直接 return，新页面收不到 CurrentLyricIndexObservable 的 PropertyChanged，
    /// 导致歌词高亮/滚动永久冻结（直到下一次行索引变化才偶然恢复）。
    /// 这里用播放器实时位置重跑歌词定位，并强制广播当前索引与填充进度，
    /// 让新页面立即完成高亮初始化。
    /// </summary>
    public void RefreshLyricDisplayAfterLayout()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                // 强制广播歌词行集合：新页面在订阅 PropertyChanged 之前 AllLyricLines
                // 可能已被设置，错过事件就永远不会 BuildLyricLabels → 歌词区域空白。
                OnPropertyChanged(nameof(AllLyricLines));
                OnPropertyChanged(nameof(HasLyrics));

                // 强制广播进度/时长：新页面的 Slider 在 XAML 初始值为 0，
                // 若 Progress 属性恰未发生数值变化则不会触发 PropertyChanged，
                // 滑块就停在 0（"进度条没有重头开始走"）。
                OnPropertyChanged(nameof(Duration));
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(TotalTimeDisplay));
                OnPropertyChanged(nameof(CurrentTimeDisplay));

                // 用播放器实时位置重跑歌词定位
                var pos = TimeSpan.FromSeconds(_audioService.CurrentPosition);
                UpdateLyricPosition(pos);

                // 强制广播：即使行索引未变化，也触发新页面的 HighlightLine 完成初始化
                OnPropertyChanged(nameof(CurrentLyricIndexObservable));
                OnPropertyChanged(nameof(CurrentLineFillProgress));

                // 同步播放/暂停图标状态
                OnPropertyChanged(nameof(IsPlaying));
                OnPropertyChanged(nameof(PlayPauseIcon));
                OnPropertyChanged(nameof(PlayPauseIconSource));
                OnPropertyChanged(nameof(PlayPauseIconSourceWhite));
            }
            catch { }
        });
    }

    /// <summary>
    /// 计算并更新当前行的逐字填充进度（Apple Music 风格）。
    /// 逐行模式：整行实心（1.0）；逐字模式：按音节时间精确映射或线性填充。
    /// </summary>
    private void UpdateFillProgress(int lineIndex, TimeSpan position)
    {
        _lastPosition = position;

        if (lineIndex < 0 || lineIndex >= _currentLyrics!.Lines.Count)
        {
            CurrentLineFillProgress = 0.0;
            return;
        }

        // 智能删除空行开启时，若当前行为空行（被过滤），跳过填充进度更新，
        // 保持上一句歌词的着色状态（1.0=完全填充），等待下一句非空行再继续着色。
        // 这样可避免空行期间把上一句的 FillProgress 重置为 0 导致重复着色。
        if (_originalToFilteredMap != null
            && lineIndex < _originalToFilteredMap.Length
            && _originalToFilteredMap[lineIndex] == -1)
        {
            CurrentLineFillProgress = 1.0;
            return;
        }

        var lineMode = Services.LyricsSettingsService.Instance.LyricsMode == Services.LyricsSettingsService.Mode.Line;
        CurrentLineFillProgress = LyricFillCalculator.ComputeFillProgress(
            _currentLyrics.Lines[lineIndex], lineIndex, _currentLyrics.Lines, position, lineMode);
    }

    /// <summary>
    /// 交互期间仅更新当前行的逐字着色进度，不切换行索引。
    /// 用于自动滚动/用户滚动期间保持着色持续推进，避免滚动结束后跳字。
    /// </summary>
    private void UpdateFillProgressOnly(TimeSpan position)
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0) return;
        _lastPosition = position;
        UpdateFillProgress(_currentLyricIndex, position);
    }

    private static string GetLineText(List<LrcLyricLine> lines, int index)
    {
        if (index < 0 || index >= lines.Count) return "";
        return lines[index].Text;
    }

    /// <summary>
    /// 根据设置构建过滤后的歌词列表（移除空行）。
    /// 同时建立原始索引→过滤后索引的映射，供 UI 显示使用。
    /// </summary>
    private void BuildFilteredLines()
    {
        if (_currentLyrics == null || _currentLyrics.Lines.Count == 0)
        {
            _filteredLines = null;
            _originalToFilteredMap = null;
            return;
        }

        var removeEmpty = Services.LyricsSettingsService.Instance.RemoveEmptyLines;
        if (!removeEmpty)
        {
            _filteredLines = null;
            _originalToFilteredMap = null;
            return;
        }

        var original = _currentLyrics.Lines;
        _filteredLines = new List<LrcLyricLine>(original.Count);
        _originalToFilteredMap = new int[original.Count];
        for (int i = 0; i < original.Count; i++)
        {
            var line = original[i];
            // 空行：文本为空或仅空白，且无翻译内容
            var isEmpty = string.IsNullOrWhiteSpace(line.Text)
                          && string.IsNullOrWhiteSpace(line.Translation);
            if (isEmpty)
            {
                _originalToFilteredMap[i] = -1;
            }
            else
            {
                _originalToFilteredMap[i] = _filteredLines.Count;
                _filteredLines.Add(line);
            }
        }
    }

    /// <summary>将原始歌词行索引映射为过滤后列表中的索引。
    /// 若该行是空行被过滤，回退到最近的前一个非空行（空行代表停顿，前一行仍为当前歌词）。</summary>
    private int MapOriginalToFiltered(int originalIndex)
    {
        if (_originalToFilteredMap == null || originalIndex < 0 || originalIndex >= _originalToFilteredMap.Length)
            return originalIndex;
        var mapped = _originalToFilteredMap[originalIndex];
        if (mapped >= 0)
            return mapped;
        // 当前行是空行（被过滤），向前查找最近的可显示行
        for (int i = originalIndex - 1; i >= 0; i--)
        {
            if (_originalToFilteredMap[i] >= 0)
                return _originalToFilteredMap[i];
        }
        // 前面没有可显示行，向后查找
        for (int i = originalIndex + 1; i < _originalToFilteredMap.Length; i++)
        {
            if (_originalToFilteredMap[i] >= 0)
                return _originalToFilteredMap[i];
        }
        return -1;
    }

    /// <summary>
    /// 重新构建过滤列表并刷新 UI（设置变更后调用）。
    /// 重新映射当前行索引并触发 AllLyricLines 属性变更通知。
    /// </summary>
    public void RefreshFilteredLines()
    {
        BuildFilteredLines();
        // 重新映射当前行索引
        if (_currentLyricIndex >= 0)
        {
            CurrentLyricIndexObservable = MapOriginalToFiltered(_currentLyricIndex);
        }
        OnPropertyChanged(nameof(AllLyricLines));
    }

    private void ClearLyrics()
    {
        // 关键修复：必须清空 _filteredLines 和 _currentLyrics，
        // 否则 AllLyricLines getter 会返回旧歌的歌词（导致切歌后歌词显示不更新）
        _currentLyrics = null;
        _currentLyricIndex = -1;
        _filteredLines = null;
        _originalToFilteredMap = null;

        LyricCurrent = "";
        LyricLine0 = "";
        LyricLine1 = "";
        LyricLine2 = "";
        LyricLine3 = "";
        LyricLine4 = "";
        LyricLine5 = "";
        LyricLine6 = "";
        LyricLine7 = "";
        OnPropertyChanged(nameof(AllLyricLines));
    }

    private async Task RecordPlayAsync(int songId, long durationMs = 0)
    {
        try
        {
            await _db.EnsureInitializedAsync();
            await _db.RecordPlayAsync(songId, durationMs);
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[NowPlayingViewModel] 记录播放失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 将当前累积的聆听时长写入数据库。
    /// 调用时机：切歌前、播放完成时、应用挂起时、每 30 秒定时。
    /// </summary>
    /// <param name="isFinalFlush">是否最终 flush（切歌/完成时），为 true 时重置追踪状态</param>
    public async Task FlushListeningAsync(bool isFinalFlush = false)
    {
        int songId;
        long elapsedMs;

        lock (_listenRecordLock)
        {
            songId = _trackedSongId;
            if (songId <= 0) return;

            // 计算从本次播放开始到现在的时长，累加到 pending
            if (_listeningStartUtc != DateTime.MinValue)
            {
                var elapsed = (long)(DateTime.UtcNow - _listeningStartUtc).TotalMilliseconds;
                if (elapsed > 0)
                {
                    _pendingListenMs += elapsed;
                    _listeningStartUtc = DateTime.UtcNow; // 重置起点，避免重复累加
                }
            }

            elapsedMs = _pendingListenMs;
            if (elapsedMs <= 0) return;

            if (isFinalFlush)
            {
                _trackedSongId = -1;
                _listeningStartUtc = DateTime.MinValue;
                _pendingListenMs = 0;
                _currentSessionId = -1;
            }
            else
            {
                // 定时 flush：保留 pending 累计时长（不清零），后续把累计总时长写回同一行会话；
                // 若清零，播放会话时长只会记录最后一次 30 秒区间，丢失前面已听时长。
            }
        }

        if (elapsedMs > 0 && songId > 0)
        {
            // 仅把本次聆听的累计时长写回同一行播放会话；绝不调用 RecordPlayAsync，
            // 否则每 30 秒 flush 都会给 PlayHistory.PlayCount +1，导致发现页「最多播放」被放大。
            if (_currentSessionId > 0)
                await _db.UpdateListenSessionAsync(_currentSessionId, elapsedMs);
            else
                _currentSessionId = await _db.LogListenSessionAsync(songId, elapsedMs);
        }
    }

    /// <summary>
    /// 外部调用入口：应用挂起时 flush 聆听时长。
    /// 供 App.xaml.cs 的 OnSleep 调用。
    /// </summary>
    public void OnAppSleep()
    {
        _ = FlushListeningAsync(isFinalFlush: false);
    }

    /// <summary>
    /// 外部调用入口：应用恢复时重启计时。
    /// 供 App.xaml.cs 的 OnResume 调用。
    /// </summary>
    public void OnAppResume()
    {
        lock (_listenRecordLock)
        {
            if (_trackedSongId > 0 && _audioService.IsPlaying)
            {
                _listeningStartUtc = DateTime.UtcNow;
            }
        }
    }

    // === Utilities ===

}

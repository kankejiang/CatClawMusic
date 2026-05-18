using Android.Content;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Services;

/// <summary>播放状态持久化管理器，保存/恢复播放进度和播放模式</summary>
public static class PlaybackStateManager
{
    private const string PrefKey = "playback_state";
    private const string PrefSongPath = "song_path";
    private const string PrefPosition = "position_seconds";
    private const string PrefPlayMode = "play_mode";
    /// <summary>歌曲来源类型（SongSource 枚举值），用于恢复时区分本地/网络匹配策略</summary>
    private const string PrefSongSource = "song_source";
    /// <summary>网络歌曲远程唯一标识（RemoteId），避免带动态 token 的 URL 无法匹配</summary>
    private const string PrefSongRemoteId = "song_remote_id";

    /// <summary>获取 SharedPreferences 实例</summary>
    private static ISharedPreferences? GetPrefs()
    {
        var ctx = global::Android.App.Application.Context;
        return ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
    }

    /// <summary>保存当前播放歌曲路径、来源和播放位置</summary>
    /// <param name="player">播放器实例</param>
    /// <param name="currentSong">当前歌曲对象，用于保存 Source 和 RemoteId 以支持网络歌曲恢复</param>
    public static void Save(IAudioPlayerService player, Song? currentSong = null)
    {
        var prefs = GetPrefs();
        if (prefs == null) return;
        var song = player.CurrentSongFilePath;
        if (string.IsNullOrEmpty(song)) return;
        var editor = prefs.Edit();
        if (editor != null)
        {
            editor.PutString(PrefSongPath, song);
            editor.PutFloat(PrefPosition, (float)player.CurrentPosition.TotalSeconds);
            // 同时保存歌曲来源和远程ID，避免网络歌曲的带 token URL 下次无法匹配
            editor.PutInt(PrefSongSource, currentSong != null ? (int)currentSong.Source : (int)SongSource.Local);
            editor.PutString(PrefSongRemoteId, currentSong?.RemoteId ?? "");
            editor.Apply();
        }
    }

    /// <summary>保存播放模式</summary>
    public static void SavePlayMode(PlayMode mode)
    {
        var prefs = GetPrefs();
        if (prefs == null) return;
        var editor = prefs.Edit();
        if (editor != null)
        {
            editor.PutInt(PrefPlayMode, (int)mode);
            editor.Apply();
        }
    }

    /// <summary>清除所有保存的播放状态</summary>
    public static void Clear()
    {
        var prefs = GetPrefs();
        if (prefs == null) return;
        var editor = prefs.Edit();
        if (editor == null) return;
        editor!.Clear().Apply();
    }

    /// <summary>从 URL 查询参数中提取指定键的值（不区分大小写）</summary>
    /// <remarks>用于从 Navidrome stream URL 中提取 songId，绕过动态 salt/token 匹配问题</remarks>
    private static string? ExtractQueryParam(string url, string param)
    {
        try
        {
            var uri = new Uri(url);
            var query = uri.Query.TrimStart('?');
            foreach (var pair in query.Split('&'))
            {
                var parts = pair.Split('=', 2);
                if (parts.Length == 2 && string.Equals(Uri.UnescapeDataString(parts[0]), param, StringComparison.OrdinalIgnoreCase))
                    return Uri.UnescapeDataString(parts[1]);
            }
        }
        catch { }
        return null;
    }

    /// <summary>从 SharedPreferences 中同步恢复播放模式和位置到 ViewModel（不播放）</summary>
    public static void RestorePrefsToViewModel(PlayQueue queue, NowPlayingViewModel vm)
    {
        var prefs = GetPrefs();
        if (prefs == null) return;
        var path = prefs.GetString(PrefSongPath, null);
        vm.CurrentPosition = TimeSpan.FromSeconds(prefs.GetFloat(PrefPosition, 0));
        var savedMode = prefs.GetInt(PrefPlayMode, (int)PlayMode.ListRepeat);
        queue.PlayMode = (PlayMode)savedMode;
        vm.SyncPlayMode();
        System.Diagnostics.Debug.WriteLine($"[CatClaw] RestorePrefs: path={(path?.Substring(0, Math.Min(50, path?.Length ?? 0)) ?? "null")}, mode={savedMode}");
    }

    /// <summary>异步恢复上次播放状态：加载歌曲、恢复队列和播放模式、Seek 到上次位置</summary>
    /// <remarks>
    /// 歌曲匹配策略：
    ///   1. 有 Source+RemoteId → 按 RemoteId 精确匹配（网络歌曲，绕过动态 token）
    ///   2. song_path 是 HTTP URL → 兼容旧数据，尝试从 URL 提取 id 参数匹配 RemoteId
    ///   3. 其他 → 按 FilePath 精确匹配（本地文件）
    /// </remarks>
    public static async Task RestoreAsync(IAudioPlayerService player, MusicDatabase db, PlayQueue queue, NowPlayingViewModel vm)
    {
        var prefs = GetPrefs();
        if (prefs == null) return;
        var path = prefs.GetString(PrefSongPath, null);
        if (string.IsNullOrEmpty(path)) return;

        var position = TimeSpan.FromSeconds(prefs.GetFloat(PrefPosition, 0));
        // 读取上次退出时保存的歌曲来源和远程唯一标识
        var savedSource = (SongSource)prefs.GetInt(PrefSongSource, (int)SongSource.Local);
        var savedRemoteId = prefs.GetString(PrefSongRemoteId, null);

        try
        {
            var songs = await db.GetSongsAsync();
            Song? song;

            // 策略1：网络歌曲 + 有 RemoteId → 精确匹配，绕过 URL 动态 token 问题
            if (savedSource == SongSource.WebDAV && !string.IsNullOrEmpty(savedRemoteId))
            {
                song = songs.FirstOrDefault(s => s.Source == SongSource.WebDAV && s.RemoteId == savedRemoteId);
            }
            // 策略2：旧数据兼容 —— song_path 是 HTTP URL 但没有 Source/RemoteId 字段
            // 从 URL 中提取 id 参数作为 RemoteId 进行匹配
            else if (path.StartsWith("http://") || path.StartsWith("https://"))
            {
                var idParam = ExtractQueryParam(path, "id");
                var remoteId = savedRemoteId ?? idParam;
                if (!string.IsNullOrEmpty(remoteId))
                    song = songs.FirstOrDefault(s => s.Source == SongSource.WebDAV && s.RemoteId == remoteId);
                else
                    song = songs.FirstOrDefault(s => s.FilePath == path);
            }
            // 策略3：本地文件 → 直接按 FilePath 匹配
            else
            {
                song = songs.FirstOrDefault(s => s.FilePath == path);
            }

            if (song == null) { Clear(); return; }

            queue.SetSongs(songs);
            queue.SelectSong(song.Id);

            var savedMode = prefs.GetInt(PrefPlayMode, (int)PlayMode.ListRepeat);
            queue.PlayMode = (PlayMode)savedMode;
            vm.SetCurrentSong(song);
            vm.SyncPlayMode();
            vm.CurrentPosition = position;

            await player.PlayAsync(song.FilePath);
            await Task.Delay(300);
            await player.PauseAsync();
            if (position.TotalSeconds > 0)
                await player.SeekAsync(position);
        }
        catch
        {
            Clear();
        }
    }
}

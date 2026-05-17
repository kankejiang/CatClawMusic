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
    private const string PrefSongSource = "song_source";
    private const string PrefSongRemoteId = "song_remote_id";

    /// <summary>获取 SharedPreferences 实例</summary>
    private static ISharedPreferences? GetPrefs()
    {
        var ctx = global::Android.App.Application.Context;
        return ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
    }

    /// <summary>保存当前播放歌曲路径和播放位置</summary>
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
    public static async Task RestoreAsync(IAudioPlayerService player, MusicDatabase db, PlayQueue queue, NowPlayingViewModel vm)
    {
        var prefs = GetPrefs();
        if (prefs == null) return;
        var path = prefs.GetString(PrefSongPath, null);
        if (string.IsNullOrEmpty(path)) return;

        var position = TimeSpan.FromSeconds(prefs.GetFloat(PrefPosition, 0));
        var savedSource = (SongSource)prefs.GetInt(PrefSongSource, (int)SongSource.Local);
        var savedRemoteId = prefs.GetString(PrefSongRemoteId, null);

        try
        {
            var songs = await db.GetSongsAsync();
            Song? song;

            if (savedSource == SongSource.WebDAV && !string.IsNullOrEmpty(savedRemoteId))
            {
                song = songs.FirstOrDefault(s => s.Source == SongSource.WebDAV && s.RemoteId == savedRemoteId);
            }
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

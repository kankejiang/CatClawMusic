using Android.Content;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using CatClawMusic.Data;
using CatClawMusic.UI.ViewModels;

namespace CatClawMusic.UI.Services;

public static class PlaybackStateManager
{
    private const string PrefKey = "playback_state";
    private const string PrefSongPath = "song_path";
    private const string PrefPosition = "position_seconds";
    private const string PrefPlayMode = "play_mode";

    private static ISharedPreferences? GetPrefs()
    {
        var ctx = global::Android.App.Application.Context;
        return ctx.GetSharedPreferences(PrefKey, FileCreationMode.Private);
    }

    public static void Save(IAudioPlayerService player)
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

    public static async Task RestoreAsync(IAudioPlayerService player, MusicDatabase db, PlayQueue queue, NowPlayingViewModel vm)
    {
        var prefs = GetPrefs();
        if (prefs == null) return;
        var path = prefs.GetString(PrefSongPath, null);
        if (string.IsNullOrEmpty(path)) return;

        var position = TimeSpan.FromSeconds(prefs.GetFloat(PrefPosition, 0));

        try
        {
            var songs = await db.GetSongsAsync();
            var song = songs.FirstOrDefault(s => s.FilePath == path);
            if (song == null) { Clear(); return; }

            // 恢复播放队列（所有歌曲）
            queue.SetSongs(songs);
            queue.SelectSong(song.Id);

            // 恢复播放模式
            var savedMode = prefs.GetInt(PrefPlayMode, (int)PlayMode.ListRepeat);
            queue.PlayMode = (PlayMode)savedMode;
            vm.SetCurrentSong(song);
            // 同步播放模式图标和进度条位置到 UI
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

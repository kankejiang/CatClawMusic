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
    private void SaveQueueState()
    {
        try
        {
            var songs = _queue.GetSongs();
            var songIds = songs.Select(s => s.Id).ToArray();
            var currentSong = _queue.CurrentSong;

            Preferences.Default.Set("queue_song_ids", string.Join(",", songIds));
            Preferences.Default.Set("queue_current_song_id", currentSong?.Id ?? -1);
            Preferences.Default.Set("queue_play_mode", (int)_queue.PlayMode);
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[NowPlaying] 保存队列状态失败: {ex.Message}");
        }
    }

    /// <summary>从 Preferences 恢复播放队列状态（在线程池线程执行以避免阻塞主线程）</summary>
    private async Task<(List<Song> songs, int currentIndex, PlayMode playMode)> RestoreQueueStateAsync()
    {
        try
        {
            // 将所有 I/O 与 SQLite 查询放到线程池线程，避免 sync-over-async 阻塞主线程
            return await Task.Run(async () =>
            {
                var songIdsStr = Preferences.Default.Get("queue_song_ids", "");
                var currentSongId = Preferences.Default.Get("queue_current_song_id", -1);
                var playMode = (PlayMode)Preferences.Default.Get("queue_play_mode", (int)PlayMode.ListRepeat);

                if (string.IsNullOrEmpty(songIdsStr) || currentSongId <= 0)
                    return (new List<Song>(), -1, playMode);

                var songIds = songIdsStr.Split(',')
                    .Where(s => int.TryParse(s, out _))
                    .Select(int.Parse)
                    .ToList();

                if (songIds.Count == 0)
                    return (new List<Song>(), -1, playMode);

                // 并行查询所有歌曲（避免串行 await）
                var songTasks = songIds.Select(async id =>
                {
                    var song = await _db.GetSongByIdAsync(id);
                    if (song == null) return null;
                    // 并行查询 artist 和 album
                    var artistTask = _db.FindArtistByIdAsync(song.ArtistId);
                    var albumTask = _db.FindAlbumByIdAsync(song.AlbumId);
                    await Task.WhenAll(artistTask, albumTask);
                    song.Artist = artistTask.Result?.Name ?? "未知艺术家";
                    song.Album = albumTask.Result?.Title ?? "未知专辑";
                    song.AllArtists = song.Artist;
                    return song;
                }).ToList();
                var results = await Task.WhenAll(songTasks);
                var songs = results.Where(s => s != null).Cast<Song>().ToList();

                // 保存顺序即展示顺序（随机模式下即洗牌顺序），计算当前歌曲在其中的索引
                var currentIndex = songs.FindIndex(s => s.Id == currentSongId);
                return (songs, currentIndex, playMode);
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Log.Debug("AppViewModels", $"[NowPlaying] 恢复队列状态失败: {ex.Message}");
            return (new List<Song>(), -1, PlayMode.ListRepeat);
        }
    }

    private void RefreshUpcomingSongs()
    {
        UpcomingSongs.Clear();
        foreach (var s in _queue.GetUpcomingSongs(10))
            UpcomingSongs.Add(s);
    }

    // === Cover Art Loading ===

}

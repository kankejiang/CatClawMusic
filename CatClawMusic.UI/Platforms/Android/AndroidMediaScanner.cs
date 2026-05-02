using Android.Content;
using Android.Database;
using Android.Provider;
using Android.Util;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android MediaStore 音频扫描器——无需权限即可读取系统媒体库
/// </summary>
public static class AndroidMediaScanner
{
    private const string TAG = "CatClaw";

    /// <summary>通过 MediaStore 查询所有音频文件（Android 10+ 无需权限）</summary>
    public static List<Song> ScanFromMediaStore()
    {
        var songs = new List<Song>();
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return songs;

            var uri = MediaStore.Audio.Media.ExternalContentUri;
            if (uri == null) return songs;
            var projection = new[]
            {
                MediaStore.Audio.Media.InterfaceConsts.Id,
                MediaStore.Audio.Media.InterfaceConsts.Title,
                MediaStore.Audio.Media.InterfaceConsts.Artist,
                MediaStore.Audio.Media.InterfaceConsts.Album,
                MediaStore.Audio.Media.InterfaceConsts.Duration,
                MediaStore.Audio.Media.InterfaceConsts.Data,
                MediaStore.Audio.Media.InterfaceConsts.Size,
                MediaStore.Audio.Media.InterfaceConsts.AlbumId,
            };

            // 不使用 ContentResolver?.Query —— ctx 已判空，ContentResolver 不会为 null
            var cursor = ctx.ContentResolver!.Query(uri, projection, null, null, null);
            if (cursor == null) return songs;

            // 预先获取列索引，不存在的列返回 -1（某些设备/ROM 不提供 year/track 列）
            var colYear = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Year);
            var colTrack = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Track);

            var count = 0;
            while (cursor.MoveToNext())
            {
                count++;
                var dataPath = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Data));
                // 放宽检查：Android 10+ scoped storage 下 File.Exists 可能误判，只过滤空路径
                if (string.IsNullOrEmpty(dataPath)) continue;

                songs.Add(new Song
                {
                    Title = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Title)) ?? "未知标题",
                    Artist = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Artist)) ?? "未知艺术家",
                    Album = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Album)) ?? "未知专辑",
                    AlbumId = 0,
                    Duration = (int)(cursor.GetLong(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Duration)) / 1000),
                    FileSize = cursor.GetLong(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Size)),
                    FilePath = dataPath,
                    Year = colYear >= 0 ? cursor.GetInt(colYear) : 0,
                    TrackNumber = colTrack >= 0 ? cursor.GetInt(colTrack) : 0,
                    LyricsPath = MusicUtility.FindLyricsFile(dataPath),
                    Source = SongSource.Local
                });
            }
            cursor.Close();
            Log.Debug(TAG, $"MediaStore 扫描完成，找到 {count} 条记录，最终 {songs.Count} 首歌曲");
        }
        catch (Exception ex)
        {
            Log.Debug(TAG, $"MediaStore 扫描异常: {ex.Message}");
        }

        return songs;
    }
}

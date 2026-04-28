using Android.Content;
using Android.Database;
using Android.Provider;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// Android MediaStore 音频扫描器——无需权限即可读取系统媒体库
/// </summary>
public static class AndroidMediaScanner
{
    /// <summary>通过 MediaStore 查询所有音频文件（Android 10+ 无需权限）</summary>
    public static List<Song> ScanFromMediaStore()
    {
        var songs = new List<Song>();
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return songs;

            var uri = MediaStore.Audio.Media.ExternalContentUri;
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

            var cursor = ctx.ContentResolver?.Query(uri, projection, null, null, null);
            if (cursor == null) return songs;

            while (cursor.MoveToNext())
            {
                var dataPath = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Data));
                if (string.IsNullOrEmpty(dataPath) || !File.Exists(dataPath)) continue;

                songs.Add(new Song
                {
                    Title = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Title)) ?? "未知标题",
                    Artist = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Artist)) ?? "未知艺术家",
                    Album = cursor.GetString(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Album)) ?? "未知专辑",
                    Duration = (int)(cursor.GetLong(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Duration)) / 1000),
                    FileSize = cursor.GetLong(cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Size)),
                    FilePath = dataPath,
                    Source = SongSource.Local
                });
            }
            cursor.Close();
        }
        catch (Exception)
        {
            // MediaStore 不可用时降级到文件扫描
        }

        return songs;
    }
}

using Android.Content;
using Android.Database;
using Android.Provider;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>Android MediaStore 音频扫描器 — 无需权限即可读取系统媒体库</summary>
public static class AndroidMediaScanner
{
    /// <summary>通过 MediaStore 查询所有音频文件</summary>
    public static List<Song> ScanFromMediaStore()
    {
        var songs = new List<Song>();
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx?.ContentResolver == null) return songs;

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

            var cursor = ctx.ContentResolver.Query(uri, projection, null, null, null);
            if (cursor == null) return songs;

            var colYear = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Year);
            var colTrack = cursor.GetColumnIndex(MediaStore.Audio.Media.InterfaceConsts.Track);

            while (cursor.MoveToNext())
            {
                var dataPath = cursor.GetString(
                    cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Data));
                if (string.IsNullOrEmpty(dataPath)) continue;

                var rawArtist = cursor.GetString(
                    cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Artist)) ?? "未知艺术家";
                var artistNames = MusicUtility.SplitArtistNames(rawArtist);
                var normalizedArtist = artistNames.Count > 0 ? artistNames[0] : "未知艺术家";

                songs.Add(new Song
                {
                    Title = cursor.GetString(
                        cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Title)) ?? "未知标题",
                    Artist = normalizedArtist,
                    Album = cursor.GetString(
                        cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Album)) ?? "未知专辑",
                    Duration = (int)(cursor.GetLong(
                        cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Duration)) / 1000),
                    FileSize = cursor.GetLong(
                        cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Size)),
                    FilePath = dataPath,
                    Year = colYear >= 0 ? cursor.GetInt(colYear) : 0,
                    TrackNumber = colTrack >= 0 ? cursor.GetInt(colTrack) : 0,
                    Source = SongSource.Local,
                    MediaStoreId = cursor.GetLong(
                        cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Id))
                });
            }
            cursor.Close();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MediaStore] Scan error: {ex.Message}");
        }
        return songs;
    }
}

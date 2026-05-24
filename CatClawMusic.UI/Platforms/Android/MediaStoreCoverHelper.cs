using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Platforms.Android;

public static class MediaStoreCoverHelper
{
    private static readonly Handler _mainHandler = new(Looper.MainLooper!);

    public static Bitmap? LoadCoverFromMediaStore(long mediaStoreId, int size = 120)
    {
        if (mediaStoreId <= 0) return null;
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return null;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                var contentUri = ContentUris.WithAppendedId(
                    MediaStore.Audio.Media.ExternalContentUri, mediaStoreId);
                if (contentUri == null) return null;
                var thumbnail = ctx.ContentResolver!.LoadThumbnail(
                    contentUri, new global::Android.Util.Size(size, size), null);
                return thumbnail;
            }
            else
            {
                return LoadAlbumArtLegacy(mediaStoreId, size);
            }
        }
        catch { return null; }
    }

    public static async Task<Bitmap?> LoadCoverFromMediaStoreAsync(long mediaStoreId, int size = 120)
    {
        if (mediaStoreId <= 0) return null;
        return await Task.Run(() => LoadCoverFromMediaStore(mediaStoreId, size));
    }

    public static (Bitmap? bitmap, long mediaStoreId) LoadCoverByFilePath(string filePath, int size = 120)
    {
        if (string.IsNullOrEmpty(filePath)) return (null, 0);
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return (null, 0);

            var cursor = ctx.ContentResolver!.Query(
                MediaStore.Audio.Media.ExternalContentUri,
                new[] { MediaStore.Audio.Media.InterfaceConsts.Id },
                $"{MediaStore.Audio.Media.InterfaceConsts.Data} = ?",
                new[] { filePath },
                null);

            if (cursor != null && cursor.MoveToFirst())
            {
                var id = cursor.GetLong(0);
                cursor.Close();
                return (LoadCoverFromMediaStore(id, size), id);
            }
            cursor?.Close();
        }
        catch { }
        return (null, 0);
    }

    private static Bitmap? LoadAlbumArtLegacy(long mediaStoreId, int size)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return null;

            var albumCursor = ctx.ContentResolver!.Query(
                MediaStore.Audio.Albums.ExternalContentUri,
                new[] { MediaStore.Audio.Albums.InterfaceConsts.AlbumArt },
                $"{MediaStore.Audio.Albums.InterfaceConsts.Id} = ?",
                new[] { mediaStoreId.ToString() },
                null);

            if (albumCursor != null && albumCursor.MoveToFirst())
            {
                var albumArtPath = albumCursor.GetString(0);
                albumCursor.Close();

                if (!string.IsNullOrEmpty(albumArtPath) && System.IO.File.Exists(albumArtPath))
                {
                    var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeFile(albumArtPath, options);

                    int inSampleSize = 1;
                    if (options.OutHeight > size || options.OutWidth > size)
                    {
                        var halfH = options.OutHeight / 2;
                        var halfW = options.OutWidth / 2;
                        while ((halfH / inSampleSize) >= size && (halfW / inSampleSize) >= size)
                            inSampleSize *= 2;
                    }

                    options.InJustDecodeBounds = false;
                    options.InSampleSize = inSampleSize;
                    options.InPreferredConfig = Bitmap.Config.Rgb565;
                    return BitmapFactory.DecodeFile(albumArtPath, options);
                }
            }
            albumCursor?.Close();
        }
        catch { }
        return null;
    }

    public static bool BatchFillMediaStoreIds(List<Song> songs)
    {
        if (songs == null || songs.Count == 0) return false;
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return false;

            var pathToSong = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
            var needQuery = new List<string>();
            foreach (var s in songs)
            {
                if (s.MediaStoreId > 0) continue;
                if (s.Source != SongSource.Local) continue;
                if (string.IsNullOrEmpty(s.FilePath)) continue;
                if (!pathToSong.ContainsKey(s.FilePath))
                {
                    pathToSong[s.FilePath] = s;
                    needQuery.Add(s.FilePath);
                }
            }

            if (needQuery.Count == 0) return true;

            var cursor = ctx.ContentResolver!.Query(
                MediaStore.Audio.Media.ExternalContentUri,
                new[] { MediaStore.Audio.Media.InterfaceConsts.Id, MediaStore.Audio.Media.InterfaceConsts.Data },
                null, null, null);

            if (cursor == null) return false;

            var colId = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Id);
            var colData = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Data);

            while (cursor.MoveToNext())
            {
                var dataPath = cursor.GetString(colData);
                if (string.IsNullOrEmpty(dataPath)) continue;
                if (pathToSong.TryGetValue(dataPath, out var song))
                {
                    song.MediaStoreId = cursor.GetLong(colId);
                }
            }
            cursor.Close();
            return true;
        }
        catch { return false; }
    }
}

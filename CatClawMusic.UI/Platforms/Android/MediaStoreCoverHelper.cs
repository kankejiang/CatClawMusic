using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Provider;
using CatClawMusic.Core.Models;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>
/// MediaStore 封面加载辅助类，用于从 Android MediaStore 中获取音频文件的专辑封面图。
/// 针对 Android Q（API 29）及以上版本使用 ContentResolver.LoadThumbnail API，
/// 针对 Q 以下版本回退到传统的专辑艺术文件解码方式。
/// 所有方法均为静态方法，无需实例化即可调用。
/// </summary>
public static class MediaStoreCoverHelper
{
    /// <summary>
    /// 绑定到 Android 主线程的 Handler 实例，用于在需要时将操作调度到主线程执行。
    /// </summary>
    private static readonly Handler _mainHandler = new(Looper.MainLooper!);

    /// <summary>
    /// 根据 MediaStore ID 同步加载音频文件的封面缩略图。
    /// <para>
    /// Android Q（API 29）及以上：使用 <c>ContentResolver.LoadThumbnail</c> 加载指定尺寸的缩略图。
    /// Android Q 以下：回退到 <see cref="LoadAlbumArtLegacy"/> 方法，从专辑艺术文件路径解码位图。
    /// </para>
    /// </summary>
    /// <param name="mediaStoreId">音频文件在 MediaStore 中的唯一标识 ID</param>
    /// <param name="size">期望的缩略图尺寸（宽高相同，单位：像素），默认 120</param>
    /// <returns>成功时返回封面 Bitmap 对象；失败或未找到时返回 <c>null</c></returns>
    public static Bitmap? LoadCoverFromMediaStore(long mediaStoreId, int size = 120)
    {
        // MediaStore ID 无效，直接返回 null
        if (mediaStoreId <= 0) return null;
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return null;

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            {
                // Android Q 及以上：通过 ContentUris 拼接 URI，使用 LoadThumbnail 获取缩略图
                var contentUri = ContentUris.WithAppendedId(
                    MediaStore.Audio.Media.ExternalContentUri, mediaStoreId);
                if (contentUri == null) return null;
                var thumbnail = ctx.ContentResolver!.LoadThumbnail(
                    contentUri, new global::Android.Util.Size(size, size), null);
                return thumbnail;
            }
            else
            {
                // Android Q 以下：回退到传统方式，从专辑艺术文件路径解码
                return LoadAlbumArtLegacy(mediaStoreId, size);
            }
        }
        catch { return null; }
    }

    /// <summary>
    /// 根据 MediaStore ID 异步加载音频文件的封面缩略图。
    /// 内部通过 <see cref="Task.Run"/> 将同步的 <see cref="LoadCoverFromMediaStore"/> 调用调度到线程池执行，
    /// 避免阻塞 UI 线程。
    /// </summary>
    /// <param name="mediaStoreId">音频文件在 MediaStore 中的唯一标识 ID</param>
    /// <param name="size">期望的缩略图尺寸（宽高相同，单位：像素），默认 120</param>
    /// <returns>异步任务，结果为封面 Bitmap 对象或 <c>null</c></returns>
    public static async Task<Bitmap?> LoadCoverFromMediaStoreAsync(long mediaStoreId, int size = 120)
    {
        if (mediaStoreId <= 0) return null;
        return await Task.Run(() => LoadCoverFromMediaStore(mediaStoreId, size));
    }

    /// <summary>
    /// 根据音频文件路径加载封面缩略图，同时返回对应的 MediaStore ID。
    /// <para>
    /// 通过文件路径在 MediaStore 中查询对应的音频记录 ID，再调用
    /// <see cref="LoadCoverFromMediaStore"/> 加载封面。此方法适用于仅知道文件路径
    /// 而不知道 MediaStore ID 的场景。
    /// </para>
    /// </summary>
    /// <param name="filePath">音频文件的完整文件系统路径</param>
    /// <param name="size">期望的缩略图尺寸（宽高相同，单位：像素），默认 120</param>
    /// <returns>元组，包含封面 Bitmap（可能为 null）和 MediaStore ID（未找到时为 0）</returns>
    public static (Bitmap? bitmap, long mediaStoreId) LoadCoverByFilePath(string filePath, int size = 120)
    {
        if (string.IsNullOrEmpty(filePath)) return (null, 0);
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return (null, 0);

            // 通过文件路径在 MediaStore 中查询对应的音频记录 ID
            var cursor = ctx.ContentResolver!.Query(
                MediaStore.Audio.Media.ExternalContentUri,
                new[] { MediaStore.Audio.Media.InterfaceConsts.Id },
                $"{MediaStore.Audio.Media.InterfaceConsts.Data} = ?",
                new[] { filePath },
                null);

            if (cursor != null && cursor.MoveToFirst())
            {
                // 找到匹配记录，获取 MediaStore ID 并加载封面
                var id = cursor.GetLong(0);
                cursor.Close();
                return (LoadCoverFromMediaStore(id, size), id);
            }
            cursor?.Close();
        }
        catch { }
        return (null, 0);
    }

    /// <summary>
    /// 传统方式加载专辑封面，适用于 Android Q 以下的版本。
    /// <para>
    /// 通过 MediaStore ID 在专辑表中查询专辑艺术文件路径，然后使用
    /// <see cref="BitmapFactory"/> 进行采样解码，以降低内存占用。
    /// 采样率（InSampleSize）根据原始图片尺寸与目标尺寸动态计算。
    /// </para>
    /// </summary>
    /// <param name="mediaStoreId">音频文件对应的 MediaStore ID</param>
    /// <param name="size">期望的缩略图尺寸（宽高相同，单位：像素）</param>
    /// <returns>解码后的封面 Bitmap 对象，失败时返回 <c>null</c></returns>
    private static Bitmap? LoadAlbumArtLegacy(long mediaStoreId, int size)
    {
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return null;

            // 在专辑表中查询专辑艺术文件的存储路径
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
                    // 第一次解码：仅获取图片尺寸，不分配像素内存（InJustDecodeBounds = true）
                    var options = new BitmapFactory.Options { InJustDecodeBounds = true };
                    BitmapFactory.DecodeFile(albumArtPath, options);

                    // 计算采样率，使解码后的图片尺寸接近目标尺寸，减少内存占用
                    int inSampleSize = 1;
                    if (options.OutHeight > size || options.OutWidth > size)
                    {
                        var halfH = options.OutHeight / 2;
                        var halfW = options.OutWidth / 2;
                        // 逐步增大采样率，直到缩小后的尺寸仍大于等于目标尺寸
                        while ((halfH / inSampleSize) >= size && (halfW / inSampleSize) >= size)
                            inSampleSize *= 2;
                    }

                    // 第二次解码：使用计算出的采样率实际解码图片
                    options.InJustDecodeBounds = false;
                    options.InSampleSize = inSampleSize;
                    // 使用 RGB_565 配置，每个像素仅占 2 字节，相比默认的 ARGB_8888（4字节）节省一半内存
                    options.InPreferredConfig = Bitmap.Config.Rgb565;
                    return BitmapFactory.DecodeFile(albumArtPath, options);
                }
            }
            albumCursor?.Close();
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 批量为歌曲列表填充 MediaStore ID。
    /// <para>
    /// 遍历歌曲列表，筛选出本地来源且尚未填充 MediaStore ID 的歌曲，
    /// 通过一次 MediaStore 查询将所有匹配的音频记录 ID 回填到对应的 Song 对象中。
    /// 此方法采用批量查询策略，避免为每首歌曲单独查询数据库，显著提升性能。
    /// </para>
    /// </summary>
    /// <param name="songs">待填充的歌曲列表</param>
    /// <returns><c>true</c> 表示填充成功或无需填充；<c>false</c> 表示查询失败</returns>
    public static bool BatchFillMediaStoreIds(List<Song> songs)
    {
        if (songs == null || songs.Count == 0) return false;
        try
        {
            var ctx = global::Android.App.Application.Context;
            if (ctx == null) return false;

            // 构建文件路径到 Song 对象的映射，用于后续快速匹配
            var pathToSong = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
            var needQuery = new List<string>();
            foreach (var s in songs)
            {
                // 跳过已有 MediaStore ID 的歌曲
                if (s.MediaStoreId > 0) continue;
                // 跳过非本地来源的歌曲（如在线音乐）
                if (s.Source != SongSource.Local) continue;
                // 跳过无文件路径的歌曲
                if (string.IsNullOrEmpty(s.FilePath)) continue;
                // 去重：同一文件路径只查询一次
                if (!pathToSong.ContainsKey(s.FilePath))
                {
                    pathToSong[s.FilePath] = s;
                    needQuery.Add(s.FilePath);
                }
            }

            // 所有歌曲均已填充，无需查询
            if (needQuery.Count == 0) return true;

            // 一次性查询 MediaStore 中所有音频记录的 ID 和文件路径
            var cursor = ctx.ContentResolver!.Query(
                MediaStore.Audio.Media.ExternalContentUri,
                new[] { MediaStore.Audio.Media.InterfaceConsts.Id, MediaStore.Audio.Media.InterfaceConsts.Data },
                null, null, null);

            if (cursor == null) return false;

            var colId = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Id);
            var colData = cursor.GetColumnIndexOrThrow(MediaStore.Audio.Media.InterfaceConsts.Data);

            // 遍历查询结果，将 MediaStore ID 回填到匹配的 Song 对象
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

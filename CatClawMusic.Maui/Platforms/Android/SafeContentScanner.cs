using Android.Content;
using Android.Database;
using Android.Provider;
using CatClawMusic.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using AUri = Android.Net.Uri;
using CatClawMusic.Core.Interfaces;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>SAF（Storage Access Framework）安全内容扫描器，通过 content:// URI 遍历并读取音频文件元数据</summary>
public static class SafeContentScanner
{
    /// <summary>支持的音频文件扩展名集合（不区分大小写）</summary>
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac", ".wma",
        ".wav", ".aiff", ".aifc", ".ape", ".wv", ".tta", ".mka", ".dsf", ".dff",
        ".mid", ".midi", ".rmi", ".spx", ".amr", ".3gp"
    };

    /// <summary>MediaMetadataRetriever 元数据键：音轨号</summary>
    private const int MdKeyTrackNumber = 0;
    /// <summary>MediaMetadataRetriever 元数据键：专辑名</summary>
    private const int MdKeyAlbum = 1;
    /// <summary>MediaMetadataRetriever 元数据键：艺术家</summary>
    private const int MdKeyArtist = 2;
    /// <summary>MediaMetadataRetriever 元数据键：流派</summary>
    private const int MdKeyGenre = 6;
    /// <summary>MediaMetadataRetriever 元数据键：标题</summary>
    private const int MdKeyTitle = 7;
    /// <summary>MediaMetadataRetriever 元数据键：年份</summary>
    private const int MdKeyYear = 8;
    /// <summary>MediaMetadataRetriever 元数据键：时长（毫秒）</summary>
    private const int MdKeyDuration = 9;
    /// <summary>MediaMetadataRetriever 元数据键：比特率（bps）</summary>
    private const int MdKeyBitrate = 20;

    /// <summary>扫描已保存的 SAF 文件夹，批量读取音频文件元数据，并通过回调分批返回 Song 列表</summary>
    /// <param name="songCallback">每批 Song 列表的异步回调</param>
    /// <param name="progress">进度上报，包含已完成数、总数和状态文本</param>
    /// <param name="existingPathModTimes">已有文件的最后修改时间映射，用于增量扫描（跳过未变更的文件）</param>
    /// <param name="onPathDiscovered">每发现一个音频文件时的回调</param>
    /// <returns>表示异步扫描完成的任务</returns>
    public static async Task ScanSavedFoldersAsync(
        Func<List<Song>, Task> songCallback,
        IProgress<(int done, int total, string status)>? progress = null,
        Dictionary<string, long>? existingPathModTimes = null,
        Action<string>? onPathDiscovered = null)
    {
        var uris = FolderPicker.GetSavedFolderUris();
        if (uris.Count == 0) return;

        for (int i = 0; i < uris.Count; i++)
        {
            try
            {
                var treeUri = AUri.Parse(uris[i]);
                if (treeUri == null) continue;
                progress?.Report((i, uris.Count, $"遍历文件夹 {i + 1}/{uris.Count}..."));
                await ScanTreeUriAsync(treeUri, songCallback, progress, existingPathModTimes, onPathDiscovered);
            }
            catch (Exception ex)
            {
                Log.Debug("SafeContentScanner", $"[SAF] Scan error: {ex.Message}");
            }
        }
    }

    /// <summary>扫描单个 SAF 树形 URI：递归收集音频文件、增量过滤未变更文件、并行读取元数据并分批回调</summary>
    /// <param name="treeUri">SAF 树形 URI</param>
    /// <param name="songCallback">每批 Song 列表的异步回调</param>
    /// <param name="progress">进度上报</param>
    /// <param name="existingPathModTimes">已有文件的最后修改时间映射，用于增量扫描</param>
    /// <param name="onPathDiscovered">每发现一个音频文件时的回调</param>
    /// <returns>表示异步扫描完成的任务</returns>
    private static async Task ScanTreeUriAsync(AUri treeUri, Func<List<Song>, Task> songCallback,
        IProgress<(int done, int total, string status)>? progress,
        Dictionary<string, long>? existingPathModTimes,
        Action<string>? onPathDiscovered)
    {
        var sw = Stopwatch.StartNew();
        var ctx = global::Android.App.Application.Context;
        var docId = DocumentsContract.GetTreeDocumentId(treeUri);

        var audioFiles = new ConcurrentBag<(AUri uri, string name, long size, long lastModified)>();
        var lrcMap = new ConcurrentDictionary<string, AUri>(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() => CollectAudioFiles(ctx, treeUri, docId, audioFiles, lrcMap, onPathDiscovered));

        var fileList = audioFiles.ToList();
        if (fileList.Count == 0) return;

        // 增量扫描：跳过未变更的文件
        var toRead = new List<(AUri uri, string name, long size, long lastModified)>();
        foreach (var file in fileList)
        {
            var uriString = file.uri.ToString();
            if (existingPathModTimes != null &&
                existingPathModTimes.TryGetValue(uriString, out var dbMod) &&
                dbMod == file.lastModified / 1000)
                continue;
            toRead.Add(file);
        }

        var total = toRead.Count;
        progress?.Report((0, total, $"发现 {fileList.Count} 个音频文件，{total} 个需要读取..."));

        if (toRead.Count == 0) return;

        var songQueue = new BlockingCollection<Song>(200);
        var processed = 0;

        var producerTask = Task.Run(() =>
        {
            try
            {
                Parallel.ForEach(toRead, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
                }, file =>
                {
                    var song = ReadSongWithRetriever(file.uri, file.name, file.size, file.lastModified);
                    if (song != null)
                    {
                        var audioNameNoExt = Path.GetFileNameWithoutExtension(file.name).ToLowerInvariant();
                        if (lrcMap.TryGetValue(audioNameNoExt, out var lrcUri))
                            song.LyricsPath = lrcUri.ToString();
                        songQueue.Add(song);
                    }
                });
            }
            finally { songQueue.CompleteAdding(); }
        });

        await Task.Run(async () =>
        {
            var batch = new List<Song>();
            foreach (var song in songQueue.GetConsumingEnumerable())
            {
                batch.Add(song);
                if (batch.Count >= 30)
                {
                    Interlocked.Add(ref processed, batch.Count);
                    var toSend = batch;
                    batch = new List<Song>();
                    await songCallback(toSend);
                    progress?.Report((processed, total, $"读取元数据 {processed}/{total}..."));
                }
            }
            if (batch.Count > 0)
            {
                Interlocked.Add(ref processed, batch.Count);
                await songCallback(batch);
            }
        });

        await producerTask;
        sw.Stop();
        Log.Debug("SafeContentScanner", $"[SAF] 扫描完成：{fileList.Count} 个文件，读取 {total}，耗时 {sw.ElapsedMilliseconds}ms");
    }

    /// <summary>使用多线程递归遍历 SAF 树形 URI 下的所有子文档，收集音频文件与歌词文件</summary>
    /// <param name="ctx">Android 上下文</param>
    /// <param name="treeUri">SAF 树形 URI</param>
    /// <param name="rootDocId">根文档 ID</param>
    /// <param name="results">收集到的音频文件列表（线程安全）</param>
    /// <param name="lrcMap">歌词文件映射（按文件名小写无扩展名）</param>
    /// <param name="onPathDiscovered">每发现一个音频文件时的回调</param>
    private static void CollectAudioFiles(Context ctx, AUri treeUri, string rootDocId,
        ConcurrentBag<(AUri uri, string name, long size, long lastModified)> results,
        ConcurrentDictionary<string, AUri>? lrcMap = null,
        Action<string>? onPathDiscovered = null)
    {
        var queue = new ConcurrentQueue<string>();
        queue.Enqueue(rootDocId);
        var pending = 1;
        var degree = Math.Min(4, Environment.ProcessorCount);

        var workers = new Task[degree];
        for (int i = 0; i < degree; i++)
        {
            workers[i] = Task.Run(() =>
            {
                while (true)
                {
                    if (!queue.TryDequeue(out var docId))
                    {
                        if (Interlocked.CompareExchange(ref pending, 0, 0) == 0) break;
                        Thread.Sleep(5);
                        continue;
                    }

                    try
                    {
                        var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, docId);
                        ICursor? cursor = null;
                        try
                        {
                            cursor = ctx.ContentResolver!.Query(childrenUri,
                                new[] {
                                    DocumentsContract.Document.ColumnDocumentId,
                                    DocumentsContract.Document.ColumnMimeType,
                                    DocumentsContract.Document.ColumnDisplayName,
                                    DocumentsContract.Document.ColumnSize,
                                    DocumentsContract.Document.ColumnLastModified,
                                }, null, null, null);

                            if (cursor == null) continue;
                            var childDirs = new List<string>();

                            while (cursor.MoveToNext())
                            {
                                var childId = cursor.GetString(0);
                                var mimeType = cursor.GetString(1);
                                var displayName = cursor.GetString(2);
                                var size = cursor.GetLong(3);
                                var lastModified = cursor.GetLong(4);
                                if (string.IsNullOrEmpty(childId)) continue;

                                if (DocumentsContract.Document.MimeTypeDir.Equals(mimeType, StringComparison.OrdinalIgnoreCase))
                                {
                                    childDirs.Add(childId);
                                }
                                else if (!string.IsNullOrEmpty(displayName) && IsAudioFile(displayName))
                                {
                                    var docUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);
                                    onPathDiscovered?.Invoke(docUri.ToString());
                                    results.Add((docUri, displayName, size, lastModified));
                                }
                                else if (lrcMap != null && !string.IsNullOrEmpty(displayName)
                                    && (displayName.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)
                                        || displayName.EndsWith(".ttml", StringComparison.OrdinalIgnoreCase)))
                                {
                                    var lrcNameNoExt = Path.GetFileNameWithoutExtension(displayName).ToLowerInvariant();
                                    var docUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);
                                    lrcMap.TryAdd(lrcNameNoExt, docUri);
                                }
                            }

                            foreach (var childDir in childDirs)
                            {
                                Interlocked.Increment(ref pending);
                                queue.Enqueue(childDir);
                            }
                        }
                        catch { }
                        finally { cursor?.Close(); }
                    }
                    catch { }
                    finally { Interlocked.Decrement(ref pending); }
                }
            });
        }
        Task.WaitAll(workers);
    }

    /// <summary>使用 MediaMetadataRetriever 读取单个音频文件的元数据并构造 Song 对象；读取失败时返回仅含基础信息的 Song</summary>
    /// <param name="uri">音频文件的 SAF URI</param>
    /// <param name="displayName">文件显示名</param>
    /// <param name="size">文件大小（字节）</param>
    /// <param name="lastModified">最后修改时间（毫秒）</param>
    /// <returns>构造完成的 Song 对象</returns>
    private static Song? ReadSongWithRetriever(AUri uri, string displayName, long size, long lastModified)
    {
        var retriever = new global::Android.Media.MediaMetadataRetriever();
        try
        {
            retriever.SetDataSource(global::Android.App.Application.Context, uri);

            var title = retriever.ExtractMetadata(MdKeyTitle);
            var artist = retriever.ExtractMetadata(MdKeyArtist);
            var album = retriever.ExtractMetadata(MdKeyAlbum);
            var durationStr = retriever.ExtractMetadata(MdKeyDuration);
            var bitrateStr = retriever.ExtractMetadata(MdKeyBitrate);
            var yearStr = retriever.ExtractMetadata(MdKeyYear);
            var trackStr = retriever.ExtractMetadata(MdKeyTrackNumber);
            var genre = retriever.ExtractMetadata(MdKeyGenre);

            int.TryParse(durationStr, out var d);
            int.TryParse(bitrateStr, out var br);
            int.TryParse(yearStr, out var year);
            var trackPart = trackStr?.Split('/')[0];
            int.TryParse(trackPart, out var trackNumber);

            // 提取嵌入封面并缓存
            var coverPath = ExtractCover(retriever, uri.ToString());

            return new Song
            {
                Title = !string.IsNullOrWhiteSpace(title) ? title : Path.GetFileNameWithoutExtension(displayName),
                Artist = !string.IsNullOrWhiteSpace(artist) ? artist : "未知艺术家",
                Album = album ?? "未知专辑",
                Duration = d / 1000,
                FileSize = size,
                Bitrate = br / 1000,
                Year = year,
                TrackNumber = trackNumber,
                Genre = genre,
                FilePath = uri.ToString(),
                Source = SongSource.Local,
                DateModified = lastModified > 0 ? lastModified / 1000 : 0,
                CoverArtPath = coverPath
            };
        }
        catch
        {
            return new Song
            {
                Title = Path.GetFileNameWithoutExtension(displayName),
                Artist = "未知艺术家",
                Album = "未知专辑",
                FilePath = uri.ToString(),
                FileSize = size,
                Source = SongSource.Local,
                DateModified = lastModified > 0 ? lastModified / 1000 : 0
            };
        }
        finally { retriever.Release(); }
    }

    /// <summary>
    /// 从 MediaMetadataRetriever 提取嵌入封面并缓存到文件。
    /// 用 filePath 的哈希值作为文件名（因为 song.Id 还未生成）。
    /// </summary>
    /// <param name="retriever">已设置数据源的 MediaMetadataRetriever</param>
    /// <param name="filePath">音频文件路径，用于生成封面文件名</param>
    /// <returns>封面文件路径，无封面时返回 null</returns>
    private static string? ExtractCover(global::Android.Media.MediaMetadataRetriever retriever, string filePath)
    {
        try
        {
            var art = retriever.GetEmbeddedPicture();
            if (art != null)
            {
                var cacheDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "covers");
                System.IO.Directory.CreateDirectory(cacheDir);
                // 用 filePath 的哈希值作为文件名
                var hash = filePath.GetHashCode().ToString("X");
                var outputPath = System.IO.Path.Combine(cacheDir, $"cover_{hash}.jpg");
                System.IO.File.WriteAllBytes(outputPath, art);
                return outputPath;
            }
        }
        catch { }
        return null;
    }

    /// <summary>根据文件名扩展名判断是否为受支持的音频文件</summary>
    /// <param name="fileName">文件名</param>
    /// <returns>是音频文件返回 true，否则返回 false</returns>
    private static bool IsAudioFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return AudioExtensions.Contains(ext);
    }
}

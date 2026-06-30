using Android.Content;
using Android.Database;
using Android.Provider;
using CatClawMusic.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using AUri = Android.Net.Uri;

namespace CatClawMusic.Maui.Platforms.Android;

/// <summary>SAF（Storage Access Framework）安全内容扫描器，通过 content:// URI 遍历并读取音频文件元数据</summary>
public static class SafeContentScanner
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac", ".wma",
        ".wav", ".aiff", ".aifc", ".ape", ".wv", ".tta", ".mka", ".dsf", ".dff",
        ".mid", ".midi", ".rmi", ".spx", ".amr", ".3gp"
    };

    private const int MdKeyTrackNumber = 0;
    private const int MdKeyAlbum = 1;
    private const int MdKeyArtist = 2;
    private const int MdKeyGenre = 6;
    private const int MdKeyTitle = 7;
    private const int MdKeyYear = 8;
    private const int MdKeyDuration = 9;
    private const int MdKeyBitrate = 20;

    /// <summary>扫描已保存的 SAF 文件夹，批量读取音频文件元数据</summary>
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
                Debug.WriteLine($"[SAF] Scan error: {ex.Message}");
            }
        }
    }

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
        Debug.WriteLine($"[SAF] 扫描完成：{fileList.Count} 个文件，读取 {total}，耗时 {sw.ElapsedMilliseconds}ms");
    }

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

    private static bool IsAudioFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return AudioExtensions.Contains(ext);
    }
}

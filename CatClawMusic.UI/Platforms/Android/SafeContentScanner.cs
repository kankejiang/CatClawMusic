using Android.Content;
using Android.Database;
using Android.Provider;
using CatClawMusic.Core.Models;
using AUri = Android.Net.Uri;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>SAF（Storage Access Framework）安全内容扫描器，通过 content:// URI 遍历并读取音频文件元数据</summary>
public static class SafeContentScanner
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac", ".wma",
        ".wav", ".aiff", ".aifc", ".ape", ".wv", ".tta", ".mka", ".dsf", ".dff",
        ".mid", ".midi", ".rmi", ".spx", ".amr", ".3gp", ".mkv", ".webm"
    };

    private const int MdKeyTrackNumber = 0;
    private const int MdKeyAlbum = 1;
    private const int MdKeyArtist = 2;
    private const int MdKeyGenre = 6;
    private const int MdKeyTitle = 7;
    private const int MdKeyYear = 8;
    private const int MdKeyDuration = 9;
    private const int MdKeyBitrate = 20;

    /// <summary>扫描已保存的 SAF 文件夹，批量读取音频文件元数据并通过回调返回</summary>
    /// <param name="songCallback">每发现一批歌曲时回调</param>
    /// <param name="progress">扫描进度报告</param>
    public static async Task ScanSavedFolderAsync(
        Func<List<Song>, Task> songCallback,
        IProgress<(int done, int total, string status)>? progress = null)
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
                await ScanTreeUriAsync(treeUri, songCallback, progress);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF scan error: {ex.Message}");
            }
        }
    }

    private static async Task ScanTreeUriAsync(AUri treeUri, Func<List<Song>, Task> songCallback,
        IProgress<(int done, int total, string status)>? progress)
    {
        var ctx = global::Android.App.Application.Context;
        var docId = DocumentsContract.GetTreeDocumentId(treeUri);

        var audioFiles = new List<(AUri uri, string name, long size, long lastModified)>();
        var lrcMap = new Dictionary<string, AUri>(StringComparer.OrdinalIgnoreCase);
        await Task.Run(() => CollectAudioFiles(ctx, treeUri, docId, audioFiles, lrcMap));

        if (audioFiles.Count == 0) return;

        var total = audioFiles.Count;
        progress?.Report((0, total, $"发现 {total} 个音频文件，读取元数据..."));

        var songQueue = new System.Collections.Concurrent.BlockingCollection<Song>(200);
        var processed = 0;

        var producerTask = Task.Run(() =>
        {
            try
            {
                Parallel.ForEach(audioFiles, new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Min(4, Environment.ProcessorCount)
                }, file =>
                {
                    var song = ReadSongWithMetadataRetriever(file.uri, file.name, file.size, file.lastModified);
                    if (song != null)
                    {
                        // 匹配同目录 .lrc 文件
                        var audioNameNoExt = Path.GetFileNameWithoutExtension(file.name).ToLowerInvariant();
                        if (lrcMap.TryGetValue(audioNameNoExt, out var lrcUri))
                            song.LyricsPath = lrcUri.ToString();
                        songQueue.Add(song);
                    }
                });
            }
            finally
            {
                songQueue.CompleteAdding();
            }
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
    }

    private static void CollectAudioFiles(Context ctx, AUri treeUri, string rootDocId,
        List<(AUri uri, string name, long size, long lastModified)> results,
        Dictionary<string, AUri>? lrcMap = null)
    {
        var stack = new Stack<string>();
        stack.Push(rootDocId);

        while (stack.Count > 0)
        {
            var docId = stack.Pop();
            var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, docId);

            ICursor? cursor = null;
            try
            {
                cursor = ctx.ContentResolver!.Query(childrenUri,
                    new[]
                    {
                        DocumentsContract.Document.ColumnDocumentId,
                        DocumentsContract.Document.ColumnMimeType,
                        DocumentsContract.Document.ColumnDisplayName,
                        DocumentsContract.Document.ColumnSize,
                        DocumentsContract.Document.ColumnLastModified,
                    }, null, null, null);

                if (cursor == null) continue;

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
                        stack.Push(childId);
                    }
                    else if (!string.IsNullOrEmpty(displayName) && IsAudioFile(displayName))
                    {
                        var docUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);
                        results.Add((docUri, displayName, size, lastModified));
                    }
                    else if (lrcMap != null && !string.IsNullOrEmpty(displayName)
                        && (displayName.EndsWith(".lrc", StringComparison.OrdinalIgnoreCase)
                            || displayName.EndsWith(".ttml", StringComparison.OrdinalIgnoreCase)
                            || displayName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
                    {
                        // 收集歌词文件（.lrc/.ttml/.xml），以文件名（不含扩展名）为 key
                        var lrcNameNoExt = Path.GetFileNameWithoutExtension(displayName);
                        var docUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);
                        lrcNameNoExt = lrcNameNoExt.ToLowerInvariant();
                        if (!lrcMap.ContainsKey(lrcNameNoExt))
                            lrcMap[lrcNameNoExt] = docUri;
                    }
                }
            }
            catch { }
            finally
            {
                cursor?.Close();
            }
        }
    }

    private static Song? ReadSongWithMetadataRetriever(AUri uri, string displayName, long size, long lastModified)
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

            var duration = 0;
            if (!string.IsNullOrEmpty(durationStr) && int.TryParse(durationStr, out var d))
                duration = d / 1000;

            var bitrate = 0;
            if (!string.IsNullOrEmpty(bitrateStr) && int.TryParse(bitrateStr, out var br))
                bitrate = br / 1000;

            var year = 0;
            if (!string.IsNullOrEmpty(yearStr) && int.TryParse(yearStr, out var y))
                year = y;

            var trackNumber = 0;
            if (!string.IsNullOrEmpty(trackStr))
            {
                var trackPart = trackStr.Split('/')[0];
                int.TryParse(trackPart, out trackNumber);
            }

            return new Song
            {
                Title = !string.IsNullOrWhiteSpace(title) ? title : Path.GetFileNameWithoutExtension(displayName),
                Artist = !string.IsNullOrWhiteSpace(artist) ? artist : "未知艺术家",
                Album = album ?? "未知专辑",
                Duration = duration,
                FileSize = size,
                Bitrate = bitrate,
                Year = year,
                TrackNumber = trackNumber,
                Genre = genre,
                FilePath = uri.ToString(),
                Source = SongSource.Local,
                DateModified = lastModified > 0 ? lastModified / 1000 : 0
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
        finally
        {
            retriever.Release();
        }
    }

    private static bool IsAudioFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return AudioExtensions.Contains(ext);
    }
}

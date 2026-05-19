using Android.Content;
using Android.Database;
using Android.Provider;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using AUri = Android.Net.Uri;

namespace CatClawMusic.UI.Platforms.Android;

/// <summary>SAF（Storage Access Framework）内容扫描器，通过 content:// URI 遍历文件夹并读取音频文件元数据</summary>
public static class SafeContentScanner
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac", ".wma",
        ".wav", ".aiff", ".aifc", ".ape", ".wv", ".tta", ".mka", ".dsf", ".dff",
        ".mid", ".midi", ".rmi", ".spx", ".amr", ".3gp", ".mkv", ".webm"
    };

    /// <summary>扫描所有已保存的 SAF 文件夹，返回歌曲列表</summary>
    public static async Task<List<Song>> ScanSavedFolderAsync()
    {
        var uris = FolderPicker.GetSavedFolderUris();
        if (uris.Count == 0) return new List<Song>();

        var allSongs = new List<Song>();
        foreach (var uriStr in uris)
        {
            try
            {
                var treeUri = AUri.Parse(uriStr);
                if (treeUri == null) continue;
                var songs = ScanTreeUri(treeUri);
                allSongs.AddRange(songs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF scan error for {uriStr}: {ex.Message}");
            }
        }

        return allSongs;
    }

    /// <summary>异步扫描单个 content:// 树 URI</summary>
    public static Task<List<Song>> ScanTreeUriAsync(AUri treeUri)
    {
        return Task.Run(() => ScanTreeUri(treeUri));
    }

    /// <summary>扫描单个 content:// 树 URI，递归遍历所有子目录</summary>
    private static List<Song> ScanTreeUri(AUri treeUri)
    {
        var songs = new List<Song>();
        try
        {
            var ctx = global::Android.App.Application.Context;
            var docId = DocumentsContract.GetTreeDocumentId(treeUri);
            WalkDocuments(ctx, treeUri, docId, songs);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF 扫描异常: {ex.Message}");
        }
        return songs;
    }

    /// <summary>递归遍历 DocumentsProvider 文档树</summary>
    private static void WalkDocuments(Context ctx, AUri treeUri, string docId, List<Song> songs)
    {
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

            if (cursor == null) return;

            while (cursor.MoveToNext())
            {
                var childId = cursor.GetString(0);
                var mimeType = cursor.GetString(1);
                var displayName = cursor.GetString(2);
                var size = cursor.GetLong(3);

                if (string.IsNullOrEmpty(childId)) continue;

                if (DocumentsContract.Document.MimeTypeDir.Equals(mimeType, StringComparison.OrdinalIgnoreCase))
                {
                    WalkDocuments(ctx, treeUri, childId, songs);
                }
                else if (!string.IsNullOrEmpty(displayName) && IsAudioFile(displayName))
                {
                    var docUri = DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);
                    var song = ReadSongFromUri(ctx, docUri, displayName, size);
                    if (song != null) songs.Add(song);
                }
            }
        }
        catch (Exception)
        {
        }
        finally
        {
            cursor?.Close();
        }
    }

    /// <summary>从 content:// URI 读取音频文件元数据</summary>
    private static Song? ReadSongFromUri(Context ctx, AUri uri, string displayName, long size)
    {
        try
        {
            using var stream = ctx.ContentResolver!.OpenInputStream(uri);
            if (stream == null) return null;

            var song = TagReader.ReadFromStream(stream, uri.ToString(), displayName, size);
            return song;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF read error: {displayName} - {ex.Message}");
            return null;
        }
    }

    /// <summary>根据扩展名判断是否为支持的音频文件</summary>
    private static bool IsAudioFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return AudioExtensions.Contains(ext);
    }
}

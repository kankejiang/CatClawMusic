using Android.Content;
using Android.Database;
using Android.Provider;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using AUri = Android.Net.Uri;

namespace CatClawMusic.UI.Platforms.Android;

public static class SafeContentScanner
{
    private static readonly HashSet<string> AudioExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".flac", ".wav", ".ogg", ".m4a", ".aac", ".wma", ".opus", ".ape", ".aiff"
    };

    public static async Task<List<Song>> ScanSavedFolderAsync()
    {
        var uris = FolderPicker.GetSavedFolderUris();
        System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF ScanSavedFolder: {uris.Count} folder(s)");
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

        System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF 多文件夹扫描完成: 共 {allSongs.Count} 首");
        return allSongs;
    }

    public static Task<List<Song>> ScanTreeUriAsync(AUri treeUri)
    {
        return Task.Run(() => ScanTreeUri(treeUri));
    }

    private static List<Song> ScanTreeUri(AUri treeUri)
    {
        var songs = new List<Song>();
        try
        {
            var ctx = global::Android.App.Application.Context;
            var docId = DocumentsContract.GetTreeDocumentId(treeUri);
            WalkDocuments(ctx, treeUri, docId, songs);
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF 扫描完成: {songs.Count} 首");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF 扫描异常: {ex.Message}");
        }
        return songs;
    }

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
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SAF walk error: {ex.Message}");
        }
        finally
        {
            cursor?.Close();
        }
    }

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

    private static bool IsAudioFile(string fileName)
    {
        var ext = Path.GetExtension(fileName);
        return AudioExtensions.Contains(ext);
    }
}

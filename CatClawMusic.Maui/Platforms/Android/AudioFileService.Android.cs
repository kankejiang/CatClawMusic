using Android.Content;
using Android.Provider;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;
using System.Text;
using TagLib;
using IOFile = System.IO.File;
using TagLibFile = TagLib.File;
using AUri = Android.Net.Uri;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// <see cref="AudioFileService"/> 的 Android SAF（Storage Access Framework）实现。
/// <para>
/// <c>content://</c> URI 无法直接以文件路径交给 TagLib 写入，采用安全方案：
/// 复制到应用缓存临时文件 → TagLib 修改 → <see cref="ContentResolver.OpenOutputStream(AUri)"/> 整体写回。
/// 重命名/删除/创建侧车 .lrc 走 <see cref="DocumentsContract"/> 原生 API。
/// </para>
/// </summary>
public partial class AudioFileService
{
    private static Android.Content.Context Ctx => global::Android.App.Application.Context;

    // ═══════════════ content:// 读取 ═══════════════

    private static async Task<AudioTagInfo?> ReadContentTagsAsync(string uri)
    {
        try
        {
            using var stream = Ctx.ContentResolver!.OpenInputStream(AUri.Parse(uri));
            if (stream == null) return null;
            var name = QueryDisplayName(uri) ?? Path.GetFileName(uri) ?? "unknown";
            var abstraction = new ReadOnlyFileAbstraction(name, stream);
            using var file = TagLibFile.Create(abstraction);
            var tag = file.Tag;
            return new AudioTagInfo
            {
                FilePath = uri,
                Title = !string.IsNullOrWhiteSpace(tag.Title) ? tag.Title : Path.GetFileNameWithoutExtension(name),
                Artist = string.Join(" / ", tag.Performers),
                Album = tag.Album ?? string.Empty,
                AlbumArtist = tag.FirstAlbumArtist ?? string.Empty,
                Year = tag.Year != 0 ? tag.Year.ToString() : string.Empty,
                Genre = tag.FirstGenre ?? string.Empty,
                TrackNumber = tag.Track != 0 ? tag.Track.ToString() : string.Empty,
                DiscNumber = tag.Disc != 0 ? tag.Disc.ToString() : string.Empty,
                Composer = string.Join(" / ", tag.Composers),
                Lyricist = ReadLyricist(file),
                Comment = tag.Comment ?? string.Empty,
                Copyright = tag.Copyright ?? string.Empty,
                CustomTags = ReadCustomTags(file),
                Lyrics = string.IsNullOrWhiteSpace(tag.Lyrics) ? null : tag.Lyrics,
                Cover = tag.Pictures is { Length: > 0 } ? tag.Pictures[0].Data.Data : null,
                DurationMs = SafeDurationMs(file),
                FileSize = QueryFileSize(uri),
                Bitrate = SafeBitrate(file),
                SampleRate = SafeSampleRate(file),
                Channels = SafeChannels(file),
                Extension = Path.GetExtension(name),
                DisplayName = name
            };
        }
        catch
        {
            return null;
        }
    }

    // ═══════════════ content:// 写入（临时文件方案） ═══════════════

    private static async Task<bool> WriteContentTagsAsync(string uri, AudioTagEdit edit)
    {
        var ext = Path.GetExtension(QueryDisplayName(uri) ?? uri);
        if (string.IsNullOrEmpty(ext)) ext = ".tmp";
        var tmp = Path.Combine(Path.GetTempPath(), $"audiotag_{Guid.NewGuid():N}{ext}");
        try
        {
            // 1) content:// → 临时文件
            using (var src = Ctx.ContentResolver!.OpenInputStream(AUri.Parse(uri)))
            {
                if (src == null) return false;
                using var dst = IOFile.Create(tmp);
                await src.CopyToAsync(dst);
            }
            // 2) TagLib 修改临时文件
            if (!WriteFileTags(tmp, edit)) return false;
            // 3) 临时文件 → content://（整体覆盖）
            using var os = Ctx.ContentResolver!.OpenOutputStream(AUri.Parse(uri));
            if (os == null) return false;
            using var fs = IOFile.OpenRead(tmp);
            await fs.CopyToAsync(os);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            try { IOFile.Delete(tmp); } catch { }
        }
    }

    // ═══════════════ 侧车 .lrc ═══════════════

    private static async Task<string?> WriteContentSidecarLyricsAsync(string uri, string lrcText)
    {
        try
        {
            var docUri = AUri.Parse(uri);
            var name = Path.GetFileNameWithoutExtension(QueryDisplayName(uri) ?? uri) + ".lrc";
            var bytes = Encoding.UTF8.GetBytes(lrcText ?? string.Empty);

            // 已存在同名 .lrc：直接覆盖写
            var existing = FindSiblingLrc(docUri, name);
            if (existing != null)
            {
                using var os = Ctx.ContentResolver!.OpenOutputStream(existing);
                if (os == null) return null;
                await os.WriteAsync(bytes, 0, bytes.Length);
                return existing.ToString();
            }

            // 不存在：在父目录创建 .lrc 文档后写入
            var parent = GetParentDocumentUri(docUri);
            if (parent == null) return null;
            var created = DocumentsContract.CreateDocument(Ctx.ContentResolver, parent, "text/plain", name);
            if (created == null) return null;
            using var os2 = Ctx.ContentResolver!.OpenOutputStream(created);
            if (os2 == null) return null;
            await os2.WriteAsync(bytes, 0, bytes.Length);
            return created.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static bool DeleteContentSidecarLyrics(string uri)
    {
        try
        {
            var docUri = AUri.Parse(uri);
            var name = Path.GetFileNameWithoutExtension(QueryDisplayName(uri) ?? uri) + ".lrc";
            var existing = FindSiblingLrc(docUri, name);
            if (existing == null) return true;
            return DocumentsContract.DeleteDocument(Ctx.ContentResolver, existing);
        }
        catch { return false; }
    }

    // ═══════════════ 重命名 / 删除 ═══════════════

    private static string? RenameContentDocument(string uri, string newName)
    {
        try
        {
            var newUri = DocumentsContract.RenameDocument(Ctx.ContentResolver, AUri.Parse(uri), newName);
            return newUri?.ToString();
        }
        catch { return null; }
    }

    private static bool DeleteContentDocument(string uri)
    {
        try
        {
            return DocumentsContract.DeleteDocument(Ctx.ContentResolver, AUri.Parse(uri));
        }
        catch { return false; }
    }

    // ═══════════════ SAF 辅助 ═══════════════

    /// <summary>查询文档显示名（OpenableColumns.DisplayName）</summary>
    private static string? QueryDisplayName(string uri)
    {
        try
        {
            using var cursor = Ctx.ContentResolver!.Query(AUri.Parse(uri), new[] { OpenableColumns.DisplayName }, null, null, null);
            if (cursor != null && cursor.MoveToFirst())
                return cursor.GetString(0);
        }
        catch { }
        return null;
    }

    /// <summary>查询文档大小（OpenableColumns.Size），失败返回 0</summary>
    private static long QueryFileSize(string uri)
    {
        try
        {
            using var cursor = Ctx.ContentResolver!.Query(AUri.Parse(uri), new[] { OpenableColumns.Size }, null, null, null);
            if (cursor != null && cursor.MoveToFirst())
                return cursor.GetLong(0);
        }
        catch { }
        return 0;
    }

    /// <summary>从 document URI 构造其父目录的 document URI；已是根目录或无父目录时返回 null</summary>
    private static AUri? GetParentDocumentUri(AUri docUri)
    {
        try
        {
            var docId = DocumentsContract.GetDocumentId(docUri);
            var lastSlash = docId.LastIndexOf('/');
            if (lastSlash <= 0) return null;
            var parentDocId = docId[..lastSlash];
            var treeUri = GetTreeUri(docUri);
            if (treeUri == null) return null;
            return DocumentsContract.BuildDocumentUriUsingTree(treeUri, parentDocId);
        }
        catch { return null; }
    }

    /// <summary>从 document URI 提取 tree URI（content://authority/tree/{treeId}）</summary>
    private static AUri? GetTreeUri(AUri docUri)
    {
        try
        {
            // path 形如 /tree/{treeId}/document/{docId}
            var segments = docUri.PathSegments;
            if (segments.Count >= 2 && string.Equals(segments[0], "tree", StringComparison.Ordinal))
            {
                return new AUri.Builder()
                    .Scheme(docUri.Scheme)
                    .Authority(docUri.Authority)
                    .AppendPath("tree")
                    .AppendPath(segments[1])
                    .Build();
            }
        }
        catch { }
        return null;
    }

    /// <summary>在文档同目录下按显示名查找同名 .lrc；未找到返回 null</summary>
    private static AUri? FindSiblingLrc(AUri docUri, string lrcName)
    {
        try
        {
            var docId = DocumentsContract.GetDocumentId(docUri);
            var lastSlash = docId.LastIndexOf('/');
            var parentDocId = lastSlash > 0 ? docId[..lastSlash] : docId;
            var treeUri = GetTreeUri(docUri);
            if (treeUri == null) return null;

            var childrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(treeUri, parentDocId);
            using var cursor = Ctx.ContentResolver!.Query(childrenUri, new[]
            {
                DocumentsContract.Document.ColumnDocumentId,
                DocumentsContract.Document.ColumnDisplayName,
            }, null, null, null);
            if (cursor == null) return null;

            while (cursor.MoveToNext())
            {
                var childId = cursor.GetString(0);
                var name = cursor.GetString(1);
                if (string.Equals(name, lrcName, StringComparison.OrdinalIgnoreCase))
                    return DocumentsContract.BuildDocumentUriUsingTree(treeUri, childId);
            }
        }
        catch { }
        return null;
    }
}

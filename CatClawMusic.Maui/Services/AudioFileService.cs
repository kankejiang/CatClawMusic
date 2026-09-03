using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Text;
using TagLib;
using TagLib.Id3v2;
using IOFile = System.IO.File;
using TagLibFile = TagLib.File;
using TagLibPicture = TagLib.IPicture;

namespace CatClawMusic.Maui.Services;

/// <summary>
/// 音频文件操作服务（<see cref="IAudioFileService"/> 实现）：音频标签读写、内嵌歌词/封面写入、
/// 侧车 .lrc 写入、重命名、删除。为插件提供 Lyrico 式标签编辑/批量操作的文件写能力。
/// <para>
/// 真实文件路径直接经 TagLib 读写；SAF <c>content://</c> URI 由 Android 平台部分
/// （复制到临时文件 → TagLib 修改 → 整体写回）处理。Windows 端走文件系统实现。
/// </para>
/// </summary>
public partial class AudioFileService : IAudioFileService
{
    public Task<AudioTagInfo?> ReadTagsAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return Task.FromResult<AudioTagInfo?>(null);
#if ANDROID
        if (IsContentUri(uri)) return ReadContentTagsAsync(uri);
#endif
        return Task.Run(() => ReadFileTags(uri));
    }

    public async Task<bool> WriteTagsAsync(string uri, AudioTagEdit edit)
    {
        if (string.IsNullOrWhiteSpace(uri) || edit == null) return false;
#if ANDROID
        if (IsContentUri(uri))
        {
            var okContent = await WriteContentTagsAsync(uri, edit);
            // 通知宿主 UI 刷新当前歌曲显示（改元数据立即生效）
            if (okContent) AudioTagEvents.RaiseTagsWritten(uri, edit);
            return okContent;
        }
#endif
        var ok = await Task.Run(() => WriteFileTags(uri, edit));
        if (ok) AudioTagEvents.RaiseTagsWritten(uri, edit);
        return ok;
    }

    public Task<string?> WriteSidecarLyricsAsync(string uri, string lrcText)
    {
        if (string.IsNullOrWhiteSpace(uri)) return Task.FromResult<string?>(null);
#if ANDROID
        if (IsContentUri(uri)) return WriteContentSidecarLyricsAsync(uri, lrcText);
#endif
        return Task.Run(() => WriteFileSidecarLyrics(uri, lrcText));
    }

    public Task<bool> DeleteSidecarLyricsAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return Task.FromResult(false);
#if ANDROID
        if (IsContentUri(uri)) return Task.Run(() => DeleteContentSidecarLyrics(uri));
#endif
        return Task.Run(() => DeleteFileSidecarLyrics(uri));
    }

    public Task<string?> RenameFileAsync(string uri, string newName)
    {
        if (string.IsNullOrWhiteSpace(uri) || string.IsNullOrWhiteSpace(newName)) return Task.FromResult<string?>(null);
#if ANDROID
        if (IsContentUri(uri)) return Task.Run(() => RenameContentDocument(uri, newName));
#endif
        return Task.Run(() => RenameFile(uri, newName));
    }

    public Task<bool> DeleteFileAsync(string uri)
    {
        if (string.IsNullOrWhiteSpace(uri)) return Task.FromResult(false);
#if ANDROID
        if (IsContentUri(uri)) return Task.Run(() => DeleteContentDocument(uri));
#endif
        return Task.Run(() => DeleteFile(uri));
    }

    /// <summary>判断是否为 SAF content:// URI</summary>
    protected static bool IsContentUri(string path)
        => path.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

    /// <summary>按 Lyrico 风格拆分多个艺人名（" / "、"；"、"," 分隔），空输入返回空数组（= 清空）</summary>
    protected static string[] SplitArtists(string artists)
        => artists.Split(new[] { " / ", "/", ";", "；", "," }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    /// <summary>安全读取时长（毫秒），读取失败返回 0</summary>
    protected static long SafeDurationMs(TagLibFile file)
    {
        try { return (long)file.Properties.Duration.TotalMilliseconds; }
        catch { return 0; }
    }

    /// <summary>安全读取比特率（kbps）</summary>
    protected static int SafeBitrate(TagLibFile file)
    {
        try { return (int)file.Properties.AudioBitrate; }
        catch { return 0; }
    }

    /// <summary>安全读取采样率（Hz）</summary>
    protected static int SafeSampleRate(TagLibFile file)
    {
        try { return (int)file.Properties.AudioSampleRate; }
        catch { return 0; }
    }

    /// <summary>安全读取声道数</summary>
    protected static int SafeChannels(TagLibFile file)
    {
        try { return (int)file.Properties.AudioChannels; }
        catch { return 0; }
    }

    // ═══════════════ 自定义标签（ID3 TXXX） ═══════════════

    /// <summary>读取自定义标签（ID3v2 TXXX），非 ID3 格式返回空字典</summary>
    protected static Dictionary<string, string> ReadCustomTags(TagLibFile file)
    {
        var result = new Dictionary<string, string>();
        try
        {
            var id3 = file.GetTag(TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            if (id3 == null) return result;
            foreach (var frame in id3.GetFrames<UserTextInformationFrame>())
            {
                if (frame == null || string.IsNullOrEmpty(frame.Description)) continue;
                if (frame.Text is { Length: > 0 })
                    result[frame.Description] = string.Join(string.Empty, frame.Text);
            }
        }
        catch { }
        return result;
    }

    /// <summary>写入自定义标签（ID3v2 TXXX）：值为空字符串表示移除该键；仅对 ID3 格式生效</summary>
    protected static void WriteCustomTags(TagLibFile file, Dictionary<string, string>? tags)
    {
        if (tags == null || tags.Count == 0) return;
        try
        {
            var id3 = file.GetTag(TagTypes.Id3v2, true) as TagLib.Id3v2.Tag;
            if (id3 == null) return;
            foreach (var kv in tags)
            {
                var key = kv.Key;
                var value = kv.Value ?? string.Empty;
                var existing = id3.GetFrames<UserTextInformationFrame>()
                    .Where(f => string.Equals(f?.Description, key, StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (value.Length == 0)
                {
                    foreach (var f in existing) id3.RemoveFrame(f);
                    continue;
                }
                if (existing.Count > 0)
                {
                    existing[0].Text = new[] { value };
                }
                else
                {
                    var frame = UserTextInformationFrame.Get(id3, key, true);
                    frame.Text = new[] { value };
                }
            }
        }
        catch { }
    }

    /// <summary>读取歌词作者（ID3v2 TEXT 帧；非 ID3 格式返回空串）</summary>
    protected static string ReadLyricist(TagLibFile file)
    {
        try
        {
            var id3 = file.GetTag(TagTypes.Id3v2) as TagLib.Id3v2.Tag;
            if (id3 == null) return string.Empty;
            foreach (var f in id3.GetFrames<TextInformationFrame>())
            {
                if (f == null || string.IsNullOrEmpty(f.ToString())) continue;
                return f.ToString();
            }
            return string.Empty;
        }
        catch { return string.Empty; }
    }

    /// <summary>写入歌词作者（ID3v2 TEXT 帧；null 不改，空字符串清除；仅 ID3 格式生效）</summary>
    protected static void WriteLyricist(TagLibFile file, string? value)
    {
        if (value == null) return;
        try
        {
            var id3 = file.GetTag(TagTypes.Id3v2, true) as TagLib.Id3v2.Tag;
            if (id3 == null) return;
            var frame = TextInformationFrame.Get(id3, TagLib.ByteVector.FromString("TEXT", StringType.Latin1), true);
            if (frame == null) return;
            frame.Text = string.IsNullOrEmpty(value) ? new string[0] : new[] { value };
        }
        catch { }
    }

    // ═══════════════ 真实文件路径实现 ═══════════════

    private static AudioTagInfo? ReadFileTags(string path)
    {
        if (!IOFile.Exists(path)) return null;
        try
        {
            using var file = TagLibFile.Create(path);
            var tag = file.Tag;
            var fi = new FileInfo(path);
            return new AudioTagInfo
            {
                FilePath = path,
                Title = !string.IsNullOrWhiteSpace(tag.Title) ? tag.Title : Path.GetFileNameWithoutExtension(path),
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
                FileSize = fi.Length,
                Bitrate = SafeBitrate(file),
                SampleRate = SafeSampleRate(file),
                Channels = SafeChannels(file),
                Extension = Path.GetExtension(path),
                DisplayName = Path.GetFileName(path)
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool WriteFileTags(string path, AudioTagEdit edit)
    {
        if (!IOFile.Exists(path)) return false;
        try
        {
            using var file = TagLibFile.Create(path);
            var tag = file.Tag;
            if (edit.Title != null) tag.Title = edit.Title;
            if (edit.Artist != null) tag.Performers = SplitArtists(edit.Artist);
            if (edit.Album != null) tag.Album = edit.Album;
            if (edit.AlbumArtist != null) tag.AlbumArtists = SplitArtists(edit.AlbumArtist);
            if (edit.Year != null && uint.TryParse(edit.Year, out var year)) tag.Year = year;
            if (edit.Genre != null) tag.Genres = string.IsNullOrWhiteSpace(edit.Genre) ? Array.Empty<string>() : new[] { edit.Genre };
            if (edit.TrackNumber != null && uint.TryParse(edit.TrackNumber, out var track)) tag.Track = track;
            if (edit.DiscNumber != null && uint.TryParse(edit.DiscNumber, out var disc)) tag.Disc = disc;
            if (edit.Composer != null) tag.Composers = SplitArtists(edit.Composer);
            if (edit.Lyricist != null) WriteLyricist(file, edit.Lyricist);
            if (edit.Comment != null) tag.Comment = edit.Comment;
            if (edit.Copyright != null) tag.Copyright = edit.Copyright;
            WriteCustomTags(file, edit.CustomTags);
            if (edit.Lyrics != null) tag.Lyrics = edit.Lyrics;
            if (edit.Cover != null)
            {
                if (edit.Cover.Length == 0)
                    tag.Pictures = Array.Empty<TagLibPicture>();
                else
                    tag.Pictures = new TagLibPicture[]
                    {
                        new Picture(new ByteVector(edit.Cover))
                        {
                            Type = PictureType.FrontCover,
                            MimeType = "image/jpeg",
                            Description = "Cover"
                        }
                    };
            }
            file.Save();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? WriteFileSidecarLyrics(string path, string lrcText)
    {
        try
        {
            var lrcPath = Path.ChangeExtension(path, ".lrc");
            IOFile.WriteAllText(lrcPath, lrcText ?? string.Empty, new UTF8Encoding(false));
            return lrcPath;
        }
        catch { return null; }
    }

    private static bool DeleteFileSidecarLyrics(string path)
    {
        try
        {
            var lrcPath = Path.ChangeExtension(path, ".lrc");
            if (!IOFile.Exists(lrcPath)) return true;
            IOFile.Delete(lrcPath);
            return !IOFile.Exists(lrcPath);
        }
        catch { return false; }
    }

    private static string? RenameFile(string path, string newName)
    {
        try
        {
            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            var newPath = Path.Combine(dir, newName);
            if (string.Equals(path, newPath, StringComparison.OrdinalIgnoreCase)) return newPath;
            if (!IOFile.Exists(path)) return null;
            IOFile.Move(path, newPath);
            return newPath;
        }
        catch { return null; }
    }

    private static bool DeleteFile(string path)
    {
        try
        {
            if (!IOFile.Exists(path)) return true;
            IOFile.Delete(path);
            return !IOFile.Exists(path);
        }
        catch { return false; }
    }
}

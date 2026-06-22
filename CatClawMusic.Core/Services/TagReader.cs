using CatClawMusic.Core.Models;
using TagLib;
using IOFile = System.IO.File;
using TagLibFile = TagLib.File;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 音频标签读取器（使用 TagLibSharp）
/// </summary>
public class TagReader
{
    /// <summary>从 Content URI Stream 读取歌曲信息（SAF 路径用）</summary>
    public static Song? ReadFromStream(Stream stream, string uri, string displayName, long fileSize)
    {
        try
        {
            var abstraction = new ReadOnlyFileAbstraction(displayName, stream);
            using var file = TagLibFile.Create(abstraction);
            var props = file.Properties;
            var tag = file.Tag;

            return new Song
            {
                Title = !string.IsNullOrWhiteSpace(tag.Title)
                    ? tag.Title : Path.GetFileNameWithoutExtension(displayName),
                Artist = !string.IsNullOrWhiteSpace(tag.FirstPerformer)
                    ? string.Join(" / ", tag.Performers)
                    : tag.FirstAlbumArtist ?? "未知艺术家",
                Album = tag.Album ?? "未知专辑",
                AlbumId = 0,
                Duration = (int)props.Duration.TotalSeconds,
                FileSize = fileSize > 0 ? fileSize : stream.Length,
                Bitrate = props.AudioBitrate,
                Year = (int)(tag.Year != 0 ? tag.Year : 0),
                TrackNumber = (int)(tag.Track != 0 ? tag.Track : 0),
                Genre = !string.IsNullOrEmpty(tag.FirstGenre) ? tag.FirstGenre : null,
                FilePath = uri,
                Source = SongSource.Local
            };
        }
        catch
        {
            // m4a/mp4 回退：TagLibSharp 无法解析时手动解析 atom 树
            var ext = Path.GetExtension(displayName);
            if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (stream.CanSeek) stream.Position = 0;
                    var meta = M4aMetadataReader.ReadAllFromStream(stream);
                    if (meta != null)
                    {
                        return new Song
                        {
                            Title = !string.IsNullOrWhiteSpace(meta.Title)
                                ? meta.Title : Path.GetFileNameWithoutExtension(displayName),
                            Artist = !string.IsNullOrWhiteSpace(meta.Artist) ? meta.Artist : "未知艺术家",
                            Album = !string.IsNullOrWhiteSpace(meta.Album) ? meta.Album : "未知专辑",
                            AlbumId = 0,
                            Duration = meta.DurationSeconds,
                            FileSize = fileSize > 0 ? fileSize : stream.Length,
                            Bitrate = meta.Bitrate,
                            FilePath = uri,
                            Source = SongSource.Local
                        };
                    }
                }
                catch { }
            }
            return new Song
            {
                Title = Path.GetFileNameWithoutExtension(displayName),
                Artist = "未知艺术家",
                Album = "未知专辑",
                AlbumId = 0,
                FilePath = uri,
                FileSize = fileSize > 0 ? fileSize : stream.Length,
                Source = SongSource.Local
            };
        }
    }
    /// <summary>
    /// 从音频文件读取歌曲信息
    /// </summary>
    public static Song? ReadSongInfo(string filePath)
    {
        if (!IOFile.Exists(filePath)) return null;

        try
        {
            using var file = TagLib.File.Create(filePath);
            var props = file.Properties;
            var tag = file.Tag;
            var fileInfo = new FileInfo(filePath);

            var artist = !string.IsNullOrWhiteSpace(tag.FirstPerformer)
                ? string.Join(" / ", tag.Performers)
                : tag.FirstAlbumArtist ?? "未知艺术家";

            var song = new Song
            {
                Title = !string.IsNullOrWhiteSpace(tag.Title)
                    ? tag.Title
                    : Path.GetFileNameWithoutExtension(filePath),
                Artist = artist,
                Album = tag.Album ?? "未知专辑",
                Duration = (int)props.Duration.TotalSeconds,
                FileSize = fileInfo.Length,
                Bitrate = props.AudioBitrate,
                FilePath = filePath,
                LyricsPath = null, // 延迟到播放时查找，避免扫描时逐文件 I/O
                DateModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds()
            };

            return song;
        }
        catch
        {
            var fileInfo = new FileInfo(filePath);
            var ext = Path.GetExtension(filePath);
            // m4a/mp4 回退：手动解析 atom 树获取元数据和音频属性
            if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var meta = M4aMetadataReader.ReadAll(filePath);
                    if (meta != null)
                    {
                        return new Song
                        {
                            Title = !string.IsNullOrWhiteSpace(meta.Title)
                                ? meta.Title
                                : Path.GetFileNameWithoutExtension(filePath),
                            Artist = !string.IsNullOrWhiteSpace(meta.Artist) ? meta.Artist : "未知艺术家",
                            Album = !string.IsNullOrWhiteSpace(meta.Album) ? meta.Album : "未知专辑",
                            Duration = meta.DurationSeconds,
                            FileSize = fileInfo.Length,
                            Bitrate = meta.Bitrate,
                            FilePath = filePath,
                            LyricsPath = null,
                            DateModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds()
                        };
                    }
                }
                catch { }
            }
            return new Song
            {
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = "未知艺术家",
                Album = "未知专辑",
                FilePath = filePath,
                LyricsPath = null,
                FileSize = fileInfo.Length,
                DateModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds()
            };
        }
    }

    /// <summary>
    /// 提取专辑封面（返回字节数组，content:// URI 由调用方单独处理）
    /// </summary>
    public static byte[]? ExtractCoverArt(string filePath)
    {
        if (!IOFile.Exists(filePath)) return null;

        try
        {
            using var file = TagLib.File.Create(filePath);
            if (file.Tag.Pictures is { Length: > 0 })
            {
                return file.Tag.Pictures[0].Data.Data;
            }
        }
        catch
        {
            return ExtractCoverArtFallback(filePath);
        }

        return null;
    }

    /// <summary>
    /// 提取封面并保存为文件，返回文件路径
    /// </summary>
    public static string? ExtractCoverArtToFile(string filePath, string outputDirectory)
    {
        if (!IOFile.Exists(filePath)) return null;

        try
        {
            var coverBytes = ExtractCoverArt(filePath);
            if (coverBytes == null) return null;

            Directory.CreateDirectory(outputDirectory);
            var fileName = Path.GetFileNameWithoutExtension(filePath) + "_cover.jpg";
            var outputPath = Path.Combine(outputDirectory, fileName);

            IOFile.WriteAllBytes(outputPath, coverBytes);
            return outputPath;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 封面提取回退方案
    /// </summary>
    private static byte[]? ExtractCoverArtFallback(string filePath)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            if (file.Tag.Pictures is not { Length: > 0 }) return null;
            return file.Tag.Pictures[0].Data.Data;
        }
        catch
        {
            // m4a/mp4: TagLibSharp 对部分 m4a 文件解析失败，手动遍历 atom 树提取 covr
            var ext = Path.GetExtension(filePath);
            if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
            {
                try { return M4aMetadataReader.ExtractCoverArt(filePath); }
                catch { }
            }
            return null;
        }
    }

    /// <summary>
    /// 读取音频文件中的嵌入歌词
    /// </summary>
    public static string? ReadEmbeddedLyrics(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) return null;
        if (filePath.StartsWith("content://", StringComparison.OrdinalIgnoreCase)) return null;

        try
        {
            using var file = TagLib.File.Create(filePath);
            return ExtractLyricsFromFile(file);
        }
        catch (Exception ex)
        {
            // 部分 m4a 文件会让 TagLibSharp 抛出负长度异常，属于已知兼容性问题，不必刷屏
            if (ex is not ArgumentException)
                System.Diagnostics.Debug.WriteLine($"[TagReader] ReadEmbeddedLyrics FAILED for {filePath}: {ex.GetType().Name}: {ex.Message}");
            // m4a/mp4 回退：手动解析 atom 树
            var ext = Path.GetExtension(filePath);
            if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var lyrics = M4aMetadataReader.ExtractLyrics(filePath);
                    if (!string.IsNullOrWhiteSpace(lyrics)) return lyrics;
                }
                catch { }
            }
            return null;
        }
    }

    public static string? ReadEmbeddedLyricsFromStream(Stream stream, string fileName)
    {
        try
        {
            var abstraction = new ReadOnlyFileAbstraction(fileName, stream);
            using var file = TagLibFile.Create(abstraction);
            return ExtractLyricsFromFile(file);
        }
        catch (Exception ex)
        {
            if (ex is not ArgumentException)
                System.Diagnostics.Debug.WriteLine($"[TagReader] ReadEmbeddedLyricsFromStream FAILED: {ex.GetType().Name}: {ex.Message}");
            // m4a/mp4 回退
            var ext = Path.GetExtension(fileName);
            if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (stream.CanSeek) stream.Position = 0;
                    return M4aMetadataReader.ExtractLyricsFromStream(stream);
                }
                catch { }
            }
            return null;
        }
    }

    private static string? ExtractLyricsFromFile(TagLibFile file)
    {
        var lyrics = file.Tag.Lyrics;
        if (!string.IsNullOrWhiteSpace(lyrics)) return lyrics;

        var id3v2 = file.GetTag(TagTypes.Id3v2) as TagLib.Id3v2.Tag;
        if (id3v2 != null)
        {
            foreach (var frame in id3v2.GetFrames())
            {
                if (frame is TagLib.Id3v2.UnsynchronisedLyricsFrame uslt
                    && !string.IsNullOrWhiteSpace(uslt.Text))
                {
                    return uslt.Text;
                }
            }
        }

        if (file.GetTag(TagTypes.Xiph) is TagLib.Ogg.XiphComment xiph)
        {
            var fields = xiph.GetField("LYRICS");
            if (fields.Length > 0 && !string.IsNullOrWhiteSpace(fields[0]))
                return fields[0];
            fields = xiph.GetField("UNSYNCEDLYRICS");
            if (fields.Length > 0 && !string.IsNullOrWhiteSpace(fields[0]))
                return fields[0];
        }

        if (file.GetTag(TagTypes.Ape) is TagLib.Ape.Tag ape)
        {
            var item = ape.GetItem("LYRICS");
            var val = item?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
                return val;
            item = ape.GetItem("UNSYNCED LYRICS");
            val = item?.ToString();
            if (!string.IsNullOrWhiteSpace(val))
                return val;
        }

        return null;
    }

    /// <summary>
    /// 将歌词写入音频文件
    /// </summary>
    public static bool WriteEmbeddedLyrics(string filePath, string lyrics)
    {
        try
        {
            using var file = TagLib.File.Create(filePath);
            file.Tag.Lyrics = lyrics;
            file.Save();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TagReader] WriteMetadata failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 将元数据写入音频文件
    /// </summary>
    public static bool WriteMetadata(string filePath, string? title, string? artist, string? album, uint? year, uint? trackNumber, string? genre)
    {
        if (!IOFile.Exists(filePath)) return false;
        try
        {
            using var file = TagLib.File.Create(filePath);
            if (title != null) file.Tag.Title = title;
            if (artist != null) file.Tag.Performers = new[] { artist };
            if (album != null) file.Tag.Album = album;
            if (year.HasValue) file.Tag.Year = year.Value;
            if (trackNumber.HasValue) file.Tag.Track = trackNumber.Value;
            if (genre != null) file.Tag.Genres = new[] { genre };
            file.Save();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TagReader] WriteEmbeddedLyrics failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 将封面图写入音频文件
    /// </summary>
    public static bool WriteCoverToFile(string filePath, byte[] coverBytes)
    {
        if (!IOFile.Exists(filePath)) return false;
        try
        {
            using var file = TagLib.File.Create(filePath);
            var picture = new TagLib.Picture(new TagLib.ByteVector(coverBytes))
            {
                Type = TagLib.PictureType.FrontCover,
                MimeType = "image/jpeg",
                Description = "Cover"
            };
            file.Tag.Pictures = new[] { picture };
            file.Save();
            return true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TagReader] WriteCoverToFile failed: {ex}");
            return false;
        }
    }

    /// <summary>
    /// 从流中提取封面图片
    /// </summary>
    public static byte[]? ExtractCoverFromStream(Stream stream, string name)
    {
        try
        {
            var abstraction = new ReadOnlyFileAbstraction(name, stream);
            using var file = TagLibFile.Create(abstraction);
            if (file.Tag.Pictures is { Length: > 0 })
                return file.Tag.Pictures[0].Data.Data;
        }
        catch
        {
            // m4a/mp4 回退：手动解析 atom 树提取封面
            var ext = Path.GetExtension(name);
            if (ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".mp4", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".m4b", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    if (stream.CanSeek) stream.Position = 0;
                    return M4aMetadataReader.ExtractCoverFromStream(stream);
                }
                catch { }
            }
        }
        return null;
    }

    /// <summary>
    /// 批量扫描目录中的音频文件
    /// </summary>
    public static List<Song> ScanDirectory(string path, bool recursive = true)
    {
        var filePaths = recursive
            ? MusicUtility.ScanFolderRecursive(path)
            : MusicUtility.ScanFolder(path);

        var songs = new List<Song>();
        foreach (var filePath in filePaths)
        {
            var song = ReadSongInfo(filePath);
            if (song != null) songs.Add(song);
        }

        return songs;
    }
}

/// <summary>
/// TagLibSharp IFileAbstraction 的只读 Stream 实现（替代缺失的 StreamFileAbstraction）
/// </summary>
public class ReadOnlyFileAbstraction : TagLib.File.IFileAbstraction
{
    /// <summary>只读流</summary>
    private readonly Stream _stream;

    /// <summary>文件名</summary>
    public string Name { get; }
    /// <summary>读取流</summary>
    public Stream ReadStream => _stream;
    /// <summary>写入流（不支持）</summary>
    public Stream WriteStream => throw new NotSupportedException();

    /// <summary>
    /// 创建只读文件抽象
    /// </summary>
    public ReadOnlyFileAbstraction(string name, Stream stream)
    {
        Name = name;
        _stream = stream;
    }

    /// <summary>关闭流（由调用方管理生命周期）</summary>
    public void CloseStream(Stream stream)
    {
    }
}

/// <summary>
/// M4A/MP4 手动 atom 树解析器。
/// 当 TagLibSharp 无法解析某些 m4a 文件时，用此类手动遍历 MP4 atom 提取封面、歌词、音频属性和元数据。
/// </summary>
public static class M4aMetadataReader
{
    /// <summary>从 m4a 文件提取封面图字节数组</summary>
    public static byte[]? ExtractCoverArt(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ExtractCoverFromStream(fs);
    }

    /// <summary>从 m4a 流中提取封面图字节数组</summary>
    public static byte[]? ExtractCoverFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        return WalkTopLevel(br, stream.Length, wantCover: true).cover;
    }

    /// <summary>从 m4a 文件提取嵌入歌词</summary>
    public static string? ExtractLyrics(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ExtractLyricsFromStream(fs);
    }

    /// <summary>从 m4a 流中提取嵌入歌词</summary>
    public static string? ExtractLyricsFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        return WalkTopLevel(br, stream.Length, wantLyrics: true).lyrics;
    }

    /// <summary>从 m4a 文件读取所有可用元数据（标签 + 音频属性）</summary>
    public static M4aMetadata? ReadAll(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ReadAllFromStream(fs);
    }

    /// <summary>从 m4a 流读取所有可用元数据</summary>
    public static M4aMetadata? ReadAllFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var result = WalkTopLevel(br, stream.Length, wantCover: false, wantLyrics: true, wantProperties: true, wantTags: true);
        return new M4aMetadata
        {
            Title = result.title,
            Artist = result.artist,
            Album = result.album,
            Lyrics = result.lyrics,
            DurationSeconds = result.durationSeconds,
            Bitrate = result.bitrate,
            SampleRate = result.sampleRate,
            Channels = result.channels,
            BitDepth = result.bitDepth,
            Codec = result.codec
        };
    }

    /// <summary>从 m4a 流中仅读取音频属性</summary>
    public static M4aMetadata? ReadAudioProperties(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        var result = WalkTopLevel(br, stream.Length, wantProperties: true);
        return new M4aMetadata
        {
            DurationSeconds = result.durationSeconds,
            Bitrate = result.bitrate,
            SampleRate = result.sampleRate,
            Channels = result.channels,
            BitDepth = result.bitDepth,
            Codec = result.codec
        };
    }

    /// <summary>快速判断 m4a 文件是否为 ALAC 编码（扫描 moov/stsd 中的 alac 标记）</summary>
    public static bool IsAlac(string filePath)
    {
        try
        {
            if (!IOFile.Exists(filePath)) return false;
            using var fs = IOFile.OpenRead(filePath);
            return IsAlac(fs);
        }
        catch { return false; }
    }

    /// <summary>快速判断 m4a 流是否为 ALAC 编码</summary>
    public static bool IsAlac(Stream stream)
    {
        try
        {
            if (!stream.CanSeek) return false;
            var originalPos = stream.Position;
            try
            {
                stream.Position = 0;
                using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
                return WalkTopLevelForAlac(br, stream.Length);
            }
            finally { stream.Position = originalPos; }
        }
        catch { return false; }
    }

    private static bool WalkTopLevelForAlac(BinaryReader br, long streamLength)
    {
        long pos = 0;
        while (pos < streamLength - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && streamLength - pos >= 16)
                dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = streamLength - pos - 8;
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > streamLength || size < 8 || dataLen < 0) break;

            if (type == "moov")
                if (WalkMoovForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkMoovForAlacFlag(BinaryReader br, long start, long length)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "trak" && WalkTrakForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkTrakForAlacFlag(BinaryReader br, long start, long length)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "mdia" && WalkMdiaForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkMdiaForAlacFlag(BinaryReader br, long start, long length)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "minf" && WalkMinfForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkMinfForAlacFlag(BinaryReader br, long start, long length)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "stbl" && WalkStblForAlacFlag(br, pos + 8, dataLen)) return true;

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    private static bool WalkStblForAlacFlag(BinaryReader br, long start, long length)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            long atomEnd = pos + 8 + dataLen;
            if (atomEnd > end || size < 8 || dataLen < 0) break;

            if (type == "stsd" && dataLen > 16)
            {
                var stsdStart = pos + 8;
                var stsdDataStart = stsdStart + 8; // skip atom header + version/flags/entryCount
                if (stsdDataStart + 8 <= end)
                {
                    br.BaseStream.Position = stsdDataStart;
                    var entrySize = ReadUInt32BE(br);
                    var entryType = ReadFourCC(br);
                    if (entryType.Equals("alac", StringComparison.OrdinalIgnoreCase)) return true;

                    // 某些 ALAC 文件 stsd entry 类型为 mp4a，但其中包含 alac box
                    if (entrySize > 28 && stsdDataStart + entrySize <= end)
                    {
                        var entryEnd = stsdDataStart + entrySize;
                        var searchPos = stsdDataStart + 8 + 6 + 2 + 8 + 12; // 跳过常见音频 sample entry 字段
                        while (searchPos + 8 < entryEnd)
                        {
                            br.BaseStream.Position = searchPos;
                            var boxSize = ReadUInt32BE(br);
                            var boxType = ReadFourCC(br);
                            if (boxSize < 8 || searchPos + boxSize > entryEnd) break;
                            if (boxType.Equals("alac", StringComparison.OrdinalIgnoreCase)) return true;
                            searchPos += boxSize;
                        }
                    }
                }
            }

            pos = atomEnd;
            if (size == 0) break;
        }
        return false;
    }

    /// <summary>从 m4a 文件提取 ALAC 解码所需数据：magic cookie 和 mdat 音频数据</summary>
    public static (byte[]? magicCookie, byte[]? mdatData) ReadAlacData(string filePath)
    {
        using var fs = IOFile.OpenRead(filePath);
        return ReadAlacDataFromStream(fs);
    }

    /// <summary>从 m4a 流提取 ALAC 解码所需数据</summary>
    public static (byte[]? magicCookie, byte[]? mdatData) ReadAlacDataFromStream(Stream stream)
    {
        using var br = new BinaryReader(stream, System.Text.Encoding.UTF8, leaveOpen: true);
        byte[]? magicCookie = null;
        byte[]? mdatData = null;
        long streamLen = stream.Length;

        WalkTopLevelExtended(br, streamLen, ref magicCookie, ref mdatData);
        return (magicCookie, mdatData);
    }

    private static void WalkTopLevelExtended(BinaryReader br, long streamLen, ref byte[]? magicCookie, ref byte[]? mdatData)
    {
        long pos = 0;
        long end = streamLen;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "moov" && magicCookie == null)
            {
                WalkMoovForAlac(br, pos + 8, dataLen, ref magicCookie);
            }
            else if (type == "mdat" && mdatData == null)
            {
                if (pos + 8 + dataLen <= end)
                {
                    mdatData = new byte[dataLen];
                    br.BaseStream.Position = pos + 8;
                    br.Read(mdatData, 0, (int)dataLen);
                }
            }

            if (magicCookie != null && mdatData != null) break;
            pos += 8 + dataLen;
        }
    }

    private static void WalkMoovForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "trak")
                WalkTrakForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkTrakForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "mdia")
                WalkMdiaForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkMdiaForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "minf")
                WalkMinfForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkMinfForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "stbl")
                WalkStblForAlac(br, pos + 8, dataLen, ref magicCookie);

            pos += 8 + dataLen;
        }
    }

    private static void WalkStblForAlac(BinaryReader br, long start, long length, ref byte[]? magicCookie)
    {
        long pos = start;
        long end = start + length;
        while (pos < end - 7 && magicCookie == null)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "stsd" && dataLen > 16)
            {
                var stsdStart = pos + 8 + 8;
                if (stsdStart + 8 <= end)
                {
                    br.BaseStream.Position = stsdStart;
                    var entrySize = ReadUInt32BE(br);
                    var entryType = ReadFourCC(br);
                    if (entryType == "alac" && entrySize > 28)
                    {
                        var entryEnd = stsdStart + (int)entrySize;
                        var searchPos = stsdStart + 8 + 6 + 2 + 8 + 12;
                        while (searchPos + 8 < entryEnd && magicCookie == null)
                        {
                            br.BaseStream.Position = searchPos;
                            var boxSize = ReadUInt32BE(br);
                            var boxType = ReadFourCC(br);
                            if (searchPos + boxSize > entryEnd || boxSize < 8) break;
                            if (boxType == "alac" && boxSize > 8)
                            {
                                magicCookie = new byte[boxSize - 8];
                                br.Read(magicCookie, 0, (int)(boxSize - 8));
                            }
                            else
                            {
                                searchPos += (int)boxSize;
                            }
                        }
                    }
                }
            }

            pos += 8 + dataLen;
        }
    }

    // ──── 内部实现 ────

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album,
        int durationSeconds, int bitrate, int sampleRate, int channels, int bitDepth, string? codec)
        WalkTopLevel(BinaryReader br, long streamLength,
            bool wantCover = false, bool wantLyrics = false, bool wantProperties = false, bool wantTags = false)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = 0;
        while (pos < streamLength - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;

            if (size == 1 && streamLength - pos >= 16) // extended size
            {
                dataLen = (long)ReadUInt64BE(br) - 16;
            }
            if (size == 0) dataLen = streamLength - pos - 8; // rest of file
            if (pos + size > streamLength || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "moov":
                    var moovResult = WalkMoov(br, pos + 8, dataLen, wantCover, wantLyrics, wantProperties, wantTags);
                    cover ??= moovResult.cover;
                    lyrics ??= moovResult.lyrics;
                    title ??= moovResult.title;
                    artist ??= moovResult.artist;
                    album ??= moovResult.album;
                    if (moovResult.timescale > 0) timescale = moovResult.timescale;
                    if (moovResult.duration > 0) duration = moovResult.duration;
                    if (moovResult.sampleRate > 0) sampleRate = moovResult.sampleRate;
                    if (moovResult.channels > 0) channels = moovResult.channels;
                    if (moovResult.bitDepth > 0) bitDepth = moovResult.bitDepth;
                    codec ??= moovResult.codec;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        int durationSec = 0;
        int bitrateVal = 0;
        if (timescale > 0 && duration > 0)
        {
            durationSec = (int)(duration / timescale);
            if (durationSec > 0)
                bitrateVal = (int)(streamLength * 8 / durationSec / 1000);
        }

        return (cover, lyrics, title, artist, album, durationSec, bitrateVal, sampleRate, channels, bitDepth, codec);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album,
        long timescale, long duration, int sampleRate, int channels, int bitDepth, string? codec)
        WalkMoov(BinaryReader br, long start, long length,
            bool wantCover, bool wantLyrics, bool wantProperties, bool wantTags)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "mvhd":
                    if (wantProperties)
                    {
                        var mvhdStart = pos + 8;
                        var version = br.ReadByte();
                        if (version == 0)
                        {
                            br.BaseStream.Position = mvhdStart + 12; // skip version+flags+creationTime+modificationTime
                            timescale = ReadUInt32BE(br);
                            duration = ReadUInt32BE(br);
                        }
                        else if (version == 1)
                        {
                            br.BaseStream.Position = mvhdStart + 20; // skip version+flags+creationTime(8)+modificationTime(8)
                            timescale = ReadUInt32BE(br);
                            duration = (long)ReadUInt64BE(br);
                        }
                    }
                    break;

                case "trak":
                    var trakResult = WalkTrak(br, pos + 8, dataLen, wantProperties);
                    if (wantProperties && trakResult.timescale > 0)
                    {
                        timescale = trakResult.timescale;
                        duration = trakResult.duration;
                    }
                    if (trakResult.sampleRate > 0) sampleRate = trakResult.sampleRate;
                    if (trakResult.channels > 0) channels = trakResult.channels;
                    if (trakResult.bitDepth > 0) bitDepth = trakResult.bitDepth;
                    codec ??= trakResult.codec;
                    break;

                case "udta":
                    var udtaResult = WalkUdta(br, pos + 8, dataLen, wantCover, wantLyrics, wantTags);
                    cover ??= udtaResult.cover;
                    lyrics ??= udtaResult.lyrics;
                    title ??= udtaResult.title;
                    artist ??= udtaResult.artist;
                    album ??= udtaResult.album;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (cover, lyrics, title, artist, album, timescale, duration, sampleRate, channels, bitDepth, codec);
    }

    private static (long timescale, long duration, int sampleRate, int channels, int bitDepth, string? codec)
        WalkTrak(BinaryReader br, long start, long length, bool wantProperties)
    {
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "mdia":
                    var mdiaResult = WalkMdia(br, pos + 8, dataLen, wantProperties);
                    if (mdiaResult.timescale > 0) timescale = mdiaResult.timescale;
                    if (mdiaResult.duration > 0) duration = mdiaResult.duration;
                    if (mdiaResult.sampleRate > 0) sampleRate = mdiaResult.sampleRate;
                    if (mdiaResult.channels > 0) channels = mdiaResult.channels;
                    if (mdiaResult.bitDepth > 0) bitDepth = mdiaResult.bitDepth;
                    codec ??= mdiaResult.codec;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (timescale, duration, sampleRate, channels, bitDepth, codec);
    }

    private static (long timescale, long duration, int sampleRate, int channels, int bitDepth, string? codec)
        WalkMdia(BinaryReader br, long start, long length, bool wantProperties)
    {
        long timescale = 0, duration = 0;
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            switch (type)
            {
                case "mdhd":
                    if (wantProperties)
                    {
                        var mdhdStart = pos + 8;
                        var version = br.ReadByte();
                        if (version == 0)
                        {
                            br.BaseStream.Position = mdhdStart + 12;
                            timescale = ReadUInt32BE(br);
                            duration = ReadUInt32BE(br);
                        }
                        else if (version == 1)
                        {
                            br.BaseStream.Position = mdhdStart + 20;
                            timescale = ReadUInt32BE(br);
                            duration = (long)ReadUInt64BE(br);
                        }
                    }
                    break;

                case "minf":
                    var minfResult = WalkMinf(br, pos + 8, dataLen, wantProperties);
                    if (minfResult.sampleRate > 0) sampleRate = minfResult.sampleRate;
                    if (minfResult.channels > 0) channels = minfResult.channels;
                    if (minfResult.bitDepth > 0) bitDepth = minfResult.bitDepth;
                    codec ??= minfResult.codec;
                    break;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (timescale, duration, sampleRate, channels, bitDepth, codec);
    }

    private static (int sampleRate, int channels, int bitDepth, string? codec)
        WalkMinf(BinaryReader br, long start, long length, bool wantProperties)
    {
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "stbl")
            {
                var stblResult = WalkStbl(br, pos + 8, dataLen, wantProperties);
                if (stblResult.sampleRate > 0) sampleRate = stblResult.sampleRate;
                if (stblResult.channels > 0) channels = stblResult.channels;
                if (stblResult.bitDepth > 0) bitDepth = stblResult.bitDepth;
                codec ??= stblResult.codec;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (sampleRate, channels, bitDepth, codec);
    }

    private static (int sampleRate, int channels, int bitDepth, string? codec)
        WalkStbl(BinaryReader br, long start, long length, bool wantProperties)
    {
        int sampleRate = 0, channels = 0, bitDepth = 0;
        string? codec = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "stsd" && wantProperties)
            {
                // stsd: version(1) + flags(3) + entryCount(4) = 8 bytes header, then entries
                var stsdDataStart = pos + 8 + 8; // skip atom header + version/flags/entryCount
                if (stsdDataStart + 8 <= end)
                {
                    br.BaseStream.Position = stsdDataStart;
                    var entrySize = ReadUInt32BE(br);
                    var entryType = ReadFourCC(br);
                    codec = MapCodecName(entryType);

                    // Audio sample entry common fields:
                    // reserved(6) + dataRefIndex(2) + reserved2(8) + channels(2) + sampleSize(2) + compressionId(2) + packetSize(2) + sampleRate(4, 16.16 fixed)
                    var entryStart = stsdDataStart;
                    var audioFieldStart = entryStart + 8 + 6 + 2 + 8; // after reserved+dataRefIndex+reserved2
                    if (audioFieldStart + 12 <= entryStart + entrySize)
                    {
                        br.BaseStream.Position = audioFieldStart;
                        var ch = ReadUInt16BE(br);
                        var ss = ReadUInt16BE(br);
                        br.ReadBytes(4); // skip compressionId + packetSize
                        var srRaw = ReadUInt32BE(br);
                        channels = ch;
                        bitDepth = ss;
                        sampleRate = (int)(srRaw >> 16);
                    }
                }
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (sampleRate, channels, bitDepth, codec);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkUdta(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (size == 1 && end - pos >= 16) dataLen = (long)ReadUInt64BE(br) - 16;
            if (size == 0) dataLen = end - pos - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "meta" && (wantCover || wantLyrics || wantTags))
            {
                var metaResult = WalkMeta(br, pos + 8, dataLen, wantCover, wantLyrics, wantTags);
                cover ??= metaResult.cover;
                lyrics ??= metaResult.lyrics;
                title ??= metaResult.title;
                artist ??= metaResult.artist;
                album ??= metaResult.album;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (cover, lyrics, title, artist, album);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkMeta(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        // meta is a full atom: 4 bytes version/flags before children
        long childrenStart = start + 4;
        long end = start + length;

        // Detect if first 4 bytes are version/flags or a child atom
        // If version/flags, first byte should be 0 and next 3 bytes are flags (usually 0)
        br.BaseStream.Position = start;
        var b0 = br.ReadByte();
        var b1 = br.ReadByte();
        var b2 = br.ReadByte();
        var b3 = br.ReadByte();

        // Heuristic: if b0 == 0 and b1-b3 are small, it's likely version/flags
        if (b0 == 0 && b1 == 0 && b2 == 0 && (b3 == 0 || b3 == 1))
        {
            // version/flags detected, children start at start+4
        }
        else
        {
            // No version/flags, children start at beginning of meta data
            childrenStart = start;
        }

        return WalkIlstContainer(br, childrenStart, end - childrenStart, wantCover, wantLyrics, wantTags);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkIlstContainer(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var size = ReadUInt32BE(br);
            var type = ReadFourCC(br);
            long dataLen = size - 8;
            if (pos + size > end || size < 8) break;
            if (dataLen <= 0) { pos += size; continue; }

            if (type == "ilst")
            {
                var ilstResult = WalkIlst(br, pos + 8, dataLen, wantCover, wantLyrics, wantTags);
                cover ??= ilstResult.cover;
                lyrics ??= ilstResult.lyrics;
                title ??= ilstResult.title;
                artist ??= ilstResult.artist;
                album ??= ilstResult.album;
            }

            pos += 8 + dataLen;
            if (size == 0) break;
        }

        return (cover, lyrics, title, artist, album);
    }

    private static (byte[]? cover, string? lyrics, string? title, string? artist, string? album)
        WalkIlst(BinaryReader br, long start, long length, bool wantCover, bool wantLyrics, bool wantTags)
    {
        byte[]? cover = null;
        string? lyrics = null;
        string? title = null, artist = null, album = null;

        long pos = start;
        long end = start + length;
        while (pos < end - 7)
        {
            br.BaseStream.Position = pos;
            var itemSize = ReadUInt32BE(br);
            var itemType = ReadFourCC(br);
            long itemDataLen = itemSize - 8;
            if (pos + itemSize > end || itemSize < 8) break;
            if (itemDataLen <= 0) { pos += itemSize; continue; }

            bool needed = (wantCover && itemType == "covr") ||
                          (wantLyrics && itemType == "\u00a9lyr") ||
                          (wantTags && itemType == "\u00a9nam") ||
                          (wantTags && itemType == "\u00a9ART") ||
                          (wantTags && itemType == "\u00a9alb");

            if (needed)
            {
                // ilst item contains a 'data' atom: size(4) + 'data'(4) + typeIndicator(4) + locale(4) + actualData
                long dataAtomPos = pos + 8;
                br.BaseStream.Position = dataAtomPos;
                var dataAtomSize = ReadUInt32BE(br);
                var dataAtomType = ReadFourCC(br);

                if (dataAtomType == "data" && dataAtomSize > 16)
                {
                    br.ReadBytes(4); // type indicator (e.g. 13=UTF8, 14=JPEG, 13=PNG for covr)
                    br.ReadBytes(4); // locale
                    var actualDataLen = (int)(dataAtomSize - 16);
                    if (actualDataLen > 0 && actualDataLen <= itemDataLen - 8)
                    {
                        var data = br.ReadBytes(actualDataLen);
                        switch (itemType)
                        {
                            case "covr" when wantCover:
                                cover = data;
                                break;
                            case "\u00a9lyr" when wantLyrics:
                                lyrics = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                break;
                            case "\u00a9nam" when wantTags:
                                title = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                break;
                            case "\u00a9ART" when wantTags:
                                artist = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                break;
                            case "\u00a9alb" when wantTags:
                                album = System.Text.Encoding.UTF8.GetString(data).TrimEnd('\0');
                                break;
                        }
                    }
                }
            }

            pos += 8 + itemDataLen;
            if (itemSize == 0) break;
        }

        return (cover, lyrics, title, artist, album);
    }

    // ──── 辅助方法 ────

    private static uint ReadUInt32BE(BinaryReader br)
    {
        var bytes = br.ReadBytes(4);
        if (bytes.Length < 4) return 0;
        return (uint)((bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3]);
    }

    private static ulong ReadUInt64BE(BinaryReader br)
    {
        var bytes = br.ReadBytes(8);
        if (bytes.Length < 8) return 0;
        return ((ulong)bytes[0] << 56) | ((ulong)bytes[1] << 48) |
               ((ulong)bytes[2] << 40) | ((ulong)bytes[3] << 32) |
               ((ulong)bytes[4] << 24) | ((ulong)bytes[5] << 16) |
               ((ulong)bytes[6] << 8) | bytes[7];
    }

    private static ushort ReadUInt16BE(BinaryReader br)
    {
        var bytes = br.ReadBytes(2);
        if (bytes.Length < 2) return 0;
        return (ushort)((bytes[0] << 8) | bytes[1]);
    }

    private static string ReadFourCC(BinaryReader br)
    {
        var bytes = br.ReadBytes(4);
        if (bytes.Length < 4) return "";
        return System.Text.Encoding.ASCII.GetString(bytes);
    }

    private static string MapCodecName(string fourCC)
    {
        return fourCC switch
        {
            "mp4a" => "AAC",
            "alac" => "ALAC",
            "fLaC" => "FLAC",
            "Opus" => "Opus",
            "ac-3" => "AC-3",
            "ec-3" => "E-AC-3",
            _ => fourCC
        };
    }
}

/// <summary>M4A 手动解析结果</summary>
public class M4aMetadata
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Album { get; set; }
    public string? Lyrics { get; set; }
    public int DurationSeconds { get; set; }
    public int Bitrate { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitDepth { get; set; }
    public string? Codec { get; set; }
}

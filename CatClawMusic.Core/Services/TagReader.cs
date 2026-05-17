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
                    ? tag.FirstPerformer
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

        // 只在 try/catch 前计算一次，避免异常时重复文件系统查询
        var lyricsPath = MusicUtility.FindLyricsFile(filePath);

        try
        {
            using var file = TagLib.File.Create(filePath);
            var props = file.Properties;
            var tag = file.Tag;
            var fileInfo = new FileInfo(filePath);

            var artist = !string.IsNullOrWhiteSpace(tag.FirstPerformer)
                ? tag.FirstPerformer
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
                LyricsPath = lyricsPath,
                DateModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds()
            };

            return song;
        }
        catch
        {
            var fileInfo = new FileInfo(filePath);
            return new Song
            {
                Title = Path.GetFileNameWithoutExtension(filePath),
                Artist = "未知艺术家",
                Album = "未知专辑",
                FilePath = filePath,
                LyricsPath = lyricsPath,
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
            return null;
        }
    }

    /// <summary>
    /// 读取音频文件中的嵌入歌词
    /// </summary>
    public static string? ReadEmbeddedLyrics(string filePath)
    {
        if (!IOFile.Exists(filePath)) return null;

        try
        {
            using var file = TagLib.File.Create(filePath);
            var lyrics = file.Tag.Lyrics;
            return !string.IsNullOrWhiteSpace(lyrics) ? lyrics : null;
        }
        catch
        {
            return null;
        }
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
        catch { }
        return null;
    }

    /// <summary>
    /// 从流中读取嵌入歌词
    /// </summary>
    public static string? ReadEmbeddedLyricsFromStream(Stream stream, string name)
    {
        try
        {
            var abstraction = new ReadOnlyFileAbstraction(name, stream);
            using var file = TagLibFile.Create(abstraction);
            var lyrics = file.Tag.Lyrics;
            return !string.IsNullOrWhiteSpace(lyrics) ? lyrics : null;
        }
        catch { }
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

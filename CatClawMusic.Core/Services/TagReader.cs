using CatClawMusic.Core.Models;
using TagLib;
using IOFile = System.IO.File;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 音频标签读取器（使用 TagLibSharp，从方糖音乐播放器移植）
/// </summary>
public class TagReader
{
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
                LastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds()
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
                FileSize = fileInfo.Length,
                LastModified = new DateTimeOffset(fileInfo.LastWriteTimeUtc).ToUnixTimeSeconds()
            };
        }
    }

    /// <summary>
    /// 提取专辑封面（返回字节数组，适合 MAUI 的 ImageSource）
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

            var data = file.Tag.Pictures[0].Data.Data;
            var tempPath = Path.Combine(Path.GetTempPath(), "catclaw_temp_" + Guid.NewGuid() + ".jpg");

            IOFile.WriteAllBytes(tempPath, data);

            try
            {
                return data;
            }
            finally
            {
                try { IOFile.Delete(tempPath); } catch { }
            }
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
        catch
        {
            return false;
        }
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

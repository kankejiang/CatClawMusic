using System.Text.Json.Serialization;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 服务端歌曲 DTO —— 由 CatClawServerClient 反序列化 /api/songs、/api/search 返回，再映射为本地 Song。
/// </summary>
public class ServerSong
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("artist")]
    public string Artist { get; set; } = string.Empty;

    [JsonPropertyName("album")]
    public string Album { get; set; } = string.Empty;

    [JsonPropertyName("duration")]
    public int Duration { get; set; }

    [JsonPropertyName("file_path")]
    public string FilePath { get; set; } = string.Empty;

    [JsonPropertyName("file_size")]
    public long FileSize { get; set; }

    [JsonPropertyName("bitrate")]
    public int Bitrate { get; set; }

    [JsonPropertyName("track_number")]
    public int TrackNumber { get; set; }

    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    [JsonPropertyName("artist_id")]
    public long ArtistId { get; set; }

    [JsonPropertyName("album_id")]
    public long AlbumId { get; set; }

    [JsonPropertyName("cover_art_path")]
    public string? CoverArtPath { get; set; }

    [JsonPropertyName("date_modified")]
    public long DateModified { get; set; }

    [JsonPropertyName("lyrics_path")]
    public string? LyricsPath { get; set; }
}

using System.Text.Json;
using System.Text.Json.Serialization;

namespace CatClawMusic.Plugins.Lrclib;

/// <summary>
/// LRCLIB 开放歌词库客户端（https://lrclib.net/api）。
/// 免费、无需 API Key：按 歌名/艺人/时长 搜索同步歌词（syncedLyrics，LRC 格式）。
/// </summary>
public class LrclibApiClient
{
    private const string BaseUrl = "https://lrclib.net/api";

    private readonly HttpClient _http;

    public LrclibApiClient()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic/1.0 (lyrics provider plugin)");
    }

    /// <summary>
    /// 按歌名/艺人/时长搜索候选歌词。
    /// </summary>
    /// <param name="trackName">歌名</param>
    /// <param name="artistName">艺人（可空）</param>
    /// <param name="albumName">专辑（可空）</param>
    /// <param name="durationSeconds">歌曲时长（秒，可空，可空时为 0 不传）</param>
    public async Task<List<LrclibTrack>?> SearchAsync(
        string trackName, string? artistName = null, string? albumName = null, double durationSeconds = 0)
    {
        var query = new List<string> { $"track_name={Uri.EscapeDataString(trackName)}" };
        if (!string.IsNullOrWhiteSpace(artistName))
            query.Add($"artist_name={Uri.EscapeDataString(artistName)}");
        if (!string.IsNullOrWhiteSpace(albumName))
            query.Add($"album_name={Uri.EscapeDataString(albumName)}");
        if (durationSeconds > 0)
            query.Add($"duration={Math.Round(durationSeconds, 3).ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        try
        {
            using var doc = await GetJsonAsync($"{BaseUrl}/search?{string.Join("&", query)}");
            if (doc?.RootElement.ValueKind != JsonValueKind.Array) return null;
            return JsonSerializer.Deserialize<List<LrclibTrack>>(doc.RootElement.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 精确匹配（LRCLIB 返回 404 表示未收录）。
    /// </summary>
    public async Task<LrclibTrack?> GetAsync(
        string trackName, string? artistName = null, string? albumName = null, double durationSeconds = 0)
    {
        var query = new List<string> { $"track_name={Uri.EscapeDataString(trackName)}" };
        if (!string.IsNullOrWhiteSpace(artistName))
            query.Add($"artist_name={Uri.EscapeDataString(artistName)}");
        if (!string.IsNullOrWhiteSpace(albumName))
            query.Add($"album_name={Uri.EscapeDataString(albumName)}");
        if (durationSeconds > 0)
            query.Add($"duration={Math.Round(durationSeconds, 3).ToString(System.Globalization.CultureInfo.InvariantCulture)}");

        try
        {
            using var doc = await GetJsonAsync($"{BaseUrl}/get?{string.Join("&", query)}");
            if (doc?.RootElement.ValueKind != JsonValueKind.Object) return null;
            return JsonSerializer.Deserialize<LrclibTrack>(doc.RootElement.GetRawText());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>GET JSON；HTTP 非 2xx（如 404）返回 null 而不是抛异常</summary>
    private async Task<JsonDocument?> GetJsonAsync(string url)
    {
        using var resp = await _http.GetAsync(url);
        if (!resp.IsSuccessStatusCode) return null;
        var text = await resp.Content.ReadAsStringAsync();
        return string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
    }
}

/// <summary>LRCLIB 曲目记录（只保留插件需要的字段）</summary>
public class LrclibTrack
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("trackName")]
    public string TrackName { get; set; } = string.Empty;

    [JsonPropertyName("artistName")]
    public string ArtistName { get; set; } = string.Empty;

    [JsonPropertyName("albumName")]
    public string? AlbumName { get; set; }

    /// <summary>歌曲时长（秒）</summary>
    [JsonPropertyName("duration")]
    public double Duration { get; set; }

    [JsonPropertyName("instrumental")]
    public bool Instrumental { get; set; }

    /// <summary>纯文本歌词（无时间轴）</summary>
    [JsonPropertyName("plainLyrics")]
    public string? PlainLyrics { get; set; }

    /// <summary>同步歌词（LRC 格式，带时间轴）</summary>
    [JsonPropertyName("syncedLyrics")]
    public string? SyncedLyrics { get; set; }
}

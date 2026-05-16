using System.Text.Json;
using CatClawMusic.Core.Models;

namespace CatClawMusic.MusicTagPlugin;

/// <summary>
/// 表示 MusicBrainz 元数据匹配结果，包含从 MusicBrainz/AcoustID 获取的歌曲标签信息
/// </summary>
public class MetadataMatchResult
{
    /// <summary>
    /// 元数据来源，固定返回 "MusicBrainz"
    /// </summary>
    public string Source => "MusicBrainz";

    /// <summary>
    /// 歌曲标题
    /// </summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// 艺术家名称
    /// </summary>
    public string Artist { get; set; } = "";

    /// <summary>
    /// 专辑名称
    /// </summary>
    public string Album { get; set; } = "";

    /// <summary>
    /// 发行年份
    /// </summary>
    public uint? Year { get; set; }

    /// <summary>
    /// 音轨编号
    /// </summary>
    public uint? TrackNumber { get; set; }

    /// <summary>
    /// 音乐流派标签
    /// </summary>
    public string? Genre { get; set; }

    /// <summary>
    /// 匹配得分（0-100），分数越高匹配越精确
    /// </summary>
    public double MatchScore { get; set; }

    /// <summary>
    /// MusicBrainz 中的录音唯一标识符
    /// </summary>
    public string MusicBrainzId { get; set; } = "";
}

/// <summary>
/// MusicBrainz 元数据插件，通过 MusicBrainz 和 AcoustID API 搜索歌曲的标签信息（标题、艺术家、专辑、年份、音轨号、流派等）
/// </summary>
public class MusicBrainzMetadataPlugin
{
    /// <summary>
    /// HTTP 客户端，用于调用 MusicBrainz 和 AcoustID 的 Web API，超时时间为 10 秒
    /// </summary>
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// 初始化插件（当前无需额外初始化操作）
    /// </summary>
    /// <returns>已完成的任务</returns>
    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>
    /// 关闭插件并释放 HTTP 客户端资源
    /// </summary>
    /// <returns>已完成的任务</returns>
    public Task ShutdownAsync()
    {
        _client.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// 根据歌曲信息搜索元数据，优先尝试 MusicBrainz API，失败后回退到 AcoustID API
    /// </summary>
    /// <param name="song">需要搜索元数据的歌曲对象</param>
    /// <returns>按匹配得分降序排列的元数据匹配结果列表</returns>
    public async Task<List<MetadataMatchResult>> SearchMetadataAsync(Song song)
    {
        if (string.IsNullOrWhiteSpace(song.Title)) return new();

        var results = await TryMusicBrainzAsync(song);
        if (results.Count > 0) return results;

        results = await TryAcoustidAsync(song);
        return results;
    }

    /// <summary>
    /// 尝试通过 MusicBrainz API 搜索录音元数据
    /// </summary>
    /// <param name="song">需要搜索的歌曲对象</param>
    /// <returns>匹配结果列表，如果请求失败或未找到结果则返回空列表</returns>
    private async Task<List<MetadataMatchResult>> TryMusicBrainzAsync(Song song)
    {
        try
        {
            var title = Sanitize(song.Title);
            var artist = Sanitize(song.Artist);
            var query = title;
            if (!string.IsNullOrWhiteSpace(artist))
                query += $" AND artist:{artist}";

            var url = $"https://musicbrainz.org/ws/2/recording?query={Uri.EscapeDataString(query)}&limit=5&fmt=json";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.UserAgent.ParseAdd("CatClawMusic/1.0 (https://github.com/catclaw)");

            var response = await _client.SendAsync(request);
            if (!response.IsSuccessStatusCode) return new();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("recordings", out var recordings))
                return new();

            if (recordings.ValueKind != JsonValueKind.Array || recordings.GetArrayLength() == 0)
                return new();

            var results = new List<MetadataMatchResult>();
            foreach (var recording in recordings.EnumerateArray())
            {
                var result = ParseRecording(recording, song.Title ?? "", song.Artist ?? "");
                if (result != null)
                    results.Add(result);
            }

            results.Sort((a, b) => b.MatchScore.CompareTo(a.MatchScore));
            return results;
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// 尝试通过 AcoustID API 搜索录音元数据（作为 MusicBrainz 查询失败后的回退方案）
    /// </summary>
    /// <param name="song">需要搜索的歌曲对象</param>
    /// <returns>匹配结果列表（去重后最多返回 5 条），如果请求失败或未找到结果则返回空列表</returns>
    private async Task<List<MetadataMatchResult>> TryAcoustidAsync(Song song)
    {
        try
        {
            var title = Sanitize(song.Title);
            var artist = Sanitize(song.Artist);

            var url = "https://api.acoustid.org/v2/lookup"
                + $"?client=CatClawMusic&meta=recordings+releasegroups+tracks"
                + $"&title={Uri.EscapeDataString(title)}"
                + (!string.IsNullOrWhiteSpace(artist) ? $"&artist={Uri.EscapeDataString(artist)}" : "")
                + "&batch=1";

            var response = await _client.GetAsync(url);
            if (!response.IsSuccessStatusCode) return new();

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var acResults))
                return new();

            if (acResults.ValueKind != JsonValueKind.Array || acResults.GetArrayLength() == 0)
                return new();

            var results = new List<MetadataMatchResult>();
            foreach (var acResult in acResults.EnumerateArray())
            {
                if (!acResult.TryGetProperty("recordings", out var recordings)) continue;
                if (recordings.ValueKind != JsonValueKind.Array || recordings.GetArrayLength() == 0) continue;

                foreach (var recording in recordings.EnumerateArray())
                {
                    var result = ParseAcoustidRecording(recording, song.Title ?? "", song.Artist ?? "");
                    if (result != null && !results.Any(r => r.MusicBrainzId == result.MusicBrainzId))
                        results.Add(result);
                }

                if (results.Count >= 5) break;
            }

            results.Sort((a, b) => b.MatchScore.CompareTo(a.MatchScore));
            return results;
        }
        catch
        {
            return new();
        }
    }

    /// <summary>
    /// 解析 MusicBrainz API 返回的录音 JSON 元素，提取标题、艺术家、专辑、年份、音轨号、流派等信息
    /// </summary>
    /// <param name="recording">MusicBrainz 录音 JSON 元素</param>
    /// <param name="searchTitle">用于匹配计算的搜索标题</param>
    /// <param name="searchArtist">用于匹配计算的搜索艺术家</param>
    /// <returns>解析后的元数据匹配结果，如果必要信息缺失则返回 null</returns>
    private MetadataMatchResult? ParseRecording(JsonElement recording, string searchTitle, string searchArtist)
    {
        var result = new MetadataMatchResult();

        if (recording.TryGetProperty("id", out var idEl))
            result.MusicBrainzId = idEl.GetString() ?? "";

        var mbTitle = recording.TryGetProperty("title", out var titleEl) ? titleEl.GetString() : null;
        result.Title = mbTitle ?? "";

        if (recording.TryGetProperty("artist-credit", out var artistCredit) && artistCredit.ValueKind == JsonValueKind.Array)
        {
            var artists = new List<string>();
            foreach (var credit in artistCredit.EnumerateArray())
            {
                if (credit.TryGetProperty("artist", out var artistObj) && artistObj.TryGetProperty("name", out var nameEl))
                    artists.Add(nameEl.GetString() ?? "");
            }
            result.Artist = string.Join(", ", artists);
        }

        if (recording.TryGetProperty("release-list", out var releaseList) && releaseList.ValueKind == JsonValueKind.Array && releaseList.GetArrayLength() > 0)
        {
            var release = releaseList[0];
            if (release.TryGetProperty("title", out var albumEl))
                result.Album = albumEl.GetString() ?? "";

            if (release.TryGetProperty("date", out var dateEl))
            {
                var dateStr = dateEl.GetString();
                if (dateStr != null)
                {
                    var yearParts = dateStr.Split('-');
                    if (uint.TryParse(yearParts[0], out var year))
                        result.Year = year;
                }
            }

            if (release.TryGetProperty("medium-list", out var mediumList) && mediumList.ValueKind == JsonValueKind.Array && mediumList.GetArrayLength() > 0)
            {
                var medium = mediumList[0];
                if (medium.TryGetProperty("track-list", out var trackList) && trackList.ValueKind == JsonValueKind.Array)
                {
                    foreach (var track in trackList.EnumerateArray())
                    {
                        if (track.TryGetProperty("number", out var numEl) && uint.TryParse(numEl.GetString(), out var num))
                        {
                            result.TrackNumber = num;
                            break;
                        }
                        if (track.TryGetProperty("position", out var posEl) && uint.TryParse(posEl.GetString(), out var pos))
                        {
                            result.TrackNumber = pos;
                            break;
                        }
                    }
                }
            }
        }

        if (recording.TryGetProperty("tag-list", out var tagList) && tagList.ValueKind == JsonValueKind.Array && tagList.GetArrayLength() > 0)
        {
            var genres = new List<string>();
            foreach (var tag in tagList.EnumerateArray().Take(3))
            {
                if (tag.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                    genres.Add(nameEl.GetString()!);
            }
            if (genres.Count > 0)
                result.Genre = string.Join(", ", genres);
        }

        result.MatchScore = CalculateScore(result.Title, result.Artist, searchTitle, searchArtist);
        return result;
    }

    /// <summary>
    /// 解析 AcoustID API 返回的录音 JSON 元素，提取标题、艺术家、专辑等信息
    /// </summary>
    /// <param name="recording">AcoustID 录音 JSON 元素</param>
    /// <param name="searchTitle">用于匹配计算的搜索标题</param>
    /// <param name="searchArtist">用于匹配计算的搜索艺术家</param>
    /// <returns>解析后的元数据匹配结果</returns>
    private MetadataMatchResult? ParseAcoustidRecording(JsonElement recording, string searchTitle, string searchArtist)
    {
        var result = new MetadataMatchResult();

        if (recording.TryGetProperty("id", out var idEl))
            result.MusicBrainzId = idEl.GetString() ?? "";

        result.Title = recording.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";

        if (recording.TryGetProperty("artists", out var artistsEl) && artistsEl.ValueKind == JsonValueKind.Array && artistsEl.GetArrayLength() > 0)
        {
            var names = new List<string>();
            foreach (var a in artistsEl.EnumerateArray())
            {
                if (a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String)
                    names.Add(n.GetString()!);
            }
            result.Artist = string.Join(", ", names);
        }

        if (recording.TryGetProperty("releasegroups", out var rgEl) && rgEl.ValueKind == JsonValueKind.Array && rgEl.GetArrayLength() > 0)
        {
            var rg = rgEl[0];
            if (rg.TryGetProperty("title", out var albumT))
                result.Album = albumT.GetString() ?? "";
        }

        result.MatchScore = CalculateScore(result.Title, result.Artist, searchTitle, searchArtist);
        return result;
    }

    /// <summary>
    /// 计算匹配得分，基于标题和艺术家的字符串相似度进行评分
    /// </summary>
    /// <param name="matchTitle">匹配到的标题</param>
    /// <param name="matchArtist">匹配到的艺术家</param>
    /// <param name="searchTitle">搜索的标题</param>
    /// <param name="searchArtist">搜索的艺术家</param>
    /// <returns>0-100 之间的匹配得分</returns>
    private static double CalculateScore(string matchTitle, string matchArtist, string searchTitle, string searchArtist)
    {
        double score = 0;
        var mt = matchTitle.ToLowerInvariant();
        var st = searchTitle.ToLowerInvariant();
        var ma = matchArtist.ToLowerInvariant();
        var sa = searchArtist.ToLowerInvariant();

        if (string.IsNullOrEmpty(mt) || string.IsNullOrEmpty(st))
            return 0.1;

        if (mt.Contains(st) || st.Contains(mt))
            score += 50;
        else
        {
            var common = CommonChars(mt, st);
            score += common * 30 / Math.Max(mt.Length, st.Length);
        }

        if (!string.IsNullOrWhiteSpace(sa) && !string.IsNullOrWhiteSpace(ma))
        {
            if (ma.Contains(sa) || sa.Contains(ma))
                score += 40;
            else
            {
                var commonA = CommonChars(ma, sa);
                score += commonA * 20 / Math.Max(ma.Length, sa.Length);
            }
        }
        else if (!string.IsNullOrWhiteSpace(ma))
            score += 15;

        return Math.Min(score, 100);
    }

    /// <summary>
    /// 计算两个字符串中相同字符的数量（每个字符只计算一次）
    /// </summary>
    /// <param name="a">第一个字符串</param>
    /// <param name="b">第二个字符串</param>
    /// <returns>相同字符的数量</returns>
    private static int CommonChars(string a, string b)
    {
        var count = 0;
        var used = new bool[b.Length];
        for (int i = 0; i < a.Length; i++)
        {
            for (int j = 0; j < b.Length; j++)
            {
                if (!used[j] && a[i] == b[j])
                {
                    count++;
                    used[j] = true;
                    break;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// 清理输入字符串，将括号替换为空格并去除首尾空白
    /// </summary>
    /// <param name="input">需要清理的原始字符串</param>
    /// <returns>清理后的字符串，如果输入为空则返回原始输入</returns>
    private static string? Sanitize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        return input.Replace("(", " ").Replace(")", " ").Replace("[", " ").Replace("]", " ").Trim();
    }
}

using System.Text.Json.Serialization;

namespace CatClawMusic.Core.Models;

/// <summary>
/// 音乐源配置：把"平台接口"从硬编码抽成可配置 JSON（借鉴落雪音源生态的脚本化思路，
/// 但由 AI 自己维护——接口失效时 Yuki 可搜索最新音源源码 → 用 update_music_source
/// 工具更新本配置 → 自动验证 → 恢复能力，无需发版改代码）。
/// </summary>
public class MusicSourceConfig
{
    /// <summary>源唯一 ID（工具按 ID 查找，如 kuwo / netease）</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>平台显示名（用于返回消息）</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>是否启用（update_music_source 验证通过才置 true）</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>解析模板：kuwo_jsonp（酷我 r.s + mobi.s）/ netease_eapi（网易云 eapi）</summary>
    [JsonPropertyName("template")]
    public string Template { get; set; } = "";

    /// <summary>搜索接口：URL（{keyword} 占位）+ 查询参数 + 解码编码</summary>
    [JsonPropertyName("search")]
    public SourceHttpSpec Search { get; set; } = new();

    /// <summary>取直链接口：URL（{id}/{quality} 占位）+ 查询参数</summary>
    [JsonPropertyName("url_api")]
    public SourceHttpSpec UrlApi { get; set; } = new();

    /// <summary>音质映射：应用档位(flac/320k/128k) → 平台档位（如 2000kflac/320kmp3/128kmp3）</summary>
    [JsonPropertyName("quality_map")]
    public Dictionary<string, string> QualityMap { get; set; } = new();

    /// <summary>搜索条目解析正则（按模板要求提供）</summary>
    [JsonPropertyName("regexes")]
    public SourceRegexSpec Regexes { get; set; } = new();
}

/// <summary>HTTP 接口规格</summary>
public class SourceHttpSpec
{
    /// <summary>完整 URL（支持 {keyword}/{id}/{quality} 占位符）</summary>
    [JsonPropertyName("url")]
    public string Url { get; set; } = "";

    /// <summary>GET 查询参数（key → 值，值支持占位符）</summary>
    [JsonPropertyName("params")]
    public Dictionary<string, string> Params { get; set; } = new();

    /// <summary>响应编码：utf8 / gbk</summary>
    [JsonPropertyName("encoding")]
    public string Encoding { get; set; } = "utf8";

    /// <summary>额外请求头（key → 值）</summary>
    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; } = new();
}

/// <summary>搜索结果解析正则（块分割 + 字段提取）</summary>
public class SourceRegexSpec
{
    /// <summary>条目块分隔正则（按分隔符切出每首歌的片段）</summary>
    [JsonPropertyName("block_split")]
    public string BlockSplit { get; set; } = "";

    /// <summary>块内必需包含的标记（不含此标记的块跳过）</summary>
    [JsonPropertyName("block_marker")]
    public string BlockMarker { get; set; } = "";

    /// <summary>从块中提取歌曲 ID 的正则（捕获组 1）</summary>
    [JsonPropertyName("id_pattern")]
    public string IdPattern { get; set; } = "";

    /// <summary>从块中提取歌名（捕获组 1）</summary>
    [JsonPropertyName("name_pattern")]
    public string NamePattern { get; set; } = "";

    /// <summary>从块中提取歌手（捕获组 1）</summary>
    [JsonPropertyName("artist_pattern")]
    public string ArtistPattern { get; set; } = "";

    /// <summary>从块中提取可用音质标记（捕获组 1，如 FORMATS 字段）</summary>
    [JsonPropertyName("formats_pattern")]
    public string FormatsPattern { get; set; } = "";
}
/// <summary>解析出的搜索结果条目</summary>
public class SourceSong
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Artist { get; set; } = "";
    public string Formats { get; set; } = "";

    /// <summary>按 FORMATS 推断最高可用档位：含 flac 关键字→flac，含 320/MP3H→320k，否则 128k</summary>
    [JsonIgnore]
    public string BestQuality
    {
        get
        {
            if (Formats.Contains("FLAC", StringComparison.OrdinalIgnoreCase) || Formats.Contains("APE", StringComparison.OrdinalIgnoreCase))
                return "flac";
            if (Formats.Contains("MP3H", StringComparison.OrdinalIgnoreCase) || Formats.Contains("320", StringComparison.OrdinalIgnoreCase))
                return "320k";
            return "128k";
        }
    }
}

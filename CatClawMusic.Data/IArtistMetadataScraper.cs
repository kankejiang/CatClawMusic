namespace CatClawMusic.Data;

/// <summary>艺术家元数据刮削器统一接口</summary>
public interface IArtistMetadataScraper
{
    /// <summary>来源名称（如"网易云"、"MusicBrainz"）</summary>
    string SourceName { get; }

    /// <summary>搜索艺术家，返回多个匹配结果</summary>
    Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10);

    /// <summary>下载并保存艺术家封面到缓存，返回缓存路径</summary>
    Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName);
}

/// <summary>统一的艺术家搜索结果模型</summary>
public class ArtistSearchResult
{
    public string Source { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? CoverUrl { get; set; }
    public string? Alias { get; set; }
    public string? Description { get; set; }
    public string? ExtraInfo { get; set; }
}

namespace CatClawMusic.Data;

/// <summary>艺术家元数据刮削器统一接口</summary>
public interface IArtistMetadataScraper
{
    /// <summary>来源名称（如"网易云"、"AI 搜索"）</summary>
    string SourceName { get; }

    /// <summary>搜索艺术家，返回多个匹配结果</summary>
    Task<List<ArtistSearchResult>> SearchArtistsAsync(string name, int limit = 10);

    /// <summary>下载并保存艺术家封面到缓存，返回缓存路径</summary>
    Task<string?> DownloadAndCacheArtistCoverAsync(string coverUrl, string artistName);
}

/// <summary>统一的艺术家搜索结果模型</summary>
public class ArtistSearchResult
{
    /// <summary>数据来源名称（如"网易云"、"豆瓣"、"AI 搜索"）</summary>
    public string Source { get; set; } = "";
    /// <summary>来源内部 ID（如网易云艺术家 ID、豆瓣 ID 等）</summary>
    public string Id { get; set; } = "";
    /// <summary>艺术家名称</summary>
    public string Name { get; set; } = "";
    /// <summary>艺术家封面图片 URL</summary>
    public string? CoverUrl { get; set; }
    /// <summary>别名/外文名</summary>
    public string? Alias { get; set; }

    // 基础信息
    /// <summary>性别（男/女/组合）</summary>
    public string? Gender { get; set; }
    /// <summary>出生日期</summary>
    public string? Birthday { get; set; }
    /// <summary>国籍/地区</summary>
    public string? Region { get; set; }
    /// <summary>艺术家简介</summary>
    public string? Description { get; set; }

    // 扩展信息（百度百科等丰富来源）
    /// <summary>本名</summary>
    public string? RealName { get; set; }
    /// <summary>昵称</summary>
    public string? Nickname { get; set; }
    /// <summary>民族</summary>
    public string? Ethnicity { get; set; }
    /// <summary>出生地</summary>
    public string? BirthPlace { get; set; }
    /// <summary>毕业院校</summary>
    public string? Education { get; set; }
    /// <summary>星座</summary>
    public string? Zodiac { get; set; }
    /// <summary>身高</summary>
    public string? Height { get; set; }
    /// <summary>经纪公司</summary>
    public string? Agency { get; set; }
    /// <summary>代表作品</summary>
    public string? RepresentativeWorks { get; set; }
    /// <summary>职业</summary>
    public string? Occupation { get; set; }

    /// <summary>通用扩展字段（JSON 格式存储其他未分类信息）</summary>
    public string? ExtraInfo { get; set; }
}

namespace CatClawMusic.Plugins.Template;

/// <summary>
/// 示例：外部 API 客户端封装。
/// <para>
/// 约定（与宿主内网易云等插件的既有模式一致）：
/// <list type="number">
///   <item>统一设置 User-Agent（部分开放 API 要求可识别的 UA，便于对方联系维护者）与超时；</item>
///   <item>网络/解析失败一律静默返回 null，不要抛异常——宿主调用链会对每个插件做 try/catch，
///        但契约方法自己兜底能让"未命中"走正常返回路径；</item>
///   <item>注意 LRCLIB 等免费 API 有请求频率限制，高频调用前请加内存缓存。</item>
/// </list>
/// </para>
/// </summary>
public class TemplateApiClient
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public TemplateApiClient()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("CatClawMusic/1.0 (template plugin)");
    }

    /// <summary>按歌名/艺人抓取歌词文本（示例：请替换为真实 API 地址与参数）</summary>
    public async Task<string?> FetchLyricsAsync(string title, string artist)
    {
        try
        {
            var url = $"https://example.invalid/api/search?track_name={Uri.EscapeDataString(title)}" +
                      $"&artist_name={Uri.EscapeDataString(artist)}";
            return await _http.GetStringAsync(url);
        }
        catch
        {
            return null;
        }
    }
}

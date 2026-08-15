using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 音乐源进化工具：让 Yuki 自己维护平台接口配置。
/// 用法：模型发现下载工具报"接口失效"后，用 web_search + fetch_web_page 读最新
/// 音源源码（如 github 上的 lx 音源仓库）→ 分析新接口 → 用本工具提交新配置 →
/// 工具自动验证（搜索+取链测试）→ 验证通过才保存启用。
/// 流程完全闭环，平台改版无需发版。
/// </summary>
public class UpdateMusicSourceTool : IAgentTool
{
    public string Name => "update_music_source";
    public string Description =>
        "更新音乐源接口配置（酷我/网易云等），提交后自动验证可用性。当 kuwo_download / netease_download " +
        "返回'接口失效/配置无效'或下载失败时使用。先搜索最新音源源码（GitHub 上的 lx 音源仓库，" +
        "如 guoyue2010/lxmusic- 的音源 js、wangxanshen/lx-music-source），用 fetch_web_page 读取并分析新接口的 " +
        "URL、参数、响应解析规则，再用本工具提交。参数：id（kuwo/netease）、config_json（可选，完整配置 JSON，" +
        "省略则只诊断现有配置并返回各步骤结果帮助定位）。配置 JSON 结构：{\"id\":\"kuwo\",\"name\":\"酷我音乐\"," +
        "\"template\":\"kuwo_jsonp\",\"search\":{\"url\":\"...\",\"params\":{\"...\":\"...\"},\"encoding\":\"gbk\"}," +
        "\"url_api\":{\"url\":\"...\",\"params\":{\"rid\":\"{id}\",\"br\":\"{quality}\"},\"headers\":{}}," +
        "\"quality_map\":{\"flac\":\"2000kflac\",\"320k\":\"320kmp3\",\"128k\":\"128kmp3\"}," +
        "\"regexes\":{\"block_split\":\"\\{'AARTIST'\",\"block_marker\":\"'SONGNAME'\",\"id_pattern\":\"'MUSICRID':\\s*'([^']*)'\"," +
        "\"name_pattern\":\"'SONGNAME':\\s*'([^']*)'\",\"artist_pattern\":\"'ARTIST':\\s*'([^']*)'\"," +
        "\"formats_pattern\":\"'FORMATS':\\s*'([^']*)'\"}}。模板：kuwo_jsonp（酷我）、netease_eapi（网易云，" +
        "url_api.params 需含 eapi_path/eapi_key/level/encode_type/immerse_type/device_id）。";

    public ToolDefinition GetDefinition() => new()
    {
        Function = new ToolFunctionDef
        {
            Name = Name,
            Description = Description,
            Parameters = new ToolParameterDef
            {
                Properties = new Dictionary<string, ToolParameterProperty>
                {
                    ["id"] = new() { Type = "string", Description = "源 ID：kuwo 或 netease" },
                    ["config_json"] = new() { Type = "string", Description = "可选：完整配置 JSON（参考工具描述中的结构）。省略则只诊断现有配置" }
                },
                Required = new List<string> { "id" }
            }
        }
    };

    public async Task<string> ExecuteAsync(string arguments)
    {
        var id = ArgHelper.ExtractStringArgFallback(arguments, "id")?.Trim();
        var configJson = ArgHelper.ExtractStringArgFallback(arguments, "config_json")?.Trim();

        if (string.IsNullOrWhiteSpace(id))
            return JsonSerializer.Serialize(new { error = "请提供源 ID（kuwo / netease）" });

        // 1. 有配置则解析 + 合并
        if (!string.IsNullOrWhiteSpace(configJson))
        {
            MusicSourceConfig? cfg;
            try
            {
                cfg = JsonSerializer.Deserialize<MusicSourceConfig>(configJson);
            }
            catch (Exception ex)
            {
                return JsonSerializer.Serialize(new { error = $"config_json 不是合法 JSON: {ex.Message}", hint = "请确保 JSON 格式正确（注意转义正则中的反斜杠）" });
            }
            if (cfg == null || string.IsNullOrWhiteSpace(cfg.Id) || string.IsNullOrWhiteSpace(cfg.Template))
                return JsonSerializer.Serialize(new { error = "配置缺少 id 或 template 字段" });
            cfg.Id = id;
            if (string.IsNullOrWhiteSpace(cfg.Name)) cfg.Name = id;

            // 2. 验证
            var validation = await ValidateAsync(cfg);
            if (!validation.Ok)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "验证未通过，配置未保存。请根据以下诊断调整 config_json 后重试",
                    id = id,
                    diagnosis = validation
                });
            }

            // 3. 通过 → 保存启用
            cfg.Enabled = true;
            MusicSourceRegistry.Upsert(cfg);
            return JsonSerializer.Serialize(new
            {
                success = true,
                message = $"「{cfg.Name}」源配置验证通过并已保存（{validation.Songs} 首搜索结果，直链: {validation.UrlPreview}）",
                id = id,
                diagnosis = validation
            });
        }

        // 3. 诊断模式：只验证现有配置
        var existing = MusicSourceRegistry.GetAll().FirstOrDefault(s => s.Id == id);
        if (existing == null)
            return JsonSerializer.Serialize(new { error = $"源 {id} 不存在。请提交 config_json 创建新配置" });
        var diag = await ValidateAsync(existing);
        return JsonSerializer.Serialize(new
        {
            success = diag.Ok,
            message = diag.Ok ? "现有配置可用" : "现有配置已失效，请提交修复后的 config_json",
            id = id,
            diagnosis = diag
        });
    }

    /// <summary>验证配置：搜索测试歌 + 取直链 + 校验 URL 可访问。返回分步诊断。</summary>
    private static async Task<Diagnosis> ValidateAsync(MusicSourceConfig cfg)
    {
        var diag = new Diagnosis();
        try
        {
            var songs = await MusicSourceRegistry.SearchAsync(cfg, "七里香");
            diag.SearchOk = songs.Count > 0;
            diag.Songs = songs.Count;
            diag.FirstResult = songs.Count > 0 ? $"{songs[0].Name}-{songs[0].Artist}" : "";
            if (!diag.SearchOk)
            {
                diag.Error = "搜索无结果：URL/参数/解析正则可能不正确（或接口已彻底更换）";
                return diag;
            }

            var url = await MusicSourceRegistry.GetUrlAsync(cfg, songs[0], songs[0].BestQuality);
            diag.UrlOk = !string.IsNullOrEmpty(url);
            diag.UrlPreview = url is { Length: > 0 } ? (url.Length > 110 ? url[..110] + "..." : url) : "";
            if (!diag.UrlOk)
            {
                diag.Error = "取链失败：url_api 参数或响应解析可能不正确";
                return diag;
            }

            // 探测 URL 可访问性（只读 1KB）
            try
            {
                using var hc = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
                hc.Timeout = TimeSpan.FromSeconds(10);
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 1023);
                using var response = await hc.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                diag.HttpOk = response.IsSuccessStatusCode;
                diag.ContentType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!diag.HttpOk)
                    diag.Error = $"直链返回 HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex)
            {
                diag.HttpOk = false;
                diag.Error = $"直链访问失败: {ex.Message}";
            }
        }
        catch (Exception ex)
        {
            diag.Error = $"验证异常: {ex.Message}";
        }
        diag.Ok = diag.SearchOk && diag.UrlOk && diag.HttpOk;
        return diag;
    }

    private sealed class Diagnosis
    {
        public bool Ok { get; set; }
        public bool SearchOk { get; set; }
        public int Songs { get; set; }
        public string FirstResult { get; set; } = "";
        public bool UrlOk { get; set; }
        public string UrlPreview { get; set; } = "";
        public bool HttpOk { get; set; }
        public string ContentType { get; set; } = "";
        public string? Error { get; set; }
    }
}

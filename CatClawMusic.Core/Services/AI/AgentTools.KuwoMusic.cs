using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 酷我音乐下载工具：全部接口逻辑由 MusicSourceConfig 配置驱动（kuwo 源配置，
/// 可被 update_music_source 工具更新）——平台改版无需改代码。
/// 支持真无损 FLAC（mobi.s 2000kflac 直链，实测 25MB fLaC 验证）。
/// </summary>
public class KuwoMusicDownloadTool : IAgentTool
{
    public string Name => "kuwo_download";
    public string Description => "从酷我音乐搜索并下载歌曲（免费直连，支持无损 FLAC）。当用户要求'下载某首歌/某音乐/要无损音质'时优先使用本工具——酷我有真无损直链且无需会员。输入歌名（可附歌手名）。";
    public bool IsReadOnly => true;

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
                    ["keyword"] = new() { Type = "string", Description = "歌曲名称（必填），如：苏公堤" },
                    ["artist"] = new() { Type = "string", Description = "歌手名（可选），如：杨一歌。用于优先匹配目标歌手版本" }
                },
                Required = new List<string> { "keyword" }
            }
        }
    };

    public async Task<string> ExecuteAsync(string arguments)
    {
        var keyword = ArgHelper.ExtractStringArgFallback(arguments, "keyword")?.Trim();
        var artist = ArgHelper.ExtractStringArgFallback(arguments, "artist")?.Trim();

        if (string.IsNullOrWhiteSpace(keyword))
            return JsonSerializer.Serialize(new { error = "请提供歌曲名称" });

        var cfg = MusicSourceRegistry.Get("kuwo");
        if (cfg == null)
            return JsonSerializer.Serialize(new
            {
                error = "酷我音乐源配置无效或已禁用",
                hint = "可让 Yuki 用 update_music_source 工具修复（搜最新音源源码 → 更新配置 → 自动验证）"
            });

        try
        {
            var songs = await MusicSourceRegistry.SearchAsync(cfg, keyword);
            if (songs.Count == 0)
                return JsonSerializer.Serialize(new { error = $"酷我音乐上未找到《{keyword}》，可尝试 netease_download 或更换歌名" });

            // 歌手匹配优先，否则按音质档位排序
            var candidates = !string.IsNullOrWhiteSpace(artist)
                ? songs.OrderByDescending(s => s.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase)).ToList()
                : songs;

            // 并行取直链（按各曲最高可用档位，mobi.s 自动降级）
            var probes = await Task.WhenAll(candidates.Take(6).Select(async s =>
            {
                var quality = s.BestQuality;
                var url = await MusicSourceRegistry.GetUrlAsync(cfg, s, quality);
                return (s, quality, url);
            }));

            var usable = probes.Where(p => p.url != null).ToList();
            if (usable.Count == 0)
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = $"《{keyword}》在酷我音乐上未能获取到播放地址（接口可能已变更）",
                    hint = "可让 Yuki 用 update_music_source 工具检查并修复酷我源"
                });

            var best = usable[0];
            var filename = $"{best.s.Name}-{best.s.Artist}.{(best.quality == "flac" ? "flac" : "mp3")}";
            var enqueued = DownloadAgentBridge.EnqueueDownload != null
                ? DownloadAgentBridge.EnqueueDownload(best.url!, filename)
                : null;

            return JsonSerializer.Serialize(new
            {
                success = true,
                song = best.s.Name,
                artist = best.s.Artist,
                quality = best.quality == "flac" ? "FLAC无损" : (best.quality == "320k" ? "320K" : "128K"),
                version_note = !string.IsNullOrWhiteSpace(artist)
                    ? (best.s.Artist.Contains(artist, StringComparison.OrdinalIgnoreCase)
                        ? "匹配到目标歌手版本"
                        : $"未找到 {artist} 的版本，已选可下载的其他版本")
                    : "已选可下载的版本",
                download_started = enqueued != null,
                message = enqueued ?? "已找到直链（当前平台未接下载器）",
                direct_url = best.url,
                available_count = usable.Count
            });
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new
            {
                error = $"酷我下载失败: {ex.Message}",
                hint = "接口可能已变更，可让 Yuki 用 update_music_source 工具修复"
            });
        }
    }
}

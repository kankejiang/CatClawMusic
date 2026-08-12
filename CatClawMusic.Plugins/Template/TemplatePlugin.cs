using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Plugins.Template;

/// <summary>
/// 插件模板主类：示例实现 <see cref="ILyricsProviderPlugin"/>。
/// <para>
/// 选择示例契约的说明：宿主 LyricsService 的歌词兜底链已接线、全仓零实现，
/// 实现它装上即生效，无需宿主改动，是验证插件链路最快的方式。
/// 做音源/UI 类插件时改为实现 <see cref="IOnlineMusicPlugin"/>、
/// <see cref="IViewContributorPlugin"/> 等接口即可（全部扩展点见 README 速查表）。
/// </para>
/// <para>
/// 复制本工程后只需替换类名与方法实现，无需理解宿主加载细节：
/// 宿主 PluginManager 会扫描 .ccp 程序集内所有 <see cref="IPlugin"/> 实现
/// （第一个实例为主插件，其余为子插件），并自动归类到对应契约。
/// </para>
/// </summary>
public class TemplatePlugin : ILyricsProviderPlugin
{
    private readonly TemplateApiClient _client = new();

    /// <summary>插件唯一标识，全局唯一即可；宿主按 "{Category}.{PluginId}" 生成类型标识</summary>
    public string PluginId => "template";

    /// <summary>插件管理页显示名称</summary>
    public string Name => "模板插件";

    public string Version => "1.0.0";
    public string Author => "YourName";

    /// <summary>插件管理页描述</summary>
    public string Description => "示例插件：按歌名/艺人从某 API 在线匹配歌词";

    /// <summary>能力清单（search/play/lyrics/playlist/fm/daily 等，展示用，宿主目前不强制校验）</summary>
    public List<string> Capabilities => new() { "lyrics" };

    /// <summary>契约接口的可用性开关：宿主调用前先查此值</summary>
    public bool IsAvailable => true;

    /// <summary>初始化：加载配置、预热客户端等（宿主启动时调用一次）</summary>
    public Task InitializeAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>关闭：释放资源（应用退出/插件卸载时调用）</summary>
    public Task ShutdownAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 契约方法：按歌曲信息返回歌词。
    /// <para>约定：未命中或网络失败一律返回 null（不抛异常），宿主继续走其余兜底链。</para>
    /// </summary>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        if (song == null || string.IsNullOrWhiteSpace(song.Title)) return null;

        var raw = await _client.FetchLyricsAsync(song.Title, song.Artist);
        if (string.IsNullOrWhiteSpace(raw)) return null;

        return TemplateLrcParser.Parse(raw);
    }
}

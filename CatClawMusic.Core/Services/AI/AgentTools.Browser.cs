using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// Agent 浏览器桥接：Maui 层注入浏览器协调器（打开内置浏览器页 + 读取渲染后正文）。
/// 工具在 Core，浏览器在 Maui——用静态委托解耦（与 LyricsService 的注入委托同一模式）。
/// </summary>
public static class AgentBrowserBridge
{
    /// <summary>导航并提取正文的注入实现（由 Maui 层在启动时赋值）</summary>
    public static Func<string, CancellationToken, Task<string?>>? Navigator { get; set; }
}

/// <summary>
/// 浏览器控制工具：打开内置浏览器加载指定网页，等待 JS 渲染后读取页面正文返回。
/// 与 fetch_web_page 的区别：浏览器会执行页面 JS——动态渲染/需要 JS 的页面
/// （天气、实时数据、单页应用等）必须用本工具，fetch_web_page 只能拿原始 HTML。
/// </summary>
public class BrowserOpenTool : IAgentTool
{
    public string Name => "browser_open";
    public string Description => "打开内置浏览器加载指定网页，并读取页面渲染后的正文内容。当 fetch_web_page 无法获取内容（动态渲染页面、需要执行 JavaScript 的站点、天气/实时数据等）时使用此工具。输入要打开的 URL。";
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
                    ["url"] = new() { Type = "string", Description = "要打开的网页完整 URL（http/https）" }
                },
                Required = new List<string> { "url" }
            }
        }
    };

    public async Task<string> ExecuteAsync(string arguments)
    {
        var url = ArgHelper.ExtractStringArgFallback(arguments, "url")?.Trim();
        if (string.IsNullOrWhiteSpace(url))
            return JsonSerializer.Serialize(new { error = "请提供要打开的 URL" });
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return JsonSerializer.Serialize(new { error = "URL 必须以 http:// 或 https:// 开头" });

        if (AgentBrowserBridge.Navigator == null)
            return JsonSerializer.Serialize(new { error = "浏览器功能未初始化（当前平台不支持）" });

        try
        {
            var content = await AgentBrowserBridge.Navigator(url, CancellationToken.None);
            if (string.IsNullOrWhiteSpace(content))
                return JsonSerializer.Serialize(new { error = "页面加载完成但没有提取到正文（可能被阻止或需要交互）" });

            return JsonSerializer.Serialize(new { success = true, url = url, content = content });
        }
        catch (Exception ex)
        {
            Log.Debug("AgentTools", $"[Browser] 打开失败: {ex.Message}");
            return JsonSerializer.Serialize(new { error = $"浏览器打开失败: {ex.Message}" });
        }
    }
}

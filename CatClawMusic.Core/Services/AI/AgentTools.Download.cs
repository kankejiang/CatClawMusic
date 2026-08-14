using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// Agent 下载桥接：Maui 层注入下载管理器（复用应用内置的下载中心）。
/// 工具在 Core、下载器在 Maui——用静态委托解耦。
/// </summary>
public static class DownloadAgentBridge
{
    /// <summary>创建下载任务的注入实现（返回任务描述文本，如"已开始下载 xxx"）</summary>
    public static Func<string, string?, string>? EnqueueDownload { get; set; }
}

/// <summary>
/// 文件下载工具：让 Agent 帮用户下载文件（URL 来源可以是搜索结果、网页内链接等）。
/// 复用应用内置下载管理器——任务会出现在"下载管理"页，支持进度/暂停/通知。
/// </summary>
public class DownloadFileTool : IAgentTool
{
    public string Name => "download_file";
    public string Description => "下载文件到本地（如音乐、图片、文档、安装包等），下载任务会出现在应用的下载管理里，支持查看进度。当用户要求'下载 XX'或需要把某个文件的下载链接保存下来时使用此工具。输入下载链接 URL（可从搜索结果或网页内容中获取）。";

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
                    ["url"] = new() { Type = "string", Description = "文件的下载链接（http/https），可从搜索结果或网页内容中提取" },
                    ["filename"] = new() { Type = "string", Description = "可选：保存的文件名（含扩展名）；不填则取链接末段" }
                },
                Required = new List<string> { "url" }
            }
        }
    };

    public Task<string> ExecuteAsync(string arguments)
    {
        var url = ArgHelper.ExtractStringArgFallback(arguments, "url")?.Trim();
        var filename = ArgHelper.ExtractStringArgFallback(arguments, "filename")?.Trim();

        if (string.IsNullOrWhiteSpace(url))
            return Task.FromResult(JsonSerializer.Serialize(new { error = "请提供下载链接 URL" }));
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(JsonSerializer.Serialize(new { error = "下载链接必须以 http:// 或 https:// 开头" }));

        if (DownloadAgentBridge.EnqueueDownload == null)
            return Task.FromResult(JsonSerializer.Serialize(new { error = "下载功能未初始化（当前平台不支持）" }));

        var message = DownloadAgentBridge.EnqueueDownload(url, string.IsNullOrWhiteSpace(filename) ? null : filename);
        return Task.FromResult(JsonSerializer.Serialize(new
        {
            success = true,
            url = url,
            message = message
        }));
    }
}

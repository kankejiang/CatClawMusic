using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 搜索音乐库工具，按关键词（歌名、艺术家、专辑）检索本地与远程合并后的歌曲列表。
/// </summary>

internal static class ArgHelper
{
    /// <summary>
    /// 从 JSON 参数字符串中提取字符串参数
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串</param>
    /// <param name="key">参数键名</param>
    /// <returns>参数值；解析失败或不存在时返回 null</returns>
    internal static string? ExtractStringArgFallback(string arguments, string key)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
            return args?.TryGetValue(key, out var val) == true ? val.GetString() : null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 从 JSON 参数字符串中提取整数参数
    /// </summary>
    /// <param name="arguments">JSON 格式参数字符串</param>
    /// <param name="key">参数键名</param>
    /// <returns>参数值；解析失败或不存在时返回 0</returns>
    internal static int ExtractIntArgFallback(string arguments, string key)
    {
        try
        {
            var args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
            return args?.TryGetValue(key, out var val) == true ? val.GetInt32() : 0;
        }
        catch { return 0; }
    }
}

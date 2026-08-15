using System.Text.Json;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services.AI;

/// <summary>
/// 工具调度辅助：复刻 opencode 的工具执行管线（tool.ts / truncate.ts）——
/// 1) 执行前按参数 schema 校验必填项（opencode InvalidArgumentsError：缺参时
///    返回明确错误，引导模型"重写输入满足 schema"而非静默取默认值）；
/// 2) 结果统一截断（opencode truncate：行数+字节双限，头部保留 + 截断标记），
///    替代原先 1000 字符硬截断丢内容。
/// </summary>
public static class AgentToolDispatch
{
    /// <summary>工具结果最大字节数（约 6K 字符），对应 32K 上下文预算的安全余量</summary>
    private const int MaxResultBytes = 6000;

    /// <summary>工具结果最大行数（防单工具返回海量行撑爆上下文）</summary>
    private const int MaxResultLines = 200;

    /// <summary>
    /// 校验工具参数是否符合 schema。通过返回 null；不通过返回给 LLM 的错误 JSON。
    /// </summary>
    public static string? ValidateArguments(IAgentTool tool, string arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
            return JsonSerializer.Serialize(new
            {
                error = $"工具 {tool.Name} 缺少参数。请提供符合 {tool.Name} 参数定义的完整 JSON 参数后重新调用",
                expected = DescribeRequired(tool)
            });

        Dictionary<string, JsonElement>? args;
        try
        {
            args = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(arguments);
        }
        catch
        {
            return JsonSerializer.Serialize(new
            {
                error = $"工具 {tool.Name} 的参数不是合法 JSON，请重写参数使其为 JSON 对象",
                expected = DescribeRequired(tool)
            });
        }
        if (args == null)
            return JsonSerializer.Serialize(new
            {
                error = $"工具 {tool.Name} 的参数应为 JSON 对象，请重写参数",
                expected = DescribeRequired(tool)
            });

        var def = tool.GetDefinition();
        var missing = new List<string>();
        var wrongType = new List<string>();
        foreach (var requiredName in def.Function.Parameters.Required)
        {
            if (!args.TryGetValue(requiredName, out var value) || value.ValueKind == JsonValueKind.Null)
            {
                missing.Add(requiredName);
                continue;
            }
            if (def.Function.Parameters.Properties.TryGetValue(requiredName, out var prop))
            {
                if (prop.Type == "string" && value.ValueKind != JsonValueKind.String)
                    wrongType.Add($"{requiredName}(应为 string)");
                else if (prop.Type == "number" && value.ValueKind != JsonValueKind.Number)
                    wrongType.Add($"{requiredName}(应为 number)");
                else if (prop.Type == "boolean" && value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
                    wrongType.Add($"{requiredName}(应为 boolean)");
            }
        }

        if (missing.Count == 0 && wrongType.Count == 0)
            return null;

        return JsonSerializer.Serialize(new
        {
            error = $"工具 {tool.Name} 参数无效：缺少必填参数 [{string.Join(", ", missing)}]" +
                    (wrongType.Count > 0 ? $"，类型不符 [{string.Join(", ", wrongType)}]" : "") +
                    "。请按工具定义重写参数后重新调用",
            expected = DescribeRequired(tool)
        });
    }

    private static string DescribeRequired(IAgentTool tool)
    {
        var def = tool.GetDefinition();
        if (def.Function.Parameters.Required.Count == 0)
            return $"{tool.Name} 不需要参数（空对象 {{}}）";
        return $"{tool.Name} 必填参数: {string.Join(", ", def.Function.Parameters.Required)}";
    }

    /// <summary>
    /// 结果截断：行数+字节双限，保留头部 + 明确截断标记。
    /// 与 opencode truncate.ts 同思路（head 方向），超出时提示模型按需重取。
    /// </summary>
    public static string TruncateResult(string result)
    {
        if (result.Length <= MaxResultBytes)
            return result;

        var lines = result.Split('\n');
        var outLines = new List<string>(Math.Min(lines.Length, MaxResultLines));
        var bytes = 0;
        var hitLimit = false;
        foreach (var line in lines)
        {
            if (outLines.Count >= MaxResultLines)
            {
                hitLimit = true;
                break;
            }
            var lineBytes = System.Text.Encoding.UTF8.GetByteCount(line) + (outLines.Count > 0 ? 1 : 0);
            if (bytes + lineBytes > MaxResultBytes)
            {
                hitLimit = true;
                break;
            }
            outLines.Add(line);
            bytes += lineBytes;
        }

        if (!hitLimit && lines.Length <= MaxResultLines)
            return result;

        var removed = hitLimit
            ? $"约 {System.Text.Encoding.UTF8.GetByteCount(result) - bytes} 字节"
            : $"{lines.Length - outLines.Count} 行";
        return $"{string.Join("\n", outLines)}\n\n...(结果过长已截断，省略{removed}。如需完整内容请再调用本工具获取特定部分)...";
    }
}

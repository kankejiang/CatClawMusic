namespace CatClawMusic.Maui.Services;

/// <summary>
/// 插件兼容版本范围解析（PEP 440 子集），用于商店清单的 minAppVersion / appVersion 字段。
/// 支持子句：<c>&gt;=x</c>、<c>&lt;=x</c>、<c>&gt;x</c>、<c>&lt;x</c>、<c>~=x</c>、<c>==x</c>、<c>x</c>（精确），
/// 多个子句用逗号分隔（逻辑与）；空/空字符串表示不限制。
/// </summary>
public static class PluginVersionRange
{
    /// <summary>判断宿主版本是否满足范围描述。</summary>
    /// <param name="range">范围描述，如 "&gt;=1.7.10"、"&gt;=1.6,&lt;2"、"~=1.7"；空则始终满足</param>
    /// <param name="hostVersion">宿主应用版本，如 "1.7.10"</param>
    public static bool IsSatisfied(string? range, string hostVersion)
    {
        if (string.IsNullOrWhiteSpace(range)) return true;
        if (!TryParseVersion(hostVersion, out var host)) return true; // 宿主版本不可解析 → 不阻断

        foreach (var clause in range.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (clause.Length == 0) continue;
            if (!EvaluateClause(clause, host)) return false;
        }
        return true;
    }

    /// <summary>返回人类可读的范围描述（用于兼容性提示），解析失败返回原文。</summary>
    public static string Describe(string? range)
    {
        if (string.IsNullOrWhiteSpace(range)) return "不限";
        return range;
    }

    private static bool EvaluateClause(string clause, Version host)
    {
        clause = clause.Trim();
        if (clause.StartsWith(">=", StringComparison.Ordinal))
            return TryParseVersion(clause[2..].Trim(), out var v) && host >= v;
        if (clause.StartsWith("<=", StringComparison.Ordinal))
            return TryParseVersion(clause[2..].Trim(), out var v) && host <= v;
        if (clause.StartsWith("==", StringComparison.Ordinal))
            return TryParseVersion(clause[2..].Trim(), out var v) && host == v;
        if (clause.StartsWith("~=", StringComparison.Ordinal))
            return EvaluateCompatibleRelease(clause[2..].Trim(), host);
        if (clause.StartsWith(">", StringComparison.Ordinal))
            return TryParseVersion(clause[1..].Trim(), out var v) && host > v;
        if (clause.StartsWith("<", StringComparison.Ordinal))
            return TryParseVersion(clause[1..].Trim(), out var v) && host < v;
        // 无操作符 → 精确版本
        return TryParseVersion(clause, out var exact) && host == exact;
    }

    /// <summary>~=x.y 语义：>= x.y 且 < x.(y+1)（兼容发布）。</summary>
    private static bool EvaluateCompatibleRelease(string versionText, Version host)
    {
        if (!TryParseVersion(versionText, out var v)) return false;
        if (host < v) return false;
        // 取主版本号一致的下一小版本作为上界
        var upper = new Version(v.Major, v.Minor + 1, 0);
        return host < upper;
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text)) return false;
        text = text.Trim().TrimStart('v', 'V');
        return Version.TryParse(text, out version);
    }
}

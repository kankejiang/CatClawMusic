namespace CatClawMusic.Core.Services;

/// <summary>
/// 音乐工具类
/// </summary>
public static class MusicUtility
{
    /// <summary>
    /// 已知乐队/组合名称（包含分隔符但不应被拆分的艺术家名）。
    /// 注意：此列表使用 OrdinalIgnoreCase 比较，大小写不敏感。
    /// </summary>
    private static readonly HashSet<string> KnownBandNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "AC/DC",
        "Earth, Wind & Fire",
        "Earth, Wind and Fire",
        "Simon & Garfunkel",
        "Crosby, Stills, Nash & Young",
        "Crosby, Stills & Nash",
        "Emerson, Lake & Palmer",
        "Mumford & Sons",
        "Guns N' Roses",
        "Fleetwood Mac",
        "The White Stripes",
        "Twenty One Pilots",
        "Stone Temple Pilots",
        "Stone Sour",
        "Florence + The Machine",
        "Death Cab for Cutie",
        "Panic! At The Disco",
        "Tears for Fears",
        "Echo & The Bunnymen",
    };
    /// <summary>支持的音频文件扩展名列表</summary>
    private static readonly string[] AudioExtensions =
    {
        ".mp3", ".flac", ".ogg", ".oga", ".opus", ".m4a", ".mp4", ".aac", ".wma",
        ".wav", ".aiff", ".aifc", ".ape", ".wv", ".tta", ".mka", ".dsf", ".dff",
        ".mid", ".midi", ".rmi", ".spx", ".amr", ".3gp", ".mkv", ".webm"
    };
    /// <summary>音频扩展名哈希集合（大写形式，用于 O(1) 查找）</summary>
    private static readonly HashSet<string> AudioExtensionSet = new(
        AudioExtensions.Select(e => e.ToUpperInvariant()), StringComparer.Ordinal);

    /// <summary>
    /// 秒转时间格式
    /// </summary>
    public static string SecToHms(double duration)
    {
        var ts = new TimeSpan(0, 0, Convert.ToInt32(duration));
        if (ts.Hours > 0)
            return $"{ts.Hours:00}:{ts.Minutes:00}:{ts.Seconds:00}";
        if (ts.Minutes > 0)
            return $"00:{ts.Minutes:00}:{ts.Seconds:00}";
        return $"00:00:{ts.Seconds:00}";
    }

    /// <summary>
    /// 秒转 mm:ss 格式（用于播放时间显示）
    /// </summary>
    public static string SecToMinSec(double duration)
    {
        var ts = TimeSpan.FromSeconds(duration);
        if (ts.Hours > 0)
            return $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    /// <summary>
    /// 截取两个字符串之间的内容
    /// </summary>
    public static string Substring(string source, string startStr, string endStr)
    {
        var startIndex = source.IndexOf(startStr, StringComparison.Ordinal);
        if (startIndex == -1) return string.Empty;

        var tmpStr = source.Substring(startIndex + startStr.Length);
        var endIndex = tmpStr.IndexOf(endStr, StringComparison.Ordinal);
        if (endIndex == -1) return string.Empty;

        return tmpStr.Substring(0, endIndex);
    }

    /// <summary>
    /// 单文件夹扫描音频文件（一次遍历替代 6 次目录扫描）
    /// </summary>
    public static List<string> ScanFolder(string folderPath)
    {
        var results = new List<string>();
        if (!Directory.Exists(folderPath)) return results;

        try
        {
            foreach (var file in Directory.EnumerateFiles(folderPath))
            {
                if (AudioExtensionSet.Contains(Path.GetExtension(file).ToUpperInvariant()))
                    results.Add(file);
            }
        }
        catch
        {
            // 忽略权限错误
        }

        return results;
    }

    /// <summary>
    /// 递归扫描目录下所有音频文件
    /// </summary>
    public static List<string> ScanFolderRecursive(string rootPath)
    {
        var results = new System.Collections.Concurrent.ConcurrentBag<string>();
        if (!Directory.Exists(rootPath)) return new List<string>();

        ScanDirectoryRecursive(new DirectoryInfo(rootPath), results);
        return results.ToList();
    }

    /// <summary>
    /// 在同目录下查找与音频文件同名的 .lrc 歌词文件
    /// </summary>
    public static string? FindLyricsFile(string audioFilePath)
    {
        if (string.IsNullOrEmpty(audioFilePath)) return null;
        var dir = Path.GetDirectoryName(audioFilePath);
        var name = Path.GetFileNameWithoutExtension(audioFilePath);
        if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name)) return null;

        // 精确匹配 song.lrc
        var exactLrc = Path.Combine(dir, name + ".lrc");
        if (File.Exists(exactLrc)) return exactLrc;

        // 精确匹配 song.ttml
        var exactTtml = Path.Combine(dir, name + ".ttml");
        if (File.Exists(exactTtml)) return exactTtml;

        // 精确匹配 song.xml（可能是 TTML）
        var exactXml = Path.Combine(dir, name + ".xml");
        if (File.Exists(exactXml)) return exactXml;

        // 模糊匹配 song*.lrc
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.lrc"))
            {
                var lrcName = Path.GetFileNameWithoutExtension(f);
                if (lrcName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
        }
        catch { }

        // 模糊匹配 song*.ttml
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.ttml"))
            {
                var ttmlName = Path.GetFileNameWithoutExtension(f);
                if (ttmlName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
        }
        catch { }

        // 模糊匹配 song*.xml（可能是 TTML）
        try
        {
            foreach (var f in Directory.GetFiles(dir, "*.xml"))
            {
                var xmlName = Path.GetFileNameWithoutExtension(f);
                if (xmlName.StartsWith(name, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
        }
        catch { }

        return null;
    }

    /// <summary>
    /// 将多值艺术家字符串拆分为独立艺术家名列表。
    /// <para>
    /// 支持的分隔符：
    /// <list type="bullet">
    ///   <item>中文顿号 "、"（如 "张学友、刘德华"）</item>
    ///   <item>分号 "；" 或 ";"（如 "Adele;Ed Sheeran"）</item>
    ///   <item>斜杠 "/" 或 " / "（如 "周杰伦/林俊杰"）</item>
    ///   <item>特征标记 " feat. " / " ft. "（如 "Artist feat. Guest"）</item>
    ///   <item>逗号 ", "（如 "Artist1, Artist2"）</item>
    ///   <item>" &amp; " 带空格（可能为协作，谨慎拆分）</item>
    /// </list>
    /// </para>
    /// <para>
    /// 防误拆分规则：
    /// <list type="bullet">
    ///   <item>已知乐队名（如 "AC/DC", "Earth, Wind &amp; Fire" 等）不拆分</item>
    ///   <item>"/" 分隔时，若某部分 ≤2 个字符则不拆分（避免拆分 "AC/DC"）</item>
    ///   <item>"&amp;" 分隔时，额外检查是否为已知乐队</item>
    /// </list>
    /// </para>
    /// </summary>
    /// <param name="rawArtist">原始艺术家字符串，可能包含多个艺术家名</param>
    /// <returns>独立艺术家名列表。若输入为 null/空白则返回空列表。</returns>
    public static List<string> SplitArtistNames(string? rawArtist)
    {
        if (string.IsNullOrWhiteSpace(rawArtist))
            return new List<string>();

        var trimmed = rawArtist.Trim();

        // 规范化：将全角分隔符替换为半角，统一处理
        trimmed = trimmed.Replace('／', '/').Replace('＆', '&').Replace('，', ',');

        // 单字符或空字符串不做拆分
        if (trimmed.Length <= 1)
            return new List<string> { trimmed };

        // 已知乐队名直接返回
        if (KnownBandNames.Contains(trimmed))
            return new List<string> { trimmed };

        // 1. 中文顿号 "、" — 总是分隔符
        if (trimmed.Contains('、'))
        {
            return trimmed.Split('、', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(part => SplitArtistNames(part))
                .ToList();
        }

        // 2. 中文/英文分号
        if (trimmed.Contains('；') || trimmed.Contains(';'))
        {
            return trimmed.Split(new[] { '；', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .SelectMany(part => SplitArtistNames(part))
                .ToList();
        }

        // 3. " feat. " / " ft. " 特征标记（前后带空格）
        foreach (var sep in new[] { " feat. ", " Feat. ", " FEAT. ", " ft. ", " Ft. ", " FT. " })
        {
            var idx = trimmed.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0 && idx + sep.Length < trimmed.Length)
            {
                var main = trimmed[..idx].Trim();
                var featured = trimmed[(idx + sep.Length)..].Trim();
                var result = new List<string>();
                if (!string.IsNullOrEmpty(main)) result.AddRange(SplitArtistNames(main));
                if (!string.IsNullOrEmpty(featured)) result.AddRange(SplitArtistNames(featured));
                if (result.Count > 0) return result;
            }
        }

        // 4. " / " 带空格的斜杠分隔
        if (trimmed.Contains(" / "))
        {
            var parts = trimmed.Split(new[] { " / " }, StringSplitOptions.None);
            return parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        }

        // 5. "/" 无空格分隔 — 如果某部分只有1-2个字符则不拆分（保护 "AC/DC" 等）
        // 但如果包含非ASCII字符（如中文），2字名称也是合法艺名，应拆分
        if (trimmed.Contains('/') && !KnownBandNames.Contains(trimmed))
        {
            var parts = trimmed.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            bool hasNonAscii = parts.Any(p => p.Any(c => c > 127));
            if (hasNonAscii || parts.All(p => p.Length >= 3))
                return parts.ToList();
        }

        // 6. " & " 带空格 — 谨慎处理：仅当不是已知乐队 且 各部分≥3字符
        // 如果包含非ASCII字符（如中文），2字名称也合法
        if (trimmed.Contains(" & ") && !KnownBandNames.Contains(trimmed))
        {
            var parts = trimmed.Split(new[] { " & " }, StringSplitOptions.None);
            var trimmedParts = parts.Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            if (trimmedParts.Length > 1)
            {
                bool hasNonAscii = trimmedParts.Any(p => p.Any(c => c > 127));
                if (hasNonAscii || trimmedParts.All(p => p.Length >= 3))
                    return trimmedParts.ToList();
            }
        }

        // 7. ", " 逗号分隔（不拆分已知乐队如 "Earth, Wind & Fire"）
        if (trimmed.Contains(", ") && !KnownBandNames.Contains(trimmed))
        {
            var parts = trimmed.Split(new[] { ", " }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length > 1)
                return parts.ToList();
        }

        // 无已知分隔符，返回原值
        return new List<string> { trimmed };
    }

    /// <summary>
    /// 递归扫描目录和子目录下的音频文件，子目录并行遍历以加速深层目录树扫描
    /// </summary>
    private static void ScanDirectoryRecursive(DirectoryInfo dir, System.Collections.Concurrent.ConcurrentBag<string> results)
    {
        if (!dir.Exists) return;

        try
        {
            foreach (var file in dir.EnumerateFiles())
            {
                if (AudioExtensionSet.Contains(file.Extension.ToUpperInvariant()))
                    results.Add(file.FullName);
            }

            var subDirs = dir.EnumerateDirectories().ToList();
            if (subDirs.Count > 0)
            {
                Parallel.ForEach(subDirs, new ParallelOptions { MaxDegreeOfParallelism = 4 }, subDir =>
                {
                    ScanDirectoryRecursive(subDir, results);
                });
            }
        }
        catch
        {
            // 忽略权限错误
        }
    }
}

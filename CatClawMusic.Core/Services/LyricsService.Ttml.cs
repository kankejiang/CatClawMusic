using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>歌词服务 —— partial 分域文件。</summary>
public partial class LyricsService
{
    public async Task<LrcLyrics?> ParseTtmlAsync(string ttmlContent)
    {
        return await Task.Run(() => ParseTtml(ttmlContent));
    }

    /// <summary>
    /// 解析 TTML 格式（文件扩展名 .ttml 或 .xml）
    /// </summary>
    public LrcLyrics? ParseTtml(string ttmlContent)
    {
        try
        {
            // 文件过大时直接跳过，避免解析阻塞播放
            if (ttmlContent.Length > MaxLyricsParseSize)
            {
                Log.Debug("LyricsService", $"[LyricsService] TTML 文件过大（{ttmlContent.Length / 1024}KB），跳过解析");
                return null;
            }

            // 兜底清理非法 XML 字符
            ttmlContent = SanitizeForXml(ttmlContent);
            if (string.IsNullOrWhiteSpace(ttmlContent)) return null;

            var xml = XElement.Parse(ttmlContent);

            // TTML 命名空间
            XNamespace ttml = "http://www.w3.org/ns/ttml";
            XNamespace ttm = "http://www.w3.org/ns/ttml#metadata";
            XNamespace tts = "http://www.w3.org/ns/ttml#styling";
            // Apple Music TTML 可能使用两种 itunes 命名空间
            XNamespace itunes1 = "http://apple.com/itunes/lyrics";
            XNamespace itunes2 = "http://music.apple.com/lyric-ttml-internal";

            // 如果没有找到标准命名空间，尝试无命名空间
            var body = xml.Descendants(ttml + "body").FirstOrDefault()
                     ?? xml.Descendants("body").FirstOrDefault();

            if (body == null)
            {
                Log.Debug("LyricsService", "[LyricsService] TTML: 未找到 <body> 元素");
                return null;
            }

            // 解析 <metadata> 中的 <ttm:agent> 元素，构建 agent ID → 对齐方式映射
            // v1 → 左(0)，v2 → 右(2)，v3+ 主唱 → 居中(1)，v1000+ 和声 → 居中(1) + IsBackingVocal
            var agentAlignment = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var agentIsBackingVocal = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var agentSingerName = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            var agents = xml.Descendants(ttm + "agent")
                        .Concat(xml.Descendants("{http://www.w3.org/ns/ttml#metadata}agent"))
                        .Concat(xml.Descendants("agent"))
                        .Distinct();
            foreach (var agent in agents)
            {
                var agentId = agent.Attribute(XNamespace.Xml + "id")?.Value
                           ?? agent.Attribute("id")?.Value;
                if (string.IsNullOrEmpty(agentId)) continue;

                var (agentAlign, isBacking) = InferRoleAlignment(agentId);
                agentAlignment[agentId] = agentAlign;
                agentIsBackingVocal[agentId] = isBacking;

                // 提取 ttm:name 作为歌手/角色名（多个 name 用 "/" 连接，去重）
                var names = agent.Elements(ttm + "name")
                                 .Concat(agent.Elements("{http://www.w3.org/ns/ttml#metadata}name"))
                                 .Concat(agent.Elements("name"))
                                 .Select(n => n.Value?.Trim())
                                 .Where(v => !string.IsNullOrWhiteSpace(v))
                                 .Distinct(StringComparer.OrdinalIgnoreCase);
                var joinedName = string.Join(" / ", names);
                if (!string.IsNullOrWhiteSpace(joinedName))
                    agentSingerName[agentId] = joinedName;

                Log.Debug("LyricsService", $"[LyricsService] TTML: 发现 agent '{agentId}' -> 对齐{agentAlign}, 和声={isBacking}, 歌手={joinedName}");
            }

            var lyrics = new LrcLyrics();
            var lines = new List<LrcLyricLine>();

            // 收集所有 <p> 元素（包括带命名空间和不带命名空间的）
            // 注意：不使用 .Distinct()，避免对唱歌曲中不同歌手的 <p> 元素被错误去重
            var paragraphs = body.Descendants(ttml + "p")
                             .Concat(body.Descendants("p"))
                             .Where(p => p != null)
                             .ToList();

            Log.Debug("LyricsService", $"[LyricsService] TTML: 找到 {paragraphs.Count} 个 <p> 元素");

            int skippedEmpty = 0;
            foreach (var p in paragraphs)
            {
                var beginAttr = p.Attribute("begin")?.Value
                                 ?? p.Attribute(ttml + "begin")?.Value;
                var endAttr = p.Attribute("end")?.Value
                               ?? p.Attribute(ttml + "end")?.Value;

                if (string.IsNullOrEmpty(beginAttr))
                {
                    Log.Debug("LyricsService", $"[LyricsService] TTML: 跳过无 begin 属性的 <p> 元素");
                    continue;
                }

                var timestamp = ParseTtmlTimestamp(beginAttr);
                if (timestamp == null)
                {
                    Log.Debug("LyricsService", $"[LyricsService] TTML: 无法解析时间戳: {beginAttr}");
                    continue;
                }

                // 提取歌词文本（可能包含 <span> 元素）
                var (text, wordTimestamps) = ParseTtmlParagraph(p, ttml, timestamp.Value);

                if (string.IsNullOrWhiteSpace(text))
                {
                    skippedEmpty++;
                    Log.Debug("LyricsService", $"[LyricsService] TTML: 跳过空文本行 (begin={beginAttr})");
                    continue;
                }

                // 检查是否是翻译行（通过检查是否包含多种文字）
                var (orig, trans) = SplitBilingual(text);

                // 解析对齐方式与和声标记：ttm:agent > itunes:role > role > tts:textAlign
                var (alignment, isBacking) = ParseTtmlAlignment(p, ttml, itunes1, itunes2, agentAlignment, agentIsBackingVocal);

                // 提取当前行的原始角色标识（用于对唱聚焦/分栏）
                var agentAttr = p.Attribute(ttm + "agent")?.Value
                             ?? p.Attribute("{http://www.w3.org/ns/ttml#metadata}agent")?.Value
                             ?? p.Attribute("agent")?.Value;
                agentSingerName.TryGetValue(agentAttr ?? string.Empty, out var singerName);

                Log.Debug("LyricsService", $"[LyricsService] TTML: 解析行 '{orig}' (时间={timestamp}, 对齐={alignment}, 和声={isBacking}, 角色={agentAttr}, 歌手={singerName})");

                lines.Add(new LrcLyricLine
                {
                    Timestamp = timestamp.Value,
                    Text = orig,
                    Translation = trans,
                    WordTimestamps = wordTimestamps,
                    Alignment = alignment,
                    IsBackingVocal = isBacking,
                    Role = agentAttr,
                    SingerName = singerName ?? agentAttr
                });
            }

            Log.Debug("LyricsService", $"[LyricsService] TTML: 解析完成，共 {lines.Count} 行有效歌词，跳过 {skippedEmpty} 行空文本");

            if (lines.Count == 0) return null;

            // 按时间戳排序
            lyrics.Lines = lines.OrderBy(l => l.Timestamp).ToList();

            // 合并同拍对唱行（v1 左 + v2 右 → 单行左右分栏）
            MergeDuetLines(lyrics);

            // 如果任何一行有非默认对齐方式或双文本，标记为逐行对齐
            lyrics.HasPerLineAlignment = lyrics.Lines.Any(l => l.Alignment != 1 || l.SecondaryText != null);

            // 合并翻译行
            MergeTranslationLines(lyrics);

            return lyrics.Lines.Count > 0 ? lyrics : null;
        }
        catch (Exception ex)
        {
            Log.Debug("LyricsService", $"[LyricsService] TTML 解析异常: {ex.Message}");
            return null;
        }
    }
    
    /// <summary>
    /// 解析 TTML 时间戳字符串为 TimeSpan
    /// 支持格式：00:07.24, 00:00:07.24, 7.24s, PT7.24S, PT1H30M7.24S, 00:00:01:25（帧率）
    /// </summary>
    private static TimeSpan? ParseTtmlTimestamp(string? timestamp)
    {
        if (string.IsNullOrEmpty(timestamp)) return null;

        // 格式1：HH:MM:SS.mmm 或 MM:SS.mmm
        var match = System.Text.RegularExpressions.Regex.Match(
            timestamp,
            @"^(?:(\d+):)?(\d+):(\d+)(?:\.(\d+))?$");
        if (match.Success)
        {
            var hours = match.Groups[1].Success ? int.Parse(match.Groups[1].Value) : 0;
            var minutes = int.Parse(match.Groups[2].Value);
            var seconds = int.Parse(match.Groups[3].Value);
            var millis = match.Groups[4].Success
                ? int.Parse(match.Groups[4].Value.PadRight(3, '0').Substring(0, 3))
                : 0;

            return new TimeSpan(0, hours, minutes, seconds, millis);
        }

        // 格式1b：HH:MM:SS:FF（帧率格式，如 00:00:01:25，默认 30fps）
        var frameMatch = System.Text.RegularExpressions.Regex.Match(
            timestamp,
            @"^(?:(\d+):)?(\d+):(\d+):(\d+)$");
        if (frameMatch.Success)
        {
            var hours = frameMatch.Groups[1].Success ? int.Parse(frameMatch.Groups[1].Value) : 0;
            var minutes = int.Parse(frameMatch.Groups[2].Value);
            var seconds = int.Parse(frameMatch.Groups[3].Value);
            var frames = int.Parse(frameMatch.Groups[4].Value);
            // 默认 30fps，帧转毫秒
            var millis = frames * 1000 / 30;
            return new TimeSpan(0, hours, minutes, seconds, millis);
        }

        // 格式2：秒数（如 7.24 或 7.24s）
        var secondsStr = timestamp.TrimEnd('s', 'S');
        if (double.TryParse(secondsStr, out var secondsFloat))
        {
            var totalSeconds = (int)secondsFloat;
            var millis = (int)((secondsFloat - totalSeconds) * 1000);
            return new TimeSpan(0, 0, 0, totalSeconds, millis);
        }

        // 格式3：ISO 8601 持续时间（如 PT7.24S, PT1H30M7.24S, PT1M5S）
        if (timestamp.StartsWith("PT") || timestamp.StartsWith("pt"))
        {
            var isoMatch = System.Text.RegularExpressions.Regex.Match(
                timestamp,
                @"^PT(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (isoMatch.Success)
            {
                double hours = 0, minutes = 0, secs = 0;
                if (isoMatch.Groups[1].Success) hours = double.Parse(isoMatch.Groups[1].Value);
                if (isoMatch.Groups[2].Success) minutes = double.Parse(isoMatch.Groups[2].Value);
                if (isoMatch.Groups[3].Success) secs = double.Parse(isoMatch.Groups[3].Value);
                var totalSecs = hours * 3600 + minutes * 60 + secs;
                var totalInt = (int)totalSecs;
                var millis = (int)((totalSecs - totalInt) * 1000);
                return new TimeSpan(0, 0, 0, totalInt, millis);
            }
        }

        return null;
    }
    
    /// <summary>
    /// 解析 TTML 段落元素，提取文本和逐字时间戳
    /// <para>使用直接子节点遍历（而非 Descendants），避免嵌套 span 重复提取</para>
    /// <para>支持 &lt;br&gt; 换行、非 span 文本节点、嵌套 span 的内层文本</para>
    /// </summary>
    private static (string text, List<WordTimestamp>? wordTimestamps) ParseTtmlParagraph(
        XElement paragraph, XNamespace ttml, TimeSpan lineStart)
    {
        var wordTimestamps = new List<WordTimestamp>();
        var textBuilder = new StringBuilder();

        var lineEnd = ParseTtmlTimestamp(paragraph.Attribute("end")?.Value
            ?? paragraph.Attribute(ttml + "end")?.Value) ?? lineStart.Add(TimeSpan.FromSeconds(5));

        // 遍历直接子节点（包括文本节点和元素节点），保持原始顺序
        bool hasSpan = false;
        foreach (var node in paragraph.Nodes())
        {
            if (node is XText textNode)
            {
                // 纯文本节点：直接追加
                textBuilder.Append(textNode.Value);
            }
            else if (node is XElement el)
            {
                if (el.Name == ttml + "br" || el.Name.LocalName == "br")
                {
                    // <br> 换行
                    textBuilder.Append('\n');
                }
                else if (el.Name == ttml + "span" || el.Name.LocalName == "span")
                {
                    // <span> 元素：提取逐字时间戳
                    hasSpan = true;
                    var spanBegin = ParseTtmlTimestamp(el.Attribute("begin")?.Value
                        ?? el.Attribute(ttml + "begin")?.Value) ?? lineStart;
                    var spanEnd = ParseTtmlTimestamp(el.Attribute("end")?.Value
                        ?? el.Attribute(ttml + "end")?.Value) ?? lineEnd;

                    // ⚠ 网易云等中文歌词 TTML 的 span begin/end 是"相对段落（行）开始"的偏移
                    // （通常从 0 起），而 TTML 规范也允许绝对时间。启发式判断：span 整体落在
                    // 段落开始时间之前 → 视为行内偏移，加 lineStart 转绝对时间。
                    // 不转换的话 position（绝对时间）永远大于 span 时间戳 → 逐字进度瞬间=1。
                    if (spanBegin < lineStart && spanEnd <= lineEnd)
                    {
                        spanBegin = lineStart + spanBegin;
                        spanEnd = lineStart + spanEnd;
                    }

                    // 递归提取 span 内的所有文本（处理嵌套 span）
                    var spanText = ExtractElementText(el, ttml);
                    if (!string.IsNullOrEmpty(spanText))
                    {
                        textBuilder.Append(spanText);
                        wordTimestamps.Add(new WordTimestamp
                        {
                            Word = spanText,
                            Start = spanBegin,
                            Duration = spanEnd - spanBegin
                        });
                    }
                }
                else
                {
                    // 其他元素：提取文本
                    textBuilder.Append(el.Value);
                }
            }
        }

        var result = textBuilder.ToString().Trim();
        return (result, hasSpan && wordTimestamps.Count > 0 ? wordTimestamps : null);
    }

    /// <summary>递归提取元素内所有文本（处理嵌套 span 和 br）</summary>
    private static string ExtractElementText(XElement el, XNamespace ttml)
    {
        var sb = new StringBuilder();
        foreach (var node in el.Nodes())
        {
            if (node is XText textNode)
                sb.Append(textNode.Value);
            else if (node is XElement child)
            {
                if (child.Name == ttml + "br" || child.Name.LocalName == "br")
                    sb.Append('\n');
                else
                    sb.Append(ExtractElementText(child, ttml));
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 解析 TTML <p> 元素的对齐方式与是否和声
    /// 0=左对齐，1=居中，2=右对齐
    /// 支持优先级：ttm:agent > itunes:role > role > tts:textAlign > 父级 div 的 agent/role/textAlign
    /// </summary>
    private static (int alignment, bool isBackingVocal) ParseTtmlAlignment(XElement paragraph, XNamespace ttml,
        XNamespace itunes1, XNamespace itunes2, Dictionary<string, int> agentAlignment,
        Dictionary<string, bool> agentIsBackingVocal)
    {
        try
        {
            XNamespace ttm = "http://www.w3.org/ns/ttml#metadata";
            XNamespace tts = "http://www.w3.org/ns/ttml#styling";

            // 1. 检查 ttm:agent（AMLL/Apple Music TTML 的多角色标识）
            var agentAttr = paragraph.Attribute(ttm + "agent")?.Value
                         ?? paragraph.Attribute("{http://www.w3.org/ns/ttml#metadata}agent")?.Value
                         ?? paragraph.Attribute("agent")?.Value;
            if (!string.IsNullOrEmpty(agentAttr))
            {
                if (agentAlignment.TryGetValue(agentAttr, out var agentAlign))
                {
                    agentIsBackingVocal.TryGetValue(agentAttr, out var backing);
                    return (agentAlign, backing);
                }
                // agent 未在 metadata 中声明时，尝试从 id 推断
                var inferred = InferRoleAlignment(agentAttr);
                return (inferred.alignment, inferred.isBackingVocal);
            }

            // 2. 检查 itunes:role（两种命名空间都检查）
            var roleAttr = paragraph.Attribute(itunes1 + "role")?.Value
                        ?? paragraph.Attribute(itunes2 + "role")?.Value
                        ?? paragraph.Attribute("role")?.Value
                        ?? paragraph.Attribute(ttml + "role")?.Value;

            if (!string.IsNullOrEmpty(roleAttr))
            {
                var inferred = InferRoleAlignment(roleAttr);
                return (inferred.alignment, inferred.isBackingVocal);
            }

            // 3. 检查 tts:textAlign（W3C 标准对齐属性）
            var textAlignAttr = paragraph.Attribute(tts + "textAlign")?.Value
                             ?? paragraph.Attribute("textAlign")?.Value;
            if (!string.IsNullOrEmpty(textAlignAttr))
            {
                var ta = textAlignAttr.ToLowerInvariant();
                if (ta == "left" || ta == "start") return (0, false);
                if (ta == "right" || ta == "end") return (2, false);
                if (ta == "center" || ta == "middle") return (1, false);
            }

            // 4. 检查父级 <div> 的 agent/role/textAlign
            var parent = paragraph.Parent;
            if (parent != null && parent.Name.LocalName == "div")
            {
                var parentAgent = parent.Attribute(ttm + "agent")?.Value
                               ?? parent.Attribute("{http://www.w3.org/ns/ttml#metadata}agent")?.Value
                               ?? parent.Attribute("agent")?.Value;
                if (!string.IsNullOrEmpty(parentAgent))
                {
                    if (agentAlignment.TryGetValue(parentAgent, out var pa))
                    {
                        agentIsBackingVocal.TryGetValue(parentAgent, out var backing);
                        return (pa, backing);
                    }
                    var inferred = InferRoleAlignment(parentAgent);
                    return (inferred.alignment, inferred.isBackingVocal);
                }

                var parentRole = parent.Attribute(itunes1 + "role")?.Value
                              ?? parent.Attribute(itunes2 + "role")?.Value
                              ?? parent.Attribute("role")?.Value;
                if (!string.IsNullOrEmpty(parentRole))
                {
                    var inferred = InferRoleAlignment(parentRole);
                    return (inferred.alignment, inferred.isBackingVocal);
                }

                var parentAlign = parent.Attribute(tts + "textAlign")?.Value
                               ?? parent.Attribute("textAlign")?.Value;
                if (!string.IsNullOrEmpty(parentAlign))
                {
                    var ta = parentAlign.ToLowerInvariant();
                    if (ta == "left" || ta == "start") return (0, false);
                    if (ta == "right" || ta == "end") return (2, false);
                    if (ta == "center" || ta == "middle") return (1, false);
                }
            }
        }
        catch { }
        return (1, false); // 默认居中
    }

    /// <summary>
    /// 从角色 id 推断对齐方式与是否和声。
    /// 规则：v1 → 左，v2 → 右，v3+ 主唱 → 居中，v1000+ / backing / chorus / harmony → 和声。
    /// </summary>
    private static (int alignment, bool isBackingVocal) InferRoleAlignment(string roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId)) return (1, false);
        var lowered = roleId.ToLowerInvariant();

        // 明确和声标记
        if (lowered.Contains("backing") || lowered.Contains("harmony") || lowered.Contains("chorus") || lowered.Contains("bgv"))
            return (1, true);

        // 提取数字编号
        var numberMatch = System.Text.RegularExpressions.Regex.Match(lowered, @"\d+");
        if (numberMatch.Success && int.TryParse(numberMatch.Value, out var number))
        {
            if (number >= 1000) return (1, true);
            if (number == 1) return (0, false);
            if (number == 2) return (2, false);
            return (1, false);
        }

        // 方位/角色关键词
        if (lowered.Contains("left") || lowered.Contains("start") || lowered.Contains("male") || lowered.Contains("男"))
            return (0, false);
        if (lowered.Contains("right") || lowered.Contains("end") || lowered.Contains("female") || lowered.Contains("女"))
            return (2, false);

        return (1, false);
    }
    
    /// <summary>
    /// 尝试解析 TTML 格式（文件扩展名 .ttml 或 .xml）
    /// </summary>
    public LrcLyrics? ParseTtmlFromFile(string filePath)
    {
        try
        {
            var content = File.ReadAllText(filePath);
            return ParseTtml(content);
        }
        catch
        {
            return null;
        }
    }
    
    /// <summary>
    /// 异步尝试解析 TTML 格式
    /// </summary>
    public async Task<LrcLyrics?> ParseTtmlFromFileAsync(string filePath)
    {
        try
        {
            var content = await File.ReadAllTextAsync(filePath);
            return ParseTtml(content);
        }
        catch
        {
            return null;
        }
    }
    /// <summary>
    /// 根据播放位置获取当前歌词行索引（二分查找，O(log n)）
    /// </summary>
    /// <param name="lyrics">歌词对象</param>
    /// <param name="position">当前播放位置</param>
    /// <returns>当前高亮行索引，-1 表示无匹配</returns>
}

using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 本地歌词格式扩展解析：酷狗 KRC（.krc）、QQ 音乐 QRC（.qrc）。
/// KRC：文件头魔数 "krc1" → 逐字节 XOR key(16) → zlib 解压 → 文本（含 [id:] 头、[language:base64]
/// 译文/罗马音 JSON、逐字时间戳行 [行起始ms,行时长ms]&lt;字起始ms,字时长ms&gt;字）。
/// QRC：文件头魔数 "QRC1" → 逐字节 XOR key(64) → zlib 解压 → XML（&lt;line t="ms"&gt;&lt;s p="ms" t="ms" w="字"/&gt;）。
/// 解密算法为公开逆向结果（与 lx-music 同源），解析输出与标准 LRC 相同的行模型（含逐字时间戳）。
/// </summary>
public partial class LyricsService
{
    /// <summary>KRC 解密密钥（lx-music 同款，16 字节）</summary>
    private static readonly byte[] KrcKey =
    {
        0x40, 0x47, 0x61, 0x77, 0x5e, 0x32, 0x74, 0x47, 0x51, 0x36, 0x31, 0x2d, 0xce, 0xd2, 0x6e, 0x69
    };

    /// <summary>QRC 解密密钥（公开逆向，64 字节）</summary>
    private static readonly byte[] QrcKey =
    {
        0x3b, 0x44, 0x51, 0x62, 0x3e, 0x58, 0x31, 0x52, 0x34, 0x55, 0x3f, 0x73, 0x42, 0x67, 0x35, 0x6d,
        0x30, 0x6d, 0x36, 0x70, 0x3e, 0x59, 0x31, 0x4c, 0x33, 0x4e, 0x31, 0x52, 0x35, 0x6e, 0x30, 0x6a,
        0x30, 0x71, 0x36, 0x73, 0x3f, 0x5b, 0x31, 0x45, 0x3c, 0x6c, 0x33, 0x55, 0x33, 0x64, 0x30, 0x70,
        0x31, 0x71, 0x36, 0x73, 0x3f, 0x5b, 0x31, 0x45, 0x3c, 0x6c, 0x33, 0x55, 0x33, 0x64, 0x30, 0x70
    };

    // ─── KRC/QRC 解析正则：源生成版本（旧版在行/块循环中每次 Regex.Match 动态解释，
    // 长逐字歌词一次解析要构造上千个临时 Regex 对象）───

    [GeneratedRegex(@"\[language:([\w=+/\\]+)\]")]
    private static partial Regex KrcLanguageRegex();

    [GeneratedRegex(@"\[(\d+),(\d+)\]")]
    private static partial Regex KrcLineBlockRegex();

    [GeneratedRegex(@"<(\d+),(\d+)(?:,\d+)?>([^<]*)")]
    private static partial Regex KrcWordRegex();

    /// <summary>解析酷狗 KRC 歌词（需完整文件字节，含 krc1 魔数）</summary>
    public LrcLyrics? ParseKrc(byte[] data)
    {
        if (data == null || data.Length < 8) return null;
        // 魔数校验：krc1
        if (data[0] != (byte)'k' || data[1] != (byte)'r' || data[2] != (byte)'c' || data[3] != (byte)'1')
            return null;

        // 解密：跳过 4 字节魔数，逐字节 XOR key[i % 16]
        var decrypted = new byte[data.Length - 4];
        for (int i = 4; i < data.Length; i++)
            decrypted[i - 4] = (byte)(data[i] ^ KrcKey[(i - 4) % KrcKey.Length]);

        string text;
        try
        {
            using var ms = new MemoryStream(decrypted);
            using var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(z, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        catch { return null; }

        return ParseKrcText(text);
    }

    /// <summary>解析解密后的 KRC 文本</summary>
    private LrcLyrics? ParseKrcText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // 译文/罗马音：[language:base64] → JSON {"content":[{"type":0,"lyricContent":[...]},...]}
        // type 0 = 罗马音，type 1 = 译文；数组与正文行按索引对齐（酷狗协议）。
        List<string>? romaByIndex = null, transByIndex = null;
        var langMatch = KrcLanguageRegex().Match(text);
        if (langMatch.Success)
        {
            try
            {
                var json = System.Text.Json.JsonDocument.Parse(
                    Encoding.UTF8.GetString(Convert.FromBase64String(langMatch.Groups[1].Value)));
                if (json.RootElement.TryGetProperty("content", out var content) && content.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    foreach (var item in content.EnumerateArray())
                    {
                        var type = item.TryGetProperty("type", out var t) ? t.GetInt32() : -1;
                        if (!item.TryGetProperty("lyricContent", out var lc) || lc.ValueKind != System.Text.Json.JsonValueKind.Array)
                            continue;
                        var list = new List<string>();
                        foreach (var seg in lc.EnumerateArray())
                        {
                            if (seg.ValueKind == System.Text.Json.JsonValueKind.String)
                                list.Add(seg.GetString() ?? "");
                            else if (seg.ValueKind == System.Text.Json.JsonValueKind.Array)
                            {
                                // 部分源为嵌套数组（逐字段）
                                var sb = new StringBuilder();
                                foreach (var w in seg.EnumerateArray())
                                    if (w.ValueKind == System.Text.Json.JsonValueKind.String)
                                        sb.Append(w.GetString());
                                list.Add(sb.ToString());
                            }
                            else list.Add("");
                        }
                        if (type == 0) romaByIndex = list;
                        else if (type == 1) transByIndex = list;
                    }
                }
            }
            catch { }
        }

        // 正文：酷狗格式——一行文本可能含多个 [行起始ms,行时长ms] 块，块内为
        // 逐字词 <字起始ms,字时长ms,0>字（字起始为相对行起始的偏移，需加行起始得绝对时间戳）
        var lines = new List<LrcLyricLine>();
        var lineIndex = 0;
        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line.StartsWith("[id:")) continue;          // [id:$xxx] 头部
            if (line.StartsWith("[language:")) continue;    // 语言块已处理

            // 全局匹配本行内所有 [行起始,行时长] 块（酷狗一行可能多块）
            var blockMatches = KrcLineBlockRegex().Matches(line);
            if (blockMatches.Count == 0) continue;

            for (int b = 0; b < blockMatches.Count; b++)
            {
                var lineStartMs = long.Parse(blockMatches[b].Groups[1].Value);

                // 块内文本：块尾到下一个块（或行尾）
                var blockEnd = b + 1 < blockMatches.Count ? blockMatches[b + 1].Index : line.Length;
                var blockText = line.Substring(blockMatches[b].Index + blockMatches[b].Length, blockEnd - (blockMatches[b].Index + blockMatches[b].Length));

                var wordMatches = KrcWordRegex().Matches(blockText);
                var textBuilder = new StringBuilder();
                var words = new List<WordTimestamp>();
                if (wordMatches.Count > 0)
                {
                    foreach (Match wm in wordMatches)
                    {
                        var word = wm.Groups[3].Value.Trim();
                        if (word.Length == 0) continue;
                        var relStartMs = long.Parse(wm.Groups[1].Value);
                        var durMs = long.Parse(wm.Groups[2].Value);
                        words.Add(new WordTimestamp
                        {
                            Word = word,
                            Start = TimeSpan.FromMilliseconds(lineStartMs + relStartMs),
                            Duration = TimeSpan.FromMilliseconds(durMs)
                        });
                        textBuilder.Append(word);
                    }
                }
                else
                {
                    // 无逐字：块内剩余文本
                    textBuilder.Append(blockText.Trim());
                }
                if (textBuilder.Length == 0) continue;

                var lyricLine = new LrcLyricLine
                {
                    Timestamp = TimeSpan.FromMilliseconds(lineStartMs),
                    Text = textBuilder.ToString(),
                    WordTimestamps = words.Count > 0 ? words : null
                };
                // 酷狗协议：译文/罗马音数组与正文行按索引对齐
                if (transByIndex != null && lineIndex < transByIndex.Count && !string.IsNullOrEmpty(transByIndex[lineIndex]))
                    lyricLine.Translation = transByIndex[lineIndex];
                if (romaByIndex != null && lineIndex < romaByIndex.Count && !string.IsNullOrEmpty(romaByIndex[lineIndex]))
                    lyricLine.Roma = romaByIndex[lineIndex];
                lineIndex++;

                lines.Add(lyricLine);
            }
        }

        if (lines.Count == 0) return null;
        return new LrcLyrics { Lines = lines };
    }

    /// <summary>解析 QQ 音乐 QRC 歌词（需完整文件字节，含 QRC1 魔数）</summary>
    public LrcLyrics? ParseQrc(byte[] data)
    {
        if (data == null || data.Length < 8) return null;
        // 魔数校验：QRC1
        if (data[0] != (byte)'Q' || data[1] != (byte)'R' || data[2] != (byte)'C' || data[3] != (byte)'1')
            return null;

        // 解密：跳过 4 字节魔数，逐字节 XOR key[i % 64]
        var decrypted = new byte[data.Length - 4];
        for (int i = 4; i < data.Length; i++)
            decrypted[i - 4] = (byte)(data[i] ^ QrcKey[(i - 4) % QrcKey.Length]);

        string text;
        try
        {
            using var ms = new MemoryStream(decrypted);
            using var z = new System.IO.Compression.ZLibStream(ms, System.IO.Compression.CompressionMode.Decompress);
            using var reader = new StreamReader(z, Encoding.UTF8);
            text = reader.ReadToEnd();
        }
        catch { return null; }

        // XML：<qrc><line t="行起始ms"><s p="字起始ms" t="字时长ms" w="字"/></line></qrc>
        try
        {
            var doc = XDocument.Parse(text);
            var lines = new List<LrcLyricLine>();
            foreach (var lineEl in doc.Descendants("line"))
            {
                var lineStartMs = (long?)lineEl.Attribute("t") ?? 0;
                var textBuilder = new StringBuilder();
                var words = new List<WordTimestamp>();
                foreach (var s in lineEl.Elements("s"))
                {
                    var word = (string?)s.Attribute("w") ?? "";
                    if (word.Length == 0) continue;
                    var startMs = (long?)s.Attribute("p") ?? 0;
                    var durMs = (long?)s.Attribute("t") ?? 0;
                    words.Add(new WordTimestamp
                    {
                        Word = word,
                        Start = TimeSpan.FromMilliseconds(startMs),
                        Duration = TimeSpan.FromMilliseconds(durMs)
                    });
                    textBuilder.Append(word);
                }
                if (textBuilder.Length == 0) continue;
                lines.Add(new LrcLyricLine
                {
                    Timestamp = TimeSpan.FromMilliseconds(lineStartMs),
                    Text = textBuilder.ToString(),
                    WordTimestamps = words.Count > 0 ? words : null
                });
            }
            if (lines.Count == 0) return null;
            return new LrcLyrics { Lines = lines };
        }
        catch
        {
            return null;
        }
    }
}

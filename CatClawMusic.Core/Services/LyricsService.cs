using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json;

namespace CatClawMusic.Core.Services;

/// <summary>
/// 歌词服务实现（LRC 格式解析 + 多源查找）
/// </summary>
public class LyricsService : ILyricsService
{
    /// <summary>插件管理器（可选，由 UI 层设置）</summary>
    public IPluginManager? PluginManager { get; set; }

    /// <summary>时间戳正则 [mm:ss.xx]</summary>
    private static readonly Regex TimeRegex = new(@"\[(\d+):(\d+)(?:\.(\d+))?\]", RegexOptions.Compiled);
    /// <summary>逐字时间戳正则 &lt;mm:ss.xx&gt;</summary>
    private static readonly Regex WordTimeRegex = new(@"<(\d+):(\d+)(?:\.(\d+))?>", RegexOptions.Compiled);
    /// <summary>元数据标签正则 [ti:...] 等</summary>
    private static readonly Regex TagRegex = new(@"\[(ti|ar|al|by|re|ve):(.+)\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    /// <summary>文件扩展名正则</summary>
    private static readonly Regex ExtensionRegex = new(@"\.\w+$", RegexOptions.Compiled);

    /// <summary>歌词文件大小上限（2MB），超过则跳过，避免读取/解析超大文件阻塞播放</summary>
    private const int MaxLyricsFileSize = 2 * 1024 * 1024;

    /// <summary>TTML/AMLL 内容解析大小上限（1MB），超过则跳过</summary>
    private const int MaxLyricsParseSize = 1 * 1024 * 1024;

    /// <summary>
    /// 获取歌词（优先级：同名 .lrc > 嵌入歌词 > 插件）
    /// </summary>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        var lyrics = await GetLocalLyricsAsync(song);
        if (lyrics != null) return lyrics;

        if (PluginManager != null)
        {
            var providers = PluginManager.GetEnabledPlugins<ILyricsProviderPlugin>();
            foreach (var provider in providers)
            {
                try
                {
                    if (!provider.IsAvailable) continue;
                    lyrics = await provider.GetLyricsAsync(song);
                    if (lyrics != null) return lyrics;
                }
                catch
                {
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 从本地获取歌词（同名 .lrc 文件 > 嵌入歌词）
    /// </summary>
    /// <param name="song">歌曲信息</param>
    /// <param name="skipEmbedded">是否跳过嵌入歌词</param>
    public async Task<LrcLyrics?> GetLocalLyricsAsync(Song song, bool skipEmbedded = false, bool preferEmbedded = false)
    {
        var songPath = song.FilePath;

        bool isContentUri = !string.IsNullOrEmpty(songPath) && songPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase);
        bool isRemoteUrl = !string.IsNullOrEmpty(songPath) && (
            songPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            songPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            songPath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase));

        // 远程 URL 不支持本地同名 .lrc 文件查找，直接尝试内嵌歌词（通过网络流）
        if (isRemoteUrl)
        {
            if (skipEmbedded) return null;
            var embeddedLyrics = await Task.Run(() => ReadEmbeddedLyrics(songPath, isContentUri: false, isRemoteUrl: true));
            if (!string.IsNullOrWhiteSpace(embeddedLyrics))
            {
                var parsed = await Task.Run(() => TryParseLyrics(embeddedLyrics));
                if (parsed != null) return parsed;
            }
            return null;
        }

        // 优先使用已知的 LyricsPath（SAF 扫描时已匹配的歌词 content:// URI 或文件路径）
        if (!string.IsNullOrEmpty(song.LyricsPath))
        {
            if (song.LyricsPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            {
                // content:// URI：先读 LyricsPath 本身
                var content = await ReadContentUriLyricsAsync(song.LyricsPath);
                var parsed = await TryParseContentAsync(content);
                if (parsed != null) return parsed;

                // 再尝试同名词曲的 .ttml / .xml URI
                foreach (var ext in new[] { ".ttml", ".xml" })
                {
                    var altUri = ConstructLyricsUri(song.LyricsPath, ext);
                    if (altUri != null)
                    {
                        content = await ReadContentUriAsync(altUri);
                        parsed = await TryParseContentAsync(content);
                        if (parsed != null) return parsed;
                    }
                }
            }
            else
            {
                // 普通文件路径：TryReadLrcFileAsync 已支持 .lrc/.ttml/.xml
                var lrcLyrics = await TryReadLrcFileAsync(song.LyricsPath);
                if (lrcLyrics != null) return lrcLyrics;
            }
        }

        if (preferEmbedded && !skipEmbedded)
        {
            var embeddedLyrics = await Task.Run(() => ReadEmbeddedLyrics(songPath, isContentUri));
            if (!string.IsNullOrWhiteSpace(embeddedLyrics))
            {
                // 异步解析 TTML（避免大文件阻塞 UI 线程）
                var parsed = await Task.Run(() => TryParseLyrics(embeddedLyrics));
                if (parsed != null) return parsed;
            }

            // 兜底：二进制扫描 M4A 自定义 atom 等 TagLibSharp 读不到的场景
            if (!isContentUri && !string.IsNullOrEmpty(songPath) && File.Exists(songPath))
            {
                var scanned = await Task.Run(() => TryScanFileForTtmlOrAmll(songPath));
                if (scanned != null) return scanned;
            }

            if (isContentUri)
            {
                var lrcUri = ConstructLrcUri(songPath);
                if (lrcUri != null)
                {
                    var content = await ReadContentUriAsync(lrcUri);
                    if (content != null) return ParseLrc(content);
                }

                // 尝试 .ttml content:// URI
                var ttmlUri = ConstructLyricsUri(songPath, ".ttml");
                if (ttmlUri != null)
                {
                    var content = await ReadContentUriAsync(ttmlUri);
                    if (content != null)
                    {
                        var parsed = await ParseTtmlAsync(content);
                        if (parsed != null) return parsed;
                    }
                }

                // 尝试 .xml content:// URI
                var xmlUri = ConstructLyricsUri(songPath, ".xml");
                if (xmlUri != null)
                {
                    var content = await ReadContentUriAsync(xmlUri);
                    if (content != null && (content.Contains("<tt") || content.Contains("xmlns=\"http://www.w3.org/ns/ttml")))
                    {
                        var parsed = await ParseTtmlAsync(content);
                        if (parsed != null) return parsed;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(songPath))
            {
                var lrcLyrics = await TryReadLrcFileAsync(songPath);
                if (lrcLyrics != null) return lrcLyrics;
            }

            return null;
        }

        if (isContentUri)
        {
            // 方式1：从 content:// URI 构造同名歌词文件的 content:// URI
            // 尝试 .lrc
            var lrcUri = ConstructLyricsUri(songPath, ".lrc");
            if (lrcUri != null)
            {
                var content = await ReadContentUriAsync(lrcUri);
                if (content != null)
                {
                    var parsed = ParseLrc(content);
                    if (parsed != null) return parsed;
                }
            }
            
            // 尝试 .ttml
            var ttmlUri = ConstructLyricsUri(songPath, ".ttml");
            if (ttmlUri != null)
            {
                var content = await ReadContentUriAsync(ttmlUri);
                if (content != null)
                {
                    var parsed = await Task.Run(() => ParseTtml(content));
                    if (parsed != null) return parsed;
                }
            }
            
            // 尝试 .xml（可能是 TTML）
            var xmlUri = ConstructLyricsUri(songPath, ".xml");
            if (xmlUri != null)
            {
                var content = await ReadContentUriAsync(xmlUri);
                if (content != null && (content.Contains("<tt") || content.Contains("xmlns=\"http://www.w3.org/ns/ttml")))
                {
                    var parsed = await Task.Run(() => ParseTtml(content));
                    if (parsed != null) return parsed;
                }
            }

            // 方式2：从 SAF document URI 提取真实文件路径，再用文件系统查找歌词
            var realPath = TryConvertContentUriToPath(songPath);
            if (!string.IsNullOrEmpty(realPath))
            {
                var lrcLyrics = await TryReadLrcFileAsync(realPath);
                if (lrcLyrics != null) return lrcLyrics;
            }
        }
        else if (!string.IsNullOrEmpty(songPath))
        {
            var lrcLyrics = await TryReadLrcFileAsync(songPath);
            if (lrcLyrics != null) return lrcLyrics;

            // 兜底：二进制扫描内嵌 TTML/AMLL（仅未跳过内嵌歌词时，避免自动选择模式重复读取音频文件）
            if (!skipEmbedded && File.Exists(songPath))
            {
                var scanned = await Task.Run(() => TryScanFileForTtmlOrAmll(songPath));
                if (scanned != null) return scanned;
            }
        }

        if (!skipEmbedded)
        {
            var embeddedLyrics = await Task.Run(() => ReadEmbeddedLyrics(songPath, isContentUri));
            if (!string.IsNullOrWhiteSpace(embeddedLyrics))
            {
                var parsed = await Task.Run(() => TryParseLyrics(embeddedLyrics));
                if (parsed != null) return parsed;
            }

            // 兜底：二进制扫描 M4A 自定义 atom 等
            if (!isContentUri && !string.IsNullOrEmpty(songPath) && File.Exists(songPath))
            {
                var scanned = await Task.Run(() => TryScanFileForTtmlOrAmll(songPath));
                if (scanned != null) return scanned;
            }
        }

        return null;
    }

    /// <summary>读取音频文件的内嵌歌词（支持普通文件路径、content:// URI 与 http(s):// 远程 URL）</summary>
    /// <param name="songPath">音频文件路径、content:// URI 或 http(s):// URL</param>
    /// <param name="isContentUri">是否为 Android content:// URI</param>
    /// <param name="isRemoteUrl">是否为 http(s):// 远程 URL</param>
    private static string? ReadEmbeddedLyrics(string? songPath, bool isContentUri, bool isRemoteUrl = false)
    {
        if (string.IsNullOrEmpty(songPath)) return null;

        if (isContentUri)
        {
            if (ContentUriLyricsReader != null)
                return ContentUriLyricsReader(songPath);
            return null;
        }

        if (isRemoteUrl)
        {
            if (RemoteUrlStreamOpener != null)
            {
                try
                {
                    using var stream = RemoteUrlStreamOpener(songPath);
                    if (stream != null)
                    {
                        var remoteLyrics = TagReader.ReadEmbeddedLyricsFromStream(stream, GetFileNameFromUrl(songPath));
                        if (!string.IsNullOrWhiteSpace(remoteLyrics))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Lyrics] 远程流内嵌歌词读取成功 (长度={remoteLyrics.Length})");
                            return remoteLyrics;
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Lyrics] RemoteUrlStreamOpener 读取异常: {ex.Message}");
                }
            }
            return null;
        }

        var lyrics = TagReader.ReadEmbeddedLyrics(songPath);
        if (!string.IsNullOrWhiteSpace(lyrics))
        {
            System.Diagnostics.Debug.WriteLine($"[Lyrics] 内嵌歌词读取成功: {songPath} (长度={lyrics.Length})");
            return lyrics;
        }
        System.Diagnostics.Debug.WriteLine($"[Lyrics] 内嵌歌词为空: {songPath}");

        if (AndroidFileStreamOpener != null)
        {
            try
            {
                var stream = AndroidFileStreamOpener(songPath);
                if (stream != null)
                {
                    using (stream)
                    {
                        lyrics = TagReader.ReadEmbeddedLyricsFromStream(stream, Path.GetFileName(songPath));
                        if (!string.IsNullOrWhiteSpace(lyrics))
                        {
                            System.Diagnostics.Debug.WriteLine($"[Lyrics] 流内嵌歌词读取成功 (长度={lyrics.Length})");
                            return lyrics;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Lyrics] AndroidFileStreamOpener 读取异常: {ex.Message}");
            }
        }

        return null;
    }

    private static string GetFileNameFromUrl(string url)
    {
        try
        {
            var uri = new Uri(url);
            var name = Path.GetFileName(uri.LocalPath);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }
        return "remote.audio";
    }

    /// <summary>静态方法：读取内嵌歌词（含 AndroidFileStreamOpener / ContentUriLyricsReader / RemoteUrlStreamOpener 回退）</summary>
    public static string? ReadEmbeddedLyricsStatic(string? songPath)
    {
        if (string.IsNullOrEmpty(songPath)) return null;
        bool isContent = songPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase);
        bool isRemote = songPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || songPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || songPath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase);
        return ReadEmbeddedLyrics(songPath, isContentUri: isContent, isRemoteUrl: isRemote);
    }

    /// <summary>查找外部 .lrc 歌词文件并返回文本内容（含 SAF content:// 回退）</summary>
    public async Task<string?> FindExternalLyricsTextAsync(Song song)
    {
        var songPath = song.FilePath;
        if (string.IsNullOrEmpty(songPath)) return null;

        bool isContentUri = songPath.StartsWith("content://", StringComparison.OrdinalIgnoreCase);

        if (isContentUri)
        {
            // 优先 SAF 方式读取 .lrc
            var lrcUri = ConstructLyricsUri(songPath, ".lrc");
            if (lrcUri != null)
            {
                var content = await ReadContentUriAsync(lrcUri);
                if (!string.IsNullOrEmpty(content)) return content;
            }

            // 尝试 .ttml
            var ttmlUri = ConstructLyricsUri(songPath, ".ttml");
            if (ttmlUri != null)
            {
                var content = await ReadContentUriAsync(ttmlUri);
                if (!string.IsNullOrEmpty(content)) return content;
            }

            // 尝试 .xml（可能是 TTML）
            var xmlUri = ConstructLyricsUri(songPath, ".xml");
            if (xmlUri != null)
            {
                var content = await ReadContentUriAsync(xmlUri);
                if (!string.IsNullOrEmpty(content)) return content;
            }

            // 回退：通过 MediaStore 解析真实路径再查找
            var realPath = TryConvertContentUriToPath(songPath);
            if (!string.IsNullOrEmpty(realPath))
            {
                var lrcPath = MusicUtility.FindLyricsFile(realPath);
                if (!string.IsNullOrEmpty(lrcPath))
                    return await ReadLyricsFileWithEncodingDetection(lrcPath);
            }
        }
        else if (!songPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            var lrcPath = MusicUtility.FindLyricsFile(songPath);
            if (!string.IsNullOrEmpty(lrcPath))
                return await ReadLyricsFileWithEncodingDetection(lrcPath);
        }

        return null;
    }

    /// <summary>尝试读取与音频同名的歌词文件（.lrc / .ttml / .xml），并按格式解析</summary>
    /// <param name="songPath">音频文件路径</param>
    private async Task<LrcLyrics?> TryReadLrcFileAsync(string songPath)
    {
        var dir = Path.GetDirectoryName(songPath) ?? "";
        var nameNoExt = Path.GetFileNameWithoutExtension(songPath);

        // 尝试读取歌词文件：.lrc → .ttml → .xml
        // 每种扩展名先尝试 File.Exists + ReadAllBytes，失败则用 FileBytesReaderAsync 回退
        var extensions = new[] { ".lrc", ".ttml", ".xml" };
        foreach (var ext in extensions)
        {
            var filePath = Path.Combine(dir, nameNoExt + ext);
            string? content = null;

            // 方式1：直接文件读取
            try
            {
                if (File.Exists(filePath))
                {
                    // 检查文件大小，避免读取超大歌词文件
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length > MaxLyricsFileSize)
                    {
                        System.Diagnostics.Debug.WriteLine($"[LyricsService] 跳过超大歌词文件: {filePath} ({fileInfo.Length / 1024}KB)");
                        continue;
                    }
                    content = await ReadLyricsFileWithEncodingDetection(filePath);
                }
            }
            catch { }

            // 方式2：FileBytesReaderAsync 回退（Android scoped storage）
            if (string.IsNullOrEmpty(content) && FileBytesReaderAsync != null)
            {
                try
                {
                    var bytes = await FileBytesReaderAsync(filePath);
                    if (bytes != null && bytes.Length > 0)
                    {
                        // 检查文件大小，避免解析超大 TTML 文件
                        if (bytes.Length > MaxLyricsFileSize)
                        {
                            System.Diagnostics.Debug.WriteLine($"[LyricsService] 跳过超大歌词文件: {filePath} ({bytes.Length / 1024}KB)");
                            continue;
                        }
                        content = EncodingDetectAndDecode(bytes);
                    }
                }
                catch { }
            }

            if (string.IsNullOrEmpty(content)) continue;

            // 按扩展名和内容解析
            if (ext == ".lrc")
            {
                var parsed = ParseLrc(content);
                if (parsed != null) return parsed;
            }
            else if (ext == ".ttml")
            {
                var parsed = await Task.Run(() => ParseTtml(content));
                if (parsed != null) return parsed;
            }
            else if (ext == ".xml")
            {
                // .xml 可能是 TTML
                if (content.Contains("<tt") || content.Contains("xmlns=\"http://www.w3.org/ns/ttml"))
                {
                    var parsed = await Task.Run(() => ParseTtml(content));
                    if (parsed != null) return parsed;
                }
            }
        }

        // 兜底：使用 MusicUtility.FindLyricsFile 进行模糊匹配（例如 songxxx.lrc）
        try
        {
            var fuzzyPath = MusicUtility.FindLyricsFile(songPath);
            if (!string.IsNullOrEmpty(fuzzyPath))
            {
                var content = await ReadLyricsFileWithEncodingDetection(fuzzyPath);
                if (!string.IsNullOrEmpty(content))
                {                    var parsed = await TryParseContentAsync(content);                    if (parsed != null) return parsed;
                }
            }
        }
        catch { }

        return null;
    }

    /// <summary>编码检测并解码字节数组为字符串</summary>
    public static string EncodingDetectAndDecode(byte[] rawBytes)
    {
        if (NativeEncodingDetector != null)
        {
            try
            {
                var result = NativeEncodingDetector(rawBytes);
                if (result != null) return result;
            }
            catch { }
        }
        return ReadLyricsFileFallback(rawBytes);
    }

    /// <summary>
    /// 读取歌词文件并自动检测编码（优先使用原生编码检测器，回退到 C# 实现）
    ///
    /// 编码检测策略：
    ///   1. 原生编码检测器（由 UI 层注入的 C++ 原生库）
    ///   2. C# 回退：BOM UTF-8 → 严格 UTF-8 → GBK → GB2312 → 默认
    /// </summary>
    /// <param name="path">歌词文件路径</param>
    /// <returns>解码后的歌词文本</returns>
    public static async Task<string> ReadLyricsFileWithEncodingDetection(string path)
    {
        var rawBytes = await File.ReadAllBytesAsync(path);

        /* 优先使用原生编码检测器（由 UI 层注入） */
        if (NativeEncodingDetector != null)
        {
            try
            {
                var nativeResult = NativeEncodingDetector(rawBytes);
                if (nativeResult != null) return nativeResult;
            }
            catch { }
        }

        /* C# 回退实现 */
        return ReadLyricsFileFallback(rawBytes);
    }

    /// <summary>
    /// 原生编码检测器委托（由 UI 层注入 C++ 原生库的实现）
    /// 输入：原始字节数据；输出：UTF-8 字符串，失败返回 null
    /// </summary>
    public static Func<byte[], string?>? NativeEncodingDetector { get; set; }

    /// <summary>
    /// C# 回退的编码检测实现（当原生库不可用时使用）
    /// </summary>
    private static string ReadLyricsFileFallback(byte[] rawBytes)
    {
        /* 1. BOM UTF-8 检测 */
        if (rawBytes.Length >= 3 && rawBytes[0] == 0xEF && rawBytes[1] == 0xBB && rawBytes[2] == 0xBF)
            return Encoding.UTF8.GetString(rawBytes, 3, rawBytes.Length - 3);

        /* 2. BOM UTF-16 LE 检测 */
        if (rawBytes.Length >= 2 && rawBytes[0] == 0xFF && rawBytes[1] == 0xFE)
            return Encoding.Unicode.GetString(rawBytes, 2, rawBytes.Length - 2);

        /* 3. BOM UTF-16 BE 检测 */
        if (rawBytes.Length >= 2 && rawBytes[0] == 0xFE && rawBytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(rawBytes, 2, rawBytes.Length - 2);

        /* 4. 严格 UTF-8 验证 */
        try
        {
            var decoder = Encoding.UTF8.GetDecoder();
            decoder.Fallback = new DecoderExceptionFallback();
            var chars = new char[rawBytes.Length];
            decoder.GetChars(rawBytes, 0, rawBytes.Length, chars, 0, false);
            return new string(chars);
        }
        catch { }

        /* 5. 若字节数能被 2 整除且含大量 0x00，优先按 UTF-16 LE 解码 */
        if (rawBytes.Length % 2 == 0 && ContainsManyNullBytes(rawBytes))
        {
            try
            {
                var utf16 = Encoding.Unicode.GetString(rawBytes);
                if (utf16.Contains('<') && utf16.Contains('>'))
                    return utf16;
            }
            catch { }
        }

        /* 6. GBK 解码 */
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var gbk = Encoding.GetEncoding("GBK");
            return gbk.GetString(rawBytes);
        }
        catch { }

        /* 7. GB2312 解码 */
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            var gb2312 = Encoding.GetEncoding("GB2312");
            return gb2312.GetString(rawBytes);
        }
        catch { }

        /* 8. 默认 UTF-8（宽松模式） */
        return Encoding.UTF8.GetString(rawBytes);
    }

    /// <summary>判断字节数组是否包含大量 0x00（UTF-16 编码特征）</summary>
    private static bool ContainsManyNullBytes(byte[] rawBytes)
    {
        int nullCount = 0;
        int sampleLen = Math.Min(rawBytes.Length, 4096);
        for (int i = 0; i < sampleLen; i++)
        {
            if (rawBytes[i] == 0x00) nullCount++;
        }
        return nullCount > sampleLen / 8;
    }

    /// <summary>
    /// 清理字符串中 XML 不允许的非法控制字符（如 0x00）以及零宽字符，
    /// 作为编码检测失败后的最后一道兜底。
    /// </summary>
    private static string SanitizeForXml(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            // 仅保留 XML 1.0 合法字符：#x9 | #xA | #xD | [#x20-#xD7FF] | [#xE000-#xFFFD] | [#x10000-#x10FFFF]
            if (ch == 0x9 || ch == 0xA || ch == 0xD ||
                (ch >= 0x20 && ch <= 0xD7FF) ||
                (ch >= 0xE000 && ch <= 0xFFFD))
            {
                sb.Append(ch);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// 若字符串被 UTF-8 误读后包含 0x00，尝试按 UTF-16 LE 重新解码原始字节。
    /// </summary>
    private static string TryReinterpretAsUtf16(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains('\0')) return text;
        try
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.Length % 2 == 0)
            {
                var reinterpreted = Encoding.Unicode.GetString(bytes);
                if (!reinterpreted.Contains('\0') && reinterpreted.Contains('<'))
                    return reinterpreted;
            }
        }
        catch { }
        return text;
    }

    /// <summary>从 SAF content URI 构造同名 .lrc 的 content URI</summary>
    internal static string? ConstructLrcUri(string songUri)
    {
        return ConstructLyricsUri(songUri, ".lrc");
    }

    /// <summary>
    /// 通用方法：从音频文件的 content URI 构造任意扩展名的 content URI
    /// </summary>
    internal static string? ConstructLyricsUri(string songUri, string extension)
    {
        try
        {
            int docIdx = songUri.LastIndexOf("/document/", StringComparison.Ordinal);
            if (docIdx < 0) return null;
            string prefix = songUri.Substring(0, docIdx + "/document/".Length);
            string docId = songUri.Substring(docIdx + "/document/".Length);
            string newDocId = ExtensionRegex.Replace(docId, extension);
            if (newDocId == docId) return null;
            return prefix + newDocId;
        }
        catch { return null; }
    }

    /// <summary>读取 content:// URI 文本（由平台层注入，用于读取 .lrc 文件）</summary>
    public static Func<string, Task<string?>>? ContentUriReader { get; set; }

    /// <summary>读取 content:// URI 音频文件并提取内嵌歌词（由平台层注入）</summary>
    public static Func<string, string?>? ContentUriLyricsReader { get; set; }

    /// <summary>通过 Android ContentResolver 打开文件流（由平台层注入，用于普通文件路径在 scoped storage 下无法直接访问时的回退）</summary>
    public static Func<string, Stream?>? AndroidFileStreamOpener { get; set; }

    /// <summary>通过 HTTP 请求打开远程音频文件流（由平台层注入，用于 WebDAV/SMB 等网络歌曲的内嵌歌词读取）。返回的流必须支持 Seek（建议返回 MemoryStream）</summary>
    public static Func<string, Stream?>? RemoteUrlStreamOpener { get; set; }

    /// <summary>读取任意文件字节（含 ContentResolver 回退），由平台层注入，用于 Android 11+ scoped storage 下读取 .lrc 等文件</summary>
    public static Func<string, Task<byte[]?>>? FileBytesReaderAsync { get; set; }

    /// <summary>通过注入的 ContentUriReader 读取 content URI 内容</summary>
    private static async Task<string?> ReadContentUriAsync(string uri)
    {
        if (ContentUriReader != null)
            return await ContentUriReader(uri);
        return null;
    }

    /// <summary>异步解析歌词文本（封装在 Task.Run 中避免阻塞 UI 线程）</summary>
    private async Task<LrcLyrics?> TryParseContentAsync(string? content)
    {
        if (string.IsNullOrEmpty(content)) return null;
        return await Task.Run(() => TryParseLyrics(content));
    }

    /// <summary>读取 content:// URI 的歌词文本（公共方法，供 SongDetailBottomSheet 等调用）</summary>
    public static async Task<string?> ReadContentUriLyricsAsync(string uri)
    {
        if (string.IsNullOrEmpty(uri)) return null;

        // 优先通过 ContentUriReader（平台注入的 ContentResolver 读取）
        var content = await ReadContentUriAsync(uri);
        if (!string.IsNullOrEmpty(content)) return content;

        // 回退：通过 FileBytesReaderAsync 读取字节并解码
        if (FileBytesReaderAsync != null)
        {
            try
            {
                var bytes = await FileBytesReaderAsync(uri);
                if (bytes != null && bytes.Length > 0)
                    return EncodingDetectAndDecode(bytes);
            }
            catch { }
        }

        return null;
    }

    /// <summary>尝试将 SAF content:// URI 转换为真实文件系统路径</summary>
    private static string? TryConvertContentUriToPath(string uri)
    {
        try
        {
            // content://com.android.externalstorage.documents/tree/primary%3AMusic/document/primary%3AMusic%2F...
            var decoded = Uri.UnescapeDataString(uri);
            // 提取 document ID 部分（最后一个 /document/ 之后）
            int docIdx = decoded.LastIndexOf("/document/", StringComparison.Ordinal);
            if (docIdx < 0) return null;
            string docId = decoded.Substring(docIdx + "/document/".Length);

            // primary:Foo/bar → /storage/emulated/0/Foo/bar
            if (docId.StartsWith("primary:", StringComparison.Ordinal))
            {
                string subPath = docId.Substring("primary:".Length);
                string fullPath = "/storage/emulated/0/" + subPath;
                if (File.Exists(fullPath)) return fullPath;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// 解析 LRC 格式字符串（增强版，兼容多种时间戳格式）
    /// 支持 [mm:ss.xx]、[mm:ss.xxx]、[mm:ss]
    /// </summary>
    public LrcLyrics? ParseLrc(string lrcContent)
    {
        var lyrics = new LrcLyrics();

        lrcContent = lrcContent.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = lrcContent.Split('\n');

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//")) continue;

            var tagMatch = TagRegex.Match(line);
            if (tagMatch.Success)
            {
                var tag = tagMatch.Groups[1].Value.ToLower();
                var value = tagMatch.Groups[2].Value.Trim();
                switch (tag)
                {
                    case "ti": lyrics.Metadata.Title = value; break;
                    case "ar": lyrics.Metadata.Artist = value; break;
                    case "al": lyrics.Metadata.Album = value; break;
                    case "by": lyrics.Metadata.Author = value; break;
                    case "re": lyrics.Metadata.Maker = value; break;
                    case "ve": lyrics.Metadata.Version = value; break;
                }
                continue;
            }

            // 解析歌词行
            var timeMatches = TimeRegex.Matches(line);
            if (timeMatches.Count == 0) continue;

            // 提取歌词文本（最后一个 ] 之后的内容）
            var lastBracketIndex = line.LastIndexOf(']');
            var text = lastBracketIndex >= 0
                ? line.Substring(lastBracketIndex + 1).Trim()
                : "";

            // 跳过纯音乐标记
            if (text.Contains("纯音乐") || text.Contains("暂无歌词"))
            {
                text = "";
            }

            // 一个歌词行可能对应多个时间戳 [01:23.45][02:34.56]歌词
            foreach (Match match in timeMatches)
            {
                var minutes = int.Parse(match.Groups[1].Value);
                var seconds = int.Parse(match.Groups[2].Value);
                var millis = match.Groups[3].Success
                    ? int.Parse(match.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                    : 0;

                var timestamp = new TimeSpan(0, 0, minutes, seconds, millis);

                var wordTimestamps = ParseWordTimestamps(text, timestamp);
                var lineText = wordTimestamps != null ? string.Join("", wordTimestamps.Select(w => w.Word)) : text;
                // 有逐字时间戳时不做 SplitBilingual（翻译行会通过 MergeTranslationLines 合并）
                var (orig, trans) = wordTimestamps != null ? (lineText, (string?)null) : SplitBilingual(lineText);

                lyrics.Lines.Add(new LrcLyricLine
                {
                    Timestamp = timestamp,
                    Text = orig,
                    Translation = trans,
                    WordTimestamps = wordTimestamps
                });
            }
        }

        // 按时间戳排序（LRC 文件通常已有序，仅兜底排序）
        lyrics.Lines = lyrics.Lines.OrderBy(l => l.Timestamp).ToList();

        // 合并同时间戳的翻译行：
        // 格式如 [00:07.24]<00:07.24>미안해 ... 和 [00:07.24]对不起 ...
        // 两个行时间戳相同，翻译行没有逐字时间戳且文本较短（纯翻译），
        // 应合并为原文行的 Translation 字段
        MergeTranslationLines(lyrics);

        return lyrics.Lines.Count > 0 ? lyrics : null;
    }

    /// <summary>
    /// 合并同时间戳的翻译行到原文行
    /// <para>判断条件：两行时间戳相同，且其中一行没有逐字时间戳（或文本明显是翻译）</para>
    /// </summary>
    private static void MergeTranslationLines(LrcLyrics lyrics)
    {
        if (lyrics.Lines.Count < 2) return;

        var merged = new List<LrcLyricLine>();
        var i = 0;
        while (i < lyrics.Lines.Count)
        {
            var current = lyrics.Lines[i];

            // 查找后续同时间戳的行
            var j = i + 1;
            while (j < lyrics.Lines.Count && lyrics.Lines[j].Timestamp == current.Timestamp)
            {
                var next = lyrics.Lines[j];

                // 判断哪行是原文、哪行是翻译
                // 规则：有逐字时间戳的是原文；都没有时，第一行是原文
                if (current.WordTimestamps != null && current.WordTimestamps.Count > 0
                    && (next.WordTimestamps == null || next.WordTimestamps.Count == 0))
                {
                    // 当前行有逐字时间戳（原文），next 是翻译
                    if (string.IsNullOrEmpty(current.Translation))
                        current.Translation = next.Text;
                    i = j + 1;
                    goto AddCurrent;
                }

                if ((next.WordTimestamps != null && next.WordTimestamps.Count > 0)
                    && (current.WordTimestamps == null || current.WordTimestamps.Count == 0))
                {
                    // next 有逐字时间戳（原文），当前行是翻译
                    if (string.IsNullOrEmpty(next.Translation))
                        next.Translation = current.Text;
                    current = next;
                    i = j + 1;
                    goto AddCurrent;
                }

                // 两行都没有逐字时间戳，用 SplitBilingual 的结果判断
                // 如果当前行已有翻译，或者 next 的文本看起来是翻译（更短、不同语言）
                if (string.IsNullOrEmpty(current.Translation) && !string.IsNullOrEmpty(next.Text))
                {
                    // 检查是否是不同语言（如韩文+中文）
                    if (IsDifferentScript(current.Text, next.Text))
                    {
                        current.Translation = next.Text;
                        i = j + 1;
                        goto AddCurrent;
                    }
                }

                // 无法判断，保留两行
                j++;
            }

            i = j;

        AddCurrent:
            merged.Add(current);
        }

        lyrics.Lines = merged;
    }

    /// <summary>
    /// 合并同拍对唱行：把同一时刻的主唱 v1（左）和 v2（右）合并为单行双文本。
    /// <para>合并后：Primary 存左侧文本，SecondaryText 存右侧文本，和声行不合并。</para>
    /// </summary>
    private static void MergeDuetLines(LrcLyrics lyrics)
    {
        if (lyrics.Lines.Count < 2) return;

        const int mergeToleranceMs = 150; // 时间差容差 150ms
        var merged = new List<LrcLyricLine>();
        var used = new bool[lyrics.Lines.Count];

        for (int i = 0; i < lyrics.Lines.Count; i++)
        {
            if (used[i]) continue;
            var current = lyrics.Lines[i];

            // 和声行不合并，保持独立
            if (current.IsBackingVocal)
            {
                merged.Add(current);
                used[i] = true;
                continue;
            }

            // 已带 SecondaryText 的行不再合并
            if (!string.IsNullOrEmpty(current.SecondaryText))
            {
                merged.Add(current);
                used[i] = true;
                continue;
            }

            // 寻找同拍且对齐方式互补的另一主唱行
            LrcLyricLine? partner = null;
            int partnerIdx = -1;
            for (int j = i + 1; j < lyrics.Lines.Count; j++)
            {
                if (used[j]) continue;
                var next = lyrics.Lines[j];
                if (next.IsBackingVocal) continue;
                if (!string.IsNullOrEmpty(next.SecondaryText)) continue;

                var diffMs = Math.Abs((next.Timestamp - current.Timestamp).TotalMilliseconds);
                if (diffMs > mergeToleranceMs) break; // 已排序，后面时间差更大

                // 仅合并对齐互补的 v1/v2 行：0+2
                if ((current.Alignment == 0 && next.Alignment == 2) ||
                    (current.Alignment == 2 && next.Alignment == 0))
                {
                    partner = next;
                    partnerIdx = j;
                    break;
                }
            }

            if (partner != null)
            {
                // current 为左，partner 为右；若 current 是右则交换
                if (current.Alignment == 0)
                {
                    current.SecondaryText = partner.Text;
                    current.SecondaryAlignment = partner.Alignment;
                }
                else
                {
                    current.SecondaryText = current.Text;
                    current.SecondaryAlignment = current.Alignment;
                    current.Text = partner.Text;
                    current.Alignment = partner.Alignment;
                }
                used[partnerIdx] = true;
            }

            merged.Add(current);
            used[i] = true;
        }

        lyrics.Lines = merged;
    }

    /// <summary>
    /// 判断两段文本是否使用了不同的文字系统（如韩文 vs 中文）
    /// </summary>
    private static bool IsDifferentScript(string text1, string text2)
    {
        if (string.IsNullOrEmpty(text1) || string.IsNullOrEmpty(text2)) return false;

        var script1 = GetDominantScript(text1);
        var script2 = GetDominantScript(text2);

        return script1 != script2 && script1 != ScriptType.Unknown && script2 != ScriptType.Unknown;
    }

    /// <summary>文字系统类型，用于区分原文与翻译</summary>
    private enum ScriptType { Unknown, Cjk, Japanese, Hangul, Latin }

    /// <summary>统计文本中各类字符的数量，返回占主导地位的文字系统</summary>
    private static ScriptType GetDominantScript(string text)
    {
        int cjk = 0, japanese = 0, hangul = 0, latin = 0;
        foreach (var ch in text)
        {
            if (IsJapanese(ch)) japanese++;
            else if (IsHangul(ch)) hangul++;
            else if (IsCjk(ch)) cjk++;
            else if (char.IsLetter(ch) && ch <= 0x007F) latin++;
        }
        // 日文判定：含假名字符则视为日文（即使也有汉字）
        if (japanese > 0) return ScriptType.Japanese;
        var max = Math.Max(cjk, Math.Max(hangul, latin));
        if (max == 0) return ScriptType.Unknown;
        if (max == cjk) return ScriptType.Cjk;
        if (max == hangul) return ScriptType.Hangul;
        return ScriptType.Latin;
    }

    /// <summary>判断字符是否为韩文字母</summary>
    private static bool IsHangul(char ch)
    {
        return (ch >= 0xAC00 && ch <= 0xD7AF) ||   // 韩文音节
               (ch >= 0x1100 && ch <= 0x11FF) ||   // 韩文字母 Jamo
               (ch >= 0x3130 && ch <= 0x318F);     // 韩文兼容字母
    }

    /// <summary>
    /// 解析纯文本歌词（无时间戳），为每行生成等间隔时间戳
    /// </summary>
    public LrcLyrics? ParsePlainTextLyrics(string text)
    {
        var lyrics = new LrcLyrics();

        text = text.Replace("\r\n", "\n").Replace("\r", "\n");
        var lines = text.Split('\n');

        int lineIndex = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;
            if (line.StartsWith("//")) continue;

            var tagMatch = TagRegex.Match(line);
            if (tagMatch.Success)
            {
                lineIndex++;
                continue;
            }

            if (line.Contains("纯音乐") || line.Contains("暂无歌词"))
                continue;

            var timestamp = TimeSpan.FromSeconds(lineIndex * 5);
            lyrics.Lines.Add(new LrcLyricLine
            {
                Timestamp = timestamp,
                Text = line
            });
            lineIndex++;
        }

        return lyrics.Lines.Count > 0 ? lyrics : null;
    }

    /// <summary>
    /// 智能解析歌词内容：先检测格式（XML/JSON/LRC），再调用对应解析器。
    /// <para>关键：XML/JSON 内容绝不回退到 ParsePlainTextLyrics，避免显示原始代码</para>
    /// </summary>
    public LrcLyrics? TryParseLyrics(string content)
    {
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 编码兜底：若误读导致含 0x00，尝试按 UTF-16 重新解释
        if (content.Contains('\0'))
            content = TryReinterpretAsUtf16(content);

        content = SanitizeForXml(content);
        if (string.IsNullOrWhiteSpace(content)) return null;

        // 检测内容类型
        bool isXml = content.Contains("<tt") || content.Contains("<?xml")
            || content.Contains("xmlns=\"http://www.w3.org/ns/ttml")
            || (content.TrimStart().StartsWith("<") && content.Contains(">") && !content.TrimStart().StartsWith("["));
        bool isJson = content.TrimStart().StartsWith("{")
            && (content.Contains("\"lyrics\"") || content.Contains("\"lines\"") || content.Contains("\"role\"")
                || content.Contains("\"code\"") || content.Contains("\"message\"") || content.Contains("\"data\""));

        if (isXml)
        {
            // TTML 专用路径：绝不回退到 PlainText
            return ParseTtml(content);
        }

        if (isJson)
        {
            // AMLL JSON 专用路径：绝不回退到 PlainText
            return ParseAmll(content);
        }

        // LRC 或纯文本：先试 LRC，再试纯文本
        var lrc = ParseLrc(content);
        if (lrc != null) return lrc;

        // 防御：非歌词内容（JSON/XML/HTML/错误响应）不应作为纯文本歌词显示
        var trimmed = content.Trim();
        if ((trimmed.StartsWith("<") && trimmed.Contains(">"))
            || (trimmed.StartsWith("{") && trimmed.EndsWith("}"))
            || trimmed.StartsWith("<!DOCTYPE", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("<body", StringComparison.OrdinalIgnoreCase))
        {
            System.Diagnostics.Debug.WriteLine("[LyricsService] 检测到非歌词内容，已过滤");
            return null;
        }

        return ParsePlainTextLyrics(content);
    }

    /// <summary>
    /// 解析 TTML (Timed Text Markup Language) 格式歌词
    /// 支持 W3C TTML 标准，常用于 Apple Music、Netflix 等平台
    /// </summary>
    /// <summary>
    /// 异步解析 TTML 格式（包装在 Task.Run 中避免阻塞 UI 线程）
    /// </summary>
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
                System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML 文件过大（{ttmlContent.Length / 1024}KB），跳过解析");
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
                System.Diagnostics.Debug.WriteLine("[LyricsService] TTML: 未找到 <body> 元素");
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

                System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML: 发现 agent '{agentId}' -> 对齐{agentAlign}, 和声={isBacking}, 歌手={joinedName}");
            }

            var lyrics = new LrcLyrics();
            var lines = new List<LrcLyricLine>();

            // 收集所有 <p> 元素（包括带命名空间和不带命名空间的）
            // 注意：不使用 .Distinct()，避免对唱歌曲中不同歌手的 <p> 元素被错误去重
            var paragraphs = body.Descendants(ttml + "p")
                             .Concat(body.Descendants("p"))
                             .Where(p => p != null)
                             .ToList();

            System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML: 找到 {paragraphs.Count} 个 <p> 元素");

            int skippedEmpty = 0;
            foreach (var p in paragraphs)
            {
                var beginAttr = p.Attribute("begin")?.Value
                                 ?? p.Attribute(ttml + "begin")?.Value;
                var endAttr = p.Attribute("end")?.Value
                               ?? p.Attribute(ttml + "end")?.Value;

                if (string.IsNullOrEmpty(beginAttr))
                {
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML: 跳过无 begin 属性的 <p> 元素");
                    continue;
                }

                var timestamp = ParseTtmlTimestamp(beginAttr);
                if (timestamp == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML: 无法解析时间戳: {beginAttr}");
                    continue;
                }

                // 提取歌词文本（可能包含 <span> 元素）
                var (text, wordTimestamps) = ParseTtmlParagraph(p, ttml, timestamp.Value);

                if (string.IsNullOrWhiteSpace(text))
                {
                    skippedEmpty++;
                    System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML: 跳过空文本行 (begin={beginAttr})");
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

                System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML: 解析行 '{orig}' (时间={timestamp}, 对齐={alignment}, 和声={isBacking}, 角色={agentAttr}, 歌手={singerName})");

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

            System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML: 解析完成，共 {lines.Count} 行有效歌词，跳过 {skippedEmpty} 行空文本");

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
            System.Diagnostics.Debug.WriteLine($"[LyricsService] TTML 解析异常: {ex.Message}");
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
    public int GetCurrentLyricIndex(LrcLyrics? lyrics, TimeSpan position)
    {
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0) return -1;

        var lines = lyrics.Lines;
        int lo = 0, hi = lines.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + (hi - lo) / 2;
            if (lines[mid].Timestamp <= position)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return hi;
    }

    /// <summary>
    /// 获取当前播放位置下所有"活跃"的歌词行索引（用于合唱/对唱同时着色）。
    /// 一行歌词的活跃区间为 [Timestamp, 下一行Timestamp)；若两行时间区间有重叠（合唱），
    /// 则同一时刻可能返回多个索引。非合唱时仅返回一个索引（与 GetCurrentLyricIndex 一致）。
    /// </summary>
    /// <param name="lyrics">歌词对象</param>
    /// <param name="position">当前播放位置</param>
    /// <returns>活跃行索引列表（按时间排序），空列表表示无匹配</returns>
    public List<int> GetActiveLyricIndices(LrcLyrics? lyrics, TimeSpan position)
    {
        var result = new List<int>();
        if (lyrics?.Lines == null || lyrics.Lines.Count == 0) return result;

        var lines = lyrics.Lines;
        for (int i = 0; i < lines.Count; i++)
        {
            var start = lines[i].Timestamp;
            // 行结束时间 = 下一行的开始时间；最后一行默认活跃 5 秒
            var end = i + 1 < lines.Count
                ? lines[i + 1].Timestamp
                : start + TimeSpan.FromSeconds(5);

            if (position >= start && position < end)
                result.Add(i);
        }

        // 若没有精确匹配（位置在第一行之前），回退到 GetCurrentLyricIndex
        if (result.Count == 0)
        {
            var idx = GetCurrentLyricIndex(lyrics, position);
            if (idx >= 0) result.Add(idx);
        }

        return result;
    }

    /// <summary>
    /// 根据播放位置获取当前行内的逐字歌词索引（遍历查找，O(n)）
    /// </summary>
    /// <returns>当前高亮字的索引，-1 表示无逐字数据</returns>
    public int GetCurrentWordIndex(LrcLyricLine? line, TimeSpan position)
    {
        if (line?.WordTimestamps == null || line.WordTimestamps.Count == 0) return -1;
        for (int i = line.WordTimestamps.Count - 1; i >= 0; i--)
        {
            if (line.WordTimestamps[i].Start <= position)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// 拆分双语歌词行：从原文本中识别并切分出原文与翻译。
    /// <para>策略1：日文+中文（含假名 vs 纯汉字）；策略2：通用 CJK + 非 CJK 分割。</para>
    /// </summary>
    /// <param name="text">待拆分的歌词文本</param>
    /// <returns>(原文, 翻译)；无翻译时第二项为 null</returns>
    private static (string original, string? translation) SplitBilingual(string text)
    {
        if (string.IsNullOrEmpty(text)) return (text, null);

        // 策略1：日文+中文分割 — 日文含假名，中文纯汉字
        // 找到"含假名的区域"结束后、"纯汉字区域"开始前的空白分隔点
        var jpCnSplit = FindJapaneseChineseSplit(text);
        if (jpCnSplit > 0)
        {
            var orig = text.Substring(0, jpCnSplit).TrimEnd();
            var trans = text.Substring(jpCnSplit).TrimStart();
            if (!string.IsNullOrEmpty(orig) && !string.IsNullOrEmpty(trans))
                return (orig, trans);
        }

        // 策略2：通用 CJK + 非 CJK 分割（韩文+中文等）
        bool hasCjk = false;
        bool hasNonCjk = false;
        foreach (var ch in text)
        {
            if (IsCjk(ch) || IsJapanese(ch) || IsHangul(ch)) hasCjk = true;
            else if (char.IsLetter(ch)) hasNonCjk = true;
        }
        if (!hasCjk || !hasNonCjk) return (text, null);

        int splitPos = -1;
        bool inCjkRun = false;
        int cjkRunStart = -1;
        bool seenJapanese = false;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (IsJapanese(ch))
            {
                seenJapanese = true;
                inCjkRun = false;
                cjkRunStart = -1;
            }
            else if (IsCjk(ch))
            {
                if (!inCjkRun)
                {
                    inCjkRun = true;
                    cjkRunStart = i;
                }
            }
            else
            {
                if (inCjkRun && seenJapanese && cjkRunStart > 0)
                {
                    if (char.IsWhiteSpace(text[cjkRunStart - 1]))
                        splitPos = cjkRunStart;
                }
                inCjkRun = false;
                cjkRunStart = -1;
            }
        }

        if (splitPos < 0)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (IsCjk(text[i]))
                {
                    if (i > 0 && char.IsWhiteSpace(text[i - 1]))
                    {
                        bool hasNonCjkBefore = false;
                        for (int j = 0; j < i - 1; j++)
                        {
                            if (char.IsLetter(text[j]) && !IsCjk(text[j]))
                            {
                                hasNonCjkBefore = true;
                                break;
                            }
                        }
                        if (hasNonCjkBefore)
                        {
                            splitPos = i;
                            break;
                        }
                    }
                    break;
                }
            }
        }

        if (splitPos > 0)
        {
            var orig = text.Substring(0, splitPos).TrimEnd();
            var trans = text.Substring(splitPos).TrimStart();
            if (!string.IsNullOrEmpty(orig) && !string.IsNullOrEmpty(trans))
                return (orig, trans);
        }

        return (text, null);
    }

    /// <summary>
    /// 查找日文+中文的分隔点
    /// <para>日文特征：含假名（ひらがな/カタカナ）；中文特征：纯汉字（无假名）</para>
    /// <para>策略：找到最后一个假名位置，在其后找空白分隔点，确保分隔后无假名</para>
    /// </summary>
    /// <returns>分割位置（空白字符位置），0 表示未找到</returns>
    private static int FindJapaneseChineseSplit(string text)
    {
        // 找到最后一个假名字符的位置
        int lastKanaPos = -1;
        for (int i = 0; i < text.Length; i++)
        {
            if (IsJapanese(text[i]))
                lastKanaPos = i;
        }

        // 没有假名，无法判断为日文
        if (lastKanaPos < 0) return 0;

        // 检查最后一个假名之后是否还有汉字（中文翻译）
        bool hasCjkAfterLastKana = false;
        for (int i = lastKanaPos + 1; i < text.Length; i++)
        {
            if (IsCjk(text[i])) { hasCjkAfterLastKana = true; break; }
        }
        if (!hasCjkAfterLastKana) return 0;

        // 从最后一个假名之后，找空白分隔点
        // 空白后面必须只有纯汉字（无假名），才认为是中文翻译
        for (int i = lastKanaPos + 1; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i])) continue;

            // 跳过连续空白
            int nextStart = i + 1;
            while (nextStart < text.Length && char.IsWhiteSpace(text[nextStart]))
                nextStart++;

            if (nextStart >= text.Length) break;

            // 空白后第一个字符必须是 CJK
            if (!IsCjk(text[nextStart])) continue;

            // 确认空白后面到文本末尾没有假名（纯中文翻译）
            bool hasKanaAfter = false;
            for (int k = nextStart; k < text.Length; k++)
            {
                if (IsJapanese(text[k])) { hasKanaAfter = true; break; }
            }
            if (!hasKanaAfter) return i;
        }

        // 如果没有空白分隔，但最后一个假名后紧跟汉字（无空格情况）
        // 尝试在假名后直接分割（不太常见但作为兜底）
        return 0;
    }

    /// <summary>判断字符是否为 CJK 中日韩统一表意文字（含兼容区与全角符号）</summary>
    private static bool IsCjk(char ch)
    {
        return (ch >= 0x4E00 && ch <= 0x9FFF) || (ch >= 0x3400 && ch <= 0x4DBF) ||
               (ch >= 0x2E80 && ch <= 0x2EFF) || (ch >= 0x3000 && ch <= 0x303F) ||
               (ch >= 0xFF00 && ch <= 0xFFEF);
    }

    /// <summary>判断字符是否为日文假名（平假名/片假名/半角片假名）</summary>
    private static bool IsJapanese(char ch)
    {
        return (ch >= 0x3040 && ch <= 0x309F) || (ch >= 0x30A0 && ch <= 0x30FF) ||
               (ch >= 0x31F0 && ch <= 0x31FF) || (ch >= 0xFF65 && ch <= 0xFF9F);
    }

    /// <summary>
    /// 解析行内逐字时间戳（格式：&lt;mm:ss.xx&gt;word &lt;mm:ss.xx&gt;word ...）
    /// </summary>
    private List<WordTimestamp>? ParseWordTimestamps(string text, TimeSpan lineTimestamp)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var matches = WordTimeRegex.Matches(text);
        if (matches.Count == 0) return null;

        var result = new List<WordTimestamp>();
        var lastIndex = 0;

        for (int i = 0; i < matches.Count; i++)
        {
            var m = matches[i];
            var wordStart = m.Index + m.Length;
            var nextTagIndex = i + 1 < matches.Count ? matches[i + 1].Index : text.Length;
            var word = text.Substring(wordStart, nextTagIndex - wordStart).Trim();

            if (string.IsNullOrEmpty(word)) continue;

            var minutes = int.Parse(m.Groups[1].Value);
            var seconds = int.Parse(m.Groups[2].Value);
            var millis = m.Groups[3].Success
                ? int.Parse(m.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                : 0;
            var start = new TimeSpan(0, 0, minutes, seconds, millis);

            TimeSpan duration;
            if (i + 1 < matches.Count)
            {
                var nextM = matches[i + 1];
                var nm = int.Parse(nextM.Groups[1].Value);
                var ns = int.Parse(nextM.Groups[2].Value);
                var nms = nextM.Groups[3].Success
                    ? int.Parse(nextM.Groups[3].Value.PadRight(3, '0').Substring(0, 3))
                    : 0;
                var nextStart = new TimeSpan(0, 0, nm, ns, nms);
                duration = nextStart - start;
            }
            else
            {
                duration = TimeSpan.FromMilliseconds(500);
            }

            result.Add(new WordTimestamp
            {
                Word = word,
                Start = start,
                Duration = duration
            });
            lastIndex = nextTagIndex;
        }

        return result.Count > 0 ? result : null;
    }

    /// <summary>
    /// 解析 AMLL (Anni Music Lyrics Library) JSON 格式
    /// AMLL 是 JSON 格式的逐字歌词，常见于网易云/QQ音乐下载的歌词文件
    /// </summary>
    public static LrcLyrics? ParseAmll(string amllContent)
    {
        try
        {
            if (amllContent.Length > MaxLyricsParseSize)
            {
                System.Diagnostics.Debug.WriteLine($"[LyricsService] AMLL 文件过大（{amllContent.Length / 1024}KB），跳过解析");
                return null;
            }

            using var doc = JsonDocument.Parse(amllContent);
            var root = doc.RootElement;

            // AMLL 常见结构：{ "lyrics": [...], "version": "..." }
            if (!root.TryGetProperty("lyrics", out var lyricsArray) && 
                !root.TryGetProperty("data", out lyricsArray))
                return null;

            var result = new LrcLyrics();
            var lines = new List<LrcLyricLine>();

            foreach (var item in lyricsArray.EnumerateArray())
            {
                // 每行结构：{ "startTime": 7224, "endTime": 10500, "content": "...", "words": [...] }
                if (!item.TryGetProperty("startTime", out var startProp)) continue;
                var startMs = startProp.GetInt64();
                var start = TimeSpan.FromMilliseconds(startMs);

                var text = "";
                if (item.TryGetProperty("content", out var contentProp))
                    text = contentProp.GetString() ?? "";
                else if (item.TryGetProperty("lyric", out var lyricProp))
                    text = lyricProp.GetString() ?? "";

                var (amllAlignment, amllBacking) = ParseAmllRole(item);
                var singer = item.TryGetProperty("singer", out var singerProp)
                    ? singerProp.GetString()
                    : null;
                var line = new LrcLyricLine
                {
                    Timestamp = start,
                    Text = text,
                    // 解析 AMLL 的 role 字段（用于对唱布局）
                    Alignment = amllAlignment,
                    IsBackingVocal = amllBacking,
                    Role = singer,
                    SingerName = singer
                };

                // 解析逐字时间戳（AMLL 特有的 words 数组）
                if (item.TryGetProperty("words", out var wordsArray))
                {
                    var wordTimestamps = new List<WordTimestamp>();
                    foreach (var word in wordsArray.EnumerateArray())
                    {
                        if (!word.TryGetProperty("word", out var wordProp)) continue;
                        var wordText = wordProp.GetString() ?? "";
                        
                        if (!word.TryGetProperty("startTime", out var wsProp)) continue;
                        var ws = TimeSpan.FromMilliseconds(wsProp.GetInt64());

                        TimeSpan dur;
                        if (word.TryGetProperty("endTime", out var weProp))
                            dur = TimeSpan.FromMilliseconds(weProp.GetInt64()) - ws;
                        else if (word.TryGetProperty("duration", out var wdProp))
                            dur = TimeSpan.FromMilliseconds(wdProp.GetInt64());
                        else
                            dur = TimeSpan.FromMilliseconds(200);

                        if (dur <= TimeSpan.Zero) dur = TimeSpan.FromMilliseconds(200);

                        wordTimestamps.Add(new WordTimestamp
                        {
                            Word = wordText,
                            Start = ws,
                            Duration = dur
                        });
                    }
                    line.WordTimestamps = wordTimestamps.Count > 0 ? wordTimestamps : null;
                }

                lines.Add(line);
            }

            if (lines.Count == 0) return null;
            // 按时间戳排序（与 TTML/LRC 保持一致）
            result.Lines = lines.OrderBy(l => l.Timestamp).ToList();
            // 合并同拍对唱行（v1 左 + v2 右 → 单行左右分栏）
            MergeDuetLines(result);
            // 如果任何一行有非默认对齐方式或双文本，标记为逐行对齐
            result.HasPerLineAlignment = result.Lines.Any(l => l.Alignment != 1 || l.SecondaryText != null);
            // 合并翻译行（同时间戳的原文+翻译行合并）
            MergeTranslationLines(result);
            return result;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 解析 AMLL 每行的 role 字段，返回对齐方式与是否和声
    /// role 常见值：male / female / duet / chorus / v1 / v2 / v1000 等
    /// </summary>
    private static (int alignment, bool isBackingVocal) ParseAmllRole(JsonElement item)
    {
        if (!item.TryGetProperty("role", out var roleProp))
            return (1, false); // 默认居中

        var role = roleProp.GetString()?.ToLowerInvariant() ?? "";
        return InferRoleAlignment(role);
    }

    /// <summary>从文本中提取带结束标记的有限长度子串，避免把二进制尾部当作歌词</summary>
    private static string? ExtractBoundedSubstring(string text, int startIndex, string endMarker, int maxLength)
    {
        var maxEnd = Math.Min(text.Length, startIndex + maxLength);
        var endIndex = text.IndexOf(endMarker, startIndex, maxEnd - startIndex, StringComparison.OrdinalIgnoreCase);
        if (endIndex < 0) return null;
        return text.Substring(startIndex, endIndex + endMarker.Length - startIndex);
    }

    /// <summary>从文本中提取一个完整的 JSON 对象（带字符串转义处理）</summary>
    private static string? ExtractBoundedJson(string text, int startIndex, int maxLength)
    {
        if (startIndex < 0 || startIndex >= text.Length || text[startIndex] != '{') return null;
        var maxEnd = Math.Min(text.Length, startIndex + maxLength);
        int braceCount = 0;
        bool inString = false;
        bool escape = false;
        for (int i = startIndex; i < maxEnd; i++)
        {
            var c = text[i];
            if (escape) { escape = false; continue; }
            if (c == '\\') { escape = true; continue; }
            if (c == '"') { inString = !inString; continue; }
            if (inString) continue;
            if (c == '{') braceCount++;
            else if (c == '}') braceCount--;
            if (braceCount == 0)
                return text.Substring(startIndex, i - startIndex + 1);
        }
        return null;
    }

    /// <summary>
    /// 兜底：对音频文件做二进制扫描，搜索内嵌的 TTML/AMLL 歌词标记
    /// 适用于 M4A 自定义 atom 等 TagLibSharp 读不到的场景
    /// </summary>
    private LrcLyrics? TryScanFileForTtmlOrAmll(string filePath)
    {
        try
        {
            var fi = new FileInfo(filePath);
            if (!fi.Exists || fi.Length > 200 * 1024 * 1024) return null; // 跳过 >200MB 的文件

            using var fs = File.OpenRead(filePath);
            var len = (int)Math.Min(fs.Length, MaxLyricsFileSize); // 最多扫描前 2MB
            var buf = new byte[len];
            var read = fs.Read(buf, 0, len);
            var text = Encoding.UTF8.GetString(buf, 0, read);

            // 尝试 AMLL JSON：只提取一个完整 JSON 对象，避免把后续二进制当作 JSON
            var amllIdx = text.IndexOf("\"lyrics\"", StringComparison.Ordinal);
            if (amllIdx < 0) amllIdx = text.IndexOf("\"data\"", StringComparison.Ordinal);
            if (amllIdx > 0)
            {
                var sub = ExtractBoundedJson(text, amllIdx - 1, MaxLyricsParseSize);
                if (sub != null)
                {
                    try
                    {
                        var result = ParseAmll(sub);
                        if (result != null) return result;
                    }
                    catch { }
                }
            }

            // 尝试 TTML XML：只取 <tt ... </tt> 之间的内容，防止把音频二进制误识别为超长 TTML
            var ttmlIdx = text.IndexOf("<tt", StringComparison.OrdinalIgnoreCase);
            while (ttmlIdx >= 0)
            {
                var sub = ExtractBoundedSubstring(text, ttmlIdx, "</tt>", MaxLyricsParseSize);
                if (sub != null)
                {
                    try
                    {
                        var result = ParseTtml(sub);
                        if (result != null) return result;
                    }
                    catch { }
                }
                ttmlIdx = text.IndexOf("<tt", ttmlIdx + 1, StringComparison.OrdinalIgnoreCase);
            }

            return null;
        }
        catch { return null; }
    }
}


using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Core.Services;

/// <summary>歌词服务 —— partial 分域文件。</summary>
public partial class LyricsService
{
    private async Task<LrcLyrics?> TryReadLrcFileAsync(string songPath)
    {
        var dir = Path.GetDirectoryName(songPath) ?? "";
        var nameNoExt = Path.GetFileNameWithoutExtension(songPath);

        // 文本格式：.lrc → .ttml → .xml（编码检测读取）
        // 二进制加密格式：.krc（酷狗）/ .qrc（QQ），按字节读取后解密解析
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
                        Log.Debug("LyricsService", $"[LyricsService] 跳过超大歌词文件: {filePath} ({fileInfo.Length / 1024}KB)");
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
                            Log.Debug("LyricsService", $"[LyricsService] 跳过超大歌词文件: {filePath} ({bytes.Length / 1024}KB)");
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

        // 二进制加密歌词：.krc（酷狗）/ .qrc（QQ）
        foreach (var ext in new[] { ".krc", ".qrc" })
        {
            var filePath = Path.Combine(dir, nameNoExt + ext);
            byte[]? bytes = null;
            try
            {
                if (File.Exists(filePath))
                {
                    var fileInfo = new FileInfo(filePath);
                    if (fileInfo.Length <= MaxLyricsFileSize)
                        bytes = File.ReadAllBytes(filePath);
                }
            }
            catch { }
            if (bytes == null && FileBytesReaderAsync != null)
            {
                try { bytes = await FileBytesReaderAsync(filePath); }
                catch { }
            }
            if (bytes == null || bytes.Length == 0) continue;

            var parsed = ext == ".krc"
                ? await Task.Run(() => ParseKrc(bytes))
                : await Task.Run(() => ParseQrc(bytes));
            if (parsed != null)
            {
                Log.Debug("LyricsService", $"[LyricsService] 解析 {ext} 歌词成功: {parsed.Lines.Count} 行");
                return parsed;
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
                {
                    var parsed = await TryParseContentAsync(content);
                    if (parsed != null) return parsed;
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
}

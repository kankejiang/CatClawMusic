using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Text.Json;

namespace CatClawMusic.Core.Services;

/// <summary>歌词来源模式（歌词设置中可选，由 UI 层同步到 <see cref="LyricsService.LyricSourceMode"/>）</summary>
public enum LyricSourceMode
{
    /// <summary>在线：优先在线匹配（网易云三流歌词等），失败回退本地歌词</summary>
    Online = 0,
    /// <summary>本地自动：同名外挂 .lrc 优先，无则读内嵌歌词</summary>
    LocalAuto = 1,
    /// <summary>本地内嵌：只读音频文件内嵌歌词</summary>
    LocalEmbedded = 2,
    /// <summary>本地外挂：只读同名 .lrc/.ttml 文件</summary>
    LocalExternal = 3
}

/// <summary>
/// 歌词服务实现（LRC/TTML/AMLL 解析 + 多源查找 + KTV 索引）。
/// 解析器按格式拆分在 LyricsService.*.cs partial 文件：
/// FileReader（编码检测/content://）/ Lrc / Ttml / Splitter（双语）/ Amll。
/// </summary>
public partial class LyricsService : ILyricsService
{
    /// <summary>插件管理器（可选，由 UI 层设置）</summary>
    public IPluginManager? PluginManager { get; set; }

    /// <summary>网络音乐服务工厂（可选，由 UI 层设置，用于 Navidrome 等远程歌词获取）</summary>
    public Func<INetworkMusicService?>? NetworkMusicServiceFactory { get; set; }

    /// <summary>当前歌词来源模式（默认在线）。由歌词设置页切换并持久化。</summary>
    public static LyricSourceMode LyricSourceMode { get; set; } = LyricSourceMode.Online;

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
    /// 获取歌词（优先级：Navidrome API > 同名 .lrc > 嵌入歌词 > 插件）
    /// 注意：Navidrome API 必须先于内嵌歌词，否则会触发 RemoteUrlStreamOpener
    /// 下载整个音频文件（最大 50MB）到 LOS 堆，导致大量 GC。
    /// </summary>
    public async Task<LrcLyrics?> GetLyricsAsync(Song song)
    {
        string fpPreview;
        if (song.FilePath == null)
            fpPreview = "null";
        else if (song.FilePath.StartsWith("http"))
            fpPreview = "http://...";
        else
            fpPreview = song.FilePath.Length <= 40 ? song.FilePath : song.FilePath[..40];
        Log.Debug("LyricsService", $"[Lyrics] GetLyricsAsync: Protocol={song.Protocol}, RemoteId={song.RemoteId ?? "null"}, FilePath={fpPreview}");

        // 本地模式：只从本地文件取歌词（自动/内嵌/外挂），不发起任何联网请求
        if (LyricSourceMode != LyricSourceMode.Online)
        {
            var local = await GetLocalLyricsAsync(song,
                skipEmbedded: LyricSourceMode == LyricSourceMode.LocalExternal,
                preferEmbedded: LyricSourceMode == LyricSourceMode.LocalEmbedded,
                skipExternal: LyricSourceMode == LyricSourceMode.LocalEmbedded);
            if (local != null)
                Log.Debug("LyricsService", $"[Lyrics] 本地模式({LyricSourceMode})命中 {local.Lines.Count} 行");
            return local;
        }

        // Navidrome/Subsonic: 优先通过 API 获取歌词（避免下载整个音频文件读内嵌歌词）
        if (song.Protocol == ProtocolType.Navidrome && !string.IsNullOrEmpty(song.RemoteId))
        {
            try
            {
                var networkSvc = NetworkMusicServiceFactory?.Invoke();
                if (networkSvc != null)
                {
                    var profiles = await networkSvc.GetProfilesAsync();
                    var profile = profiles.FirstOrDefault(p => p.Protocol == ProtocolType.Navidrome);
                    if (profile != null)
                    {
                        var lrcText = await networkSvc.GetLyricsAsync(song.RemoteId, profile);
                        if (!string.IsNullOrWhiteSpace(lrcText))
                        {
                            var parsed = await Task.Run(() => TryParseLyrics(lrcText));
                            if (parsed != null) return parsed;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("LyricsService", $"[Lyrics] Navidrome API 获取失败: {ex.Message}");
            }
        }

        // 在线音乐插件歌词：临时 Song 的 RemoteId 形如 "{platform}:{onlineId}"（搜索/发现页播放时构造）。
        // 命中则优先路由到对应 IOnlineMusicPlugin 取歌词（原文+翻译合并），避免对 http 直链做本地内嵌探测。
        if (PluginManager != null && !string.IsNullOrEmpty(song.RemoteId) && song.RemoteId.Contains(':'))
        {
            var parts = song.RemoteId.Split(':', 2);
            var platform = parts[0];
            var onlineId = parts.Length > 1 ? parts[1] : null;
            if (!string.IsNullOrEmpty(onlineId))
            {
                // 网易云：宿主直连歌词（原文+译文+罗马音，yrc/lrc/tlyric/romalrc 直接解析并入），
                // 不依赖插件；失败时静默回退插件路径（保持原有能力）。
                if (string.Equals(platform, "netease", StringComparison.OrdinalIgnoreCase)
                    && long.TryParse(onlineId, out var neteaseSongId))
                {
                    var hostLyrics = await NetEaseLyricsService.GetLyricsAsync(neteaseSongId);
                    if (hostLyrics != null && hostLyrics.Lines.Count > 0)
                    {
                        Log.Debug("LyricsService", $"[Lyrics] 网易云直连歌词成功: {hostLyrics.Lines.Count} 行");
                        return hostLyrics;
                    }
                }

                foreach (var provider in PluginManager.GetEnabledPlugins<IOnlineMusicPlugin>())
                {
                    if (!string.Equals(provider.PlatformName, platform, StringComparison.OrdinalIgnoreCase)) continue;
                    try
                    {
                        var onlineSong = new OnlineSong
                        {
                            Id = onlineId,
                            Platform = platform,
                            Title = song.Title,
                            Artist = song.Artist,
                            Album = song.Album
                        };
                        var onlineLyrics = await provider.GetLyricsWithRomaAsync(onlineSong);
                        if (onlineLyrics != null && !string.IsNullOrWhiteSpace(onlineLyrics.Value.Lrc))
                        {
                            // 原文/译文/罗马音三流分别解析后按时间戳合并（lx-music 音源同款：
                            // lrc/tlyric/romalrc 独立歌词流）。字符串拼接依赖"时间戳精确相等"
                            // 合并，译文时间戳与原文差几毫秒就合并失败 → 译文错位成独立行。
                            var parsed = await Task.Run(() => TryParseLyrics(onlineLyrics.Value.Lrc));
                            if (parsed != null)
                            {
                                if (!string.IsNullOrWhiteSpace(onlineLyrics.Value.TLrc))
                                    parsed.TranslationLines = await Task.Run(() =>
                                        TryParseLyrics(onlineLyrics.Value.TLrc)?.Lines);
                                if (!string.IsNullOrWhiteSpace(onlineLyrics.Value.RLrc))
                                    parsed.RomaLines = await Task.Run(() =>
                                        TryParseLyrics(onlineLyrics.Value.RLrc)?.Lines);
                                MergeExtendedLines(parsed);
                                return parsed;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("LyricsService", $"[Lyrics] 在线插件歌词获取失败: {ex.Message}");
                    }
                }
            }
        }

        var lyrics = await GetLocalLyricsAsync(song);
        if (lyrics != null) return lyrics;

        // 在线模式：本地无歌词时按标题+歌手去网易云匹配歌词（宿主直连，原文+译文/罗马音已并入）
        if (!string.IsNullOrWhiteSpace(song.Title))
        {
            try
            {
                var netease = await NetEaseLyricsService.MatchLocalSongAsync(song.Title, song.Artist);
                if (netease != null && netease.Lines.Count > 0)
                {
                    Log.Debug("LyricsService", $"[Lyrics] 网易云匹配歌词成功: {song.Title} ({netease.Lines.Count} 行)");
                    return netease;
                }
            }
            catch (Exception ex)
            {
                Log.Debug("LyricsService", $"[Lyrics] 网易云匹配歌词失败: {ex.Message}");
            }
        }

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
    /// <param name="skipEmbedded">是否跳过嵌入歌词（仅外挂模式）</param>
    /// <param name="preferEmbedded">是否内嵌优先（内嵌模式）</param>
    /// <param name="skipExternal">是否跳过外挂歌词查找（仅内嵌模式）</param>
    public async Task<LrcLyrics?> GetLocalLyricsAsync(Song song, bool skipEmbedded = false, bool preferEmbedded = false, bool skipExternal = false)
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
            Log.Debug("LyricsService", $"[Lyrics] isRemoteUrl=true, Protocol={song.Protocol}, skipEmbedded={skipEmbedded}");
            if (skipEmbedded) return null;
            // Navidrome: 内嵌歌词需要下载整个音频文件（最大 10MB）到 LOS 堆，
            // 代价过高且 API 已是规范来源，跳过。WebDAV/SMB 的直链 stream URL 仍尝试。
            if (song.Protocol == ProtocolType.Navidrome)
            {
                Log.Debug("LyricsService", "[Lyrics] 跳过 Navidrome 内嵌歌词");
                return null;
            }
            Log.Debug("LyricsService", "[Lyrics] 调用 ReadEmbeddedLyrics (isRemoteUrl=true)");
            var embeddedLyrics = await Task.Run(() => ReadEmbeddedLyrics(songPath, isContentUri: false, isRemoteUrl: true));
            Log.Debug("LyricsService", $"[Lyrics] ReadEmbeddedLyrics 返回: {(embeddedLyrics != null ? $"{embeddedLyrics.Length} 字符" : "null")}");
            if (!string.IsNullOrWhiteSpace(embeddedLyrics))
            {
                var parsed = await Task.Run(() => TryParseLyrics(embeddedLyrics));
                if (parsed != null) return parsed;
            }
            return null;
        }

        // 优先使用已知的 LyricsPath（SAF 扫描时已匹配的歌词 content:// URI 或文件路径）
        if (!skipExternal && !string.IsNullOrEmpty(song.LyricsPath))
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

            // 内嵌模式（skipExternal）：外挂歌词查找整体跳过
            if (skipExternal) return null;

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
            if (!skipExternal)
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
            }

            // 方式2：从 SAF document URI 提取真实文件路径，再用文件系统查找歌词
            if (!skipExternal)
            {
                var realPath = TryConvertContentUriToPath(songPath);
                if (!string.IsNullOrEmpty(realPath))
                {
                    var lrcLyrics = await TryReadLrcFileAsync(realPath);
                    if (lrcLyrics != null) return lrcLyrics;
                }
            }
        }
        else if (!string.IsNullOrEmpty(songPath) && !skipExternal)
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
            Log.Debug("LyricsService", $"[Lyrics] ReadEmbeddedLyrics isRemoteUrl, RemoteUrlStreamOpener={(RemoteUrlStreamOpener != null ? "已设置" : "null")}");
            if (RemoteUrlStreamOpener != null)
            {
                try
                {
                    var spPreview = songPath?[..Math.Min(60, songPath?.Length ?? 0)] ?? "";
                    Log.Debug("LyricsService", $"[Lyrics] 调用 RemoteUrlStreamOpener: {spPreview}...");
                    using var stream = RemoteUrlStreamOpener(songPath);
                    Log.Debug("LyricsService", $"[Lyrics] RemoteUrlStreamOpener 返回: {(stream != null ? $"{stream.Length} bytes" : "null")}");
                    if (stream != null)
                    {
                        var remoteLyrics = TagReader.ReadEmbeddedLyricsFromStream(stream, GetFileNameFromUrl(songPath));
                        if (!string.IsNullOrWhiteSpace(remoteLyrics))
                        {
                            Log.Debug("LyricsService", $"[Lyrics] 远程流内嵌歌词读取成功 (长度={remoteLyrics.Length})");
                            return remoteLyrics;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Debug("LyricsService", $"[Lyrics] RemoteUrlStreamOpener 读取异常: {ex.Message}");
                }
            }
            return null;
        }

        var lyrics = TagReader.ReadEmbeddedLyrics(songPath);
        if (!string.IsNullOrWhiteSpace(lyrics))
        {
            Log.Debug("LyricsService", $"[Lyrics] 内嵌歌词读取成功: {songPath} (长度={lyrics.Length})");
            return lyrics;
        }
        Log.Debug("LyricsService", $"[Lyrics] 内嵌歌词为空: {songPath}");

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
                            Log.Debug("LyricsService", $"[Lyrics] 流内嵌歌词读取成功 (长度={lyrics.Length})");
                            return lyrics;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Debug("LyricsService", $"[Lyrics] AndroidFileStreamOpener 读取异常: {ex.Message}");
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
    /// 支持 [mm:ss.xx]、[mm:ss.xxx]、[mm:ss]
    /// </summary>
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


using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

/// <summary>
/// 网络音乐服务——按协议类型分发
/// </summary>
public class NetworkMusicService : INetworkMusicService
{
    /// <summary>
    /// 数据库操作实例
    /// </summary>
    private readonly MusicDatabase _db;

    /// <summary>
    /// Subsonic/Navidrome API 客户端
    /// </summary>
    private readonly ISubsonicService _subsonic;

    /// <summary>
    /// WebDAV 文件服务
    /// </summary>
    private readonly INetworkFileService _webDav;
    /// <summary>SMB 文件服务</summary>
    private readonly INetworkFileService _smb;

    /// <summary>限制并发元数据下载的信号量（最多 8 个并行任务），避免压垮远程服务器</summary>
    private static readonly SemaphoreSlim ScanSemaphore = new(8, 8);
    /// <summary>控制递归目录扫描的并发数（OpenList 等不支持深度 PROPFIND 的服务器）</summary>
    private static readonly SemaphoreSlim DirScanSemaphore = new(4, 4);

    /// <summary>OpenList stream URL 缓存：filePath → (url, expiry)，避免每次播放重复 API 调用</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string url, DateTime expiry)> _streamUrlCache = new();
    /// <summary>OpenList stream URL 缓存有效期（5 分钟），过期后重新请求 /api/fs/get</summary>
    private static readonly TimeSpan StreamUrlCacheTtl = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 创建网络音乐服务实例
    /// </summary>
    /// <param name="db">数据库操作实例</param>
    /// <param name="subsonic">Subsonic/Navidrome API 客户端</param>
    /// <param name="webDav">WebDAV 文件服务</param>
    /// <param name="smb">SMB 文件服务</param>
    public NetworkMusicService(MusicDatabase db, ISubsonicService subsonic, INetworkFileService webDav, INetworkFileService smb)
    {
        _db = db;
        _subsonic = subsonic;
        _webDav = webDav;
        _smb = smb;
    }

    /// <summary>
    /// 获取所有连接配置
    /// </summary>
    /// <returns>连接配置列表</returns>
    public async Task<List<ConnectionProfile>> GetProfilesAsync()
    {
        await _db.EnsureInitializedAsync();
        return await _db.GetConnectionProfilesAsync();
    }

    /// <summary>
    /// 解析 WebDAV 播放 URL：自动检测 OpenList 服务器，修复 /dav 前缀或获取签名 raw_url。
    /// 用于播放时动态修正旧的错误URL（缺少 /dav 前缀）或获取OpenList签名链接。
    /// </summary>
    /// <param name="url">原始URL（可能是带认证信息的 http://user:pass@host:port/path）</param>
    /// <returns>可直接播放的URL；如果不需要修复则返回原URL</returns>
    public async Task<string?> ResolveWebDavPlaybackUrlAsync(string url)
    {
        if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return null;

        try
        {
            var uri = new Uri(url);
            var host = uri.Host;
            var port = uri.Port;

            // 从URL提取认证信息
            string user = "";
            string pass = "";
            if (!string.IsNullOrEmpty(uri.UserInfo))
            {
                var parts = uri.UserInfo.Split(':');
                if (parts.Length >= 1) user = Uri.UnescapeDataString(parts[0]);
                if (parts.Length >= 2) pass = Uri.UnescapeDataString(parts[1]);
            }

            // 如果路径已经包含 /dav/ 或 /webdav/，认为URL已正确，直接尝试OpenList签名URL获取
            var path = uri.AbsolutePath;
            bool hasDavPrefix = path.StartsWith("/dav/", StringComparison.OrdinalIgnoreCase)
                             || path.Equals("/dav", StringComparison.OrdinalIgnoreCase)
                             || path.StartsWith("/webdav/", StringComparison.OrdinalIgnoreCase)
                             || path.Equals("/webdav", StringComparison.OrdinalIgnoreCase);

            // 查找匹配的ConnectionProfile
            await _db.EnsureInitializedAsync();
            var profiles = await _db.GetConnectionProfilesAsync();
            var matchingProfile = profiles.FirstOrDefault(p =>
                p.Protocol == ProtocolType.WebDAV && p.IsEnabled
                && string.Equals(p.Host.Trim('/'), host, StringComparison.OrdinalIgnoreCase)
                && p.Port == port);

            ConnectionProfile profileToUse;
            if (matchingProfile != null)
            {
                profileToUse = matchingProfile;
            }
            else
            {
                // 数据库中无匹配，从URL构建临时profile用于检测
                profileToUse = new ConnectionProfile
                {
                    Protocol = ProtocolType.WebDAV,
                    Host = host,
                    Port = port,
                    UserName = user,
                    Password = pass,
                    UseHttps = uri.Scheme == "https",
                    BasePath = "/"
                };
            }

            // 配置WebDAV服务并检测服务器类型
            _webDav.Configure(profileToUse);
            if (_webDav is WebDavService wds)
            {
                await wds.EnsureDetectedAsync();

                // 提取虚拟路径（去掉 /dav 前缀后的实际文件路径）
                string virtualPath;
                if (hasDavPrefix)
                {
                    // URL已有dav前缀，提取后面的部分作为虚拟路径
                    virtualPath = path;
                    var davPrefix = wds.DavPrefix;
                    if (!string.IsNullOrEmpty(davPrefix) && path.StartsWith(davPrefix + "/", StringComparison.OrdinalIgnoreCase))
                        virtualPath = path[davPrefix.Length..];
                    else if (path.StartsWith("/dav/", StringComparison.OrdinalIgnoreCase))
                        virtualPath = path[4..];
                    else if (path.StartsWith("/webdav/", StringComparison.OrdinalIgnoreCase))
                        virtualPath = path[7..];
                }
                else
                {
                    virtualPath = path;
                }

                // 对于OpenList，尝试获取签名raw_url
                if (wds.CurrentServerType == WebDavServerType.OpenList)
                {
                    try
                    {
                        var rawUrl = await wds.GetOpenListStreamUrlAsync(virtualPath);
                        if (!string.IsNullOrEmpty(rawUrl))
                        {
                            System.Diagnostics.Debug.WriteLine($"[URL Resolver] OpenList raw_url: {rawUrl[..Math.Min(80, rawUrl.Length)]}...");
                            return rawUrl;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[URL Resolver] OpenList raw_url 获取失败: {ex.Message}");
                    }
                }

                // 如果URL缺少dav前缀且探测到需要前缀，用BuildStreamUrl修复
                if (!hasDavPrefix && !string.IsNullOrEmpty(wds.DavPrefix))
                {
                    var fixedUrl = wds.BuildStreamUrl(virtualPath);
                    System.Diagnostics.Debug.WriteLine($"[URL Resolver] 修复URL: {url[..Math.Min(60, url.Length)]}... → {fixedUrl[..Math.Min(80, fixedUrl.Length)]}...");
                    return fixedUrl;
                }
            }

            return null; // 无需修复
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[URL Resolver] 解析失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 扫描网络音乐源，按协议类型分发到 Subsonic 或 WebDAV/SMB 扫描
    /// </summary>
    /// <param name="profile">连接配置</param>
    /// <param name="progress">进度报告回调</param>
    /// <param name="songBatchCallback">每批次歌曲扫描完成后的回调</param>
    /// <returns>扫描到的所有歌曲列表</returns>
    public async Task<List<Song>> ScanAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null,
        Action<List<Song>>? songBatchCallback = null,
        bool quickScan = false)
    {
        try { await _db.EnsureInitializedAsync(); } catch { }

        var scannedRemoteIds = new HashSet<string>();
        var allSongs = new List<Song>();

        if (profile.Protocol == ProtocolType.Navidrome)
        {
            var scanner = new MusicScanner(_db, songBatchCallback);
            allSongs = await _subsonic.GetSongsAsync(profile, progress, async (batch) =>
            {
                try
                {
                    foreach (var s in batch)
                    {
                        if (!string.IsNullOrEmpty(s.RemoteId)) scannedRemoteIds.Add(s.RemoteId);
                        await scanner.AddSongAsync(s);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] 增量入库失败: {ex.Message}");
                }
                songBatchCallback?.Invoke(batch);
            });
            await scanner.FlushAsync();
        }
        else if (profile.Protocol == ProtocolType.WebDAV)
        {
            var (newSongs, allFoundIds) = await ScanWebDavAsync(profile, songBatchCallback, progress);
            allSongs = newSongs;
            foreach (var id in allFoundIds)
            {
                if (!string.IsNullOrEmpty(id)) scannedRemoteIds.Add(id);
            }
        }
        else if (profile.Protocol == ProtocolType.SMB)
        {
            var (newSongs, allFoundIds) = await ScanSmbAsync(profile, songBatchCallback, progress, quickScan);
            allSongs = newSongs;
            foreach (var id in allFoundIds)
            {
                if (!string.IsNullOrEmpty(id)) scannedRemoteIds.Add(id);
            }
        }

        try
        {
            var source = profile.Protocol == ProtocolType.SMB ? SongSource.SMB : SongSource.WebDAV;
            var removed = await _db.RemoveStaleSongsAsync(source, new HashSet<string>(), scannedRemoteIds);
            if (removed > 0)
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 清理 {removed} 首已移除的网络歌曲 ({source})");
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[CatClaw] 清理旧网络歌曲失败: {ex.Message}"); }

        return allSongs;
    }

    /// <summary>
    /// 按协议类型搜索网络音乐（当前仅支持 Navidrome）
    /// </summary>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="profile">连接配置</param>
    /// <returns>匹配的歌曲列表</returns>
    public async Task<List<Song>> SearchAsync(string keyword, ConnectionProfile profile)
    {
        return profile.Protocol switch
        {
            ProtocolType.Navidrome => await _subsonic.SearchAsync(keyword, profile),
            _ => new List<Song>()
        };
    }

    /// <summary>
    /// 下载文件头部数据用于读取标签信息的大小（256KB，平衡扫描速度和封面质量）
    /// </summary>
    private const int TagHeadSize = 256 * 1024;

    /// <summary>
    /// 下载远程文件的头部数据用于标签解析，失败时回退到完整下载
    /// </summary>
    private async Task<MemoryStream?> DownloadHeadAsync(string remotePath)
    {
        var head = await _webDav.OpenReadRangeAsync(remotePath, 0, TagHeadSize);
        if (head.Length > 0)
            return new MemoryStream(head);

        try
        {
            using var stream = await _webDav.OpenReadAsync(remotePath);
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }
        catch { return null; }
    }

    /// <summary>
    /// 下载 SMB 远程文件的头部数据用于标签解析，失败时回退到完整下载。
    /// </summary>
    /// <param name="remotePath">SMB 远程文件路径。</param>
    /// <param name="profile">连接配置，用于初始化 SMB 客户端。</param>
    /// <returns>包含文件头数据的内存流，失败时返回 null。</returns>
    private async Task<MemoryStream?> DownloadSmbHeadAsync(string remotePath, ConnectionProfile profile)
    {
        _smb.Configure(profile);
        var head = await _smb.OpenReadRangeAsync(remotePath, 0, TagHeadSize);
        if (head.Length > 0)
            return new MemoryStream(head);

        try
        {
            using var stream = await _smb.OpenReadAsync(remotePath);
            var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;
            return ms;
        }
        catch { return null; }
    }

    /// <summary>
    /// 获取歌曲封面图流，按协议类型分发
    /// </summary>
    /// <param name="songId">歌曲 ID 或文件路径</param>
    /// <param name="profile">连接配置</param>
    /// <returns>封面图流，失败时返回 null</returns>
    public async Task<Stream?> GetCoverAsync(string songId, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
        {
            var bytes = await _subsonic.GetCoverArtAsync(songId, profile);
            return bytes != null ? new MemoryStream(bytes) : null;
        }
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            _webDav.Configure(profile);
            if (_webDav is WebDavService wdsEnsure) await wdsEnsure.EnsureDetectedAsync();

            // OpenList: 使用 raw_url (CDN 直链) 下载文件头，WebDAV 端点 302 到 CDN 会拒绝 Basic Auth
            var isOpenList = (WebDavServerType)profile.ServerType == WebDavServerType.OpenList;
            if (!isOpenList && _webDav is WebDavService wdsCheck2 && wdsCheck2.CurrentServerType == WebDavServerType.OpenList)
                isOpenList = true;

            if (isOpenList && _webDav is WebDavService openListService)
            {
                try
                {
                    // 优先复用播放用的 /d/ URL（已缓存，无需额外 API 调用）
                    string? downloadUrl = null;
                    if (_streamUrlCache.TryGetValue(songId, out var cached) && cached.expiry > DateTime.UtcNow)
                    {
                        downloadUrl = cached.url;
                        System.Diagnostics.Debug.WriteLine("[CatClaw] Cover: 复用播放缓存 URL");
                    }

                    // 缓存不可用：获取 CDN raw_url
                    if (string.IsNullOrEmpty(downloadUrl))
                        downloadUrl = await openListService.GetOpenListDownloadUrlAsync(songId);

                    if (!string.IsNullOrEmpty(downloadUrl))
                    {
                        using var httpClient = new HttpClient(new SocketsHttpHandler
                        {
                            SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                            {
                                RemoteCertificateValidationCallback = (_, _, _, _) => true
                            },
                            ConnectTimeout = TimeSpan.FromSeconds(10),
                            AllowAutoRedirect = true
                        })
                        { Timeout = TimeSpan.FromSeconds(15) };

                        var rangeReq = new HttpRequestMessage(HttpMethod.Get, downloadUrl);
                        rangeReq.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, TagHeadSize - 1);
                        var rangeResp = await httpClient.SendAsync(rangeReq, HttpCompletionOption.ResponseHeadersRead);
                        if (rangeResp.IsSuccessStatusCode)
                        {
                            var ms = new MemoryStream();
                            await rangeResp.Content.CopyToAsync(ms);
                            ms.Position = 0;
                            try
                            {
                                var coverBytes = TagReader.ExtractCoverFromStream(ms, songId);
                                if (coverBytes != null)
                                    return new MemoryStream(coverBytes);
                            }
                            finally { ms.Dispose(); }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[CatClaw] OpenList 封面提取失败: {ex.Message}");
                }
            }

            try
            {
                var ms = await DownloadHeadAsync(songId);
                if (ms != null)
                {
                    try
                    {
                        var coverBytes = TagReader.ExtractCoverFromStream(ms, songId);
                        if (coverBytes != null)
                            return new MemoryStream(coverBytes);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] WebDAV 封面提取失败: {ex.Message}");
            }
        }
        if (profile.Protocol == ProtocolType.SMB)
        {
            try
            {
                var ms = await DownloadSmbHeadAsync(songId, profile);
                if (ms != null)
                {
                    try
                    {
                        var coverBytes = TagReader.ExtractCoverFromStream(ms, songId);
                        if (coverBytes != null)
                            return new MemoryStream(coverBytes);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SMB 封面提取失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 获取远程歌曲歌词，优先查找外部 .lrc 文件，回退到嵌入标签
    /// </summary>
    /// <param name="remotePath">远程文件路径</param>
    /// <param name="profile">连接配置</param>
    /// <returns>歌词文本，失败时返回 null</returns>
    public async Task<string?> GetLyricsAsync(string remotePath, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
        {
            // Navidrome: 通过 Subsonic API getLyricsBySongId 获取歌词
            // remotePath 对于 Navidrome 歌曲实际上是 stream URL，使用 RemoteId 作为 songId
            return await _subsonic.GetLyricsAsync(remotePath, profile);
        }
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            _webDav.Configure(profile);
            if (_webDav is WebDavService wdsLyrics) await wdsLyrics.EnsureDetectedAsync();

            var lastDot = remotePath.LastIndexOf('.');
            if (lastDot > 0)
            {
                var lrcPath = remotePath.Substring(0, lastDot) + ".lrc";
                try
                {
                    using var lrcStream = await _webDav.OpenReadAsync(lrcPath);
                    using var reader = new StreamReader(lrcStream);
                    var lrcText = await reader.ReadToEndAsync();
                    System.Diagnostics.Debug.WriteLine($"[WebDAV] 读取歌词文件 {lrcPath}，长度={lrcText?.Length ?? 0}，前200字符={lrcText?[..Math.Min(200, lrcText?.Length ?? 0)]?.Replace('\n', ' ')}");
                    if (!string.IsNullOrWhiteSpace(lrcText))
                        return lrcText;
                }
                catch { }
            }

            try
            {
                var ms = await DownloadHeadAsync(remotePath);
                if (ms != null)
                {
                    try
                    {
                        return TagReader.ReadEmbeddedLyricsFromStream(ms, remotePath);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] WebDAV 歌词提取失败: {ex.Message}");
            }
        }
        if (profile.Protocol == ProtocolType.SMB)
        {
            _smb.Configure(profile);
            var lastDot = remotePath.LastIndexOf('.');
            if (lastDot > 0)
            {
                var lrcPath = remotePath.Substring(0, lastDot) + ".lrc";
                try
                {
                    using var lrcStream = await _smb.OpenReadAsync(lrcPath);
                    using var reader = new StreamReader(lrcStream);
                    var lrcText = await reader.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(lrcText))
                        return lrcText;
                }
                catch { }
            }
            try
            {
                var ms = await DownloadSmbHeadAsync(remotePath, profile);
                if (ms != null)
                {
                    try
                    {
                        return TagReader.ReadEmbeddedLyricsFromStream(ms, remotePath);
                    }
                    finally { ms.Dispose(); }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] SMB 歌词提取失败: {ex.Message}");
            }
        }
        return null;
    }

    /// <summary>
    /// 从远程文件头部数据中解析歌曲元数据，更新歌曲信息
    /// </summary>
    /// <param name="song">待更新元数据的歌曲对象</param>
    /// <param name="profile">连接配置</param>
    /// <returns>更新后的歌曲对象，失败时返回 null</returns>
    public async Task<Song?> FetchSongMetadataAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.WebDAV)
        {
            var result = await FetchWebDavMetadataAsync(song, profile);
            if (result != null) return result;
        }
        if (profile.Protocol == ProtocolType.SMB)
        {
            var result = await FetchSmbMetadataAsync(song, profile);
            if (result != null) return result;
        }
        return null;
    }

    /// <summary>
    /// 后台回填网络歌曲元数据：找到所有缺少元数据的歌曲（快速扫描入库的），
    /// 逐批从远程服务器下载标签信息并更新数据库。
    /// </summary>
    public async Task BackfillMetadataAsync(ConnectionProfile profile,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        try { await _db.EnsureInitializedAsync(); } catch { }

        var source = profile.Protocol switch
        {
            ProtocolType.SMB => SongSource.SMB,
            ProtocolType.WebDAV => SongSource.WebDAV,
            ProtocolType.Navidrome => SongSource.WebDAV,
            _ => (SongSource?)null
        };
        if (source == null) return;

        // 找到所有缺少元数据的歌曲（快速扫描时 Artist 设为"未知艺术家"、Duration=0）
        var allCached = await _db.GetCachedNetworkSongsAsync();
        var needsBackfill = allCached
            .Where(s => s.Source == source.Value
                && (s.Duration <= 0
                    || string.IsNullOrWhiteSpace(s.Artist)
                    || s.Artist == "未知艺术家"))
            .ToList();

        if (needsBackfill.Count == 0) return;

        var total = needsBackfill.Count;
        var done = 0;
        progress?.Report((0, total, $"正在补全元数据 0/{total}"));

        var tasks = needsBackfill.Select(song => Task.Run(async () =>
        {
            await ScanSemaphore.WaitAsync();
            try
            {
                var tagged = await FetchSongMetadataAsync(song, profile);
                if (tagged != null)
                {
                    if (!string.IsNullOrWhiteSpace(tagged.Title) && tagged.Title != song.Title)
                        song.Title = tagged.Title;
                    if (!string.IsNullOrWhiteSpace(tagged.Artist) && tagged.Artist != "未知艺术家")
                        song.Artist = tagged.Artist;
                    if (!string.IsNullOrWhiteSpace(tagged.Album) && tagged.Album != "未知专辑")
                        song.Album = tagged.Album;
                    if (tagged.Duration > 0) song.Duration = tagged.Duration;
                    if (tagged.Bitrate > 0) song.Bitrate = tagged.Bitrate;
                    if (tagged.Year > 0) song.Year = tagged.Year;
                    if (tagged.TrackNumber > 0) song.TrackNumber = tagged.TrackNumber;
                    song.Genre = tagged.Genre;
                    await _db.SaveSongAsync(song);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[CatClaw] 元数据回填失败: {song.FilePath}, {ex.Message}");
            }
            finally
            {
                ScanSemaphore.Release();
                var d = Interlocked.Increment(ref done);
                if (progress != null && (d % 10 == 0 || d == total))
                    progress.Report((d, total, $"正在补全元数据 {d}/{total}"));
            }
        }));

        await Task.WhenAll(tasks);
        progress?.Report((total, total, $"元数据补全完成，共 {total} 首"));
    }

    /// <summary>
    /// 从 WebDAV 远程文件头部数据中解析歌曲元数据（标题、艺术家、专辑、时长等）并更新歌曲对象。
    /// 内部会自动处理 OpenList 服务器类型检测与缓存。
    /// </summary>
    /// <param name="song">待更新元数据的歌曲对象，需包含 RemoteId 或 CoverArtPath。</param>
    /// <param name="profile">WebDAV 连接配置。</param>
    /// <returns>更新后的歌曲对象；解析失败或无远程路径时返回 null。</returns>
    private async Task<Song?> FetchWebDavMetadataAsync(Song song, ConnectionProfile profile)
    {
        var remotePath = song.RemoteId ?? song.CoverArtPath;
        if (string.IsNullOrEmpty(remotePath)) return null;

        _webDav.Configure(profile);
        if (_webDav is WebDavService wdsMeta) await wdsMeta.EnsureDetectedAsync();

        try
        {
            var ms = await DownloadHeadAsync(remotePath);
            if (ms != null)
            {
                try
                {
                    var decodedRemotePath = Uri.UnescapeDataString(remotePath);
                    var tagSong = TagReader.ReadFromStream(ms, song.FilePath, decodedRemotePath, song.FileSize);
                    if (tagSong != null)
                    {
                        if (!string.IsNullOrWhiteSpace(tagSong.Title) && tagSong.Title != song.Title)
                        {
                            var tagTitleDecoded = Uri.UnescapeDataString(tagSong.Title);
                            song.Title = tagTitleDecoded != song.Title ? tagTitleDecoded : song.Title;
                        }
                        song.Artist = !string.IsNullOrWhiteSpace(tagSong.Artist) && tagSong.Artist != "未知艺术家" ? tagSong.Artist : song.Artist;
                        // 规范化艺术家名：拆分多值字符串，取第一位作为主艺术家
                        if (!string.IsNullOrWhiteSpace(song.Artist))
                        {
                            var artistNames = MusicUtility.SplitArtistNames(song.Artist);
                            if (artistNames.Count > 0)
                                song.Artist = artistNames[0];
                        }
                        song.Album = !string.IsNullOrWhiteSpace(tagSong.Album) && tagSong.Album != "未知专辑" ? tagSong.Album : song.Album;
                        song.Duration = tagSong.Duration > 0 ? tagSong.Duration : song.Duration;
                        song.Bitrate = tagSong.Bitrate > 0 ? tagSong.Bitrate : song.Bitrate;
                        song.Year = tagSong.Year > 0 ? tagSong.Year : song.Year;
                        song.TrackNumber = tagSong.TrackNumber > 0 ? tagSong.TrackNumber : song.TrackNumber;
                        song.Genre = tagSong.Genre;
                        return song;
                    }
                }
                finally { ms.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] WebDAV 元数据获取失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 从 SMB 远程文件头部数据中解析歌曲元数据并更新歌曲对象。
    /// </summary>
    /// <param name="song">待更新元数据的歌曲对象，需包含 RemoteId 或 CoverArtPath。</param>
    /// <param name="profile">SMB 连接配置。</param>
    /// <returns>更新后的歌曲对象；解析失败或无远程路径时返回 null。</returns>
    private async Task<Song?> FetchSmbMetadataAsync(Song song, ConnectionProfile profile)
    {
        var remotePath = song.RemoteId ?? song.CoverArtPath;
        if (string.IsNullOrEmpty(remotePath)) return null;

        _smb.Configure(profile);
        try
        {
            var ms = await DownloadSmbHeadAsync(remotePath, profile);
            if (ms != null)
            {
                try
                {
                    var tagSong = TagReader.ReadFromStream(ms, song.FilePath, remotePath, song.FileSize);
                    if (tagSong != null)
                    {
                        if (!string.IsNullOrWhiteSpace(tagSong.Title) && tagSong.Title != song.Title)
                            song.Title = tagSong.Title;
                        song.Artist = !string.IsNullOrWhiteSpace(tagSong.Artist) && tagSong.Artist != "未知艺术家" ? tagSong.Artist : song.Artist;
                        // 规范化艺术家名：拆分多值字符串，取第一位作为主艺术家
                        if (!string.IsNullOrWhiteSpace(song.Artist))
                        {
                            var artistNames = MusicUtility.SplitArtistNames(song.Artist);
                            if (artistNames.Count > 0)
                                song.Artist = artistNames[0];
                        }
                        song.Album = !string.IsNullOrWhiteSpace(tagSong.Album) && tagSong.Album != "未知专辑" ? tagSong.Album : song.Album;
                        song.Duration = tagSong.Duration > 0 ? tagSong.Duration : song.Duration;
                        song.Bitrate = tagSong.Bitrate > 0 ? tagSong.Bitrate : song.Bitrate;
                        song.Year = tagSong.Year > 0 ? tagSong.Year : song.Year;
                        song.TrackNumber = tagSong.TrackNumber > 0 ? tagSong.TrackNumber : song.TrackNumber;
                        song.Genre = tagSong.Genre;
                        return song;
                    }
                }
                finally { ms.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[CatClaw] SMB 元数据获取失败: {ex.Message}");
        }
        return null;
    }

    /// <summary>
    /// 获取歌曲流 URL，按协议类型构建对应的播放地址
    /// </summary>
    /// <param name="song">歌曲对象</param>
    /// <param name="profile">连接配置</param>
    /// <returns>流播放 URL</returns>
    public async Task<string> GetStreamUrlAsync(Song song, ConnectionProfile profile)
    {
        if (profile.Protocol == ProtocolType.Navidrome)
            return _subsonic.GetStreamUrl(song.RemoteId ?? song.FilePath, profile);

        if (profile.Protocol == ProtocolType.WebDAV)
        {
            var filePath = song.RemoteId ?? song.FilePath;

            _webDav.Configure(profile);
            if (_webDav is WebDavService wdsDetect) await wdsDetect.EnsureDetectedAsync();

            var isOpenList = _webDav is WebDavService wdsCheck && wdsCheck.CurrentServerType == WebDavServerType.OpenList;
            if (isOpenList && _webDav is WebDavService webDavService)
            {
                // 检查缓存，避免短时间内重复请求 /api/fs/get
                var cacheKey = filePath;
                if (_streamUrlCache.TryGetValue(cacheKey, out var cached)
                    && cached.expiry > DateTime.UtcNow)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl cache hit: {cacheKey[..Math.Min(60, cacheKey.Length)]}");
                    return cached.url;
                }

                var openListUrl = await webDavService.GetOpenListStreamUrlAsync(filePath);
                if (!string.IsNullOrEmpty(openListUrl))
                {
                    _streamUrlCache[cacheKey] = (openListUrl, DateTime.UtcNow + StreamUrlCacheTtl);
                    return openListUrl;
                }
            }

            // 使用 BuildStreamUrl 构建带 /dav 前缀和 Basic Auth 的正确 URL
            if (_webDav is WebDavService wds)
                return wds.BuildStreamUrl(filePath);

            return BuildWebDavStreamUrl(filePath, profile);
        }

        if (profile.Protocol == ProtocolType.SMB)
            return BuildSmbStreamUrl(song.RemoteId ?? song.FilePath, profile);

        return song.FilePath;
    }

    /// <summary>
    /// 构建 SMB 流媒体 URL（smb:// scheme），包含认证信息，供 ExoPlayer 直接播放。
    /// </summary>
    /// <param name="filePath">SMB 文件相对路径。</param>
    /// <param name="profile">连接配置，提供主机、共享名、认证信息。</param>
    /// <returns>完整的 smb:// URL 字符串。</returns>
    private static string BuildSmbStreamUrl(string filePath, ConnectionProfile profile)
    {
        if (filePath.StartsWith("smb://")) return filePath;
        var host = profile.Host.Trim();
        var share = string.IsNullOrEmpty(profile.ShareName) ? "share" : profile.ShareName.Trim();
        var path = filePath.Replace('\\', '/').TrimStart('/');
        var auth = string.IsNullOrEmpty(profile.UserName) ? "" : $"{Uri.EscapeDataString(profile.UserName)}:{Uri.EscapeDataString(profile.Password)}@";
        return $"smb://{auth}{host}/{share}/{path}";
    }

    /// <summary>
    /// 构建包含认证信息的 WebDAV 流媒体 URL
    /// </summary>
    private static string BuildWebDavStreamUrl(string filePath, ConnectionProfile profile)
    {
        // 如果已经是完整 HTTP URL，直接返回
        if (filePath.StartsWith("http://") || filePath.StartsWith("https://"))
            return filePath;

        var scheme = profile.UseHttps ? "https" : "http";
        var path = filePath.TrimStart('/');
        // 清理主机地址
        var host = (profile.Host ?? "").TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            host = host[7..];
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            host = host[8..];
        // 去掉端口（已经单独设置了 Profile.Port）
        var colonIdx = host.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _))
            host = host[..colonIdx];
        // 包含 Basic 认证信息的 URL（ExoPlayer 原生支持）
        var authUser = string.IsNullOrEmpty(profile.UserName) ? "" : Uri.EscapeDataString(profile.UserName);
        var authPass = string.IsNullOrEmpty(profile.Password) ? "" : Uri.EscapeDataString(profile.Password);
        var auth = string.IsNullOrEmpty(authUser) ? "" : $"{authUser}:{authPass}@";
        return $"{scheme}://{auth}{host}:{profile.Port}/{path}";
    }

    /// <summary>
    /// 递归扫描 WebDAV 目录，批量入库发现的音频文件
    /// </summary>
    private async Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanWebDavAsync(
        ConnectionProfile profile, Action<List<Song>>? songBatchCallback,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        var songs = new List<Song>();
        var basePath = profile.BasePath?.TrimEnd('/') ?? "/";
        if (string.IsNullOrEmpty(basePath)) basePath = "/";

        progress?.Report((0, 0, "正在连接服务器..."));
        var connResult = await _webDav.TestConnectionAsync(profile);
        if (!connResult.Success)
        {
            return (songs, new HashSet<string>());
        }

        _webDav.Configure(profile);
        if (_webDav is WebDavService wdsScan) await wdsScan.EnsureDetectedAsync();

        var foundIds = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        var existingIds = new HashSet<string>();

        var scanner = new MusicScanner(_db, songBatchCallback);
        var serverType = (WebDavServerType)profile.ServerType;

        // 如果自动检测发现是 OpenList，同步到 serverType
        if (serverType != WebDavServerType.OpenList && _webDav is WebDavService wdsScanType
            && wdsScanType.CurrentServerType == WebDavServerType.OpenList)
        {
            serverType = WebDavServerType.OpenList;
            profile.ServerType = (int)WebDavServerType.OpenList;
            try { await _db.SaveConnectionProfileAsync(profile); } catch { }
        }

        // OpenList/Alist：先快速扫描入库（不下载元数据），再后台补齐元数据
        var quickScan = serverType == WebDavServerType.OpenList;

        var allFiles = await _webDav.ListAllFilesAsync(basePath, serverType);

        // 自动检测：如果 ListAllFiles 内部切换了 ServerType，同步到 profile 并保存
        if (_webDav is WebDavService wds && wds.CurrentServerType == WebDavServerType.OpenList
            && (WebDavServerType)profile.ServerType != WebDavServerType.OpenList)
        {
            System.Diagnostics.Debug.WriteLine("[WebDAV Scan] 自动检测到 OpenList，更新 profile.ServerType");
            profile.ServerType = (int)WebDavServerType.OpenList;
            try { await _db.SaveConnectionProfileAsync(profile); } catch { }
            serverType = WebDavServerType.OpenList;
            quickScan = true;
        }

        // 已存在但元数据缺失（艺术家/专辑为"未知"或空）的歌曲，需在完整扫描时补齐
        var metadataRefreshMap = new System.Collections.Concurrent.ConcurrentDictionary<string, Song>();
        try
        {
            var existingSongs = await _db.GetCachedNetworkSongsAsync();
            foreach (var s in existingSongs.Where(s => s.Source == SongSource.WebDAV && !string.IsNullOrEmpty(s.RemoteId)))
            {
                existingIds.Add(s.RemoteId!);
                // 完整扫描模式下，对元数据缺失的已存在歌曲进行补齐
                if (!quickScan &&
                    (string.IsNullOrEmpty(s.Artist) || s.Artist == "未知艺术家" ||
                     string.IsNullOrEmpty(s.Album) || s.Album == "未知专辑"))
                {
                    metadataRefreshMap.TryAdd(s.RemoteId!, s);
                }
            }
        }
        catch { }

        if (allFiles.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 深度 PROPFIND 成功，找到 {allFiles.Count} 个文件，并发处理中...");
            progress?.Report((0, allFiles.Count, $"发现 {allFiles.Count} 个文件，正在扫描..."));
            await ProcessFileListAsync(allFiles, profile, songs, foundIds, existingIds, scanner, quickScan, metadataRefreshMap, progress);
        }
        else
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 深度 PROPFIND 不支持，回退到递归扫描 (quickScan={quickScan})");
            progress?.Report((0, 0, "正在递归扫描目录..."));
            var visitedDirs = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            await ScanWebDavDirectoryAsync(basePath, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, metadataRefreshMap, 0, progress);
        }

        await scanner.FlushAsync();

        // 快速扫描模式：后台异步补齐元数据（不阻塞扫描完成）
        if (quickScan && songs.Count > 0)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 快速扫描完成，{songs.Count} 首歌曲已入库，后台补齐元数据...");
            progress?.Report((songs.Count, songs.Count, $"快速扫描完成，发现 {songs.Count} 首歌曲，后台补齐元数据..."));
            _ = Task.Run(async () => await FetchMetadataInBackgroundAsync(songs, profile));
        }
        else
        {
            progress?.Report((songs.Count, songs.Count, $"扫描完成，发现 {songs.Count} 首歌曲"));
        }

        return (songs, new HashSet<string>(foundIds.Keys, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// WebDAV 目录扫描最大递归深度
    /// </summary>
    private const int MaxScanDepth = 20;

    /// <summary>
    /// 支持的音频文件扩展名集合
    /// </summary>
    private static readonly HashSet<string> AudioExtSet = new(
        new[] { ".MP3", ".WAV", ".FLAC", ".AAC", ".OGG", ".M4A", ".WMA", ".APE", ".AIFF", ".DSF" },
        StringComparer.Ordinal);

    /// <summary>
    /// 判断文件扩展名是否为支持的音频格式
    /// </summary>
    private static bool IsAudioExtension(string ext)
        => AudioExtSet.Contains(ext);

    /// <summary>
    /// 并发处理深度 PROPFIND 返回的扁平文件列表
    /// </summary>
    private async Task ProcessFileListAsync(List<RemoteFile> allFiles, ConnectionProfile profile,
        List<Song> songs, System.Collections.Concurrent.ConcurrentDictionary<string, byte> foundIds, HashSet<string> existingIds, MusicScanner scanner, bool quickScan = false,
        System.Collections.Concurrent.ConcurrentDictionary<string, Song>? metadataRefreshMap = null,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        var audioFiles = allFiles
            .Where(f =>
            {
                var ext = System.IO.Path.GetExtension(f.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(f.Path)?.ToUpperInvariant() ?? "";
                return IsAudioExtension(ext);
            })
            .ToList();

        System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 过滤后音频文件: {audioFiles.Count}");
        progress?.Report((0, audioFiles.Count, $"发现 {audioFiles.Count} 个音频文件，正在提取元数据..."));

        var processedCount = 0;
        var progressLock = new object();
        var totalAudio = audioFiles.Count;

        var metadataTasks = audioFiles.Select(file => Task.Run(async () =>
        {
            await ScanSemaphore.WaitAsync();
            try
            {
                foundIds.TryAdd(file.Path, 0);

                if (existingIds.Contains(file.Path))
                {
                    // 已存在的歌曲：若元数据缺失（艺术家/专辑为"未知"或空），补齐元数据
                    if (metadataRefreshMap != null && metadataRefreshMap.TryRemove(file.Path, out var existingSong))
                    {
                        try
                        {
                            var tagged = await FetchWebDavMetadataAsync(existingSong, profile);
                            if (tagged != null)
                            {
                                await _db.SaveSongAsync(existingSong);
                                System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 元数据补齐: {existingSong.Title} → {existingSong.Artist}/{existingSong.Album}");
                            }
                        }
                        catch { }
                    }
                    return;
                }

                // 使用 WebDavService.BuildStreamUrl 构建正确的 URL（自动包含 /dav 前缀和 Basic Auth）
                string streamUrl;
                if (_webDav is WebDavService wds)
                    streamUrl = wds.BuildStreamUrl(file.Path);
                else
                    streamUrl = BuildWebDavStreamUrl(file.Path, profile);

                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.WebDAV,
                    Protocol = ProtocolType.WebDAV,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                if (!quickScan)
                {
                    try
                    {
                        var tagged = await FetchWebDavMetadataAsync(song, profile);
                        if (tagged == null)
                        {
                            song.Artist = "未知艺术家";
                            song.Album = "未知专辑";
                        }
                    }
                    catch
                    {
                        song.Artist = "未知艺术家";
                        song.Album = "未知专辑";
                    }
                }
                else
                {
                    song.Artist = "未知艺术家";
                    song.Album = "未知专辑";
                }

                lock (songs)
                    songs.Add(song);
                await scanner.AddSongAsync(song);
            }
            finally
            {
                ScanSemaphore.Release();
                int done;
                lock (progressLock)
                {
                    processedCount++;
                    done = processedCount;
                }
                if (progress != null && (done % 5 == 0 || done == totalAudio))
                {
                    progress.Report((done, totalAudio, $"正在扫描 {done}/{totalAudio}"));
                }
            }
        }));

        await Task.WhenAll(metadataTasks);
    }

    /// <summary>
    /// 递归扫描 WebDAV 目录（回退方案，当深度 PROPFIND 不支持时使用）
    /// </summary>
    private async Task ScanWebDavDirectoryAsync(string path, ConnectionProfile profile, List<Song> songs,
        System.Collections.Concurrent.ConcurrentDictionary<string, byte> foundIds, HashSet<string> existingIds, MusicScanner scanner, System.Collections.Concurrent.ConcurrentDictionary<string, byte> visitedDirs, bool quickScan = false,
        System.Collections.Concurrent.ConcurrentDictionary<string, Song>? metadataRefreshMap = null, int depth = 0,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        if (depth > MaxScanDepth)
            return;

        var normalizedDir = path.TrimEnd('/').TrimEnd('\\');
        if (string.IsNullOrEmpty(normalizedDir)) normalizedDir = "/";
        if (!visitedDirs.TryAdd(normalizedDir, 0))
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 跳过已访问目录: {path}");
            return;
        }

        List<RemoteFile> files;
        try
        {
            files = await _webDav.ListFilesAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 列出 {path} 失败: {ex.Message}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 目录 {path} 有 {files.Count} 个条目 (depth={depth})");

        var audioFiles = new List<RemoteFile>();
        var subDirs = new List<RemoteFile>();

        foreach (var file in files)
        {
            if (file.IsDirectory)
                subDirs.Add(file);
            else
            {
                var ext = System.IO.Path.GetExtension(file.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(file.Path)?.ToUpperInvariant() ?? "";
                if (IsAudioExtension(ext))
                    audioFiles.Add(file);
            }
        }

        // 并行扫描子目录（限制并发数避免服务器过载）
        var subDirTasks = subDirs.Select(subDir => Task.Run(async () =>
        {
            await DirScanSemaphore.WaitAsync();
            try
            {
                await ScanWebDavDirectoryAsync(subDir.Path, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, metadataRefreshMap, depth + 1, progress);
            }
            finally
            {
                DirScanSemaphore.Release();
            }
        }));
        await Task.WhenAll(subDirTasks);

        if (audioFiles.Count == 0) return;

        progress?.Report((songs.Count, 0, $"正在扫描 {Path.GetFileName(path)} ({audioFiles.Count} 个音频文件)"));

        var dirProcessedCount = 0;
        var dirProgressLock = new object();
        var dirTotalAudio = audioFiles.Count;

        var metadataTasks = audioFiles.Select(file => Task.Run(async () =>
        {
            await ScanSemaphore.WaitAsync();
            try
            {
                foundIds.TryAdd(file.Path, 0);

                if (existingIds.Contains(file.Path))
                {
                    // 已存在的歌曲：若元数据缺失（艺术家/专辑为"未知"或空），补齐元数据
                    if (metadataRefreshMap != null && metadataRefreshMap.TryRemove(file.Path, out var existingSong))
                    {
                        try
                        {
                            var tagged = await FetchWebDavMetadataAsync(existingSong, profile);
                            if (tagged != null)
                            {
                                await _db.SaveSongAsync(existingSong);
                                System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 元数据补齐: {existingSong.Title} → {existingSong.Artist}/{existingSong.Album}");
                            }
                        }
                        catch { }
                    }
                    return;
                }

                // 使用 WebDavService.BuildStreamUrl 构建正确的 URL（自动包含 /dav 前缀和 Basic Auth）
                string streamUrl;
                if (_webDav is WebDavService wds)
                    streamUrl = wds.BuildStreamUrl(file.Path);
                else
                    streamUrl = BuildWebDavStreamUrl(file.Path, profile);

                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.WebDAV,
                    Protocol = ProtocolType.WebDAV,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                if (!quickScan)
                {
                    try
                    {
                        var tagged = await FetchWebDavMetadataAsync(song, profile);
                        if (tagged == null)
                        {
                            song.Artist = "未知艺术家";
                            song.Album = "未知专辑";
                        }
                    }
                    catch
                    {
                        song.Artist = "未知艺术家";
                        song.Album = "未知专辑";
                    }
                }
                else
                {
                    song.Artist = "未知艺术家";
                    song.Album = "未知专辑";
                }

                lock (songs)
                    songs.Add(song);
                await scanner.AddSongAsync(song);
            }
            finally
            {
                ScanSemaphore.Release();
                int done;
                lock (dirProgressLock)
                {
                    dirProcessedCount++;
                    done = dirProcessedCount;
                }
                if (progress != null && (done % 5 == 0 || done == dirTotalAudio))
                {
                    progress.Report((done, dirTotalAudio, $"正在扫描 {Path.GetFileName(path)} {done}/{dirTotalAudio}"));
                }
            }
        }));

        await Task.WhenAll(metadataTasks);
    }

    /// <summary>
    /// 后台异步补齐歌曲元数据（OpenList 快速扫描模式使用）
    /// 延迟启动，逐首串行下载，避免与播放抢带宽
    /// </summary>
    private async Task FetchMetadataInBackgroundAsync(List<Song> songs, ConnectionProfile profile)
    {
        // 延迟 10 秒启动，优先让播放流建立稳定连接
        await Task.Delay(10_000);

        _webDav.Configure(profile);
        if (_webDav is WebDavService wdsBg) await wdsBg.EnsureDetectedAsync();
        var updated = 0;
        foreach (var song in songs)
        {
            try
            {
                var tagged = await FetchWebDavMetadataAsync(song, profile);
                if (tagged != null)
                {
                    await _db.SaveSongAsync(song);
                    updated++;
                }
            }
            catch { }
            // 每首之间等待 200ms，避免突发并发请求占满带宽
            await Task.Delay(200);
        }
        System.Diagnostics.Debug.WriteLine($"[WebDAV Scan] 后台元数据补齐完成: {updated}/{songs.Count} 首已更新");
    }

    /// <summary>
    /// 递归扫描 SMB 共享目录，批量入库发现的音频文件。
    /// </summary>
    /// <param name="profile">SMB 连接配置。</param>
    /// <param name="songBatchCallback">每批次歌曲扫描完成后的回调。</param>
    /// <returns>(新扫描到的歌曲列表, 所有发现的文件路径 ID 集合)。</returns>
    private async Task<(List<Song> NewSongs, HashSet<string> AllFoundIds)> ScanSmbAsync(
        ConnectionProfile profile, Action<List<Song>>? songBatchCallback,
        IProgress<(int done, int total, string status)>? progress = null,
        bool quickScan = false)
    {
        var songs = new List<Song>();
        var basePath = profile.BasePath?.TrimEnd('/', '\\') ?? "\\";
        if (string.IsNullOrEmpty(basePath) || basePath == "/") basePath = "\\";

        progress?.Report((0, 0, "正在连接 SMB 服务器..."));
        var connResult = await _smb.TestConnectionAsync(profile);
        if (!connResult.Success) return (songs, new HashSet<string>());

        _smb.Configure(profile);

        var foundIds = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        var existingIds = new HashSet<string>();
        try
        {
            var existingSongs = await _db.GetCachedNetworkSongsAsync();
            existingIds = existingSongs
                .Where(s => s.Source == SongSource.SMB && !string.IsNullOrEmpty(s.RemoteId))
                .Select(s => s.RemoteId!.TrimStart('\\'))
                .ToHashSet();
        }
        catch { }

        var scanner = new MusicScanner(_db, songBatchCallback);
        var visitedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        progress?.Report((0, 0, "正在递归扫描 SMB 目录..."));
        await ScanSmbDirectoryAsync(basePath, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, 0, progress);
        await scanner.FlushAsync();
        progress?.Report((songs.Count, songs.Count, $"扫描完成，发现 {songs.Count} 首歌曲"));

        return (songs, new HashSet<string>(foundIds.Keys, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 递归扫描 SMB 单个目录，将其中的音频文件入库并继续扫描子目录。
    /// 使用 visitedDirs 集合防止循环引用，depth 限制最大递归深度。
    /// </summary>
    /// <param name="path">当前扫描的 SMB 目录路径。</param>
    /// <param name="profile">SMB 连接配置。</param>
    /// <param name="songs">累计扫描到的歌曲列表。</param>
    /// <param name="foundIds">本次扫描发现的所有文件路径 ID 集合。</param>
    /// <param name="existingIds">数据库中已存在的文件路径 ID 集合（用于增量扫描跳过）。</param>
    /// <param name="scanner">音乐扫描器实例。</param>
    /// <param name="visitedDirs">已访问目录集合（防止循环）。</param>
    /// <param name="depth">当前递归深度。</param>
    private async Task ScanSmbDirectoryAsync(string path, ConnectionProfile profile, List<Song> songs,
        System.Collections.Concurrent.ConcurrentDictionary<string, byte> foundIds, HashSet<string> existingIds, MusicScanner scanner, HashSet<string> visitedDirs, bool quickScan = false, int depth = 0,
        IProgress<(int done, int total, string status)>? progress = null)
    {
        if (depth > MaxScanDepth) return;

        var normalizedDir = path.TrimEnd('/').TrimEnd('\\');
        if (string.IsNullOrEmpty(normalizedDir)) normalizedDir = "\\";
        if (!visitedDirs.Add(normalizedDir))
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Scan] 跳过已访问目录: {path}");
            return;
        }

        List<RemoteFile> files;
        try
        {
            files = await _smb.ListFilesAsync(path);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SMB Scan] 列出 {path} 失败: {ex.Message}");
            return;
        }

        System.Diagnostics.Debug.WriteLine($"[SMB Scan] 目录 {path} 有 {files.Count} 个条目 (depth={depth})");

        var audioFiles = new List<RemoteFile>();
        var subDirs = new List<RemoteFile>();

        foreach (var file in files)
        {
            if (file.IsDirectory)
                subDirs.Add(file);
            else
            {
                var ext = System.IO.Path.GetExtension(file.Name)?.ToUpperInvariant() ?? "";
                if (string.IsNullOrEmpty(ext))
                    ext = System.IO.Path.GetExtension(file.Path)?.ToUpperInvariant() ?? "";
                if (IsAudioExtension(ext))
                    audioFiles.Add(file);
            }
        }

        foreach (var subDir in subDirs)
            await ScanSmbDirectoryAsync(subDir.Path, profile, songs, foundIds, existingIds, scanner, visitedDirs, quickScan, depth + 1, progress);

        if (audioFiles.Count == 0) return;

        progress?.Report((songs.Count, 0, $"正在扫描 {Path.GetFileName(path)} ({audioFiles.Count} 个音频文件)"));

        var smbProcessedCount = 0;
        var smbProgressLock = new object();
        var smbTotalAudio = audioFiles.Count;

        var metadataTasks = audioFiles.Select(file => Task.Run(async () =>
        {
            await ScanSemaphore.WaitAsync();
            try
            {
                foundIds.TryAdd(file.Path, 0);

                if (existingIds.Contains(file.Path))
                    return;

                var streamUrl = BuildSmbStreamUrl(file.Path, profile);
                var title = System.IO.Path.GetFileNameWithoutExtension(file.Name) ?? file.Name;
                if (string.IsNullOrEmpty(title))
                    title = System.IO.Path.GetFileNameWithoutExtension(file.Path) ?? file.Path;
                var song = new Song
                {
                    Title = title,
                    Artist = "",
                    Album = "",
                    FilePath = streamUrl,
                    Duration = 0,
                    FileSize = file.Size,
                    DateAdded = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    Source = SongSource.SMB,
                    Protocol = ProtocolType.SMB,
                    RemoteId = file.Path,
                    CoverArtPath = file.Path
                };

                if (!quickScan)
                {
                    try
                    {
                        var tagged = await FetchSmbMetadataAsync(song, profile);
                        if (tagged == null)
                        {
                            song.Artist = "未知艺术家";
                            song.Album = "未知专辑";
                        }
                    }
                    catch
                    {
                        song.Artist = "未知艺术家";
                        song.Album = "未知专辑";
                    }
                }
                else
                {
                    song.Artist = "未知艺术家";
                    song.Album = "未知专辑";
                }

                lock (songs)
                    songs.Add(song);
                await scanner.AddSongAsync(song);
            }
            finally
            {
                ScanSemaphore.Release();
                int done;
                lock (smbProgressLock)
                {
                    smbProcessedCount++;
                    done = smbProcessedCount;
                }
                if (progress != null && (done % 5 == 0 || done == smbTotalAudio))
                {
                    progress.Report((done, smbTotalAudio, $"正在扫描 {Path.GetFileName(path)} {done}/{smbTotalAudio}"));
                }
            }
        }));

        await Task.WhenAll(metadataTasks);
    }
}

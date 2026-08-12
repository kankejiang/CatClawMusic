using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Services;

namespace CatClawMusic.Data;

/// <summary>
/// 网络音乐服务——按协议类型分发
/// </summary>
public partial class NetworkMusicService : INetworkMusicService
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
    /// <summary>分享音频大文件下载用共享客户端（10 分钟超时），避免每请求 new HttpClient 造成 socket 泄漏</summary>
    private static readonly HttpClient ShareHttpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    /// <summary>OpenList 封面头部下载共享客户端（连接池复用；证书策略统一走 WebDavService 全局开关）</summary>
    private static readonly HttpClient OpenListCoverClient = new(new SocketsHttpHandler
    {
        SslOptions = new System.Net.Security.SslClientAuthenticationOptions
        {
            RemoteCertificateValidationCallback = CatClawMusic.Data.WebDavCertPolicy.CreateCertValidationCallback("OpenListCover")
        },
        ConnectTimeout = TimeSpan.FromSeconds(10),
        AllowAutoRedirect = true
    })
    { Timeout = TimeSpan.FromSeconds(15) };

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
                            Log.Debug("NetworkMusicService", $"[URL Resolver] OpenList raw_url: {rawUrl[..Math.Min(80, rawUrl.Length)]}...");
                            return rawUrl;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("NetworkMusicService", $"[URL Resolver] OpenList raw_url 获取失败: {ex.Message}");
                    }
                }

                // 如果URL缺少dav前缀且探测到需要前缀，用BuildStreamUrl修复
                if (!hasDavPrefix && !string.IsNullOrEmpty(wds.DavPrefix))
                {
                    var fixedUrl = wds.BuildStreamUrl(virtualPath);
                    Log.Debug("NetworkMusicService", $"[URL Resolver] 修复URL: {url[..Math.Min(60, url.Length)]}... → {fixedUrl[..Math.Min(80, fixedUrl.Length)]}...");
                    return fixedUrl;
                }
            }

            return null; // 无需修复
        }
        catch (Exception ex)
        {
            Log.Debug("NetworkMusicService", $"[URL Resolver] 解析失败: {ex.Message}");
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
}

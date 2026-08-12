using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;
using System.Collections.Concurrent;

namespace CatClawMusic.Data;

/// <summary>网络音乐服务 —— partial 分域文件。</summary>
public partial class NetworkMusicService
{
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
                    Log.Debug("NetworkMusicService", $"[OpenList] StreamUrl cache hit: {cacheKey[..Math.Min(60, cacheKey.Length)]}");
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
    /// 打开音频流，供「分享音频文件」把远程音频下载到本地后分享。
    /// 按协议复用既有的流通道：Navidrome 走 HTTP GET；WebDAV/SMB 走对应服务的 OpenReadAsync。
    /// 调用方负责释放返回的流。
    /// </summary>
    public async Task<Stream?> OpenAudioStreamAsync(Song song, ConnectionProfile profile)
    {
        try
        {
            if (profile.Protocol == ProtocolType.Navidrome)
            {
                var url = _subsonic.GetStreamUrl(song.RemoteId ?? song.FilePath, profile);
                // 复用静态共享客户端（大文件下载 10 分钟超时），避免每请求 new HttpClient 造成 socket 泄漏
                return await ShareHttpClient.GetStreamAsync(url);
            }

            var remotePath = song.RemoteId ?? song.FilePath;

            if (profile.Protocol == ProtocolType.WebDAV)
            {
                _webDav.Configure(profile);
                if (_webDav is WebDavService wdsDetect) await wdsDetect.EnsureDetectedAsync();
                return await _webDav.OpenReadAsync(remotePath);
            }

            if (profile.Protocol == ProtocolType.SMB)
            {
                _smb.Configure(profile);
                return await _smb.OpenReadAsync(remotePath);
            }
        }
        catch (Exception ex)
        {
            Log.Debug("NetworkMusicService", $"[Share] 打开音频流失败: {song.Title}, {ex.Message}");
        }
        return null;
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
}

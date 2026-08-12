using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml.Linq;
using CatClawMusic.Core.Models;
using CatClawMusic.Core.Interfaces;

using System.Net.Security;

namespace CatClawMusic.Data;

/// <summary>WebDAV 文件服务 —— partial 分域文件。</summary>
public partial class WebDavService
{
    public async Task EnsureDetectedAsync()
    {
        if (_detectionTask != null && !_detectionTask.IsCompleted)
        {
            try { CurrentServerType = await _detectionTask; }
            catch { }
            _detectionTask = null;
        }
    }

    /// <summary>
    /// 尝试通过 REST API 检测是否为 OpenList/Alist 服务器。
    /// 手动跟随重定向以适配域名经反向代理的场景。
    /// </summary>
    private static async Task<bool> IsOpenListByApiAsync(ConnectionProfile profile)
    {
        try
        {
            var scheme = profile.UseHttps ? "https" : "http";
            var host = (profile.Host ?? "").TrimEnd('/');
            if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = host[7..];
            else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = host[8..];
            var colonIdx = host.LastIndexOf(':');
            if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _)) host = host[..colonIdx];
            var port = profile.Port;
            var apiUrl = port == 80 || port == 443
                ? $"{scheme}://{host}/api/public/settings"
                : $"{scheme}://{host}:{port}/api/public/settings";

            using var apiClient = new HttpClient(new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = WebDavCertPolicy.CreateCertValidationCallback("WebDAV")
                },
                ConnectTimeout = TimeSpan.FromSeconds(5),
                AllowAutoRedirect = false
            })
            { Timeout = TimeSpan.FromSeconds(5) };

            // 手动跟随重定向（域名经反代时 /api/public/settings 可能重定向）
            var currentUrl = apiUrl;
            for (var i = 0; i <= 3; i++)
            {
                var apiResp = await apiClient.GetAsync(currentUrl);
                var statusCode = (int)apiResp.StatusCode;
                if (statusCode == 301 || statusCode == 302 || statusCode == 307 || statusCode == 308)
                {
                    var location = apiResp.Headers.Location;
                    if (location == null) return false;
                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();
                    continue;
                }
                if (apiResp.IsSuccessStatusCode)
                {
                    var body = await apiResp.Content.ReadAsStringAsync();
                    if (body.Contains("\"version\"", StringComparison.Ordinal) &&
                        (body.Contains("alist", StringComparison.OrdinalIgnoreCase) ||
                         body.Contains("openlist", StringComparison.OrdinalIgnoreCase)))
                    {
                        Log.Debug("WebDavService", $"[WebDAV] API 检测到 OpenList/Alist");
                        return true;
                    }
                }
                return false;
            }
            return false;
        }
        catch { /* API 检测失败不影响主流程 */ }
        return false;
    }

    /// <summary>
    /// 尝试对指定 URL 发送 PROPFIND depth=0 请求，返回是否成功。
    /// 手动跟随 301/302/307/308 重定向（域名经反向代理时常见 HTTP→HTTPS、路径规范化等重定向）。
    /// </summary>
    private async Task<bool> TryPropFindAsync(string url, HttpClient? client = null)
    {
        try
        {
            var httpClient = client ?? GetClient();
            var currentUrl = url;
            for (var i = 0; i <= 3; i++)
            {
                var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
                req.Headers.Add("Depth", "0");
                req.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");
                var resp = await httpClient.SendAsync(req);
                var statusCode = (int)resp.StatusCode;

                // 跟随重定向（域名经反向代理时常见）
                if (statusCode == 301 || statusCode == 302 || statusCode == 307 || statusCode == 308)
                {
                    var location = resp.Headers.Location;
                    if (location == null) return false;
                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();
                    Log.Debug("WebDavService", $"[WebDAV] TryPropFind 重定向: {statusCode} -> {currentUrl}");
                    continue;
                }

                return resp.IsSuccessStatusCode;
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>
    /// 对指定组合（前缀 + 路径）尝试 PROPFIND，返回 (是否成功, 完整URL)
    /// </summary>
    private async Task<(bool Success, string Url)> TryPropFindWithPrefixAsync(ConnectionProfile profile, string prefix, string path, HttpClient? client = null)
    {
        var normalizedPrefix = prefix.TrimEnd('/');
        var combined = normalizedPrefix + "/" + path.TrimStart('/');
        if (string.IsNullOrEmpty(combined) || combined == "/") combined = normalizedPrefix != "" ? normalizedPrefix : "/";
        var url = BuildUrlForProfile(profile, combined, isDirectory: true);
        var ok = await TryPropFindAsync(url, client);
        return (ok, url);
    }

    /// <summary>
    /// 检测 WebDAV 服务器类型（标准 vs OpenList/Alist）
    /// 优先通过 REST API 检测；PROPFIND 返回 405 时自动尝试 /dav、/webdav 等前缀。
    /// </summary>
    public async Task<WebDavServerType> DetectServerTypeAsync(ConnectionProfile profile)
    {
        try
        {
            EnsureClient(profile);
            var basePath = profile.BasePath?.Trim() ?? "/";
            if (string.IsNullOrEmpty(basePath)) basePath = "/";

            // ── 第1步：优先通过 REST API 检测 OpenList（不依赖 PROPFIND 可用）──
            bool isApiOpenList = await IsOpenListByApiAsync(profile);

            // ── 第2步：尝试 PROPFIND，覆盖多种前缀组合 ──
            // 先尝试无前缀，再尝试 /dav、/webdav 前缀
            string? foundPrefix = null;
            string? foundUrl = null;
            foreach (var prefix in new[] { "", "/dav", "/webdav" })
            {
                var (ok, url) = await TryPropFindWithPrefixAsync(profile, prefix, basePath, GetClient());
                if (ok)
                {
                    foundPrefix = prefix;
                    foundUrl = url;
                    break;
                }
            }

            // 如果用户路径完全失败，也尝试纯前缀路径（用户可能把整个 WebDAV 端点填在了 basePath 里）
            if (foundPrefix == null)
            {
                foreach (var prefix in new[] { "/dav", "/webdav" })
                {
                    var tryUrl = BuildUrlForProfile(profile, prefix, isDirectory: true);
                    if (await TryPropFindAsync(tryUrl, GetClient()))
                    {
                        foundPrefix = prefix;
                        foundUrl = tryUrl;
                        break;
                    }
                }
            }

            if (foundPrefix != null)
            {
                _davPrefix = foundPrefix;
                Log.Debug("WebDavService", $"[WebDAV] PROPFIND 成功: prefix='{foundPrefix}', url={foundUrl}");
            }

            // ── 第3步：综合判断服务器类型 ──
            if (isApiOpenList)
            {
                // API 明确表明是 OpenList
                if (foundPrefix == null)
                {
                    // PROPFIND 完全不行但 API 可用 —— 使用 /dav 默认前缀（所有 Alist/OpenList 都用 /dav）
                    _davPrefix = "/dav";
                    Log.Debug("WebDavService", $"[WebDAV] API 检测为 OpenList，但 PROPFIND 不可用（将仅使用 REST API，默认前缀 /dav）");
                }
                DetectedServerType = WebDavServerType.OpenList;
                return WebDavServerType.OpenList;
            }

            if (foundPrefix != null && !string.IsNullOrEmpty(foundPrefix))
            {
                // 非空前缀才可用 → OpenList/Alist 特征（标准 WebDAV 不会要求前缀）
                Log.Debug("WebDavService", $"[WebDAV] 需要前缀 '{foundPrefix}' → OpenList");
                DetectedServerType = WebDavServerType.OpenList;
                return WebDavServerType.OpenList;
            }

            if (foundPrefix == null)
            {
                // 连无前缀的 PROPFIND 也失败了，无法判断，返回 Standard
                Log.Debug("WebDavService", $"[WebDAV] PROPFIND 全部失败，无法确定服务器类型");
                DetectedServerType = WebDavServerType.Standard;
                return WebDavServerType.Standard;
            }

            // PROPFIND 无前缀成功（标准 WebDAV），检查 Server 头确认
            try
            {
                var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), foundUrl);
                req.Headers.Add("Depth", "0");
                req.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");
                var resp = await GetClient().SendAsync(req);
                if (resp.IsSuccessStatusCode)
                {
                    var serverHeader = resp.Headers.Server?.ToString() ?? "";
                    if (serverHeader.Contains("Alist", StringComparison.OrdinalIgnoreCase) ||
                        serverHeader.Contains("OpenList", StringComparison.OrdinalIgnoreCase))
                    {
                        DetectedServerType = WebDavServerType.OpenList;
                        return WebDavServerType.OpenList;
                    }
                }
            }
            catch { }

            DetectedServerType = WebDavServerType.Standard;
            return WebDavServerType.Standard;
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[WebDAV] 检测服务器类型失败: {ex.Message}");
            return WebDavServerType.Standard;
        }
    }

    /// <summary>
    /// 获取当前已配置的 HttpClient，未配置时抛出异常。
    /// </summary>
    /// <returns>已初始化的 HttpClient 实例。</returns>
}

using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using CatClawMusic.Core.Interfaces;
using CatClawMusic.Core.Models;

namespace CatClawMusic.Data;

/// <summary>
/// WebDAV 网络文件服务，提供文件列表、读取、上传和连接测试功能
/// </summary>
public class WebDavService : INetworkFileService, IDisposable
{
    /// <summary>WebDAV 主请求 HttpClient（带 Basic Auth）</summary>
    private HttpClient? _client;
    /// <summary>当前已配置的连接信息</summary>
    private ConnectionProfile? _profile;

    /// <summary>最近一次 TestConnection 检测到的服务器类型</summary>
    public WebDavServerType DetectedServerType { get; private set; } = WebDavServerType.Standard;

    /// <summary>当前配置的服务器类型（Configure 时从 profile 读取）</summary>
    public WebDavServerType CurrentServerType { get; private set; } = WebDavServerType.Standard;

    // ── OpenList / Alist REST API 字段 ──
    /// <summary>OpenList REST API 的 Bearer token（登录后缓存）</summary>
    private string? _openListToken;
    /// <summary>OpenList REST API 专用 HttpClient（无 Basic Auth）</summary>
    private HttpClient? _openListApiClient;
    // ── 检测缓存：同一 host 只检测一次 ──
    /// <summary>最近一次完成服务器类型检测的 host:port 键值，避免重复检测</summary>
    private string? _lastDetectedHost;
    /// <summary>正在进行的异步服务器类型检测任务</summary>
    private Task<WebDavServerType>? _detectionTask;
    /// <summary>自动探测到的 WebDAV 路径前缀（如 "/dav"、"/webdav"），为空表示无前缀</summary>
    private string _davPrefix = "";

    /// <summary>
    /// 等待首次服务器类型检测完成（如有正在进行的检测）。
    /// 确保 CurrentServerType 已更新为真实值。
    /// </summary>
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
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
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
                        System.Diagnostics.Debug.WriteLine($"[WebDAV] API 检测到 OpenList/Alist");
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
                    System.Diagnostics.Debug.WriteLine($"[WebDAV] TryPropFind 重定向: {statusCode} -> {currentUrl}");
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
                System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 成功: prefix='{foundPrefix}', url={foundUrl}");
            }

            // ── 第3步：综合判断服务器类型 ──
            if (isApiOpenList)
            {
                // API 明确表明是 OpenList
                if (foundPrefix == null)
                {
                    // PROPFIND 完全不行但 API 可用 —— 使用 /dav 默认前缀（所有 Alist/OpenList 都用 /dav）
                    _davPrefix = "/dav";
                    System.Diagnostics.Debug.WriteLine($"[WebDAV] API 检测为 OpenList，但 PROPFIND 不可用（将仅使用 REST API，默认前缀 /dav）");
                }
                DetectedServerType = WebDavServerType.OpenList;
                return WebDavServerType.OpenList;
            }

            if (foundPrefix != null && !string.IsNullOrEmpty(foundPrefix))
            {
                // 非空前缀才可用 → OpenList/Alist 特征（标准 WebDAV 不会要求前缀）
                System.Diagnostics.Debug.WriteLine($"[WebDAV] 需要前缀 '{foundPrefix}' → OpenList");
                DetectedServerType = WebDavServerType.OpenList;
                return WebDavServerType.OpenList;
            }

            if (foundPrefix == null)
            {
                // 连无前缀的 PROPFIND 也失败了，无法判断，返回 Standard
                System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 全部失败，无法确定服务器类型");
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
            System.Diagnostics.Debug.WriteLine($"[WebDAV] 检测服务器类型失败: {ex.Message}");
            return WebDavServerType.Standard;
        }
    }

    /// <summary>
    /// 获取当前已配置的 HttpClient，未配置时抛出异常。
    /// </summary>
    /// <returns>已初始化的 HttpClient 实例。</returns>
    /// <exception cref="InvalidOperationException">未调用 Configure 或 EnsureClient。</exception>
    private HttpClient GetClient()
    {
        if (_client == null || _profile == null)
            throw new InvalidOperationException("WebDAV 未配置连接");
        return _client;
    }

    /// <summary>
    /// 确保 HttpClient 已按 profile 完成初始化。
    /// 当 host/port/账号密码/协议变化时重新创建 HttpClient，避免复用旧连接。
    /// </summary>
    /// <param name="profile">连接配置。</param>
    /// <param name="forceNew">是否强制重新创建 HttpClient（忽略缓存判断）。</param>
    private void EnsureClient(ConnectionProfile profile, bool forceNew = false)
    {
        if (!forceNew && _client != null && _profile?.Host == profile.Host
            && _profile?.Port == profile.Port
            && _profile?.UserName == profile.UserName
            && _profile?.Password == profile.Password
            && _profile?.UseHttps == profile.UseHttps)
            return;

        _client?.Dispose();

        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AllowAutoRedirect = false
        };
        _client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrEmpty(profile.UserName))
        {
            // 使用 UTF8 而非 ASCII，避免密码含非 ASCII 字符（如中文）时被截断
            var byteArray = Encoding.UTF8.GetBytes($"{profile.UserName}:{profile.Password}");
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        _profile = profile;
    }

    /// <summary>
    /// 基于当前 profile 构建完整的 WebDAV 请求 URL。
    /// 自动 prepend 已探测到的 dav 前缀（如 /dav）。
    /// </summary>
    /// <param name="path">远程路径（相对或绝对）。</param>
    /// <param name="isDirectory">是否为目录（影响是否补尾部斜杠）。</param>
    /// <returns>完整 URL 字符串。</returns>
    private string BuildUrl(string path, bool isDirectory = false)
    {
        var profile = _profile ?? throw new InvalidOperationException("未配置连接");
        // 自动拼接 dav 前缀（避免双重前缀）
        var effectivePath = path;
        if (!string.IsNullOrEmpty(_davPrefix))
        {
            var trimmedPath = path.TrimStart('/');
            var prefixTrimmed = _davPrefix.Trim('/');
            if (!trimmedPath.StartsWith(prefixTrimmed + "/", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(trimmedPath, prefixTrimmed, StringComparison.OrdinalIgnoreCase))
            {
                effectivePath = _davPrefix.TrimEnd('/') + "/" + trimmedPath;
            }
        }
        return BuildUrlForProfile(profile, effectivePath, isDirectory);
    }

    /// <summary>
    /// 基于指定 profile 构建完整的 WebDAV 请求 URL。
    /// 处理 host 中已包含 scheme/port 的情况，统一输出 scheme://host:port/path 形式。
    /// </summary>
    /// <param name="profile">连接配置。</param>
    /// <param name="path">远程路径。</param>
    /// <param name="isDirectory">是否为目录。</param>
    /// <returns>完整 URL 字符串。</returns>
    private static string BuildUrlForProfile(ConnectionProfile profile, string path, bool isDirectory = false)
    {
        var scheme = profile.UseHttps ? "https" : "http";
        var host = profile.Host.TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            host = host[7..];
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            host = host[8..];
        var colonIdx = host.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _))
            host = host[..colonIdx];
        var port = profile.Port;

        var normalizedPath = NormalizeHrefToPath(path).TrimStart('/');
        if (string.IsNullOrEmpty(normalizedPath)) normalizedPath = "";

        var baseUrl = port == 80 || port == 443
            ? $"{scheme}://{host}"
            : $"{scheme}://{host}:{port}";

        var url = $"{baseUrl}/{normalizedPath}";
        if (isDirectory && !url.EndsWith("/"))
            url += "/";
        else if (!isDirectory)
            url = url.TrimEnd('/');

        return url;
    }

    /// <summary>
    /// 将 PROPFIND 响应中的 href 规范化为路径形式。
    /// 完整 URL 会被提取为 AbsolutePath，相对路径原样返回。
    /// </summary>
    /// <param name="href">PROPFIND 响应中的 href 值。</param>
    /// <returns>规范化后的路径。</returns>
    private static string NormalizeHrefToPath(string href)
    {
        if (string.IsNullOrEmpty(href)) return "/";
        if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return new Uri(href).AbsolutePath;
            }
            catch { }
        }
        return href;
    }

    /// <summary>
    /// 创建用于连接测试的临时 HttpClient（独立于缓存的 _client，便于一次性探测）。
    /// </summary>
    /// <param name="profile">连接配置。</param>
    /// <returns>带 Basic Auth 和忽略 SSL 校验的 HttpClient。</returns>
    private static HttpClient CreateTestClient(ConnectionProfile profile)
    {
        var handler = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(30),
            AllowAutoRedirect = false
        };
        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        if (!string.IsNullOrEmpty(profile.UserName))
        {
            // 使用 UTF8 而非 ASCII，避免密码含非 ASCII 字符时被截断
            var byteArray = Encoding.UTF8.GetBytes($"{profile.UserName}:{profile.Password}");
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Basic", Convert.ToBase64String(byteArray));
        }

        return client;
    }

    /// <summary>
    /// PROPFIND 请求体模板，请求 resourcetype / contentlength / lastmodified / displayname 四个属性。
    /// </summary>
    private const string PropFindBody = @"<?xml version='1.0' encoding='utf-8'?>
<D:propfind xmlns:D='DAV:'>
  <D:prop>
    <D:resourcetype/>
    <D:getcontentlength/>
    <D:getlastmodified/>
    <D:displayname/>
  </D:prop>
</D:propfind>";

    /// <summary>
    /// 使用当前 HttpClient 发送 PROPFIND 请求并解析响应为 XDocument。
    /// </summary>
    /// <param name="url">请求 URL。</param>
    /// <param name="depth">Depth 头值，0=仅当前资源，1=当前+直接子项，&gt;1=递归（服务器支持时）。</param>
    /// <returns>解析后的 XML 文档；请求失败时返回 null。</returns>
    private async Task<XDocument?> PropFindAsync(string url, int depth = 1)
    {
        return await PropFindWithRedirectAsync(GetClient(), url, depth);
    }

    /// <summary>
    /// 发送 PROPFIND 请求并手动跟随 301/302/307/308 重定向。
    /// 标准 HttpClient 默认会跟随重定向，但 OpenList/Alist 重定向后可能丢弃 PROPFIND 方法，
    /// 因此手动处理以保证方法体完整传输。
    /// </summary>
    /// <param name="client">用于发送请求的 HttpClient。</param>
    /// <param name="url">起始 URL。</param>
    /// <param name="depth">Depth 头值。</param>
    /// <param name="maxRedirects">最大重定向次数。</param>
    /// <returns>解析后的 XML 文档。</returns>
    private static async Task<XDocument?> PropFindWithRedirectAsync(HttpClient client, string url, int depth = 1, int maxRedirects = 3)
    {
        var currentUrl = url;
        for (var i = 0; i <= maxRedirects; i++)
        {
            var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
            request.Headers.Add("Depth", depth.ToString());
            request.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");

            var response = await client.SendAsync(request);
            var statusCode = response.StatusCode;

            if ((int)statusCode == 301 || (int)statusCode == 302 || (int)statusCode == 307 || (int)statusCode == 308)
            {
                var location = response.Headers.Location;
                if (location == null)
                    throw new HttpRequestException($"服务器返回重定向 {(int)statusCode} 但缺少 Location 头");

                currentUrl = location.IsAbsoluteUri
                    ? location.ToString()
                    : new Uri(new Uri(currentUrl), location).ToString();

                System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 重定向: {statusCode} -> {currentUrl}");
                continue;
            }

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return XDocument.Parse(content);
        }

        throw new HttpRequestException("重定向次数过多");
    }

    /// <summary>
    /// 测试 WebDAV 服务器连接是否可用
    /// 自动探测 /dav、/webdav 等前缀（适配 OpenList/Alist），支持任意 basePath。
    /// </summary>
    /// <param name="profile">连接配置</param>
    /// <returns>包含是否成功和消息的元组</returns>
    public async Task<(bool Success, string Message)> TestConnectionAsync(ConnectionProfile profile)
    {
        var hostInfo = $"{profile.Host}:{profile.Port}";
        if (profile.UseHttps) hostInfo = "https://" + hostInfo;

        try
        {
            EnsureClient(profile, forceNew: true);
            _davPrefix = ""; // 重置前缀
            var basePath = profile.BasePath?.Trim() ?? "/";
            if (string.IsNullOrEmpty(basePath)) basePath = "/";

            // ── 第1步：通过 API 快速检测是否为 OpenList ──
            bool isApiOpenList = await IsOpenListByApiAsync(profile);

            // ── 第2步：尝试 PROPFIND 多种组合 ──
            // 构建要尝试的 (prefix, path) 组合列表
            var attempts = new List<(string prefix, string path, string desc)>();

            // (a) 无前缀 + 用户路径
            attempts.Add(("", basePath, $"basePath {basePath}"));
            // (b) 已知前缀 + 用户路径（OpenList/Alist：WebDAV 端点在 /dav 下）
            foreach (var p in new[] { "/dav", "/webdav" })
                attempts.Add((p, basePath, $"{p}{basePath}"));
            // (c) 非根路径时，也尝试无前缀根路径
            if (basePath != "/")
                attempts.Add(("", "/", "/"));
            // (d) 非根路径时，尝试前缀 + 根路径
            if (basePath != "/")
                foreach (var p in new[] { "/dav", "/webdav" })
                    attempts.Add((p, "/", $"{p}/"));

            string? workingPrefix = null;
            string? workingUrl = null;

            foreach (var (prefix, path, desc) in attempts)
            {
                var (ok, url) = await TryPropFindWithPrefixAsync(profile, prefix, path, GetClient());
                if (ok)
                {
                    workingPrefix = prefix;
                    workingUrl = url;
                    System.Diagnostics.Debug.WriteLine($"[WebDAV] 测试连接 PROPFIND 成功: {desc} → {url}");
                    break;
                }
                System.Diagnostics.Debug.WriteLine($"[WebDAV] 测试连接 PROPFIND 失败: {desc}");
            }

            if (workingPrefix != null)
            {
                _davPrefix = workingPrefix;
                // 启动后台检测获取完整服务器类型
                _ = Task.Run(async () =>
                {
                    try { CurrentServerType = await DetectServerTypeAsync(profile); }
                    catch { }
                });

                if (!string.IsNullOrEmpty(workingPrefix))
                    return (true, $"连接成功 → {hostInfo}\n检测到 OpenList/Alist，WebDAV 前缀为 {workingPrefix}");
                return (true, $"连接成功 → {hostInfo}");
            }

            // ── 第3步：PROPFIND 全部失败，但 API 检测到 OpenList → 验证 API 可访问 ──
            if (isApiOpenList)
            {
                try
                {
                    _openListToken = null;
                    var apiBaseUrl = BuildApiBaseUrl(profile);
                    var listUrl = $"{apiBaseUrl}/api/fs/list";
                    var openListVirtualPath = ToOpenListPath(basePath);
                    // 确保 _profile 已设置（用于 BuildApiBaseUrl 和 OpenListSendAsync）
                    if (_profile == null) _profile = profile;

                    var body = JsonSerializer.Serialize(new
                    {
                        path = openListVirtualPath,
                        password = "",
                        page = 1,
                        per_page = 1,
                        refresh = false
                    });

                    // 尝试登录
                    var token = await OpenListLoginAsync(profile);
                    if (!string.IsNullOrEmpty(token))
                    {
                        var req = new HttpRequestMessage(HttpMethod.Post, listUrl);
                        req.Headers.Add("Authorization", token);
                        req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                        var resp = await GetOpenListApiClient().SendAsync(req);
                        var json = await resp.Content.ReadAsStringAsync();
                        if (resp.IsSuccessStatusCode)
                        {
                            using var doc = JsonDocument.Parse(json);
                            var code = doc.RootElement.GetProperty("code").GetInt32();
                            if (code == 200)
                            {
                                _davPrefix = "/dav";
                                CurrentServerType = WebDavServerType.OpenList;
                                DetectedServerType = WebDavServerType.OpenList;
                                System.Diagnostics.Debug.WriteLine($"[WebDAV] PROPFIND 不可用但 REST API 正常，使用 API 模式");
                                return (true, $"连接成功 → {hostInfo}\n检测到 OpenList/Alist，将使用 REST API 模式（PROPFIND 不可用）");
                            }
                            var apiMsg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                            System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenList API 返回 code={code}: {apiMsg}");
                            if (code == 403 || apiMsg?.Contains("permission") == true || apiMsg?.Contains("密码") == true)
                                return (false, $"认证失败：{hostInfo}，请检查账号和密码");
                            if (code == 400 || apiMsg?.Contains("not found") == true || apiMsg?.Contains("不存在") == true)
                                return (false, $"路径不存在：{basePath}\nURL: {BuildUrlForProfile(profile, basePath, isDirectory: true)}");
                        }
                    }
                    else
                    {
                        return (false, $"认证失败：{hostInfo}，OpenList 登录不成功，请检查账号和密码");
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenList API 验证失败: {ex.Message}");
                }
            }

            // ── 第4步：区分认证错误（手动跟随重定向以获取最终状态码） ──
            try
            {
                var rootUrl = BuildUrlForProfile(profile, "/", isDirectory: true);
                var currentUrl = rootUrl;
                for (var i = 0; i <= 3; i++)
                {
                    var req = new HttpRequestMessage(new HttpMethod("PROPFIND"), currentUrl);
                    req.Headers.Add("Depth", "0");
                    req.Content = new StringContent(PropFindBody, Encoding.UTF8, "application/xml");
                    var resp = await GetClient().SendAsync(req);
                    var statusCode = (int)resp.StatusCode;

                    // 跟随重定向（域名经反向代理时常见 HTTP→HTTPS、路径规范化等）
                    if (statusCode == 301 || statusCode == 302 || statusCode == 307 || statusCode == 308)
                    {
                        var location = resp.Headers.Location;
                        if (location == null) break;
                        currentUrl = location.IsAbsoluteUri
                            ? location.ToString()
                            : new Uri(new Uri(currentUrl), location).ToString();
                        System.Diagnostics.Debug.WriteLine($"[WebDAV] 第4步重定向: {statusCode} -> {currentUrl}");
                        continue;
                    }

                    if (statusCode == 401 || statusCode == 403)
                    {
                        // 检查 WWW-Authenticate 头以区分 Basic/Digest 认证
                        var authHeader = resp.Headers.WwwAuthenticate;
                        var authSchemes = authHeader?.Select(a => a.Scheme)?.ToList();
                        var schemesText = authSchemes != null && authSchemes.Count > 0
                            ? string.Join(", ", authSchemes)
                            : "未知";

                        // 域名场景下常见反向代理剥离 Authorization 头
                        var isDomain = !System.Net.IPAddress.TryParse(profile.Host, out _);
                        var hint = isDomain
                            ? "\n\n可能原因：\n• 域名经反向代理（Nginx/Caddy）时可能未转发 Authorization 头\n• 请在反代配置中添加：proxy_set_header Authorization $http_authorization;\n• 或检查域名是否指向了正确的 WebDAV 服务端口"
                            : "";

                        return (false, $"认证失败：{hostInfo}（HTTP {statusCode}）\n服务器要求认证方式：{schemesText}\n请检查账号和密码{hint}");
                    }

                    break;
                }
            }
            catch (HttpRequestException aex) when ((int?)aex.StatusCode == 401 || (int?)aex.StatusCode == 403)
            {
                var isDomain = !System.Net.IPAddress.TryParse(profile.Host, out _);
                var hint = isDomain
                    ? "\n\n提示：使用域名时如果密码正确但仍报此错误，可能是反向代理未转发 Authorization 头"
                    : "";
                return (false, $"认证失败：{hostInfo}，请检查账号和密码{hint}");
            }
            catch { }

            return (false, $"连接失败：{hostInfo}\nURL: {BuildUrlForProfile(profile, basePath, isDirectory: true)}\n服务器不响应 PROPFIND，请确认地址、端口和 WebDAV 路径是否正确\n（OpenList/Alist 用户请确保 WebDAV 功能已开启）");
        }
        catch (HttpRequestException ex)
        {
            var msg = ex.Message;
            if ((int?)ex.StatusCode == 401 || (int?)ex.StatusCode == 403)
            {
                var isDomain = !System.Net.IPAddress.TryParse(profile.Host, out _);
                msg = isDomain
                    ? $"认证失败，请检查账号和密码\n\n提示：使用域名时可能是反向代理未转发 Authorization 头"
                    : "认证失败，请检查账号和密码";
            }
            else if ((int?)ex.StatusCode == 404) msg = $"路径不存在 → {hostInfo}";
            else if (ex.Message.Contains("timeout") || ex.Message.Contains("timed out")) msg = $"连接超时：{hostInfo}";
            else if (ex.Message.Contains("refused")) msg = $"连接被拒绝：{hostInfo}";
            return (false, msg);
        }
        catch (TaskCanceledException)
        {
            return (false, $"连接超时：{hostInfo}，请检查地址和端口");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] 测试异常: {ex}");
            return (false, $"{hostInfo}: {ex.Message}");
        }
    }

    /// <summary>
    /// 列出指定路径下的文件和目录
    /// </summary>
    /// <param name="path">WebDAV 目录路径</param>
    /// <returns>远程文件信息列表</returns>
    public async Task<List<RemoteFile>> ListFilesAsync(string path)
    {
        await EnsureDetectedAsync();

        // OpenList: 使用 /api/fs/list 替代 PROPFIND（更快、无 405/深度限制）
        if (CurrentServerType == WebDavServerType.OpenList)
        {
            return await OpenListListFilesAsync(path);
        }

        try
        {
            var url = BuildUrl(path, isDirectory: true);
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles: {url}");
            var doc = await PropFindAsync(url, 1);
            var ns = XNamespace.Get("DAV:");
            var files = new List<RemoteFile>();

            var selfPath = new Uri(url).AbsolutePath.TrimEnd('/');
            if (string.IsNullOrEmpty(selfPath)) selfPath = "/";

            foreach (var resp in doc.Descendants(ns + "response"))
            {
                var href = resp.Element(ns + "href")?.Value ?? "";

                try
                {
                    var hrefPath = (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                    href.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        ? new Uri(href).AbsolutePath.TrimEnd('/')
                        : href.TrimEnd('/');
                    if (hrefPath == selfPath) continue;
                }
                catch { }

                var propstat = resp.Element(ns + "propstat");
                var prop = propstat?.Element(ns + "prop");
                if (prop == null) continue;

                var displayName = prop.Element(ns + "displayname")?.Value ?? "";
                var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
                var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";
                var resType = prop.Element(ns + "resourcetype");

                bool isDir = resType?.Element(ns + "collection") != null;

                var rawName = href.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? href;
                var displayFromHref = Uri.UnescapeDataString(rawName);
                var name = !string.IsNullOrEmpty(displayName) ? displayName : displayFromHref;

                var normalizedPath = NormalizeHrefToPath(href);

                files.Add(new RemoteFile
                {
                    Name = name,
                    Path = normalizedPath,
                    IsDirectory = isDir,
                    Size = long.TryParse(contentLength, out var sz) ? sz : 0,
                    LastModified = DateTimeOffset.TryParse(lastModified, out var dt)
                        ? dt.ToUnixTimeSeconds() : 0
                });
            }

            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles 结果: {files.Count} 个条目 ({path})");
            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListFiles 失败: {ex.Message}");
            return new List<RemoteFile>();
        }
    }

    /// <summary>
    /// 递归列出指定路径下的所有文件（不含目录）。
    /// 优先尝试深度 PROPFIND（depth=infinity）；若服务器不支持（如 OpenList/Alist），
    /// 自动回退到 REST API 递归扫描。
    /// </summary>
    /// <param name="path">起始目录路径。</param>
    /// <param name="serverType">服务器类型，决定使用 PROPFIND 还是 REST API。</param>
    /// <returns>扁平化的文件列表（仅文件，不含目录）。</returns>
    public async Task<List<RemoteFile>> ListAllFilesAsync(string path, WebDavServerType serverType = WebDavServerType.Standard)
    {
        await EnsureDetectedAsync();
        if (serverType == WebDavServerType.Standard) serverType = CurrentServerType;

        // OpenList/Alist: 使用 /api/fs/list 递归扫描（PROPFIND depth>1 被当作 depth=1）
        if (serverType == WebDavServerType.OpenList)
        {
            System.Diagnostics.Debug.WriteLine("[WebDAV] OpenList 模式：使用 /api/fs/list 递归扫描");
            return await OpenListListAllFilesRecursiveAsync(path);
        }

        try
        {
            var url = BuildUrl(path, isDirectory: true);
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListAllFiles (depth=infinity): {url}");
            var doc = await PropFindAsync(url, 899);
            var ns = XNamespace.Get("DAV:");
            var files = new List<RemoteFile>();

            foreach (var resp in doc.Descendants(ns + "response"))
            {
                var href = resp.Element(ns + "href")?.Value ?? "";
                var propstat = resp.Element(ns + "propstat");
                var prop = propstat?.Element(ns + "prop");
                if (prop == null) continue;

                var resType = prop.Element(ns + "resourcetype");
                bool isDir = resType?.Element(ns + "collection") != null;
                if (isDir) continue;

                var displayName = prop.Element(ns + "displayname")?.Value ?? "";
                var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
                var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";

                var rawName = href.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? href;
                var displayFromHref = Uri.UnescapeDataString(rawName);
                var name = !string.IsNullOrEmpty(displayName) ? displayName : displayFromHref;

                var normalizedPath = NormalizeHrefToPath(href);

                files.Add(new RemoteFile
                {
                    Name = name,
                    Path = normalizedPath,
                    IsDirectory = false,
                    Size = long.TryParse(contentLength, out var sz) ? sz : 0,
                    LastModified = DateTimeOffset.TryParse(lastModified, out var dt)
                        ? dt.ToUnixTimeSeconds() : 0
                });
            }

            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListAllFiles 结果: {files.Count} 个文件 ({path})");
            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] ListAllFiles 失败 (将回退到递归扫描): {ex.Message}");

            // 自动检测 OpenList：depth>1 PROPFIND 返回 404/405 是 OpenList 特征
            if (serverType == WebDavServerType.Standard && CurrentServerType == WebDavServerType.Standard)
            {
                var msg = ex.Message;
                if (msg.Contains("404") || msg.Contains("405"))
                {
                    System.Diagnostics.Debug.WriteLine("[WebDAV] depth>1 PROPFIND 返回 404/405 → 自动切换 OpenList 模式");
                    CurrentServerType = WebDavServerType.OpenList;
                    try
                    {
                        return await OpenListListAllFilesRecursiveAsync(path);
                    }
                    catch (Exception apiEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenList 自动检测后扫描失败: {apiEx.Message}");
                        CurrentServerType = WebDavServerType.Standard;
                    }
                }
            }

            return new List<RemoteFile>();
        }
    }

    /// <summary>
    /// 配置并连接到 WebDAV 服务器
    /// </summary>
    /// <param name="profile">连接配置</param>
    public void Configure(ConnectionProfile profile)
    {
        EnsureClient(profile);
        var hostKey = $"{profile.Host}:{profile.Port}";

        // 同一 host 已检测过则跳过（保留已探测的 _davPrefix 和 CurrentServerType）
        if (_lastDetectedHost == hostKey) return;

        _openListToken = null;
        _davPrefix = "";
        CurrentServerType = WebDavServerType.Standard;
        _lastDetectedHost = hostKey;
        _detectionTask = DetectServerTypeAsync(profile);
        _ = _detectionTask.ContinueWith(t =>
        {
            if (t.IsCompletedSuccessfully)
                CurrentServerType = t.Result;
        });
    }

    /// <summary>
    /// 构建带 Basic Auth 认证的完整播放/流 URL（自动包含 /dav 前缀）。
    /// 供扫描、播放等场景使用，返回 ExoPlayer 可直接使用的 http://user:pass@host:port/dav/path 形式。
    /// </summary>
    public string BuildStreamUrl(string path)
    {
        if (_profile == null) throw new InvalidOperationException("WebDAV 未配置连接");

        // 使用 BuildUrl 获取正确的路径（含 /dav 前缀），然后添加 Basic Auth
        var url = BuildUrl(path, isDirectory: false);
        var profile = _profile;

        var authUser = string.IsNullOrEmpty(profile.UserName) ? "" : Uri.EscapeDataString(profile.UserName);
        var authPass = string.IsNullOrEmpty(profile.Password) ? "" : Uri.EscapeDataString(profile.Password);

        if (string.IsNullOrEmpty(authUser)) return url;

        // 在 URL 的 scheme:// 后插入 user:pass@
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0) return url;
        var insertPos = schemeEnd + 3;
        return url[..insertPos] + $"{authUser}:{authPass}@" + url[insertPos..];
    }

    /// <summary>
    /// 当前探测到的 WebDAV 路径前缀（如 "/dav"），公开供外部使用。
    /// </summary>
    public string DavPrefix => _davPrefix;

    /// <summary>
    /// 无认证头的 HttpClient，专用于跟随重定向后访问 CDN（CDN 拒绝带有 Basic Auth 的请求）
    /// </summary>
    private HttpClient? _redirectClient;
    /// <summary>
    /// 获取或创建无 Auth 头的 CDN HttpClient。
    /// </summary>
    /// <returns>无认证头的 HttpClient 实例。</returns>
    private HttpClient GetRedirectClient()
    {
        if (_redirectClient == null)
        {
            _redirectClient = new HttpClient(new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                },
                ConnectTimeout = TimeSpan.FromSeconds(30),
                AllowAutoRedirect = false
            })
            { Timeout = TimeSpan.FromSeconds(30) };
        }
        return _redirectClient;
    }

    /// <summary>
    /// 发送 GET 请求并手动跟随 302/307 重定向（重定向后使用无 Auth 的 HttpClient 访问 CDN）
    /// </summary>
    private async Task<HttpResponseMessage> GetWithRedirectAsync(string url, Action<HttpRequestMessage>? customizeRequest = null)
    {
        var client = GetClient();
        var currentUrl = url;
        const int maxRedirects = 5;

        for (var i = 0; i <= maxRedirects; i++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, currentUrl);

            if (i == 0)
            {
                // 首次请求：应用自定义（Range 等），使用主 HttpClient（带 Auth）
                customizeRequest?.Invoke(request);
                var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var code = (int)response.StatusCode;

                if (code == 301 || code == 302 || code == 307 || code == 308)
                {
                    var location = response.Headers.Location;
                    if (location == null)
                        throw new HttpRequestException($"服务器返回重定向 {code} 但缺少 Location 头");

                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();

                    System.Diagnostics.Debug.WriteLine($"[WebDAV] GET 重定向: {code} -> {currentUrl}");
                    response.Dispose();
                    continue;
                }

                return response;
            }
            else
            {
                // 重定向后：使用无 Auth 头的 HttpClient 访问 CDN
                // （OpenList 的 CDN 收到 Basic Auth 头会返回 400）
                if (i > 1)
                    customizeRequest = null; // 仅首次重定向保留 Range 等自定义头

                var redirectClient = GetRedirectClient();
                var redirectResponse = await redirectClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                var redirectCode = (int)redirectResponse.StatusCode;

                if (redirectCode == 301 || redirectCode == 302 || redirectCode == 307 || redirectCode == 308)
                {
                    var location = redirectResponse.Headers.Location;
                    if (location == null)
                        throw new HttpRequestException($"CDN 返回重定向 {redirectCode} 但缺少 Location 头");

                    currentUrl = location.IsAbsoluteUri
                        ? location.ToString()
                        : new Uri(new Uri(currentUrl), location).ToString();

                    System.Diagnostics.Debug.WriteLine($"[WebDAV] CDN 重定向: {redirectCode} -> {currentUrl}");
                    redirectResponse.Dispose();
                    continue;
                }

                return redirectResponse;
            }
        }

        throw new HttpRequestException("重定向次数过多");
    }

    /// <summary>
    /// 以流的方式读取远程文件内容（支持 302 重定向）
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <returns>包含文件内容的可读流</returns>
    public async Task<Stream> OpenReadAsync(string filePath)
    {
        await EnsureDetectedAsync();
        try
        {
            // OpenList: 通过 REST API 获取 raw_url 直连 CDN，避免 302+Auth→400
            if (CurrentServerType == WebDavServerType.OpenList)
            {
                try
                {
                    return await OpenListDownloadViaRawUrlAsync(filePath);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenList] raw_url 下载失败，回退 WebDAV: {ex.Message}");
                }
            }

            var url = BuildUrl(filePath);
            var response = await GetWithRedirectAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenRead 失败: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// 读取远程文件指定范围的字节数据（支持 302 重定向）
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <param name="offset">起始偏移量</param>
    /// <param name="length">读取长度</param>
    /// <returns>指定范围的字节数组</returns>
    public async Task<byte[]> OpenReadRangeAsync(string filePath, long offset, long length)
    {
        await EnsureDetectedAsync();
        try
        {
            // OpenList: 通过 REST API 获取 raw_url + Range 请求，避免 302+Auth→400
            if (CurrentServerType == WebDavServerType.OpenList)
            {
                try
                {
                    var result = await OpenListDownloadRangeViaRawUrlAsync(filePath, offset, length);
                    if (result.Length > 0) return result;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[OpenList] raw_url Range 失败，回退 WebDAV: {ex.Message}");
                }
            }

            var url = BuildUrl(filePath);
            var response = await GetWithRedirectAsync(url, req =>
            {
                req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(offset, offset + length - 1);
            });
            if (!response.IsSuccessStatusCode) return Array.Empty<byte>();
            return await response.Content.ReadAsByteArrayAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenReadRange 失败: {ex.Message}");
            return Array.Empty<byte>();
        }
    }

    /// <summary>
    /// 获取远程文件的信息
    /// </summary>
    /// <param name="filePath">远程文件路径</param>
    /// <returns>文件信息，失败时返回 null</returns>
    public async Task<RemoteFile?> GetFileInfoAsync(string filePath)
    {
        await EnsureDetectedAsync();

        // OpenList: 使用 /api/fs/get 获取文件信息
        if (CurrentServerType == WebDavServerType.OpenList)
        {
            try
            {
                var openListPath = ToOpenListPath(filePath);
                var baseUrl = BuildApiBaseUrl();
                var url = $"{baseUrl}/api/fs/get";
                var body = JsonSerializer.Serialize(new { path = openListPath, password = "" });
                var response = await OpenListSendAsync(url,
                    new StringContent(body, Encoding.UTF8, "application/json"));
                var json = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.GetProperty("code").GetInt32() == 200)
                    {
                        var data = doc.RootElement.GetProperty("data");
                        var name = data.GetProperty("name").GetString() ?? "";
                        var size = data.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                        var isDir = data.GetProperty("is_dir").GetBoolean();
                        var modified = data.TryGetProperty("modified", out var mod) ? mod.GetString() : "";
                        DateTimeOffset.TryParse(modified, out var dt);
                        return new RemoteFile
                        {
                            Name = name,
                            Path = filePath,
                            IsDirectory = isDir,
                            Size = size,
                            LastModified = dt.ToUnixTimeSeconds()
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[WebDAV] OpenList GetFileInfo 失败: {ex.Message}");
            }
        }

        try
        {
            var url = BuildUrl(filePath);
            var doc = await PropFindAsync(url, 0);
            var ns = XNamespace.Get("DAV:");

            var resp = doc.Descendants(ns + "response").FirstOrDefault();
            var propstat = resp?.Element(ns + "propstat");
            var prop = propstat?.Element(ns + "prop");
            if (prop == null) return null;

            var displayName = prop.Element(ns + "displayname")?.Value ?? "";
            var contentLength = prop.Element(ns + "getcontentlength")?.Value ?? "0";
            var lastModified = prop.Element(ns + "getlastmodified")?.Value ?? "";
            var resType = prop.Element(ns + "resourcetype");

            return new RemoteFile
            {
                Name = !string.IsNullOrEmpty(displayName) ? displayName
                    : filePath.Split('/').LastOrDefault(s => !string.IsNullOrEmpty(s)) ?? filePath,
                Path = filePath,
                IsDirectory = resType?.Element(ns + "collection") != null,
                Size = long.TryParse(contentLength, out var sz) ? sz : 0,
                LastModified = DateTimeOffset.TryParse(lastModified, out var dt)
                    ? dt.ToUnixTimeSeconds() : 0
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] GetFileInfo 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 上传文件到远程路径
    /// </summary>
    /// <param name="remotePath">远程目标路径</param>
    /// <param name="content">文件内容</param>
    /// <param name="contentType">MIME 类型</param>
    /// <returns>包含是否成功和消息的元组</returns>
    public async Task<(bool Success, string Message)> UploadFileAsync(string remotePath, byte[] content, string? contentType = null)
    {
        try
        {
            var url = BuildUrl(remotePath);
            var ct = contentType ?? "application/octet-stream";
            var requestContent = new ByteArrayContent(content);
            requestContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(ct);

            var request = new HttpRequestMessage(HttpMethod.Put, url)
            {
                Content = requestContent
            };

            var response = await GetClient().SendAsync(request);
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 201 || (int)response.StatusCode == 204)
            {
                return (true, "上传成功");
            }

            var errorMsg = $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}";
            return (false, errorMsg);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[WebDAV] PUT 异常: {ex.Message}");
            return (false, $"上传失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  OpenList / Alist REST API 实现
    //  绕过 WebDAV PROPFIND+GET 的 302 重定向问题
    // ════════════════════════════════════════════════════════════

    /// <summary>构建 OpenList API 基础 URL（scheme://host:port），不含 BasePath</summary>
    private string BuildApiBaseUrl(ConnectionProfile? profile = null)
    {
        var p = profile ?? _profile ?? throw new InvalidOperationException("未配置连接");
        var scheme = p.UseHttps ? "https" : "http";
        var host = (p.Host ?? "").TrimEnd('/');
        if (host.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) host = host[7..];
        else if (host.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) host = host[8..];
        var colonIdx = host.LastIndexOf(':');
        if (colonIdx > 0 && int.TryParse(host[(colonIdx + 1)..], out _)) host = host[..colonIdx];
        var port = p.Port;
        return port == 80 || port == 443 ? $"{scheme}://{host}" : $"{scheme}://{host}:{port}";
    }

    /// <summary>获取无 Basic Auth 的 API HttpClient</summary>
    private HttpClient GetOpenListApiClient()
    {
        if (_openListApiClient == null)
        {
            _openListApiClient = new HttpClient(new SocketsHttpHandler
            {
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                },
                ConnectTimeout = TimeSpan.FromSeconds(15),
                AllowAutoRedirect = false
            })
            { Timeout = TimeSpan.FromSeconds(30) };
        }
        return _openListApiClient;
    }

    /// <summary>将 WebDAV 路径转换为 OpenList 虚拟文件系统路径（去除自动探测的 dav 前缀）</summary>
    private string ToOpenListPath(string webDavPath)
    {
        var cleanPath = webDavPath.TrimStart('/');
        // 如果路径以 _davPrefix 开头，先去掉此前缀
        if (!string.IsNullOrEmpty(_davPrefix))
        {
            var prefixTrimmed = _davPrefix.Trim('/');
            if (cleanPath.StartsWith(prefixTrimmed + "/", StringComparison.OrdinalIgnoreCase))
                cleanPath = cleanPath[prefixTrimmed.Length..].TrimStart('/');
            else if (string.Equals(cleanPath, prefixTrimmed, StringComparison.OrdinalIgnoreCase))
                cleanPath = "";
        }
        // 也兼容用户手动在 basePath 中写了 /dav 前缀的情况
        if (cleanPath.StartsWith("dav/", StringComparison.OrdinalIgnoreCase))
            cleanPath = cleanPath[4..].TrimStart('/');
        else if (string.Equals(cleanPath, "dav", StringComparison.OrdinalIgnoreCase))
            cleanPath = "";

        return string.IsNullOrEmpty(cleanPath) ? "/" : "/" + cleanPath;
    }

    /// <summary>登录 OpenList REST API，获取 Bearer token</summary>
    public async Task<string?> OpenListLoginAsync(ConnectionProfile? profile = null)
    {
        var p = profile ?? _profile;
        if (p == null) return null;

        var baseUrl = BuildApiBaseUrl(p);
        var url = $"{baseUrl}/api/auth/login";

        var client = GetOpenListApiClient();
        var body = JsonSerializer.Serialize(new
        {
            username = p.UserName ?? "",
            password = p.Password ?? ""
        });

        try
        {
            var response = await client.PostAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenList] 登录失败: {response.StatusCode} - {json}");
                return null;
            }

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "unknown";
                System.Diagnostics.Debug.WriteLine($"[OpenList] 登录失败: code={code}, {msg}");
                return null;
            }

            var token = doc.RootElement.GetProperty("data").GetProperty("token").GetString();
            _openListToken = token;
            System.Diagnostics.Debug.WriteLine($"[OpenList] 登录成功, token={token?[..Math.Min(20, token?.Length ?? 0)]}...");
            return token;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] 登录异常: {ex.Message}");
            return null;
        }
    }

    /// <summary>发送带自动 token 获取和刷新的 OpenList API 请求</summary>
    private async Task<HttpResponseMessage> OpenListSendAsync(string url, HttpContent? content = null, HttpMethod? method = null)
    {
        var client = GetOpenListApiClient();
        var httpMethod = method ?? HttpMethod.Post;

        // 如果 token 为空，先登录
        if (string.IsNullOrEmpty(_openListToken))
        {
            var token = await OpenListLoginAsync();
            if (string.IsNullOrEmpty(token))
                throw new HttpRequestException("OpenList 认证失败，无法获取 token");
        }

        var request = new HttpRequestMessage(httpMethod, url);
        request.Headers.Add("Authorization", _openListToken);
        if (content != null) request.Content = content;

        var response = await client.SendAsync(request);

        // 如果 token 过期（401），重新登录并重试
        if ((int)response.StatusCode == 401)
        {
            _openListToken = null;
            var token = await OpenListLoginAsync();
            if (string.IsNullOrEmpty(token))
                throw new HttpRequestException("OpenList token 已过期，重新认证失败");

            var retryRequest = new HttpRequestMessage(httpMethod, url);
            retryRequest.Headers.Add("Authorization", _openListToken);
            if (content != null) retryRequest.Content = content;
            return await client.SendAsync(retryRequest);
        }

        return response;
    }

    /// <summary>使用 OpenList /api/fs/list 列出目录</summary>
    public async Task<List<RemoteFile>> OpenListListFilesAsync(string path)
    {
        try
        {
            if (_profile == null) throw new InvalidOperationException("未配置连接");

            var openListPath = ToOpenListPath(path);
            var baseUrl = BuildApiBaseUrl();
            var url = $"{baseUrl}/api/fs/list";

            var body = JsonSerializer.Serialize(new
            {
                path = openListPath,
                password = "",
                page = 1,
                per_page = 0,
                refresh = false
            });

            System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles: {openListPath}");
            var response = await OpenListSendAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 失败: {response.StatusCode}");
                return new List<RemoteFile>();
            }

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200)
            {
                var msg = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : "";
                System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 错误: code={code}, {msg}");
                return new List<RemoteFile>();
            }

            var data = doc.RootElement.GetProperty("data");
            var files = new List<RemoteFile>();

            if (data.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in content.EnumerateArray())
                {
                    var name = item.GetProperty("name").GetString() ?? "";
                    var isDir = item.GetProperty("is_dir").GetBoolean();
                    var size = item.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
                    var modified = item.TryGetProperty("modified", out var m2) ? m2.GetString() ?? "" : "";

                    // openListItemPath 是 OpenList 虚拟路径（从根开始的完整路径），也是 WebDAV 路径（BuildUrl 会自动加 /dav 前缀）
                    var openListItemPath = "/" + name;
                    if (!string.IsNullOrEmpty(openListPath) && openListPath != "/")
                        openListItemPath = openListPath.TrimEnd('/') + "/" + name;

                    files.Add(new RemoteFile
                    {
                        Name = name,
                        Path = openListItemPath,
                        IsDirectory = isDir,
                        Size = size,
                        LastModified = DateTimeOffset.TryParse(modified, out var dt) ? dt.ToUnixTimeSeconds() : 0
                    });
                }
            }

            System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 结果: {files.Count} 个条目 ({path} → {openListPath})");
            return files;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] ListFiles 失败: {ex.Message}");
            return new List<RemoteFile>();
        }
    }

    /// <summary>使用 OpenList API 递归扫描所有文件（并发，限深度）</summary>
    private async Task<List<RemoteFile>> OpenListListAllFilesRecursiveAsync(string basePath)
    {
        var allFiles = new List<RemoteFile>();
        var dirQueue = new Queue<string>();
        dirQueue.Enqueue(basePath);

        const int maxDepth = 20;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (dirQueue.Count > 0)
        {
            var currentDir = dirQueue.Dequeue();
            if (!visited.Add(currentDir.TrimEnd('/'))) continue;

            // 深度保护
            var depth = currentDir.TrimStart('/').Split('/').Length -
                        (basePath.TrimStart('/').Split('/').Length - 1);
            if (depth > maxDepth) continue;

            var files = await OpenListListFilesAsync(currentDir);
            foreach (var file in files)
            {
                if (file.IsDirectory)
                {
                    // 防御性检查：跳过自引用目录（路径与当前目录相同）
                    var dirPath = file.Path.TrimEnd('/');
                    var curPath = currentDir.TrimEnd('/');
                    if (!string.Equals(dirPath, curPath, StringComparison.OrdinalIgnoreCase))
                        dirQueue.Enqueue(file.Path);
                }
                else
                    allFiles.Add(file);
            }

            // 每次扫描间隔 50ms，避免请求过快
            if (dirQueue.Count > 0)
                await Task.Delay(50);
        }

        System.Diagnostics.Debug.WriteLine($"[OpenList] 递归扫描完成: {allFiles.Count} 个文件");
        return allFiles;
    }

    /// <summary>获取 OpenList 文件的 raw_url（直接 CDN 下载链接）</summary>
    public async Task<string?> GetOpenListDownloadUrlAsync(string filePath)
    {
        try
        {
            if (_profile == null) throw new InvalidOperationException("未配置连接");

            var rawPath = filePath;
            if (rawPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { rawPath = Uri.UnescapeDataString(new Uri(rawPath).AbsolutePath); } catch { }
            }
            var openListPath = ToOpenListPath(rawPath);
            var baseUrl = BuildApiBaseUrl();
            var url = $"{baseUrl}/api/fs/get";

            var body = JsonSerializer.Serialize(new { path = openListPath, password = "" });
            var response = await OpenListSendAsync(url,
                new StringContent(body, Encoding.UTF8, "application/json"));
            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(json);
            var code = doc.RootElement.GetProperty("code").GetInt32();
            if (code != 200) return null;

            var rawUrl = doc.RootElement.GetProperty("data").GetProperty("raw_url").GetString();
            System.Diagnostics.Debug.WriteLine($"[OpenList] GetDownloadUrl: {filePath} → {rawUrl?[..Math.Min(80, rawUrl?.Length ?? 0)]}...");
            return rawUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] GetDownloadUrl 失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// 构建 OpenList 播放流 URL（使用 /d/ 端点 + token，绕过 302 重定向链）
    /// ExoPlayer 直接访问 /d/ 端点，Alist 返回 302 到 CDN（URL 无 Auth → CDN 不会 400）
    /// </summary>
    public async Task<string?> GetOpenListStreamUrlAsync(string filePath)
    {
        try
        {
            if (_profile == null) throw new InvalidOperationException("未配置连接");

            // 确保已登录获取 token
            if (string.IsNullOrEmpty(_openListToken))
            {
                var token = await OpenListLoginAsync();
                if (string.IsNullOrEmpty(token)) return null;
            }

            // 输入可能是完整 URL（http://user:pass@host/dav/WEBDAV/file）或纯路径
            var rawPath = filePath;
            if (rawPath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                rawPath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                try { rawPath = Uri.UnescapeDataString(new Uri(rawPath).AbsolutePath); } catch { }
            }
            // /d/ 端点路径（去掉 WebDAV 挂载前缀）
            var openListPath = ToOpenListPath(rawPath);
            var baseUrl = BuildApiBaseUrl();

            // /api/fs/get 使用 OpenList 虚拟路径（和 GetDownloadUrl 一致）
            var getUrl = $"{baseUrl}/api/fs/get";
            var getBody = JsonSerializer.Serialize(new { path = openListPath, password = "" });
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl fs/get path: {openListPath[..Math.Min(80, openListPath.Length)]}");
            var getResponse = await OpenListSendAsync(getUrl,
                new StringContent(getBody, Encoding.UTF8, "application/json"));
            var getJson = await getResponse.Content.ReadAsStringAsync();
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl fs/get response: {(int)getResponse.StatusCode}, body={getJson[..Math.Min(200, getJson.Length)]}");

            string? sign = null;
            string? rawUrl = null;
            if (getResponse.IsSuccessStatusCode)
            {
                using var getDoc = JsonDocument.Parse(getJson);
                var code = getDoc.RootElement.GetProperty("code").GetInt32();
                var msg = getDoc.RootElement.TryGetProperty("message", out var msgElem) ? msgElem.GetString() : "";
                System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl fs/get code={code}, message={msg}");
                if (code == 200 && getDoc.RootElement.TryGetProperty("data", out var getData))
                {
                    if (getData.TryGetProperty("sign", out var signElem) && signElem.ValueKind == JsonValueKind.String)
                        sign = signElem.GetString();
                    if (getData.TryGetProperty("raw_url", out var rawUrlElem) && rawUrlElem.ValueKind == JsonValueKind.String)
                        rawUrl = rawUrlElem.GetString();
                }
            }

            // 构建 /d/ 端点 URL：路径为 OpenList 虚拟路径
            var encodedPath = string.Join("/",
                openListPath.TrimStart('/').Split('/')
                    .Select(s => Uri.EscapeDataString(Uri.UnescapeDataString(s))));

            // 优先使用 sign（Alist /d/ 端点认证方式）
            if (!string.IsNullOrEmpty(sign))
            {
                var url = $"{baseUrl}/d/{encodedPath}?sign={Uri.EscapeDataString(sign)}";
                System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl (sign): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
                return url;
            }

            // sign 不可用：使用 raw_url（直接 CDN 链接，无需认证）
            if (!string.IsNullOrEmpty(rawUrl))
            {
                System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl (raw_url): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
                return rawUrl;
            }

            // 最终回退：/d/ + token
            var fallbackUrl = $"{baseUrl}/d/{encodedPath}?token={_openListToken}";
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl (token fallback): /d/{openListPath[..Math.Min(40, openListPath.Length)]}");
            return fallbackUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OpenList] StreamUrl 构建失败: {ex.Message}");
            return null;
        }
    }

    /// <summary>通过 OpenList raw_url 下载文件（无认证直连 CDN）</summary>
    private async Task<Stream> OpenListDownloadViaRawUrlAsync(string filePath)
    {
        var rawUrl = await GetOpenListDownloadUrlAsync(filePath);
        if (string.IsNullOrEmpty(rawUrl))
            throw new HttpRequestException($"无法获取 OpenList 下载链接: {filePath}");

        using var client = new HttpClient(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AllowAutoRedirect = true
        })
        { Timeout = TimeSpan.FromSeconds(60) };

        var response = await client.GetAsync(rawUrl, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStreamAsync();
    }

    /// <summary>通过 OpenList raw_url 读取文件指定范围（Range 请求）</summary>
    private async Task<byte[]> OpenListDownloadRangeViaRawUrlAsync(string filePath, long offset, long length)
    {
        var rawUrl = await GetOpenListDownloadUrlAsync(filePath);
        if (string.IsNullOrEmpty(rawUrl))
            return Array.Empty<byte>();

        using var client = new HttpClient(new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, _, _, _) => true
            },
            ConnectTimeout = TimeSpan.FromSeconds(15),
            AllowAutoRedirect = true
        })
        { Timeout = TimeSpan.FromSeconds(30) };

        var request = new HttpRequestMessage(HttpMethod.Get, rawUrl);
        request.Headers.Range = new RangeHeaderValue(offset, offset + length - 1);

        var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return Array.Empty<byte>();
        return await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>
    /// 释放 HTTP 客户端资源
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _client = null;
        _redirectClient?.Dispose();
        _redirectClient = null;
        _openListApiClient?.Dispose();
        _openListApiClient = null;
        _openListToken = null;
    }
}

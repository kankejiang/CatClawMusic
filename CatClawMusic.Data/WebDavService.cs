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
/// WebDAV 网络文件服务，提供文件列表、读取、上传和连接测试功能。
/// 服务器检测 / 连接测试 / OpenList REST API 见同目录 WebDavService.*.cs partial 文件；
/// 统一 TLS 证书策略见 <see cref="WebDavCertPolicy"/>。
/// </summary>
public partial class WebDavService : INetworkFileService, IDisposable
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
                // 默认仍接受无效证书以保持局域网/自签 NAS 的可用性，
                // 但证书确实无效时记录明确的中间人风险告警（仅无效时打日志，正常链路零开销）。
                // 后续可接入连接配置中的“忽略证书错误”开关做严格校验。
                RemoteCertificateValidationCallback = WebDavCertPolicy.CreateCertValidationCallback(profile.Host)
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
                // 默认仍接受无效证书以保持局域网/自签 NAS 的可用性，
                // 但证书确实无效时记录明确的中间人风险告警（仅无效时打日志，正常链路零开销）。
                // 后续可接入连接配置中的“忽略证书错误”开关做严格校验。
                RemoteCertificateValidationCallback = WebDavCertPolicy.CreateCertValidationCallback(profile.Host)
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

                Log.Debug("WebDavService", $"[WebDAV] PROPFIND 重定向: {statusCode} -> {currentUrl}");
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
            Log.Debug("WebDavService", $"[WebDAV] ListFiles: {url}");
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

            Log.Debug("WebDavService", $"[WebDAV] ListFiles 结果: {files.Count} 个条目 ({path})");
            return files;
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[WebDAV] ListFiles 失败: {ex.Message}");
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
            Log.Debug("WebDavService", "[WebDAV] OpenList 模式：使用 /api/fs/list 递归扫描");
            return await OpenListListAllFilesRecursiveAsync(path);
        }

        try
        {
            var url = BuildUrl(path, isDirectory: true);
            Log.Debug("WebDavService", $"[WebDAV] ListAllFiles (depth=infinity): {url}");
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

            Log.Debug("WebDavService", $"[WebDAV] ListAllFiles 结果: {files.Count} 个文件 ({path})");
            return files;
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[WebDAV] ListAllFiles 失败 (将回退到递归扫描): {ex.Message}");

            // 自动检测 OpenList：depth>1 PROPFIND 返回 404/405 是 OpenList 特征
            if (serverType == WebDavServerType.Standard && CurrentServerType == WebDavServerType.Standard)
            {
                var msg = ex.Message;
                if (msg.Contains("404") || msg.Contains("405"))
                {
                    Log.Debug("WebDavService", "[WebDAV] depth>1 PROPFIND 返回 404/405 → 自动切换 OpenList 模式");
                    CurrentServerType = WebDavServerType.OpenList;
                    try
                    {
                        return await OpenListListAllFilesRecursiveAsync(path);
                    }
                    catch (Exception apiEx)
                    {
                        Log.Debug("WebDavService", $"[WebDAV] OpenList 自动检测后扫描失败: {apiEx.Message}");
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
                    RemoteCertificateValidationCallback = WebDavCertPolicy.CreateCertValidationCallback("WebDAV")
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

                    Log.Debug("WebDavService", $"[WebDAV] GET 重定向: {code} -> {currentUrl}");
                    response.Dispose();
                    continue;
                }

                return response;
            }
            else
            {
                // 重定向后：使用无 Auth 头的 HttpClient 访问 CDN
                // （OpenList 的 CDN 收到 Basic Auth 头会返回 400）
                // 首次重定向（i==1，即跳到 CDN）保留 Range 等自定义头，使 seek 在 CDN 跳转后仍生效；
                // 后续重定向（i>1）丢弃自定义头，避免多级 CDN 链对 Range 的兼容问题。
                if (i == 1)
                    customizeRequest?.Invoke(request);

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

                    Log.Debug("WebDavService", $"[WebDAV] CDN 重定向: {redirectCode} -> {currentUrl}");
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
                    Log.Debug("WebDavService", $"[OpenList] raw_url 下载失败，回退 WebDAV: {ex.Message}");
                }
            }

            var url = BuildUrl(filePath);
            var response = await GetWithRedirectAsync(url);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStreamAsync();
        }
        catch (Exception ex)
        {
            Log.Debug("WebDavService", $"[WebDAV] OpenRead 失败: {ex.Message}");
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
                    Log.Debug("WebDavService", $"[OpenList] raw_url Range 失败，回退 WebDAV: {ex.Message}");
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
            Log.Debug("WebDavService", $"[WebDAV] OpenReadRange 失败: {ex.Message}");
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
                Log.Debug("WebDavService", $"[WebDAV] OpenList GetFileInfo 失败: {ex.Message}");
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
            Log.Debug("WebDavService", $"[WebDAV] GetFileInfo 失败: {ex.Message}");
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
            Log.Debug("WebDavService", $"[WebDAV] PUT 异常: {ex.Message}");
            return (false, $"上传失败: {ex.Message}");
        }
    }

    // ════════════════════════════════════════════════════════════
    //  OpenList / Alist REST API 实现
    //  绕过 WebDAV PROPFIND+GET 的 302 重定向问题
    // ════════════════════════════════════════════════════════════

    /// <summary>构建 OpenList API 基础 URL（scheme://host:port），不含 BasePath</summary>
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
